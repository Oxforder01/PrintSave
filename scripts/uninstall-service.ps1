# Stops and removes the PrintSave service and restores printer settings. MUST be run elevated.
$ErrorActionPreference = 'Stop'
$name = 'PrintSave'
$repo = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $repo 'artifacts\service\PrintSave.Service.exe'

$elev = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elev) {
    Write-Host "Not elevated - relaunching as Administrator..." -ForegroundColor Cyan
    Start-Process powershell -Verb RunAs -ArgumentList ('-NoExit -ExecutionPolicy Bypass -File "{0}"' -f $PSCommandPath)
    return
}

$svc = Get-Service $name -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "Service '$name' is not installed." -ForegroundColor Yellow; return }

if ($svc.Status -ne 'Stopped') { Stop-Service $name -Force; Start-Sleep -Seconds 1 }

# Restore printers: clear the keep-printed-jobs flag we set.
if (Test-Path $exe) {
    Write-Host "Restoring printer settings..." -ForegroundColor Cyan
    & $exe --restore-flags
}

sc.exe delete $name | Out-Null
Write-Host "Uninstalled '$name' and restored printer settings." -ForegroundColor Green
