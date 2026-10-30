$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$output=Join-Path $root 'StartMenuChecks.exe'
& "$framework\csc.exe" /nologo /target:exe /platform:x64 "/out:$output" "/reference:$root\TokenTaskbar.exe" /reference:System.Drawing.dll "$PSScriptRoot\test_start_menu.cs"
if($LASTEXITCODE -ne 0){throw 'Start menu test compilation failed'}
& $output
if($LASTEXITCODE -ne 0){throw 'Start menu and icon checks failed'}
