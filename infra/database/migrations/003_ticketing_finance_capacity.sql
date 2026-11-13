-- Fare, ticketing, capacity reservation, payment, AFC and refund domain.
CREATE TABLE metro.ticket_products (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(50) NOT NULL, name text NOT NULL, product_type text NOT NULL CHECK (product_type IN ('SINGLE_TRIP','DAY','WEEK','MONTH','MULTI_MONTH')),
    duration_value integer, duration_unit text CHECK (duration_unit IN ('DAY','MONTH')),
    expiry_rule text NOT NULL CHECK (expiry_rule IN ('SINGLE_JOURNEY','NEXT_LOCAL_MIDNIGHT','ELAPSED_DURATION')),
    latest_activation_interval interval, requires_named_holder boolean NOT NULL DEFAULT false,
    capacity_controlled boolean NOT NULL DEFAULT false, status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('DRAFT','ACTIVE','SUSPENDED','RETIRED')),
    created_by_user_id text REFERENCES public."AspNetUsers" ("Id"), updated_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code),
    CHECK ((product_type = 'SINGLE_TRIP' AND duration_value IS NULL AND duration_unit IS NULL AND expiry_rule = 'SINGLE_JOURNEY')
        OR (product_type <> 'SINGLE_TRIP' AND duration_value > 0 AND duration_unit IS NOT NULL)),
    CHECK (capacity_controlled = false OR product_type = 'SINGLE_TRIP')
);

CREATE TABLE metro.fare_policy_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(60) NOT NULL, version integer NOT NULL CHECK (version > 0), scope_key varchar(100) NOT NULL,
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    currency char(3) NOT NULL DEFAULT 'VND' CHECK (currency = 'VND'), effective_from timestamptz NOT NULL, effective_to timestamptz,
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    published_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_at timestamptz, published_at timestamptz, decision_reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), UNIQUE (operator_id, scope_key, version),
    CHECK (effective_to IS NULL OR effective_to > effective_from),
    CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id),
    CHECK (published_by_user_id IS NULL OR published_by_user_id <> created_by_user_id),
    EXCLUDE USING gist (operator_id WITH =, scope_key WITH =, tstzrange(effective_from, effective_to, '[)') WITH &&)
        WHERE (status = 'PUBLISHED')
);

CREATE TABLE metro.fare_matrix_entries (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, fare_policy_version_id uuid NOT NULL,
    ticket_product_id uuid NOT NULL, line_id uuid, origin_station_id uuid, destination_station_id uuid,
    payment_channel text NOT NULL CHECK (payment_channel IN ('CASH','NON_CASH','ANY')),
    eligibility_group varchar(50) NOT NULL DEFAULT 'ORDINARY', amount_minor bigint NOT NULL CHECK (amount_minor >= 0), sellable boolean NOT NULL DEFAULT true,
    FOREIGN KEY (operator_id, fare_policy_version_id) REFERENCES metro.fare_policy_versions (operator_id, id),
    FOREIGN KEY (operator_id, ticket_product_id) REFERENCES metro.ticket_products (operator_id, id),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, origin_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, destination_station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, fare_policy_version_id, ticket_product_id, line_id, origin_station_id, destination_station_id, payment_channel, eligibility_group),
    CHECK (origin_station_id IS NULL OR destination_station_id IS NULL OR origin_station_id <> destination_station_id)
);

CREATE TABLE metro.eligibility_policies (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(50) NOT NULL, version integer NOT NULL CHECK (version > 0), beneficiary_group varchar(50) NOT NULL,
    discount_type text NOT NULL CHECK (discount_type IN ('FREE','PERCENT','FIXED_AMOUNT')),
    discount_value numeric(12,4) NOT NULL CHECK (discount_value >= 0), eligible_product_codes text[] NOT NULL DEFAULT '{}',
    required_evidence_types text[] NOT NULL DEFAULT '{}', effective_from timestamptz NOT NULL, effective_to timestamptz,
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code, version), CHECK (effective_to IS NULL OR effective_to > effective_from),
    CHECK (discount_type <> 'PERCENT' OR discount_value <= 100),
    CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id)
);

CREATE TABLE metro.eligibility_applications (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, application_code varchar(60) NOT NULL,
    applicant_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), eligibility_policy_id uuid NOT NULL,
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','SUBMITTED','NEEDS_INFO','APPROVED','REJECTED','EXPIRED','REVOKED')),
    submitted_at timestamptz, reviewed_by_user_id text REFERENCES public."AspNetUsers" ("Id"), decided_at timestamptz,
    valid_from date, valid_to date, decision_reason text, evidence_purged_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, eligibility_policy_id) REFERENCES metro.eligibility_policies (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, application_code), CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from),
    CHECK (status NOT IN ('APPROVED','REJECTED') OR (reviewed_by_user_id IS NOT NULL AND decided_at IS NOT NULL AND nullif(btrim(decision_reason),'') IS NOT NULL))
);

CREATE TABLE metro.eligibility_evidence (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, application_id uuid NOT NULL,
    evidence_type varchar(60) NOT NULL, masked_reference text, reference_hash char(64), valid_from date, valid_to date,
    storage_reference text, uploaded_at timestamptz NOT NULL DEFAULT clock_timestamp(), purged_at timestamptz,
    metadata jsonb NOT NULL DEFAULT '{}',
    FOREIGN KEY (operator_id, application_id) REFERENCES metro.eligibility_applications (operator_id, id),
    CHECK (reference_hash IS NULL OR reference_hash ~ '^[0-9a-f]{64}$'), CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from),
    CHECK (purged_at IS NULL OR storage_reference IS NULL)
);

CREATE TABLE metro.orders (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    order_code varchar(60) NOT NULL, channel text NOT NULL CHECK (channel IN ('WEB','COUNTER','ADMIN')),
    payer_user_id text REFERENCES public."AspNetUsers" ("Id"), beneficiary_user_id text REFERENCES public."AspNetUsers" ("Id"),
    selling_station_id uuid, created_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    payer_name_snapshot text, payer_email_snapshot text, beneficiary_name_snapshot text,
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','HELD','PENDING_PAYMENT','PAID','ISSUED','EXPIRED','CANCELLED','REFUND_PENDING','REFUNDED')),
    currency char(3) NOT NULL DEFAULT 'VND' CHECK (currency = 'VND'), subtotal_minor bigint NOT NULL DEFAULT 0 CHECK (subtotal_minor >= 0),
    discount_minor bigint NOT NULL DEFAULT 0 CHECK (discount_minor >= 0), total_minor bigint NOT NULL DEFAULT 0 CHECK (total_minor >= 0),
    fare_policy_version_id uuid NOT NULL, fare_snapshot jsonb NOT NULL DEFAULT '{}',
    hold_expires_at timestamptz, payment_started_at timestamptz, paid_at timestamptz, issued_at timestamptz,
    cancelled_at timestamptz, cancelled_by_user_id text REFERENCES public."AspNetUsers" ("Id"), cancellation_reason text,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, selling_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, fare_policy_version_id) REFERENCES metro.fare_policy_versions (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, order_code), UNIQUE (operator_id, correlation_id),
    CHECK (total_minor = subtotal_minor - discount_minor), CHECK (discount_minor <= subtotal_minor),
    CHECK ((channel = 'WEB' AND payer_user_id IS NOT NULL AND beneficiary_user_id IS NOT NULL AND selling_station_id IS NULL)
        OR (channel = 'COUNTER' AND selling_station_id IS NOT NULL AND created_by_user_id IS NOT NULL)
        OR channel = 'ADMIN'),
    CHECK (status <> 'CANCELLED' OR (cancelled_at IS NOT NULL AND nullif(btrim(cancellation_reason),'') IS NOT NULL))
);

