<#
.SYNOPSIS
    Renews the Tailscale HTTPS certificate and restarts the service so Kestrel picks it up.

.DESCRIPTION
    Tailscale-issued certificates expire (currently ~90 days). Register this script as a monthly
    Windows scheduled task (see README "Deploy - Tailscale HTTPS") so the cert never goes stale
    unattended. `tailscale cert` is itself idempotent/safe to re-run before expiry; Kestrel only
    reads the cert files at startup, so the service still needs a restart after a renewal for the
    new cert to take effect.

.PARAMETER TailnetHost
    The full Tailscale hostname to certify, e.g. pickem.tailxxxx.ts.net.

.PARAMETER CertDir
    Folder to write <TailnetHost>.crt / .key into. Must match Kestrel__Certificates__Default__Path
    /KeyPath in deploy\.env.

.PARAMETER ServiceName
    Windows service to restart afterwards. Default NcaafPickEm.

.EXAMPLE
    ./deploy/renew-cert.ps1 -TailnetHost pickem.tailnet-1234.ts.net -CertDir C:\NcaafPickEm\cert
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $TailnetHost,

    [string] $CertDir = 'C:\NcaafPickEm\cert',

    [string] $ServiceName = 'NcaafPickEm'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command tailscale.exe -ErrorAction SilentlyContinue) -and -not (Get-Command tailscale -ErrorAction SilentlyContinue)) {
    throw 'tailscale CLI not found on PATH. Install Tailscale on this machine first.'
}

if (-not (Test-Path $CertDir)) {
    if ($PSCmdlet.ShouldProcess($CertDir, 'New-Item -ItemType Directory')) {
        New-Item -ItemType Directory -Path $CertDir -Force | Out-Null
    }
}

$certPath = Join-Path $CertDir "$TailnetHost.crt"
$keyPath = Join-Path $CertDir "$TailnetHost.key"

if ($PSCmdlet.ShouldProcess($TailnetHost, 'tailscale cert')) {
    Write-Host "Renewing Tailscale cert for $TailnetHost..."
    Push-Location $CertDir
    try {
        & tailscale cert --cert-file $certPath --key-file $keyPath $TailnetHost
        if ($LASTEXITCODE -ne 0) {
            throw "tailscale cert failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
    Write-Host "Wrote $certPath and $keyPath."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Restart-Service')) {
        Write-Host "Restarting service '$ServiceName' to pick up the renewed cert..."
        Restart-Service -Name $ServiceName -Force
        Write-Host 'Done.' -ForegroundColor Green
    }
} else {
    Write-Warning "Service '$ServiceName' not found; cert renewed but nothing restarted."
}
