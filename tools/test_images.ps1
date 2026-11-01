param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(-not $OutputDirectory){$OutputDirectory=Join-Path $Root 'work\image-tests'}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $Root 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$runner=Join-Path $OutputDirectory 'ImageTests.exe'
& $compiler /nologo /platform:x64 /optimize+ /codepage:65001 /target:winexe /main:MiniAI.ImageTests ('/out:'+$runner) /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $sources (Join-Path $PSScriptRoot 'ImageTests.cs')
if($LASTEXITCODE -ne 0){throw 'Image test compilation failed.'}
$process=Start-Process -FilePath $runner -ArgumentList @(('"'+$Root+'"'),('"'+$OutputDirectory+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(180000)){throw 'Image checks timed out.'}
Get-Content -LiteralPath (Join-Path $OutputDirectory 'image-checks.txt')
if($process.ExitCode -ne 0){throw 'Image checks failed.'}
