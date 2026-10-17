param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$app = Join-Path $root 'TokenTaskbar.exe'
$stage = $null
$stopped = $false
$replaced = $false
$lockHash=[BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($root.ToLowerInvariant()))).Replace('-','')
$updateLock=New-Object Threading.Mutex($false,('Local\TokenTaskbar-Update-'+$lockHash))
$ownsLock=$false
$items = @('TokenTaskbar.exe','TokenTaskbar.Collector.exe','_internal','assets','README.md','Update-TokenTaskbar.ps1')
function Stop-OwnProcesses {
    Get-Process TokenTaskbar,TokenTaskbar.Collector -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ([IO.Path]::GetDirectoryName($_.Path) -eq $root) } |
        ForEach-Object { $_.Kill(); $_.WaitForExit() }
}
try {
    try {$ownsLock=$updateLock.WaitOne(0)} catch [Threading.AbandonedMutexException] {$ownsLock=$true}
    if(-not $ownsLock) {Write-Host 'An update check is already running.';exit 0}
    if (-not (Test-Path -LiteralPath $app)) { throw 'Keep the updater beside TokenTaskbar.exe.' }
    $current = [version](Get-Item -LiteralPath $app).VersionInfo.FileVersion
    Write-Host 'Checking GitHub for updates...'
    $release = Invoke-RestMethod 'https://api.github.com/repos/micrane12/TokenTaskbar/releases/latest' -Headers @{'User-Agent'='TokenTaskbar-Updater'}
    $parsed = [version]$release.tag_name.TrimStart('v')
    $latest = [version]::new($parsed.Major,$parsed.Minor,$parsed.Build,0)
    if ($latest -le $current) { Write-Host "Already up to date ($current)."; if (-not $CheckOnly) { Read-Host 'Press Enter to close' }; exit 0 }
    $asset = $release.assets | Where-Object name -Like '*-win-x64.zip' | Select-Object -First 1
    if (-not $asset) { throw 'No Windows ZIP is attached to this release.' }
    $checksum = $release.assets | Where-Object name -EQ ($asset.name+'.sha256') | Select-Object -First 1
    if (-not $checksum) { throw 'The release checksum is missing.' }
    foreach ($url in @($asset.browser_download_url,$checksum.browser_download_url)) {
        $uri = [uri]$url
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or -not $uri.AbsolutePath.StartsWith('/micrane12/TokenTaskbar/releases/download/')) { throw 'Unexpected release URL.' }
    }
    $stage = Join-Path $env:TEMP ('TokenTaskbar-update-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    $zip = Join-Path $stage 'release.zip'
    Write-Host "Downloading $($release.tag_name)..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    $checksumContent=(Invoke-WebRequest $checksum.browser_download_url -UseBasicParsing).Content
    if($checksumContent -is [byte[]]) {$checksumContent=[Text.Encoding]::UTF8.GetString($checksumContent)}
    $expected=([string]$checksumContent -split '\s+')[0]
    if ($expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'Checksum verification failed. Existing files were not changed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $unpack = [IO.Path]::GetFullPath((Join-Path $stage 'unpacked'))
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $unpack $entry.FullName))
            if (-not $target.StartsWith($unpack+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $entry.FullName.Contains(':')) { throw ('Unsafe archive entry: '+$entry.FullName+'; target='+$target+'; root='+$unpack) }
        }
    } finally { $archive.Dispose() }
    Expand-Archive -LiteralPath $zip -DestinationPath $unpack
    $source = Join-Path $unpack 'TokenTaskbar'
    foreach ($name in $items) { if (-not (Test-Path -LiteralPath (Join-Path $source $name))) { throw "Missing release component: $name" } }
    if ([version](Get-Item (Join-Path $source 'TokenTaskbar.exe')).VersionInfo.FileVersion -ne $latest) { throw 'Downloaded application version does not match the release.' }
    if ($CheckOnly) { Write-Host 'Download, checksum, archive and version checks passed.'; exit 0 }
    $backup = Join-Path $stage 'backup'
    New-Item -ItemType Directory -Path $backup | Out-Null
    foreach ($name in $items) {
        $existing = Join-Path $root $name
        if (Test-Path -LiteralPath $existing) { Copy-Item -LiteralPath $existing -Destination $backup -Recurse -Force }
    }
    # Confirm write access before stopping the running widget.
    $probe = Join-Path $root ('.update-'+[guid]::NewGuid().ToString('N'))
    Set-Content -LiteralPath $probe -Value 'write check'
    Remove-Item -LiteralPath $probe
    Write-Host 'Installing update...'
    Stop-OwnProcesses
    $stopped = $true
    $replaced = $true
    foreach ($name in $items) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $root -Recurse -Force }
    Start-Process -FilePath $app -WorkingDirectory $root -WindowStyle Hidden
    $stopped = $false
    Write-Host "Updated to $($release.tag_name). Settings, cache and startup shortcuts were preserved."
} catch {
    if ($replaced) {
        try {
            Stop-OwnProcesses
            foreach ($name in $items) {
                $old = Join-Path $backup $name
                if (Test-Path -LiteralPath $old) { Copy-Item -LiteralPath $old -Destination $root -Recurse -Force }
            }
            Write-Host 'Restored the previous app files.'
        } catch { Write-Host "Automatic recovery failed. Backup retained at $backup" }
    }
    if ($stopped) { try { Start-Process -FilePath $app -WorkingDirectory $root -WindowStyle Hidden } catch { } }
    Write-Host ('Update failed: '+$_.Exception.Message)
    if (-not $CheckOnly) { Read-Host 'Press Enter to close' }
    exit 1
}

finally {if($ownsLock) {$updateLock.ReleaseMutex()};$updateLock.Dispose()}
