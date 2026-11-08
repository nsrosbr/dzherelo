# release.ps1 - ONE command to ship: package (build+publish) -> selftest gate -> sign (fail-closed).
# Usage: bump <Version> in Dzherelo.csproj, then run  .elease.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $root "package.ps1")   # build + publish + assemble release

# THE GATE. Verify the exact shipped single-file exe passes its self-check before signing.
& (Join-Path $root "publish\Dzherelo.exe") --selftest
if ($LASTEXITCODE -ne 0) { throw "GATE FAILED (--selftest exit $LASTEXITCODE) - no release." }

& (Join-Path $root "sign.ps1") | Select-Object -Last 1
