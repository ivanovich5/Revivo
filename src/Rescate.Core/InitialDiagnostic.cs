namespace Rescate.Core;

public sealed record InitialDiagnostic(string SourceIdentity, DateTime DateUtc, long TotalBytes,
    long ReadBytes, long FailedBytes, long PendingBytes, bool CopyComplete, int TimedBlocks,
    int SlowBlocks, double? MaximumReadMilliseconds, string Status, string? ErrorStage, string? Error)
{
    public static InitialDiagnostic From(AcquisitionMap map)
    {
        var timed = map.Ranges.Where(r => r.ReadMilliseconds.HasValue).ToArray();
        return new(map.SourceIdentity, map.UpdatedUtc, map.Length, map.ReadBytes, map.FailedBytes,
            map.PendingBytes, map.Complete, timed.Length, timed.Count(r => r.ReadMilliseconds > 2000),
            timed.Length == 0 ? null : timed.Max(r => r.ReadMilliseconds),
            map.Complete ? "Lectura y copia completas" : map.FailedBytes > 0 ? "Lectura con errores; copia incompleta" : "Prueba incompleta",
            map.ErrorStage, map.Error);
    }

    public string Summary => $"DIAGNÓSTICO INICIAL · durante la copia · {DateUtc.ToLocalTime():g}\n" +
        $"{Status}\nLeído y guardado: {ReadBytes / 1e9:N2} / {TotalBytes / 1e9:N2} GB\n" +
        $"Bytes ilegibles: {FailedBytes:N0} · pendientes de copiar: {PendingBytes:N0}\n" +
        (TimedBlocks == 0 ? "Esta copia antigua no registró tiempos de lectura.\n" :
            $"Bloques medidos: {TimedBlocks:N0} · mayores de 2 segundos: {SlowBlocks:N0}\nMáxima demora de lectura: {MaximumReadMilliseconds / 1000:N2} s\n") +
        (Error is null ? "" : $"Incidencia: {Error}\n") +
        "\nEvidencia del origen de esta copia, no de la USB seleccionada ahora. No se volvió a leer la memoria. " +
        "La lentitud por sí sola no demuestra daño físico. Escritura, capacidad real y retención: sin comprobar. " +
        "Una copia completa no certifica que todos los archivos estén íntegros.";
}
