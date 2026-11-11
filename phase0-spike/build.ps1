# Compiles the Phase 0 spike with the in-box .NET Framework compiler. No SDK needed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }

$src = Join-Path $here 'CaptureSpike.cs'
$out = Join-Path $here 'PrintSaveSpike.exe'

& $csc /nologo /platform:x64 /target:exe /out:"$out" "$src"
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)" }
Write-Host "Built: $out" -ForegroundColor Green
