$ErrorActionPreference = 'Stop'
$marketplaceRoot = $PSScriptRoot
$codexCommand = Get-Command codex -ErrorAction Stop
& $codexCommand.Source plugin marketplace add $marketplaceRoot
if ($LASTEXITCODE -ne 0) { throw 'Marketplace registration failed.' }
Write-Output 'Marketplace registered. Install with: codex plugin add codex-usage-rings@local-usage-rings, or open the Plugins Directory and choose local-usage-rings.'
