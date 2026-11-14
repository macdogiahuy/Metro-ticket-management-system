# Entity relationship overview

The diagram reflects SRS v1.2 (migration `006_srs_v1_2_alignment.sql`). The legacy
v1.1 capacity-hold chain still exists physically but is marked legacy and is not
part of any current flow.

```mermaid
erDiagram
    OPERATORS ||--o{ LINES : owns
    OPERATORS ||--o{ STATIONS : owns
    LINES ||--o{ PLANNED_TRIPS : schedules
    PLANNED_TRIPS ||--o{ PLANNED_STOP_TIMES : contains
    TRAINSETS ||--o{ TRIP_ALLOCATIONS : assigned_to
    PLANNED_TRIPS ||--o{ TRIP_ALLOCATIONS : receives
    TRAINSETS ||--o{ CARS : formed_of
    PLANNED_TRIPS ||--o{ LOAD_OBSERVATIONS : measured_by
    PLANNED_TRIPS ||--o{ TRIP_ETA_UPDATES : announced_by
    CAPACITY_PROFILES ||--o{ PLANNED_TRIPS : load_limit_for

    FARE_POLICY_VERSIONS ||--o{ ORDERS : prices
    ORDERS ||--|{ ORDER_VERSIONS : versions
    ORDERS ||--|{ ORDER_ITEMS : contains
    ORDER_VERSIONS ||--o| TRIP_SUGGESTIONS : suggests
    ORDERS ||--o{ PURCHASE_CONSENTS : requires
    TICKET_PRODUCTS ||--o{ ORDER_ITEMS : selected_as

    USERS ||--o{ GUARDIAN_LINKS : guardian
    GUARDIAN_LINKS ||--o{ TICKETS : entitles
    ORDER_ITEMS ||--o{ TICKETS : issues
    TICKETS ||--o{ TICKET_CREDENTIALS : represented_by
    TICKETS ||--o{ TICKET_CHANGE_REQUESTS : changed_by
    TICKETS ||--o{ JOURNEYS : used_for
    JOURNEYS ||--o{ AFC_EVENTS : records

    ORDERS ||--o{ PAYMENTS : attempts
    PAYMENTS ||--o{ PAYMENT_EVENTS : receives
    TICKETS ||--o{ REFUND_REQUESTS : requested_for
    PAYMENTS ||--o{ REFUND_REQUESTS : funded_by
    REFUND_REQUESTS ||--o{ REFUND_TRANSACTIONS : executes
    REFUND_TRANSACTIONS ||--o{ REFUND_ATTEMPTS : retries
    REFUND_REQUESTS ||--o{ REFUND_PAYOUT_REVISIONS : snapshots

    STATIONS ||--o{ STATION_SUPPORT_CAPACITIES : provides
    STATION_SUPPORT_CAPACITIES ||--o{ ACCESSIBILITY_REQUESTS : slots
    ACCESSIBILITY_REQUESTS ||--o{ ACCESSIBILITY_REQUEST_EVENTS : history

    LOST_REPORTS ||--o{ LOST_ITEM_CLAIMS : claims
    FOUND_ITEMS ||--o{ LOST_ITEM_CLAIMS : matched_with
    LOST_ITEM_CLAIMS ||--o{ LOST_ITEM_HANDOVERS : handed_over

    OPERATORS ||--o{ DATA_QUALITY_DAYS : reports
    STATIONS ||--o{ DEMAND_FORECASTS : forecast_for
```

`USERS` stands for the ASP.NET Core Identity table `public."AspNetUsers"`; the
physical name is quoted and lives outside the `metro` schema.

## Legacy chain (not used by SRS v1.2 flows)

```mermaid
erDiagram
    CAPACITY_PROFILES ||--o{ TRIP_CAPACITY_SEGMENTS : defines
    PLANNED_TRIPS ||--o{ TRIP_CAPACITY_SEGMENTS : partitions
    ORDER_ITEMS ||--o| CAPACITY_HOLDS : reserves
    CAPACITY_HOLDS ||--|{ CAPACITY_HOLD_SEGMENTS : covers
    TRIP_CAPACITY_SEGMENTS ||--o{ CAPACITY_HOLD_SEGMENTS : allocated_on
```

## Notes

- All business relationships are operator-scoped through composite foreign keys
  `(operator_id, id)`.
- Deletion does not cascade through ticketing, finance or history. Twenty-five
  reconciliation tables reject `DELETE`/`TRUNCATE` at statement level.
- `tickets` carries order/version, service date, station pair, guardian relation
  and change count. Validity overlap is prevented by the GiST exclusion constraint
  `ex_tickets_no_overlapping_validity`, not by a "one active ticket" unique index.
- `trip_suggestions` keeps history; a trigger supersedes the previous current row
  and `v_current_trip_suggestion` exposes only one row per order version.
- `demand_forecasts.data_quality_status` records the completeness status the
  administrator confirmed; the seven-day rule itself lives in the service layer
  backed by `data_quality_days`.
- Identity credential child rows may use ASP.NET Identity cascade rules, but domain
  history restricts deletion of referenced users.
