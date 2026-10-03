# Builds DlbPrecision.DesktopClock.exe with the C# compiler that ships inside Windows. Nothing to install.
#   powershell -ExecutionPolicy Bypass -File .\build.ps1             build next to this script, then start it
#   ... -NoLaunch                                                   build only
#   ... -Version 1.2.9.9 -TestBuild -OutputDirectory <folder>       a local test build that can never be a release
#   ... -Sign -OutputDirectory artifacts\release-1.3.0-attempt-1    a signed release with its .sha256 (see CODE-SIGNING.md)
# The clock adds itself to Start with Windows when it runs; the build doesn't touch the registry.
param(
    [string]$Version,
    [switch]$TestBuild,
    [string]$OutputDirectory,
    [switch]$NoLaunch,
    [switch]$Sign
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
if ($Sign -and $inPlace) { throw 'A signed build needs -OutputDirectory: a new folder for each release attempt.' }
if ($Sign -and (Test-Path (Join-Path $OutputDirectory 'DlbPrecision.DesktopClock.exe'))) { throw "$OutputDirectory already has a build. Use a new folder for each attempt." }
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

if ($Sign) {
    $config = Join-Path $root 'signing.local.json'
    if (-not (Test-Path $config)) { throw "Signing needs $config (see CODE-SIGNING.md)." }
    $signing = Get-Content $config -Raw | ConvertFrom-Json
    if ($signing.AzureCliPath) { $env:PATH = "$($signing.AzureCliPath);$env:PATH" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\Sign-Artifact.ps1') -FilePath $out `
        -SignToolPath $signing.SignToolPath -DlibPath $signing.DlibPath -MetadataPath $signing.MetadataPath -ExpectedPublisher 'DLB Precision, LLC'
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }
    $hash = (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$out.sha256", "$hash  DlbPrecision.DesktopClock.exe`n", (New-Object Text.UTF8Encoding($false)))

    # The new exe applies every rule installed clocks will, so a build they would refuse fails here, not after publishing.
    $report = Join-Path (Split-Path $out) 'verify-report.txt'
    $checkArgs = @('--verify-package', "`"$out`"", $Version, "`"$report`"")
    if ($TestBuild) { $checkArgs += '--test-build' }
    $check = Start-Process -FilePath $out -ArgumentList $checkArgs -Wait -PassThru
    $result = if (Test-Path $report) { (Get-Content $report -Raw).Trim() } else { '(no report)' }
    if ($check.ExitCode -ne 0) { throw "Installed clocks would refuse this build: $result" }
    "Signed and checked: $result"
    "Publish exactly these two files: $out and $out.sha256"
}

if ($inPlace -and -not $NoLaunch) { Start-Process $out; "Launched." }
