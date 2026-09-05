# Builds ChevyClock.exe with the C# compiler that ships inside Windows.
# Nothing to install. Run:  powershell -ExecutionPolicy Bypass -File .\build.ps1
param([switch]$NoStartup, [switch]$NoLaunch)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fw)) { $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc  = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

# WPF assemblies live in the WPF subfolder; the rest sit in the framework root.
$need = 'System.dll','System.Core.dll','System.Xaml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll'
$refs = @()
foreach ($dll in $need) {
    $p = Join-Path $fw $dll
    if (-not (Test-Path $p)) { $p = Join-Path $fw "WPF\$dll" }
    if (-not (Test-Path $p)) { throw "Missing reference assembly: $dll" }
    $refs += "/r:$p"
}

$src = Join-Path $root 'ChevyClock.cs'
$out = Join-Path $root 'ChevyClock.exe'

Get-Process -Name 'ChevyClock' -ErrorAction SilentlyContinue | Stop-Process -Force
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:1 "/out:$out" $refs $src
if ($LASTEXITCODE -ne 0) { throw "Compile failed (exit $LASTEXITCODE)" }
"Built $out  ({0:N0} KB)" -f ((Get-Item $out).Length / 1KB)

if (-not $NoStartup) {
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-ItemProperty -Path $run -Name 'ChevyClock' -Value "`"$out`"" -PropertyType String -Force | Out-Null
    "Registered to start with Windows (HKCU Run key 'ChevyClock')."
}

if (-not $NoLaunch) { Start-Process $out; "Launched." }
