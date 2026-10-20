using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rescate.Core;

namespace Rescate.Desktop;

public sealed class ReviewWindow : UserControl
{
    private readonly List<ReviewEntry> entries;
    private readonly string folder;
    private readonly TextBox search=new(){MinWidth=230,Margin=new Thickness(0,0,10,0)};
    private readonly ComboBox type=new(){MinWidth=130,Margin=new Thickness(0,0,10,0)};
    private readonly ComboBox state=new(){MinWidth=195,Margin=new Thickness(0,0,10,0)};
    private readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,CanUserAddRows=false};
    private readonly TextBlock count=new(){Margin=new Thickness(0,6,0,6)};
    private readonly TextBox description=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=145};
    private readonly TextBox text=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly Image image=new(){Stretch=Stretch.Uniform};
    private readonly ScrollViewer imageScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ComboBox zoom=new(){Width=140,Margin=new Thickness(10,0,0,0)};
    private void FitImage()
    {
        if(image.Source is not BitmapSource bitmap)return;
        var w=Math.Max(1,imageScroll.ActualWidth-20);var h=Math.Max(1,imageScroll.ActualHeight-20);
        var scale=zoom.SelectedIndex switch {1=>w/bitmap.PixelWidth,2=>1.5,3=>2.0,_=>Math.Min(w/bitmap.PixelWidth,h/bitmap.PixelHeight)};
        image.Width=bitmap.PixelWidth*scale;image.Height=bitmap.PixelHeight*scale;
    }
    private readonly TextBlock pageLabel=new(){VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,10,0)};
    private bool changingChecks;
    private int page;
    private ReviewEntry? selected;
    private readonly TabControl tabs=new();
    private readonly DockPanel sheetPanel=new();
    private readonly StackPanel pageNav=new(){Orientation=Orientation.Horizontal};
    private readonly ComboBox sheets=new(){Margin=new Thickness(6)};
    private readonly DataGrid cells=new(){IsReadOnly=true,AutoGenerateColumns=true,CanUserAddRows=false,EnableRowVirtualization=true};
    private readonly StackPanel audioPanel=new(){Margin=new Thickness(15)};
    private readonly MediaPlayer player=new();
    private readonly TextBlock audioStatus=new(){Text="Pulsa Reproducir para escuchar el WAV. No se reproduce automáticamente.",TextWrapping=TextWrapping.Wrap};
    private void SetupExtraViews()
    {
        DockPanel.SetDock(sheets,Dock.Top);sheetPanel.Children.Add(sheets);sheetPanel.Children.Add(cells);
        sheets.SelectionChanged+=(_,_)=>
        {
            if(sheets.SelectedItem is not SheetPreview sheet){cells.ItemsSource=null;return;}
            var table=new System.Data.DataTable();table.Columns.Add("Fila");
            for(int i=0;i<30;i++)table.Columns.Add(i<26?((char)('A'+i)).ToString():"A"+(char)('A'+i-26));
            foreach(var row in sheet.Rows)table.Rows.Add(row.Take(31).Cast<object>().ToArray());
            cells.ItemsSource=table.DefaultView;
        };
        sheets.DisplayMemberPath="Name";audioPanel.Children.Add(audioStatus);
        var play=new Button{Content="Reproducir",Margin=new Thickness(0,12,0,6)};
        play.Click+=async(_,_)=>
        {
            var entry=selected;if(entry is null || entry.Group!="Audio_WAV")return;
            try
            {
                play.IsEnabled=false;audioStatus.Text="Comprobando el archivo…";
                Safety.RequireLocalDisk(entry.Path);
                if(!string.Equals(await RecoveryEngine.HashFile(entry.Path,CancellationToken.None),entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("El archivo cambió desde la revisión.");
                if(selected!=entry || !IsVisible)return;
                player.Open(new Uri(entry.Path));player.Play();audioStatus.Text="Reproduciendo WAV. Puedes detenerlo en cualquier momento.";
            }catch(Exception ex){audioStatus.Text="No se pudo reproducir: "+ex.Message;}finally{play.IsEnabled=true;}
        };
        audioPanel.Children.Add(play);var stop=new Button{Content="Detener"};stop.Click+=(_,_)=>{player.Stop();audioStatus.Text="Reproducción detenida.";};audioPanel.Children.Add(stop);
        player.MediaFailed+=(_,e)=>audioStatus.Text="El reproductor no pudo leer el audio: "+e.ErrorException.Message;
        player.MediaEnded+=(_,_)=>audioStatus.Text="Fin del audio.";
    }
    private void SelectView()
    {
        player.Close();sheets.ItemsSource=selected?.Sheets;sheets.SelectedIndex=0;
        if(selected is null)return;
        pageNav.Visibility=selected.Images.Count>0?Visibility.Visible:Visibility.Collapsed;
        ((TabItem)tabs.Items[0]).IsEnabled=selected.Images.Count>0;
        ((TabItem)tabs.Items[2]).IsEnabled=selected.Sheets?.Count>0;
        ((TabItem)tabs.Items[3]).IsEnabled=selected.Group=="Audio_WAV";
        audioStatus.Text="Pulsa Reproducir para escuchar el WAV. No se reproduce automáticamente.";
        tabs.SelectedIndex=selected.Group=="Audio_WAV"?3:selected.Sheets?.Count>0?2:selected.Images.Count>0?0:1;
    }
    private static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};

    public ReviewWindow(string catalog)
    {
        Safety.RequireLocalDisk(catalog); folder=Path.GetDirectoryName(Path.GetFullPath(catalog))!;
        entries=JsonSerializer.Deserialize<List<ReviewEntry>>(File.ReadAllText(catalog),Json)??[];
        Background=new SolidColorBrush(Color.FromRgb(21,19,46));Foreground=new SolidColorBrush(Color.FromRgb(238,237,254));
        var outer=new DockPanel{Margin=new Thickness(14),Background=Background};Content=outer;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);outer.Children.Add(header);
        var titleBar=new DockPanel();header.Children.Add(titleBar);
        var heading=new TextBlock{Text="Revisa y elige tus archivos",FontFamily=(FontFamily)Application.Current.FindResource("TitleFont"),FontSize=26,FontWeight=FontWeights.SemiBold};
        header.Children.Add(new TextBlock{Text="Haz doble clic en el nombre para ampliar la vista. Marca las casillas de los archivos que quieras guardar.",Margin=new Thickness(0,4,0,6)});
        header.Children.Add(new TextBlock{Text=$"Estructura comprobada: {entries.Count(x=>x.Status.StartsWith("01_")):N0} · Con avisos: {entries.Count(x=>x.Status.StartsWith("02_")):N0} · Fragmentos: {entries.Count(x=>x.Status.StartsWith("03_")):N0} · Sin comprobar: {entries.Count(x=>!x.Status.StartsWith("01_")&&!x.Status.StartsWith("02_")&&!x.Status.StartsWith("03_")):N0} · Copias duplicadas agrupadas: {entries.Sum(x=>x.DuplicateCount):N0}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        var save=new Button{Content="Guardar selección",HorizontalAlignment=HorizontalAlignment.Right,Background=new SolidColorBrush(Color.FromRgb(83,74,183)),Foreground=Brushes.White,Margin=new Thickness(0,0,0,12),ToolTip="Guarda únicamente los archivos con su casilla marcada, incluso si cambias de filtro"};
        save.Click+=async(_,_)=>{save.IsEnabled=false;try{await SaveFiles();}finally{save.IsEnabled=true;}};DockPanel.SetDock(save,Dock.Right);titleBar.Children.Add(save);titleBar.Children.Add(heading);
        header.Children.Add(new TextBlock{Text="Tipo                           Estado",Margin=new Thickness(0,0,0,5),Foreground=new SolidColorBrush(Color.FromRgb(175,169,236))});
        var filters=new StackPanel{Orientation=Orientation.Horizontal};header.Children.Add(filters);
        search.ToolTip="Buscar nombre, carpeta original o texto de la vista previa";
        filters.Children.Add(type);filters.Children.Add(state);
        type.Items.Add("Todos los tipos");foreach(var value in entries.Select(x=>x.Group).Distinct().Order())type.Items.Add(value);type.SelectedIndex=0;
        foreach(var value in new[]{"Todos los estados","Comprobación superada","Con avisos o daños","Fragmentos","Sin validar","Con extracción parcial"})state.Items.Add(value);state.SelectedIndex=0;
        var selectionTools=new WrapPanel{Margin=new Thickness(0,10,0,0)};
        var all=new Button{Content="Seleccionar todos (filtro)"};all.Click+=(_,_)=>SetChecks(true);selectionTools.Children.Add(all);
        var clear=new Button{Content="Quitar toda la selección"};clear.Click+=(_,_)=>{changingChecks=true;foreach(var entry in entries)entry.IsChecked=false;changingChecks=false;UpdateCount();};selectionTools.Children.Add(clear);
        header.Children.Add(selectionTools);header.Children.Add(count);
        foreach(var entry in entries)entry.PropertyChanged+=(_,_)=>UpdateCount();
        var layout=new Grid();outer.Children.Add(layout);
        grid.RowHeight=32;grid.ColumnHeaderHeight=32;layout.Children.Add(grid);
        var checkFactory=new FrameworkElementFactory(typeof(CheckBox));
        checkFactory.SetBinding(CheckBox.IsCheckedProperty,new System.Windows.Data.Binding("IsChecked"){Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});
        checkFactory.SetBinding(CheckBox.IsEnabledProperty,new System.Windows.Data.Binding("CanSave"));
        checkFactory.SetValue(CheckBox.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        checkFactory.SetValue(CheckBox.VerticalAlignmentProperty,VerticalAlignment.Center);
        checkFactory.SetValue(CheckBox.ToolTipProperty,"Incluir este archivo en el respaldo");
        grid.Columns.Add(new DataGridTemplateColumn{Header="✓",Width=40,CellTemplate=new DataTemplate{VisualTree=checkFactory}});
        foreach(var (title,binding,width) in new[]{("Archivo","Name",260),("Tipo","Group",95),("Estado","StatusLabel",170)})grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new System.Windows.Data.Binding(binding),Width=width});
        grid.Columns[1].MinWidth=260;
        grid.SizeChanged+=(_,_)=>grid.Columns[1].Width=new DataGridLength(Math.Max(260,grid.ActualWidth-330));
        var right=new DockPanel{Margin=new Thickness(16)};
        var info=new StackPanel();DockPanel.SetDock(info,Dock.Top);right.Children.Add(info);info.Children.Add(new Expander{Header="Detalles del archivo y avisos",Content=description,IsExpanded=false});
        var actions=new WrapPanel{Margin=new Thickness(0,8,0,8)};info.Children.Add(actions);
        var open=new Button{Content="Mostrar carpeta",Padding=new Thickness(8)};open.Click+=(_,_)=>OpenFolder();actions.Children.Add(open);
        void OpenPreview()
        {
            var viewer=new Window{Title="REVIVO · Vista previa",Owner=Window.GetWindow(this),WindowState=WindowState.Maximized,Background=Background,Foreground=Foreground};
            viewer.Title="REVIVO · "+selected?.Name;viewer.Content=right;
            try{viewer.ShowDialog();}finally{player.Close();viewer.Content=null;}
        }
        grid.MouseDoubleClick+=(_,e)=>
        {
            var element=e.OriginalSource as DependencyObject;
            while(element is not null && element is not DataGridCell)element=VisualTreeHelper.GetParent(element);
            if(element is DataGridCell cell && cell.Column.DisplayIndex==1 && selected is not null){OpenPreview();e.Handled=true;}
        };
        var nav=pageNav;info.Children.Add(nav);
        var prev=new Button{Content="Anterior"};prev.Click+=(_,_)=>ShowPage(page-1);nav.Children.Add(prev);nav.Children.Add(pageLabel);
        var next=new Button{Content="Siguiente"};next.Click+=(_,_)=>ShowPage(page+1);nav.Children.Add(next);
        tabs.Margin=new Thickness(0,8,0,0);right.Children.Add(tabs);
        imageScroll.Content=image;imageScroll.SizeChanged+=(_,_)=>FitImage();
        foreach(var label in new[]{"Página completa","Ajustar al ancho","150 %","200 %"})zoom.Items.Add(label);
        zoom.SelectedIndex=1;zoom.SelectionChanged+=(_,_)=>FitImage();nav.Children.Add(zoom);
        tabs.Items.Add(new TabItem{Header="Imagen / página",Content=imageScroll});
        tabs.Items.Add(new TabItem{Header="Texto extraído",Content=text});
        tabs.Items.Add(new TabItem{Header="Hoja de cálculo",Content=sheetPanel});
        tabs.Items.Add(new TabItem{Header="Audio",Content=audioPanel});
        SetupExtraViews();
        Unloaded+=(_,_)=>player.Close();
        search.TextChanged+=(_,_)=>Filter();type.SelectionChanged+=(_,_)=>Filter();state.SelectionChanged+=(_,_)=>Filter();
        grid.SelectionChanged+=(_,_)=>Select();Filter();
        var first=entries.FirstOrDefault(x=>x.Status.StartsWith("01_") && x.Group=="PDF" && x.Images.Count>0);
        if(first is not null){grid.SelectedItem=first;grid.ScrollIntoView(first);}
    }
    private void Filter()
    {
        var query=search.Text.Trim();var status=state.SelectedIndex;
        var list=entries.Where(x=>(type.SelectedIndex==0 || x.Group==(string?)type.SelectedItem)
            && (status==0 || status==5 && x.Partial || status is >=1 and <=4 && x.Status.StartsWith($"0{status}_"))
            && (query.Length==0 || (x.Name+" "+x.OriginalPath+" "+x.Text).Contains(query,StringComparison.OrdinalIgnoreCase))).ToList();
        grid.ItemsSource=list;UpdateCount();
        if(list.Count>0)grid.SelectedIndex=0;else{player.Close();selected=null;description.Text="No hay coincidencias.";text.Text="";image.Source=null;pageLabel.Text="";cells.ItemsSource=null;}
    }
    public void VerifyFilters()
    {
        state.SelectedIndex=0;
        SetChecks(true);
        if(entries.Count(x=>x.IsChecked)!=entries.Count(x=>x.CanSave))throw new IOException("Selección múltiple incorrecta");
        var firstChecked=entries.FirstOrDefault(x=>x.IsChecked);
        if(firstChecked is not null){firstChecked.IsChecked=false;if(firstChecked.IsChecked)throw new IOException("No se pudo desmarcar un archivo");}
        var before=entries.Count(x=>x.IsChecked);search.Text=Guid.NewGuid().ToString();SetChecks(false);
        if(entries.Count(x=>x.IsChecked)!=before)throw new IOException("El filtro cambió la selección de archivos ocultos");
        search.Text="";SetChecks(false);
        if(entries.Any(x=>x.IsChecked))throw new IOException("No se pudo quitar la selección");
        if(grid.Items.Count!=entries.Count)throw new IOException("Filtro de todos los archivos incorrecto");
        state.SelectedIndex=5;
        if(grid.Items.Count!=entries.Count(x=>x.Partial))throw new IOException("Filtro de parciales incorrecto");
        state.SelectedIndex=0;search.Text=Guid.NewGuid().ToString();
        if(grid.Items.Count!=0)throw new IOException("Búsqueda incorrecta");
        search.Text="";state.SelectedIndex=1;
        if(grid.Items.Count!=entries.Count(x=>x.Status.StartsWith("01_")))throw new IOException("Filtro de comprobados incorrecto");
        var preview=entries.FirstOrDefault(x=>x.Status.StartsWith("01_") && x.Images.Count>0);
        if(preview is not null){grid.SelectedItem=preview;Select();if(image.Source is null)throw new IOException("La vista previa no se pudo cargar");}
        foreach(var kind in new[]{"DOCX","XLSX","PPTX","Audio_WAV"})
        {
            var entry=entries.FirstOrDefault(x=>x.Group==kind && x.Status.StartsWith("01_"));if(entry is null)continue;
            grid.SelectedItem=entry;Select();
            var expected=kind=="Audio_WAV"?3:kind=="XLSX" && entry.Sheets?.Count>0?2:1;
            if(tabs.SelectedIndex!=expected)throw new IOException("Selección automática de visor incorrecta: "+kind);
            if(expected==2 && cells.ItemsSource is null)throw new IOException("Tabla de Excel vacía");
        }
        if(preview is not null)grid.SelectedItem=preview;
        if(!entries.Any(x=>x.Status.StartsWith("01_")))state.SelectedIndex=0;
    }
    public void InspectType(string kind)
    {
        state.SelectedIndex=0;type.SelectedItem=kind;
        if(grid.Items.Count==0)throw new IOException("No hay archivos del tipo solicitado");
        grid.SelectedIndex=0;Select();
    }
    private void Select()
    {
        selected=grid.SelectedItem as ReviewEntry;if(selected is null)return;
        description.Text=$"{selected.Name}\n{selected.StatusLabel}\n{selected.Reason}\nCopias idénticas adicionales: {selected.DuplicateCount} · Extensión identificada: {selected.IdentifiedExtension ?? "sin determinar"}\n{selected.Note}\nCarpeta original: {(string.IsNullOrEmpty(selected.OriginalPath)?"No reconstruida":selected.OriginalPath)}";
        text.Text=string.IsNullOrWhiteSpace(selected.Text)
            ?selected.Note.StartsWith("Vista no disponible")?"Error al preparar la vista. Puede deberse al archivo o al lector; consulta el aviso de arriba. No se ha certificado que el archivo esté dañado."
            :selected.Group is "DOCX" or "XLSX" or "PPTX" or "PDF" or "Diseno_Illustrator"?"No se obtuvo texto legible en el extracto. Consulta el aviso de arriba y, si está disponible, la vista de imagen."
            :"Este formato no tiene vista de texto compatible. Esto, por sí solo, no indica que el archivo esté dañado."
            :selected.Text;ShowPage(0);SelectView();
    }
    private void ShowPage(int requested)
    {
        image.Source=null;if(selected is null || selected.Images.Count==0){pageLabel.Text="Sin imagen disponible";return;}
        page=Math.Clamp(requested,0,selected.Images.Count-1);pageLabel.Text=$"Vista {page+1} / {selected.Images.Count}";
        try
        {
            var path=Path.GetFullPath(selected.Images[page]);
            if(!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(path)!=".png")throw new IOException("Vista fuera de la carpeta autorizada");
            using var stream=File.OpenRead(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=1100;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();image.Source=bitmap;FitImage();imageScroll.ScrollToTop();imageScroll.ScrollToLeftEnd();
        }catch(Exception ex){pageLabel.Text="Vista no disponible: "+ex.Message;}
    }
    private void OpenFolder()
    {
        if(selected is null)return;
        try
        {
            var path=selected.Partial?selected.DerivativesFolder:Path.GetDirectoryName(selected.Path)!;Safety.RequireLocalDisk(path);
            var start=new System.Diagnostics.ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add(path);System.Diagnostics.Process.Start(start);
        }catch(Exception ex){MessageBox.Show(Window.GetWindow(this),ex.Message);}
    }
    private void UpdateCount()
    {
        if(changingChecks)return;
        var checkedCount=entries.Count(x=>x.IsChecked && x.CanSave);
        var shown=grid.Items.Cast<ReviewEntry>().ToList();
        var hidden=checkedCount-shown.Count(x=>x.IsChecked && x.CanSave);
        count.Text=$"{shown.Count:N0} de {entries.Count:N0} archivos · {checkedCount:N0} seleccionados"+(hidden>0?$" ({hidden:N0} fuera del filtro)":"");
    }
    private void SetChecks(bool value)
    {
        changingChecks=true;
        foreach(var entry in grid.Items.Cast<ReviewEntry>())if(entry.CanSave)entry.IsChecked=value;
        changingChecks=false;
        UpdateCount();
    }
    public static void ValidateFolderName(string name)
    {
        name=name.Trim();
        if(name.Length==0 || name.Length>80 || name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || System.Text.RegularExpressions.Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new IOException("Usa un nombre de 1 a 80 caracteres, sin rutas ni símbolos reservados.");
    }
    private static string SafeExtension(ReviewEntry entry)
    {
        var ext=entry.IdentifiedExtension;
        return ext is ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".jpg" or ".png" or ".gif" or ".wav" or ".zip" or ".epub" or ".txt" ? ext : Path.GetExtension(entry.Name);
    }
    private static async Task<string> ExportFiles(List<ReviewEntry> chosen,string destination,string folderName="Archivos recuperados",Action<double,string>? progress=null)
    {
            Safety.RequireInternalDisk(destination);ValidateFolderName(folderName);
            var clean=folderName.Trim();var output=Path.Combine(destination,clean);int suffix=2;
            while(Directory.Exists(output)||File.Exists(output))output=Path.Combine(destination,$"{clean} ({suffix++})");
            Safety.LocalPath(output);Directory.CreateDirectory(output);
            long total=chosen.Sum(e=>new FileInfo(e.Path).Length);long done=0;int completed=0;
            progress?.Invoke(0,$"Preparando {chosen.Count} archivos");
            var report=new List<object>();
            try { foreach(var entry in chosen)
            {
                Safety.RequireLocalDisk(entry.Path);
                if(!entry.CanSave)throw new IOException("Se excluyen accesos directos y programas de esta exportación.");
                var dir=Path.Combine(output,Safety.SafeName(entry.Group));Directory.CreateDirectory(dir);
                var path=Path.Combine(dir,entry.Sha256[..12]+"_"+Safety.SafeName(Path.GetFileNameWithoutExtension(entry.Name)+SafeExtension(entry)));
                await using(var input=File.OpenRead(entry.Path))
                await using(var file=new FileStream(path+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    var buffer=new byte[1024*1024];int n;long last=Environment.TickCount64;
                    while((n=await input.ReadAsync(buffer))>0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0,n));done+=n;
                        if(Environment.TickCount64-last>100){progress?.Invoke(total>0?Math.Min(99,100.0*done/total):0,$"Copiando {completed+1} de {chosen.Count} · {entry.Name}");last=Environment.TickCount64;}
                    }
                }
                progress?.Invoke(total>0?Math.Min(99,100.0*done/total):0,$"Verificando {completed+1} de {chosen.Count} · {entry.Name}");
                if(!string.Equals(await RecoveryEngine.HashFile(path+".partial",default),entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("La copia de un archivo no coincide. Se conserva con extensión .partial.");
                File.Move(path+".partial",path);completed++;progress?.Invoke(total>0?Math.Min(99,100.0*done/total):0,$"{completed} de {chosen.Count} archivos guardados y verificados");report.Add(new{entry.Name,entry.Status,entry.Sha256,Output=path});
            }
            }
            catch(Exception ex){throw new IOException($"Se guardaron y verificaron {completed} de {chosen.Count} archivos. El proceso se detuvo; los restantes no se completaron.\nCarpeta: {output}\nNo borres los archivos originales. Revisa el espacio disponible y vuelve a intentarlo.\nDetalle: {ex.Message}",ex);}
            await File.WriteAllTextAsync(Path.Combine(output,"informe.json"),JsonSerializer.Serialize(report,Json));
        progress?.Invoke(100,$"{chosen.Count} archivos guardados y verificados");
        return output;
    }
    public async Task VerifyBatchExport(string destination)
    {
        var chosen=entries.Where(x=>x.CanSave && File.Exists(x.Path) && new FileInfo(x.Path).Length<1024*1024).Take(2).ToList();
        if(chosen.Count!=2)throw new IOException("La prueba necesita dos archivos pequeños");
        var values=new List<double>();var output=await ExportFiles(chosen,destination,"Prueba con nombre",(percent,_)=>values.Add(percent));
        if(values.Count<3 || values[0]!=0 || values[^1]!=100 || !values.SequenceEqual(values.Order()))throw new IOException("Progreso incorrecto");
        var second=await ExportFiles(chosen,destination,"Prueba con nombre");if(second==output)throw new IOException("Se sobrescribió una carpeta existente");
        foreach(var bad in new[]{"../escape","CON","a/b","", "final."}){bool rejected=false;try{ValidateFolderName(bad);}catch(IOException){rejected=true;}if(!rejected)throw new IOException("Nombre inseguro aceptado");}
        using var report=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"informe.json")));
        if(report.RootElement.GetArrayLength()!=2)throw new IOException("No se exportó el lote completo");
        await File.WriteAllTextAsync(Path.Combine(destination,"prueba-lote.json"),JsonSerializer.Serialize(new{Passed=true,Count=2,Output=output}));
    }
    private async Task SaveFiles()
    {
        await System.Windows.Threading.Dispatcher.Yield();
        var chosen=entries.Where(x=>x.IsChecked && x.CanSave).DistinctBy(x=>x.Sha256).ToList();
        if(chosen.Count==0){MessageBox.Show(Window.GetWindow(this),"Marca las casillas de los archivos que quieres guardar, o pulsa Seleccionar todos (filtro).");return;}
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title=$"Guardar {chosen.Count} archivo(s) en la computadora"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try
        {
            var owner=Window.GetWindow(this);var folderName=SaveProgressWindow.AskName(owner,chosen.Count,chosen.Sum(x=>new FileInfo(x.Path).Length),dialog.FolderName);if(folderName is null)return;
            string? output=null;Exception? failure=null;
            var progressWindow=new SaveProgressWindow{Owner=owner};
            progressWindow.Loaded+=async(_,_)=>
            {
                try{await System.Windows.Threading.Dispatcher.Yield();output=await ExportFiles(chosen,dialog.FolderName,folderName,progressWindow.Report);}
                catch(Exception ex){failure=ex;}
                finally{progressWindow.Finished=true;progressWindow.Close();}
            };
            progressWindow.ShowDialog();if(failure is not null)throw failure;
            CompletionWindow.Show(owner,"Archivos guardados",$"Se guardaron {chosen.Count:N0} archivos por tipo en:\n{output}\n\nLas copias se verificaron contra los archivos recuperados. Esto no repara daños que ya existían.","Abrir carpeta",output,folder);
        }catch(Exception ex){MessageBox.Show(Window.GetWindow(this),"No se completó la exportación: "+ex.Message);}
    }
}

public sealed record ReviewEntry(string Name,string Group,string Status,string Path,string Sha256,string OriginalPath,string Reason,string Text,List<string> Images,bool Partial,string Note,string DerivativesFolder,List<SheetPreview>? Sheets=null,int DuplicateCount=0,string? IdentifiedExtension=null) : System.ComponentModel.INotifyPropertyChanged
{
    private bool isChecked;
    [System.Text.Json.Serialization.JsonIgnore] public bool CanSave=>Group is not ("Contenido_activo_sin_vista" or "Accesos_o_programas" or "Cambio_detectado");
    [System.Text.Json.Serialization.JsonIgnore] public bool IsChecked {get=>isChecked;set {var next=value && CanSave;if(next==isChecked)return;isChecked=next;PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked)));}}
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string StatusLabel=>Status.StartsWith("01_")?"Comprobación superada":Status.StartsWith("02_")?"Con avisos o daños":Status.StartsWith("03_")?"Fragmentos":"Sin validar";
}

public sealed record SheetPreview(string Name,List<List<string>> Rows);
