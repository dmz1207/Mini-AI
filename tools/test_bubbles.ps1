param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(-not $OutputDirectory){$OutputDirectory=Join-Path $Root 'work\bubble-tests'}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $Root 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$runner=Join-Path $OutputDirectory 'BubbleTests.exe'
& $compiler /nologo /platform:x64 /optimize+ /codepage:65001 /target:winexe /main:MiniAI.BubbleTests ('/out:'+$runner) ('/resource:'+(Join-Path $Root 'assets\EnglishWords.gz')+',MiniAI.EnglishWords') /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Xaml.dll /reference:System.Xml.dll /reference:System.Xml.Linq.dll ('/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll')) ('/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll')) ('/reference:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll')) ('/reference:'+(Join-Path $Root 'assets\lib\WpfMath.dll')) ('/resource:'+(Join-Path $Root 'assets\lib\WpfMath.dll')+',MiniAI.WpfMath') $sources (Join-Path $PSScriptRoot 'BubbleTests.cs')
if($LASTEXITCODE -ne 0){throw 'Bubble test compilation failed.'}
$log=Join-Path $OutputDirectory 'bubble-tests.txt'
$process=Start-Process -FilePath $runner -ArgumentList ('"'+$log+'"') -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(30000)){throw 'Bubble tests timed out.'}
Get-Content -LiteralPath $log
if($process.ExitCode -ne 0){throw 'Bubble checks failed.'}
