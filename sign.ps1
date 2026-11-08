# sign.ps1 - Authenticode-sign release\Dzherelo.exe with the PINNED publisher cert.
# FAIL-CLOSED: no pinned cert -> REFUSE to sign (never ship a build every installed client
# will reject at the update thumbprint gate). ASCII-only (PS 5.1 reads .ps1 as ANSI).
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "release\Dzherelo.exe"),
    [string]$TimeStampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = "Stop"

# The publisher identity lives in this thumbprint once the cert exists (T11 - channel/cert
# are blocked on the owner). Empty means "not provisioned on this device" and signing refuses.
$Pinned = ""

$cert = $null
if ($Pinned) {
    $cert = Get-ChildItem "Cert:\CurrentUser\My" |
        Where-Object { $_.Thumbprint -eq $Pinned -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1
    if (-not $cert) {
        $root = Split-Path -Parent $Exe
        $pfx = Join-Path $root "_codesign.pfx"
        if (Test-Path $pfx) {
            $pw = $env:CODESIGN_PWD
            if ($pw) {
                Import-PfxCertificate -FilePath $pfx -CertStoreLocation "Cert:\CurrentUser\My" -Password (ConvertTo-SecureString $pw -AsPlainText -Force) | Out-Null
                $cert = Get-ChildItem "Cert:\CurrentUser\My" | Where-Object { $_.Thumbprint -eq $Pinned } | Select-Object -First 1
            }
        }
    }
}

if (-not $cert) {
    throw "PINNED code-signing cert not found and no _codesign.pfx. REFUSING TO SIGN (fail closed).`n`nSet `$Pinned in sign.ps1 to the publisher cert thumbprint once it is provisioned (T11). Signing with any other cert breaks the update channel silently."
}

Write-Host ("Certificate: {0}  (valid until {1:yyyy-MM-dd})" -f $cert.Thumbprint, $cert.NotAfter)
try {
    $sig = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimeStampUrl
    if ($null -eq $sig.TimeStamperCertificate) { throw "no timestamp" }
    Write-Host "Signed WITH timestamp."
} catch {
    Write-Host "Timestamp unavailable (offline) - signing without it."
    $sig = Set-AuthenticodeSignature -FilePath $Exe -Certificate $cert -HashAlgorithm SHA256
}
Write-Host ("Signature Status = {0}" -f $sig.Status)
