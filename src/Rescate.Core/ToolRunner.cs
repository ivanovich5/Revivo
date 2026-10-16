using System.Diagnostics;
using System.Text;

namespace Rescate.Core;

public sealed record ToolResult(int ExitCode, string Output, string Error);
public sealed class ToolRunner(string engineRoot)
{
    public string Find(string name)
    {
        var files = Directory.GetFiles(engineRoot, name, SearchOption.AllDirectories);
        if (files.Length != 1) throw new IOException($"Motor no disponible o ambiguo: {name}");
        return files[0];
    }

    public async Task<ToolResult> Run(string name, IEnumerable<string> args, string working,
        CancellationToken token, Stream? binaryOutput = null, Action<string>? lineOutput = null)
    {
        var info = new ProcessStartInfo(Find(name))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = working,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        // Upstream PhotoRec requests elevation for physical disks. Image-only jobs
        // deliberately run with the caller's ordinary token; never show UAC.
        if (name is "photorec_win.exe" or "testdisk_win.exe") info.Environment["__COMPAT_LAYER"] = "RunAsInvoker";
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new IOException("No se pudo iniciar el motor.");
        process.StandardInput.Close();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = Collect(process.StandardError, null, CancellationToken.None);
        Task<string> output = binaryOutput is null
            ? Collect(process.StandardOutput, lineOutput, CancellationToken.None)
            : Copy(process.StandardOutput.BaseStream, binaryOutput);
        await process.WaitForExitAsync(CancellationToken.None);
        var result = new ToolResult(process.ExitCode, await output, await errors);
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static async Task<string> Copy(Stream source, Stream destination)
    { await source.CopyToAsync(destination); return ""; }

    private static async Task<string> Collect(StreamReader reader, Action<string>? callback, CancellationToken token)
    {
        var collected = new StringBuilder();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            callback?.Invoke(line);
            if (collected.Length < 256_000) collected.AppendLine(line);
        }
        return collected.ToString();
    }
}
