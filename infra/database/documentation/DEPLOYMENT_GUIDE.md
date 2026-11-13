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
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/review_regressions_test.sql
```

`review_regressions_test.sql` covers the review-driven contract: NULL-safe fare uniqueness, mandatory product duration, multiple unassigned payment/refund provider references, mandatory verified signature and non-NULL callback amount/currency, payment-event ownership, ticket-code uniqueness, and operator-scoped credential replacement. It runs in a transaction and rolls back.

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

The seven workflow functions (`hold_trip_capacity`, `confirm_capacity_hold`, `release_capacity_hold`, `expire_capacity_holds`, `confirm_payment_and_issue_tickets`, `cancel_unpaid_order`, `revoke_unused_ticket`) are `SECURITY DEFINER` with a pinned `search_path = pg_catalog, metro, public`, and their implicit `PUBLIC` EXECUTE grant is revoked. Grant them explicitly, for example:

```sql
GRANT EXECUTE ON FUNCTION metro.hold_trip_capacity(uuid, uuid, varchar, timestamptz) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.confirm_payment_and_issue_tickets(uuid, uuid, text, varchar, char, bigint, char, boolean) TO metro_runtime;
```

Because the functions run with the migration owner's privileges, keep `search_path` pinned and never create objects in `public` that shadow `metro` objects.

## Identity email uniqueness

`001_identity_and_access.sql` enforces FR-01.01 with a unique index on `public."AspNetUsers" ("NormalizedEmail") WHERE "NormalizedEmail" IS NOT NULL`, after a preflight that aborts the deployment with an actionable message if existing rows already contain duplicate normalized emails. Configure ASP.NET Core Identity with `UserOptions.RequireUniqueEmail = true` so duplicate registrations return a validation error instead of a database error.

## Production approvals still required

- Operator-specific legal retention durations and anonymization rules.
- Capacity profile values and effective versions.
- Fare/timetable/reference seed provenance.
- Payment adapter/provider contract and reconciliation procedures.
- Backup storage encryption, PITR retention and restore schedule.
