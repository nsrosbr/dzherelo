# package.ps1 - assemble release\ from build.ps1 output: the single Dzherelo.exe + latest.txt (4 lines).
# Does NOT publish; that is publish.ps1. ASCII-only (PS 5.1 reads .ps1 as ANSI).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# Set when the update-channel repo exists (T11, blocked on owner's repo + token). Empty for now.
$GitHubRepo = ""

& (Join-Path $root "build.ps1")

$exe = Join-Path $root "publish\Dzherelo.exe"
if (-not (Test-Path $exe)) { throw "publish\Dzherelo.exe missing - run build.ps1 first." }

# Version from the built exe, cross-checked against the declared <Version> in the csproj
# (same fail-closed idea as Valera src\Ver.cs): a stale build must not go out under a new tag.
$ver = (Get-Item $exe).VersionInfo.ProductVersion
if ([string]::IsNullOrEmpty($ver)) { $ver = (Get-Item $exe).VersionInfo.FileVersion }
$declared = ([regex]::Match([System.IO.File]::ReadAllText((Join-Path $root "Dzherelo.csproj"), [Text.Encoding]::UTF8), '<Version>([^<]+)</Version>')).Groups[1].Value
if ($ver -ne $declared) { throw "VERSION MISMATCH: exe reports '$ver' but csproj declares '$declared'" }

$rel = Join-Path $root "release"
New-Item -ItemType Directory -Path $rel -Force | Out-Null
# release\ holds exactly the app (one file) + the manifest. Clear stale leftovers.
Get-ChildItem $rel -File -ErrorAction SilentlyContinue | ForEach-Object { [System.IO.File]::Delete($_.FullName) }

Copy-Item $exe (Join-Path $rel "Dzherelo.exe") -Force

# Setup.exe — той самий single-file exe під installer-ім'ям: запущений як Setup.exe,
# він сканує систему і ставить себе per-user (Setup.Run). Один вміст, два імені.
Copy-Item $exe (Join-Path $rel "Setup.exe") -Force

# latest.txt - 4 lines (SPEC sec.20): version / url / sha256 / notes.
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$url = if ($GitHubRepo) { "https://github.com/$GitHubRepo/releases/latest/download/Dzherelo.exe" } else { "" }
$notes = "Dzherelo $ver"
$manifest = @("version=$ver", "url=$url", "sha256=$sha", "notes=$notes") -join "`r`n"
[System.IO.File]::WriteAllText((Join-Path $rel "latest.txt"), $manifest + "`r`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host ("=== RELEASE v{0} -> {1} ===" -f $ver, $rel)
Get-ChildItem $rel | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize
