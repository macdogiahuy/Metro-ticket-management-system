# Entity relationship overview

```mermaid
erDiagram
    OPERATORS ||--o{ LINES : owns
    OPERATORS ||--o{ STATIONS : owns
    LINES ||--o{ PLANNED_TRIPS : schedules
    PLANNED_TRIPS ||--o{ PLANNED_STOP_TIMES : contains
    CAPACITY_PROFILES ||--o{ TRIP_CAPACITY_SEGMENTS : defines
    PLANNED_TRIPS ||--o{ TRIP_CAPACITY_SEGMENTS : partitions

    FARE_POLICY_VERSIONS ||--o{ ORDERS : prices
    ORDERS ||--|{ ORDER_ITEMS : contains
    TICKET_PRODUCTS ||--o{ ORDER_ITEMS : selected_as
    ORDER_ITEMS ||--o| CAPACITY_HOLDS : reserves
    CAPACITY_HOLDS ||--|{ CAPACITY_HOLD_SEGMENTS : covers
    TRIP_CAPACITY_SEGMENTS ||--o{ CAPACITY_HOLD_SEGMENTS : allocated_on

    ORDERS ||--o{ PAYMENTS : attempts
    PAYMENTS ||--o{ PAYMENT_EVENTS : receives
    ORDER_ITEMS ||--o{ TICKETS : issues
    TICKETS ||--o{ TICKET_CREDENTIALS : represented_by
    TICKETS ||--o{ JOURNEYS : used_for
    JOURNEYS ||--o{ AFC_EVENTS : records

    TICKETS ||--o{ REFUND_REQUESTS : requested_for
    PAYMENTS ||--o{ REFUND_REQUESTS : funded_by
    REFUND_REQUESTS ||--o{ REFUND_TRANSACTIONS : executes
    REFUND_TRANSACTIONS ||--o{ REFUND_ATTEMPTS : retries
    REFUND_REQUESTS ||--o{ REFUND_PAYOUT_REVISIONS : snapshots
```

All shown business relationships are operator-scoped. Deletion does not cascade through ticketing or finance history. Identity credential child rows may use the normal ASP.NET Identity cascade rules, but domain history restricts deletion of referenced users.
