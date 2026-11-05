$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Helpers.ps1')
$appPath = Join-Path $PSScriptRoot 'bin\CodexUsageRings.exe'
if (-not (Test-Path -LiteralPath $appPath)) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$scheduled = Get-ScheduledTask -TaskName 'CodexUsageRings' -ErrorAction SilentlyContinue
if ($scheduled) {
    if (-not (Test-UsageTaskTarget $scheduled $PSScriptRoot)) {
        Write-Output 'Updating stale startup task to this installation.'
        & (Join-Path $PSScriptRoot 'AutoStart.ps1') -NoStart
    }
    Start-ScheduledTask -TaskName 'CodexUsageRings'
    Write-Output 'Usage rings started through independent Windows task.'
    return
}
$started = Start-Process -FilePath $appPath -WindowStyle Hidden -PassThru
Write-Output ('Usage rings launched. PID: ' + $started.Id)
