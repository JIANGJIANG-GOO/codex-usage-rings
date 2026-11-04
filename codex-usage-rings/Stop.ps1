$ErrorActionPreference = 'Stop'
$appPath = Join-Path $PSScriptRoot 'bin\CodexUsageRings.exe'
if (Test-Path -LiteralPath $appPath) { Start-Process -FilePath $appPath -ArgumentList '--stop' -WindowStyle Hidden -Wait }
Write-Output 'Stop signal sent to Usage Rings only.'
