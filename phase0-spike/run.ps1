# Runs the Phase 0 spike elevated (needed to read the spool directory and set printer flags).
# Optional: pass a printer name, e.g.  .\run.ps1 "Microsoft Print to PDF"
param([string]$Printer)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here 'PrintSaveSpike.exe'
if (-not (Test-Path $exe)) { throw "Not built yet. Run .\build.ps1 first." }

$argList = @()
if ($Printer) { $argList += "`"$Printer`"" }

# Relaunch elevated in a window that stays open so you can read the report.
$inner = "& '$exe' $($argList -join ' '); Write-Host ''; Read-Host 'Press Enter to close'"
Start-Process powershell -Verb RunAs -ArgumentList @(
    '-NoProfile','-ExecutionPolicy','Bypass','-Command', $inner
)
Write-Host "Launched elevated. Accept the UAC prompt, then print a document when asked." -ForegroundColor Cyan
