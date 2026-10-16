using Rescate.Core;
using System.Text.Json;

try
{
    if(args.Length==6 && args[0]=="prepare-reuse")
    {
        var target=(await UsbInventory.List(default)).Single(d=>d.Number==int.Parse(args[3]));
        var plan=await ReusePlanner.Prepare(args[1],args[2],target,args[4],args[5],default);
        Console.WriteLine(JsonSerializer.Serialize(new{PlanFile=Path.Combine(plan.ReportFolder,"plan.json"),Count=plan.Files.Count,Bytes=plan.Files.Sum(f=>f.Length),Operation=plan.Operation,Executed=false},RecoveryEngine.Json));
        return;
    }
    if (args.Length < 3) throw new ArgumentException("Uso: scan imagen destino metadata|signatures; export session.json destino");
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !Directory.Exists(Path.Combine(root.FullName,"engines"))) root=root.Parent;
    if(root is null) throw new IOException("No se encontraron los motores.");
    var engine=new RecoveryEngine(Path.Combine(root.FullName,"engines"));
    var output=Safety.LocalPath(args[2]); Safety.RequireInternalDisk(output); Directory.CreateDirectory(output);
    using var cancel=new CancellationTokenSource();
    Console.CancelKeyPress+=(_,e)=> {e.Cancel=true;cancel.Cancel();};
    using var stop=new Timer(_=> {if(File.Exists(Path.Combine(output,"STOP")))cancel.Cancel();},null,1000,1000);
    var progress=new InlineProgress(p=>
    {
        Console.WriteLine(p.Message);
        ImageAcquirer.AtomicJson(Path.Combine(output,"cli-progress.json"),new { p.Message,p.Percent,UpdatedUtc=DateTime.UtcNow }).GetAwaiter().GetResult();
    });
    if(args[0]=="verify")
    {
        Console.WriteLine("Verificando todos los bloques de la copia local; sin acceder a la USB.");
        var hash=await ImageAcquirer.Verify(args[1],cancel.Token);
        await ImageAcquirer.AtomicJson(Path.Combine(output,"verification.json"),new {Verified=true,ImageSha256=hash,DateUtc=DateTime.UtcNow});
        Console.WriteLine("Imagen local verificada por bloques y SHA-256 completo.");
    }
    else if(args[0]=="scan")
    {
        if(args.Length!=4 || args[3] is not ("metadata" or "signatures")) throw new ArgumentException("Tipo de análisis inválido.");
        var (session,folder)=await engine.Scan(args[1],output,args[3]=="signatures",512,progress,cancel.Token);
        var result=new {SessionDirectory=folder,SessionFile=Path.Combine(folder,"session.json"),session.Status,Count=session.Files.Count,Bytes=session.Files.Sum(f=>f.Size),session.Warnings};
        await ImageAcquirer.AtomicJson(Path.Combine(output,"scan-result.json"),result);
        Console.WriteLine(JsonSerializer.Serialize(result,RecoveryEngine.Json));
        Environment.ExitCode=session.Status.StartsWith("Finalizado")?0:2;
    }
    else if(args[0]=="export")
    {
        var session=await RecoveryEngine.Load(args[1]);
        var folder=await engine.Export(session,Path.GetDirectoryName(args[1])!,session.Files,output,progress,cancel.Token);
        await ImageAcquirer.AtomicJson(Path.Combine(output,"export-result.json"),new {Directory=folder,Count=session.Files.Count});
        Console.WriteLine(folder);
    }
    else throw new ArgumentException("Operación no reconocida.");
}
catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
internal sealed class InlineProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
{ public void Report(ScanProgress value)=>report(value); }
