# Builds PrintSave.msi: publishes the three executables into one staging folder,
# then compiles the WiX package. Does NOT need elevation. Requires the WiX global tool
# (dotnet tool install --global wix) and its Util + UI extensions, which this script adds.
$ErrorActionPreference = 'Stop'

$here  = $PSScriptRoot
$repo  = Split-Path -Parent $here
$dn    = "$env:ProgramFiles\dotnet\dotnet.exe"
$stage = Join-Path $repo 'artifacts\msi-stage'
$out   = Join-Path $repo 'artifacts\PrintSave.msi'

# Version flows into the MSI ProductVersion / upgrade logic.
$version = '1.0.1'

$projects = @(
    'src\PrintSave.Service\PrintSave.Service.csproj',
    'src\PrintSave.Tray\PrintSave.Tray.csproj',
    'src\PrintSave.ConsoleHost\PrintSave.ConsoleHost.csproj'
)

Write-Host "Cleaning stage $stage ..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

foreach ($p in $projects) {
    $full = Join-Path $repo $p
    Write-Host "Publishing $p ..." -ForegroundColor Cyan
    & $dn publish $full -c Release -o $stage --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $p" }
}

# Drop PDBs and the .NET host config files we don't ship.
Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue

# Requires WiX v5 (the last MS-RL / fee-free release; v7 is gated behind a paid EULA).
Write-Host "Ensuring WiX extensions ..." -ForegroundColor Cyan
wix extension add -g WixToolset.Util.wixext/5.0.2 2>&1 | Out-Null
wix extension add -g WixToolset.UI.wixext/5.0.2   2>&1 | Out-Null

Write-Host "Building MSI -> $out ..." -ForegroundColor Cyan
wix build (Join-Path $here 'PrintSave.wxs') `
    -arch x64 `
    -d "PublishDir=$stage" `
    -d "ProductVersion=$version" `
    -ext WixToolset.Util.wixext `
    -ext WixToolset.UI.wixext `
    -b "$here" `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

Write-Host "Built $out" -ForegroundColor Green
Get-Item $out | Format-List Name, Length, LastWriteTime

# Authenticode-sign the MSI if a code-signing cert is available. A self-signed 'CN=PrintSave'
# cert is enough for your own machine + tamper-evidence; for public distribution replace it
# with a CA-issued cert (same subject, or edit the lookup below). Unsigned is left as-is.
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq 'CN=PrintSave' } | Select-Object -First 1
if ($cert) {
    Write-Host "Signing with $($cert.Subject) [$($cert.Thumbprint)] ..." -ForegroundColor Cyan
    $sig = Set-AuthenticodeSignature -FilePath $out -Certificate $cert -HashAlgorithm SHA256 `
               -TimestampServer 'http://timestamp.digicert.com' -ErrorAction SilentlyContinue
    if ($sig -and $sig.SignerCertificate) {
        $trust = if ($sig.Status -eq 'Valid') { 'trusted' } else { 'untrusted root (self-signed; Valid on machines that trust the cert)' }
        $ts = if ($sig.TimeStamperCertificate) { 'timestamped' } else { 'no timestamp' }
        Write-Host "Signed by $($sig.SignerCertificate.Subject): $trust, $ts" -ForegroundColor Green
    } else {
        Write-Host "Signing failed: $(if($sig){$sig.Status}else{'no result'})" -ForegroundColor Yellow
    }
} else {
    Write-Host "No 'CN=PrintSave' code-signing cert found; MSI left unsigned." -ForegroundColor Yellow
    Write-Host "  Create one: New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=PrintSave' -CertStoreLocation Cert:\CurrentUser\My"
}
