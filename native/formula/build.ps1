param([Parameter(Mandatory=$true)][string]$Compiler,[string]$Runtime,[string]$Output)
$ErrorActionPreference='Stop'
$packageRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(-not $Runtime){$Runtime=Join-Path $packageRoot 'runtime\formula'}
if(-not $Output){$Output=Join-Path $Runtime 'crispembed.dll'}
$Runtime=(Resolve-Path -LiteralPath $Runtime).Path
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null
Push-Location -LiteralPath $PSScriptRoot
try{
    # Baseline stays portable. AVX2/AVX-512 are isolated functions selected
    # after CPUID + OS state checks, never global -march=native flags.
    & $Compiler -std=c++17 -O3 -ffp-contract=off -march=x86-64 -mtune=generic -DNOMINMAX -DWIN32_LEAN_AND_MEAN -DGGML_SHARED -DGGML_BACKEND_SHARED -shared -static -s '-Wl,--exclude-all-symbols,--no-insert-timestamp,--image-base,0x180000000' -Iinclude -I. ppformulanet_ocr.cpp core/gguf_loader.cpp miniai_api.cpp ('-L'+$Runtime) '-l:ggml-base.dll' '-l:ggml.dll' '-l:ggml-cpu.dll' -o $Output
    if($LASTEXITCODE -ne 0){throw 'Formula native compilation failed.'}
    Get-Item -LiteralPath $Output | Select-Object Name,Length
}finally{Pop-Location}
