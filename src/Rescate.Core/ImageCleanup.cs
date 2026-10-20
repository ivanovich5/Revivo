using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace Rescate.Core;

public sealed record ImageCleanupPlan(string Image,long Bytes,long ModifiedTicks,string MapHash,string SourceHash);
public static class ImageCleanup
{
    public static ImageCleanupPlan? Find(string reviewFolder)
    {
        var current=Safety.LocalPath(reviewFolder);
        while(Path.GetDirectoryName(current) is not null)
        {
            if(File.Exists(Path.Combine(current,"usb.img.map.json")))return Inspect(current);
            current=Path.GetDirectoryName(current)!;
        }
        return null;
    }
    public static ImageCleanupPlan Inspect(string folder)
    {
        var image=Safety.LocalPath(Path.Combine(folder,"usb.img"));
        Safety.RequireLocalDisk(image);
        var mapPath=Safety.LocalPath(image+".map.json");
        var sourcePath=Safety.LocalPath(Path.Combine(folder,"source.json"));
        var mapBytes=File.ReadAllBytes(mapPath);var sourceBytes=File.ReadAllBytes(sourcePath);
        var map=JsonSerializer.Deserialize<AcquisitionMap>(mapBytes,RecoveryEngine.Json) ?? throw new IOException("Mapa inválido.");
        using var source=JsonDocument.Parse(sourceBytes);
        if(!map.Complete || !map.SourceIdentity.StartsWith("PhysicalDrive",StringComparison.Ordinal) || source.RootElement.GetProperty("Identity").GetString()!=map.SourceIdentity)
            throw new IOException("No se identificó una copia completa creada por REVIVO-USB.");
        var info=new FileInfo(image);
        if(!info.Exists || info.Length!=map.Length)throw new IOException("La imagen falta o cambió de tamaño.");
        return new(image,info.Length,info.LastWriteTimeUtc.Ticks,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mapBytes)),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes)));
    }
    // Called only after an explicit confirmation. Delete exactly the opened file, never its folder.
    public static void DeleteConfirmed(ImageCleanupPlan approved)
    {
        var folder=Path.GetDirectoryName(Safety.LocalPath(approved.Image))!;
        if(Inspect(folder)!=approved)throw new IOException("La copia cambió desde la confirmación. No se eliminó.");
        using var handle=CreateFile(approved.Image,0x00010000|0x80000000,0,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid)throw new IOException("La imagen está en uso o no se permite eliminarla.");
        if(Inspect(folder)!=approved)throw new IOException("La copia cambió. No se eliminó.");
        var disposition=new byte[]{1};
        if(!SetFileInformationByHandle(handle,4,disposition,1))throw new IOException("Windows no pudo eliminar la imagen.");
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateFileW",SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle,int kind,byte[] data,uint size);
}
