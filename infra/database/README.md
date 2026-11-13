# Metro PostgreSQL deployment package

Production-oriented PostgreSQL 17 schema for the Metro ticket management system. The package is generated from immutable, checksummed migrations and covers identity, network operations, fares, ticketing, capacity, payments, refunds, AFC, support, analytics and automation.

## Prerequisites

- PostgreSQL 17 or later with UTF-8 server encoding.
- A deployment role that can create the `btree_gist` extension and the `metro` schema.
- Python 3 to rebuild `deploy.sql`.
- `psql` for deployment and verification.

Do not use the application login as the migration owner. Application roles should receive only the table/function privileges required by their service responsibilities.

## Package layout and execution order

1. `migrations/001_identity_and_access.sql`
2. `migrations/002_network_operations.sql`
3. `migrations/003_ticketing_finance_capacity.sql`
4. `migrations/004_support_analytics_automation.sql`
5. `migrations/005_transactional_capacity_retention.sql`

`build_deploy.py` combines those sources into `deploy.sql`, wraps them in one transaction, verifies migration checksums, and serializes deployments with a PostgreSQL advisory lock. Edit only migration sources; never hand-edit `deploy.sql`.

```powershell
python infra/database/build_deploy.py
python infra/database/build_deploy.py --check
psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/scripts/09_verify.sql
```

The deployment is repeatable. Reapplying an unchanged migration reports it as already applied. A checksum mismatch stops deployment and requires a new migration.

## Seed data

No production operator, user, credential, role assignment, fare, timetable, capacity, or retention policy is silently seeded. These values require an approved operator source/version. Test-only deterministic fixtures are under `tests/00_test_fixture.sql` and must never be loaded into production.

## Transactional API

Application services should call these functions inside their normal request transaction:

- `metro.hold_trip_capacity` — locks every affected segment in route order and creates an idempotent hold.
- `metro.confirm_capacity_hold` / `metro.release_capacity_hold` — transfer or release counters atomically.
- `metro.expire_capacity_holds` — bounded `SKIP LOCKED` expiry worker.
- `metro.confirm_payment_and_issue_tickets` — verifies the callback snapshot, confirms capacity, records the event, succeeds the payment and issues tickets in one transaction.
- `metro.cancel_unpaid_order` — cancels an unpaid order and releases its holds.
- `metro.revoke_unused_ticket` — revokes an unused ticket and active credentials without deleting history.

Do not update capacity counters or perform payment/ticket state transitions with ad-hoc SQL.

## Migration and upgrades

- Add a new numbered migration; never rewrite an applied migration.
- Rebuild and commit `deploy.sql` with the migration.
- Test from a clean database and against a copy of the previous schema version.
- Take and verify a backup before production deployment.
- Apply through one controlled release job, not independently from every application instance.

## Backup and restore

Example logical backup:

```powershell
pg_dump --format=custom --file=metro_ticket.dump metro_ticket
pg_restore --clean --if-exists --create --dbname=postgres metro_ticket.dump
```

Production must additionally meet SRS `NFR-AVL-003`: RPO no more than 15 minutes for tickets, payments, refunds, journeys and AFC events. Configure WAL archiving/PITR accordingly and rehearse restoration at least periodically (`NFR-AVL-005`).

## Retention and deletion

Tickets, orders, payments, payment events, refunds, journeys, AFC events, capacity allocations and audit history reject `DELETE` and `TRUNCATE`. Lifecycle status and approved anonymization are the supported mechanisms. Legal retention durations remain an operator-approved production input (`OI-001`, `NFR-PRI-005`).

See [DATABASE_DESIGN.md](documentation/DATABASE_DESIGN.md), [BUSINESS_RULE_MAPPING.md](documentation/BUSINESS_RULE_MAPPING.md), and [DEPLOYMENT_GUIDE.md](documentation/DEPLOYMENT_GUIDE.md).
