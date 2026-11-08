# publish.ps1 - upload release\Dzherelo.exe + latest.txt to GitHub Releases via REST.
# Requires a fine-grained token (Contents: write) in _ghtoken.txt or env GH_TOKEN.
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
# Set when the update-channel repo exists (T11); blocked on the owner for now.
$repo = ""

if (-not $repo) { throw "GitHub repo not set (`$repo in publish.ps1) - the update channel is blocked on the owner (T11)." }

$token = $env:GH_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    $tf = Join-Path $root "_ghtoken.txt"
    if (Test-Path $tf) { $token = (Get-Content $tf -Raw).Trim() }
}
if ([string]::IsNullOrWhiteSpace($token)) { throw "No GitHub token (_ghtoken.txt or env GH_TOKEN)." }

$rel = Join-Path $root "release"
$exe = Join-Path $rel "Dzherelo.exe"
$manifest = Join-Path $rel "latest.txt"
if (-not (Test-Path $exe) -or -not (Test-Path $manifest)) { throw "release\Dzherelo.exe or latest.txt missing - run package.ps1 first." }
$ver = ((Get-Content $manifest | Where-Object { $_ -like 'version=*' }) -replace 'version=','').Trim()
$tag = "v$ver"
$headers = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "User-Agent" = "Dzherelo-Publisher"; "X-GitHub-Api-Version" = "2022-11-28" }
$api = "https://api.github.com/repos/$repo"

Write-Host "Publishing $tag to $repo ..."
$release = $null
try { $release = Invoke-RestMethod -Uri "$api/releases/tags/$tag" -Headers $headers -Method Get } catch { }
if ($release) {
    Write-Host "  release $tag exists (id=$($release.id)) - refreshing assets."
} else {
    $body = @{ tag_name = $tag; name = "Dzherelo $ver"; body = "Dzherelo $ver"; draft = $false; prerelease = $false; make_latest = "true" } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri "$api/releases" -Headers $headers -Method Post -Body $body -ContentType "application/json"
    Write-Host "  created release $tag (id=$($release.id))."
}
function Publish-Asset($path, $name) {
    foreach ($a in @($release.assets | Where-Object { $_.name -eq $name })) {
        try { Invoke-RestMethod -Uri "$api/releases/assets/$($a.id)" -Headers $headers -Method Delete | Out-Null } catch { }
    }
    $up = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$name"
    Invoke-RestMethod -Uri $up -Headers $headers -Method Post -Body ([System.IO.File]::ReadAllBytes($path)) -ContentType "application/octet-stream" | Out-Null
    Write-Host ("  uploaded: {0}  ({1:N0} KB)" -f $name, ((Get-Item $path).Length / 1KB))
}
Publish-Asset $exe "Dzherelo.exe"
Publish-Asset $manifest "latest.txt"
Write-Host "DONE. $tag published: https://github.com/$repo/releases/latest"
