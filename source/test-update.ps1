$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('TokenTaskbar-updater-test-'+[guid]::NewGuid().ToString('N'))
$installed=Join-Path $fixture 'installed'
$payload=Join-Path $fixture 'payload/TokenTaskbar'
New-Item -ItemType Directory -Path $installed,$payload -Force | Out-Null
$built=Join-Path $project 'TokenTaskbar.exe'
Copy-Item -LiteralPath $built -Destination $installed
Copy-Item -LiteralPath (Join-Path $project 'Update-TokenTaskbar.ps1') -Destination $installed
foreach($name in @('TokenTaskbar.exe','TokenTaskbar.Collector.exe','_internal','assets','README.md','Update-TokenTaskbar.ps1')) {Copy-Item -LiteralPath (Join-Path $project $name) -Destination $payload -Recurse}
Set-Content (Join-Path $installed 'widget-settings.json') 'preserve settings'
$fixtureZip=Join-Path $fixture 'payload.zip'
Compress-Archive -LiteralPath $payload -DestinationPath $fixtureZip
$expectedHash=(Get-FileHash $fixtureZip -Algorithm SHA256).Hash
$version=([version](Get-Item $built).VersionInfo.FileVersion).ToString(3)
$global:testUpdaterBadHash=$false
$global:testUpdaterStarted=$false
function Invoke-RestMethod {
    return @{tag_name=('v'+$version);assets=@(@{name='TokenTaskbar-win-x64.zip';browser_download_url='https://github.com/micrane12/TokenTaskbar/releases/download/test/app.zip'},@{name='TokenTaskbar-win-x64.zip.sha256';browser_download_url='https://github.com/micrane12/TokenTaskbar/releases/download/test/app.zip.sha256'})}
}
function Invoke-WebRequest {
    param($Uri,$OutFile,[switch]$UseBasicParsing)
    if($OutFile) {Copy-Item -LiteralPath $fixtureZip -Destination $OutFile}
    else {return @{Content=[Text.Encoding]::UTF8.GetBytes($(if($global:testUpdaterBadHash) {'0'*64} else {$expectedHash}))}}
}
function Get-Item {
    param($LiteralPath,$Path)
    $selected=if($LiteralPath) {$LiteralPath} else {$Path}
    if($selected -eq (Join-Path $installed 'TokenTaskbar.exe')) {return @{VersionInfo=@{FileVersion='0.0.0.0'}}}
    Microsoft.PowerShell.Management\Get-Item -LiteralPath $selected
}
function Get-Process {param($Name,$ErrorAction) return @()}
function Start-Process {param($FilePath,$WorkingDirectory,$WindowStyle) $global:testUpdaterStarted=$true}
function Read-Host {param($Prompt) return ''}
& (Join-Path $installed 'Update-TokenTaskbar.ps1')
if(-not $global:testUpdaterStarted) {throw 'Updated application was not restarted'}
if((Get-Content (Join-Path $installed 'widget-settings.json')) -ne 'preserve settings') {throw 'Settings changed'}
$before=(Get-FileHash (Join-Path $installed 'TokenTaskbar.exe')).Hash
$global:testUpdaterStarted=$false;$global:testUpdaterBadHash=$true
& (Join-Path $installed 'Update-TokenTaskbar.ps1') -CheckOnly
if($global:testUpdaterStarted -or (Get-FileHash (Join-Path $installed 'TokenTaskbar.exe')).Hash -ne $before) {throw 'Bad checksum modified the app'}
Write-Host 'PASS: updater installs verified package, preserves settings, restarts, and rejects bad checksums without changing app files.'
exit 0
