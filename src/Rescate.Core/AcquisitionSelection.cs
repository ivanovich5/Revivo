namespace Rescate.Core;

public static class AcquisitionSelection
{
    public static void Validate(UsbDisk expected,UsbDisk? current)
    {
        if(expected.IsBoot || expected.IsSystem || expected.Size<=0 || string.IsNullOrWhiteSpace(expected.UniqueId))
            throw new IOException("Memoria no identificada con seguridad o disco protegido.");
        if(current is null || current.IsBoot || current.IsSystem || current.Number!=expected.Number || current.Size!=expected.Size || current.UniqueId!=expected.UniqueId || current.FriendlyName!=expected.FriendlyName)
            throw new IOException("La memoria cambió desde la selección. Actualiza la lista y vuelve a elegirla.");
    }
    public static long RequiredSpace(long bytes)=>checked(bytes*3+1024L*1024*1024);
}
