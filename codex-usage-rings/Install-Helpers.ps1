function Test-UsagePathEqual {
    param([string]$Left, [string]$Right)
    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) { return $false }
    try {
        $a = [IO.Path]::GetFullPath($Left.Trim('"')).TrimEnd('\','/')
        $b = [IO.Path]::GetFullPath($Right.Trim('"')).TrimEnd('\','/')
        return [string]::Equals($a,$b,[StringComparison]::OrdinalIgnoreCase)
    } catch { return $false }
}
function Test-UsageTaskTarget {
    param($Task, [string]$PackageRoot)
    if ($null -eq $Task) { return $false }
    $actions = @($Task.Actions)
    if ($actions.Count -ne 1) { return $false }
    return ((Test-UsagePathEqual $actions[0].Execute (Join-Path $PackageRoot 'bin\CodexUsageRings.exe')) -and
        (Test-UsagePathEqual $actions[0].WorkingDirectory $PackageRoot) -and [string]::IsNullOrWhiteSpace($actions[0].Arguments))
}
function Stop-UsageCompanion {
    $stopEvent = $null
    if ([Threading.EventWaitHandle]::TryOpenExisting('Local\CodexUsageRings.Stop.v1', [ref]$stopEvent)) {
        try { [void]$stopEvent.Set() } finally { $stopEvent.Dispose() }
        $deadline = [DateTime]::UtcNow.AddSeconds(12)
        do {
            $mutex = $null
            if (-not [Threading.Mutex]::TryOpenExisting('Local\CodexUsageRings.Instance.v1', [ref]$mutex)) { return }
            $mutex.Dispose()
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        throw 'Usage Rings did not exit within 12 seconds. Close it before retrying; no process was forcibly terminated.'
    }
}
function Get-UsagePackageVersion {
    param([string]$Root)
    $plugin = Join-Path $Root 'codex-usage-rings'
    foreach ($relative in @('.codex-plugin\plugin.json','plugin.json')) {
        $bytes=[IO.File]::ReadAllBytes((Join-Path $plugin $relative))
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) { throw 'Plugin JSON must use UTF-8 without BOM for Codex CLI compatibility.' }
    }
    $a = Get-Content (Join-Path $plugin '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $b = Get-Content (Join-Path $plugin 'plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($a.name -ne 'codex-usage-rings' -or $b.name -ne 'codex-usage-rings' -or $a.version -notmatch '^\d+\.\d+\.\d+$' -or $a.version -ne $b.version) { throw 'Plugin version metadata is missing or inconsistent.' }
    $source = Join-Path $plugin 'src\Program.cs'
    if (Test-Path -LiteralPath $source) {
        $match = [regex]::Match([IO.File]::ReadAllText($source),'version\s*=\s*"([\d.]+)"')
        if (-not $match.Success -or $match.Groups[1].Value -ne $a.version) { throw 'Program source and plugin versions disagree.' }
    }
    return [string]$a.version
}
function Test-UsageInstalledSnapshot {
    param($Snapshot, [string]$ExpectedVersion, [string]$ExpectedExe, [string]$ActualExe, [DateTime]$StartedAfter)
    if ($null -eq $Snapshot -or $Snapshot.version -ne $ExpectedVersion) { return $false }
    if (-not (Test-UsagePathEqual $ActualExe $ExpectedExe)) { return $false }
    try { $checked = [DateTime]::Parse($Snapshot.checkedAtUtc).ToUniversalTime() } catch { return $false }
    return ($checked -ge $StartedAfter.ToUniversalTime() -and $checked -le [DateTime]::UtcNow.AddSeconds(5) -and ([DateTime]::UtcNow-$checked).TotalSeconds -lt 15)
}
function Assert-UsageDownloadHash {
    param([string]$Archive, [string]$ChecksumFile)
    $pattern = '^(?<hash>[a-fA-F0-9]{64})\s+\*?' + [regex]::Escape([IO.Path]::GetFileName($Archive)) + '$'
    $lines = @(Get-Content -LiteralPath $ChecksumFile | Where-Object { $_ -match $pattern })
    if ($lines.Count -ne 1) { throw 'Release checksum entry is missing or ambiguous.' }
    $expected = [regex]::Match($lines[0],$pattern).Groups['hash'].Value
    if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $expected) { throw 'Release SHA-256 checksum mismatch. Installation stopped.' }
}
function Expand-UsageArchive {
    param([string]$Archive, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $target = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) {
            $path = [IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
            if (-not $path.StartsWith($target,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::IsPathRooted($entry.FullName) -or $entry.FullName.Contains(':')) { throw 'Unsafe path in release archive.' }
        }
    } finally { $zip.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($Archive,$Destination)
}
function Register-UsageMarketplace {
    param([string]$CliPath, [string]$Root)
    $listing = & $CliPath plugin marketplace list --json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing plugin marketplace registration.' }
    $old = @((($listing -join "`n") | ConvertFrom-Json).marketplaces | Where-Object { $_.name -eq 'local-usage-rings' })
    $previousRoot = $null
    if ($old.Count -gt 1) { throw 'Ambiguous local-usage-rings marketplace registration.' }
    if ($old.Count -eq 1 -and -not (Test-UsagePathEqual $old[0].root $Root)) {
        $previousRoot = $old[0].root
        $manifest = Get-Content (Join-Path $previousRoot '.agents\plugins\marketplace.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($manifest.name -ne 'local-usage-rings' -or @($manifest.plugins).Count -ne 1 -or $manifest.plugins[0].name -ne 'codex-usage-rings') {
            throw 'Existing marketplace contains other plugins; it was not replaced.'
        }
        & $CliPath plugin marketplace remove local-usage-rings | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Cannot migrate the previous Usage Rings marketplace.' }
    }
    & $CliPath plugin marketplace add $Root | Out-Host
    if ($LASTEXITCODE -ne 0) {
        if ($previousRoot) { & $CliPath plugin marketplace add $previousRoot | Out-Host }
        throw 'Marketplace registration failed; attempted to restore the previous source.'
    }
}
