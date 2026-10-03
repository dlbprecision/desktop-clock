# Builds and runs the automated tests: the product sources plus tests\*.cs, compiled into tests\bin\Tests.exe
# with the in-box compiler. No network. Tests never touch the real install, settings or Run key.
#   powershell -ExecutionPolicy Bypass -File .\test.ps1                run everything
#   powershell -ExecutionPolicy Bypass -File .\test.ps1 -Only Feed     run suites whose name contains "Feed"
param([string]$Only)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'scripts\BuildCommon.ps1')
$fw   = Get-FrameworkDir
$csc  = Join-Path $fw 'csc.exe'
$refs = @(Get-CompilerReferences $fw)
$bin  = Join-Path $root 'tests\bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

# A fixed version, so version-dependent checks don't change with each release.
$versionFile = Join-Path $root 'obj\TestVersion.g.cs'
Write-VersionFile $versionFile '1.3.0'

$fake = Join-Path $bin 'FakeClock.exe'
& $csc /nologo /target:winexe /platform:anycpu /warn:4 /warnaserror+ "/out:$fake" $refs (Join-Path $root 'tests\FakeClock.cs')
if ($LASTEXITCODE -ne 0) { throw "FakeClock compile failed (exit $LASTEXITCODE)" }

$tests = Join-Path $bin 'Tests.exe'
$testSources = @(Get-ChildItem (Join-Path $root 'tests') -Filter *.cs -File | Where-Object { $_.Name -ne 'FakeClock.cs' } | ForEach-Object { $_.FullName })
& $csc /nologo /target:exe /platform:anycpu /warn:4 /warnaserror+ /main:DlbPrecision.DesktopClock.Tests.TestRunner "/out:$tests" $refs @(Get-ProductSources $root) $versionFile $testSources
if ($LASTEXITCODE -ne 0) { throw "Test compile failed (exit $LASTEXITCODE)" }

$runArgs = @($fake)
if ($Only) { $runArgs += $Only }
& $tests @runArgs
$failed = $LASTEXITCODE
if ($failed -ne 0) { Write-Host "TESTS FAILED ($failed)" -ForegroundColor Red } else { Write-Host 'ALL TESTS PASSED' -ForegroundColor Green }
exit $failed