CREATE TABLE metro.order_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, order_id uuid NOT NULL,
    ticket_product_id uuid NOT NULL, planned_trip_id uuid, origin_station_id uuid, destination_station_id uuid,
    passenger_count integer NOT NULL DEFAULT 1 CHECK (passenger_count > 0), unit_price_minor bigint NOT NULL CHECK (unit_price_minor >= 0),
    discount_minor bigint NOT NULL DEFAULT 0 CHECK (discount_minor >= 0), line_total_minor bigint NOT NULL CHECK (line_total_minor >= 0),
    eligibility_application_id uuid, product_snapshot jsonb NOT NULL DEFAULT '{}', fare_snapshot jsonb NOT NULL DEFAULT '{}',
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id),
    FOREIGN KEY (operator_id, ticket_product_id) REFERENCES metro.ticket_products (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, origin_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, destination_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, eligibility_application_id) REFERENCES metro.eligibility_applications (operator_id, id),
    UNIQUE (operator_id, id), CHECK (line_total_minor = unit_price_minor * passenger_count - discount_minor),
    CHECK (discount_minor <= unit_price_minor * passenger_count),
    CHECK ((planned_trip_id IS NULL AND origin_station_id IS NULL AND destination_station_id IS NULL)
        OR (planned_trip_id IS NOT NULL AND origin_station_id IS NOT NULL AND destination_station_id IS NOT NULL AND origin_station_id <> destination_station_id))
);

CREATE TABLE metro.capacity_profiles (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(60) NOT NULL, version integer NOT NULL CHECK (version > 0), line_id uuid, trainset_id uuid,
    seated_capacity integer NOT NULL CHECK (seated_capacity >= 0), standing_capacity integer NOT NULL CHECK (standing_capacity >= 0),
    design_factor numeric(6,4) NOT NULL DEFAULT 1 CHECK (design_factor > 0 AND design_factor <= 1),
    permitted_capacity integer GENERATED ALWAYS AS (floor((seated_capacity + standing_capacity) * design_factor)) STORED,
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    effective_from timestamptz NOT NULL, effective_to timestamptz,
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, trainset_id) REFERENCES metro.trainsets (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code, version), CHECK (num_nonnulls(line_id, trainset_id) >= 1),
    CHECK (effective_to IS NULL OR effective_to > effective_from), CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id)
);

CREATE TABLE metro.trip_capacity_segments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, planned_trip_id uuid NOT NULL,
    capacity_profile_id uuid NOT NULL, from_sequence smallint NOT NULL CHECK (from_sequence > 0),
    to_sequence smallint NOT NULL CHECK (to_sequence = from_sequence + 1), permitted_capacity integer NOT NULL CHECK (permitted_capacity >= 0),
    held_count integer NOT NULL DEFAULT 0 CHECK (held_count >= 0), confirmed_count integer NOT NULL DEFAULT 0 CHECK (confirmed_count >= 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, capacity_profile_id) REFERENCES metro.capacity_profiles (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, planned_trip_id, from_sequence),
    CHECK (held_count + confirmed_count <= permitted_capacity)
);

CREATE TABLE metro.capacity_holds (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, order_item_id uuid NOT NULL,
    idempotency_key varchar(120) NOT NULL, passenger_count integer NOT NULL CHECK (passenger_count > 0),
    status text NOT NULL DEFAULT 'HELD' CHECK (status IN ('HELD','CONFIRMED','RELEASED','EXPIRED','CANCELLED')),
    held_at timestamptz NOT NULL DEFAULT clock_timestamp(), expires_at timestamptz NOT NULL,
    confirmed_at timestamptz, released_at timestamptz, release_reason text,
    FOREIGN KEY (operator_id, order_item_id) REFERENCES metro.order_items (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, idempotency_key),
    CHECK (expires_at > held_at), CHECK (status NOT IN ('RELEASED','EXPIRED','CANCELLED') OR released_at IS NOT NULL)
);

CREATE TABLE metro.capacity_hold_segments (
    operator_id uuid NOT NULL, capacity_hold_id uuid NOT NULL, capacity_segment_id uuid NOT NULL,
    passenger_count integer NOT NULL CHECK (passenger_count > 0),
    PRIMARY KEY (operator_id, capacity_hold_id, capacity_segment_id),
    FOREIGN KEY (operator_id, capacity_hold_id) REFERENCES metro.capacity_holds (operator_id, id),
    FOREIGN KEY (operator_id, capacity_segment_id) REFERENCES metro.trip_capacity_segments (operator_id, id)
);

