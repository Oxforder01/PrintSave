# Publishes PrintSave.Service and installs it as an auto-start Windows service (LocalSystem).
# MUST be run elevated.
$ErrorActionPreference = 'Stop'
$name = 'PrintSave'
$repo = Split-Path -Parent $PSScriptRoot
$dn   = "$env:ProgramFiles\dotnet\dotnet.exe"
$proj = Join-Path $repo 'src\PrintSave.Service\PrintSave.Service.csproj'
$out  = Join-Path $repo 'artifacts\service'
$exe  = Join-Path $out  'PrintSave.Service.exe'

$elev = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elev) { throw "Run this from an elevated (Administrator) PowerShell." }

Write-Host "Publishing service to $out ..." -ForegroundColor Cyan
& $dn publish $proj -c Release -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Remove any existing instance first.
$svc = Get-Service $name -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service $name -Force }
    sc.exe delete $name | Out-Null
    Start-Sleep -Seconds 1
}

# Ensure the Event Log source exists.
try { if (-not [Diagnostics.EventLog]::SourceExists($name)) { New-EventLog -LogName Application -Source $name } } catch {}

New-Service -Name $name -BinaryPathName ('"' + $exe + '"') `
    -DisplayName 'PrintSave' `
    -Description 'Saves every printed document as a PDF, organized by date.' `
    -StartupType Automatic | Out-Null

# Delayed auto-start + restart-on-failure recovery.
sc.exe config  $name start= delayed-auto | Out-Null
sc.exe failure $name reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

Start-Service $name
Write-Host "Installed and started '$name'." -ForegroundColor Green
Get-Service $name | Format-List Name, Status, StartType
Write-Host "Logs: C:\PrintSave\logs  |  Event Viewer: Windows Logs > Application (source 'PrintSave')"
