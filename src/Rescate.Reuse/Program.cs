using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rescate.Core;
using Rescate.Acquire;

string? reportFolder=null;bool formatStarted=false;int copied=0;
try
{
    if(args.Length!=1)throw new IOException("Se requiere una solicitud confirmada.");
    var path=Safety.LocalPath(args[0]);Safety.RequireInternalDisk(path);
    var plan=JsonSerializer.Deserialize<ReusePlan>(await File.ReadAllTextAsync(path),RecoveryEngine.Json)??throw new IOException("Solicitud no válida.");
    reportFolder=Safety.LocalPath(plan.ReportFolder);Safety.RequireInternalDisk(reportFolder);
    if(!Directory.Exists(reportFolder) || File.Exists(Path.Combine(reportFolder,"reuse-result.json")))throw new IOException("Carpeta no válida o resultado existente.");
    ReusePlanner.ValidateTarget(plan.Target,plan.DriveLetter,plan.Operation);
    if(plan.Confirmation!=ReusePlanner.ConfirmationText(plan.DriveLetter) || DateTime.UtcNow-plan.PreparedUtc>TimeSpan.FromHours(1))throw new IOException("Falta confirmación vigente del borrado.");
    if(Process.GetProcessesByName("RescateUSB.Copia").Length>0 || Process.GetProcesses().Any(p=>p.ProcessName.Contains("recoverit",StringComparison.OrdinalIgnoreCase)))throw new IOException("Hay un diagnóstico, copia o Recoverit activo; borrado bloqueado.");
    var image=Safety.ImagePath(plan.ImagePath);
    if(File.Exists(Path.Combine(Path.GetDirectoryName(image)!,"reuse-hold.json")))throw new IOException("Hay una alerta de consistencia pendiente. No se borrará la memoria.");
    using var backup=File.Open(image,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(backup)),plan.ImageSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("La copia de respaldo no coincide.");
    var diagnostic=JsonSerializer.Deserialize<ReadDiagnosticResult>(await File.ReadAllTextAsync(Safety.LocalPath(plan.DiagnosticPath)),RecoveryEngine.Json)!;
    if(!diagnostic.Complete || diagnostic.Error is not null || diagnostic.Sha256!=plan.ImageSha256 || DateTime.UtcNow-diagnostic.DateUtc>TimeSpan.FromHours(24))throw new IOException("Diagnóstico insuficiente o antiguo.");
    foreach(var file in plan.Files)
    {
        ReusePlanner.CheckedRelativePath(file.RelativePath);Safety.RequireInternalDisk(file.Source);
        if(new FileInfo(file.Source).Length!=file.Length || !string.Equals(await RecoveryEngine.HashFile(file.Source,default),file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("Respaldo de archivo no coincide.");
    }
    if(plan.Files.Count==0 || plan.Files.Sum(f=>f.Length)+128L*1024*1024>plan.Target.Size)throw new IOException("Selección vacía o demasiado grande.");
    var device=(await UsbInventory.List(default)).SingleOrDefault(d=>d.Number==plan.Target.Number);
    if(device is null || device.IsBoot || device.IsSystem || device.UniqueId!=plan.Target.UniqueId || device.Size!=plan.Target.Size || device.FriendlyName!=plan.Target.FriendlyName)throw new IOException("Cambió la memoria seleccionada.");
    // Independent read-only identity samples matter when USB serials are generic.
    using(var source=new PhysicalSource(device.Number,device.Size,device.FriendlyName))
    {
        var a=new byte[ImageAcquirer.BlockSize];var b=new byte[a.Length];
        foreach(long offset in new[]{0L,device.Size/3/4096*4096,device.Size*2/3/4096*4096,device.Size-a.Length})
        {
            if(await source.Read(offset,a,default)!=a.Length)throw new IOException("Muestra de USB incompleta.");
            backup.Position=offset;await backup.ReadExactlyAsync(b);
            if(!a.AsSpan().SequenceEqual(b))throw new IOException("La memoria conectada no coincide con el respaldo. Borrado bloqueado.");
        }
    }
    var script=Path.Combine(AppContext.BaseDirectory,"format-selected-volume.ps1");
    if(!File.Exists(script))throw new IOException("No se encontró el formateador controlado.");
    await ImageAcquirer.AtomicJson(Path.Combine(reportFolder,"reuse-progress.json"),new{Status="Preparando el formateo confirmado",Copied=0,Total=plan.Files.Count});
    var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
    {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
    foreach(var argument in new[]{"-NoProfile","-NonInteractive","-File",script,"-RequestPath",path})start.ArgumentList.Add(argument);
    formatStarted=true;
    using var process=Process.Start(start)??throw new IOException("No se inició el formateador.");
    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if(process.ExitCode!=0)throw new IOException("El formateador no terminó correctamente: "+await stderr);
    using var formatted=JsonDocument.Parse(await stdout);
    var volumePath=formatted.RootElement.GetProperty("VolumePath").GetString()!;
    if(!Regex.IsMatch(volumePath,@"^\\\\\?\\Volume\{[0-9a-fA-F-]{36}\}\\$"))throw new IOException("Ruta de volumen no válida; no se copian archivos.");
    // Address the volume GUID, not a drive letter that could be reassigned.
    var destination=volumePath+"Recuperados_"+Guid.NewGuid().ToString("N");
    var manifest=new List<object>();
    if(plan.Operation=="FormatAndCopy")
    {
        Directory.CreateDirectory(destination);
        foreach(var file in plan.Files)
        {
            var output=destination+"\\"+ReusePlanner.CheckedRelativePath(file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using(var source=File.Open(file.Source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.WriteThrough))
            {await source.CopyToAsync(target);target.Flush(true);}
            using var verify=File.OpenRead(output);
            var hash=Convert.ToHexString(await SHA256.HashDataAsync(verify));
            if(!hash.Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("La lectura de comprobación no coincide; recopia detenida.");
            copied++;manifest.Add(new{file.RelativePath,file.Length,Sha256=hash});
            await ImageAcquirer.AtomicJson(Path.Combine(reportFolder,"reuse-progress.json"),new{Status="Copiando y comprobando",Copied=copied,Total=plan.Files.Count});
        }
    }
    await ImageAcquirer.AtomicJson(Path.Combine(reportFolder,"reuse-result.json"),new{Status="Finalizado",FormatStarted=formatStarted,Copied=copied,Destination=destination,Files=manifest,
        Warning="La comprobación de los archivos copiados no prueba toda la capacidad ni la retención a largo plazo. Los respaldos permanecen en la computadora."});
}
catch(Exception ex)
{
    if(reportFolder is not null && Directory.Exists(reportFolder))await ImageAcquirer.AtomicJson(Path.Combine(reportFolder,"reuse-result.json"),new{Status="Error",FormatStarted=formatStarted,Copied=copied,Error=ex.Message,Warning="Si el formateo comenzó, no se revierte. Los respaldos de la computadora se conservan."});
    Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;
}
