<#
.SYNOPSIS
    Publishes NcaafPickEm.Api (Release, trimmed, Brotli) and installs/updates it as a Windows
    service on the home server.

.DESCRIPTION
    Idempotent: safe to re-run. On a re-run it stops the existing service, republishes over the
    same install directory, and starts it again. On a first run it creates the service, points it
    at the published exe, sets recovery options (auto-restart on crash), and writes the .env file's
    keys into the service's own registry environment - not the machine-wide environment - so other
    processes on the box never see them.

    Windows services do not expose a "working directory" the way a scheduled task or a shortcut
    does; the SCM always launches a service with the current directory set to
    %SystemRoot%\System32. Verified this does not matter here: launched NcaafPickEm.Api.exe with
    -WorkingDirectory C:\Windows\System32 and it still logged
    "Content root path: <its own publish folder>" and served /health/ready 200 - WebApplication's
    minimal hosting model resolves its content root from the executable's own directory
    (AppContext.BaseDirectory), not the process's current directory. Flagged here only because it
    is the first thing to re-check if a future change makes the app read a relative path some
    other way.

.PARAMETER InstallDir
    Root folder for the published app, logs, and backups. Default C:\NcaafPickEm.

.PARAMETER ServiceName
    Windows service name. Default NcaafPickEm.

.PARAMETER EnvFile
    Path to the .env file (KEY=VALUE per line) supplying every configuration key. Default
    deploy\.env next to this script. Never commit this file; deploy\.env.example documents every
    key.

.PARAMETER RepoRoot
    Path to the repository root. Default: two levels up from this script
    (deploy\install-service.ps1 -> repo root).

.PARAMETER WhatIf
    Standard ShouldProcess switch. Prints every action (publish target, service create/update,
    registry keys that would be written, start) without doing any of them. Useful for a dry run on
    a machine where dotnet publish or sc.exe should not actually execute.

.EXAMPLE
    ./deploy/install-service.ps1

.EXAMPLE
    ./deploy/install-service.ps1 -InstallDir D:\Apps\NcaafPickEm -EnvFile D:\Apps\NcaafPickEm.env

.EXAMPLE
    ./deploy/install-service.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string] $InstallDir = 'C:\NcaafPickEm',

    [string] $ServiceName = 'NcaafPickEm',

    [string] $EnvFile,

    [string] $RepoRoot,

    [int] $HealthTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
if (-not $EnvFile) {
    $EnvFile = Join-Path $PSScriptRoot '.env'
}

$apiProject = Join-Path $RepoRoot 'src\NcaafPickEm.Api'
if (-not (Test-Path $apiProject)) {
    throw "Could not find the Api project at $apiProject. Pass -RepoRoot explicitly."
}

if (-not (Test-Path $EnvFile)) {
    throw "Env file not found at $EnvFile. Copy deploy\.env.example to deploy\.env and fill it in first."
}

function Read-EnvFile {
    param([Parameter(Mandatory)][string] $Path)

    $result = [ordered]@{}
    foreach ($rawLine in Get-Content -Path $Path) {
        $line = $rawLine.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) {
            continue
        }
        $eq = $line.IndexOf('=')
        if ($eq -lt 1) {
            continue
        }
        $key = $line.Substring(0, $eq).Trim()
        $value = $line.Substring($eq + 1)
        $result[$key] = $value
    }
    return $result
}

$envValues = Read-EnvFile -Path $EnvFile

# Deploy-script-only keys (BACKUP_*) are not part of the app's own environment; every other key
# is written into the service's environment below.
$deployOnlyKeys = @('BACKUP_DATABASE_NAME', 'BACKUP_FOLDER', 'BACKUP_SQL_SERVER_INSTANCE')
$appEnvKeys = $envValues.Keys | Where-Object { $deployOnlyKeys -notcontains $_ }

if ($appEnvKeys.Count -eq 0) {
    throw "$EnvFile has no usable KEY=VALUE lines."
}

$appDir = Join-Path $InstallDir 'app'
$logsDir = Join-Path $InstallDir 'logs'
$backupsDir = Join-Path $InstallDir 'backups'
$exePath = Join-Path $appDir 'NcaafPickEm.Api.exe'

Write-Host "Install dir : $InstallDir"
Write-Host "Service name: $ServiceName"
Write-Host "Env file    : $EnvFile ($($appEnvKeys.Count) app keys)"
Write-Host ''

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

# --- 1. Stop the existing service (if any) before we overwrite its files ---
if ($existingService -and $existingService.Status -ne 'Stopped') {
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop-Service')) {
        Write-Host "Stopping existing service '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
        $existingService.WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
    }
}

