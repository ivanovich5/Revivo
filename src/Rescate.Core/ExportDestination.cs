using System.Text.Json;
namespace Rescate.Core;

public static class ExportDestination
{
    public static void ValidateFiles(Safety.StorageVolume volume,IReadOnlyList<long> sizes)
    {
        if(volume.BusType is not (1 or 3 or 7 or 8 or 10 or 11 or 17))throw new IOException("Destino desconocido o virtual: operación bloqueada.");
        bool fat=string.Equals(volume.Format,"FAT32",StringComparison.OrdinalIgnoreCase);
        if(!fat && !string.Equals(volume.Format,"NTFS",StringComparison.OrdinalIgnoreCase) && !string.Equals(volume.Format,"exFAT",StringComparison.OrdinalIgnoreCase))throw new IOException("Para guardar archivos usa NTFS, exFAT o FAT32.");
        if(sizes.Any(x=>x<0))throw new IOException("Tamaño de archivo inválido.");
        if(fat && sizes.Any(x=>x>uint.MaxValue))throw new IOException("Uno de los archivos supera el límite de FAT32 (4 GB menos 1 byte). Elige otra unidad NTFS/exFAT o excluye ese archivo. No hace falta repetir el análisis ni formatear esta memoria.");
        long required=checked(sizes.Sum()+16L*1024*1024);
        if(volume.FreeBytes<required)throw new IOException($"No hay espacio suficiente: se necesitan {required/1e9:N2} GB para la selección y su informe.");
    }
    public static void ValidateOriginal(UsbDisk target,long originalSize,string originalSerial,string originalUniqueId)
    {
        if(originalSize<=0)throw new IOException("No se pudo identificar la memoria original; guarda la selección en el disco interno.");
        if(!string.IsNullOrWhiteSpace(originalUniqueId) && string.Equals(target.UniqueId,originalUniqueId,StringComparison.OrdinalIgnoreCase))throw new IOException("Esta es la memoria original. Elige otra unidad para no sobrescribir datos recuperables.");
        var serial=originalSerial.Trim();var other=target.SerialNumber.Trim();
        bool useful=serial.Length>=4 && serial.Any(c=>c!='0' && c!=' ');
        if(useful && string.Equals(serial,other,StringComparison.OrdinalIgnoreCase))throw new IOException("El destino tiene el mismo número de serie que la memoria original.");
        if(target.Size==originalSize && (!useful || string.IsNullOrWhiteSpace(other)))throw new IOException("Las memorias tienen igual capacidad y no se distinguen con seguridad por su número de serie. Guarda en el disco interno u otro dispositivo identificable.");
    }
    public static async Task Check(string destination,string reviewFolder,IReadOnlyList<long> sizes)
    {
        var volume=Safety.InspectStorage(destination);ValidateFiles(volume,sizes);
        if(volume.BusType!=7 && !volume.Removable)return;
        var current=Safety.LocalPath(reviewFolder);long length=0;string serial="",unique="";
        while(Path.GetDirectoryName(current) is not null)
        {
            var source=Safety.LocalPath(Path.Combine(current,"source.json"));
            if(File.Exists(source))
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(source));
                length=doc.RootElement.GetProperty("Length").GetInt64();
                var identity=doc.RootElement.GetProperty("Identity").GetString()?.Split('|');
                if(identity is {Length:7} && identity[1]=="USB")serial=identity[4];
                var request=Safety.LocalPath(Path.Combine(current,"request.json"));
                if(File.Exists(request)){using var req=JsonDocument.Parse(File.ReadAllText(request));if(req.RootElement.TryGetProperty("ExpectedUniqueId",out var id))unique=id.GetString()??"";}
                break;
            }
            current=Path.GetDirectoryName(current)!;
        }
        var target=(await UsbInventory.List(default)).SingleOrDefault(x=>x.Number==volume.DiskNumber) ?? throw new IOException("El destino USB cambió o no se pudo identificar.");
        ValidateOriginal(target,length,serial,unique);
        if(Safety.InspectStorage(destination)!=volume)throw new IOException("El destino cambió durante la comprobación. Vuelve a elegirlo.");
    }
}
