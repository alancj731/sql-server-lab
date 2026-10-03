# Backup and restore

**Implemented in Milestone 8.** Planned behaviour (from the build plan §17):

- Only for `Ready` labs with no other mutating job.
- `BACKUP DATABASE … TO URL` using managed identity where supported, unique blob path
  `{labId}/{database}/{utc}/{backupId}.bak`.
- Record type, size, checksum setting, timings, SQL version, LSNs, and the blob URI **without credentials**.
- `RESTORE VERIFYONLY`, then an optional test restore to a temporary database that is dropped afterwards.
- Retention via storage lifecycle policy plus application metadata. SAS tokens, if ever required, live only in
  Key Vault and are redacted from SQL and log output.

## Control database

Local SQLite: stop all processes and copy `.data/controlplane.db*`. Hosted Azure SQL: point-in-time restore.
