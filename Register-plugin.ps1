$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'codex-usage-rings\Install-Helpers.ps1')
$codexCommand = Get-Command codex -ErrorAction Stop
Register-UsageMarketplace $codexCommand.Source $PSScriptRoot
Write-Output 'Marketplace registered. Install with: codex plugin add codex-usage-rings@local-usage-rings, or use Install.ps1 for the complete installation.'