# --- 2. Create install directories ---
foreach ($dir in @($InstallDir, $appDir, $logsDir, $backupsDir)) {
    if (-not (Test-Path $dir)) {
        if ($PSCmdlet.ShouldProcess($dir, 'New-Item -ItemType Directory')) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }
}

# --- 3. Publish Release (trimmed, Brotli - the Web project's csproj sets PublishTrimmed and
#        CompressionEnabled for Release; do NOT pass TrimMode/AOT switches here, see D-025) ---
if ($PSCmdlet.ShouldProcess("dotnet publish -> $appDir", 'Publish Release')) {
    Write-Host "Publishing Release to $appDir..."
    & dotnet publish $apiProject -c Release -o $appDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path $exePath)) {
        throw "Publish completed but $exePath was not produced."
    }
}

# --- 4. Create or update the service definition ---
$serviceExists = [bool](Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)
if (-not $serviceExists) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "sc.exe create (binPath=$exePath)")) {
        Write-Host "Creating service '$ServiceName'..."
        $binPath = '"' + $exePath + '"'
        & sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "NCAAF Pick Em" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "sc.exe create failed with exit code $LASTEXITCODE."
        }
        & sc.exe description $ServiceName "NCAAF Pick Em web app (P8-02, deploy/install-service.ps1)." | Out-Null

        # Auto-restart on crash: reset the failure counter after a day, restart 5s after each
        # failure, up to the SCM's own default retry ceiling.
        & sc.exe failure $ServiceName reset= 86400 actions= restart/5000 | Out-Null
    }
} else {
    Write-Host "Service '$ServiceName' already exists; reusing it (binPath unchanged: $exePath)."
}

# --- 5. Write the .env keys into the service's own registry environment (not machine-wide) ---
# Services read a REG_MULTI_SZ "Environment" value under their own registry key; each entry is
# "NAME=VALUE". This is the documented, reliable way to give a Windows service its own env vars
# without touching [Environment]::SetEnvironmentVariable('...', '...', 'Machine'), which would
# leak into every other process on the box.
$serviceRegKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$envLines = foreach ($key in $appEnvKeys) { "$key=$($envValues[$key])" }

if ($PSCmdlet.ShouldProcess($serviceRegKey, 'Set-ItemProperty Environment (REG_MULTI_SZ)')) {
    if (-not (Test-Path $serviceRegKey)) {
        throw "Service registry key $serviceRegKey does not exist yet - was service creation skipped by -WhatIf?"
    }
    New-ItemProperty -Path $serviceRegKey -Name 'Environment' -Value $envLines -PropertyType MultiString -Force | Out-Null
    Write-Host "Wrote $($envLines.Count) environment entries to $serviceRegKey\Environment."
}

# --- 6. Start the service and poll /health/ready ---
if ($PSCmdlet.ShouldProcess($ServiceName, 'Start-Service')) {
    Write-Host "Starting service '$ServiceName'..."
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Running', (New-TimeSpan -Seconds 30))

    $baseUrl = $envValues['ASPNETCORE_URLS']
    if (-not $baseUrl) {
        $baseUrl = 'https://localhost:8443'
    } else {
        # ASPNETCORE_URLS can list several bindings separated by ';'; use the first for the probe
        # and swap a wildcard host for localhost so the probe works from the box itself.
        $baseUrl = ($baseUrl -split ';')[0] -replace '0\.0\.0\.0', 'localhost'
    }
    $healthUrl = "$baseUrl/health/ready"

    # The Tailscale cert is only trusted on the tailnet's own resolver chain, and a self-signed
    # placeholder cert is not trusted at all - the probe target is our own freshly-issued cert
    # either way, so skip validation here rather than requiring -SkipCertificateCheck (PS7-only;
    # this script also runs under Windows PowerShell 5.1).
    $originalCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    try {
        Write-Host "Polling $healthUrl (up to $HealthTimeoutSeconds s)..."
        $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
        $healthy = $false
        while ((Get-Date) -lt $deadline) {
            try {
                $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
                if ($response.StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            } catch {
                # Not up yet; keep polling.
            }
            Start-Sleep -Seconds 2
        }
    } finally {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $originalCallback
    }

    if ($healthy) {
        Write-Host "Service is up and /health/ready returned 200." -ForegroundColor Green
    } else {
        Write-Warning "Service started but /health/ready did not return 200 within $HealthTimeoutSeconds s."
        Write-Warning "Check logs under $logsDir\ and 'Get-EventLog -LogName Application -Source $ServiceName' (or Event Viewer)."
        exit 1
    }
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host "Reboot check: restart the machine, then confirm 'Get-Service $ServiceName' shows Running (start type is Automatic already)."
