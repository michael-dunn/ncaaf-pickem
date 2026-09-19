#!/usr/bin/env bash
# Nightly SQL Server backup for the Docker deployment (P8-05).
#
# Runs from the HOST's cron, against the db container. Install with:
#   sudo crontab -e
#   45 3 * * * /srv/docker/ncaaf-pickem/backup.sh >> /var/log/ncaaf-backup.log 2>&1
#
# The .bak lands in /var/opt/mssql/backups INSIDE the container, which is part of the
# /srv/docker/configs/ncaaf-pickem/mssql bind mount, so the host sees it at
#   /srv/docker/configs/ncaaf-pickem/mssql/backups/NcaafPickEm_YYYYMMDD.bak
# Back that directory up off-box too - a bind mount on the same disk is not a backup.
#
# The sa password is never passed in from here: it is already inside the container as
# MSSQL_SA_PASSWORD, so nothing sensitive appears in the host's process list or in cron's mail.
#
# Environment overrides: NCAAF_DB_CONTAINER, NCAAF_DATABASE, NCAAF_RETENTION_DAYS.

set -euo pipefail

CONTAINER="${NCAAF_DB_CONTAINER:-ncaaf-db}"
DATABASE="${NCAAF_DATABASE:-NcaafPickEm}"
RETENTION_DAYS="${NCAAF_RETENTION_DAYS:-30}"
BACKUP_DIR="/var/opt/mssql/backups"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

STAMP="$(date +%Y%m%d)"
TARGET="${BACKUP_DIR}/${DATABASE}_${STAMP}.bak"

if ! docker inspect --format '{{.State.Running}}' "$CONTAINER" 2>/dev/null | grep -q true; then
    echo "ERROR: container '$CONTAINER' is not running; nothing was backed up." >&2
    exit 1
fi

echo "$(date -Is) backing up [$DATABASE] to $TARGET (container $CONTAINER)"

# WITH CHECKSUM verifies page checksums as it writes; INIT overwrites same-day reruns rather
# than appending a second backup set to the same file. No COMPRESSION: it is an
# Standard/Enterprise-only option and the image's Developer edition accepts it, but the Windows
# path (deploy/backup.sql) had to drop it for Express, and keeping the two identical means a
# .bak from either host restores the same way.
#
# -b makes sqlcmd exit non-zero on a T-SQL error; without it a failed BACKUP still exits 0 and
# cron reports success forever. The SQL arrives as $1 so no quoting of it happens twice.
docker exec "$CONTAINER" /bin/bash -c '
    set -euo pipefail
    mkdir -p "$2"
    "$3" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "$1"
' _ "BACKUP DATABASE [${DATABASE}] TO DISK = N'${TARGET}' WITH CHECKSUM, INIT, STATS = 25;" \
    "$BACKUP_DIR" "$SQLCMD"

# Pruned inside the container, where the files are owned by the mssql user (uid 10001); on the
# host they would need root. -mtime +N is "older than N full days".
docker exec "$CONTAINER" find "$BACKUP_DIR" \
    -maxdepth 1 -type f -name "${DATABASE}_*.bak" -mtime "+${RETENTION_DAYS}" -print -delete

echo "$(date -Is) backup complete: $TARGET"
echo "Remaining backups:"
docker exec "$CONTAINER" ls -lh "$BACKUP_DIR"
