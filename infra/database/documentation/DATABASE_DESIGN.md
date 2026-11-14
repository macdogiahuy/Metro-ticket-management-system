# Database design

## Architecture

The target engine is PostgreSQL 17. Domain objects use the `metro` schema; ASP.NET Core Identity retains its standard `public."AspNet*"` tables and string user keys. Multi-operator business entities carry `operator_id`, and composite foreign keys prevent cross-operator relationships.

The current design is derived from:

- `docs/requirements/Metro_SRS_v1.2.docx` — the authoritative specification (`§1.4`, `§11.3`)
- `docs/requirements/Metro_Dac_ta_ UC_v1.6.docx` — input use-case catalogue
- `docs/database/drawsql-srs-v1.1-review.md` — earlier review notes
- root `README.md` workflows and security guidance

SRS v1.2 supersedes the v1.1 quota model. Migration `006_srs_v1_2_alignment.sql` implements the consolidated decisions `DEC-SRS-203…206`: no capacity holds, trip suggestions instead of reserved seats, manual kg load, guardian links, purchase consent, order versions and ticket change requests. The v1.1 capacity tables remain in place for historical compatibility and are documented as legacy; new application flows must not write to them.

## Domain groups

- Identity/access: users, operator accounts, roles, permissions, assignments, sessions, immutable audit and versioned configuration/retention policy.
- Network/operations: lines, stations, calendars, schedule versions, trips/stops, fleet, incidents and notices.
- Fare/ticketing: ticket products, fare versions/matrix, eligibility, orders/order versions, consent, items, ticket suggestions, tickets and credentials.
- Capacity (legacy): versioned profiles, per-trip adjacent segments, idempotent holds and hold-to-segment details.
- Load (SRS v1.2): car/trip `max_load_kg`, manual `load_observations` and ETA updates.
- Finance: payments, provider events, refund requests/transactions/attempts and payout revisions.
- AFC: journeys and idempotent entry/exit events, including entry trip/direction.
- Guardianship: `guardian_links` for approved guardian/child relationships, with pair QR linkage on tickets.
- Support: support cases, accessibility requests and station slot capacity, lost and found, notifications and deliveries.
- Analytics/automation: data-quality days, SRS-shaped entry forecasts, OD/APC/crowding data and governed automation.

The deployed package contains 93 `metro` tables plus the 7 Identity tables. Tables are generally in third normal form; immutable transaction snapshots (`fare_snapshot`, `product_snapshot`, payout snapshots) are deliberate denormalization required to preserve historical facts.

## Capacity, load and trip suggestions

SRS v1.2 has no sellable quota and no held seats. A single-trip order receives at most one current suggestion per order version:

- `trip_suggestions` stores `suggestion_code`, beneficiary, service date, origin/destination, line/direction, suggested trip and departure, source, freshness and update reason.
- A trigger supersedes the previous current suggestion; `v_current_trip_suggestion` exposes only the current row.
- A suggestion is never a booking, a journey or a guarantee of space, and does not block entry.
- `cars.max_load_kg` and `capacity_profiles.max_load_kg` hold the physical limit; `validate_load_profile` checks that car limits do not exceed the trip limit.
- `load_observations` stores manual kg readings (`numeric(8,1)`, one decimal) with actor and observation time. `v_trip_load_status` reports `NO_DATA`, `NO_LIMIT`, `FULL` or `AVAILABLE`.
- `trip_eta_updates` stores administrator-entered ETA changes per trip/station.

The v1.1 tables (`capacity_profiles` seating fields, `trip_capacity_segments`, `capacity_holds`, `capacity_hold_segments`, `trip_load_profiles`, `ticket_products.capacity_controlled`) are marked `LEGACY` in database comments. The five legacy capacity functions stay available so historical fixtures and any migrated data remain readable; new flows do not call them.

## Orders, tickets and validity

- One order has one beneficiary and one ticket product (`order_items` trigger; legacy capacity-controlled products keep their historical group shape).
- `order_versions` stores each confirmed content version; a partial unique index keeps one `CONFIRMED` version per order.
- `purchase_consents` binds a buyer's order version to the beneficiary's acceptance, with a 24-hour expiry enforced by the service.
- `tickets` carries order/version, service date, station pair and guardian relation. The trigger `fill_ticket_travel_context` fills those fields from the order item and sets the single-journey validity window in `Asia/Ho_Chi_Minh`.
- `ex_tickets_no_overlapping_validity` is a GiST exclusion constraint: a holder cannot have two `ACTIVE`/`IN_USE` tickets with overlapping validity. Expired, used or revoked tickets free the slot. Pending paid/free-confirmed orders are checked by the service before issuance.
- A free child ticket is issued by `issue_free_order` without a payment row or a fake `paid_at`. The order must be fully discounted and the beneficiary must be an eligible linked child.

## Payment, issuance and cancellation

`confirm_payment_and_issue_tickets` locks payment and order, validates signature/amount/currency/idempotency, records the provider event, changes payment state and inserts the ticket before committing. An error rolls back the entire operation.

Callback hardening:

- `p_signature_verified` must be `IS TRUE`; a NULL result is rejected, not treated as verified.
- Received amount/currency must be non-NULL and match both the payment and order snapshots (`IS DISTINCT FROM` comparisons).
- The provider event is claimed atomically with `INSERT ... ON CONFLICT DO NOTHING RETURNING id`. A different owning payment aborts with `23505`; duplicate delivery by the same payment returns an idempotent issued-ticket count.
- Ticket codes are `'TKT-' || <order-item uuid without dashes> || '-' || <sequence>`, unique per item, under 80 characters.

