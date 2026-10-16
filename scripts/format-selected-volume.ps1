param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new()
$request = Get-Content -LiteralPath $RequestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($request.Operation -notin @('FormatOnly','FormatAndCopy') -or $request.DriveLetter -cnotmatch '^[D-Z]$' -or $request.Confirmation -cne ('BORRAR ' + $request.DriveLetter + ':')) { throw 'Falta confirmación explícita del borrado.' }
$disk = Get-Disk -Number $request.Target.Number
if ($disk.BusType -ne 'USB' -or $disk.IsBoot -or $disk.IsSystem -or $disk.Size -ne $request.Target.Size -or $disk.UniqueId -cne $request.Target.UniqueId -or $disk.FriendlyName -cne $request.Target.FriendlyName) { throw 'El dispositivo cambió o está protegido.' }
$partitions = @(Get-Partition -DriveLetter $request.DriveLetter)
if ($partitions.Count -ne 1 -or $partitions[0].DiskNumber -ne $disk.Number -or $partitions[0].IsBoot -or $partitions[0].IsSystem) { throw 'La letra no corresponde exclusivamente a la USB confirmada.' }
$volume = @($partitions[0] | Get-Volume)
if ($volume.Count -ne 1) { throw 'No hay un volumen inequívoco para formatear. No se modifica la tabla de particiones.' }
# Only this selected volume is formatted. No Clear-Disk, Initialize-Disk, or partition deletion.
$volume[0] | Format-Volume -FileSystem exFAT -NewFileSystemLabel 'RESCATE_USB' -Confirm:$false -Force | Out-Null
$diskAfter = Get-Disk -Number $request.Target.Number
if ($diskAfter.UniqueId -cne $request.Target.UniqueId -or $diskAfter.BusType -ne 'USB') { throw 'La unidad cambió después de formatear; recopia bloqueada.' }
$partitionAfter = Get-Partition -DriveLetter $request.DriveLetter
if ($partitionAfter.DiskNumber -ne $diskAfter.Number) { throw 'Cambió la asignación de letra.' }
$volumeAfter = $partitionAfter | Get-Volume
if ($volumeAfter.FileSystem -ne 'exFAT') { throw 'No se confirmó exFAT.' }
[pscustomobject]@{VolumePath=$volumeAfter.Path;DriveLetter=$request.DriveLetter;DiskNumber=$diskAfter.Number} | ConvertTo-Json -Compress
