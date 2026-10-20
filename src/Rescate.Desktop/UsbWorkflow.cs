using Rescate.Core;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace Rescate.Desktop;

public partial class MainWindow
{
    private bool inventoryLoading;
    private void Home_Click(object sender,RoutedEventArgs e)=>WorkspaceTabs.SelectedIndex=0;
    private async void RefreshUsb_Click(object sender,RoutedEventArgs e)=>await RefreshUsb();
    private void UsbSelection_Changed(object sender,SelectionChangedEventArgs e)
    {if(UsbScanButton is not null)UsbScanButton.IsEnabled=!busy && UsbList.SelectedItem is UsbDisk;}
    public async Task RefreshUsb()
    {
        if(busy || inventoryLoading)return;
        inventoryLoading=true;RefreshUsbButton.IsEnabled=false;
        try
        {
            var disks=(await UsbInventory.List(CancellationToken.None)).Where(d=>!d.IsBoot && !d.IsSystem).ToArray();
            UsbList.ItemsSource=disks;UsbList.SelectedIndex=-1;
            UsbHint.Text=disks.Length==0?"No se detectaron memorias USB. Conecta una y pulsa Actualizar lista.":"Selecciona la memoria por su letra y capacidad. También aparecen memorias RAW o sin letra.";
        }
        catch(Exception ex){UsbHint.Text="No se pudo consultar la lista: "+ex.Message;}
        finally{inventoryLoading=false;RefreshUsbButton.IsEnabled=true;}
    }
    private async void UsbScan_Click(object sender,RoutedEventArgs e)
    {
        if(busy || UsbList.SelectedItem is not UsbDisk selected)return;
        var destination=OutputBox.Text;
        await RunOperation(async(progress,token)=>
        {
            Safety.RequireLocalDisk(destination,selected.Number,selected.Size);
            AcquisitionSelection.Validate(selected,(await UsbInventory.List(token)).SingleOrDefault(d=>d.Number==selected.Number));
            var required=AcquisitionSelection.RequiredSpace(selected.Size);
            var available=new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace;
            if(available<required)throw new IOException($"Espacio disponible: {available/1e9:N1} GB. REVIVO requiere {required/1e9:N1} GB libres para el proceso completo.\n\nLa imagen de esta USB ocupará aproximadamente {selected.Size/1e9:N1} GB. Los otros {(required-selected.Size)/1e9:N1} GB son una reserva preventiva para archivos extraídos, vistas previas y margen de trabajo; no se ocupan de inmediato.\n\nElige otra carpeta en otro disco interno, memoria USB o SSD externo con más espacio o libera espacio sin borrar las recuperaciones que quieras conservar. El uso final depende de los resultados y puede variar.");
            var caseFolder=Safety.CreateOutput(destination,"USB");
            settings=(settings??new LocalSettings(destination,null)) with {AcquisitionFolder=caseFolder,LatestReviewFolder=null,LatestPreviewCatalog=null,LatestSessionFile=null};
            await SavePreferences();
            var request=Path.Combine(caseFolder,"request.json");
            await File.WriteAllTextAsync(request,JsonSerializer.Serialize(new {DiskNumber=selected.Number,ExpectedBytes=selected.Size,ExpectedProduct=selected.FriendlyName,ExpectedUniqueId=selected.UniqueId,Destination=caseFolder,Mode="Acquire"},RecoveryEngine.Json),token);
            progress.Report(new("Paso 1 de 3: Preparando una copia · Windows solicitará permiso para leer la memoria. No se formateará."));
            var start=new ProcessStartInfo(Path.Combine(root,"app-copia-092","RescateUSB.Copia.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add(request);
            using var child=Process.Start(start)??throw new IOException("No se pudo iniciar la copia protegida.");
            bool stopSent=false;var copyClock=Stopwatch.StartNew();var estimate=new CopyTimeEstimate();
            while(!child.HasExited)
            {
                if(token.IsCancellationRequested && !stopSent){await File.WriteAllTextAsync(Path.Combine(caseFolder,"STOP"),"Detener");stopSent=true;}
                var progressFile=Path.Combine(caseFolder,"progress.json");
                if(File.Exists(progressFile))
                {
                    try
                    {
                        var p=JsonSerializer.Deserialize<AcquisitionProgress>(await File.ReadAllTextAsync(progressFile),RecoveryEngine.Json);
                        if(p is not null)progress.Report(new(stopSent?"Deteniendo la copia y conservando el avance…":$"Paso 1 de 3: Preparando una copia · Copia segura y diagnóstico de lectura · {p.ReadBytes/1e9:N2} de {p.Total/1e9:N2} GB · {estimate.Update(p.ReadBytes,p.Total,p.FailedBytes,copyClock.Elapsed.TotalSeconds)}",p.Total>0?(int)(100*p.ReadBytes/p.Total):null));
                    }catch(IOException){}catch(JsonException){}
                }
                await Task.Delay(700);
            }
            token.ThrowIfCancellationRequested();
            var completion=Path.Combine(caseFolder,"completion.json");
            if(!File.Exists(completion))throw new IOException("La copia no terminó. Los datos disponibles se conservaron en "+caseFolder);
            using var result=JsonDocument.Parse(await File.ReadAllTextAsync(completion,token));
            if(!result.RootElement.GetProperty("Complete").GetBoolean())throw new IOException("No se pudo obtener una copia completa. No se analizará como si estuviera íntegra. "+(result.RootElement.TryGetProperty("Error",out var error)?error.GetString():"Consulta la carpeta de recuperación."));
            image=Safety.ImagePath(Path.Combine(caseFolder,"usb.img"));ImageBox.Text=image;
            var map=JsonSerializer.Deserialize<AcquisitionMap>(await File.ReadAllTextAsync(image+".map.json",token),RecoveryEngine.Json)!;
            settings=(settings??new LocalSettings(destination,null)) with {AcquisitionFolder=caseFolder,LatestReviewFolder=null,LatestPreviewCatalog=null,LatestSessionFile=null};
            await SearchAutomatically(image,caseFolder,map.SectorSize,progress,token);
        });
    }
    private async Task SearchAutomatically(string source,string destination,int sector,IProgress<ScanProgress> progress,CancellationToken token,bool persist=true)
    {
        ReviewHost.Content=new TextBlock{Text="Preparando los resultados de esta búsqueda…",Margin=new Thickness(24)};
        progress.Report(new("Paso 2 de 3: Buscando carpetas y archivos…"));
        var scanProgress=new Progress<ScanProgress>(p=>progress.Report(p with {Message="Paso 2 de 3: Buscando archivos · "+p.Message}));
        var result=await engine.Scan(source,destination,false,sector,scanProgress,token);
        token.ThrowIfCancellationRequested();
        if(result.Session.Files.Count==0)
        {
            progress.Report(new("Paso 2 de 3: Las carpetas no se pudieron leer. Iniciando búsqueda profunda…"));
            result=await engine.Scan(source,destination,true,sector,scanProgress,token);
        }
        session=result.Session;sessionDir=result.Directory;ShowResults();
        token.ThrowIfCancellationRequested();
        if(session.Files.Count==0){StatusText.Text="No se encontraron archivos en esta búsqueda. Consulta los detalles.";return;}
        OutputBox.Text=destination;progress.Report(new("Paso 3 de 3: Preparando archivos y vistas previas…"));
        await PrepareReview(progress,token,persist);
    }
}
