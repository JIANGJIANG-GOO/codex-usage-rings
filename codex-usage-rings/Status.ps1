$statusFile = Join-Path $env:LOCALAPPDATA 'CodexUsageRings\status.json'
if (Test-Path -LiteralPath $statusFile) {
  $snapshot = Get-Content -LiteralPath $statusFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $running = $false
  try { $running = (Get-Process -Id $snapshot.pid -ErrorAction Stop).ProcessName -eq 'CodexUsageRings' } catch { }
  [pscustomobject]@{ running=$running; status=$snapshot } | ConvertTo-Json -Depth 8
} else { '{"running":false,"message":"尚未启动或仍在首次初始化"}' }
