param([string]$Package,[string]$Output,[string]$Python='python')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$buildTemp=Join-Path $root 'work\build-temp'
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP=$buildTemp; $env:TMP=$buildTemp; $env:PYTHONDONTWRITEBYTECODE='1'
if(-not $Package){$Package=Join-Path $root 'work\MiniAI-windows-x64.zip'}
if(-not $Output){$Output=Join-Path $root 'work\MiniAI-Portable.exe'}
$build=Join-Path $root 'work\portable-build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$compact=Join-Path $build 'payload.zip'
& $Python (Join-Path $PSScriptRoot 'compact_payload.py') --package $Package --output $compact
if($LASTEXITCODE -ne 0){throw 'Compact payload verification failed.'}
$Package=$compact
$manifest=Join-Path $build 'manifest.json'
& $Python (Join-Path $PSScriptRoot 'portable_payload.py') --package $Package --manifest $manifest
if($LASTEXITCODE -ne 0){throw 'Cannot read portable payload.'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$stub=Join-Path $build 'MiniAI.Launcher.exe'
& $compiler /nologo /platform:x64 /target:winexe /optimize+ /debug- /codepage:65001 ('/win32icon:'+(Join-Path $root 'assets\icon.ico')) (('/out:')+$stub) (('/resource:')+$manifest+',MiniAI.Manifest') /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll (Join-Path $PSScriptRoot 'PortableLauncher.cs') (Join-Path $PSScriptRoot 'PortableCleanup.cs') (Join-Path $PSScriptRoot 'PayloadCodec.cs') (Join-Path $root 'src\AssemblyInfo.cs')
if($LASTEXITCODE -ne 0){throw 'Portable launcher compilation failed.'}
& $Python (Join-Path $PSScriptRoot 'portable_payload.py') --package $Package --stub $stub --output $Output
if($LASTEXITCODE -ne 0){throw 'Single executable payload verification failed.'}
Get-Item -LiteralPath $Output | Select-Object Name,Length
