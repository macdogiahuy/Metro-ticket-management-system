# Metro PostgreSQL deployment package

Production-oriented PostgreSQL 17 schema for the Metro ticket management system. The package is generated from immutable, checksummed migrations and implements SRS v1.2: identity, network operations, fares, order versions, trip suggestions, ticketing, manual kg load, payments, refunds, AFC, guardianship, support, lost property, analytics and automation. The v1.1 capacity-hold tables are retained as legacy compatibility only.

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
6. `migrations/006_srs_v1_2_alignment.sql`

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

- `metro.confirm_payment_and_issue_tickets` — verifies the callback snapshot, records the event, succeeds the payment and issues the ticket in one transaction.
- `metro.issue_free_order` — issues a fully discounted free child ticket without a payment row or fake `paid_at`.
- `metro.cancel_unpaid_order` — cancels an unpaid order across both the current and legacy models.
- `metro.revoke_unused_ticket` — revokes an unused ticket and active credentials without deleting history.
- `metro.apply_ticket_change` — applies one approved disruption change, revokes the old QR and keeps the change history.
- `metro.confirm_accessibility_request` / `metro.release_accessibility_capacity` — consume or release a station support slot atomically.
- `metro.mark_overdue_lost_items` — moves unclaimed found items and open lost reports to `OVERDUE`.
- `metro.validate_load_profile` — checks car load limits against the trip limit.
- Legacy only: `metro.hold_trip_capacity`, `metro.confirm_capacity_hold`, `metro.release_capacity_hold`, `metro.expire_capacity_holds` (SRS v1.1 quota model; new flows never hold capacity).

Do not update capacity counters, ticket validity or payment/ticket state transitions with ad-hoc SQL.

All workflow functions are `SECURITY DEFINER` with a pinned `search_path` and no `PUBLIC` EXECUTE grant, so a runtime role needs explicit `GRANT EXECUTE` and no direct table write access. See [DEPLOYMENT_GUIDE.md](documentation/DEPLOYMENT_GUIDE.md#required-application-privilege-posture).

## Validation tests

Run against a disposable database after `deploy.sql`, in this order:

```powershell
foreach ($f in "00_test_fixture.sql","business_rules_test.sql","constraints_test.sql","integrity_test.sql","review_regressions_test.sql","srs_v1_2_test.sql") {
    psql -v ON_ERROR_STOP=1 -d metro_ticket_test -f "infra/database/tests/$f"
}
```

`review_regressions_test.sql` locks in the review-driven behaviour: NULL-safe fare keys, mandatory product duration, multiple unassigned provider references, verified-signature and non-NULL payment snapshots, payment-event ownership, unique ticket codes, and operator-scoped credential replacement. `srs_v1_2_test.sql` covers guardian links, purchase consent, current trip suggestions, manual kg load, ETA, free issuance, overlapping-ticket validity, ticket changes, accessibility slots, data-quality days, lost property and history retention.

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

Tickets, orders/versions, consents, payments, payment events, refunds, journeys, AFC events, ticket changes, suggestions, support/accessibility history, lost property and audit history reject `DELETE` and `TRUNCATE`. Lifecycle status and approved anonymization are the supported mechanisms. Legal retention durations remain an operator-approved production input (`OI-001`, `NFR-PRI-005`).

See [DATABASE_DESIGN.md](documentation/DATABASE_DESIGN.md), [BUSINESS_RULE_MAPPING.md](documentation/BUSINESS_RULE_MAPPING.md), and [DEPLOYMENT_GUIDE.md](documentation/DEPLOYMENT_GUIDE.md).
