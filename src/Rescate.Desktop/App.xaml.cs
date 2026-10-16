using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rescate.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if(e.Args.Length==2 && e.Args[0]=="--test-discovery")
        {
            try
            {
                var test=Path.Combine(e.Args[1],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(test);
                var oldCase=Path.Combine(test,"USB-antigua-nombre-largo");Directory.CreateDirectory(oldCase);File.WriteAllBytes(Path.Combine(oldCase,"usb.img"),new byte[512]);
                var files=Path.Combine(oldCase,"recuperados-nombre-largo");Directory.CreateDirectory(files);File.WriteAllText(Path.Combine(files,"informe.json"),"{}");
                var imageOnly=Path.Combine(test,"USB-solo-imagen");Directory.CreateDirectory(imageOnly);File.WriteAllBytes(Path.Combine(imageOnly,"usb.img"),new byte[512]);
                var found=SavedRecovery.Find(test);
                if(found.Count!=2 || found.Count(x=>x.HasFiles)!=1 || found.Any(x=>x.Folder==oldCase))throw new IOException("Detección incorrecta");
                if(SavedRecovery.Find(files).Single().Folder!=files || SavedRecovery.Find(oldCase).Single().Folder!=files)throw new IOException("Reapertura de carpeta antigua incorrecta");
                var first=Rescate.Core.Safety.CreateOutput(test,"recuperados");var second=Rescate.Core.Safety.CreateOutput(test,"recuperados");
                if(first==second || !Path.GetFileName(first).StartsWith("Archivos-") || Path.GetFileName(first).Length>20)throw new IOException("Nombre largo o repetido");
                File.WriteAllText(Path.Combine(e.Args[1],"discovery.txt"),"PASS: carpetas antiguas, carpeta general, imagen sola, prioridad de archivos y nombres cortos distintos.");Shutdown(0);
            }catch(Exception ex){File.WriteAllText(Path.Combine(e.Args[1],"error.txt"),ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==2 && e.Args[0]=="--test-time-estimate")
        {
            try {
                var estimate=new CopyTimeEstimate();
                if(!estimate.Update(100,1000,0,0).Contains("Calculando"))throw new Exception("Estimación prematura");
                if(!estimate.Update(400,1000,0,30).Contains("aproximadamente 1 min"))throw new Exception("Estimación incorrecta");
                if(!estimate.Update(400,1000,0,46).Contains("no disponible"))throw new Exception("Lectura detenida sin aviso");
                if(!estimate.Update(500,1000,1,47).Contains("no disponible"))throw new Exception("Errores sin aviso");
                if(!estimate.Update(1000,1000,0,60).Contains("falta finalizar"))throw new Exception("Fin incorrecto");
                File.WriteAllText(e.Args[1],"PASS: inicio, velocidad, estancamiento, errores y final.");Shutdown(0);
            }catch(Exception ex){File.WriteAllText(e.Args[1],ex.ToString());Shutdown(1);}return;
        }
        if(e.Args.Length==2 && e.Args[0]=="--test-layout")
        {
            try
            {
                Directory.CreateDirectory(e.Args[1]);
                var view=(System.Windows.Controls.ScrollViewer)window.Content;
                foreach(var scale in new[]{1.0,1.25,1.5,2.0})
                {
                    var size=WindowSizing.FitSize(1380,900,1366,728,scale,scale);
                    if(size.Width>1366/scale || size.Height>728/scale)throw new IOException("Ventana fuera del área útil");
                    view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();
                    if(view.ViewportHeight>size.Height || view.ViewportWidth>size.Width)throw new IOException("Viewport fuera de ventana");
                    if(size.Height<640 && view.ScrollableHeight<=0)throw new IOException("No se puede llegar a la parte inferior");
                    if(size.Width<780 && view.ScrollableWidth<=0)throw new IOException("No se puede llegar al extremo derecho");
                    view.ScrollToBottom();view.ScrollToRightEnd();view.UpdateLayout();
                    if(Math.Abs(view.VerticalOffset-view.ScrollableHeight)>1)throw new IOException("Desplazamiento incompleto");
                    view.ScrollToTop();view.ScrollToLeftEnd();view.UpdateLayout();
                    var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(e.Args[1],"escala-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));png.Save(file);
                }
                File.WriteAllText(Path.Combine(e.Args[1],"resultado.txt"),"PASS: 100%, 125%, 150%, 200%; tamaño y acceso por desplazamiento.");Shutdown(0);
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(e.Args[1],"error.txt"),ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==3 && e.Args[0]=="--test-resume")
        {
            try { await window.ResumeRecovery(e.Args[1],new Progress<Rescate.Core.ScanProgress>(),CancellationToken.None); window.VerifyResumedReview(); File.WriteAllText(e.Args[2],"Resume: PASS");Shutdown(0); }
            catch(Exception ex){File.WriteAllText(e.Args[2],ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==2 && e.Args[0]=="--test-startup")
        {
            try { window.VerifyFreshStart();File.WriteAllText(e.Args[1],"Fresh session: PASS");Shutdown(0); }
            catch(Exception ex){File.WriteAllText(e.Args[1],ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==3 && e.Args[0]=="--test-batch")
        {
            try{var review=new ReviewWindow(e.Args[1]);review.VerifyFilters();await review.VerifyBatchExport(e.Args[2]);Shutdown(0);}
            catch(Exception ex){File.WriteAllText(Path.Combine(e.Args[2],"error.txt"),ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==2 && e.Args[0] is "--render-home" or "--render-disabled-usb")
        {
            try
            {
                if(e.Args[0]=="--render-disabled-usb"){
                    var list=(System.Windows.Controls.ListBox)window.FindName("UsbList");
                    list.ItemsSource=new[]{new Rescate.Core.UsbDisk(99,"USB de prueba", "TEST","TEST",61870000000,"Unknown","Unknown",false,false,"E:")};list.SelectedIndex=0;list.IsEnabled=false;
                }else await window.RefreshUsb();
                var view=(FrameworkElement)window.Content;view.Measure(new Size(1360,850));view.Arrange(new Rect(0,0,1360,850));view.UpdateLayout();
                var bitmap=new RenderTargetBitmap(1360,850,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(e.Args[1]);png.Save(file);Shutdown(0);
            }catch(Exception ex){File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==4 && e.Args[0]=="--test-integrated")
        {
            try
            {
                await window.VerifyIntegratedFlow(e.Args[1],e.Args[2]);
                var view=(FrameworkElement)window.Content;view.Measure(new Size(1400,900));view.Arrange(new Rect(0,0,1400,900));view.UpdateLayout();
                var bitmap=new RenderTargetBitmap(1400,900,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(e.Args[3]);png.Save(file);Shutdown(0);
            }catch(Exception ex){File.WriteAllText(e.Args[3]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length is 3 or 4 && e.Args[0]=="--render-review")
        {
            try
            {
                var review=new ReviewWindow(e.Args[1]);review.VerifyFilters();
                if(e.Args.Length==4)review.InspectType(e.Args[3]);
                var view=(FrameworkElement)review;view.Measure(new Size(1200,780));view.Arrange(new Rect(0,0,1200,780));view.UpdateLayout();
                var bitmap=new RenderTargetBitmap(1200,780,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(e.Args[2]);png.Save(file);Shutdown(0);
            }
            catch(Exception ex){File.WriteAllText(e.Args[2]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if(e.Args.Length==2 && e.Args[0] is "--render-diagnostic" or "--render-after")
        {
            try
            {
                var project=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
                LocalSettings? config=null;
                if(File.Exists(Path.Combine(project,"settings.local.json")))config=System.Text.Json.JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(Path.Combine(project,"settings.local.json")));
                Window diagnostic=e.Args[0]=="--render-after"
                    ?new AfterRecoveryWindow(project,config?.AcquisitionFolder??throw new IOException("Sin caso"),config.LatestReviewFolder??throw new IOException("Sin revisión"))
                    :new DiagnosticWindow(project,null);
                var view=(FrameworkElement)diagnostic.Content;view.Measure(new Size(780,680));view.Arrange(new Rect(0,0,780,680));view.UpdateLayout();
                var bitmap=new RenderTargetBitmap(780,680,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(e.Args[1]);png.Save(file);Shutdown(0);
            }
            catch(Exception ex){File.WriteAllText(e.Args[1]+".error.txt",ex.ToString());Shutdown(1);}
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--render")
        {
            try
            {
                await window.LoadSession(e.Args[1]);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1320, 820));
                content.Arrange(new Rect(0, 0, 1320, 820));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1320, 820, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(e.Args[2]); encoder.Save(file);
                Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(e.Args[2] + ".error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        window.VerifyFreshStart();
        window.Show();
    }
}
