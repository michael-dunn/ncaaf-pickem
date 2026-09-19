<#
.SYNOPSIS
    Registers a daily Windows scheduled task that runs deploy\backup.ps1 at 03:45 local time.

.DESCRIPTION
    SQL Server Express has no SQL Server Agent, so Task Scheduler is the nightly-job mechanism
    (Feature 10). 03:45 is chosen to run well after the latest plausible Saturday-night activity
    (games and the poller's window close by 03:00 ET Sunday) and before anyone is likely to be
    using the app early morning.

.PARAMETER SqlServerInstance
    Passed through to backup.ps1. Default localhost.

.PARAMETER DatabaseName
    Passed through to backup.ps1. Default NcaafPickEm.

.PARAMETER BackupFolder
    Passed through to backup.ps1. Default C:\NcaafPickEm\backups.

.PARAMETER TaskName
    Scheduled task name. Default NcaafPickEm-NightlyBackup.

.EXAMPLE
    ./deploy/register-backup-task.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $SqlServerInstance = 'localhost',

    [string] $DatabaseName = 'NcaafPickEm',

    [string] $BackupFolder = 'C:\NcaafPickEm\backups',

    [string] $TaskName = 'NcaafPickEm-NightlyBackup'
)

$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'backup.ps1'
if (-not (Test-Path $scriptPath)) {
    throw "Could not find $scriptPath."
}

$argumentList = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -SqlServerInstance `"$SqlServerInstance`" -DatabaseName `"$DatabaseName`" -BackupFolder `"$BackupFolder`""
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argumentList
$trigger = New-ScheduledTaskTrigger -Daily -At '03:45AM'
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries

if ($PSCmdlet.ShouldProcess($TaskName, 'Register-ScheduledTask')) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Registered scheduled task '$TaskName' (daily 03:45 local)." -ForegroundColor Green
}