CREATE TABLE metro.payments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, order_id uuid NOT NULL,
    payment_code varchar(60) NOT NULL, provider varchar(60) NOT NULL, provider_payment_reference text,
    method text NOT NULL CHECK (method IN ('SANDBOX_GATEWAY','CASH','BANK_TRANSFER','OTHER')),
    amount_minor bigint NOT NULL CHECK (amount_minor >= 0), currency char(3) NOT NULL DEFAULT 'VND' CHECK (currency = 'VND'),
    status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','SUCCEEDED','FAILED','EXPIRED','CANCELLED','PARTIALLY_REFUNDED','REFUNDED','REVIEW_REQUIRED')),
    idempotency_key varchar(120) NOT NULL, initiated_at timestamptz NOT NULL DEFAULT clock_timestamp(), verified_at timestamptz,
    succeeded_at timestamptz, failure_code text, reconciliation_status text NOT NULL DEFAULT 'UNRECONCILED'
        CHECK (reconciliation_status IN ('UNRECONCILED','MATCHED','MISMATCHED','MANUAL_REVIEW')),
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, payment_code), UNIQUE (operator_id, provider, idempotency_key),
    UNIQUE NULLS NOT DISTINCT (operator_id, provider, provider_payment_reference), UNIQUE (operator_id, correlation_id)
);

CREATE TABLE metro.payment_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, payment_id uuid NOT NULL,
    provider_event_id text NOT NULL, idempotency_key varchar(120) NOT NULL, event_type varchar(60) NOT NULL,
    signature_verified boolean NOT NULL, received_amount_minor bigint, received_currency char(3),
    payload_hash char(64) NOT NULL CHECK (payload_hash ~ '^[0-9a-f]{64}$'), outcome text NOT NULL,
    received_at timestamptz NOT NULL DEFAULT clock_timestamp(), processed_at timestamptz, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    FOREIGN KEY (operator_id, payment_id) REFERENCES metro.payments (operator_id, id),
    UNIQUE (operator_id, provider_event_id), UNIQUE (operator_id, idempotency_key), UNIQUE (operator_id, correlation_id)
);

CREATE TABLE metro.tickets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, ticket_code varchar(80) NOT NULL,
    order_item_id uuid NOT NULL, ticket_product_id uuid NOT NULL, holder_user_id text REFERENCES public."AspNetUsers" ("Id"),
    sequence_no integer NOT NULL CHECK (sequence_no > 0), status text NOT NULL DEFAULT 'ISSUED'
        CHECK (status IN ('ISSUED','ACTIVE','IN_USE','USED','EXPIRED','REFUND_PENDING','REFUNDED','REVOKED')),
    effective_from timestamptz, expires_at timestamptz, latest_activation_at timestamptz, first_used_at timestamptz,
    revoked_at timestamptz, revocation_reason text, fare_policy_version_id uuid NOT NULL,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), issued_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, order_item_id) REFERENCES metro.order_items (operator_id, id),
    FOREIGN KEY (operator_id, ticket_product_id) REFERENCES metro.ticket_products (operator_id, id),
    FOREIGN KEY (operator_id, fare_policy_version_id) REFERENCES metro.fare_policy_versions (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, ticket_code), UNIQUE (operator_id, order_item_id, sequence_no), UNIQUE (operator_id, correlation_id),
    CHECK (expires_at IS NULL OR effective_from IS NULL OR expires_at > effective_from),
    CHECK (revoked_at IS NULL OR nullif(btrim(revocation_reason),'') IS NOT NULL)
);

CREATE TABLE metro.ticket_credentials (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, ticket_id uuid NOT NULL,
    credential_type text NOT NULL CHECK (credential_type IN ('QR','CITIZEN_ID_MOCK','NFC_MOCK')),
    token_hash char(64) NOT NULL CHECK (token_hash ~ '^[0-9a-f]{64}$'), token_last4 varchar(4),
    status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','SUSPENDED','REVOKED','REPLACED','EXPIRED')),
    issued_at timestamptz NOT NULL DEFAULT clock_timestamp(), revoked_at timestamptz, replaced_by_id uuid,
    FOREIGN KEY (operator_id, ticket_id) REFERENCES metro.tickets (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, token_hash), FOREIGN KEY (replaced_by_id) REFERENCES metro.ticket_credentials (id)
);

