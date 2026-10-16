using System.Diagnostics;
using System.Security.Cryptography;

namespace Rescate.Core;

public sealed record ReadDiagnosticResult(string Identity, long TotalBytes, long ReadBytes,
    bool Complete, string Status, string Recommendation, string? Sha256, long? ErrorOffset,
    string? Error, double Seconds, int SlowBlocks, DateTime DateUtc);

public static class ReadDiagnostic
{
    public static async Task<ReadDiagnosticResult> Run(IReadOnlySource source,
        IProgress<AcquisitionProgress>? progress, CancellationToken token)
    {
        if(source.Length<=0 || source.SectorSize is not (512 or 4096) || source.Length%source.SectorSize!=0)
            throw new IOException("Geometría no válida para diagnóstico.");
        var buffer=new byte[ImageAcquirer.BlockSize];
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var elapsed=Stopwatch.StartNew(); var last=TimeSpan.Zero;
        long read=0; int slow=0; string? error=null; string status="Lectura completa sin errores reportados";
        try
        {
            while(read<source.Length)
            {
                token.ThrowIfCancellationRequested();
                int requested=(int)Math.Min(buffer.Length,source.Length-read);
                var block=Stopwatch.StartNew();
                int count=await source.Read(read,buffer.AsMemory(0,requested),token);
                if(count!=requested) throw new IOException("Lectura incompleta. La prueba se detuvo sin reintentar.");
                if(block.Elapsed.TotalSeconds>2) slow++;
                hash.AppendData(buffer,0,count);read+=count;
                if(elapsed.Elapsed-last>TimeSpan.FromSeconds(1) || read==source.Length)
                {
                    try {progress?.Report(new(read,0,source.Length-read,source.Length,"Leyendo sin escribir",read/1048576d/Math.Max(.001,elapsed.Elapsed.TotalSeconds),DateTime.UtcNow));}
                    catch(Exception ex) {throw new InvalidOperationException("No se pudo guardar el progreso del diagnóstico. Esto no indica un fallo de la USB.",ex);}
                    last=elapsed.Elapsed;
                }
            }
        }
        catch(OperationCanceledException) {status="Cancelado: diagnóstico incompleto";}
        catch(Exception ex) when(ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {status="Detenido por fallo de lectura o comunicación";error=ex.Message;}
        bool complete=read==source.Length;
        string recommendation=error is not null
            ? "No recomendable para datos importantes: se observó un fallo. Puede corresponder a la memoria, el puerto, el cable o el controlador. Conservar la copia; no insistir con reintentos."
            : complete
                ? "Lectura accesible en esta prueba. Fiabilidad no certificada: no comprueba escritura, capacidad real ni retención. Con antecedentes de RAW, no usar como única copia de datos importantes."
                : "Sin conclusión: la prueba no terminó. No se declara que la memoria esté sana.";
        return new(source.Identity,source.Length,read,complete,status,recommendation,
            complete?Convert.ToHexString(hash.GetHashAndReset()):null,error is null?null:read,error,
            elapsed.Elapsed.TotalSeconds,slow,DateTime.UtcNow);
    }
}
