using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace Rescate.Core;

public static class Safety
{
    public static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\") || path.StartsWith("//") || path.Contains('\0'))
            throw new IOException("Solo se admiten archivos locales; no dispositivos, rutas de red ni rutas especiales.");
        var full = Path.GetFullPath(path);
        if (full.Length < 3 || full[1] != ':' || full[3..].Contains(':'))
            throw new IOException("Ruta local no válida.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("No se admiten enlaces ni carpetas redirigidas en esta versión.");
        return full;
    }

    public static string ImagePath(string path)
    {
        var full = LocalPath(path);
        if (!new[] { ".img", ".dd", ".raw" }.Contains(Path.GetExtension(full).ToLowerInvariant()))
            throw new IOException("Selecciona una imagen sin comprimir .img, .dd o .raw.");
        if (!File.Exists(full) || new FileInfo(full).Length < 512)
            throw new IOException("La imagen no existe o tiene menos de 512 bytes.");
        RequireLocalDisk(full);
        CheckAcquisition(full);
        return full;
    }

    public static void CheckAcquisition(string image, string? hash = null)
    {
        var sidecar = image + ".map.json";
        if (!File.Exists(sidecar)) return;
        var map = System.Text.Json.JsonSerializer.Deserialize<AcquisitionMap>(File.ReadAllText(sidecar), RecoveryEngine.Json)
            ?? throw new IOException("Mapa de adquisición no válido.");
        if (!map.Complete || map.Length != new FileInfo(image).Length)
            throw new IOException($"La imagen está {map.Status.ToLowerInvariant()}: {map.FailedBytes:N0} bytes ilegibles y {map.PendingBytes:N0} pendientes. Este analizador aún no interpreta huecos; no se tratarán como datos reales.");
        if (hash is not null && map.ImageSha256 != hash) throw new IOException("La imagen no coincide con el hash de adquisición.");
    }

    // Query-only volume/device inspection. Unknown and multi-disk volumes fail closed.
    public sealed record StorageVolume(uint DiskNumber, int BusType, bool Removable, string Format, long FreeBytes);
    public static StorageVolume InspectStorage(string path)
    {
        var full=LocalPath(path);
        var existing=full;
        while(!File.Exists(existing) && !Directory.Exists(existing))
            existing=Path.GetDirectoryName(existing) ?? throw new IOException("No se encontró el volumen.");
        var volume=new StringBuilder(1024);
        if(!GetVolumePathName(existing,volume,volume.Capacity))throw new IOException("No se pudo identificar el volumen.");
        var root=volume.ToString();
        if(root.Length!=3)throw new IOException("No se admiten volúmenes montados en carpetas.");
        var drive=new DriveInfo(root);
        if(!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))throw new IOException("Selecciona un disco local disponible.");
        using var handle=CreateFile(@"\\.\"+root[..2],0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        var data=new byte[1024];
        if(handle.IsInvalid || !DeviceIoControl(handle,0x00560000,null,0,data,data.Length,out var count,IntPtr.Zero) || count<32 || BitConverter.ToInt32(data,0)!=1)
            throw new IOException("Volumen compuesto o no identificable: operación bloqueada.");
        var number=BitConverter.ToUInt32(data,8);
        using var disk=CreateFile(@"\\.\PhysicalDrive"+number,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        var query=new byte[12];
        if(disk.IsInvalid || !DeviceIoControl(disk,0x002D1400,query,query.Length,data,data.Length,out count,IntPtr.Zero) || count<32)
            throw new IOException("No se pudo comprobar la conexión del disco.");
        return new(number,BitConverter.ToInt32(data,28),data[10]!=0,drive.DriveFormat,drive.AvailableFreeSpace);
    }
    public static void ValidateStorage(StorageVolume volume,int? sourceDisk=null,long imageBytes=0)
    {
        if(volume.BusType is not (1 or 3 or 7 or 8 or 10 or 11 or 17))throw new IOException("Conexión de disco desconocida o virtual: operación bloqueada.");
        if(sourceDisk is not null && volume.DiskNumber==sourceDisk.Value)throw new IOException("El destino pertenece a la misma memoria de origen. Elige otro dispositivo físico, no otra partición.");
        if(!string.Equals(volume.Format,"NTFS",StringComparison.OrdinalIgnoreCase) && !string.Equals(volume.Format,"exFAT",StringComparison.OrdinalIgnoreCase))
            throw new IOException("El destino debe usar NTFS o exFAT. FAT32 no admite imágenes grandes. REVIVO-USB no formateará el destino.");
        if(imageBytes>0 && volume.FreeBytes<checked(imageBytes+256L*1024*1024))throw new IOException("No hay espacio suficiente para la imagen y su registro.");
    }
    public static void RequireLocalDisk(string path,int? sourceDisk=null,long imageBytes=0)=>ValidateStorage(InspectStorage(path),sourceDisk,imageBytes);
    public static void RequireInternalDisk(string path)
    {
        var volume=InspectStorage(path);
        ValidateStorage(volume);
        if(volume.Removable || volume.BusType==7)throw new IOException("Esta operación requiere un disco interno.");
    }

    public static string CreateOutput(string parent, string prefix)
    {
        var full = LocalPath(parent);
        RequireLocalDisk(full);
        Directory.CreateDirectory(full);
        var label=prefix switch { "recuperados"=>"Archivos", "sesion"=>"Analisis", _=>prefix };
        var suffix=Guid.NewGuid().ToString("N")[..8];
        var child = Path.Combine(full, prefix=="USB"?$"USB-{DateTime.Now:yyMMdd-HHmm}-{suffix}":$"{label}-{suffix}");
        Directory.CreateDirectory(child);
        return child;
    }

    public static string SafeName(string original)
    {
        var name = original.Replace('\\', '/').Split('/').LastOrDefault() ?? "archivo";
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (name.Length > 120) name = name[..100] + Path.GetExtension(name)[..Math.Min(16, Path.GetExtension(name).Length)];
        return string.IsNullOrWhiteSpace(name) ? "archivo" : name;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNameW")]
    private static extern bool GetVolumePathName(string file, StringBuilder volume, int size);
}
