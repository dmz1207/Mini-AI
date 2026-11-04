param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(-not $OutputDirectory){$OutputDirectory=Join-Path $Root 'work\gpu-fallback-tests'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /platform:x64 /optimize+ /target:exe ('/out:'+(Join-Path $OutputDirectory 'GpuFailureServer.exe')) (Join-Path $PSScriptRoot 'GpuFailureServer.cs')
if($LASTEXITCODE -ne 0){throw 'GPU test double build failed'}
$sources=@('Hardware.cs','HyTranslation.cs','Translation.cs','Service.cs','OfflineRuntime.cs','OfflineOcr.cs','FormulaOcr.cs','FormulaGeometry.cs','NativeDependencies.cs','ProcessGroup.cs','ClientLease.cs','ResultStore.cs','LanguageCatalog.cs','TranslationText.cs') | ForEach-Object {Join-Path (Join-Path $Root 'src') $_}
$runner=Join-Path $OutputDirectory 'GpuFallbackTests.exe'
& $compiler /nologo /platform:x64 /optimize+ /codepage:65001 /target:exe /main:MiniAI.GpuFallbackTests ('/out:'+$runner) ('/resource:'+(Join-Path $Root 'assets\EnglishWords.gz')+',MiniAI.EnglishWords') /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $sources (Join-Path $PSScriptRoot 'GpuFallbackTests.cs')
if($LASTEXITCODE -ne 0){throw 'GPU fallback test build failed'}
$process=Start-Process -FilePath $runner -ArgumentList @(('"'+$Root+'"'),('"'+$OutputDirectory+'"')) -WorkingDirectory $Root -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(120000)){throw 'GPU fallback test timed out'}
Get-Content -LiteralPath (Join-Path $OutputDirectory 'gpu-fallback-checks.txt')
if($process.ExitCode -ne 0){throw 'GPU fallback checks failed'}
