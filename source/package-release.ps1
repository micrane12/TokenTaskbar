param([string]$Version = 'dev')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw 'Invalid release version' }
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'release'
$stage = Join-Path $output ('stage-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage 'TokenTaskbar'
New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach ($name in @('TokenTaskbar.exe', 'TokenTaskbar.Collector.exe', '_internal', 'assets', 'README.md')) {
    $item = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $item)) { throw "Missing release component: $name" }
    Copy-Item -LiteralPath $item -Destination $package -Recurse -Force
}
$zip = Join-Path $output "TokenTaskbar-$Version-win-x64.zip"
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower()
Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding ascii
Write-Output $zip
