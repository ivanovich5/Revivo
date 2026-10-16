using Rescate.Core;
using Rescate.Acquire;
using System.Diagnostics;
using System.Text.Json;

// Usage: RescateUSB.Copia.exe <request.json>. Only this small helper is elevated.
string? folder = null;
try
{
    if(args.Length!=1) throw new ArgumentException("Se requiere una solicitud de copia JSON.");
    var requestPath=Safety.LocalPath(args[0]); Safety.RequireInternalDisk(requestPath);
    var request=JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(requestPath),RecoveryEngine.Json) ?? throw new IOException("Solicitud inválida.");
    folder=Safety.LocalPath(request.Destination); Safety.RequireInternalDisk(folder);
    if(Process.GetProcesses().Any(p=>p.ProcessName.Contains("recoverit",StringComparison.OrdinalIgnoreCase)))
        throw new IOException("Recoverit está abierto. Ciérralo antes de copiar la memoria.");
    Directory.CreateDirectory(folder);
    using var cancel = new CancellationTokenSource();
    Console.CancelKeyPress += (_,e)=> { e.Cancel=true; cancel.Cancel(); };
    using var stopWatcher = new Timer(_=> { if(File.Exists(Path.Combine(folder,"STOP"))) cancel.Cancel(); },null,500,500);
    if(request.Mode is "Diagnostic" or "DiagnosticSamples")
    {
        if(File.Exists(Path.Combine(folder,"diagnostic-result.json"))) throw new IOException("Ya existe un diagnóstico; no se sobrescribe.");
        var disk=(await UsbInventory.List(cancel.Token)).SingleOrDefault(d=>d.Number==request.DiskNumber);
        if(disk is null || disk.IsBoot || disk.IsSystem || disk.Size!=request.ExpectedBytes || disk.UniqueId!=request.ExpectedUniqueId || disk.FriendlyName!=request.ExpectedProduct)
            throw new IOException("El dispositivo cambió o es de arranque/sistema. Diagnóstico bloqueado.");
        using var diagnosticSource=new PhysicalSource(request.DiskNumber,request.ExpectedBytes,request.ExpectedProduct,request.Mode=="DiagnosticSamples");
        if(request.Mode=="DiagnosticSamples")
        {
            var reference=Safety.ImagePath(request.ReferenceImage??throw new IOException("Falta la imagen de referencia."));
            using var image=File.Open(reference,FileMode.Open,FileAccess.Read,FileShare.Read);
            var samples=await StabilityProbe.Run(diagnosticSource,image,Path.Combine(folder,"samples"),cancel.Token);
            await ImageAcquirer.AtomicJson(Path.Combine(folder,"samples-result.json"),new{Complete=true,diagnosticSource.Identity,Access="GENERIC_READ | FILE_FLAG_NO_BUFFERING",ComparedSamples=samples.Count,DifferentFromReference=samples.Count(s=>!s.MatchesReference),DifferentBetweenReads=samples.Count(s=>!s.RepeatMatches),Samples=samples,DateUtc=DateTime.UtcNow,Limit="Muestreo acotado; no certifica sectores no muestreados ni descarta caché interna del dispositivo."});
            return;
        }
        var progress=new SynchronousProgress(p=>ImageAcquirer.AtomicJson(Path.Combine(folder,"diagnostic-progress.json"),p).GetAwaiter().GetResult());
        var result=await ReadDiagnostic.Run(diagnosticSource,progress,cancel.Token);
        await ImageAcquirer.AtomicJson(Path.Combine(folder,"diagnostic-result.json"),result);
        Environment.ExitCode=result.Complete?0:2;
        return;
    }
    if(request.Mode!="Acquire") throw new IOException("Modo no admitido.");
    var expected=new UsbDisk(request.DiskNumber,request.ExpectedProduct,"",request.ExpectedUniqueId??"",request.ExpectedBytes,"","",false,false);
    AcquisitionSelection.Validate(expected,(await UsbInventory.List(cancel.Token)).SingleOrDefault(d=>d.Number==request.DiskNumber));
    using var source = new PhysicalSource(request.DiskNumber,request.ExpectedBytes,request.ExpectedProduct);
    await ImageAcquirer.AtomicJson(Path.Combine(folder,"source.json"),new {source.Identity,source.Length,source.SectorSize,Access="GENERIC_READ",DateUtc=DateTime.UtcNow});
    var map=await new ImageAcquirer().Create(source,folder,false,null,cancel.Token);
    await ImageAcquirer.AtomicJson(Path.Combine(folder,"completion.json"),new {map.Status,map.Complete,map.ReadBytes,map.FailedBytes,map.PendingBytes,map.ImageSha256,map.Error,map.ErrorStage,map.ErrorOffset,map.ErrorCode});
    Environment.ExitCode=map.Complete?0:2;
}
catch(Exception ex)
{
    Console.Error.WriteLine(ex);
    if(folder is not null && Directory.Exists(folder))
        await ImageAcquirer.AtomicJson(Path.Combine(folder,"completion.json"),new {Status="Error",Complete=false,Error=ex.Message,Details=ex.ToString()});
    Environment.ExitCode=1;
}
internal sealed record Request(int DiskNumber,long ExpectedBytes,string ExpectedProduct,string Destination,string Mode="Acquire",string? ExpectedUniqueId=null,string? ReferenceImage=null);
internal sealed class SynchronousProgress(Action<AcquisitionProgress> report) : IProgress<AcquisitionProgress>
{ public void Report(AcquisitionProgress value)=>report(value); }
