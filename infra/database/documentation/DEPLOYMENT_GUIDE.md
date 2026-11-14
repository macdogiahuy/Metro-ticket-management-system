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

The deploy file uses one transaction, a 15-second lock timeout, a 5-minute
statement timeout and an advisory transaction lock. It refuses PostgreSQL below
version 17, non-UTF8 encoding, or an existing unversioned `metro` schema containing
tables. The current artifact contains six checksummed migrations; `09_verify.sql`
requires migration 6, the 14 workflow functions, the 16 SRS v1.2 tables, the
`ex_tickets_no_overlapping_validity` constraint and 25 retention triggers.

## Validation tests

Run tests only in a disposable database:

```powershell
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/00_test_fixture.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/business_rules_test.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/constraints_test.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/integrity_test.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/review_regressions_test.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f infra/database/tests/srs_v1_2_test.sql
```

`review_regressions_test.sql` covers the review-driven contract: NULL-safe fare
uniqueness, mandatory product duration, multiple unassigned payment/refund provider
references, mandatory verified signature and non-NULL callback amount/currency,
payment-event ownership, ticket-code uniqueness, and operator-scoped credential
replacement.

`srs_v1_2_test.sql` covers the SRS v1.2 contract: guardian links and the active-pair
uniqueness, purchase consent, one current trip suggestion per order version, manual
kg load and the load-status view, free issuance without a payment row, overlap
rejection via the exclusion constraint, one approved disruption change, accessibility
slot capacity and release, data-quality day uniqueness, overdue lost property and
history retention. It runs in a transaction and rolls back.

For the legacy capacity concurrency test, start `concurrency_session_a.sql` and
`concurrency_session_b.sql` at the same time, expect session A to succeed and
session B to fail with capacity exceeded, then run `concurrency_assert.sql`. That
test exercises the v1.1 hold model only and does not describe a current flow.

## Upgrade to SRS v1.2 (from a v1.1 database)

`006_srs_v1_2_alignment.sql` is additive but it performs two preflights that can
abort the whole transaction:

- **One-passenger guard** — a trigger rejects `order_items.passenger_count > 1` for
  products with `capacity_controlled = false`. Legacy capacity-controlled group
  sales keep their historical shape so old fixtures and migrated rows still load.
- **Ticket overlap preflight** — before creating `ex_tickets_no_overlapping_validity`
  the migration scans `tickets` for holders with two `ACTIVE`/`IN_USE` rows whose
  `tstzrange(effective_from, expires_at)` overlap, and aborts with the offending
  `operator_id/holder_user_id` pairs. Resolve them first — typically by expiring,
  revoking or reversing the duplicate ticket — then re-run.

Backfill behaviour:

- every existing order gets version `1` in `order_versions`;
- tickets inherit `service_date`, origin/destination and `order_id`/`order_version`
  from their order item;
- `planned_trips.load_profile_id` is filled from `trip_capacity_segments` only when
  the whole trip maps to one capacity profile.

The migration is safe to re-run on the same database only through the normal
`schema_migrations` checksum guard; never edit an applied migration, roll forward
with a new one instead.

## Maintenance

SRS v1.2 has no quota counters to reconcile. Scheduled work:

- `SELECT metro.mark_overdue_lost_items(<operator_id>, 30);` daily, to move unclaimed
  found items and open reports to `OVERDUE` without deleting them.
- Keep `station_support_capacities.used_count` consistent by always confirming and
  releasing through `confirm_accessibility_request` / `release_accessibility_capacity`.
- Monitor unreconciled payments, unknown refunds, failed notification deliveries and
  failed queues.
- Analyze/vacuum according to observed write volume; keep autovacuum enabled.
- Perform logical backups and WAL/PITR appropriate to the 15-minute critical-data RPO.
- Test restore procedures; a backup is not accepted until restored successfully.
- Review index usage/query plans before adding or removing indexes.

Legacy only: `SELECT metro.expire_capacity_holds(500);` still exists so an upgraded
v1.1 environment does not leak holds, but no new flow creates them.

## Required application privilege posture

Use separate roles for migration, runtime and read-only reporting. The runtime role
should not own protected tables and should receive EXECUTE on approved workflow
functions instead of broad direct UPDATE rights to ticket/order/payment state.
Auditors receive read-only access. Only the controlled migration role may
alter/drop retention triggers.

The thirteen `SECURITY DEFINER` functions use a pinned
`search_path = pg_catalog, metro, public` and their implicit `PUBLIC` EXECUTE grant
is revoked. Grant them explicitly, for example:

```sql
GRANT EXECUTE ON FUNCTION metro.confirm_payment_and_issue_tickets(uuid, uuid, text, varchar, char, bigint, char, boolean) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.issue_free_order(uuid, uuid, text, text) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.cancel_unpaid_order(uuid, uuid, text, text) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.revoke_unused_ticket(uuid, uuid, text) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.apply_ticket_change(uuid, uuid, text) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.confirm_accessibility_request(uuid, uuid, text) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.release_accessibility_capacity(uuid, uuid) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.mark_overdue_lost_items(uuid, integer) TO metro_runtime;
GRANT EXECUTE ON FUNCTION metro.validate_load_profile(uuid, uuid) TO metro_runtime;
```

The four legacy capacity functions (`hold_trip_capacity`, `confirm_capacity_hold`,
`release_capacity_hold`, `expire_capacity_holds`) are also `SECURITY DEFINER`; grant
them only if an upgraded environment still needs to drain v1.1 data.

Because the functions run with the migration owner's privileges, keep `search_path`
pinned and never create objects in `public` that shadow `metro` objects.

## Identity email uniqueness

`001_identity_and_access.sql` enforces FR-01.01 with a unique index on
`public."AspNetUsers" ("NormalizedEmail") WHERE "NormalizedEmail" IS NOT NULL`, after
a preflight that aborts the deployment with an actionable message if existing rows
already contain duplicate normalized emails. Configure ASP.NET Core Identity with
`UserOptions.RequireUniqueEmail = true` so duplicate registrations return a
validation error instead of a database error.

## Production approvals still required

- Operator-specific legal retention durations and anonymization rules.
- Physical load limits (`cars.max_load_kg`, `capacity_profiles.max_load_kg`) and the
  station support-slot capacity per 30-minute window.
- Fare/timetable/reference seed provenance.
- Payment adapter/provider contract and reconciliation procedures.
- Backup storage encryption, PITR retention and restore schedule.
