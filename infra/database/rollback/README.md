# Rollback policy

Production schema changes are forward-only. Applied migrations are checksummed, and financial/ticket history must not be deleted. Revert application traffic and deploy a reviewed compensating migration when rollback is required.

For a disposable test database, drop the entire database with an explicit name:

```powershell
dropdb metro_ticket_test
```

Do not run schema/table drop scripts against a shared, staging, or production database. Backups and PITR are the recovery mechanisms for data loss/corruption; migration rollback is not a substitute.
