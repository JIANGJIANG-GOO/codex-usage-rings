$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('UsageRings-install-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'bin') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Start.ps1') $fixture
if (Test-Path (Join-Path $PSScriptRoot 'Install-Helpers.ps1')) { Copy-Item (Join-Path $PSScriptRoot 'Install-Helpers.ps1') $fixture }
[IO.File]::WriteAllBytes((Join-Path $fixture 'bin\CodexUsageRings.exe'), [byte[]]@())
Set-Content (Join-Path $fixture 'AutoStart.ps1') 'param([switch]$NoStart) $global:repairedUsageTask = $true' -Encoding UTF8
$global:repairedUsageTask = $false
$global:startedStaleUsageTask = $false
function Get-ScheduledTask { param($TaskName,$ErrorAction) [pscustomobject]@{Actions=@([pscustomobject]@{Execute='C:\old-install\bin\CodexUsageRings.exe';WorkingDirectory='C:\old-install';Arguments=''})} }
function Start-ScheduledTask { param($TaskName) $global:startedStaleUsageTask = -not $global:repairedUsageTask }
function Start-Process { throw 'Unexpected direct process start in task migration test' }
& (Join-Path $fixture 'Start.ps1')
if (-not $global:repairedUsageTask -or $global:startedStaleUsageTask) { throw 'Stale task must be repaired before it is started.' }
Write-Output 'PASS stale task repaired before starting; fake scheduler only.'
. (Join-Path $PSScriptRoot 'Install-Helpers.ps1')
$script:checks = 1
function Check($Value, $Message) { if (-not $Value) { throw $Message }; $script:checks++ }
function Reject([scriptblock]$Action, $Message) { $failed=$false; try { & $Action | Out-Null } catch { $failed=$true }; Check $failed $Message }
$task = [pscustomobject]@{Actions=@([pscustomobject]@{Execute=(Join-Path $fixture 'bin\CodexUsageRings.exe');WorkingDirectory=$fixture;Arguments=''})}
Check (Test-UsageTaskTarget $task $fixture) 'Current task target should match.'
$task.Actions[0].WorkingDirectory='C:\old-install'
Check (-not (Test-UsageTaskTarget $task $fixture)) 'Wrong working directory must be repaired.'
$task.Actions[0].WorkingDirectory=$fixture; $task.Actions[0].Arguments='--data-dir C:\old-data'
Check (-not (Test-UsageTaskTarget $task $fixture)) 'Unexpected task arguments must not be reused.'
Check (Test-UsagePathEqual ('"'+$fixture.ToUpperInvariant()+'\"') $fixture) 'Quoted, case-varied, trailing-slash paths should match.'
$now=[DateTime]::UtcNow
$snapshot=[pscustomobject]@{version='0.4.11';checkedAtUtc=$now.ToString('o')}
Check (Test-UsageInstalledSnapshot $snapshot '0.4.11' 'C:\new\app.exe' 'C:\new\app.exe' $now.AddSeconds(-1)) 'Fresh matching snapshot should verify.'
Check (-not (Test-UsageInstalledSnapshot $snapshot '0.4.11' 'C:\new\app.exe' 'C:\old\app.exe' $now.AddSeconds(-1))) 'Old process path must not verify.'
Check (-not (Test-UsageInstalledSnapshot $snapshot '0.4.10' 'C:\new\app.exe' 'C:\new\app.exe' $now.AddSeconds(-1))) 'Wrong version must not verify.'
$snapshot.checkedAtUtc=$now.AddMinutes(-1).ToString('o')
Check (-not (Test-UsageInstalledSnapshot $snapshot '0.4.11' 'C:\new\app.exe' 'C:\new\app.exe' $now.AddSeconds(-1))) 'Stale status must not verify.'

