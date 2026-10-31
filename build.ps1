$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$files=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName
& $compiler /nologo /platform:x64 /optimize+ /debug- /codepage:65001 /target:winexe ('/out:'+(Join-Path $PSScriptRoot 'MiniAI.exe')) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $files
if($LASTEXITCODE -ne 0){throw 'MiniAI compilation failed.'}
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'MiniAI.exe') | Select-Object Name,Length
