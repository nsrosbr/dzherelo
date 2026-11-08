# git credential helper: токен із DPAPI, нічого не питає.
if ($args[0] -ne "get") { exit 0 }
$exe = "D:/VS/Dzherelo/bin/Release/net10.0-windows/Dzherelo.exe"
$b64 = & $exe --secret-b64 github.token 2>$null
if (-not $b64) { exit 1 }
$tok = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64.Trim()))
"username=x-access-token"
"password=$tok"
