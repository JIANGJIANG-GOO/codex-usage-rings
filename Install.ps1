[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CodexUsageRings\app'),
    [switch]$DownloadLatest,
    [switch]$SkipPluginRegistration,
    [switch]$NoAutoStart,
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'codex-usage-rings\Install-Helpers.ps1')
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'Windows x64 is required.'
}
$sourceRoot = $PSScriptRoot
if ($DownloadLatest) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent'='Codex-Usage-Rings-Installer'; Accept='application/vnd.github+json' }
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/JIANGJIANG-GOO/codex-usage-rings/releases/latest' -Headers $headers
    if ($release.tag_name -notmatch '^v\d+\.\d+\.\d+$') { throw 'Unexpected release version.' }
    $releaseVersion = $release.tag_name.Substring(1)
    $archiveName = 'Codex-Usage-Rings-' + $releaseVersion + '.zip'
    $assets = @($release.assets | Where-Object { $_.name -eq $archiveName })
    $checksums = @($release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' })
    if ($assets.Count -ne 1 -or $checksums.Count -ne 1) { throw 'Latest release is missing its install package or checksum.' }
    $downloadDir = Join-Path ([IO.Path]::GetTempPath()) ('CodexUsageRings-download-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $downloadDir | Out-Null
    foreach ($asset in @($assets[0],$checksums[0])) {
        $prefix = 'https://github.com/JIANGJIANG-GOO/codex-usage-rings/releases/download/'
        if (-not $asset.browser_download_url.StartsWith($prefix,[StringComparison]::Ordinal)) { throw 'Unexpected release asset URL.' }
        Invoke-WebRequest -UseBasicParsing -Uri $asset.browser_download_url -OutFile (Join-Path $downloadDir $asset.name)
    }
    $archivePath = Join-Path $downloadDir $archiveName
    Assert-UsageDownloadHash $archivePath (Join-Path $downloadDir 'SHA256SUMS.txt')
    $sourceRoot = Join-Path $downloadDir 'package'
    Expand-UsageArchive $archivePath $sourceRoot
}
$version = Get-UsagePackageVersion $sourceRoot
if ($DownloadLatest -and $version -ne $releaseVersion) { throw 'Release tag and package version disagree.' }
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
if (Test-UsagePathEqual $InstallDir ([IO.Path]::GetPathRoot($InstallDir))) { throw 'Do not install into a drive root.' }
if (Test-Path -LiteralPath $InstallDir) {
    if ((Get-Item -LiteralPath $InstallDir).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'InstallDir must not be a link or junction.' }
    $existingFiles = @(Get-ChildItem -LiteralPath $InstallDir -Force)
    if ($existingFiles.Count -gt 0 -and -not (Test-UsagePathEqual $InstallDir $sourceRoot)) {
        $existingManifest = Join-Path $InstallDir 'codex-usage-rings\.codex-plugin\plugin.json'
        if (-not (Test-Path $existingManifest) -or (Get-Content $existingManifest -Raw -Encoding UTF8 | ConvertFrom-Json).name -ne 'codex-usage-rings') {
            throw 'InstallDir is not empty and is not a Usage Rings installation. Choose another directory.'
        }
    }
}
$sourcePlugin = Join-Path $sourceRoot 'codex-usage-rings'
if (-not (Test-Path (Join-Path $sourcePlugin 'bin\CodexUsageRings.exe'))) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $compiler)) { throw 'Source build requires the Windows .NET Framework x64 compiler; use the prebuilt release package.' }
    & (Join-Path $sourcePlugin 'Build.ps1') | Out-Host
}
# Only signal this companion's named event, never kill Codex or other apps.
Stop-UsageCompanion
if (-not (Test-UsagePathEqual $InstallDir $sourceRoot)) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $files = @(Get-ChildItem -LiteralPath $sourcePlugin -File -Recurse -Force | Where-Object {
        $_.FullName -notlike (Join-Path $sourcePlugin 'bin\*') -or $_.Name -eq 'CodexUsageRings.exe'
    })
    $files += @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot '.agents') -File -Recurse -Force)
    foreach ($name in @('Install.ps1','Register-plugin.ps1','README.md','CHANGELOG.md')) { $files += Get-Item (Join-Path $sourceRoot $name) }
    foreach ($file in $files) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package files must not be links.' }
        $relative = $file.FullName.Substring($sourceRoot.TrimEnd('\','/').Length).TrimStart('\','/')
        $target = Join-Path $InstallDir $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
$pluginRoot = Join-Path $InstallDir 'codex-usage-rings'
$registration = 'skipped'
if (-not $SkipPluginRegistration) {
    $cli = Get-Command codex -ErrorAction SilentlyContinue
    if ($null -eq $cli) {
        $registration = 'pending-cli-unavailable'
        Write-Warning 'Companion installation continues. Codex CLI is unavailable; plugin registration is pending.'
    } else {
        try {
            Register-UsageMarketplace $cli.Source $InstallDir
            & $cli.Source plugin add 'codex-usage-rings@local-usage-rings' | Out-Host
            if ($LASTEXITCODE -ne 0) { throw 'Plugin registration failed.' }
            $installedJson = & $cli.Source plugin list --marketplace local-usage-rings --json
            if ($LASTEXITCODE -ne 0) { throw 'Cannot verify plugin registration.' }
            $installed = @((($installedJson -join "`n") | ConvertFrom-Json).installed | Where-Object { $_.pluginId -eq 'codex-usage-rings@local-usage-rings' -and $_.version -eq $version -and $_.enabled })
            if ($installed.Count -ne 1) { throw 'Registered plugin version or enabled state does not match this package.' }
            $registration = 'registered'
        } catch {
            $registration = 'pending-registration-failed'
            Write-Warning ('Companion installation continues; plugin registration needs attention: ' + $_.Exception.Message)
        }
    }
}
if ($NoAutoStart) { & (Join-Path $pluginRoot 'AutoStart.ps1') -Remove | Out-Host }
else { & (Join-Path $pluginRoot 'AutoStart.ps1') -NoStart | Out-Host }
$report = [ordered]@{
    version=$version; installDirectory=$InstallDir; pluginRegistration=$registration
    autoStart=(-not $NoAutoStart); running=$false; display='not-started'; fastKnown=$false
}
if (-not $NoStart) {
    $startedAt = [DateTime]::UtcNow
    & (Join-Path $pluginRoot 'Start.ps1') | Out-Host
    $statusPath = Join-Path $env:LOCALAPPDATA 'CodexUsageRings\status.json'
    $expectedExe = Join-Path $pluginRoot 'bin\CodexUsageRings.exe'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        try {
            $snapshot = Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $process = Get-Process -Id $snapshot.pid -ErrorAction Stop
            if (Test-UsageInstalledSnapshot $snapshot $version $expectedExe $process.Path $startedAt) {
                $report.running=$true
                $report.display=if($snapshot.overlayVisible){'visible'}else{'waiting-for-Codex-or-anchor'}
                $report.fastKnown=[bool]$snapshot.mode.FastKnown
                break
            }
        } catch { }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $report.running) { throw 'Startup verification failed: no fresh matching process/path/version status within 30 seconds. Run Status.ps1 to diagnose.' }
}
$reportJson = $report | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $InstallDir 'install-result.json'),$reportJson,[Text.UTF8Encoding]::new($false))
$reportJson
Write-Host 'Installation finished. Open the Codex model menu once for initial Fast detection; no model or speed settings were changed.'
