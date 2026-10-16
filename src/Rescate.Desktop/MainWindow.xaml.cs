using Microsoft.Win32;
using Rescate.Core;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;

namespace Rescate.Desktop;

public partial class MainWindow : Window
{
    private readonly string root;
    private readonly RecoveryEngine engine;
    private RecoverySession? session;
    private string? sessionDir;
    private string? image;
    private string? lastExport;
    private CancellationTokenSource? operation;
    private bool busy;
    private int previewGeneration;
    private LocalSettings? settings;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_,_) => WindowSizing.FitRestoreBounds(this);
        root = FindRoot();
        engine = new(Path.Combine(root, "engines"));
        OutputBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Revivo", "Recuperaciones");
        try
        {
            var config=PreferencesPath;
            if(File.Exists(config)) { var preferences=JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(config),RecoveryEngine.Json); settings=new LocalSettings(preferences?.ResultsFolder,null); }
            if(settings?.ResultsFolder is not null) OutputBox.Text=Safety.LocalPath(settings.ResultsFolder);
            ReviewedButton.IsEnabled=settings?.LatestReviewFolder is not null && Directory.Exists(settings.LatestReviewFolder);
        }
        catch(Exception ex) { StatusText.Text="No se pudo cargar la configuración local: "+ex.Message; }
        settings ??= new LocalSettings(OutputBox.Text,null);
        Closing += OnClosing;
        Loaded+=async(_,_)=>await RefreshUsb();
    }

    private static string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Revivo","preferences.json");
    private async Task SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        await ImageAcquirer.AtomicJson(PreferencesPath,new LocalSettings(OutputBox.Text,null));
    }
    public void VerifyFreshStart()
    {
        if(settings?.AcquisitionFolder is not null || settings?.LatestPreviewCatalog is not null || session is not null || image is not null)
            throw new IOException("Se restauró información de una sesión anterior.");
    }
    private static string FindRoot()
    {
        var path = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "engines"))) return dir.FullName;
        return path;
    }

    private void Image_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var picker = new OpenFileDialog { Title = "Abrir una imagen (no la USB)", Filter = "Imágenes RAW|*.img;*.dd;*.raw", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try { image = Safety.ImagePath(picker.FileName); ImageBox.Text = image; }
        catch (Exception ex) { Error(ex); }
    }

    private void Output_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var path = PickFolder("Carpeta en un disco interno para resultados");
        if (path is null) return;
        try { Safety.RequireInternalDisk(path); OutputBox.Text = path; }
        catch (Exception ex) { Error(ex); }
    }

    private void Demo_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var demo = Path.Combine(root, "demo", "fat32-demo.img");
        if (!File.Exists(demo)) { Error(new IOException("No se encontró la imagen de ejemplo.")); return; }
        image = demo; ImageBox.Text = demo; ModeBox.SelectedIndex = 0;
        StatusText.Text = "Ejemplo preparado: pulsa Buscar archivos. Incluye archivos conocidos y uno eliminado.";
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (image is null) { Error(new IOException("Elige una imagen o usa el ejemplo incluido.")); return; }
        var selectedImage = image;
        var parent = OutputBox.Text;
        var signatures = ModeBox.SelectedIndex == 1;
        var sector = SectorBox.SelectedIndex == 0 ? 512 : 4096;
        await RunOperation(async (progress, token) =>
        {
            await SearchAutomatically(selectedImage,parent,sector,progress,token);
        });
    }

    public async Task LoadSession(string path)
    {
        session = await RecoveryEngine.Load(path);
        sessionDir = Path.GetDirectoryName(path)!;
        image = session.Image.Path; ImageBox.Text = image;
        ShowResults();
        StatusText.Text = "Sesión abierta. Se comprobará la huella de la imagen antes de exportar.";
    }

    public async Task LoadLatestResult()
    {
        if(settings?.LatestSessionFile is null) return;
        try { await LoadSession(settings.LatestSessionFile); }
        catch(Exception ex) { StatusText.Text="El último resultado no está disponible: "+ex.Message; }
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var picker = new OpenFileDialog { Title = "Abrir session.json", Filter = "Sesión|session.json" };
        if (picker.ShowDialog(this) != true) return;
        try { await LoadSession(picker.FileName); }
        catch (Exception ex) { Error(ex); }
    }

    private void ShowResults()
    {
        if (session is null) return;
        CountText.Text = $"{session.Files.Count:N0} hallazgos del motor";
        SessionText.Text = $"{session.Mode} · {session.Status}";
        Filter();
        ExportAllButton.IsEnabled = !busy && session.Files.Count > 0;
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) => Filter();
    private void Filter()
    {
        if (FilesGrid is null || session is null) return;
        var query = FilterBox.Text.Trim();
        FilesGrid.ItemsSource = session.Files.Where(f => query.Length == 0 || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || f.State.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        ExportSelectedButton.IsEnabled = !busy && FilesGrid.SelectedItems.Count > 0;
        if (FilesGrid.SelectedItem is not FoundFile file || session is null || sessionDir is null) return;
        var generation = ++previewGeneration;
        try
        {
            var text = await engine.Preview(session, sessionDir, file, CancellationToken.None);
            if (generation == previewGeneration) PreviewText.Text = text;
        }
        catch (Exception ex) { PreviewText.Text = ex.Message; }
    }

    private async void ExportSelected_Click(object sender, RoutedEventArgs e) => await Export(FilesGrid.SelectedItems.Cast<FoundFile>().ToList());
    private async void ExportAll_Click(object sender, RoutedEventArgs e) => await Export(session?.Files.ToList() ?? []);
    private async Task Export(List<FoundFile> entries)
    {
        if (busy || session is null || sessionDir is null) return;
        var destination = PickFolder("Exportar a una carpeta de un disco interno");
        if (destination is null) return;
        await RunOperation(async (progress, token) =>
        {
            lastExport = await engine.Export(session, sessionDir, entries, destination, progress, token);
            StatusText.Text = "Exportación terminada. Revisa informe.csv para distinguir archivos parciales.";
            MessageBox.Show(this, $"Archivos e informe guardados en:\n\n{lastExport}\n\nExportado no significa que el contenido esté íntegro.", "Exportación terminada", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var left = PickFolder("1 de 2 · Carpeta de archivos exportados por Rescate USB", lastExport);
        if (left is null) return;
        var right = PickFolder("2 de 2 · Carpeta de archivos exportados por Recoverit u otra herramienta");
        if (right is null) return;
        var destination = OutputBox.Text;
        await RunOperation(async (progress, token) =>
        {
            var result = await Comparison.Compare(left, right, destination, progress, token);
            var s = result.Summary;
            MessageBox.Show(this, $"Rescate USB: {s.RescateFiles:N0} archivos / {s.RescateUnique:N0} contenidos únicos\n" +
                $"Otra herramienta: {s.OtherFiles:N0} archivos / {s.OtherUnique:N0} contenidos únicos\n\n" +
                $"Contenidos idénticos: {s.CommonUnique:N0}\nSolo Rescate USB: {s.RescateOnlyUnique:N0}\nSolo otra herramienta: {s.OtherOnlyUnique:N0}\n\n" +
                $"Informe: {result.Report}\n\nLa comparación necesita archivos exportados; el conteo del escaneo de Recoverit es una referencia separada.",
                "Comparación por SHA-256", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Comparación terminada; se guardó un informe con duplicados y coincidencias.";
        });
    }

    private async Task RunOperation(Func<IProgress<ScanProgress>, CancellationToken, Task> action)
    {
        busy = true; operation = new(); SetBusy();
        var progress = new Progress<ScanProgress>(p =>
        {
            StatusText.Text = p.Message;
            ProgressBar.IsIndeterminate = p.Percent is null;
            if (p.Percent is not null) ProgressBar.Value = p.Percent.Value;
        });
        try { await action(progress, operation.Token); }
        catch (OperationCanceledException) { StatusText.Text = "Operación cancelada. Los resultados parciales permanecen en su carpeta."; }
        catch (Exception ex) { Error(ex); }
        finally { operation.Dispose(); operation = null; busy = false; SetBusy(); }
    }

    private void SetBusy()
    {
        UsbList.IsEnabled=!busy;RefreshUsbButton.IsEnabled=!busy;UsbScanButton.IsEnabled=!busy && UsbList.SelectedItem is UsbDisk;
        ScanButton.IsEnabled = !busy; ModeBox.IsEnabled = !busy; SectorBox.IsEnabled = !busy;
        CancelButton.IsEnabled = busy; ProgressBar.IsIndeterminate = busy;
        ExportAllButton.IsEnabled = !busy && session?.Files.Count > 0;
        ExportSelectedButton.IsEnabled = !busy && FilesGrid.SelectedItems.Count > 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { operation?.Cancel(); StatusText.Text = "Deteniendo el motor y guardando el estado…"; }
    private void Acquisition_Click(object sender, RoutedEventArgs e)
    {
        if(busy) return;
        try
        {
            var folder=settings?.AcquisitionFolder ?? throw new IOException("No hay una copia asistida configurada. Abre una imagen existente o prepara una solicitud de adquisición.");
            Safety.RequireInternalDisk(folder);
            var progressPath=Path.Combine(folder,"progress.json");
            if(!File.Exists(progressPath)) throw new IOException("La copia todavía no ha publicado su progreso.");
            var p=JsonSerializer.Deserialize<AcquisitionProgress>(File.ReadAllText(progressPath),RecoveryEngine.Json) ?? throw new IOException("Progreso no válido.");
            if(p.Status=="Completa")
            {
                image=Safety.ImagePath(Path.Combine(folder,"usb.img")); ImageBox.Text=image;
                StatusText.Text="Copia completa cargada. Puedes iniciar el análisis sin acceder a la USB.";
            }
            MessageBox.Show(this,$"Estado: {p.Status}\n\nLeído: {p.ReadBytes/1e9:N2} / {p.Total/1e9:N2} GB\n"+
                $"Bloques con error: {p.FailedBytes:N0} bytes\nPendiente: {p.PendingBytes:N0} bytes\n\nCarpeta: {folder}\n\n"+
                "La copia la realiza un proceso separado. Cancelar operación en esta ventana cancela el análisis, no la adquisición.","Copia de tu memoria",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex) { Error(ex); }
    }
    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        var current = session is null ? "Aún no hay un análisis de Rescate USB." : $"Sesión actual: {session.Files.Count:N0} entradas ({session.Mode}).";
        MessageBox.Show(this,
            "Referencia de Recoverit · captura del 8 de octubre de 2026\n\n" +
            "Total mostrado: 11,515 archivos · 31.11 GB\n" +
            "Fotos: 149\nDocumentos: 7,667\nVídeos: 12\nCorreos electrónicos: 1\nAudio: 153\nOtros: 3,508\n\n" +
            "Las categorías suman 11,490: diferencia de 25 respecto al total.\n" +
            "El tamaño mostrado es agregado; no demuestra 31.11 GB de datos únicos.\n\n" + current +
            "\n\nLos conteos de motores distintos no equivalen a archivos íntegros ni únicos. Para comparar contenido usa carpetas de archivos exportados.",
            "Referencia aportada por el usuario", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Reviewed_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder=Safety.LocalPath(settings?.LatestReviewFolder ?? throw new IOException("No hay una revisión configurada."));
            Safety.RequireInternalDisk(folder);
            if(!Directory.Exists(folder) || !File.Exists(Path.Combine(folder,"resumen-final.json")))
                throw new IOException("No se encontró una revisión terminada en esa carpeta.");
            var byType=Path.Combine(folder,"00_Por_tipo");
            if(File.Exists(Path.Combine(byType,"catalogo.csv"))) folder=byType;
            var start=new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute=false };
            start.ArgumentList.Add(folder);
            System.Diagnostics.Process.Start(start);
            StatusText.Text="Archivos por tipo abiertos. Las carpetas originales parciales están en el directorio superior. La tabla muestra los hallazgos iniciales del motor.";
        }
        catch(Exception ex) { Error(ex); }
    }
    private void Feedback_Click(object sender,RoutedEventArgs e)
    {
        try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://revivo.digital/#feedback"){UseShellExecute=true});}
        catch(Exception ex){Error(new IOException("Abre https://revivo.digital/#feedback en tu navegador. "+ex.Message));}
    }
    private void MoreOptions_Click(object sender,RoutedEventArgs e)
    {
        if(sender is System.Windows.Controls.Button button && button.ContextMenu is { } menu)
        { menu.PlacementTarget=button;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Right;menu.IsOpen=true; }
    }
    private void ReviewBrowser_Click(object sender,RoutedEventArgs e)
    {
        try { if(ReviewHost.Content is ReviewWindow)WorkspaceTabs.SelectedItem=ReviewTab;else OpenReview(); }
        catch(Exception ex) { Error(ex); }
    }
    public void OpenReview(string? catalog=null)
    {
        catalog??=settings?.LatestPreviewCatalog??throw new IOException("Todavía no hay vistas preparadas. Termina un escaneo para generarlas.");
        ReviewHost.Content=new ReviewWindow(catalog);WorkspaceTabs.SelectedItem=ReviewTab;ResultsNav.IsEnabled=true;
    }
    public async Task VerifyIntegratedFlow(string demo,string destination)
    {
        OutputBox.Text=destination;
        await SearchAutomatically(demo,destination,512,new Progress<ScanProgress>(),CancellationToken.None,false);
        if(session is null || session.Files.Count==0)throw new IOException("La prueba no encontró archivos");
        if(ReviewHost.Content is not ReviewWindow review || WorkspaceTabs.SelectedItem!=ReviewTab)throw new IOException("No se abrió la revisión integrada");
        review.VerifyFilters();
        await File.WriteAllTextAsync(Path.Combine(destination,"integrated-test.json"),JsonSerializer.Serialize(new {Passed=true,ScanFiles=session.Files.Count,ExportFolder=lastExport,Catalog=settings?.LatestPreviewCatalog}));
    }
    private async Task PrepareReview(IProgress<ScanProgress> progress,CancellationToken token,bool persist=true)
    {
        if(session is null || sessionDir is null)return;
        var preparation=new Progress<ScanProgress>(p=>progress.Report(p with {Message="Paso 3 de 3: Preparando archivos y vistas · "+p.Message}));
        lastExport=await engine.Export(session,sessionDir,session.Files,OutputBox.Text,preparation,token);
        await ValidateAndPreview(lastExport,Path.Combine(lastExport,"vistas-integradas"),progress,token);
        var catalog=Path.Combine(lastExport,"vistas-integradas","catalog.json");
        settings=(settings??new LocalSettings(OutputBox.Text,null)) with {LatestPreviewCatalog=catalog,LatestSessionFile=Path.Combine(sessionDir,"session.json")};
        if(persist)await SavePreferences();
        OpenReview(catalog);StatusText.Text="Validación terminada. Revisa el estado de cada archivo; una estructura válida no garantiza contenido completo.";
        if(persist){var found=JsonSerializer.Deserialize<List<ReviewEntry>>(await File.ReadAllTextAsync(catalog),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})?.Count??0;
            CompletionWindow.Show(this,"Búsqueda terminada",$"Tienes {found:N0} archivos para revisar.\n\nHaz doble clic en un nombre para ver su contenido y marca las casillas de los archivos que quieras guardar.\n\nEncontrar un archivo no garantiza que su contenido esté completo.","Revisar archivos");}
    }
    private async Task ValidateAndPreview(string export,string catalogFolder,IProgress<ScanProgress> progress,CancellationToken token)
    {
        var python=Path.Combine(root,"python","python.exe");
        if(!File.Exists(python))python=settings?.PythonExecutable??"C:\\Python314\\python.exe";
        if(!File.Exists(python))throw new IOException("Los archivos se guardaron, pero falta el componente Python de vistas previas en esta instalación.");
        progress.Report(new("Paso 3 de 3: Comprobando archivos y preparando vistas de los archivos recuperados…"));
        var start=new System.Diagnostics.ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
        start.Environment["PYTHONUTF8"]="1";start.Environment["PYTHONIOENCODING"]="utf-8";
        start.ArgumentList.Add(Path.Combine(root,"scripts","preview_export.py"));start.ArgumentList.Add(export);start.ArgumentList.Add(catalogFolder);
        using var process=System.Diagnostics.Process.Start(start)??throw new IOException("No se pudo iniciar el visor");
        using var registration=token.Register(()=>{try{process.Kill(true);}catch(InvalidOperationException){}});
        var stderr=process.StandardError.ReadToEndAsync(token);
        while(await process.StandardOutput.ReadLineAsync(token) is {} line)progress.Report(new("Paso 3 de 3: Comprobando archivos y preparando vistas · "+line));
        await process.WaitForExitAsync(token);var error=await stderr;
        if(process.ExitCode!=0)
        {
            var details=Path.Combine(export,"error-vistas.txt");
            try { await File.WriteAllTextAsync(details,error,CancellationToken.None); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
            throw new IOException("No se completaron las vistas. Los archivos recuperados permanecen en: " + export +
                "\nPuedes reintentarlo en Abrir recuperación.\nDetalles: " + details);
        }
    }
    public void VerifyResumedReview()
    {
        if(session is not null || image is not null)throw new IOException("La reapertura inició un escaneo");
        if(ReviewHost.Content is not ReviewWindow review)throw new IOException("Falta la revisión");
        review.VerifyFilters();
    }
    private void OpenResultsFolder_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var folder=Safety.LocalPath(lastExport??settings?.AcquisitionFolder??OutputBox.Text);
            if(!Directory.Exists(folder))throw new IOException("Todavía no hay una carpeta guardada en este destino.");
            var start=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add(folder);System.Diagnostics.Process.Start(start);
        }catch(Exception ex){Error(ex);}
    }
    private async void ResumeRecovery_Click(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        var dialog=new OpenFolderDialog{Title="Elige la carpeta de tu recuperación; también puedes elegir la carpeta general Revivo",InitialDirectory=Directory.Exists(OutputBox.Text)?OutputBox.Text:null};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var folder=Safety.LocalPath(dialog.FolderName);Safety.RequireInternalDisk(folder);
            var choices=await Task.Run(()=>SavedRecovery.Find(folder));
            if(choices.Count==0)throw new IOException("No se encontró una recuperación aquí. Elige la carpeta donde guardaste la copia y los resultados, o usa Elegir copia si tienes un archivo .img.");
            SavedRecovery? selected=choices.Count==1?choices[0]:ChooseRecovery(choices);
            if(selected is null)return;
            if(!selected.HasFiles)
            {
                image=Safety.ImagePath(Path.Combine(selected.Folder,"usb.img"));ImageBox.Text=image;OutputBox.Text=selected.Folder;WorkspaceTabs.SelectedIndex=0;SavedImageSection.IsExpanded=true;
                var mapFile=image+".map.json";
                var map=File.Exists(mapFile)?JsonSerializer.Deserialize<AcquisitionMap>(File.ReadAllText(mapFile),RecoveryEngine.Json):null;
                SectorBox.SelectedIndex=map?.SectorSize==4096?1:0;
                SavedImageSection.BringIntoView();
                StatusText.Text="Encontramos la copia de la USB. Pulsa Buscar en la copia para recuperar sus archivos sin conectar la memoria.";
                MessageBox.Show(this,StatusText.Text,"Copia encontrada",MessageBoxButton.OK,MessageBoxImage.Information);return;
            }
            var catalogs=Directory.EnumerateDirectories(selected.Folder).Where(x=>(File.GetAttributes(x)&FileAttributes.ReparsePoint)==0 && Path.GetFileName(x).StartsWith("vistas",StringComparison.OrdinalIgnoreCase))
                .Select(x=>Path.Combine(x,"catalog.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            if(catalogs.Count>0)
            {
                lastExport=selected.Folder;settings=(settings??new LocalSettings(OutputBox.Text,null)) with {LatestPreviewCatalog=catalogs[0]};
                OpenReview(catalogs[0]);StatusText.Text="Recuperación abierta. Puedes revisar y guardar tus archivos sin conectar la USB.";return;
            }
            await RunOperation(async(progress,token)=>await ResumeRecovery(selected.Folder,progress,token));
        }catch(Exception ex){Error(ex);}
    }
    private SavedRecovery? ChooseRecovery(List<SavedRecovery> choices)
    {
        var picker=new Window{Owner=this,Title="¿Qué recuperación quieres abrir?",Width=Math.Min(760,ActualWidth),Height=Math.Min(460,ActualHeight),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground};
        var panel=new DockPanel{Margin=new Thickness(18)};picker.Content=panel;
        var open=new Button{Content="Abrir selección",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(open,Dock.Bottom);panel.Children.Add(open);
        var list=new ListBox{ItemsSource=choices,DisplayMemberPath="Label",SelectedIndex=0};panel.Children.Add(list);
        open.Click+=(_,_)=>picker.DialogResult=true;
        return picker.ShowDialog()==true?list.SelectedItem as SavedRecovery:null;
    }
    public async Task ResumeRecovery(string export,IProgress<ScanProgress> progress,CancellationToken token)
    {
        export=Safety.LocalPath(export);Safety.RequireInternalDisk(export);
        if(!File.Exists(Path.Combine(export,"informe.json")))
            throw new IOException("Selecciona la carpeta recuperados- que contiene informe.json, no la carpeta USB que la contiene.");
        var output=Path.Combine(export,"vistas-"+Guid.NewGuid().ToString("N")[..8]);
        await ValidateAndPreview(export,output,progress,token);
        token.ThrowIfCancellationRequested();
        lastExport=export;
        settings=(settings??new LocalSettings(OutputBox.Text,null)) with {LatestPreviewCatalog=Path.Combine(output,"catalog.json")};
        OpenReview(settings.LatestPreviewCatalog);
        StatusText.Text="Vistas preparadas desde los archivos guardados. No se volvió a leer la USB.";
    }
    private async void ValidateFiles_Click(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        await RunOperation(async(progress,token)=>
        {
            var previous=settings?.LatestPreviewCatalog??throw new IOException("Primero termina una recuperación.");
            var priorFolder=Path.GetDirectoryName(Safety.LocalPath(previous))!;
            var export=Path.GetDirectoryName(priorFolder)!;
            Safety.RequireInternalDisk(export);
            if(!File.Exists(Path.Combine(export,"informe.json")))throw new IOException("Esta colección antigua no tiene un informe de exportación compatible. Las nuevas recuperaciones se validan automáticamente.");
            var output=Path.Combine(export,"vistas-validadas-"+Guid.NewGuid().ToString("N"));
            await ValidateAndPreview(export,output,progress,token);
            token.ThrowIfCancellationRequested();
            var decisions=Path.Combine(priorFolder,"revision-personal.json");
            if(File.Exists(decisions))File.Copy(decisions,Path.Combine(output,"revision-personal.json"));
            settings=settings! with {LatestPreviewCatalog=Path.Combine(output,"catalog.json")};
            await SavePreferences();
            OpenReview(settings.LatestPreviewCatalog);
            StatusText.Text="Validación terminada. Se conservaron los archivos originales y la revisión anterior.";
        });
    }
    private void Diagnostic_Click(object sender,RoutedEventArgs e)
    {if(!busy)new DiagnosticWindow(root,settings?.AcquisitionFolder){Owner=this}.ShowDialog();}
    private void AfterRecovery_Click(object sender,RoutedEventArgs e)
    {
        if(busy)return;
        if(settings?.AcquisitionFolder is null || settings.LatestReviewFolder is null){Error(new IOException("Primero guarda una imagen y termina la revisión de los archivos."));return;}
        new AfterRecoveryWindow(root,settings.AcquisitionFolder,settings.LatestReviewFolder){Owner=this}.ShowDialog();
    }
    private void Warnings_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        session is null ? "Todavía no hay una sesión." : session.Warnings.Count == 0 ? "No se registraron errores del motor. Esto no certifica la integridad de los archivos." : string.Join("\n\n", session.Warnings),
        "Avisos del análisis", MessageBoxButton.OK, MessageBoxImage.Information);
    private string? PickFolder(string title, string? initial = null)
    { var dialog = new OpenFolderDialog { Title = title }; if (initial is not null) dialog.InitialDirectory = initial; return dialog.ShowDialog(this) == true ? dialog.FolderName : null; }
    private void Error(Exception ex) { StatusText.Text = ex.Message; MessageBox.Show(this, ex.Message, "No se pudo completar la operación", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void OnClosing(object? sender, CancelEventArgs e)
    { if (busy) { e.Cancel = true; operation?.Cancel(); StatusText.Text = "Guardando resultados. Cierra la ventana cuando termine la cancelación."; } }
}

internal sealed record LocalSettings(string? ResultsFolder,string? AcquisitionFolder,string? LatestSessionFile=null,string? LatestReviewFolder=null,string? LatestPreviewCatalog=null,string? PythonExecutable=null);

