using System.Security.Cryptography;
using System.Text.Json;

namespace Rescate.Core;

// The source contract deliberately has no Write, SetLength, or repair operation.
public interface IReadOnlySource : IDisposable
{
    long Length { get; }
    int SectorSize { get; }
    string Identity { get; }
    bool CanResume { get; }
    ValueTask<int> Read(long offset, Memory<byte> buffer, CancellationToken token);
}

public sealed record ImageRange(long Offset, int Length, string State, string? Sha256 = null, string? Error = null, double? ReadMilliseconds = null);
public sealed class AcquisitionMap
{
    public int Version { get; set; } = 1;
    public string SourceIdentity { get; set; } = "";
    public long Length { get; set; }
    public int SectorSize { get; set; }
    public string Status { get; set; } = "Pendiente";
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ImageRange> Ranges { get; set; } = [];
    public string? ImageSha256 { get; set; }
    public string? Error { get; set; }
    public string? ErrorStage { get; set; }
    public long? ErrorOffset { get; set; }
    public int? ErrorCode { get; set; }
    public long ReadBytes => Ranges.Where(r => r.State == "Leido").Sum(r => (long)r.Length);
    public long FailedBytes => Ranges.Where(r => r.State == "Ilegible").Sum(r => (long)r.Length);
    public long PendingBytes => Length - ReadBytes - FailedBytes;
    public bool Complete => ReadBytes == Length && ImageSha256 is not null && Status == "Completa";
}

public sealed record AcquisitionProgress(long ReadBytes, long FailedBytes, long PendingBytes, long Total,
    string Status, double MebibytesPerSecond, DateTime UpdatedUtc);

