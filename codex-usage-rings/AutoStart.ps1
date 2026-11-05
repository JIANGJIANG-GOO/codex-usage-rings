param([switch]$Remove, [switch]$NoStart)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Helpers.ps1')
$taskName = 'CodexUsageRings'
if ($Remove) {
    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
    Write-Output 'Usage Rings automatic startup removed.'
    return
}
$appPath = Join-Path $PSScriptRoot 'bin\CodexUsageRings.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Build the companion before registering startup.' }
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing -and -not (Test-UsageTaskTarget $existing $PSScriptRoot)) { Stop-UsageCompanion }
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$action = New-ScheduledTaskAction -Execute $appPath -WorkingDirectory $PSScriptRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
$principal = New-ScheduledTaskPrincipal -UserId $identity.User.Value -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Codex usage ring companion. Independent of Codex; shows while a Codex window is visible.' -Force | Out-Null
if (-not $NoStart) { Start-ScheduledTask -TaskName $taskName }
Get-ScheduledTask -TaskName $taskName | Select-Object TaskName,State
