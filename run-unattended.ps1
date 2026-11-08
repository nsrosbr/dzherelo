param(
  [int]$MaxLoops = 30,
  [int]$PauseSec = 300,
  [string]$Out = "D:\ПРИЙОМКА"
)
# Недоглянутий прогін: перезапуск після збою, поки не вийде 0/2/130.
# Продовження з місця безкоштовне: каталог пропускає вже скачане за sha256 і URL.
$exe = Join-Path $PSScriptRoot "bin\Release\net10.0-windows\Dzherelo.exe"
if (-not (Test-Path $exe)) { Write-Host "немає $exe"; exit 9 }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

for ($i = 1; $i -le $MaxLoops; $i++) {
  $free = (Get-PSDrive -Name ($Out.Substring(0,1))).Free / 1GB
  if ($free -lt 2) { Write-Host "вільно $([math]::Round($free,1)) ГБ — зупиняюсь"; exit 4 }

  Write-Host ("[{0}] прогін {1}/{2}" -f (Get-Date -Format "HH:mm:ss"), $i, $MaxLoops)
  & $exe --collect --sources all --out $Out
  $code = $LASTEXITCODE

  switch ($code) {
    0   { Write-Host "готово"; exit 0 }
    2   { Write-Host "нічого не вибрано"; exit 2 }
    130 { Write-Host "зупинено людиною"; exit 130 }
    3   { Write-Host "тека зайнята іншим екземпляром"; Start-Sleep -Seconds 60 }
    default { Write-Host "часткові невдачі (код $code) — пауза $PauseSec с"; Start-Sleep -Seconds $PauseSec }
  }
}
Write-Host "вичерпано спроби"
exit 1
