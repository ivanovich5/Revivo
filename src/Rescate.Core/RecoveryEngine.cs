using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rescate.Core;

public sealed class RecoveryEngine(string engineRoot)
{
    private readonly ToolRunner tools = new(engineRoot);
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const int CatalogLimit = 200_000;

    public async Task<(RecoverySession Session, string Directory)> Scan(string imagePath, string outputParent,
        bool signatures, int sectorSize, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        if (sectorSize is not (512 or 4096)) throw new ArgumentException("Tamaño de sector no admitido.");
        var image = Safety.ImagePath(imagePath);
        using var guard = File.Open(image, FileMode.Open, FileAccess.Read, FileShare.Read);
        var dir = Safety.CreateOutput(outputParent, "sesion");
        progress?.Report(new("Identificando la imagen (SHA-256)…"));
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(guard, token));
        Safety.CheckAcquisition(image, hash);
        var identity = new ImageIdentity(image, guard.Length, File.GetLastWriteTimeUtc(image), hash);
        var session = new RecoverySession("0.1.0", identity, DateTime.UtcNow,
            signatures ? "Firmas / PhotoRec 7.2" : "Metadatos / TSK 4.15.0", "En curso", [], [], []);
        await Save(session, dir);
        try
        {
            if (signatures) await Carve(session, dir, progress, token);
            else await Metadata(session, dir, sectorSize, progress, token);
            session = session with { Status = session.Warnings.Count == 0 ? "Finalizado" : "Finalizado con avisos" };
        }
        catch (OperationCanceledException)
        {
            session = session with { Status = "Cancelado: resultados incompletos" };
            session.Warnings.Add("El análisis se canceló. No es un escaneo completo. Puede iniciarse otra sesión.");
        }
        catch (Exception ex)
        {
            session = session with { Status = "Error: resultados incompletos" };
            session.Warnings.Add(ex.Message);
        }
        await Save(session, dir);
        progress?.Report(new($"{session.Status} · {session.Files.Count:N0} entradas", 100));
        return (session, dir);
    }

    private async Task Metadata(RecoverySession session, string dir, int sectorSize,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        progress?.Report(new("Buscando particiones y volúmenes…"));
        var partitions = await tools.Run("mmls.exe", ["-i", "raw", "-b", sectorSize.ToString(), "-a", session.Image.Path], dir, token);
        await File.WriteAllTextAsync(Path.Combine(dir, "particiones.txt"), partitions.Output + partitions.Error, token);
        foreach (var line in partitions.Output.Split('\n'))
        {
            var match = Regex.Match(line, @"^\s*\d+:\s+\S+\s+(\d+)\s+(\d+)\s+(\d+)\s+(.+)$");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var offset) && offset < session.Image.Length / sectorSize)
                session.Volumes.Add(new(offset, sectorSize, match.Groups[4].Value.Trim()));
        }
        if (session.Volumes.Count == 0) session.Volumes.Add(new(0, sectorSize, "Volumen sin tabla / sector inicial"));
        foreach (var volume in session.Volumes)
        {
            progress?.Report(new($"Leyendo carpetas · sector {volume.SectorOffset:N0}…"));
            var countBefore = session.Files.Count;
            var result = await tools.Run("fls.exe", ["-i", "raw", "-b", volume.SectorSize.ToString(),
                "-o", volume.SectorOffset.ToString(), "-r", "-F", "-m", "/", session.Image.Path], dir, token,
                lineOutput: line =>
                {
                    var entry = ParseBodyLine(line, volume, session.Files.Count + 1);
                    if (entry is not null && session.Files.Count < CatalogLimit) session.Files.Add(entry);
                });
            if (result.ExitCode != 0) session.Warnings.Add($"Volumen {volume.SectorOffset}: {result.Error.Trim()}");
            await File.WriteAllTextAsync(Path.Combine(dir, $"volumen-{volume.SectorOffset}.txt"), result.Output + result.Error, token);
            progress?.Report(new($"{session.Files.Count:N0} entradas localizadas; aún sin verificar su contenido."));
            if (session.Files.Count == countBefore && result.ExitCode == 0)
                session.Warnings.Add($"No se encontraron entradas exportables en el volumen {volume.SectorOffset}.");
        }
        if (session.Files.Count >= CatalogLimit) session.Warnings.Add($"Se alcanzó el límite de {CatalogLimit:N0} entradas; catálogo incompleto.");
        if (session.Files.Count == 0) session.Warnings.Add("No se pudieron reconstruir archivos. Prueba el análisis por firmas; no necesita una tabla de particiones legible.");
    }

    public static FoundFile? ParseBodyLine(string line, VolumeCandidate volume, int id)
    {
        var parts = line.Split('|');
        if (parts.Length < 11) return null;
        // Parse fixed fields from the end so a pipe inside the filename is preserved.
        var tail = parts.Length - 9;
        var inode = parts[tail];
        var mode = parts[tail + 1];
        if (!Regex.IsMatch(inode, @"^\d+(?:-\d+){0,2}$") || mode.StartsWith('d') || mode.StartsWith('v') || mode.StartsWith('V')) return null;
        if (!long.TryParse(parts[tail + 4], out var size) || size < 0) return null;
        var name = string.Join('|', parts.Skip(1).Take(tail - 1));
        if (name.Contains("/$OrphanFiles/") && name.EndsWith("/$OrphanFiles")) return null;
        var deleted = name.EndsWith(" (deleted)", StringComparison.Ordinal);
        if (deleted) name = name[..^10];
        return new(id.ToString("D7"), name, size, "Metadatos",
            deleted ? "Eliminado · sin verificar" : "Encontrado · sin verificar", volume.SectorOffset, volume.SectorSize, inode);
    }

    private async Task Carve(RecoverySession session, string dir, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        progress?.Report(new("PhotoRec está recuperando por firmas. Los archivos temporales se guardan en la sesión…"));
        var destination = Path.Combine(dir, "carved");
        var result = await tools.Run("photorec_win.exe", ["/log", "/d", destination, "/cmd", session.Image.Path,
            "partition_none,search"], dir, token);
        await File.WriteAllTextAsync(Path.Combine(dir, "motor-photorec.txt"), result.Output + result.Error, token);
        if (result.ExitCode != 0) session.Warnings.Add($"PhotoRec terminó con código {result.ExitCode}. Revisa su registro.");
        progress?.Report(new("Catalogando archivos recuperados…"));
        foreach (var folder in Directory.EnumerateDirectories(dir, "carved.*"))
            foreach (var path in SafeFiles(folder))
            {
                token.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).Equals("report.xml", StringComparison.OrdinalIgnoreCase)) continue;
                if (session.Files.Count >= CatalogLimit) { session.Warnings.Add("Límite del catálogo alcanzado; hay más archivos en la sesión."); return; }
                var info = new FileInfo(path);
                session.Files.Add(new((session.Files.Count + 1).ToString("D7"), info.Name, info.Length, "Firmas",
                    "Extraído · sin verificar", CachedPath: Path.GetRelativePath(dir, path)));
            }
    }

    public async Task<string> Export(RecoverySession session, string sessionDir, IEnumerable<FoundFile> selected,
        string destination, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var entries = selected.ToList();
        if (entries.Count == 0) throw new IOException("Selecciona al menos un archivo.");
        var image = Safety.ImagePath(session.Image.Path);
        using var guard = File.Open(image, FileMode.Open, FileAccess.Read, FileShare.Read);
        progress?.Report(new("Comprobando que la imagen no ha cambiado…"));
        if (guard.Length != session.Image.Length || Convert.ToHexString(await SHA256.HashDataAsync(guard, token)) != session.Image.Sha256)
            throw new IOException("La imagen cambió desde el análisis. Exportación bloqueada.");
        var dir = Safety.CreateOutput(destination, "recuperados");
        var reports = new List<ExportedFile>();
        try
        {
            for (int index = 0; index < entries.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var entry = entries[index];
                ValidateEntry(entry, session, sessionDir);
                var name = $"{index + 1:D7}_{Safety.SafeName(entry.Name)}";
                var path = Path.Combine(dir, name);
                string? error = null;
                try
                {
                    if (entry.CachedPath is not null && !File.Exists(CachePath(sessionDir, entry.CachedPath)))
                        throw new IOException("El archivo recuperado ya no está disponible en la sesión. Consulta el antivirus y el registro; no se creará una copia vacía.");
                    await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    if (entry.CachedPath is not null)
                    {
                        await using var input = File.OpenRead(CachePath(sessionDir, entry.CachedPath));
                        await input.CopyToAsync(output, token);
                    }
                    else
                    {
                        var result = await tools.Run("icat.exe", ["-i", "raw", "-b", entry.SectorSize.ToString(), "-o",
                            entry.SectorOffset.ToString(), "-r", image, entry.Inode], sessionDir, token, output);
                        if (result.ExitCode != 0) error = result.Error.Trim();
                    }
                }
                catch (OperationCanceledException)
                {
                    reports.Add(new(entry.Id, entry.Name, name, File.Exists(path) ? new FileInfo(path).Length : 0,
                        "", "Interrumpido · parcial", entry.Method, "Exportación cancelada"));
                    throw;
                }
                catch (Exception ex) { error = ex.Message; }
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                var hash = File.Exists(path) ? await HashFile(path, token) : "";
                var state = error is not null || bytes != entry.Size ? "Parcial / error" : "Exportado · contenido sin verificar";
                reports.Add(new(entry.Id, entry.Name, name, bytes, hash, state, entry.Method, error));
                progress?.Report(new($"Exportando {index + 1:N0} de {entries.Count:N0}", (index + 1) * 100 / entries.Count));
            }
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "informe.json"), JsonSerializer.Serialize(new
            { ImageSha256 = session.Image.Sha256, Requested = entries.Count, Reported = reports.Count, Files = reports }, Json));
            await WriteCsv(Path.Combine(dir, "informe.csv"), reports);
        }
        return dir;
    }

    public async Task<string> Preview(RecoverySession session, string sessionDir, FoundFile entry, CancellationToken token)
    {
        ValidateEntry(entry, session, sessionDir);
        var heading = $"{entry.Name}\n{entry.SizeLabel}\n{entry.State}\nMétodo: {entry.Method}\n\n";
        if (entry.CachedPath is null)
            return heading + "Vista de contenido disponible tras exportar. Esta versión evita ejecutar decodificadores de documentos o imágenes recuperadas.";
        var buffer = new byte[1024];
        using var stream = File.OpenRead(CachePath(sessionDir, entry.CachedPath));
        int size = await stream.ReadAsync(buffer, token);
        var hex = string.Join('\n', buffer.AsSpan(0, size).ToArray().Chunk(16).Select(chunk => Convert.ToHexString(chunk)));
        return heading + "Primeros bytes (vista segura, sin ejecutar contenido):\n\n" + hex;
    }

    private static void ValidateEntry(FoundFile entry, RecoverySession session, string sessionDir)
    {
        if (!Regex.IsMatch(entry.Id, @"^\d{1,12}$") || entry.Size < 0 || entry.SectorOffset < 0 ||
            entry.SectorSize is not (512 or 4096) || entry.SectorOffset >= session.Image.Length / entry.SectorSize)
            throw new IOException("Entrada de sesión no válida.");
        if (entry.CachedPath is null && !Regex.IsMatch(entry.Inode, @"^\d+(?:-\d+){0,2}$")) throw new IOException("Referencia de archivo no válida.");
        if (entry.CachedPath is not null) _ = CachePath(sessionDir, entry.CachedPath);
    }

    private static string CachePath(string dir, string relative)
    {
        var root = Safety.LocalPath(dir) + Path.DirectorySeparatorChar;
        var full = Safety.LocalPath(Path.Combine(dir, relative));
        if (Path.IsPathRooted(relative) || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetRelativePath(dir, full).StartsWith("carved.", StringComparison.OrdinalIgnoreCase))
            throw new IOException("La sesión contiene una ruta de caché no válida.");
        return full;
    }

    public static async Task<RecoverySession> Load(string file)
    {
        var path = Safety.LocalPath(file);
        Safety.RequireInternalDisk(path);
        if (new FileInfo(path).Length > 128 * 1024 * 1024) throw new IOException("Sesión demasiado grande.");
        var session = JsonSerializer.Deserialize<RecoverySession>(await File.ReadAllTextAsync(path), Json) ?? throw new IOException("Sesión no válida.");
        _ = Safety.ImagePath(session.Image.Path);
        for(int index=0;index<session.Files.Count;index++)
        {
            var entry=session.Files[index];
            ValidateEntry(entry, session, Path.GetDirectoryName(path)!);
            if(entry.CachedPath is not null && !File.Exists(CachePath(Path.GetDirectoryName(path)!,entry.CachedPath)))
                session.Files[index]=entry with {State="No disponible en la sesión"};
        }
        return session;
    }

    public static async Task Save(RecoverySession session, string dir)
    {
        var path = Path.Combine(dir, "session.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(session, Json));
        File.Move(path + ".tmp", path, true);
    }

    public static async Task<string> HashFile(string path, CancellationToken token)
    { using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, token)); }

    public static IEnumerable<string> SafeFiles(string dir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Se encontró un enlace; lectura detenida.");
            if ((attributes & FileAttributes.Directory) != 0)
            { foreach (var file in SafeFiles(entry)) yield return file; }
            else yield return entry;
        }
    }

    private static string Csv(string s) => "\"" + ((s.Length > 0 && "=+-@\t\r".Contains(s[0])) ? "'" + s : s).Replace("\"", "\"\"") + "\"";
    private static Task WriteCsv(string path, IEnumerable<ExportedFile> files)
    {
        var lines = new[] { "Id,NombreOriginal,ArchivoExportado,Bytes,SHA256,Estado,Metodo,Error" }
            .Concat(files.Select(f => string.Join(',', new[] { f.Id, f.OriginalName, f.OutputName,
                f.Bytes.ToString(CultureInfo.InvariantCulture), f.Sha256, f.State, f.Method, f.Error ?? "" }.Select(Csv))));
        return File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true));
    }
}
