using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rescate.Desktop;

public sealed class SaveProgressWindow : Window
{
    private readonly ProgressBar bar=new(){Height=16,Minimum=0,Maximum=100,Margin=new Thickness(0,16,0,12)};
    private readonly TextBlock status=new(){Text="Preparando el respaldo…",TextWrapping=TextWrapping.Wrap};
    public bool Finished {get;set;}
    public SaveProgressWindow()
    {
        Title="Revivo · Guardando archivos";Width=570;Height=210;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(21,19,46));Foreground=new SolidColorBrush(Color.FromRgb(238,237,254));
        var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="Guardando tu selección",FontSize=23,FontFamily=(FontFamily)Application.Current.FindResource("TitleFont")});panel.Children.Add(bar);panel.Children.Add(status);Content=panel;
        Closing+=(_,e)=>{if(!Finished)e.Cancel=true;};
    }
    public void Report(double percent,string message){bar.Value=percent;status.Text=$"{percent:N0}% · {message}";}
    public static string? AskName(Window owner,int count,long bytes,string destination)
    {
        var window=new Window{Owner=owner,Title="Revivo · Nombre del respaldo",Width=480,Height=330,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(21,19,46)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(24)};window.Content=panel;
        panel.Children.Add(new TextBlock{Text="Nombre de la carpeta para tus archivos",FontSize=16});
        panel.Children.Add(new TextBlock{Text=$"{count:N0} archivos · {bytes/1048576.0:N1} MB\nDestino: {destination}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
        var name=new TextBox{Text="Archivos recuperados",Margin=new Thickness(0,12,0,12)};panel.Children.Add(name);
        var error=new TextBlock{Foreground=Brushes.Salmon};panel.Children.Add(error);
        var button=new Button{Content="Comenzar respaldo",IsDefault=true};panel.Children.Add(button);
        button.Click+=(_,_)=>{try{ReviewWindow.ValidateFolderName(name.Text);window.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
        return window.ShowDialog()==true?name.Text.Trim():null;
    }
}