CREATE TABLE metro.journeys (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, journey_code varchar(80) NOT NULL,
    ticket_id uuid NOT NULL, planned_origin_station_id uuid, planned_destination_station_id uuid,
    actual_entry_station_id uuid, actual_exit_station_id uuid,
    status text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','COMPLETED','MANUAL_REVIEW','CANCELLED')),
    entered_at timestamptz, exited_at timestamptz, supplemental_fare_minor bigint NOT NULL DEFAULT 0 CHECK (supplemental_fare_minor >= 0),
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, ticket_id) REFERENCES metro.tickets (operator_id, id),
    FOREIGN KEY (operator_id, planned_origin_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, planned_destination_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, actual_entry_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, actual_exit_station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, journey_code), UNIQUE (operator_id, correlation_id),
    CHECK (exited_at IS NULL OR entered_at IS NULL OR exited_at >= entered_at)
);
CREATE UNIQUE INDEX uq_ticket_open_journey ON metro.journeys (operator_id, ticket_id) WHERE status = 'OPEN';

CREATE TABLE metro.afc_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, event_code varchar(100) NOT NULL,
    idempotency_key varchar(120) NOT NULL, journey_id uuid, ticket_id uuid NOT NULL, credential_id uuid,
    event_type text NOT NULL CHECK (event_type IN ('TAP_IN','TAP_OUT','MANUAL_ENTRY','MANUAL_EXIT','SYNC_OFFLINE')),
    station_id uuid NOT NULL, device_code varchar(80) NOT NULL, event_at timestamptz NOT NULL,
    received_at timestamptz NOT NULL DEFAULT clock_timestamp(), offline boolean NOT NULL DEFAULT false,
    decision text NOT NULL CHECK (decision IN ('ALLOW','DENY','REVIEW')),
    reason_code varchar(80) NOT NULL, actor_user_id text REFERENCES public."AspNetUsers" ("Id"), manual_reason text,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    FOREIGN KEY (operator_id, journey_id) REFERENCES metro.journeys (operator_id, id),
    FOREIGN KEY (operator_id, ticket_id) REFERENCES metro.tickets (operator_id, id),
    FOREIGN KEY (operator_id, credential_id) REFERENCES metro.ticket_credentials (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, event_code), UNIQUE (operator_id, idempotency_key), UNIQUE (operator_id, correlation_id),
    CHECK (event_type NOT IN ('MANUAL_ENTRY','MANUAL_EXIT') OR (actor_user_id IS NOT NULL AND nullif(btrim(manual_reason),'') IS NOT NULL))
);

CREATE TABLE metro.refund_requests (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, request_code varchar(60) NOT NULL,
    ticket_id uuid NOT NULL, payment_id uuid NOT NULL, incident_id uuid, reason_type varchar(60) NOT NULL,
    requested_amount_minor bigint NOT NULL CHECK (requested_amount_minor >= 0), approved_amount_minor bigint CHECK (approved_amount_minor >= 0),
    status text NOT NULL DEFAULT 'SUBMITTED' CHECK (status IN ('SUBMITTED','UNDER_REVIEW','NEEDS_INFO','PENDING_APPROVAL','APPROVED','PROCESSING','COMPLETED','FAILED','REJECTED','CANCELLED')),
    submitted_by_user_id text REFERENCES public."AspNetUsers" ("Id"), reviewed_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"), reviewed_at timestamptz, approved_at timestamptz,
    decision_reason text, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, ticket_id) REFERENCES metro.tickets (operator_id, id),
    FOREIGN KEY (operator_id, payment_id) REFERENCES metro.payments (operator_id, id),
    FOREIGN KEY (operator_id, incident_id) REFERENCES metro.incidents (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, request_code), UNIQUE (operator_id, correlation_id),
    CHECK (approved_amount_minor IS NULL OR approved_amount_minor <= requested_amount_minor),
    CHECK (approved_by_user_id IS NULL OR reviewed_by_user_id IS NULL OR approved_by_user_id <> reviewed_by_user_id),
    CHECK (status NOT IN ('APPROVED','REJECTED') OR (approved_by_user_id IS NOT NULL AND approved_at IS NOT NULL AND nullif(btrim(decision_reason),'') IS NOT NULL))
);
CREATE UNIQUE INDEX uq_active_refund_per_ticket ON metro.refund_requests (operator_id, ticket_id)
    WHERE status IN ('SUBMITTED','UNDER_REVIEW','NEEDS_INFO','PENDING_APPROVAL','APPROVED','PROCESSING');

