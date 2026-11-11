# Compiles the XPS open+render check. References the in-box WPF assemblies. No SDK needed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $fw 'csc.exe'
$wpf  = Join-Path $fw 'WPF'

$refs = @(
    (Join-Path $fw  'System.dll'),
    (Join-Path $fw  'System.Core.dll'),
    (Join-Path $fw  'System.Xaml.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'ReachFramework.dll'),
    (Join-Path $wpf 'System.Printing.dll')
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "Missing reference: $r" } }

$src = Join-Path $here 'ConvertSpike.cs'
$out = Join-Path $here 'PrintSaveConvert.exe'
$refArgs = $refs | ForEach-Object { "/reference:$_" }

& $csc /nologo /platform:x64 /target:exe /out:"$out" @refArgs "$src"
if ($LASTEXITCODE -ne 0) { throw "Compilation failed (exit $LASTEXITCODE)" }
Write-Host "Built: $out" -ForegroundColor Green