# Exercise the real installer with fake package/build/scheduler helpers. No live
# process, account, startup task, plugin registration or network is touched.
$source=Join-Path $fixture 'source'
$package=Join-Path $source 'codex-usage-rings'
New-Item -ItemType Directory -Force (Join-Path $package '.codex-plugin'),(Join-Path $package 'bin'),(Join-Path $source '.agents\plugins') | Out-Null
Copy-Item (Join-Path (Split-Path $PSScriptRoot) 'Install.ps1') $source
Copy-Item (Join-Path $PSScriptRoot 'Install-Helpers.ps1') $package
Add-Content (Join-Path $package 'Install-Helpers.ps1') "`nfunction Stop-UsageCompanion { `$global:fakeStopCount++ }"
Set-Content (Join-Path $package '.codex-plugin\plugin.json') '{"name":"codex-usage-rings","version":"0.4.11"}'
Copy-Item (Join-Path $package '.codex-plugin\plugin.json') (Join-Path $package 'plugin.json')
Set-Content (Join-Path $package 'AutoStart.ps1') 'param([switch]$Remove,[switch]$NoStart) $global:fakeAutoRemove=[bool]$Remove; $global:fakeAutoNoStart=[bool]$NoStart'
Set-Content (Join-Path $package 'Start.ps1') 'throw "NoStart must prevent startup"'
Set-Content (Join-Path $package 'Build.ps1') '$global:fakeBuildCount++; [IO.File]::WriteAllText((Join-Path $PSScriptRoot "bin\CodexUsageRings.exe"),"fake-binary")'
foreach($name in @('Register-plugin.ps1','README.md','CHANGELOG.md')) {Set-Content (Join-Path $source $name) 'fixture'}
Set-Content (Join-Path $source '.agents\plugins\marketplace.json') '{}'
$global:fakeStopCount=0; $global:fakeBuildCount=0
$destination=Join-Path $fixture 'new installation'
$result=& (Join-Path $source 'Install.ps1') -InstallDir $destination -NoStart -NoAutoStart -SkipPluginRegistration | ConvertFrom-Json
Check ($result.version -eq '0.4.11' -and -not $result.running) 'NoStart must report installed, not running.'
Check ($global:fakeBuildCount -eq 1 -and $global:fakeStopCount -eq 1) 'Source install must build once and gracefully stop before copying.'
Check $global:fakeAutoRemove 'NoAutoStart must remove the old task rather than leave an old path active.'
Check (Test-Path (Join-Path $destination '.agents\plugins\marketplace.json')) 'Hidden marketplace metadata must be copied.'
Check (Test-Path (Join-Path $destination 'codex-usage-rings\.codex-plugin\plugin.json')) 'Hidden plugin metadata must be copied.'
$result=& (Join-Path $source 'Install.ps1') -InstallDir $destination -NoStart -SkipPluginRegistration | ConvertFrom-Json
Check ($global:fakeBuildCount -eq 1 -and $global:fakeAutoNoStart -and -not $global:fakeAutoRemove) 'Reinstall uses existing binary and configures startup without accidentally launching.'
Check ((Get-UsagePackageVersion $destination) -eq '0.4.11') 'Installed metadata versions should agree.'
$metadataPath=Join-Path $package 'plugin.json'
[IO.File]::WriteAllText($metadataPath,'{"name":"codex-usage-rings","version":"0.4.11"}',[Text.UTF8Encoding]::new($true))
Reject { Get-UsagePackageVersion $source } 'BOM JSON must be rejected before Codex CLI registration.'
Copy-Item (Join-Path $package '.codex-plugin\plugin.json') $metadataPath -Force
Set-Content (Join-Path $package 'plugin.json') '{"name":"codex-usage-rings","version":"0.0.1"}'
Reject { & (Join-Path $source 'Install.ps1') -InstallDir $destination -NoStart -SkipPluginRegistration } 'Version mismatch must stop installation.'
Copy-Item (Join-Path $package '.codex-plugin\plugin.json') (Join-Path $package 'plugin.json') -Force
$occupied=Join-Path $fixture 'unrelated'; New-Item -ItemType Directory $occupied | Out-Null; Set-Content (Join-Path $occupied 'keep.txt') 'keep'
Reject { & (Join-Path $source 'Install.ps1') -InstallDir $occupied -NoStart -SkipPluginRegistration } 'Installer must not overwrite unrelated directories.'
Check ((Get-Content (Join-Path $occupied 'keep.txt')) -eq 'keep') 'Unrelated files are preserved.'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $fixture 'Codex-Usage-Rings-0.4.11.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($source,$archive)
$checksums=Join-Path $fixture 'SHA256SUMS.txt'
Set-Content $checksums ((Get-FileHash $archive -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($archive))
Assert-UsageDownloadHash $archive $checksums; $script:checks++
Expand-UsageArchive $archive (Join-Path $fixture 'extracted')
Check (Test-Path (Join-Path $fixture 'extracted\codex-usage-rings\.codex-plugin\plugin.json')) 'Validated zip extracts hidden metadata.'
Set-Content $checksums (('0'*64)+'  '+[IO.Path]::GetFileName($archive))
Reject { Assert-UsageDownloadHash $archive $checksums } 'Tampered archive must fail checksum verification.'
$bad=Join-Path $fixture 'traversal.zip'; $z=[IO.Compression.ZipFile]::Open($bad,[IO.Compression.ZipArchiveMode]::Create)
try { [void]$z.CreateEntry('../escaped.txt') } finally {$z.Dispose()}
Reject { Expand-UsageArchive $bad (Join-Path $fixture 'bad-extract') } 'Zip path traversal must be rejected before extraction.'
Check (-not (Test-Path (Join-Path $fixture 'escaped.txt'))) 'Traversal did not create a file outside extraction root.'
Set-Content $checksums ((Get-FileHash $archive -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($archive))
$global:usageTestArchivePath=$archive; $global:usageTestChecksumPath=$checksums
function Invoke-RestMethod { param($Uri,$Headers)
    if ($Uri -ne 'https://api.github.com/repos/JIANGJIANG-GOO/codex-usage-rings/releases/latest') {throw 'Unexpected network request'}
    [pscustomobject]@{tag_name='v0.4.11';assets=@(
        [pscustomobject]@{name='Codex-Usage-Rings-0.4.11.zip';browser_download_url='https://github.com/JIANGJIANG-GOO/codex-usage-rings/releases/download/v0.4.11/Codex-Usage-Rings-0.4.11.zip'},
        [pscustomobject]@{name='SHA256SUMS.txt';browser_download_url='https://github.com/JIANGJIANG-GOO/codex-usage-rings/releases/download/v0.4.11/SHA256SUMS.txt'})}
}
function Invoke-WebRequest { param($Uri,$OutFile,[switch]$UseBasicParsing)
    if ($Uri.EndsWith('.zip')) {Copy-Item -LiteralPath $global:usageTestArchivePath -Destination $OutFile} else {Copy-Item -LiteralPath $global:usageTestChecksumPath -Destination $OutFile}
}
function Get-Command { param($Name,$ErrorAction) if($Name -ne 'codex'){throw 'Unexpected command lookup'}; return $null }
$downloadTarget=Join-Path $fixture 'download installation'
$result=& (Join-Path $source 'Install.ps1') -DownloadLatest -InstallDir $downloadTarget -NoStart -NoAutoStart | ConvertFrom-Json
Check ($result.version -eq '0.4.11') 'Latest-release download installs the version described by the metadata.'
Check ($result.pluginRegistration -eq 'pending-cli-unavailable') 'Missing CLI must explicitly report pending registration.'
Check ((Get-FileHash (Join-Path $downloadTarget 'codex-usage-rings\bin\CodexUsageRings.exe')).Hash -eq (Get-FileHash (Join-Path $package 'bin\CodexUsageRings.exe')).Hash) 'Downloaded installation preserves the verified binary.'
$global:usageOldMarketRoot=$source; $global:usageNewMarketRoot=$destination
$marketPath=Join-Path $source '.agents\plugins\marketplace.json'
Set-Content $marketPath '{"name":"local-usage-rings","plugins":[{"name":"codex-usage-rings"}]}'
$global:usageRemovedMarket=$false; $global:usageRegisteredMarket=''; $global:usageFailNewMarket=$false
function Invoke-UsageFakeCli {
    $global:LASTEXITCODE=0
    $command=$args -join ' '
    if ($command -eq 'plugin marketplace list --json') {
        return ([pscustomobject]@{marketplaces=@([pscustomobject]@{name='local-usage-rings';root=$global:usageOldMarketRoot})} | ConvertTo-Json -Depth 4)
    }
    if ($command -eq 'plugin marketplace remove local-usage-rings') {$global:usageRemovedMarket=$true; return}
    if ($args[2] -eq 'add') {
        if($global:usageFailNewMarket -and $args[3] -eq $global:usageNewMarketRoot){$global:LASTEXITCODE=1;return}
        $global:usageRegisteredMarket=$args[3]; return
    }
    throw 'Unexpected CLI invocation'
}
Register-UsageMarketplace 'Invoke-UsageFakeCli' $destination
Check ($global:usageRemovedMarket -and $global:usageRegisteredMarket -eq $destination) 'Dedicated old marketplace migrates to new installation.'
$global:usageFailNewMarket=$true
Reject {Register-UsageMarketplace 'Invoke-UsageFakeCli' $destination} 'Failed new registration must be reported.'
Check ($global:usageRegisteredMarket -eq $source) 'Failed migration restores the previous marketplace source.'
$global:usageRemovedMarket=$false
Set-Content $marketPath '{"name":"local-usage-rings","plugins":[{"name":"codex-usage-rings"},{"name":"unrelated"}]}'
Reject {Register-UsageMarketplace 'Invoke-UsageFakeCli' $destination} 'A marketplace containing unrelated plugins must not be migrated.'
Check (-not $global:usageRemovedMarket) 'Unrelated plugin registration is preserved.'
Write-Output ("PASS installation: $script:checks checks; mocked scheduler/build and isolated package only.")
