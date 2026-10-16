using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Rescate.Core;

namespace Rescate.Desktop;

public sealed class AfterRecoveryWindow : Window
{
    private readonly string project,caseFolder,reviewFolder;
    private readonly ComboBox disks=new(){Margin=new Thickness(0,8,0,8)};
    private readonly ComboBox mode=new(){Margin=new Thickness(0,8,0,8)};
    private readonly TextBox letter=new(){Text="E",MaxLength=1,Width=65,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly TextBox confirmation=new(){Margin=new Thickness(0,8,0,8)};
    private readonly TextBox detail=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=155};
    private readonly Button prepare=new(){Content="Preparar y verificar respaldos"};
    private readonly Button execute=new(){Content="Formatear la USB seleccionada",IsEnabled=false,Background=Brushes.DarkRed,Foreground=Brushes.White};
    private ReusePlan? plan;private bool busy;
    public AfterRecoveryWindow(string root,string casePath,string reviewPath)
    {
        project=root;caseFolder=casePath;reviewFolder=reviewPath;Title="Después de recuperar · conservar o reutilizar";Width=800;Height=800;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(21,19,46));Foreground=Brushes.White;
        var panel=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=panel,Background=Background,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="¿Qué hacer después de recuperar?",FontSize=23,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Recomendado: conservar los archivos en la computadora. Formatear no repara daños físicos. Los respaldos de la computadora se conservan con cualquier opción.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,12)});
        var keep=new Button{Content="Conservar y abrir archivos en la computadora"};panel.Children.Add(keep);
        keep.Click+=(_,_)=>{var p=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};p.ArgumentList.Add(Safety.LocalPath(Path.Combine(reviewFolder,"00_Por_tipo")));Process.Start(p);};
        var reuse=new StackPanel();panel.Children.Add(new Expander{Header="Opcional: borrar y reutilizar la USB",Content=reuse,IsExpanded=false,Margin=new Thickness(0,18,0,0)});
        reuse.Children.Add(new TextBlock{Text="Opcional: reutilización con borrado",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,18,0,0)});
        reuse.Children.Add(disks);reuse.Children.Add(new TextBlock{Text="Letra actual de la memoria USB:"});reuse.Children.Add(letter);
        mode.Items.Add("Formatear y dejar vacía; conservar archivos en la computadora");mode.Items.Add("Formatear y copiar sólo los archivos con comprobación superada");mode.SelectedIndex=1;reuse.Children.Add(mode);
        reuse.Children.Add(prepare);reuse.Children.Add(detail);
        reuse.Children.Add(new TextBlock{Text="Sólo si decides borrar la memoria, escribe BORRAR seguido de su letra y dos puntos (ejemplo: BORRAR E:). Se perderá el contenido de la USB.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        reuse.Children.Add(confirmation);reuse.Children.Add(execute);
        reuse.Children.Add(new TextBlock{Text="No se certifica la fiabilidad de la USB. No desconectarla durante formateo o copia. No se copian fragmentos, archivos dudosos ni accesos directos. Los datos de la computadora no se eliminan.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        confirmation.TextChanged+=(_,_)=>execute.IsEnabled=!busy && plan is not null && confirmation.Text==ReusePlanner.ConfirmationText(plan.DriveLetter);
        disks.SelectionChanged+=(_,_)=>Invalidate();mode.SelectionChanged+=(_,_)=>Invalidate();letter.TextChanged+=(_,_)=>Invalidate();
        prepare.Click+=async(_,_)=>await Prepare();execute.Click+=async(_,_)=>await Execute();
        Loaded+=async(_,_)=>{try{disks.ItemsSource=(await UsbInventory.List(default)).Where(d=>!d.IsBoot&&!d.IsSystem).ToArray();detail.Text="Selecciona la memoria por modelo y capacidad. No se preparará su formateo hasta terminar el diagnóstico y verificar los respaldos.";}catch(Exception ex){detail.Text=ex.Message;}};
        Closing+=(_,e)=>{if(busy){e.Cancel=true;detail.Text+="\nEspera a que termine la operación antes de cerrar.";}};
    }
    private void Invalidate(){plan=null;confirmation.Text="";execute.IsEnabled=false;}
    private void Busy(bool value){busy=value;prepare.IsEnabled=!value;disks.IsEnabled=!value;mode.IsEnabled=!value;letter.IsEnabled=!value;confirmation.IsEnabled=!value;execute.IsEnabled=!value&&plan is not null&&confirmation.Text==ReusePlanner.ConfirmationText(plan.DriveLetter);}
    private async Task Prepare()
    {
        if(disks.SelectedItem is not UsbDisk target){detail.Text="Selecciona una USB conectada.";return;}
        Busy(true);plan=null;detail.Text="Verificando la imagen y los archivos de la computadora. No se escribe en la USB…";
        try
        {
            plan=await ReusePlanner.Prepare(caseFolder,reviewFolder,target,letter.Text.ToUpperInvariant(),mode.SelectedIndex==0?"FormatOnly":"FormatAndCopy",default);
            detail.Text=$"PLAN PREPARADO — TODAVÍA NO SE HA BORRADO NADA\n\nDestino: {target}, letra {plan.DriveLetter}:\nSistema de archivos: exFAT\nOperación: {(plan.Operation=="FormatOnly"?"formatear y dejar vacía":"formatear y copiar archivos comprobados")}\nSelección respaldada: {plan.Files.Count:N0} archivos, {plan.Files.Sum(f=>f.Length)/1e9:N2} GB.\n\nLa imagen y los archivos se conservarán en la computadora. El último diagnóstico completo coincide con la imagen.\nPlan guardado en: {plan.ReportFolder}";
        }
        catch(Exception ex){detail.Text="Operación bloqueada: "+ex.Message;}
        finally{Busy(false);}
    }
    private async Task Execute()
    {
        if(plan is null || confirmation.Text!=ReusePlanner.ConfirmationText(plan.DriveLetter))return;
        Busy(true);
        try
        {
            var confirmed=plan with{Confirmation=confirmation.Text};var request=Path.Combine(plan.ReportFolder,"confirmed-request.json");
            await ImageAcquirer.AtomicJson(request,confirmed);
            var helper=Path.Combine(project,"app-reutilizar","RescateUSB.Reutilizar.exe");
            using var process=Process.Start(new ProcessStartInfo(helper){UseShellExecute=true,Verb="runas",Arguments="\""+request+"\"",WindowStyle=ProcessWindowStyle.Hidden})??throw new IOException("No se inició la operación.");
            detail.Text="Operación confirmada. Verificando antes del borrado; después se formateará la USB. No desconectarla.";
            while(!process.HasExited)
            {
                await Task.Delay(1000);var progress=Path.Combine(plan.ReportFolder,"reuse-progress.json");
                if(File.Exists(progress))try{using var p=JsonDocument.Parse(await File.ReadAllTextAsync(progress));detail.Text=$"{p.RootElement.GetProperty("Status").GetString()}\nArchivos: {p.RootElement.GetProperty("Copied")} / {p.RootElement.GetProperty("Total")}\nNo desconectes la memoria. Los respaldos permanecen en la computadora.";}catch(IOException){}catch(JsonException){}
            }
            var result=Path.Combine(plan.ReportFolder,"reuse-result.json");
            if(!File.Exists(result))throw new IOException("No hay informe final. No se presume que terminó; conserva todos los respaldos.");
            using var report=JsonDocument.Parse(await File.ReadAllTextAsync(result));var r=report.RootElement;
            detail.Text=r.GetProperty("Status").GetString()=="Finalizado"
                ? $"Operación terminada. Archivos copiados y verificados: {r.GetProperty("Copied")}.\n\nLos respaldos siguen en la computadora. Esta comprobación no certifica toda la capacidad ni la retención futura.\nInforme: {result}"
                : $"La operación no terminó: {r.GetProperty("Error").GetString()}\nEl formateo, si llegó a comenzar, no se revierte. Los respaldos de la computadora permanecen.\nInforme: {result}";
        }
        catch(Exception ex){detail.Text="No se completó la operación: "+ex.Message;}
        finally{plan=null;confirmation.Text="";Busy(false);}
    }
}
