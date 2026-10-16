using Rescate.Core;
using System.Text.Json;
using DiscUtils;
using DiscUtils.Ntfs;
using System.Security.Cryptography;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : throw new ArgumentException("Project root required"));
var run = Path.Combine(args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "test-results"), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(run);
var engine = new RecoveryEngine(Path.Combine(root, "engines"));
var demo = Path.Combine(root, "demo");
var results = new List<object>();
int failures = 0;
async Task Check(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); results.Add(new { Name = name, Passed = true }); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + " " + ex); results.Add(new { Name = name, Passed = false, Error = ex.ToString() }); }
}
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Expected safety rejection"); }
RecoverySession? session = null;
string? sessionDir = null;
string? exportDir = null;
var expected = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(demo, "expected.json")))
    .RootElement.EnumerateArray().Select(e => e.GetProperty("Sha256").GetString()!).ToHashSet();

await Check("Reject device, UNC, ADS, wrong extension", () =>
{
    foreach (var path in new[] { @"\\.\PhysicalDrive2", @"\\?\C:\test.img", @"\\server\test.img", @"C:\test.img:stream", @"C:\test.exe" }) Throws(() => Safety.ImagePath(path));
    return Task.CompletedTask;
});
await Check("Accept internal workspace and safe filenames", () =>
{
    Safety.RequireInternalDisk(root);
    Assert(Safety.SafeName("../../CON:payload\0.txt") == "CON_payload_.txt", "Unsafe filename");
    return Task.CompletedTask;
});
await Check("Parse filenames containing pipes and deleted entries", () =>
{
    var entry = RecoveryEngine.ParseBodyLine("0|/a|b.txt (deleted)|5|r/rrwxrwxrwx|0|0|14|0|0|0|0", new(0,512,"FAT32"),1);
    Assert(entry is not null && entry.Name == "/a|b.txt" && entry.Size == 14 && entry.State.StartsWith("Eliminado"), "Parser mismatch");
    return Task.CompletedTask;
});
await Check("FAT32 list, deleted entry, read-only image", async () =>
{
    var image = Path.Combine(demo, "fat32-demo.img");
    var before = await RecoveryEngine.HashFile(image, default);
    (session, sessionDir) = await engine.Scan(image, run, false, 512, null, default);
    Assert(session.Status == "Finalizado", string.Join("; ", session.Warnings));
    Assert(session.Files.Count == 4, $"Expected 4 files, got {session.Files.Count}: " + string.Join(",",session.Files.Select(f=>f.Name)));
    Assert(session.Files.Any(f=>f.State.StartsWith("Eliminado")), "Missing deleted file");
    Assert(before == await RecoveryEngine.HashFile(image, default), "Source changed");
});
await Check("Export fragmented and deleted FAT32 files byte-exactly", async () =>
{
    exportDir = await engine.Export(session!, sessionDir!, session!.Files, run, null, default);
    var hashes = new HashSet<string>();
    foreach (var file in Directory.GetFiles(exportDir).Where(f=> !Path.GetFileName(f).StartsWith("informe."))) hashes.Add(await RecoveryEngine.HashFile(file, default));
    Assert(expected.SetEquals(hashes), "Exported content differs from reference");
});
await Check("Session reload and manifest path traversal rejection", async () =>
{
    var loaded = await RecoveryEngine.Load(Path.Combine(sessionDir!, "session.json"));
    Assert(loaded.Files.Count == 4, "Reload failed");
    var bad = loaded with { Files = [new("1", "malicious", 1, "Firmas", "", CachedPath:"../outside.txt")] };
    var badDir = Path.Combine(run, "bad-session"); Directory.CreateDirectory(badDir);
    await RecoveryEngine.Save(bad, badDir);
    try { await RecoveryEngine.Load(Path.Combine(badDir, "session.json")); } catch(IOException) { return; }
    throw new Exception("Traversal accepted");
});
await Check("MBR partition offset discovery and export", async () =>
{
    var (s, d) = await engine.Scan(Path.Combine(demo, "fat32-mbr.img"), run, false, 512, null, default);
    Assert(s.Volumes.Any(v=>v.SectorOffset == 2048), "MBR offset not found");
    Assert(s.Files.Count == 4, "MBR file count mismatch");
    var exported = await engine.Export(s,d,s.Files,run,null,default);
    var hashes = new HashSet<string>();
    foreach(var file in Directory.GetFiles(exported).Where(f=>!Path.GetFileName(f).StartsWith("informe."))) hashes.Add(await RecoveryEngine.HashFile(file,default));
    Assert(expected.SetEquals(hashes), "Offset export mismatch");
});
await Check("RAW metadata failure is reported, not successful zero", async () =>
{
    var (s, _) = await engine.Scan(Path.Combine(demo, "raw-damaged.img"),run,false,512,null,default);
    Assert(s.Files.Count == 0 && s.Warnings.Count > 0 && s.Status != "Finalizado", "Missing failure warning");
});
await Check("PhotoRec recovers PNG from damaged RAW image", async () =>
{
    var image = Path.Combine(demo, "raw-damaged.img");
    var before = await RecoveryEngine.HashFile(image, default);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var (s,d) = await engine.Scan(image,run,true,512,null,timeout.Token);
    Assert(s.Status.StartsWith("Finalizado"), $"PhotoRec did not finish: {s.Status} {string.Join(";", s.Warnings)}");
    Assert(s.Files.Any(f=>f.Extension == ".png"), "No PNG recovered");
    var hashes = new HashSet<string>();
    foreach(var f in s.Files) hashes.Add(await RecoveryEngine.HashFile(Path.Combine(d,f.CachedPath!),default));
    var pngHash = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(demo,"expected.json"))).RootElement.EnumerateArray().Single(e=>e.GetProperty("Name").GetString()=="PHOTO.PNG").GetProperty("Sha256").GetString()!;
    Assert(hashes.Contains(pngHash), "PNG differs from reference");
    Assert(before == await RecoveryEngine.HashFile(image,default), "PhotoRec changed source");
});
await Check("Comparison counts unique content, not duplicate filenames", async () =>
{
    var other = Path.Combine(run,"other-tool"); Directory.CreateDirectory(other);
    var files = Directory.GetFiles(exportDir!).Where(f=>!Path.GetFileName(f).StartsWith("informe.")).ToArray();
    File.Copy(files[0],Path.Combine(other,"different-name.bin"));
    File.Copy(files[0],Path.Combine(other,"duplicate.bin"));
    await File.WriteAllTextAsync(Path.Combine(other,"unique.txt"),"Only the other tool");
    var (s,_) = await Comparison.Compare(exportDir!,other,run,null,default);
    Assert(s.RescateFiles==4 && s.OtherFiles==3 && s.CommonUnique==1 && s.OtherDuplicates==1 && s.RescateOnlyUnique==3, "Comparison mismatch");
});
await Check("Changed image blocks export", async () =>
{
    var changed = Path.Combine(run,"changed.img"); File.Copy(Path.Combine(demo,"fat32-demo.img"),changed);
    using(var file = File.OpenWrite(changed)) { file.Position=500; file.WriteByte(42); }
    var altered = session! with { Image = session!.Image with { Path=changed } };
    try { await engine.Export(altered,sessionDir!,altered.Files,run,null,default); } catch(IOException) { return; }
    throw new Exception("Modified source accepted");
});
await Check("Pre-cancelled scan preserves source", async () =>
{
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    try { await engine.Scan(Path.Combine(demo,"fat32-demo.img"),run,false,512,null,cancel.Token); } catch(OperationCanceledException) { return; }
    throw new Exception("Cancellation ignored");
});
await Check("NTFS resident and nonresident file export", async () =>
{
    var path = Path.Combine(run,"ntfs-test.img");
    var known = new Dictionary<string,byte[]> { ["small.txt"] = System.Text.Encoding.UTF8.GetBytes("Resident NTFS content"), ["large.bin"] = Enumerable.Range(0,20000).Select(i=>(byte)(i%251)).ToArray() };
    using (var stream = new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
    {
        stream.SetLength(64L*1024*1024);
        var boot = new byte[512]; boot[0]=0xeb; boot[1]=0x52; boot[2]=0x90; boot[510]=0x55; boot[511]=0xaa;
        using var ntfs = NtfsFileSystem.Format(stream,"RESCATE TEST",Geometry.FromCapacity(stream.Length),0,stream.Length/512,boot);
        foreach(var item in known) { using var file = ntfs.OpenFile(item.Key,FileMode.Create,FileAccess.ReadWrite); file.Write(item.Value); }
    }
    var before = await RecoveryEngine.HashFile(path,default);
    var (s,d) = await engine.Scan(path,run,false,512,null,default);
    var selected = s.Files.Where(f=>known.ContainsKey(f.Name.TrimStart('/'))).ToArray();
    Assert(selected.Length == 2,"NTFS files not found: " + string.Join(";", s.Warnings));
    var output = await engine.Export(s,d,selected,run,null,default);
    var hashes = new HashSet<string>();
    foreach(var file in Directory.GetFiles(output).Where(f=>!Path.GetFileName(f).StartsWith("informe."))) hashes.Add(await RecoveryEngine.HashFile(file,default));
    Assert(hashes.SetEquals(known.Values.Select(bytes=>Convert.ToHexString(SHA256.HashData(bytes)))),"NTFS hash mismatch");
    Assert(before == await RecoveryEngine.HashFile(path,default),"NTFS image modified");
});
await Check("exFAT long filename and content export", async () =>
{
    var image = Path.Combine(demo,"exfat-demo.img");
    var before = await RecoveryEngine.HashFile(image,default);
    var known = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(demo,"exfat-expected.json"))).RootElement.EnumerateArray().ToDictionary(e=>e.GetProperty("Name").GetString()!,e=>e.GetProperty("Sha256").GetString()!);
    var (s,d) = await engine.Scan(image,run,false,512,null,default);
    var selected = s.Files.Where(f=>known.ContainsKey(f.Name.TrimStart('/'))).ToList();
    Assert(selected.Count==2,"exFAT files not found: " + string.Join(";",s.Warnings));
    var output = await engine.Export(s,d,selected,run,null,default);
    var hashes = new HashSet<string>();
    foreach(var file in Directory.GetFiles(output).Where(f=>!Path.GetFileName(f).StartsWith("informe."))) hashes.Add(await RecoveryEngine.HashFile(file,default));
    Assert(hashes.SetEquals(known.Values),"exFAT content mismatch");
    Assert(before == await RecoveryEngine.HashFile(image,default),"exFAT source changed");
});
await Check("Acquisition: byte-exact image and complete map", async () =>
{
    var folder=Path.Combine(run,"acquire-good");
    using var source=new SyntheticSource();
    var map=await new ImageAcquirer().Create(source,folder,false,null,default);
    Assert(map.Complete && map.ReadBytes==source.Length && map.FailedBytes==0,"Incomplete synthetic copy");
    Assert(map.ImageSha256==Convert.ToHexString(SHA256.HashData(source.Data)),"Acquisition hash mismatch");
    _=Safety.ImagePath(Path.Combine(folder,"usb.img"));
    Safety.CheckAcquisition(Path.Combine(folder,"usb.img"),map.ImageSha256);
});
await Check("Acquisition: failed block recorded and analysis blocked", async () =>
{
    var folder=Path.Combine(run,"acquire-crc");
    using var source=new SyntheticSource { FaultAt=ImageAcquirer.BlockSize };
    var map=await new ImageAcquirer().Create(source,folder,false,null,default);
    Assert(!map.Complete && map.FailedBytes==ImageAcquirer.BlockSize && map.ReadBytes==2L*ImageAcquirer.BlockSize,"Bad error map");
    Throws(()=>Safety.ImagePath(Path.Combine(folder,"usb.img")));
    Assert(source.ReadOffsets.Count==3,"Unexpected retries");
});
await Check("Acquisition: interrupted copy resumes only pending blocks", async () =>
{
    var folder=Path.Combine(run,"acquire-resume");
    using(var source=new SyntheticSource { CancelAt=ImageAcquirer.BlockSize })
    {
        var first=await new ImageAcquirer().Create(source,folder,false,null,default);
        Assert(first.Status=="Cancelada" && first.ReadBytes==ImageAcquirer.BlockSize,"Cancellation map mismatch");
    }
    using var next=new SyntheticSource();
    var map=await new ImageAcquirer().Create(next,folder,true,null,default);
    Assert(map.Complete && !next.ReadOffsets.Contains(0),"Already read block was read again");
    Assert(map.ImageSha256==Convert.ToHexString(SHA256.HashData(next.Data)),"Resumed hash mismatch");
});
await Check("Acquisition: resume rejects changed identity and altered blocks", async () =>
{
    var folder=Path.Combine(run,"acquire-tamper");
    using(var source=new SyntheticSource { CancelAt=ImageAcquirer.BlockSize })
        await new ImageAcquirer().Create(source,folder,false,null,default);
    using(var changed=new SyntheticSource { Identity="different" })
    {
        bool blocked=false; try { await new ImageAcquirer().Create(changed,folder,true,null,default); } catch(IOException) { blocked=true; }
        Assert(blocked,"Different identity accepted");
    }
    using(var file=File.OpenWrite(Path.Combine(folder,"usb.img"))) file.WriteByte(255);
    using var original=new SyntheticSource();
    bool tamperBlocked=false; try { await new ImageAcquirer().Create(original,folder,true,null,default); } catch(IOException) { tamperBlocked=true; }
    Assert(tamperBlocked,"Changed completed block accepted");
});
await Check("Acquisition: existing destination cannot be overwritten", async () =>
{
    var folder=Path.Combine(run,"acquire-good");
    var before=await RecoveryEngine.HashFile(Path.Combine(folder,"usb.img"),default);
    using var source=new SyntheticSource();
    bool blocked=false; try { await new ImageAcquirer().Create(source,folder,false,null,default); } catch(IOException) { blocked=true; }
    Assert(blocked && before==await RecoveryEngine.HashFile(Path.Combine(folder,"usb.img"),default),"Overwrite allowed");
});
await Check("Acquisition: timeout stops without rereading", async () =>
{
    var folder=Path.Combine(run,"acquire-timeout");
    using var source=new SyntheticSource { TimeoutAt=ImageAcquirer.BlockSize };
    var map=await new ImageAcquirer().Create(source,folder,false,null,default);
    Assert(map.Status=="Detenida" && map.PendingBytes==2L*ImageAcquirer.BlockSize && source.ReadOffsets.Count==2,"Timeout did not stop");
});
await Check("Acquisition: local block verification rejects corruption", async () =>
{
    var folder=Path.Combine(run,"acquire-good");
    var image=Path.Combine(folder,"usb.img");
    _=await ImageAcquirer.Verify(image,default);
    using(var file=File.OpenWrite(image)) {file.Position=1024;file.WriteByte(99);}
    bool blocked=false;try {await ImageAcquirer.Verify(image,default);}catch(IOException){blocked=true;}
    Assert(blocked,"Local verification ignored corruption");
});
await Check("Missing recovered file reports an error without empty export", async () =>
{
    var folder=Path.Combine(run,"missing-cache");Directory.CreateDirectory(folder);
    var altered=session! with {Files=[new("1","unavailable.txt",3,"Firmas","",CachedPath:"carved.1/missing.txt")]};
    var output=await engine.Export(altered,folder,altered.Files,run,null,default);
    Assert(!File.Exists(Path.Combine(output,"0000001_unavailable.txt")),"An empty recovery placeholder was created");
    using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"informe.json")));
    Assert(report.RootElement.GetProperty("Files")[0].GetProperty("State").GetString()=="Parcial / error","Missing-file error not recorded");
});
await Check("Diagnostic: complete read hash without source mutation", async () =>
{
    using var source=new SyntheticSource();var before=SHA256.HashData(source.Data);
    var result=await ReadDiagnostic.Run(source,null,default);
    Assert(result.Complete && result.ReadBytes==source.Length && result.Sha256==Convert.ToHexString(before),"Incomplete diagnostic");
    Assert(SHA256.HashData(source.Data).SequenceEqual(before) && result.Recommendation.Contains("no certificada"),"Unsafe or overconfident diagnostic");
});
await Check("Diagnostic: stops on first error without retries", async () =>
{
    using var source=new SyntheticSource{FaultAt=ImageAcquirer.BlockSize};
    var result=await ReadDiagnostic.Run(source,null,default);
    Assert(!result.Complete && result.ErrorOffset==ImageAcquirer.BlockSize && result.Sha256 is null && source.ReadOffsets.Count==2,"Failure was hidden or retried");
});
await Check("Diagnostic: timeout does not claim a healthy device", async () =>
{
    using var source=new SyntheticSource{TimeoutAt=0};var result=await ReadDiagnostic.Run(source,null,default);
    Assert(!result.Complete && result.ReadBytes==0 && result.Error is not null,"Timeout ignored");
});
await Check("Diagnostic: cancellation is inconclusive", async () =>
{
    using var source=new SyntheticSource{CancelAt=0};var result=await ReadDiagnostic.Run(source,null,default);
    Assert(!result.Complete && result.Error is null && result.Sha256 is null && result.Status.StartsWith("Cancelado"),"Cancellation misclassified");
});
await Check("Diagnostic: progress storage failure is not a USB fault", async () =>
{
    using var source=new SyntheticSource();bool distinguished=false;
    try {await ReadDiagnostic.Run(source,new FailingDiagnosticProgress(),default);}
    catch(InvalidOperationException ex){distinguished=ex.Message.Contains("no indica un fallo");}
    Assert(distinguished,"Report I/O failure incorrectly blamed on USB");
});
await Check("USB inventory query returns only nonnegative device numbers", async () =>
{
    var disks=await UsbInventory.List(default);
    Assert(disks.All(d=>d.Number>=0 && d.Size>=0),"Invalid inventory response");
});
await Check("Reuse: system disks and unknown operations are blocked", () =>
{
    var target=new UsbDisk(2,"Test USB","serial","unique",16_000_000_000,"Healthy","Online",false,false);
    ReusePlanner.ValidateTarget(target,"E","FormatAndCopy");
    Throws(()=>ReusePlanner.ValidateTarget(target with{IsBoot=true},"E","FormatAndCopy"));
    Throws(()=>ReusePlanner.ValidateTarget(target with{IsSystem=true},"E","FormatOnly"));
    Throws(()=>ReusePlanner.ValidateTarget(target with{UniqueId=""},"E","FormatOnly"));
    Throws(()=>ReusePlanner.ValidateTarget(target,"C","FormatOnly"));
    Throws(()=>ReusePlanner.ValidateTarget(target,"E","ClearDisk"));
    Assert(ReusePlanner.ConfirmationText("E")=="BORRAR E:","Ambiguous consent text");
    return Task.CompletedTask;
});
await Check("Reuse: destination paths cannot escape the new folder", () =>
{
    foreach(var path in new[]{"../a",@"..\a",@"C:\a",@"\\server\a","a:stream","a//b","a/./b",""})Throws(()=>ReusePlanner.CheckedRelativePath(path));
    Assert(ReusePlanner.CheckedRelativePath(@"PDF\document.pdf")==@"PDF\document.pdf","Safe relative path blocked");
    return Task.CompletedTask;
});
await Check("Stability: matching repeated reads and original reference", async () =>
{
    using var source=new SyntheticSource();using var reference=new MemoryStream(source.Data,false);
    var samples=await StabilityProbe.Run(source,reference,Path.Combine(run,"stable-samples"),default,4);
    Assert(samples.All(s=>s.RepeatMatches&&s.MatchesReference),"Stable data misclassified");
});
await Check("Stability: catches silent changes without read errors", async () =>
{
    using var source=new SyntheticSource{ChangeRepeatedReads=true};using var reference=new MemoryStream(source.Data,false);
    var samples=await StabilityProbe.Run(source,reference,Path.Combine(run,"unstable-samples"),default,4);
    Assert(samples.All(s=>!s.RepeatMatches&&!s.MatchesReference),"Silent corruption missed");
});
await Check("Acquisition selection rejects swapped and protected devices",()=>
{
    var selected=new UsbDisk(2,"General UDisk","","id",16000000000,"","",false,false,"E:");
    AcquisitionSelection.Validate(selected,selected);
    Throws(()=>AcquisitionSelection.Validate(selected,selected with{UniqueId="another"}));
    Throws(()=>AcquisitionSelection.Validate(selected,selected with{IsSystem=true}));
    Throws(()=>AcquisitionSelection.Validate(selected,selected with{Size=8000000000}));
    Throws(()=>AcquisitionSelection.Validate(selected,null));
    Throws(()=>AcquisitionSelection.Validate(selected with{UniqueId=""},selected));
    Assert(selected.DisplayName.Contains("E:"),"Drive letter is not visible");
    Assert(AcquisitionSelection.RequiredSpace(16000000000)>48000000000,"Missing output reserve");
    return Task.CompletedTask;
});
await Check("Acquisition: access denial identifies source stage and preserves partial bytes",async()=>
{
    using var source=new SyntheticSource{DeniedAt=ImageAcquirer.BlockSize};
    var map=await new ImageAcquirer().Create(source,Path.Combine(run,"access-denied"),false,null,default);
    Assert(!map.Complete && map.ReadBytes==ImageAcquirer.BlockSize && map.ErrorOffset==ImageAcquirer.BlockSize,"Partial state lost");
    Assert(map.ErrorStage=="Lectura de la memoria USB" && map.ErrorCode is not null && map.Error!.Contains("Windows"),"Access failure not explained");
});
await Check("Initial diagnostic is collected during acquisition without extra reads",async()=>
{
    using var source=new SyntheticSource();
    var folder=Path.Combine(run,"initial-diagnostic");
    var map=await new ImageAcquirer().Create(source,folder,false,null,default);
    var report=InitialDiagnostic.From(map);
    Assert(source.ReadOffsets.Count==3 && report.TimedBlocks==3 && report.CopyComplete,"Extra reads or missing timings");
    Assert(File.Exists(Path.Combine(folder,"diagnostico-inicial.json")),"Report not persisted");
    var old=InitialDiagnostic.From(new AcquisitionMap());
    Assert(old.MaximumReadMilliseconds is null && old.Summary.Contains("no registró tiempos"),"Old timing guessed");
    map.Ranges[0]=map.Ranges[0] with{ReadMilliseconds=2501};
    Assert(InitialDiagnostic.From(map).SlowBlocks==1,"Slow block threshold");
});
await Check("Initial diagnostic distinguishes read faults and incomplete access denial",async()=>
{
    using var source=new SyntheticSource{FaultAt=ImageAcquirer.BlockSize};
    var map=await new ImageAcquirer().Create(source,Path.Combine(run,"initial-error"),false,null,default);
    var report=InitialDiagnostic.From(map);
    Assert(report.FailedBytes==ImageAcquirer.BlockSize && !report.CopyComplete && report.Status.Contains("errores"),"Read error hidden");
    var denied=InitialDiagnostic.From(new AcquisitionMap{Length=100,ErrorStage="Escritura de la copia en la computadora",Error="Disco lleno"});
    Assert(denied.FailedBytes==0 && denied.Status=="Prueba incompleta","Local failure called USB damage");
});
await Check("TestDisk evaluates verified working copy and preserves original",async()=>
{
    var original=Path.Combine(demo,"fat32-mbr.img");
    var before=await RecoveryEngine.HashFile(original,default);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var report=await new StructureEvaluation(Path.Combine(root,"engines")).Run(original,run,null,timeout.Token);
    Assert(report.ExitCode==0 && report.Status.StartsWith("Evaluación terminada"),"TestDisk failed: "+report.Status);
    Assert(report.Candidates.Any(x=>x.Contains("FAT32")),"Missing FAT32 candidate");
    Assert(report.WorkImage!=original && report.OriginalSha256==before && report.WorkSha256==before,"Copy integrity mismatch");
    Assert(await RecoveryEngine.HashFile(original,default)==before,"Original changed");
});
await Check("Structure evaluation cancellation is not a successful diagnosis",async()=>
{
    using var cancel=new CancellationTokenSource();cancel.Cancel();
    try{await new StructureEvaluation(Path.Combine(root,"engines")).Run(Path.Combine(demo,"fat32-demo.img"),run,null,cancel.Token);}
    catch(OperationCanceledException){return;}
    throw new Exception("Cancellation ignored");
});
await File.WriteAllTextAsync(Path.Combine(run,"results.json"),JsonSerializer.Serialize(results,RecoveryEngine.Json));
await File.WriteAllTextAsync(Path.Combine(root,"test-results","latest.txt"),run);
Console.WriteLine($"\n{results.Count-failures}/{results.Count} passed. Results: {run}");
if(sessionDir is not null) await File.WriteAllTextAsync(Path.Combine(root,"test-results","render-session.txt"),Path.Combine(sessionDir,"session.json"));
Environment.ExitCode=failures==0?0:1;

