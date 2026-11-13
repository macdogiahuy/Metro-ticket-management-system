# Database design

## Architecture

The target engine is PostgreSQL 17. Domain objects use the `metro` schema; ASP.NET Core Identity retains its standard `public."AspNet*"` tables and string user keys. Multi-operator business entities carry `operator_id`, and composite foreign keys prevent cross-operator relationships.

The design was derived from:

- `docs/requirements/SRS_HE_THONG_QUAN_LY_TAU_METRO.md`
- `backend/artifacts/database-work/Metro-SRS-v1.1.txt`
- `docs/database/drawsql-srs-v1.1-review.md`
- root `README.md` workflows and security guidance
- saved Hanoi Metro reference artifacts under `output/`

The DrawSQL review predates the current consolidated SRS and recommends one ticket per order. The current SRS use cases require one ticket per purchased quantity and explicitly describe multi-ticket orders. The executable design therefore uses `orders -> order_items -> tickets`, with a unique ticket sequence per item. This is the documented conflict resolution.

## Domain groups

- Identity/access: users, operator accounts, roles, permissions, assignments, sessions, immutable audit and versioned configuration/retention policy.
- Network/operations: lines, stations, calendars, schedule versions, trips/stops, fleet, staffing, maintenance, incidents and notices.
- Fare/ticketing: ticket products, fare versions/matrix, eligibility, orders/items, tickets and credentials.
- Capacity: versioned profiles, per-trip adjacent segments, idempotent holds and hold-to-segment details.
- Finance: payments, provider events, refund requests/transactions/attempts and payout revisions.
- AFC: journeys and idempotent entry/exit events.
- Support/analytics/automation: support cases, notifications, reports, OD/APC/load data, crowding, forecasts and governed automation.

The deployed package contains 77 `metro` tables. Tables are generally in third normal form; immutable transaction snapshots (`fare_snapshot`, `product_snapshot`, payout snapshots) are deliberate denormalization required to preserve historical facts.

## Capacity and concurrency

`trip_capacity_segments` stores capacity for every adjacent station pair. A journey reserves every segment between its origin and destination, so overlapping journeys contend only where they actually overlap.

`hold_trip_capacity`:

1. Locks the order item and order.
2. Resolves origin/destination sequence numbers.
3. Locks affected segment rows in ascending route sequence.
4. Validates `held_count + confirmed_count + requested <= permitted_capacity` on every segment.
5. Inserts the idempotent hold/details and increments all counters in the same transaction.

The deterministic lock order prevents lost updates and minimizes deadlocks. The table CHECK constraint remains a final invariant even if a privileged maintenance statement bypasses the function. Expiry uses bounded `FOR UPDATE SKIP LOCKED` processing so multiple workers can cooperate.

## Payment, issuance and cancellation

`confirm_payment_and_issue_tickets` locks payment and order, validates signature/amount/currency/idempotency, confirms all capacity holds, records the provider event, changes payment state, and inserts exactly one ticket per passenger before committing. An error rolls back the entire operation.

Partial workflow state guards reject invalid status transitions for orders, payments and tickets. Unique partial indexes prevent more than one active hold for an order item and more than one successful payment for an order.

Unpaid cancellation releases holds and cancels pending payments atomically. Paid/issued cancellation must use refund/revocation workflows. Revoking an individual ticket conservatively retains confirmed trip capacity; releasing grouped capacity for one ticket could oversell when an order item represents several passengers. A future partial-capacity-release requirement would need an explicit per-ticket allocation model.

## Retention and audit

Fifteen reconciliation tables reject statement-level `DELETE` and `TRUNCATE`, including audit, orders/items, holds, payments/events, tickets/credentials, journeys/AFC events and refund history. Data is closed through statuses and, where approved, PII fields may be anonymized while financial and operational lineage remains.

Eligibility evidence is intentionally different: SRS `BR-ELG-002` and `NFR-PRI-003` require evidence content to be purged after decision. The evidence row, hashes, dates and purge marker remain for traceability.

## Index strategy

- B-tree primary/unique indexes enforce identity and idempotency.
- Composite operator-first indexes support tenant-scoped access.
- Partial indexes cover active holds, active refunds, unresolved refunds, open journeys, queues and current notifications.
- Fare lookup includes returned amount/sellable columns.
- Time-descending indexes support passenger, payment, audit, AFC and operational history.
- GiST exclusion constraints prevent overlapping published configuration/schedule/fare and role assignment periods.

The package has 261 indexes including constraint-backed indexes. Query plans should be monitored before adding more; historical growth is addressed with selective indexes and future time partitioning only when measured volume warrants it.

## Security

- Password hashes remain the responsibility of ASP.NET Core Identity; plaintext passwords are never stored.
- Database/application credentials and provider secrets are not stored in schema seed data.
- Payment card numbers/CVV are not modeled.
- Credential tokens are stored as hashes.
- PII snapshots are limited to reconciliation needs and must be masked in application output/logs.
- Functions use explicit `search_path`; ownership and EXECUTE grants must be assigned by deployment administration.

## Known policy inputs

Legal retention periods (`OI-001`), production capacity/profile values (`ASM-011`), provider contract fields (`Q-103` in the DrawSQL review), and production operator seed data require human approval. The database refuses to invent those values.
