using System.Security.Cryptography;
using System.Text.Json;

namespace Rescate.Core;

public sealed record ReuseFile(string Source,string RelativePath,long Length,string Sha256);
public sealed record ReusePlan(string Id,string Operation,UsbDisk Target,string DriveLetter,string ImagePath,
    string ImageSha256,string DiagnosticPath,string ReportFolder,List<ReuseFile> Files,DateTime PreparedUtc,
    string Confirmation="");

public static class ReusePlanner
{
    public static void ValidateTarget(UsbDisk target,string letter,string operation)
    {
        if(target.Number<0 || target.Size<=0 || target.IsBoot || target.IsSystem || string.IsNullOrWhiteSpace(target.UniqueId))
            throw new IOException("No se admite una unidad de arranque, sistema o sin identificar.");
        if(letter.Length!=1 || letter[0]<'D' || letter[0]>'Z')throw new IOException("Selecciona una letra de USB entre D y Z.");
        if(operation is not ("FormatOnly" or "FormatAndCopy"))throw new IOException("Operación de reutilización no admitida.");
    }
    public static string ConfirmationText(string letter)=>$"BORRAR {letter}:";
    public static string CheckedRelativePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0') || path.Replace('\\','/').Split('/').Any(x=>x is "" or "." or ".."))
            throw new IOException("Ruta de recopia no válida.");
        return path;
    }
    public static async Task<ReusePlan> Prepare(string caseFolder,string reviewFolder,UsbDisk target,string letter,string operation,CancellationToken token)
    {
        ValidateTarget(target,letter,operation);
        if(System.Diagnostics.Process.GetProcessesByName("RescateUSB.Copia").Length>0)
            throw new IOException("Hay una adquisición o un diagnóstico activo. Espera a que termine antes de preparar el borrado.");
        if(await UsbInventory.DiskNumberForLetter(letter,token)!=target.Number)throw new IOException("La letra no corresponde a la USB seleccionada.");
        caseFolder=Safety.LocalPath(caseFolder);reviewFolder=Safety.LocalPath(reviewFolder);
        Safety.RequireInternalDisk(caseFolder);Safety.RequireInternalDisk(reviewFolder);
        if(File.Exists(Path.Combine(caseFolder,"reuse-hold.json")))throw new IOException("Hay una alerta de consistencia pendiente en este caso. Debe resolverse antes de reutilizar la memoria.");
        var image=Safety.ImagePath(Path.Combine(caseFolder,"usb.img"));
        var map=JsonSerializer.Deserialize<AcquisitionMap>(await File.ReadAllTextAsync(image+".map.json",token),RecoveryEngine.Json)!;
        var candidates=Directory.EnumerateDirectories(caseFolder,"diagnostico-*")
            .Select(d=>Path.Combine(d,"diagnostic-result.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        if(candidates.Length==0)throw new IOException("Falta un diagnóstico completo de la USB. No se permite preparar su formateo.");
        string diagnostic=candidates[0];
        var result=JsonSerializer.Deserialize<ReadDiagnosticResult>(await File.ReadAllTextAsync(diagnostic,token),RecoveryEngine.Json)!;
        if(!result.Complete || result.Error is not null || result.TotalBytes!=target.Size || result.Sha256!=map.ImageSha256 || DateTime.UtcNow-result.DateUtc>TimeSpan.FromHours(24))
            throw new IOException("El último diagnóstico no es reciente, completo y coincidente con la copia. Reutilización bloqueada.");
        var currentHash=await RecoveryEngine.HashFile(image,token);
        if(!string.Equals(currentHash,map.ImageSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("La imagen guardada cambió. No se permite borrar la USB.");
        var files=new List<ReuseFile>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var catalog=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(reviewFolder,"informes","por-tipo.json"),token));
        foreach(var entry in catalog.RootElement.GetProperty("files").EnumerateArray())
        {
            if(entry.GetProperty("status").GetString()!="01_Comprobacion_superada")continue;
            var source=Safety.LocalPath(entry.GetProperty("path").GetString()!);
            if(!source.StartsWith(reviewFolder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Archivo fuera de la carpeta revisada.");
            var sha=entry.GetProperty("sha256").GetString()!;
            if(!seen.Add(sha))continue;
            if(!string.Equals(await RecoveryEngine.HashFile(source,token),sha,StringComparison.OrdinalIgnoreCase))throw new IOException("Cambió un archivo revisado; no se prepara el borrado.");
            var category=Safety.SafeName(entry.GetProperty("group").GetString()!);
            var relative=CheckedRelativePath(Path.Combine(category,Safety.SafeName(Path.GetFileName(source))));
            files.Add(new(source,relative,new FileInfo(source).Length,sha));
        }
        if(files.Count==0)throw new IOException("No hay archivos comprobados para respaldar la operación.");
        if(files.Sum(f=>f.Length)+128L*1024*1024>target.Size)throw new IOException("Los archivos no caben con el margen reservado.");
        var folder=Safety.CreateOutput(caseFolder,"reutilizacion");
        var plan=new ReusePlan(Guid.NewGuid().ToString("N"),operation,target,letter,image,currentHash,diagnostic,folder,files,DateTime.UtcNow);
        await ImageAcquirer.AtomicJson(Path.Combine(folder,"plan.json"),plan);
        return plan;
    }
}
