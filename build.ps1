# build.ps1 - dotnet build + single-file publish for Dzherelo.
# Self-contained win-x64 exe; IncludeNativeLibrariesForSelfExtract (csproj) folds
# WebView2Loader.dll and every other native lib into the single exe (SPEC sec.20).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

& dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED (dotnet build exit $LASTEXITCODE)" }

# dotnet publish reuses the output dir: stale files (old name, old native loader) would
# otherwise survive and break the one-file check. Wipe publish\ first.
if (Test-Path (Join-Path $root "publish")) { Remove-Item (Join-Path $root "publish") -Recurse -Force }
& dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o (Join-Path $root "publish")
if ($LASTEXITCODE -ne 0) { throw "PUBLISH FAILED (dotnet publish exit $LASTEXITCODE)" }

$exe = Join-Path $root "publish\Dzherelo.exe"
if (-not (Test-Path $exe)) { throw "single-file exe missing after publish" }

# one-file proof: no DLL of any kind (managed or native WebView2Loader) next to the exe.
$companion = Get-ChildItem (Join-Path $root "publish") -Recurse -File |
    Where-Object { $_.FullName -ne $exe -and $_.Extension -eq ".dll" }
if ($companion) { throw ("NOT single-file - stray DLLs: " + ($companion.Name -join ', ')) }

Write-Host ("BUILD OK -> " + $exe)
Write-Host ("Size: {0:N0} bytes" -f (Get-Item $exe).Length)