internal sealed class FailingDiagnosticProgress : IProgress<AcquisitionProgress>
{public void Report(AcquisitionProgress value)=>throw new IOException("Test output disk failure");}
internal sealed class SyntheticSource : IReadOnlySource
{
    public byte[] Data {get;}=Enumerable.Range(0,3*ImageAcquirer.BlockSize).Select(i=>(byte)(i%251)).ToArray();
    public long Length=>Data.Length;
    public int SectorSize=>512;
    public string Identity {get;set;}="synthetic-fixed-source-v1";
    public bool CanResume=>true;
    public long DeniedAt {get;init;}=-1;
    public long FaultAt {get;init;}=-1;
    public long CancelAt {get;init;}=-1;
    public long TimeoutAt {get;init;}=-1;
    public bool ChangeRepeatedReads {get;init;}
    public List<long> ReadOffsets {get;}=[];
    public ValueTask<int> Read(long offset,Memory<byte> buffer,CancellationToken token)
    {
        ReadOffsets.Add(offset); token.ThrowIfCancellationRequested();
        if(offset==DeniedAt)throw new UnauthorizedAccessException();
        if(offset==FaultAt) throw new IOException("Synthetic CRC error",unchecked((int)0x80070017));
        if(offset==CancelAt) throw new OperationCanceledException();
        if(offset==TimeoutAt) throw new TimeoutException("Synthetic device timeout");
        Data.AsMemory((int)offset,buffer.Length).CopyTo(buffer);
        if(ChangeRepeatedReads && ReadOffsets.Count(x=>x==offset)>1)buffer.Span[0]^=1;
        return ValueTask.FromResult(buffer.Length);
    }
    public void Dispose() { }
}
