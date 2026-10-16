namespace Rescate.Core;

public sealed record VolumeCandidate(long SectorOffset, int SectorSize, string Description);
public sealed record FoundFile(string Id, string Name, long Size, string Method, string State,
    long SectorOffset = 0, int SectorSize = 512, string Inode = "", string? CachedPath = null)
{
    public string Extension => Path.GetExtension(Name).ToLowerInvariant();
    public string SizeLabel => Size < 0 ? "Desconocido" : $"{Size:N0} bytes";
}
public sealed record ImageIdentity(string Path, long Length, DateTime LastWriteUtc, string Sha256);
public sealed record RecoverySession(string Version, ImageIdentity Image, DateTime CreatedUtc,
    string Mode, string Status, List<VolumeCandidate> Volumes, List<FoundFile> Files, List<string> Warnings);
public sealed record ExportedFile(string Id, string OriginalName, string OutputName, long Bytes,
    string Sha256, string State, string Method, string? Error);
public sealed record ScanProgress(string Message, int? Percent = null);
public sealed record ComparisonSummary(long RescateFiles, long OtherFiles, long RescateUnique,
    long OtherUnique, long CommonUnique, long RescateOnlyUnique, long OtherOnlyUnique,
    long RescateDuplicates, long OtherDuplicates, string Note);
