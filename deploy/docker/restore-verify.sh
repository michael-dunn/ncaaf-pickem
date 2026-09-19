#!/usr/bin/env bash
# Prove the latest backup actually restores (P8-05). The Docker counterpart of
# deploy/restore-verify.ps1; a green `BACKUP DATABASE` exit code is not evidence of a restorable
# file, so run this by hand after the first nightly backup lands and periodically afterwards.
#
#   /srv/docker/ncaaf-pickem/restore-verify.sh
#
# It never touches the live database: the newest .bak is restored into a separate
# <DATABASE>_RestoreCheck, DBCC CHECKDB'd, row-counted, and dropped again - including when a
# check fails partway.
#
# Environment overrides: NCAAF_DB_CONTAINER, NCAAF_DATABASE.

set -euo pipefail

CONTAINER="${NCAAF_DB_CONTAINER:-ncaaf-db}"
DATABASE="${NCAAF_DATABASE:-NcaafPickEm}"
BACKUP_DIR="/var/opt/mssql/backups"
DATA_DIR="/var/opt/mssql/data"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"
SCRATCH="${DATABASE}_RestoreCheck"

if ! docker inspect --format '{{.State.Running}}' "$CONTAINER" 2>/dev/null | grep -q true; then
    echo "ERROR: container '$CONTAINER' is not running." >&2
    exit 1
fi

# Runs a T-SQL batch in the container. -b so a T-SQL error fails the script.
run_sql() {
    docker exec "$CONTAINER" /bin/bash -c \
        '"$2" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "$1"' _ "$1" "$SQLCMD"
}

# Same, but returns a single bare value (-h -1 drops the header, -W the padding).
run_scalar() {
    docker exec "$CONTAINER" /bin/bash -c \
        '"$2" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -h -1 -W -Q "$1"' _ "$1" "$SQLCMD" \
        | tr -d '\r' | grep -v '^$' | head -n 1
}

drop_scratch() {
    run_sql "IF DB_ID('${SCRATCH}') IS NOT NULL BEGIN ALTER DATABASE [${SCRATCH}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${SCRATCH}]; END" >/dev/null
    docker exec "$CONTAINER" rm -f "${DATA_DIR}/${SCRATCH}.mdf" "${DATA_DIR}/${SCRATCH}_log.ldf" || true
}

LATEST="$(docker exec "$CONTAINER" /bin/bash -c \
    'ls -1t "$1"/"$2"_*.bak 2>/dev/null | head -n 1' _ "$BACKUP_DIR" "$DATABASE" | tr -d '\r')"

if [[ -z "$LATEST" ]]; then
    echo "ERROR: no ${DATABASE}_*.bak in ${BACKUP_DIR}. Run backup.sh first." >&2
    exit 1
fi

echo "Restoring $(basename "$LATEST") into scratch database [${SCRATCH}]..."

# Clear a scratch database left behind by an earlier failed run.
drop_scratch
trap drop_scratch EXIT

# The logical file names are the SQL Server defaults for a database created as
# CREATE DATABASE [NcaafPickEm] - which is what EF Core's MigrateAsync does - so they are
# "<name>" and "<name>_log". If a future backup ever restores with "logical file is not part of
# database", list the real names with:
#   docker exec ncaaf-db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
#       -C -Q "RESTORE FILELISTONLY FROM DISK = N'<path-to.bak>'"
run_sql "RESTORE DATABASE [${SCRATCH}] FROM DISK = N'${LATEST}' WITH MOVE '${DATABASE}' TO N'${DATA_DIR}/${SCRATCH}.mdf', MOVE '${DATABASE}_log' TO N'${DATA_DIR}/${SCRATCH}_log.ldf', REPLACE, STATS = 25;"
echo "Restore succeeded."

echo "Running DBCC CHECKDB..."
run_sql "DBCC CHECKDB(N'${SCRATCH}') WITH NO_INFOMSGS, ALL_ERRORMSGS;"
echo "DBCC CHECKDB: no errors reported."

echo "Counting key tables..."
USERS="$(run_scalar "SET NOCOUNT ON; SELECT COUNT(*) FROM [${SCRATCH}].dbo.Users;")"
LEAGUES="$(run_scalar "SET NOCOUNT ON; SELECT COUNT(*) FROM [${SCRATCH}].dbo.Leagues;")"
PICKS="$(run_scalar "SET NOCOUNT ON; SELECT COUNT(*) FROM [${SCRATCH}].dbo.Picks;")"

echo "Users=${USERS}  Leagues=${LEAGUES}  Picks=${PICKS}"
echo
echo "RESTORE VERIFY PASSED for $(basename "$LATEST")."