public sealed class ImageAcquirer
{
    public const int BlockSize = 1024 * 1024;
    public static async Task<string> Verify(string imagePath, CancellationToken token)
    {
        var path=Safety.ImagePath(imagePath);
        var map=JsonSerializer.Deserialize<AcquisitionMap>(await File.ReadAllTextAsync(path+".map.json",token),RecoveryEngine.Json)
            ?? throw new IOException("Mapa de adquisición no válido.");
        using var source=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var whole=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[BlockSize];
        long cursor=0;
        foreach(var range in map.Ranges)
        {
            if(range.State!="Leido" || range.Offset!=cursor || range.Length<=0 || range.Length>BlockSize)
                throw new IOException("Mapa incompleto o fuera de orden.");
            await source.ReadExactlyAsync(buffer.AsMemory(0,range.Length),token);
            if(Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0,range.Length)))!=range.Sha256)
                throw new IOException($"La copia no coincide con los bytes leídos del origen en el bloque {range.Offset}.");
            whole.AppendData(buffer,0,range.Length);
            cursor+=range.Length;
        }
        var hash=Convert.ToHexString(whole.GetHashAndReset());
        if(cursor!=source.Length || hash!=map.ImageSha256) throw new IOException("La imagen completa no coincide con su registro.");
        return hash;
    }

    public async Task<AcquisitionMap> Create(IReadOnlySource source, string folder, bool resume,
        IProgress<AcquisitionProgress>? progress, CancellationToken token)
    {
        folder = Safety.LocalPath(folder);
        Safety.RequireInternalDisk(folder);
        if (source.Length <= 0 || source.SectorSize is not (512 or 4096) || source.Length % source.SectorSize != 0)
            throw new IOException("Geometría de origen no válida.");
        Directory.CreateDirectory(folder);
        var imagePath = Path.Combine(folder, "usb.img");
        var mapPath = imagePath + ".map.json";
        AcquisitionMap map;
        if (resume)
        {
            if (!source.CanResume) throw new IOException("Este dispositivo no tiene identidad suficiente para reanudar de forma automática.");
            map = JsonSerializer.Deserialize<AcquisitionMap>(await File.ReadAllTextAsync(mapPath, token), RecoveryEngine.Json) ?? throw new IOException("Mapa no válido.");
            ValidateMap(map, source);
        }
        else
        {
            if (File.Exists(imagePath) || File.Exists(mapPath)) throw new IOException("La carpeta ya contiene una imagen; no se sobrescribirá.");
            map = new() { SourceIdentity = source.Identity, Length = source.Length, SectorSize = source.SectorSize };
            for (long offset = 0; offset < source.Length; offset += BlockSize)
                map.Ranges.Add(new(offset, (int)Math.Min(BlockSize, source.Length - offset), "Pendiente"));
        }
        var free = new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace;
        if (!resume && free < source.Length + 256L * 1024 * 1024) throw new IOException("No hay espacio suficiente para la imagen y su registro.");
        if (!resume) await AtomicJson(mapPath, map);
        await using var output = new FileStream(imagePath, resume ? FileMode.Open : FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.Read, BlockSize, FileOptions.Asynchronous);
        if (resume && output.Length != source.Length) throw new IOException("Longitud de imagen incorrecta al reanudar.");
        if (!resume) output.SetLength(source.Length);
        var buffer = new byte[BlockSize];
        if (resume)
        {
            foreach (var range in map.Ranges.Where(r => r.State == "Leido"))
            {
                output.Position = range.Offset;
                await output.ReadExactlyAsync(buffer.AsMemory(0, range.Length), token);
                if (Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, range.Length))) != range.Sha256)
                    throw new IOException("Un bloque de la imagen cambió; reanudación bloqueada.");
            }
        }
        map.Status = "Copiando";
        map.Error = null;
        await Checkpoint(map, mapPath, output);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var checkpointClock = System.Diagnostics.Stopwatch.StartNew();
        long initialRead = map.ReadBytes;
        int consecutiveErrors = 0;
        string stage="Inicio";
        long? currentOffset=null;
        try
        {
            for (int index = 0; index < map.Ranges.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var range = map.Ranges[index];
                // Resume only pending blocks; failed blocks are never repeatedly hammered.
                if (range.State != "Pendiente") continue;
                int count;
                var readClock = System.Diagnostics.Stopwatch.StartNew();
                stage="Lectura de la memoria USB";currentOffset=range.Offset;
                try
                {
                    count = await source.Read(range.Offset, buffer.AsMemory(0, range.Length), token);
                    if (count != range.Length) throw new EndOfStreamException($"Lectura corta: {count}/{range.Length} bytes.");
                }
                catch (IOException ex) when (IsReadError(ex))
                {
                    map.Ranges[index] = range with { State = "Ilegible", Error = ex.Message, ReadMilliseconds = readClock.Elapsed.TotalMilliseconds };
                    if (++consecutiveErrors >= 8) throw new IOException("Ocho bloques consecutivos no se pudieron leer; se detuvo para evitar insistir sobre la memoria.", ex);
                    continue;
                }
                readClock.Stop();
                consecutiveErrors = 0;
                // Write errors are outside the source-read catch: never mistake disk-full
                // on the destination for unreadable USB sectors.
                output.Position = range.Offset;
                stage="Escritura de la copia en la computadora";
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                map.Ranges[index] = range with { State = "Leido", ReadMilliseconds = readClock.Elapsed.TotalMilliseconds, Sha256 = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, count))) };
                if (checkpointClock.ElapsedMilliseconds >= 1500 || index == map.Ranges.Count - 1)
                {
                    stage="Guardado del registro de copia";
                    await Checkpoint(map, mapPath, output);
                    var status = new AcquisitionProgress(map.ReadBytes, map.FailedBytes, map.PendingBytes, map.Length,
                        map.Status, (map.ReadBytes - initialRead) / 1048576.0 / Math.Max(.001, clock.Elapsed.TotalSeconds), DateTime.UtcNow);
                    progress?.Report(status);
                    await AtomicJson(Path.Combine(folder, "progress.json"), status);
                    checkpointClock.Restart();
                }
            }
            map.Status = map.FailedBytes == 0 ? "Verificando copia local" : "Incompleta";
            stage="Verificación de la copia local";currentOffset=null;
            await Checkpoint(map, mapPath, output);
            output.Position = 0;
            map.ImageSha256 = Convert.ToHexString(await SHA256.HashDataAsync(output, token));
            map.Status = map.FailedBytes == 0 ? "Completa" : "Incompleta";
        }
        catch (OperationCanceledException) { map.Status = "Cancelada"; map.Error = "Cancelada; los bloques pendientes no contienen datos verificados."; }
        catch (Exception ex)
        {
            map.Status="Detenida";map.ErrorStage=stage;map.ErrorOffset=currentOffset;map.ErrorCode=ex.HResult;
            map.Error=ex is UnauthorizedAccessException || (ex.HResult & 0xFFFF)==5
                ?$"Windows denegó el acceso durante: {stage}. Se conservó lo copiado. Esto no demuestra por sí solo daño físico de la memoria."
                :$"{stage}: {ex.Message}";
        }
        finally
        {
            await Checkpoint(map, mapPath, output);
            await AtomicJson(Path.Combine(folder, "diagnostico-inicial.json"), InitialDiagnostic.From(map));
            await AtomicJson(Path.Combine(folder, "progress.json"), new AcquisitionProgress(map.ReadBytes, map.FailedBytes,
                map.PendingBytes, map.Length, map.Status, (map.ReadBytes - initialRead) / 1048576.0 / Math.Max(.001, clock.Elapsed.TotalSeconds), DateTime.UtcNow));
        }
        return map;
    }

    // Media read faults and deliberate synthetic short reads can be skipped. A
    // disconnected device, timeout, changed media, access error or unknown failure stops.
    private static bool IsReadError(IOException ex) => ex is EndOfStreamException || (ex.HResult & 0xFFFF) is 23 or 27 or 1117;

    private static void ValidateMap(AcquisitionMap map, IReadOnlySource source)
    {
        if (map.Version != 1 || map.SourceIdentity != source.Identity || map.Length != source.Length || map.SectorSize != source.SectorSize)
            throw new IOException("La identidad de origen no coincide con la sesión.");
        long cursor = 0;
        foreach (var range in map.Ranges)
        {
            if (range.Offset != cursor || range.Length <= 0 || range.Length > BlockSize || range.Length % map.SectorSize != 0 ||
                range.State is not ("Pendiente" or "Leido" or "Ilegible")) throw new IOException("Mapa de rangos no válido.");
            cursor = checked(cursor + range.Length);
        }
        if (cursor != map.Length) throw new IOException("El mapa no cubre el dispositivo completo.");
    }

    private static async Task Checkpoint(AcquisitionMap map, string path, FileStream image)
    {
        await image.FlushAsync();
        image.Flush(true); // Make data durable before declaring ranges successfully read.
        map.UpdatedUtc = DateTime.UtcNow;
        await AtomicJson(path, map);
    }

    public static async Task AtomicJson<T>(string path, T value)
    {
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, RecoveryEngine.Json);
            await stream.FlushAsync(); stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
}
