using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rescate.Desktop;

public sealed class SaveProgressWindow : Window
{
    private readonly ProgressBar bar=new(){Height=16,Minimum=0,Maximum=100,Margin=new Thickness(0,16,0,12)};
    private readonly TextBlock status=new(){Text="Preparando el respaldo…",TextWrapping=TextWrapping.Wrap};
    private readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
    private readonly CancellationTokenSource cancellation=new();
    private readonly Button cancel=new(){Content="Cancelar",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
    private readonly TextBlock remaining=new(){Text="Calculando tiempo restante…",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0),Foreground=new SolidColorBrush(Color.FromRgb(175,169,236))};
    public CancellationToken Token=>cancellation.Token;
    private void RequestCancel(){cancellation.Cancel();cancel.IsEnabled=false;cancel.Content="Cancelando…";remaining.Text="Deteniendo el guardado; se conservan los archivos ya completados.";}
    public bool Finished {get;set;}
    public SaveProgressWindow()
    {
        Title="REVIVO-USB · Guardando archivos";Width=570;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(21,19,46));Foreground=new SolidColorBrush(Color.FromRgb(238,237,254));
        var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="Guardando tu selección",FontSize=23,FontFamily=(FontFamily)Application.Current.FindResource("TitleFont")});panel.Children.Add(bar);panel.Children.Add(status);panel.Children.Add(remaining);panel.Children.Add(cancel);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        cancel.Click+=(_,_)=>RequestCancel();
        Closing+=(_,e)=>{if(!Finished){e.Cancel=true;RequestCancel();}};
    }
    public void Report(double percent,string message)
    {
        bar.Value=percent;status.Text=$"{percent:N0}% · {message}";
        if(cancellation.IsCancellationRequested)return;
        if(percent>=100){remaining.Text="Guardado terminado";return;}
        if(percent>=99){remaining.Text="Finalizando el informe…";return;}
        if(percent<=0 || clock.Elapsed.TotalSeconds<3){remaining.Text="Calculando tiempo restante…";return;}
        var seconds=clock.Elapsed.TotalSeconds*(100-percent)/percent;
        remaining.Text="Tiempo restante estimado: "+(seconds<60?"menos de 1 minuto":seconds<3600?$"{Math.Ceiling(seconds/60):N0} min":$"{Math.Floor(seconds/3600):N0} h {Math.Ceiling(seconds%3600/60):N0} min")+". Puede variar con la velocidad del dispositivo.";
    }
    public static string? AskName(Window owner,int count,long bytes,string destination)
    {
        var window=new Window{Owner=owner,Title="REVIVO-USB · Nombre del respaldo",Width=480,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(21,19,46)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(24)};window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="Nombre de la carpeta para tus archivos",FontSize=16});
        panel.Children.Add(new TextBlock{Text=$"{count:N0} archivos · {bytes/1048576.0:N1} MB\nDestino: {destination}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        var volume=Rescate.Core.Safety.InspectStorage(destination);
        if(volume.BusType==7 || volume.Removable)
            panel.Children.Add(new TextBlock{Text="La velocidad depende del dispositivo y del puerto USB. Algunas memorias pueden tardar más. Mantén la unidad conectada hasta que termine.",TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(175,169,236)),Margin=new Thickness(0,12,0,0)});
        var name=new TextBox{Text="Archivos recuperados",Margin=new Thickness(0,12,0,12)};panel.Children.Add(name);
        var error=new TextBlock{Foreground=Brushes.Salmon};panel.Children.Add(error);
        var button=new Button{Content="Comenzar respaldo",IsDefault=true};panel.Children.Add(button);
        button.Click+=(_,_)=>{try{ReviewWindow.ValidateFolderName(name.Text);window.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
        return window.ShowDialog()==true?name.Text.Trim():null;
    }
}
