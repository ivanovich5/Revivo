using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
namespace Rescate.Desktop;
internal static class CompletionWindow
{
    public static void Show(Window owner,string title,string message,string action,string? folder=null)
    {
        var window=new Window{Owner=owner,Title="REVIVO · "+title,Width=620,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(21,19,46)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(24)};window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text=title,FontSize=24,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,20)});
        var button=new Button{Content=action,IsDefault=true};panel.Children.Add(button);
        button.Click+=(_,_)=>{
            if(folder is not null){try{var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add(Rescate.Core.Safety.LocalPath(folder));Process.Start(start);}catch(Exception ex){MessageBox.Show(window,"No se pudo abrir la carpeta: "+ex.Message);return;}}
            window.Close();
        };
        window.ShowDialog();
    }
}
