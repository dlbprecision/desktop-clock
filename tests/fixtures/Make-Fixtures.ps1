# Rebuilds and DLB-signs the two signed test fixtures that VerifierTests check against:
#   dlb-signed-clock.exe  a real clock, as the four-part test build 1.2.9.1 that can never be offered as a release
#   dlb-signed-other.exe  a DLB-signed program that isn't the clock (the test stand-in)
# Needs DLB's Artifact Signing and a signed-in Azure CLI (see CODE-SIGNING.md). Run it only when the fixtures
# must change; the committed copies stay valid because their signatures are timestamped.
param(
    [Parameter(Mandatory)][string]$SignToolPath,
    [Parameter(Mandatory)][string]$DlibPath,
    [Parameter(Mandatory)][string]$MetadataPath
)

$ErrorActionPreference = 'Stop'
$fixtures = $PSScriptRoot
$root = Split-Path (Split-Path $fixtures)
$work = Join-Path $root 'obj\fixtures'
if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

& (Join-Path $root 'build.ps1') -Version 1.2.9.1 -TestBuild -OutputDirectory $work | Out-Null
Copy-Item -LiteralPath (Join-Path $work 'DlbPrecision.DesktopClock.exe') -Destination (Join-Path $work 'dlb-signed-clock.exe')
& (Join-Path $root 'test.ps1') -Only Harness | Out-Null   # builds tests\bin\FakeClock.exe
Copy-Item -LiteralPath (Join-Path $root 'tests\bin\FakeClock.exe') -Destination (Join-Path $work 'dlb-signed-other.exe')

foreach ($name in 'dlb-signed-clock.exe', 'dlb-signed-other.exe') {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\Sign-Artifact.ps1') -FilePath (Join-Path $work $name) `
        -SignToolPath $SignToolPath -DlibPath $DlibPath -MetadataPath $MetadataPath -ExpectedPublisher 'DLB Precision, LLC'
    if ($LASTEXITCODE -ne 0) { throw "Signing $name failed." }
    Copy-Item -LiteralPath (Join-Path $work $name) -Destination (Join-Path $fixtures $name) -Force
    "Signed fixture: $name"
}
