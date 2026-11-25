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
7. `migrations/007_profile_eligibility_evidence.sql`
8. `migrations/008_profile_verification_workflow.sql`
9. `migrations/009_profile_evidence_purge_queue.sql`
10. `migrations/010_ticket_issuance.sql`
11. `migrations/011_paid_ticket_issuance.sql`
12. `migrations/012_free_child_ticket_issuance.sql`
13. `migrations/013_booking_requests.sql`

`build_deploy.py` combines those sources into `deploy.sql`, wraps them in one transaction, verifies migration checksums, and serializes deployments with a PostgreSQL advisory lock. Edit only migration sources; never hand-edit `deploy.sql`.

```powershell
python infra/database/build_deploy.py
python infra/database/build_deploy.py --check
psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d metro_ticket -f infra/database/scripts/09_verify.sql
```

BE-04 owns the already-deployed profile versions 007–009; BE-09 appends 010–012 without changing any 001–009 source/checksum.
BE-07 appends 013: transactional booking idempotency, payment attempt numbers (maximum three per order),
the stored Checkout URL used for replay, and the no-trip suggestion constraint. It preserves migrations 001–012.

The deployment is repeatable. Reapplying an unchanged migration reports it as already applied. A checksum mismatch stops deployment and requires a new migration.

BE-10 appends local migrations 014–016 for atomic gate receipts, immutable passage snapshots,
evidence-based resolutions/corrections, verified QR support, encrypted email queue and shared
SRS email-code policy (five-minute expiry, cooldown/quota across purposes). Migrations 001–013 are unchanged.
See [BE-10 integration notes](../../docs/handover/BE-10-GATE-VALIDATION.md), including the
migration-number overlap with the unmerged BE-14 branch.

## Seed data

No production operator, user, credential, role assignment, fare, timetable, capacity, or retention policy is silently seeded. These values require an approved operator source/version. Test-only deterministic fixtures are under `tests/00_test_fixture.sql` and must never be loaded into production.

## Transactional API

Application services should call these functions inside their normal request transaction:

- `metro.confirm_payment_and_issue_tickets` — verifies the callback snapshot, records the event, succeeds the payment and creates the pending ticket/job in one transaction. BE-09 prepares QR before marking the order ISSUED; see [BE-09 handover](../../docs/handover/BE-09-IMPLEMENTATION.md).
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

Current local acceptance through migration 016 uses real PostgreSQL through `backend/tests/Run-PostgresTests.ps1`. Deploy `deploy.sql`, run `scripts/09_verify.sql`, then follow [backend test setup](../../backend/tests/README.md). BE-09 tests cover paid/free SQL contracts, locking/races, current QR, eligibility, history and HTTP ownership. BE-07 adds Booking/consent, immutable retry attempts, unresolved payment guards, free child Checkout and trip suggestion history. BE-10 adds gate decisions/journeys, verified QR replacement, shared email-code policy, encrypted delivery and administrator support review. Never run fixtures against a shared DB.

The standalone SQL files under `tests/` predate BE-09 migration 010 and preserve historical migration 005/006 expectations (group capacity issuance and orders immediately ISSUED). They are historical regression assets for those schema revisions, not the acceptance suite for the current Metro issuance contract. Do not run that old ordered fixture chain as validation of 010–012 or interpret its immediate-ISSUED assertions as current requirements.

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
