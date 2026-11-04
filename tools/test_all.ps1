param([string]$Root=(Split-Path -Parent $PSScriptRoot),[string]$Python='python',[switch]$Desktop)
$ErrorActionPreference='Stop'
$output=Join-Path $Root 'work\tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$env:TEMP=$output; $env:TMP=$output; $env:PYTHONDONTWRITEBYTECODE='1'
$env:MINIAI_RESULTS_DIR=Join-Path $output 'results'
$process=Start-Process -FilePath (Join-Path $Root 'MiniAI.exe') -ArgumentList '--headless --port 0' -WorkingDirectory $Root -WindowStyle Hidden -PassThru
$connection=$null
try {
    for($attempt=0;$attempt -lt 100;$attempt++) {
        try {
            $candidate=Get-Content -LiteralPath (Join-Path $Root 'state\connection.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            $health=Invoke-RestMethod ($candidate.url+'/health') -Headers @{Authorization=('Bearer '+$candidate.token)} -TimeoutSec 2
            if($health.ok -and $candidate.pid -eq $process.Id){$connection=$candidate;break}
        } catch {}
        Start-Sleep -Milliseconds 100
    }
    if(-not $connection){throw 'The local test service did not start.'}
    & $Python (Join-Path $PSScriptRoot 'test_ocr_cache.py') --root $Root --output $output
    if($LASTEXITCODE -ne 0){throw 'OCR cache and lazy-loading checks failed.'}
    & $Python (Join-Path $PSScriptRoot 'test_service.py') --root $Root --output $output --translation
    if($LASTEXITCODE -ne 0){throw 'Service integration checks failed.'}
    & $Python (Join-Path $PSScriptRoot 'test_streaming.py') --root $Root --output $output
    if($LASTEXITCODE -ne 0){throw 'Streaming checks failed.'}
    & $Python (Join-Path $PSScriptRoot 'test_languages.py') --root $Root --output $output
    if($LASTEXITCODE -ne 0){throw 'Multilingual translation checks failed.'}
    & (Join-Path $PSScriptRoot 'test_formulas.ps1') -Root $Root -OutputDirectory (Join-Path $output 'formulas')
    & $Python (Join-Path $PSScriptRoot 'test_formula_service.py') --root $Root --image (Join-Path $output 'formulas\formula-preview.png') --output $output
    if($LASTEXITCODE -ne 0){throw 'Dedicated formula OCR checks failed.'}
    if($Desktop){& (Join-Path $PSScriptRoot 'test_desktop.ps1') -Root $Root -OutputDirectory (Join-Path $output 'desktop'); & (Join-Path $PSScriptRoot 'test_bubbles.ps1') -Root $Root -OutputDirectory (Join-Path $output 'bubbles'); & (Join-Path $PSScriptRoot 'test_images.ps1') -Root $Root -OutputDirectory (Join-Path $output 'images')}
    & (Join-Path $PSScriptRoot 'test_gpu_fallback.ps1') -Root $Root -OutputDirectory (Join-Path $output 'gpu-fallback')
} finally {
    if($connection -and $connection.pid -eq $process.Id) {
        try{Invoke-RestMethod ($connection.url+'/v1/shutdown') -Method Post -ContentType 'application/json' -Headers @{Authorization=('Bearer '+$connection.token)} -Body '{}' | Out-Null}catch{}
        [void]$process.WaitForExit(5000)
    }
}
