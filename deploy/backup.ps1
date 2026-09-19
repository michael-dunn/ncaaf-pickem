<#
.SYNOPSIS
    Runs the nightly SQL Server backup (deploy\backup.sql via sqlcmd) and prunes .bak files older
    than 30 days.

.DESCRIPTION
    Feature 10: "nightly backups are taken and retained for at least 30 days." Registered as a
    Windows Task Scheduler job (deploy\register-backup-task.ps1) rather than a SQL Server Agent
    job, because SQL Server Express has no Agent.

.PARAMETER SqlServerInstance
    sqlcmd -S target. Default localhost (matches ConnectionStrings__Default's Server=localhost).

.PARAMETER DatabaseName
    Database to back up. Default NcaafPickEm.

.PARAMETER BackupFolder
    Destination folder for .bak files. Created if missing. Default C:\NcaafPickEm\backups.

.PARAMETER RetentionDays
    .bak files older than this are deleted after a successful backup. Default 30.

.EXAMPLE
    ./deploy/backup.ps1

.EXAMPLE
    ./deploy/backup.ps1 -SqlServerInstance "(localdb)\MSSQLLocalDB" -BackupFolder C:\temp\pickem-backup-test
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $SqlServerInstance = 'localhost',

    [string] $DatabaseName = 'NcaafPickEm',

    [string] $BackupFolder = 'C:\NcaafPickEm\backups',

    [int] $RetentionDays = 30
)

$ErrorActionPreference = 'Stop'

$sqlScript = Join-Path $PSScriptRoot 'backup.sql'
if (-not (Test-Path $sqlScript)) {
    throw "Could not find $sqlScript."
}

if (-not (Test-Path $BackupFolder)) {
    if ($PSCmdlet.ShouldProcess($BackupFolder, 'New-Item -ItemType Directory')) {
        New-Item -ItemType Directory -Path $BackupFolder -Force | Out-Null
    }
}

$dateStamp = Get-Date -Format 'yyyyMMdd'
$expectedFile = Join-Path $BackupFolder "$($DatabaseName)_$dateStamp.bak"

Write-Host "Backing up [$DatabaseName] on $SqlServerInstance to $expectedFile..."

if ($PSCmdlet.ShouldProcess($expectedFile, 'sqlcmd BACKUP DATABASE')) {
    # sqlcmd's `-v var=value` CLI parser (at least version 15.0.1300, the one on this machine)
    # mis-tokenizes any value containing a drive-letter colon (`-v BackupFolder=C:\temp` fails
    # with "Sqlcmd: ':\temp': Invalid argument", confirmed interactively; no combination of
    # quoting/spacing around `=` avoids it). backup.sql stays the documented, `$(Var)`-parameterized
    # source of truth (works fine from SSMS or an interactive sqlcmd session); this wrapper
    # substitutes the same three tokens itself and runs the result via -i with no -v involved.
    $resolvedSql = (Get-Content -Path $sqlScript -Raw) `
        -replace '\$\(DatabaseName\)', $DatabaseName `
        -replace '\$\(BackupFolder\)', $BackupFolder `
        -replace '\$\(DateStamp\)', $dateStamp
    $resolvedSqlPath = Join-Path ([System.IO.Path]::GetTempPath()) "ncaafpickem-backup-$([Guid]::NewGuid()).sql"
    Set-Content -Path $resolvedSqlPath -Value $resolvedSql -Encoding UTF8
    try {
        & sqlcmd -S $SqlServerInstance -i $resolvedSqlPath
        if ($LASTEXITCODE -ne 0) {
            throw "sqlcmd failed with exit code $LASTEXITCODE."
        }
    } finally {
        Remove-Item -Path $resolvedSqlPath -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path $expectedFile)) {
        throw "Backup command succeeded but $expectedFile was not created."
    }
    $sizeMb = [Math]::Round((Get-Item $expectedFile).Length / 1MB, 1)
    Write-Host "Backup written: $expectedFile ($sizeMb MB)." -ForegroundColor Green
}

# Prune backups older than the retention window.
$cutoff = (Get-Date).AddDays(-$RetentionDays)
$stale = Get-ChildItem -Path $BackupFolder -Filter "$DatabaseName`_*.bak" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $cutoff }

foreach ($file in $stale) {
    if ($PSCmdlet.ShouldProcess($file.FullName, 'Remove-Item (retention prune)')) {
        Write-Host "Pruning stale backup: $($file.Name) (last written $($file.LastWriteTime))"
        Remove-Item -Path $file.FullName -Force
    }
}

Write-Host "Retention: kept backups from the last $RetentionDays days, pruned $($stale.Count) older file(s)."
