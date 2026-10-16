namespace Rescate.Desktop;
internal sealed class CopyTimeEstimate
{
    private double start=-1,lastChange;
    private long first,last;
    public string Update(long read,long total,long failed,double seconds)
    {
        if(read>=total && total>0)return "Copia leída; falta finalizar y buscar archivos.";
        if(read<=0)return "Calculando tiempo de copia…";
        if(start<0){start=lastChange=seconds;first=last=read;}
        if(read!=last){last=read;lastChange=seconds;}
        if(failed>0 || seconds-lastChange>=15)return "Lectura irregular: tiempo de copia no disponible; puede tardar más.";
        var elapsed=seconds-start;
        if(elapsed<15 || read<=first)return "Calculando tiempo de copia con la velocidad de lectura…";
        var remaining=(total-read)*elapsed/(read-first);
        var minutes=Math.Max(1,Math.Ceiling(remaining/60));
        return $"Copia: quedan aproximadamente {minutes:N0} min. Después faltan la búsqueda y las vistas; el tiempo puede cambiar.";
    }
}
