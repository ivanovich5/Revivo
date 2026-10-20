using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Rescate.Core;

public sealed record StructureReport(string Status, string OriginalImage, string WorkImage, string OriginalSha256,
    string WorkSha256, int ExitCode, string[] Candidates, string LogPath, DateTime DateUtc)
{
    public string Summary => $"EVALUACIÓN DE ESTRUCTURA · TestDisk 7.2\n{Status}\n\n" +
        (Candidates.Length == 0 ? "No se identificaron candidatos en el registro. Esto no descarta archivos recuperables por firmas."
        : "Estructuras candidatas (aún no prueban que sean reparables):\n" + string.Join("\n", Candidates)) +
        "\n\nSe analizó una copia local verificada. No se reparó ni modificó la USB ni la imagen original. " +
        "El siguiente paso es revisar los candidatos; no se aplican cambios automáticamente.\n\nRegistro: " + LogPath;
}

// Deliberately image-only and fixed commands: no caller-supplied commands, device paths or write operations.
public sealed class StructureEvaluation(string engineRoot)
{
    public async Task<StructureReport> Run(string imagePath, string destination, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var original = Safety.ImagePath(imagePath);
        Safety.RequireLocalDisk(destination);
        using var guard = File.Open(original, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace < guard.Length + 268435456L)
            throw new IOException("Se necesita espacio para otra copia completa de la imagen y su registro.");
        var folder = Safety.CreateOutput(destination, "evaluacion-estructura");
        var work = Path.Combine(folder, "copia-trabajo.img");
        var log = Path.Combine(folder, "testdisk.log");
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(work, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576, true))
            {
                var buffer = new byte[1048576];
                int count;
                long copied = 0;
                while ((count = await guard.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    hash.AppendData(buffer, 0, count); copied += count;
                    progress?.Report(new("Preparando copia local para evaluar la estructura…", (int)(copied * 100 / guard.Length)));
                }
                await output.FlushAsync(token); output.Flush(true);
            }
            var originalHash = Convert.ToHexString(hash.GetHashAndReset());
            Safety.CheckAcquisition(original, originalHash);
            var workHash = await RecoveryEngine.HashFile(work, token);
            if (originalHash != workHash) throw new IOException("La copia de trabajo no coincide con la imagen. Evaluación bloqueada.");
            progress?.Report(new("TestDisk busca estructuras en la copia local. Puede tardar; puedes detenerlo."));
            var result = await new ToolRunner(engineRoot).Run("testdisk_win.exe", ["/debug", "/log", "/cmd", work, "analyze,search"], folder, token);
            var text = File.Exists(log) ? await File.ReadAllTextAsync(log, token) : "";
            workHash = await RecoveryEngine.HashFile(work, token);
            if (workHash != originalHash) throw new IOException("El motor modificó la copia de trabajo inesperadamente; evaluación no válida. El original permanece protegido.");
            var normal = result.ExitCode == 0 && text.Contains("TestDisk exited normally.", StringComparison.Ordinal);
            var report = new StructureReport(normal ? "Evaluación terminada; reparabilidad no confirmada" : "Evaluación incompleta; no permite concluir reparabilidad",
                original, work, originalHash, workHash, result.ExitCode, ParseCandidates(text), log, DateTime.UtcNow);
            await ImageAcquirer.AtomicJson(Path.Combine(folder, "evaluacion.json"), report);
            await File.WriteAllTextAsync(Path.Combine(folder, "resumen.txt"), report.Summary, token);
            return report;
        }
        catch (Exception ex)
        {
            await ImageAcquirer.AtomicJson(Path.Combine(folder, "evaluacion-incompleta.json"), new { Status = "Incompleta", Error = ex.Message, OriginalImage = original, WorkImage = work });
            throw;
        }
    }

    public static string[] ParseCandidates(string text) => text.Split('\n')
        .Select(s => s.Trim()).Where(s => Regex.IsMatch(s,
            @"^(?:\d+\s+)?(?:[PLED*]\s+)?(?:FAT(?:12|16|32)(?:\s+LBA)?|NTFS|HPFS - NTFS|exFAT)\s+\d+\s+\d+\s+\d+\s+\d+\s+\d+\s+\d+\s+\d+", RegexOptions.IgnoreCase))
        .Select(s => Regex.Replace(s, @"^(?:\d+\s+)?[PLED*]\s+", ""))
        .Select(s => Regex.Replace(s, @"\s+", " ")).Distinct().Take(100).ToArray();
}
