using System.Text.Json;

namespace Rescate.Core;

public static class Comparison
{
    public static async Task<(ComparisonSummary Summary, string Report)> Compare(string rescateFolder,
        string otherFolder, string outputParent, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var leftPath = Safety.LocalPath(rescateFolder);
        var rightPath = Safety.LocalPath(otherFolder);
        Safety.RequireLocalDisk(leftPath); Safety.RequireLocalDisk(rightPath);
        if (leftPath.TrimEnd('\\').Equals(rightPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Selecciona dos carpetas distintas.");
        var left = await Inventory(leftPath, progress, token);
        var right = await Inventory(rightPath, progress, token);
        var leftHashes = left.Select(x => x.Sha256).ToHashSet();
        var rightHashes = right.Select(x => x.Sha256).ToHashSet();
        var common = leftHashes.Intersect(rightHashes).LongCount();
        var summary = new ComparisonSummary(left.Count, right.Count, leftHashes.Count, rightHashes.Count, common,
            leftHashes.Count - common, rightHashes.Count - common, left.Count - leftHashes.Count, right.Count - rightHashes.Count,
            "Coincidencia exacta de bytes por SHA-256, independiente del nombre. No demuestra integridad ni cuenta archivos que solo aparecen en la vista previa de Recoverit. No detecta equivalencia entre un archivo completo y uno con relleno o truncado.");
        var output = Safety.CreateOutput(outputParent, "comparacion");
        var report = Path.Combine(output, "comparacion.json");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Summary = summary, Rescate = left, Recoverit = right }, RecoveryEngine.Json), token);
        await File.WriteAllTextAsync(Path.Combine(output, "LEER.txt"),
            $"Comparación por contenido\n\nRescate USB: {left.Count} archivos; {leftHashes.Count} contenidos únicos.\n" +
            $"Otra herramienta: {right.Count} archivos; {rightHashes.Count} contenidos únicos.\nCoinciden: {common}.\n" +
            $"Solo Rescate USB: {summary.RescateOnlyUnique}. Solo otra herramienta: {summary.OtherOnlyUnique}.\n\n{summary.Note}\n", token);
        return (summary, report);
    }

    private sealed record InventoryFile(string RelativePath, long Bytes, string Sha256);
    private static async Task<List<InventoryFile>> Inventory(string root, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var result = new List<InventoryFile>();
        // A Rescate export report gives an exact file list, so real recovered files called
        // informe.json elsewhere are not globally excluded by their name.
        IEnumerable<string> files = RecoveryEngine.SafeFiles(root);
        var report = Path.Combine(root, "informe.json");
        if (File.Exists(report))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(report, token));
            if (document.RootElement.TryGetProperty("ImageSha256", out _) && document.RootElement.TryGetProperty("Files", out var array))
                files = array.EnumerateArray().Select(x => x.GetProperty("OutputName").GetString()!).Select(name =>
                {
                    if (Path.GetFileName(name) != name) throw new IOException("Informe de exportación no válido.");
                    return Path.Combine(root, name);
                }).ToArray();
        }
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            _ = Safety.LocalPath(file);
            progress?.Report(new($"Comparando contenido · {result.Count + 1:N0} archivos…"));
            result.Add(new(Path.GetRelativePath(root, file), new FileInfo(file).Length, await RecoveryEngine.HashFile(file, token)));
        }
        return result;
    }
}
