<#
.SYNOPSIS
    Redeploys NcaafPickEm.Api on the home server: runs pending EF Core migrations, republishes,
    restarts the Windows service, and verifies /health/ready.

.DESCRIPTION
    Refuses to run during the Saturday game window (Feature 10: "Deployments should not occur
    between the first kickoff and the last final on Saturdays") - from Saturday 10:00 ET through
    Sunday 03:00 ET, matching the SaturdayPoller's own close time - unless -Force is passed.

    Order of operations: guard -> migrate -> stop service -> publish -> start service -> poll
    /health/ready. Migrating before stopping the old service is deliberate: the currently-running
    (old) code and the new schema must never both be live for more than the few seconds the
    migration itself takes, and running it first means the service is down for the shortest
    possible window (just publish + start), not migrate + publish + start.

.PARAMETER Force
    Bypasses the Saturday deployment window guard.

.PARAMETER InstallDir
    Same meaning as install-service.ps1. Default C:\NcaafPickEm.

.PARAMETER ServiceName
    Same meaning as install-service.ps1. Default NcaafPickEm.

.PARAMETER EnvFile
    Path to the .env file supplying the connection string used for `dotnet ef database update`
    and the health-check base URL. Default deploy\.env next to this script.

.PARAMETER RepoRoot
    Path to the repository root. Default: two levels up from this script.

.PARAMETER WhatIf
    Standard ShouldProcess switch. Prints what would run (including whether the Saturday guard
    would block) without touching migrations, the service, or the filesystem.

.EXAMPLE
    ./deploy/deploy.ps1

.EXAMPLE
    # Bypass the Saturday guard for an emergency fix
    ./deploy/deploy.ps1 -Force

.EXAMPLE
    ./deploy/deploy.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [switch] $Force,

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
$infraProject = Join-Path $RepoRoot 'src\NcaafPickEm.Infrastructure'
$appDir = Join-Path $InstallDir 'app'
$logsDir = Join-Path $InstallDir 'logs'
$exePath = Join-Path $appDir 'NcaafPickEm.Api.exe'

function Test-InSaturdayWindow {
    <#
    Returns $true when "now" (converted to America/New_York) falls between Saturday 10:00 and
    the following Sunday 03:00, inclusive of the start and exclusive of the end - the window
    Feature 10 and the SaturdayPoller (04-Domain-Algorithms.md section 10) both treat as the
    Saturday game day.
    #>
    param([datetime] $NowUtc)

    $eastern = [System.TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
    $nowEt = [System.TimeZoneInfo]::ConvertTimeFromUtc($NowUtc, $eastern)

    # DayOfWeek: Sunday=0 .. Saturday=6.
    if ($nowEt.DayOfWeek -eq [System.DayOfWeek]::Saturday -and $nowEt.Hour -ge 10) {
        return $true
    }
    if ($nowEt.DayOfWeek -eq [System.DayOfWeek]::Sunday -and $nowEt.Hour -lt 3) {
        return $true
    }
    return $false
}

$nowUtc = [System.DateTime]::UtcNow
$inWindow = Test-InSaturdayWindow -NowUtc $nowUtc

if ($inWindow -and -not $Force) {
    Write-Error "Refusing to deploy: it is currently within the Saturday game window (Sat 10:00 ET - Sun 03:00 ET). Pass -Force to override."
    exit 1
}
if ($inWindow -and $Force) {
    Write-Warning "Deploying during the Saturday game window because -Force was passed."
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
$connectionString = $envValues['ConnectionStrings__Default']
if (-not $connectionString) {
    throw "ConnectionStrings__Default not found in $EnvFile."
}

Write-Host "Deploying to $InstallDir (service '$ServiceName')..."
Write-Host ''

# --- 1. Migrate first, while the old version is still serving traffic ---
# `dotnet ef database update` is the simple, always-works path and is what is used here; the
# alternative considered was `dotnet ef migrations bundle --self-contained -r win-x64` (produces
# a standalone efbundle.exe so the deploy machine does not need the SDK/dotnet-ef tool installed).
# The home server already has the .NET SDK and dotnet-ef installed for local development, so the
# bundle's only advantage - not needing them - does not apply here; `database update` is simpler
# to reason about and to WhatIf. If the server is ever locked down to the runtime only, switch to:
#   dotnet ef migrations bundle --self-contained -r win-x64 --project <infra> --startup-project <api> -o deploy\efbundle.exe
#   deploy\efbundle.exe --connection "<ConnectionStrings__Default>"
if ($PSCmdlet.ShouldProcess('database', 'dotnet ef database update')) {
    Write-Host 'Running migrations...'
    $env:ConnectionStrings__Default = $connectionString
    & dotnet ef database update --project $infraProject --startup-project $apiProject
    $migrateExitCode = $LASTEXITCODE
    Remove-Item Env:\ConnectionStrings__Default -ErrorAction SilentlyContinue
    if ($migrateExitCode -ne 0) {
        throw "dotnet ef database update failed with exit code $migrateExitCode."
    }
}

# --- 2. Stop the service ---
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    if ($WhatIfPreference) {
        # A dry run on a machine that never had install-service.ps1 run (e.g. a dev box used only
        # to sanity-check this script) should still report what *would* happen, not fail outright.
        Write-Warning "Service '$ServiceName' is not installed here; continuing -WhatIf as if it existed."
    } else {
        throw "Service '$ServiceName' is not installed. Run install-service.ps1 first."
    }
}
if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop-Service')) {
    Write-Host "Stopping service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', (New-TimeSpan -Seconds 30))
}

# --- 3. Publish ---
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

# --- 4. Start the service ---
if ($PSCmdlet.ShouldProcess($ServiceName, 'Start-Service')) {
    Write-Host "Starting service '$ServiceName'..."
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Running', (New-TimeSpan -Seconds 30))
}

# --- 5. Poll /health/ready ---
if ($WhatIfPreference) {
    Write-Host "What if: would poll /health/ready for up to $HealthTimeoutSeconds s."
    exit 0
}

$baseUrl = $envValues['ASPNETCORE_URLS']
if (-not $baseUrl) {
    $baseUrl = 'https://localhost:8443'
} else {
    $baseUrl = ($baseUrl -split ';')[0] -replace '0\.0\.0\.0', 'localhost'
}
$healthUrl = "$baseUrl/health/ready"

$originalCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$healthy = $false
try {
    Write-Host "Polling $healthUrl (up to $HealthTimeoutSeconds s)..."
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
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
    Write-Host "Deploy succeeded: /health/ready returned 200." -ForegroundColor Green
    exit 0
}

Write-Warning "Deploy finished but /health/ready did not return 200 within $HealthTimeoutSeconds s. Last 20 log lines:"
$latestLog = Get-ChildItem -Path $logsDir -Filter 'ncaaf-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latestLog) {
    Get-Content -Path $latestLog.FullName -Tail 20
} else {
    Write-Warning "No log file found under $logsDir."
}
exit 1
