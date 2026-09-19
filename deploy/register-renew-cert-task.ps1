<#
.SYNOPSIS
    Registers a monthly Windows scheduled task that runs deploy\renew-cert.ps1.

.PARAMETER TailnetHost
    Passed straight through to renew-cert.ps1.

.PARAMETER CertDir
    Passed straight through to renew-cert.ps1. Default C:\NcaafPickEm\cert.

.PARAMETER TaskName
    Scheduled task name. Default NcaafPickEm-RenewCert.

.EXAMPLE
    ./deploy/register-renew-cert-task.ps1 -TailnetHost pickem.tailnet-1234.ts.net
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $TailnetHost,

    [string] $CertDir = 'C:\NcaafPickEm\cert',

    [string] $TaskName = 'NcaafPickEm-RenewCert'
)

$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'renew-cert.ps1'
if (-not (Test-Path $scriptPath)) {
    throw "Could not find $scriptPath."
}

$argumentList = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -TailnetHost `"$TailnetHost`" -CertDir `"$CertDir`""
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argumentList

# Monthly, on the 1st, at 04:15 local time - certs expire on the order of months, so monthly gives
# ample margin without needing calendar math for a specific expiry date.
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -WeeksInterval 4 -At '04:15AM'
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries

if ($PSCmdlet.ShouldProcess($TaskName, 'Register-ScheduledTask')) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Registered scheduled task '$TaskName' (every 4 weeks, Sunday 04:15 local)." -ForegroundColor Green
}
