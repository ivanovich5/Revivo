$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cacheRoot = Join-Path $projectRoot '.cache'
$engineRoot = Join-Path $projectRoot 'engines'
$sourceRoot = Join-Path $projectRoot 'third-party-source'
New-Item -ItemType Directory -Force $cacheRoot, $engineRoot, $sourceRoot | Out-Null

function Receive-VerifiedFile([string]$Url, [string]$Path, [string]$ExpectedHash) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    $actualHash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedHash) { throw "El hash no coincide: $Path. No se extraerá ni ejecutará." }
}

$tskZip = Join-Path $cacheRoot 'sleuthkit-4.15.0-win32.zip'
Receive-VerifiedFile 'https://github.com/sleuthkit/sleuthkit/releases/download/sleuthkit-4.15.0/sleuthkit-4.15.0-win32.zip' $tskZip 'c2ebab8105b893d97bd8ce35b88e01985e2a106efc97f03adf95840a631b20ce'
if (-not (Test-Path -LiteralPath (Join-Path $engineRoot 'sleuthkit-4.15.0-win32'))) { Expand-Archive -LiteralPath $tskZip -DestinationPath $engineRoot }

$photoZip = Join-Path $cacheRoot 'testdisk-7.2.win64.zip'
Receive-VerifiedFile 'https://www.cgsecurity.org/testdisk-7.2.win64.zip' $photoZip 'e97e203ce77b6b1a3a37d01beccf069dc6c4632b579ffbb82ae739cdda229f38'
if (-not (Test-Path -LiteralPath (Join-Path $engineRoot 'testdisk-7.2'))) { Expand-Archive -LiteralPath $photoZip -DestinationPath $engineRoot }

Receive-VerifiedFile 'https://github.com/sleuthkit/sleuthkit/releases/download/sleuthkit-4.15.0/sleuthkit-4.15.0.tar.gz' (Join-Path $sourceRoot 'sleuthkit-4.15.0.tar.gz') '3a8c1e7d18a9b81f3e5e8aa78313974aceaafc6e051d636bc92cd7168286eca9'
Receive-VerifiedFile 'https://www.cgsecurity.org/testdisk-7.2.tar.bz2' (Join-Path $sourceRoot 'testdisk-7.2.tar.bz2') 'f8343be20cb4001c5d91a2e3bcd918398f00ae6d8310894a5a9f2feb813c283f'
Write-Output 'Motores y fuentes preparados. No se ha abierto ningún dispositivo físico.'
