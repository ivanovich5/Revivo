param([Parameter(Mandatory=$true)][string]$ScratchFolder)
$ErrorActionPreference='Stop'
# All device and format commands are replaced with in-memory functions. No disk
# command below reaches Windows Storage; assert shadowing before running cases.
function Get-Disk { param($Number) [pscustomobject]@{Number=42;BusType='USB';IsBoot=$global:RescateMockBoot;IsSystem=$false;Size=16000000000;UniqueId=$global:RescateMockId;FriendlyName='MOCK USB'} }
function Get-Partition { param($DriveLetter) [pscustomobject]@{DiskNumber=$global:RescateMockPartitionDisk;IsBoot=$false;IsSystem=$false} }
function Get-Volume { [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject) process { [pscustomobject]@{FileSystem='exFAT';Path='\\?\Volume{12345678-1234-1234-1234-123456789abc}\'} } }
function Format-Volume { [CmdletBinding(SupportsShouldProcess=$true)]param([Parameter(ValueFromPipeline=$true)]$InputObject,[string]$FileSystem,[string]$NewFileSystemLabel,[switch]$Force) process { $global:RescateFormatCalls++; if($FileSystem -ne 'exFAT'){throw 'Unexpected filesystem'} } }
foreach($command in @('Get-Disk','Get-Partition','Get-Volume','Format-Volume')) { if((Get-Command $command).CommandType -ne 'Function'){throw 'Mock not installed; refusing to test'} }
$scratch=[IO.Path]::GetFullPath($ScratchFolder)
if($scratch -notlike '*\work\*'){throw 'Tests only write under work'}
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$formatter=Join-Path $PSScriptRoot '..\scripts\format-selected-volume.ps1'
$requestPath=Join-Path $scratch 'mock-request.json'
$results=@()
foreach($case in @('approved-mock','no-consent','wrong-disk','changed-id','boot-disk')) {
 $global:RescateMockBoot=$false;$global:RescateMockId='MOCK-ID';$global:RescateMockPartitionDisk=42;$global:RescateFormatCalls=0
 $consent='BORRAR Z:'
 switch($case){'no-consent'{$consent=''} 'wrong-disk'{$global:RescateMockPartitionDisk=41} 'changed-id'{$global:RescateMockId='OTHER'} 'boot-disk'{$global:RescateMockBoot=$true}}
 @{Operation='FormatAndCopy';DriveLetter='Z';Confirmation=$consent;Target=@{Number=42;Size=16000000000;UniqueId='MOCK-ID';FriendlyName='MOCK USB'}} | ConvertTo-Json | Set-Content -LiteralPath $requestPath -Encoding UTF8
 $failed=$false
 $failureMessage=''
 try{$null=& $formatter -RequestPath $requestPath}catch{$failed=$true;$failureMessage=$_.Exception.Message}
 $pass=if($case -eq 'approved-mock'){ !$failed -and $global:RescateFormatCalls -eq 1 }else{ $failed -and $global:RescateFormatCalls -eq 0 }
 $results += [pscustomobject]@{Case=$case;Passed=$pass;MockFormatCalls=$global:RescateFormatCalls}
 if(!$pass){throw ('Guard test failed: '+$case+'; mock calls='+$global:RescateFormatCalls+'; error='+$failureMessage)}
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'results.json') -Encoding UTF8
$results | Format-Table
