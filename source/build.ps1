$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
$outputRoot = Split-Path $sourceRoot -Parent
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$scratchRoot = Join-Path $outputRoot 'build'
New-Item -ItemType Directory -Force -Path $scratchRoot | Out-Null
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/win32manifest:$sourceRoot\app.manifest" "/win32icon:$outputRoot\assets\TokenTaskbar.ico" "/out:$outputRoot\TokenTaskbar.exe" "/resource:$outputRoot\assets\TokenTaskbar.ico,TokenTaskbar.App.ico" "/resource:$outputRoot\assets\claude.png,TokenTaskbar.Claude.png" "/resource:$outputRoot\assets\codex.png,TokenTaskbar.Codex.png" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" "$sourceRoot\TokenTaskbar.cs"
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed' }
python -m PyInstaller --noconfirm --clean --onedir --console --name TokenTaskbar.Collector --distpath "$scratchRoot\dist" --workpath "$scratchRoot\build" --specpath "$scratchRoot" "$sourceRoot\worker.py"
if ($LASTEXITCODE -ne 0) { throw 'Collector packaging failed' }
Copy-Item "$scratchRoot\dist\TokenTaskbar.Collector\*" $outputRoot -Recurse -Force
