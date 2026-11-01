$ErrorActionPreference='Stop'
$buildTemp=Join-Path $PSScriptRoot 'work\build-temp'
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP=$buildTemp; $env:TMP=$buildTemp; $env:PYTHONDONTWRITEBYTECODE='1'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$files=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
& $compiler /nologo /platform:x64 /optimize+ /debug- /codepage:65001 /target:winexe ('/win32icon:'+(Join-Path $PSScriptRoot 'assets\icon.ico')) ('/out:'+(Join-Path $PSScriptRoot 'MiniAI.exe')) ('/resource:'+(Join-Path $PSScriptRoot 'assets\EnglishWords.gz')+',MiniAI.EnglishWords') /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xaml.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll ('/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll')) ('/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll')) ('/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll')) ('/reference:'+(Join-Path $PSScriptRoot 'assets\lib\WpfMath.dll')) ('/resource:'+(Join-Path $PSScriptRoot 'assets\lib\WpfMath.dll')+',MiniAI.WpfMath') $files
if($LASTEXITCODE -ne 0){throw 'MiniAI compilation failed.'}
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'MiniAI.exe') | Select-Object Name,Length
