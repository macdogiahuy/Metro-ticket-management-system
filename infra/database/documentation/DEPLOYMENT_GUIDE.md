# Deployment guide

## BE-14 migration 014 production release gate

**Migration 014 has not been applied to Neon production.** This is a required
release procedure, as for migrations 010–013. Do not run production SQL or
enable the BE-14 rollout until all evidence below has been recorded in the
[BE-14 production release record](../../../docs/handover/BE-14-PRODUCTION-RELEASE.md).
That record starts **BLOCKED / NOT DEPLOYED**; empty fields are not approvals.

1. Obtain separate, explicit authorization naming the Neon project ID,
   production branch ID, database, release window, reviewed Git SHA and exact
   migration scope. Authorization for an earlier release, a PR approval or
   passing tests does not authorize 014. Read the target migration ledger first:
   the generated package applies **every pending migration through 014**, so
   any pending 010–013 must also be included in the authorized scope.
2. Before applying SQL, create a **new, separate database backup branch** from
   the confirmed production branch at the pre-release point. Record its actual
   ID, parent/source branch, creation time with timezone, snapshot LSN/time and
   retention. An existing BE-04 backup or a Git branch does not satisfy this
   release's backup requirement. Authorization to create a backup/rehearsal
   branch and authorization to apply production SQL must be recorded separately.
3. Verify recovery using a separate restore/rehearsal branch from that backup.
   Record the recovery procedure and results; compare the migration ledger,
   checksums and relevant data counts with the captured pre-release baseline.
   Keep the backup unchanged and retained throughout release acceptance.
4. Rehearse the exact reviewed package on the isolated rehearsal branch, then
   run `09_verify.sql`. Run acceptance tests only on a fresh `metro_test_*`
   database, never on production or the retained backup. Resolve the migration
   number collision with PR #90 before finalizing the release package; if the
   package or scope changes, repeat rehearsal and obtain matching authorization.
5. Immediately before deployment, recheck the release window, target IDs,
   backup/recovery evidence, approved Git/package checksums and current ledger.
   **Stop if any item is missing or differs from the approved release.** Only
   the explicitly authorized operator may apply the package with
   `ON_ERROR_STOP=1`, then run the verifier and BE-14 smoke checks. Record the
   resulting ledger and outcomes before marking the release complete.
6. If SQL fails before commit, its transaction rolls back; investigate before
   retrying. After commit, stop the affected application rollout if necessary.
   Any production restore or branch switch requires separate authorization and
   reconciliation of writes since the backup; do not automatically restore.

This gate is procedural: the raw SQL package does not verify human approvals
or Neon backup metadata. The commands below do not bypass this gate and are
not authorization to operate on production.

## Clean deployment

Use a new, dedicated non-production database for this example. Production
upgrades must satisfy the release gate above first.

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
tables. The current artifact contains fourteen checksummed migrations; `09_verify.sql`
requires migrations 012–014, the BE-09 issuance and BE-07 booking metadata,
the BE-14 operations tables, the 14 workflow functions, the SRS v1.2 and profile verification/purge tables, the
`ex_tickets_no_overlapping_validity` constraint and 25 retention triggers.

## BE-04 production release gate

Neon production was upgraded from 006 to **008**, then to **009** on 20 November
2026. The two database releases, retained backups and rollback details are in
the [production deployment record](../../../backend/docs/BE-04-PRODUCTION-007-008.md).
The application rollout is separate from the database release.

1. Obtain explicit user authorization for the named Neon project/production
   branch and release window. Separately obtain M01's PR review and explicit
   exception to the 400-line logic limit. Record links to those approvals in the
   PR; neither is implied by a test result.
2. Before production SQL or starting the new worker, create a **separate backup
   branch** from the production branch at the pre-release point, for example
   `backup/be04-pre-009-<release-id>`. Record its actual branch ID, source,
   creation time and restore procedure; verify it is usable. Retain it throughout
   release acceptance. A Git branch is not a database backup.
3. Create a separate rehearsal branch from that same production state. Check
   `metro.schema_migrations` and checksums, apply the generated `deploy.sql`
   with `ON_ERROR_STOP=1`, then run `09_verify.sql`. Exercise profile ownership,
   anonymous 401, non-Admin 403 and migrated legacy evidence using a private test
   bucket. Never run acceptance fixtures/tests against production or let a
   rehearsal worker delete objects from the production bucket.
4. In the authorized release window, pause profile writes/lifecycle workers,
   recheck the target branch and its 001-008 checksums, then apply the reviewed
   package once using the migration role/direct connection. Its transaction and
   checksum guard skip 001-008 and apply 009. Verify versions 1-9
   and run `09_verify.sql`. Grant the lifecycle runtime role only the needed
   `SELECT`, `INSERT`, and `DELETE` permissions on
   `metro.profile_eligibility_purge_queue` when the role lacks them. A separate
   runtime role also needs `UPDATE` on at least one column for
   `FOR UPDATE SKIP LOCKED`. Confirm its existing profile and audit privileges
   and deploy the new API and worker after schema verification succeeds.
5. Smoke-test auth/ownership and monitor purge retry warnings and queue depth
   without logging queue object keys or PII. Record deployment time, schema
   verification and smoke-test evidence before marking production complete.
6. If SQL fails before commit, the deployment transaction rolls back; keep the
   previous application running. If application rollout fails after schema
   commit, stop the new worker and roll back the application while retaining
   the additive schema/queue. Any restore or production branch switch needs
   explicit user authorization and reconciliation of writes since backup.
   A database backup does not restore object bytes already purged: coordinate
   object-storage recovery before restoring references to them.

Migration 008 is retained unchanged to preserve checksums of the deployed
production release and existing test/staging installations. Its
`pg_get_constraintdef LIKE '%status%'` matcher is a known maintenance risk.
Subsequent state-check changes must target the explicit
names `ck_profile_eligibility_status` and `ck_profile_eligibility_decision` in a
new migration, without matching constraint text. All constraints introduced by
009 already have explicit names. `09_verify.sql` requires both named workflow
checks to exist on `metro.profile_eligibility_requests` as validated CHECK
constraints. Run that verification after a clean deployment or upgrade; it does
not alter the deployed migration or its checksum.

## Validation tests

For the current package through 014, follow the
[backend acceptance harness](../../../backend/tests/README.md) using a completely
fresh dedicated `metro_test_*` database for each run. Do not use production or
the retained backup for fixtures/tests.

The standalone SQL commands below are **historical tests for migrations
005/006**, not acceptance evidence for 010–014. Run them only when intentionally
checking that historical schema in a disposable database:

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
