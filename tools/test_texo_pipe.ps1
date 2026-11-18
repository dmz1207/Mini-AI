param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$OutputDirectory=(Join-Path $PSScriptRoot '../work/tests/texo-pipe'),[string]$Assembly)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if(-not $Assembly){$Assembly=Join-Path $Root 'MiniAI.exe'}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$test=Join-Path $OutputDirectory 'TexoPipeTests.exe'
& $compiler /nologo /platform:x64 /optimize+ /reference:System.Web.Extensions.dll ('/out:'+$test) (Join-Path $Root 'tools/TexoPipeTests.cs')
if($LASTEXITCODE -ne 0){throw 'Texo pipe regression compilation failed'}
$log=Join-Path $OutputDirectory 'stdout.log'
$errorLog=Join-Path $OutputDirectory 'stderr.log'
$process=Start-Process -FilePath $test -ArgumentList @(('"'+$Assembly+'"'),('"'+$OutputDirectory+'"')) -WorkingDirectory $OutputDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput $log -RedirectStandardError $errorLog
if(-not $process.WaitForExit(120000)){$process.Kill();throw 'Texo pipe regression timed out'}
$process.WaitForExit()
Get-Content -LiteralPath $log -Encoding UTF8
if($process.ExitCode -ne 0){Get-Content -LiteralPath $errorLog -Encoding UTF8;throw ('Texo pipe regression failed: '+$process.ExitCode)}
