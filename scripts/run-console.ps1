# Builds (if needed) and runs the PrintSave Phase 1 capture console ELEVATED.
# Optional: -Root to override the archive folder (default C:\PrintSave).
param([string]$Root)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dn   = "$env:ProgramFiles\dotnet\dotnet.exe"
$proj = Join-Path $repo 'src\PrintSave.ConsoleHost\PrintSave.ConsoleHost.csproj'
$exe  = Join-Path $repo 'src\PrintSave.ConsoleHost\bin\Debug\net48\PrintSave.ConsoleHost.exe'

Write-Host "Building..." -ForegroundColor Cyan
& $dn build $proj -c Debug --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$inner = "& '$exe'"
if ($Root) { $inner += " --root '$Root'" }

Write-Host "Launching elevated (accept the UAC prompt)..." -ForegroundColor Cyan
Start-Process powershell -Verb RunAs -ArgumentList @(
    '-NoExit','-NoProfile','-ExecutionPolicy','Bypass','-Command', $inner
)
Write-Host "Running in the elevated window. Print something, then watch it get saved." -ForegroundColor Green
