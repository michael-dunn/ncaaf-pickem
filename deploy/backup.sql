-- deploy/backup.sql
-- Nightly full backup of the NcaafPickEm database, run by deploy/backup.ps1 via sqlcmd -v so the
-- database name and destination folder come from the caller rather than being hard-coded here.
--
-- Usage (backup.ps1 does this for you):
--   sqlcmd -S <server> -v DatabaseName="NcaafPickEm" BackupFolder="C:\NcaafPickEm\backups" DateStamp="20260101" -i deploy\backup.sql
--
-- No WITH COMPRESSION: backup compression is Standard/Enterprise/Developer only - SQL Server
-- Express rejects it outright (Msg 1844, "BACKUP DATABASE WITH COMPRESSION is not supported on
-- Express Edition"), confirmed against LocalDB (which reports as Express) while validating this
-- script. If the home server ever runs Standard or Developer edition instead, add COMPRESSION
-- back for a smaller .bak file. CHECKSUM verifies page checksums during the backup so a corrupt
-- page is caught here, not three weeks later during a restore; INIT overwrites rather than
-- appends, since each run writes its own dated filename and never targets an existing file.
BACKUP DATABASE [$(DatabaseName)]
TO DISK = N'$(BackupFolder)\$(DatabaseName)_$(DateStamp).bak'
WITH CHECKSUM, INIT,
    NAME = N'NcaafPickEm nightly backup',
    STATS = 10;
GO
