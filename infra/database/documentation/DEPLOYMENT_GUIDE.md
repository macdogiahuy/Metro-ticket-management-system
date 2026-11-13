# Deployment guide

## Clean deployment

1. Create a UTF-8 PostgreSQL 17 database owned by a migration role.
2. Back up any environment being upgraded.
3. Rebuild and verify the generated artifact.
4. Run with `ON_ERROR_STOP`.
5. Execute verification and application integration tests.

```powershell
createdb --encoding=UTF8 metro_ticket
python infra/database/build_deploy.py
python infra/database/build_deploy.py --check
psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/scripts/09_verify.sql
```

The deploy file uses one transaction, a 15-second lock timeout, a 5-minute statement timeout and an advisory transaction lock. It refuses PostgreSQL below version 17, non-UTF8 encoding, or an existing unversioned `metro` schema containing tables.

## Validation tests

Run tests only in a disposable database:

```powershell
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/00_test_fixture.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/business_rules_test.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/constraints_test.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/integrity_test.sql
```

For the concurrency test, start `concurrency_session_a.sql` and `concurrency_session_b.sql` at the same time, expect session A to succeed and session B to fail with capacity exceeded, then run `concurrency_assert.sql`.

## Upgrade procedure

- Stop or quiesce incompatible writers if a future migration requires it.
- Apply the new generated deploy artifact once.
- Verify `metro.schema_migrations` version/name/checksum.
- Run `09_verify.sql` and service smoke tests.
- Monitor lock waits, errors, payment reconciliation and capacity counter reconciliation.

Never change an applied migration. Roll forward with the next migration. If a release must be reverted, revert application traffic first and use a reviewed compensating migration. Destructive full-schema rollback is restricted to disposable environments.

## Maintenance

- Schedule `SELECT metro.expire_capacity_holds(500);` frequently enough that expired holds do not reduce saleable capacity for long.
- Monitor holds past expiry, capacity counters, unreconciled payments, unknown refunds and failed queues.
- Analyze/vacuum according to observed write volume; keep autovacuum enabled.
- Perform logical backups and WAL/PITR appropriate to the 15-minute critical-data RPO.
- Test restore procedures; a backup is not accepted until restored successfully.
- Review index usage/query plans before adding or removing indexes.

## Required application privilege posture

Use separate roles for migration, runtime and read-only reporting. The runtime role should not own protected tables and should receive EXECUTE on approved workflow functions instead of broad direct UPDATE rights to capacity/payment/ticket state. Auditors receive read-only access. Only the controlled migration role may alter/drop retention triggers.

## Production approvals still required

- Operator-specific legal retention durations and anonymization rules.
- Capacity profile values and effective versions.
- Fare/timetable/reference seed provenance.
- Payment adapter/provider contract and reconciliation procedures.
- Backup storage encryption, PITR retention and restore schedule.
