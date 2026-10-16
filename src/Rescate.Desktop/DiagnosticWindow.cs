using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Rescate.Core;

namespace Rescate.Desktop;

public sealed class DiagnosticWindow : Window
{
    private readonly ComboBox disks=new(){MinWidth=450,Margin=new Thickness(0,8,0,8)};
    private readonly TextBox details=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=140,MaxHeight=190};
    private readonly TextBlock progress=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    private readonly Button refresh=new(){Content="Buscar memorias conectadas"};
    private readonly Button read=new(){Content="Comprobar lectura completa",IsEnabled=false};
    private readonly Button stop=new(){Content="Detener prueba",IsEnabled=false};
    private readonly string project;
    private readonly string? caseFolder;
    private string? activeFolder;
    private bool running;
    private readonly Button initial=new(){Content="Ver diagnóstico de la copia"};
    private readonly Button evaluate=new(){Content="Evaluar estructura en copia local"};
    private readonly ProgressBar meter=new(){Height=12,Margin=new Thickness(0,8,0,8)};
    private CancellationTokenSource? evaluationCancel;

    public DiagnosticWindow(string projectRoot,string? folder)
    {
        project=projectRoot;caseFolder=folder;Title="Diagnóstico de memoria · sin escritura";Width=780;Height=680;MinWidth=660;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21,19,46));Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Ink");
        var panel=new StackPanel{Margin=new Thickness(20)};Content=new ScrollViewer{Content=panel,Background=Background,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="Diagnóstico de la memoria USB",FontSize=24,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Consulta de Windows y prueba de lectura. No formatea, repara ni escribe en la memoria.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16)});
        panel.Children.Add(details);
        panel.Children.Add(initial);panel.Children.Add(evaluate);panel.Children.Add(meter);
        initial.IsEnabled=caseFolder is not null && File.Exists(Path.Combine(caseFolder,"usb.img.map.json"));
        evaluate.IsEnabled=initial.IsEnabled;
        initial.Click+=(_,_)=>ShowInitial();evaluate.Click+=async(_,_)=>await Evaluate();
        panel.Children.Add(refresh);panel.Children.Add(disks);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};buttons.Children.Add(read);buttons.Children.Add(stop);panel.Children.Add(buttons);panel.Children.Add(progress);
        panel.Children.Add(new TextBlock{Text="Una lectura correcta no certifica escritura, capacidad real ni retención. No se realizan pruebas destructivas. El estado que informa Windows tampoco equivale a un diagnóstico físico completo.",TextWrapping=TextWrapping.Wrap});
        refresh.Click+=async(_,_)=>await Refresh();read.Click+=async(_,_)=>await Read();
        stop.Click+=(_,_)=>{if(evaluationCancel is not null){evaluationCancel.Cancel();progress.Text="Deteniendo evaluación…";return;}if(activeFolder is not null){File.WriteAllText(Path.Combine(activeFolder,"STOP"),"stop");progress.Text="Deteniendo al terminar o cancelar la lectura pendiente…";}};
        disks.SelectionChanged+=(_,_)=>{read.IsEnabled=!running && disks.SelectedItem is UsbDisk d && !d.IsBoot && !d.IsSystem;};
        Closing+=(_,e)=>{if(running){e.Cancel=true;progress.Text="Detén la prueba antes de cerrar esta ventana.";}};
        LoadHistory();
        Loaded+=async(_,_)=>await Refresh();
    }

    private void LoadHistory()
    {
        details.Text="Sesión nueva. Selecciona una USB conectada para consultar su estado.";
        if(caseFolder is null)return;
        ShowInitial();
        try
        {
            var latest=Directory.EnumerateDirectories(caseFolder,"diagnostico-*")
                .Select(d=>Path.Combine(d,"diagnostic-result.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if(latest is null)return;
            var result=JsonSerializer.Deserialize<ReadDiagnosticResult>(File.ReadAllText(latest),RecoveryEngine.Json);
            var map=JsonSerializer.Deserialize<AcquisitionMap>(File.ReadAllText(Path.Combine(caseFolder,"usb.img.map.json")),RecoveryEngine.Json);
            if(result is not null && map is not null)
            {
                var matches=result.Complete && string.Equals(result.Sha256,map.ImageSha256,StringComparison.OrdinalIgnoreCase);
                details.Text=$"ÚLTIMO DIAGNÓSTICO GUARDADO · {result.DateUtc.ToLocalTime():g}\n\n{result.Status}\nLeído: {result.ReadBytes/1e9:N2} / {result.TotalBytes/1e9:N2} GB.\n\n"+
                    (matches?"La huella coincide con la primera copia. Esto no certifica escritura ni retención.":"ATENCIÓN: lectura incompleta o huella distinta del respaldo. No se puede declarar fiable. La reutilización automática está bloqueada; investigar antes de borrar.")+"\n\n"+result.Recommendation;
                var sample=Directory.EnumerateDirectories(caseFolder,"estabilidad-*").Select(d=>Path.Combine(d,"samples-result.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(sample is not null)
                {
                    using var s=JsonDocument.Parse(File.ReadAllText(sample));var r=s.RootElement;
                    details.Text+=$"\n\nMuestreo posterior: {r.GetProperty("ComparedSamples")} zonas; distintas del respaldo: {r.GetProperty("DifferentFromReference")}; distintas entre lecturas: {r.GetProperty("DifferentBetweenReads")}. No certifica zonas no muestreadas.";
                }
            }
        }
        catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {details.Text+="\nNo se pudo cargar el detalle del diagnóstico reciente.";}
    }

    private void ShowInitial()
    {
        if(caseFolder is null)return;
        try
        {
            var map=JsonSerializer.Deserialize<AcquisitionMap>(File.ReadAllText(Path.Combine(caseFolder,"usb.img.map.json")),RecoveryEngine.Json);
            if(map is not null)details.Text=InitialDiagnostic.From(map).Summary;
        }
        catch(Exception ex) when(ex is IOException or JsonException){details.Text="No se pudo cargar el diagnóstico inicial: "+ex.Message;}
    }

    private async Task Evaluate()
    {
        if(running || caseFolder is null)return;
        running=true;refresh.IsEnabled=false;read.IsEnabled=false;disks.IsEnabled=false;
        initial.IsEnabled=false;evaluate.IsEnabled=false;stop.IsEnabled=true;
        evaluationCancel=new CancellationTokenSource();meter.IsIndeterminate=true;
        try
        {
            var updates=new Progress<ScanProgress>(p=>{progress.Text=p.Message;meter.IsIndeterminate=p.Percent is null;if(p.Percent is int percent)meter.Value=percent;});
            var report=await new StructureEvaluation(Path.Combine(project,"engines")).Run(Path.Combine(caseFolder,"usb.img"),caseFolder,updates,evaluationCancel.Token);
            details.Text=report.Summary;progress.Text="Informe guardado junto a la copia de trabajo. No se ha reparado la USB.";
        }
        catch(OperationCanceledException){progress.Text="Evaluación detenida. El original permanece intacto; la copia de trabajo incompleta se conserva.";}
        catch(Exception ex){progress.Text="No se completó la evaluación: "+ex.Message;}
        finally
        {
            evaluationCancel.Dispose();evaluationCancel=null;running=false;meter.IsIndeterminate=false;
            initial.IsEnabled=true;evaluate.IsEnabled=true;stop.IsEnabled=false;refresh.IsEnabled=true;disks.IsEnabled=true;
            read.IsEnabled=disks.SelectedItem is UsbDisk d && !d.IsBoot && !d.IsSystem;
        }
    }

    private async Task Refresh()
    {
        refresh.IsEnabled=false;read.IsEnabled=false;
        try
        {
            var values=await UsbInventory.List(default);disks.ItemsSource=values;disks.SelectedIndex=-1;
            progress.Text=values.Length==0?"No hay memorias USB conectadas. Conecta una para iniciar el diagnóstico.":"Selecciona la memoria por modelo y capacidad. Se excluyen las unidades de arranque y sistema.";
            disks.SelectionChanged-=DiskDetails;disks.SelectionChanged+=DiskDetails;
        }
        catch(Exception ex){progress.Text="No se pudo consultar Windows: "+ex.Message;}
        finally{refresh.IsEnabled=true;}
    }
    private void DiskDetails(object sender,SelectionChangedEventArgs e)
    {
        if(disks.SelectedItem is UsbDisk d)progress.Text=$"{d}\nEstado informado por Windows: {d.HealthStatus}; {d.OperationalStatus}.\nContadores de desgaste/SMART: no consultados por este módulo; no se presumen normales.\nLa prueba completa requiere permiso de administrador y puede tardar varios minutos.";
    }
    private async Task Read()
    {
        if(disks.SelectedItem is not UsbDisk d || d.IsBoot || d.IsSystem)return;
        running=true;initial.IsEnabled=false;evaluate.IsEnabled=false;refresh.IsEnabled=false;read.IsEnabled=false;disks.IsEnabled=false;
        try
        {
            var parent=caseFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Revivo","Diagnosticos");Safety.RequireInternalDisk(parent);
            activeFolder=Safety.CreateOutput(parent,"diagnostico");
            var request=Path.Combine(activeFolder,"request.json");
            await ImageAcquirer.AtomicJson(request,new{DiskNumber=d.Number,ExpectedBytes=d.Size,ExpectedProduct=d.FriendlyName,Destination=activeFolder,Mode="Diagnostic",ExpectedUniqueId=d.UniqueId});
            var helper=Path.Combine(project,"app-copia-092","RescateUSB.Copia.exe");
            if(!File.Exists(helper))throw new IOException("No se encontró el ayudante de diagnóstico.");
            using var process=Process.Start(new ProcessStartInfo(helper){UseShellExecute=true,Verb="runas",Arguments="\""+request+"\"",WindowStyle=ProcessWindowStyle.Hidden}) ?? throw new IOException("No se inició el diagnóstico.");
            stop.IsEnabled=true;progress.Text="Prueba iniciada. Se lee la memoria sin guardar otra imagen ni escribir en ella.";
            while(!process.HasExited)
            {
                await Task.Delay(1000);
                var path=Path.Combine(activeFolder,"diagnostic-progress.json");
                if(File.Exists(path))
                {
                    try
                    {
                        var p=JsonSerializer.Deserialize<AcquisitionProgress>(await File.ReadAllTextAsync(path),RecoveryEngine.Json);
                        if(p is not null)progress.Text=$"Leído {p.ReadBytes/1e9:N2} / {p.Total/1e9:N2} GB · {p.MebibytesPerSecond:N1} MiB/s";
                    }
                    catch(IOException){}catch(JsonException){}
                }
            }
            var resultPath=Path.Combine(activeFolder,"diagnostic-result.json");
            if(!File.Exists(resultPath))
            {
                var errorPath=Path.Combine(activeFolder,"completion.json");
                if(File.Exists(errorPath)){using var error=JsonDocument.Parse(await File.ReadAllTextAsync(errorPath));throw new IOException(error.RootElement.GetProperty("Error").GetString());}
                throw new IOException("El ayudante terminó sin diagnóstico. No se declara que la memoria esté sana.");
            }
            var result=JsonSerializer.Deserialize<ReadDiagnosticResult>(await File.ReadAllTextAsync(resultPath),RecoveryEngine.Json) ?? throw new IOException("Resultado no válido.");
            details.Text=$"{result.Status}\n\nLeído: {result.ReadBytes/1e9:N2} / {result.TotalBytes/1e9:N2} GB\nBloques que tardaron más de 2 segundos: {result.SlowBlocks}\n{result.Error}\n\n{result.Recommendation}\n\nInforme: {resultPath}";
            progress.Text="Informe guardado. La lentitud por sí sola no prueba daño físico.";
        }
        catch(Exception ex){progress.Text="No se completó el diagnóstico: "+ex.Message;}
        finally{running=false;initial.IsEnabled=caseFolder is not null;evaluate.IsEnabled=initial.IsEnabled;stop.IsEnabled=false;refresh.IsEnabled=true;disks.IsEnabled=true;read.IsEnabled=disks.SelectedItem is UsbDisk selected && !selected.IsBoot && !selected.IsSystem;}
    }
}
