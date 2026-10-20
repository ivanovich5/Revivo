using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
namespace Rescate.Desktop;
internal static class CompletionWindow
{
    public static void Show(Window owner,string title,string message,string action,string? folder=null,string? reviewFolder=null)
    {
        var window=new Window{Owner=owner,Title="REVIVO-USB · "+title,Width=620,SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(21,19,46)),Foreground=Brushes.White};
        var panel=new StackPanel{Margin=new Thickness(24)};window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text=title,FontSize=24,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,20)});
        var button=new Button{Content=action,IsDefault=true};panel.Children.Add(button);
        button.Click+=(_,_)=>{
            if(folder is not null){try{var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};start.ArgumentList.Add(Rescate.Core.Safety.LocalPath(folder));Process.Start(start);}catch(Exception ex){MessageBox.Show(window,"No se pudo abrir la carpeta: "+ex.Message);return;}}
            window.Close();
        };
        if(reviewFolder is not null)
        {
            try
            {
                var plan=Rescate.Core.ImageCleanup.Find(reviewFolder);
                if(plan is not null)
                {
                    panel.Children.Add(new TextBlock{Text=$"La imagen ocupa {plan.Bytes/1e9:N2} GB. Puedes conservarla para repetir el análisis. Eliminarla no borra los archivos recuperados ni los informes.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,10)});
                    var cleanup=new Button{Content=$"Liberar {plan.Bytes/1e9:N2} GB: eliminar imagen",IsDefault=false};panel.Children.Add(cleanup);
                    cleanup.Click+=(_,_)=>
                    {
                        if(MessageBox.Show(window,$"¿Eliminar permanentemente esta imagen?\n{plan.Image}\n\nYa no podrás repetir el análisis desde esta copia. Los archivos recuperados, las vistas y los informes se conservarán. Hazlo solo cuando hayas revisado y respaldado lo importante.","Eliminar imagen de la USB",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                        try{Rescate.Core.ImageCleanup.DeleteConfirmed(plan);cleanup.IsEnabled=false;cleanup.Content="Imagen eliminada; archivos conservados";}
                        catch(Exception ex){MessageBox.Show(window,"No se eliminó la imagen: "+ex.Message);}
                    };
                }
            }
            catch(System.Exception){ /* Ineligible or missing acquisition: never offer an inferred deletion. */ }
        }
        window.ShowDialog();
    }
}
