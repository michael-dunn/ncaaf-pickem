<#
.SYNOPSIS
    Restores the most recent .bak file into a scratch database, runs DBCC CHECKDB, counts a few
    key tables as a sanity check, then drops the scratch database.

.DESCRIPTION
    "Verify restore once" (P8-02 task card) means: prove a backup file is actually restorable,
    not just that BACKUP DATABASE exited 0. Run this by hand after the first backup lands, and
    periodically afterwards (there is no code-review substitute for actually restoring one).

    Never restores over the real NcaafPickEm database - always a separate "<DatabaseName>_RestoreCheck"
    database, dropped at the end whether the checks pass or fail.

.PARAMETER SqlServerInstance
    sqlcmd -S target / EF connection server. Default localhost.

.PARAMETER DatabaseName
    Source database name (used to find the latest .bak and to name the scratch database
    "<DatabaseName>_RestoreCheck"). Default NcaafPickEm.

.PARAMETER BackupFolder
    Folder to find the latest .bak in. Default C:\NcaafPickEm\backups.

.PARAMETER DataDirectory
    Folder SQL Server should place the restored .mdf/.ldf files in. Default: same as
    BackupFolder's parent \data (created if missing); on a real SQL Server instance this should
    normally be the instance's own default data directory instead - pass it explicitly there.

.EXAMPLE
    ./deploy/restore-verify.ps1

.EXAMPLE
    ./deploy/restore-verify.ps1 -SqlServerInstance "(localdb)\MSSQLLocalDB" -BackupFolder C:\temp\pickem-backup-test -DataDirectory C:\temp\pickem-backup-test\data
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $SqlServerInstance = 'localhost',

    [string] $DatabaseName = 'NcaafPickEm',

    [string] $BackupFolder = 'C:\NcaafPickEm\backups',

    [string] $DataDirectory
)

$ErrorActionPreference = 'Stop'

$latest = Get-ChildItem -Path $BackupFolder -Filter "$DatabaseName`_*.bak" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $latest) {
    throw "No $DatabaseName`_*.bak files found in $BackupFolder. Run backup.ps1 first."
}

if (-not $DataDirectory) {
    $DataDirectory = Join-Path $BackupFolder 'data'
}
if (-not (Test-Path $DataDirectory)) {
    if ($PSCmdlet.ShouldProcess($DataDirectory, 'New-Item -ItemType Directory')) {
        New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
    }
}

$restoreDbName = "$($DatabaseName)_RestoreCheck"
$mdfPath = Join-Path $DataDirectory "$restoreDbName.mdf"
$ldfPath = Join-Path $DataDirectory "$restoreDbName.ldf"

Write-Host "Restoring $($latest.Name) into scratch database [$restoreDbName]..."

function Invoke-Sql {
    param([Parameter(Mandatory)][string] $Query)

    & sqlcmd -S $SqlServerInstance -Q $Query -b
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit $LASTEXITCODE) running: $Query"
    }
}

function Invoke-SqlScalar {
    param([Parameter(Mandatory)][string] $Query)

    # -h -1 suppresses the column header/row-count footer so the single value comes back clean.
    $raw = & sqlcmd -S $SqlServerInstance -Q $Query -h -1 -W
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit $LASTEXITCODE) running: $Query"
    }
    return ($raw | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim()
}

if ($PSCmdlet.ShouldProcess($restoreDbName, 'RESTORE DATABASE')) {
    try {
        # Drop a leftover scratch database from a previous failed run before restoring again.
        Invoke-Sql "IF DB_ID('$restoreDbName') IS NOT NULL BEGIN ALTER DATABASE [$restoreDbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$restoreDbName]; END"

        $restoreQuery = @"
RESTORE DATABASE [$restoreDbName]
FROM DISK = N'$($latest.FullName)'
WITH MOVE '$DatabaseName' TO N'$mdfPath',
     MOVE '$($DatabaseName)_log' TO N'$ldfPath',
     REPLACE, STATS = 10;
"@
        Invoke-Sql $restoreQuery
        Write-Host "Restore succeeded." -ForegroundColor Green

        Write-Host 'Running DBCC CHECKDB...'
        Invoke-Sql "DBCC CHECKDB(N'$restoreDbName') WITH NO_INFOMSGS, ALL_ERRORMSGS;"
        Write-Host "DBCC CHECKDB: no errors reported." -ForegroundColor Green

        Write-Host 'Counting key tables...'
        $userCount = Invoke-SqlScalar "SET NOCOUNT ON; SELECT COUNT(*) FROM [$restoreDbName].dbo.Users;"
        $leagueCount = Invoke-SqlScalar "SET NOCOUNT ON; SELECT COUNT(*) FROM [$restoreDbName].dbo.Leagues;"
        $pickCount = Invoke-SqlScalar "SET NOCOUNT ON; SELECT COUNT(*) FROM [$restoreDbName].dbo.Picks;"

        Write-Host ("Users={0}  Leagues={1}  Picks={2}" -f $userCount, $leagueCount, $pickCount)

        Write-Host ''
        Write-Host "RESTORE VERIFY PASSED for $($latest.Name)." -ForegroundColor Green
    } finally {
        Write-Host "Dropping scratch database [$restoreDbName]..."
        Invoke-Sql "IF DB_ID('$restoreDbName') IS NOT NULL BEGIN ALTER DATABASE [$restoreDbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$restoreDbName]; END"
        Remove-Item -Path $mdfPath, $ldfPath -Force -ErrorAction SilentlyContinue
    }
}