Partial workflow state guards reject invalid status transitions for orders, payments and tickets. The guard also permits `DRAFT/HELD/PENDING_PAYMENT → ISSUED` only for a zero-payable free order with `free_confirmed_at`, and free-child cancellation before entry.

Unpaid cancellation locks pending payments first and then the order, releasing legacy holds when present. Paid/issued cancellation must use the refund/revocation workflows. `refund_*` exists for the SRS "hoàn khoản thu lỗi" decision (`DEC-SRS-203`), not for voluntary cancellation or disruption compensation.

## Ticket changes and disruption

`ticket_change_requests` records the before/after date, station pair or trip, the reason, the incident, the decision and the related child ticket. `apply_ticket_change`:

1. accepts only an `APPROVED` request for an unused, unopened ticket;
2. enforces one change per ticket (`tickets.change_count`);
3. updates the travel context and single-journey validity;
4. revokes the old QR credentials and keeps them as history;
5. writes an audit row.

Changing a child ticket requires the paired guardian change at the service layer; the table keeps `related_child_ticket_id` for traceability.

## Accessibility and lost property

- `station_support_capacities` holds two slots per station per 30-minute window.
- `confirm_accessibility_request` atomically consumes a slot and rejects a full or already-ended slot; `release_accessibility_capacity` returns capacity only for `PENDING`/`CONFIRMED` requests. Completed support stays counted as used.
- `accessibility_request_events` keeps the actor/time/reason history.
- `lost_reports`, `found_items`, `lost_item_claims` and `lost_item_handovers` implement the lost-property flow. One effective approval per found item is enforced by a partial unique index; one handover per item is enforced by a unique key.
- `mark_overdue_lost_items` moves unclaimed items and open reports to `OVERDUE` without deleting anything.

## Retention and audit

Twenty-five reconciliation tables reject statement-level `DELETE` and `TRUNCATE`, including audit, orders/versions/items, consents, holds, payments/events, tickets/credentials, ticket changes, suggestions, journeys/AFC events, refund history, support/accessibility history and lost property. Data is closed through statuses and, where approved, PII fields may be anonymized while financial and operational lineage remains.

Eligibility evidence is intentionally different: SRS `BR-ELG-002` and `NFR-PRI-003` require evidence content to be purged after decision. The evidence row, hashes, dates and purge marker remain for traceability.

## Index strategy

- B-tree primary/unique indexes enforce identity and idempotency.
- Composite operator-first indexes support tenant-scoped access.
- Partial indexes cover current suggestions, active refunds, open journeys, support slots and queue states.
- GiST exclusion constraints prevent overlapping published configuration/schedule/fare and role assignment periods, and overlapping active ticket validity.
- Time-descending indexes support passenger, payment, audit, AFC and operational history.

The package has 329 indexes including constraint-backed indexes, and 297 foreign keys. Query plans should be monitored before adding more; historical growth is addressed with selective indexes and future time partitioning only when measured volume warrants it.

## Nullable uniqueness decisions

A natural key that includes nullable scope columns needs an explicit decision, because ordinary `UNIQUE` treats every NULL as distinct.

- `fare_matrix_entries` uses `UNIQUE NULLS NOT DISTINCT` on `(operator_id, fare_policy_version_id, ticket_product_id, line_id, origin_station_id, destination_station_id, payment_channel, eligibility_group)`. Two network-wide fares with the same scope and different amounts can no longer coexist and leave pricing ambiguous.
- `payments`, `refund_transactions` and `refund_attempts` keep ordinary `UNIQUE` on their provider-reference columns. Provider references are assigned asynchronously, so many CASH payments or in-flight refund attempts legitimately hold NULL at the same time; `NULLS NOT DISTINCT` would allow only one.
- `ticket_credentials` replaces a plain `replaced_by_id` foreign key with the composite `(operator_id, replaced_by_id) -> ticket_credentials (operator_id, id)` plus `CHECK (replaced_by_id IS NULL OR replaced_by_id <> id)`, so replacement chains stay inside one operator and cannot self-reference.
- `ticket_products` requires a duration for every non-single-trip product (`duration_value IS NOT NULL`, `duration_value > 0`, `duration_unit IS NOT NULL`) and forbids `expiry_rule = 'SINGLE_JOURNEY'` outside single trips.

## Security

- Password hashes remain the responsibility of ASP.NET Core Identity; plaintext passwords are never stored.
- Database/application credentials and provider secrets are not stored in schema seed data.
- Payment card numbers/CVV are not modeled.
- Credential tokens are stored as hashes.
- PII snapshots are limited to reconciliation needs and must be masked in application output/logs.
- Functions use explicit `search_path`; ownership and EXECUTE grants must be assigned by deployment administration.
- The thirteen workflow functions run as `SECURITY DEFINER` with `search_path = pg_catalog, metro, public` so a least-privilege runtime role can execute them without direct table write access. Their implicit `PUBLIC` EXECUTE grant is revoked; grant each function explicitly to the runtime role.
- Identity emails are unique per FR-01.01 through a partial unique index on `"NormalizedEmail"`; the application must set `RequireUniqueEmail = true`, and the deployment preflight aborts with an actionable message if existing rows already contain duplicates.

## Known policy inputs

Legal retention periods (`OI-001`), production capacity/profile values (`ASM-011`), provider contract fields (`Q-103` in the DrawSQL review), and production operator seed data require human approval. The database refuses to invent those values.
