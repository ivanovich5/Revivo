using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Rescate.Core;

public sealed record UsbDisk(int Number,string FriendlyName,string SerialNumber,string UniqueId,long Size,
    string HealthStatus,string OperationalStatus,bool IsBoot,bool IsSystem,string DriveLetters="")
{
    public string DisplayName=>string.IsNullOrWhiteSpace(DriveLetters)?"USB sin letra · disco "+Number:"Memoria USB ("+DriveLetters+")";
    public string CapacityLabel=>$"{Size/1e9:N2} GB";
    public override string ToString()=>$"Disco {Number} · {FriendlyName} · {Size/1e9:N2} GB";
}

public static class UsbInventory
{
    public static async Task<UsbDisk[]> List(CancellationToken token)
    {
        // Fixed read-only query: no user strings are interpolated into PowerShell.
        const string script="[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $ErrorActionPreference='Stop'; $items=@(Get-Disk | Where-Object BusType -eq 'USB' | ForEach-Object { [pscustomobject]@{ Number=[int]$_.Number; FriendlyName=[string]$_.FriendlyName; SerialNumber=[string]$_.SerialNumber; UniqueId=[string]$_.UniqueId; Size=[long]$_.Size; HealthStatus=[string]$_.HealthStatus; OperationalStatus=[string]($_.OperationalStatus -join ', '); IsBoot=[bool]$_.IsBoot; IsSystem=[bool]$_.IsSystem } }); ConvertTo-Json -InputObject $items -Compress";
        var enriched=script.Replace("[pscustomobject]@{ Number=","$letters=@(Get-Partition -DiskNumber $_.Number -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -and [int][char]$_.DriveLetter -ne 0 } | ForEach-Object { [string]$_.DriveLetter + ':' }) -join ', '; [pscustomobject]@{ DriveLetters=$letters; Number=");
        return JsonSerializer.Deserialize<UsbDisk[]>(await Query(enriched,token),RecoveryEngine.Json) ?? [];
    }
    public static async Task<int> DiskNumberForLetter(string letter,CancellationToken token)
    {
        if(letter.Length!=1 || letter[0]<'D' || letter[0]>'Z')throw new IOException("Letra de USB no válida.");
        var script="$ErrorActionPreference='Stop'; $partitions=@(Get-Partition -DriveLetter ([char]"+((int)letter[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)+")); if($partitions.Count -ne 1 -or $partitions[0].IsBoot -or $partitions[0].IsSystem){throw 'Partición no inequívoca o protegida'}; [int]$partitions[0].DiskNumber";
        return int.Parse((await Query(script,token)).Trim(),System.Globalization.CultureInfo.InvariantCulture);
    }
    private static async Task<string> Query(string script,CancellationToken token)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
        {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}) start.ArgumentList.Add(arg);
        using var p=Process.Start(start) ?? throw new IOException("No se pudo consultar Windows.");
        var output=p.StandardOutput.ReadToEndAsync(token);var error=p.StandardError.ReadToEndAsync(token);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try {await p.WaitForExitAsync(timeout.Token);}
        catch {try{p.Kill(true);}catch(InvalidOperationException){} throw;}
        if(p.ExitCode!=0) throw new IOException("Windows no pudo enumerar las memorias USB: "+await error);
        return await output;
    }
}