CREATE TABLE metro.refund_transactions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, refund_request_id uuid NOT NULL,
    payment_id uuid NOT NULL, refund_code varchar(60) NOT NULL, provider varchar(60) NOT NULL,
    provider_refund_reference text, method text NOT NULL, amount_minor bigint NOT NULL CHECK (amount_minor > 0), currency char(3) NOT NULL DEFAULT 'VND',
    status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','PROCESSING','SUCCEEDED','FAILED','UNKNOWN','CANCELLED')),
    idempotency_key varchar(120) NOT NULL, payout_recipient_snapshot jsonb NOT NULL DEFAULT '{}',
    processed_by_user_id text REFERENCES public."AspNetUsers" ("Id"), succeeded_at timestamptz, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, refund_request_id) REFERENCES metro.refund_requests (operator_id, id),
    FOREIGN KEY (operator_id, payment_id) REFERENCES metro.payments (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, refund_code), UNIQUE (operator_id, provider, idempotency_key),
    UNIQUE NULLS NOT DISTINCT (operator_id, provider, provider_refund_reference), UNIQUE (operator_id, correlation_id)
);
CREATE UNIQUE INDEX uq_unresolved_refund_per_request ON metro.refund_transactions (operator_id, refund_request_id)
    WHERE status IN ('PENDING','PROCESSING','UNKNOWN');

CREATE TABLE metro.refund_attempts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, refund_transaction_id uuid NOT NULL,
    attempt_no integer NOT NULL CHECK (attempt_no > 0), provider_event_id text, request_hash char(64) NOT NULL,
    response_hash char(64), status text NOT NULL CHECK (status IN ('SENT','SUCCEEDED','FAILED','TIMEOUT','UNKNOWN')),
    error_code text, attempted_at timestamptz NOT NULL DEFAULT clock_timestamp(), completed_at timestamptz,
    FOREIGN KEY (operator_id, refund_transaction_id) REFERENCES metro.refund_transactions (operator_id, id),
    UNIQUE (operator_id, refund_transaction_id, attempt_no), UNIQUE NULLS NOT DISTINCT (operator_id, provider_event_id)
);

CREATE TABLE metro.refund_payout_revisions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, refund_request_id uuid NOT NULL,
    revision_no integer NOT NULL CHECK (revision_no > 0), bank_code varchar(40) REFERENCES metro.banks (code),
    account_ciphertext bytea, account_last4 varchar(4), holder_name text, changed_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    change_reason text NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, refund_request_id) REFERENCES metro.refund_requests (operator_id, id),
    UNIQUE (operator_id, refund_request_id, revision_no)
);

CREATE INDEX ix_fare_lookup ON metro.fare_matrix_entries (operator_id, fare_policy_version_id, ticket_product_id, origin_station_id, destination_station_id, payment_channel) INCLUDE (amount_minor, sellable);
CREATE INDEX ix_orders_user_time ON metro.orders (operator_id, beneficiary_user_id, created_at DESC);
CREATE INDEX ix_orders_status_expiry ON metro.orders (operator_id, status, hold_expires_at);
CREATE INDEX ix_order_items_trip ON metro.order_items (operator_id, planned_trip_id, origin_station_id, destination_station_id);
CREATE INDEX ix_capacity_holds_expiry ON metro.capacity_holds (operator_id, status, expires_at) WHERE status = 'HELD';
CREATE INDEX ix_payments_order ON metro.payments (operator_id, order_id, created_at DESC);
CREATE INDEX ix_payment_events_payment ON metro.payment_events (operator_id, payment_id, received_at DESC);
CREATE INDEX ix_tickets_holder ON metro.tickets (operator_id, holder_user_id, status, issued_at DESC);
CREATE INDEX ix_afc_ticket_time ON metro.afc_events (operator_id, ticket_id, event_at DESC);
CREATE INDEX ix_refunds_payment ON metro.refund_requests (operator_id, payment_id, status);
