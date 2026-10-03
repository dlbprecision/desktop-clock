# Builds DlbPrecision.DesktopClock.exe with the C# compiler that ships inside Windows. Nothing to install.
#   powershell -ExecutionPolicy Bypass -File .\build.ps1             build next to this script, then start it
#   ... -NoLaunch                                                   build only
#   ... -Version 1.2.9.9 -TestBuild -OutputDirectory <folder>       a local test build that can never be a release
# The clock adds itself to Start with Windows when it runs; the build no longer touches the registry.
param(
    [string]$Version,
    [switch]$TestBuild,
    [string]$OutputDirectory,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'scripts\BuildCommon.ps1')

if (-not $Version) { $Version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim() }
if (-not (Test-ClockVersion $Version $TestBuild.IsPresent)) {
    if ($TestBuild) { throw "A test build needs a four-part version such as 1.2.9.9, not '$Version'." }
    throw "Release versions are MAJOR.MINOR.PATCH, not '$Version'. Use -TestBuild for a four-part test version."
}
$inPlace = -not $OutputDirectory
if ($inPlace) { $OutputDirectory = $root }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$out = Join-Path (Resolve-Path $OutputDirectory).Path 'DlbPrecision.DesktopClock.exe'

$fw = Get-FrameworkDir
$versionFile = Join-Path $root 'obj\AppVersion.g.cs'
Write-VersionFile $versionFile $Version
$sources = @(Get-ProductSources $root) + $versionFile

# Stop only a clock running from the file about to be replaced, never an installed copy elsewhere.
Get-Process -Name 'DlbPrecision.DesktopClock' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and [string]::Equals($_.Path, $out, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force

& (Join-Path $fw 'csc.exe') /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /warnaserror+ "/out:$out" @(Get-CompilerReferences $fw) $sources
if ($LASTEXITCODE -ne 0) { throw "Compile failed (exit $LASTEXITCODE)" }
"Built $out  ($Version, {0:N0} KB)" -f ((Get-Item $out).Length / 1KB)

if ($inPlace -and -not $NoLaunch) { Start-Process $out; "Launched." }
