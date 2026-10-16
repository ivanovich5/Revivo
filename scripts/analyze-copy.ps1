param([Parameter(Mandatory=$true)][string]$AcquisitionFolder)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$cli=Join-Path $projectRoot 'app-cli\RescateUSB.Cli.exe'
$image=Join-Path $AcquisitionFolder 'usb.img'
$completion=Join-Path $AcquisitionFolder 'completion.json'
while(-not (Test-Path -LiteralPath $completion)) {
    if(Test-Path -LiteralPath (Join-Path $AcquisitionFolder 'STOP')) { throw 'Adquisición cancelada.' }
    Start-Sleep -Seconds 5
}
$result=Get-Content -LiteralPath $completion -Raw | ConvertFrom-Json
if(-not $result.Complete) { throw "No se analizará una imagen incompleta: $($result.Status) $($result.Error)" }
& $cli verify $image $AcquisitionFolder
if($LASTEXITCODE -ne 0) { throw 'La verificación de la copia falló; no se iniciará el análisis.' }
$metadata=Join-Path $AcquisitionFolder 'analisis-metadatos'
& $cli scan $image $metadata metadata
if($LASTEXITCODE -ne 0) { throw 'No terminó el análisis de metadatos.' }
$signatures=Join-Path $AcquisitionFolder 'analisis-firmas'
& $cli scan $image $signatures signatures
if($LASTEXITCODE -ne 0) { throw 'No terminó el análisis por firmas.' }
Write-Output 'Ambos análisis han finalizado. Los informes scan-result.json indican sus sesiones.'
