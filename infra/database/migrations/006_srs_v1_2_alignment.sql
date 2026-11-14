-- SRS v1.2 / UC v1.6 alignment.
--
-- This migration closes the gaps between the v1.1-era schema and the consolidated
-- SRS 1.2 decisions (DEC-SRS-203..206):
--   * no quota/hold model; a single-trip order gets a trip suggestion (DATA-020)
--   * manual kg load observations instead of passenger-count capacity (DATA-011/012)
--   * guardian/child links and pair QR (DATA-003/008, BR-006)
--   * purchase-on-behalf consent (DATA-005)
--   * order versions (DATA-004)
--   * one_active enforcement (BR-012)
--   * free-ticket issuance without a fake payment (BR-007)
--   * ticket change requests for disruption changes (DATA-013, UC42/43)
--   * lost and found (DATA-016, UC46)
--   * accessibility support requests and station slot capacity (DATA-015, UC44)
--   * daily data quality and SRS-shaped entry forecasts (DATA-019, UC47)
--   * entry trip/direction on journeys and AFC events (DATA-009, UC36)
--
-- Legacy capacity tables are retained for historical compatibility but are marked
-- as legacy. New application flows must not hold capacity.

-- ---------------------------------------------------------------------------
-- 1. Extend existing base tables
-- ---------------------------------------------------------------------------

ALTER TABLE metro.cars
    ADD CONSTRAINT uq_cars_operator_id_id UNIQUE (operator_id, id);

ALTER TABLE metro.cars
    ADD COLUMN IF NOT EXISTS max_load_kg numeric(8,1)
        CHECK (max_load_kg IS NULL OR max_load_kg > 0);

ALTER TABLE metro.capacity_profiles
    ADD COLUMN IF NOT EXISTS max_load_kg numeric(9,1)
        CHECK (max_load_kg IS NULL OR max_load_kg > 0),
    ALTER COLUMN seated_capacity SET DEFAULT 0,
    ALTER COLUMN standing_capacity SET DEFAULT 0;

-- SRS v1.2 models one passenger per order. Legacy capacity-controlled products
-- keep their historical group-sale shape; new products must use one item per order.
ALTER TABLE metro.order_items
    DROP CONSTRAINT IF EXISTS order_items_passenger_count_check;

CREATE OR REPLACE FUNCTION metro.enforce_single_passenger_item()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_capacity_controlled boolean;
BEGIN
    IF NEW.passenger_count <= 1 THEN
        RETURN NEW;
    END IF;

    SELECT capacity_controlled INTO v_capacity_controlled
    FROM metro.ticket_products
    WHERE operator_id = NEW.operator_id AND id = NEW.ticket_product_id;

    IF NOT COALESCE(v_capacity_controlled, false) THEN
        RAISE EXCEPTION USING ERRCODE = '23514',
            MESSAGE = 'SRS v1.2 orders must contain exactly one passenger';
    END IF;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_order_items_single_passenger
BEFORE INSERT OR UPDATE ON metro.order_items
FOR EACH ROW EXECUTE FUNCTION metro.enforce_single_passenger_item();

ALTER TABLE metro.line_stations
    ADD COLUMN IF NOT EXISTS travel_time_seconds integer NOT NULL DEFAULT 180
        CHECK (travel_time_seconds >= 0);

ALTER TABLE metro.stations
    ADD COLUMN IF NOT EXISTS min_transfer_seconds integer NOT NULL DEFAULT 300
        CHECK (min_transfer_seconds >= 0);

ALTER TABLE metro.planned_trips
    ADD COLUMN IF NOT EXISTS load_profile_id uuid,
    ADD COLUMN IF NOT EXISTS boarding_station_id uuid,
    ADD COLUMN IF NOT EXISTS boarding_status text NOT NULL DEFAULT 'OPEN'
        CHECK (boarding_status IN ('OPEN','PAUSED','CLOSED'));

ALTER TABLE metro.planned_trips
    ADD CONSTRAINT fk_planned_trips_load_profile
        FOREIGN KEY (operator_id, load_profile_id)
        REFERENCES metro.capacity_profiles (operator_id, id),
    ADD CONSTRAINT fk_planned_trips_boarding_station
        FOREIGN KEY (operator_id, boarding_station_id)
        REFERENCES metro.stations (operator_id, id);

ALTER TABLE metro.demand_forecasts
    ALTER COLUMN od_matrix_version_id DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS station_id uuid,
    ADD COLUMN IF NOT EXISTS line_id uuid,
    ADD COLUMN IF NOT EXISTS direction smallint CHECK (direction IN (0,1)),
    ADD COLUMN IF NOT EXISTS target_date date,
    ADD COLUMN IF NOT EXISTS hour_of_day smallint CHECK (hour_of_day BETWEEN 1 AND 24),
    ADD COLUMN IF NOT EXISTS source_days smallint CHECK (source_days > 0),
    ADD COLUMN IF NOT EXISTS sample_count integer CHECK (sample_count >= 0),
    ADD COLUMN IF NOT EXISTS minimum_value numeric(16,4) CHECK (minimum_value >= 0),
    ADD COLUMN IF NOT EXISTS maximum_value numeric(16,4),
    ADD COLUMN IF NOT EXISTS data_quality_status text
        CHECK (data_quality_status IN ('COMPLETE','INCOMPLETE','DEMO'));

ALTER TABLE metro.demand_forecasts
    ADD CONSTRAINT fk_demand_forecasts_station
        FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    ADD CONSTRAINT fk_demand_forecasts_line
        FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    ADD CONSTRAINT ck_demand_forecasts_scope
        CHECK (od_matrix_version_id IS NOT NULL
               OR (station_id IS NOT NULL AND target_date IS NOT NULL AND hour_of_day IS NOT NULL)),
    ADD CONSTRAINT ck_demand_forecasts_minmax
        CHECK (minimum_value IS NULL OR maximum_value IS NULL OR maximum_value >= minimum_value);

ALTER TABLE metro.incidents
    ADD COLUMN IF NOT EXISTS car_id uuid,
    ADD COLUMN IF NOT EXISTS position_text text,
    ADD COLUMN IF NOT EXISTS reporter_confirmed_at timestamptz;

ALTER TABLE metro.incidents
    ADD CONSTRAINT fk_incidents_car
        FOREIGN KEY (operator_id, car_id) REFERENCES metro.cars (operator_id, id);

ALTER TABLE metro.support_requests
    ADD COLUMN IF NOT EXISTS location_text text,
    ADD COLUMN IF NOT EXISTS planned_trip_id uuid,
    ADD COLUMN IF NOT EXISTS car_id uuid,
    ADD COLUMN IF NOT EXISTS reported_for_user_id text REFERENCES public."AspNetUsers" ("Id"),
    ADD COLUMN IF NOT EXISTS attachment_reference text,
    ADD COLUMN IF NOT EXISTS previous_request_code varchar(60),
    ADD COLUMN IF NOT EXISTS resolution_summary text,
    ADD COLUMN IF NOT EXISTS accepted_at timestamptz;

ALTER TABLE metro.support_requests
    ADD CONSTRAINT fk_support_requests_trip
        FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    ADD CONSTRAINT fk_support_requests_car
        FOREIGN KEY (operator_id, car_id) REFERENCES metro.cars (operator_id, id);

ALTER TABLE metro.notifications
    ADD COLUMN IF NOT EXISTS audience_code varchar(60),
    ADD COLUMN IF NOT EXISTS recipient_count integer CHECK (recipient_count >= 0);

ALTER TABLE metro.journeys
    ADD COLUMN IF NOT EXISTS service_date date,
    ADD COLUMN IF NOT EXISTS entry_planned_trip_id uuid,
    ADD COLUMN IF NOT EXISTS exit_planned_trip_id uuid,
    ADD COLUMN IF NOT EXISTS entry_direction smallint CHECK (entry_direction IN (0,1)),
    ADD COLUMN IF NOT EXISTS paired_journey_id uuid;

ALTER TABLE metro.journeys
    ADD CONSTRAINT fk_journeys_entry_trip
        FOREIGN KEY (operator_id, entry_planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    ADD CONSTRAINT fk_journeys_exit_trip
        FOREIGN KEY (operator_id, exit_planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    ADD CONSTRAINT fk_journeys_pair
        FOREIGN KEY (operator_id, paired_journey_id) REFERENCES metro.journeys (operator_id, id),
    ADD CONSTRAINT ck_journeys_pair
        CHECK (paired_journey_id IS NULL OR paired_journey_id <> id);

ALTER TABLE metro.afc_events
    ADD COLUMN IF NOT EXISTS planned_trip_id uuid,
    ADD COLUMN IF NOT EXISTS direction smallint CHECK (direction IN (0,1));

ALTER TABLE metro.afc_events
    ADD CONSTRAINT fk_afc_events_trip
        FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id);

-- ---------------------------------------------------------------------------
-- 2. New entities
-- ---------------------------------------------------------------------------

CREATE TABLE metro.guardian_links (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL REFERENCES metro.operators (id),
    guardian_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    child_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    relationship text NOT NULL CHECK (relationship IN ('PARENT','LEGAL_GUARDIAN','OTHER')),
    verification_type text NOT NULL DEFAULT 'GUARDIANSHIP'
        CHECK (verification_type IN ('GUARDIANSHIP','ELDERLY_AGE')),
    status text NOT NULL DEFAULT 'PENDING'
        CHECK (status IN ('PENDING','ACTIVE','REVOKED','EXPIRED')),
    evidence_reference text,
    evidence_purged_at timestamptz,
    submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    verified_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    verified_at timestamptz,
    valid_from date,
    valid_to date,
    decision_reason text,
    revocation_reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    CHECK (guardian_user_id <> child_user_id),
    CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from),
    CHECK (status <> 'ACTIVE' OR (verified_by_user_id IS NOT NULL AND verified_at IS NOT NULL)),
    CHECK (status <> 'REVOKED' OR nullif(btrim(revocation_reason), '') IS NOT NULL),
    CHECK (evidence_purged_at IS NULL OR evidence_reference IS NULL)
);

CREATE UNIQUE INDEX uq_guardian_links_active_pair
    ON metro.guardian_links (operator_id, guardian_user_id, child_user_id)
    WHERE status = 'ACTIVE';
CREATE INDEX ix_guardian_links_guardian
    ON metro.guardian_links (operator_id, guardian_user_id, status);

CREATE TABLE metro.order_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    order_id uuid NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    status text NOT NULL DEFAULT 'DRAFT'
        CHECK (status IN ('DRAFT','CONFIRMED','SUPERSEDED','CANCELLED')),
    service_date date,
    ticket_product_id uuid NOT NULL,
    origin_station_id uuid,
    destination_station_id uuid,
    beneficiary_user_id text REFERENCES public."AspNetUsers" ("Id"),
    eligibility_group varchar(50),
    age_reference_date date,
    subtotal_minor bigint NOT NULL CHECK (subtotal_minor >= 0),
    discount_minor bigint NOT NULL DEFAULT 0 CHECK (discount_minor >= 0),
    total_minor bigint NOT NULL CHECK (total_minor >= 0),
    fare_policy_version_id uuid NOT NULL,
    fare_snapshot jsonb NOT NULL DEFAULT '{}',
    change_reason text,
    created_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    confirmed_at timestamptz,
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, order_id, version),
    FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id),
    FOREIGN KEY (operator_id, ticket_product_id) REFERENCES metro.ticket_products (operator_id, id),
    FOREIGN KEY (operator_id, origin_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, destination_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, fare_policy_version_id) REFERENCES metro.fare_policy_versions (operator_id, id),
    CHECK (total_minor = subtotal_minor - discount_minor),
    CHECK (origin_station_id IS NULL OR destination_station_id IS NULL
           OR origin_station_id <> destination_station_id),
    CHECK (status <> 'CONFIRMED' OR confirmed_at IS NOT NULL)
);

CREATE UNIQUE INDEX uq_order_versions_current
    ON metro.order_versions (operator_id, order_id)
    WHERE status = 'CONFIRMED';

CREATE TABLE metro.purchase_consents (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    order_id uuid NOT NULL,
    order_version integer NOT NULL DEFAULT 1 CHECK (order_version > 0),
    buyer_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    beneficiary_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    status text NOT NULL DEFAULT 'REQUESTED'
        CHECK (status IN ('REQUESTED','ACCEPTED','DECLINED','WITHDRAWN','EXPIRED')),
    order_content_hash char(64),
    requested_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    responded_at timestamptz,
    response_note text,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, correlation_id),
    FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id),
    FOREIGN KEY (operator_id, order_id, order_version)
        REFERENCES metro.order_versions (operator_id, order_id, version),
    CHECK (expires_at > requested_at),
    CHECK (buyer_user_id <> beneficiary_user_id),
    CHECK ((status = 'REQUESTED' AND responded_at IS NULL)
           OR (status IN ('ACCEPTED','DECLINED','WITHDRAWN') AND responded_at IS NOT NULL)
           OR status = 'EXPIRED'),
    CHECK (order_content_hash IS NULL OR order_content_hash ~ '^[0-9a-f]{64}$')
);

CREATE UNIQUE INDEX uq_purchase_consents_pending
    ON metro.purchase_consents (operator_id, order_id)
    WHERE status = 'REQUESTED';

CREATE TABLE metro.trip_suggestions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    order_id uuid NOT NULL,
    order_version integer NOT NULL CHECK (order_version > 0),
    suggestion_code varchar(60) NOT NULL,
    beneficiary_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    service_date date NOT NULL,
    origin_station_id uuid NOT NULL,
    destination_station_id uuid NOT NULL,
    line_id uuid,
    direction smallint CHECK (direction IN (0,1)),
    suggested_planned_trip_id uuid,
    suggested_departure_at timestamptz,
    status text NOT NULL DEFAULT 'SUGGESTED'
        CHECK (status IN ('SUGGESTED','NO_TRIP','SUPERSEDED','FAILED')),
    no_trip_reason text,
    source text NOT NULL DEFAULT 'SCHEDULE' CHECK (source IN ('SCHEDULE','ETA','MANUAL')),
    schedule_version_id uuid,
    source_version_label varchar(60),
    data_freshness_seconds integer CHECK (data_freshness_seconds >= 0),
    update_reason text,
    computed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    superseded_at timestamptz,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, suggestion_code),
    UNIQUE (operator_id, correlation_id),
    FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id),
    FOREIGN KEY (operator_id, order_id, order_version)
        REFERENCES metro.order_versions (operator_id, order_id, version),
    FOREIGN KEY (operator_id, origin_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, destination_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, suggested_planned_trip_id)
        REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, schedule_version_id)
        REFERENCES metro.schedule_versions (operator_id, id),
    CHECK (origin_station_id <> destination_station_id),
    CHECK (status <> 'SUGGESTED' OR suggested_planned_trip_id IS NOT NULL),
    CHECK (status <> 'NO_TRIP' OR nullif(btrim(no_trip_reason), '') IS NOT NULL),
    CHECK (source <> 'SCHEDULE' OR schedule_version_id IS NOT NULL
           OR suggested_planned_trip_id IS NOT NULL)
);

CREATE UNIQUE INDEX uq_trip_suggestions_current
    ON metro.trip_suggestions (operator_id, order_id, order_version)
    WHERE superseded_at IS NULL;
CREATE INDEX ix_trip_suggestions_trip
    ON metro.trip_suggestions (operator_id, suggested_planned_trip_id, service_date);

CREATE TABLE metro.load_observations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    planned_trip_id uuid NOT NULL,
    capacity_profile_id uuid,
    observed_load_kg numeric(8,1) NOT NULL CHECK (observed_load_kg >= 0),
    source text NOT NULL DEFAULT 'MANUAL' CHECK (source IN ('MANUAL')),
    recorded_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    observed_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    note text,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, correlation_id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, capacity_profile_id)
        REFERENCES metro.capacity_profiles (operator_id, id)
);

CREATE INDEX ix_load_observations_trip
    ON metro.load_observations (operator_id, planned_trip_id, observed_at DESC);

-- DATA-012: ETA updates are entered by an administrator, never derived from load.
CREATE TABLE metro.trip_eta_updates (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    planned_trip_id uuid NOT NULL,
    station_id uuid NOT NULL,
    expected_arrival_at timestamptz,
    expected_departure_at timestamptz,
    source text NOT NULL DEFAULT 'MANUAL' CHECK (source IN ('MANUAL')),
    recorded_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    note text,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, correlation_id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    CHECK (expected_arrival_at IS NOT NULL OR expected_departure_at IS NOT NULL),
    CHECK (expected_arrival_at IS NULL OR expected_departure_at IS NULL
           OR expected_departure_at >= expected_arrival_at)
);

CREATE INDEX ix_trip_eta_updates_trip
    ON metro.trip_eta_updates (operator_id, planned_trip_id, station_id, recorded_at DESC);

CREATE TABLE metro.data_quality_days (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL REFERENCES metro.operators (id),
    quality_date date NOT NULL,
    status text NOT NULL CHECK (status IN ('COMPLETE','INCOMPLETE')),
    entry_count integer CHECK (entry_count >= 0),
    confirmed_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    confirmed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    evidence_reference text,
    note text,
    UNIQUE (operator_id, quality_date)
);

CREATE TABLE metro.ticket_change_requests (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    request_code varchar(60) NOT NULL,
    ticket_id uuid NOT NULL,
    order_id uuid NOT NULL,
    requested_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    reason_type text NOT NULL CHECK (reason_type IN ('SERVICE_DISRUPTION','SYSTEM_ERROR','OTHER')),
    incident_id uuid,
    original_service_date date,
    requested_service_date date,
    original_origin_station_id uuid,
    original_destination_station_id uuid,
    new_origin_station_id uuid,
    new_destination_station_id uuid,
    original_planned_trip_id uuid,
    new_planned_trip_id uuid,
    status text NOT NULL DEFAULT 'REQUESTED'
        CHECK (status IN ('REQUESTED','APPROVED','REJECTED','APPLIED','CANCELLED','EXPIRED')),
    decided_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    decided_at timestamptz,
    decision_reason text,
    applied_at timestamptz,
    change_sequence smallint NOT NULL DEFAULT 1 CHECK (change_sequence > 0),
    related_child_ticket_id uuid,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, request_code),
    UNIQUE (operator_id, correlation_id),
    FOREIGN KEY (operator_id, ticket_id) REFERENCES metro.tickets (operator_id, id),
    FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id),
    FOREIGN KEY (operator_id, incident_id) REFERENCES metro.incidents (operator_id, id),
    FOREIGN KEY (operator_id, original_origin_station_id)
        REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, original_destination_station_id)
        REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, new_origin_station_id)
        REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, new_destination_station_id)
        REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, original_planned_trip_id)
        REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, new_planned_trip_id)
        REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, related_child_ticket_id)
        REFERENCES metro.tickets (operator_id, id),
    CHECK (requested_service_date IS NOT NULL
           OR new_origin_station_id IS NOT NULL
           OR new_destination_station_id IS NOT NULL
           OR new_planned_trip_id IS NOT NULL),
    CHECK (new_origin_station_id IS NULL OR new_destination_station_id IS NULL
           OR new_origin_station_id <> new_destination_station_id),
    CHECK (status NOT IN ('APPROVED','REJECTED','APPLIED')
           OR (decided_by_user_id IS NOT NULL AND decided_at IS NOT NULL
               AND nullif(btrim(decision_reason), '') IS NOT NULL)),
    CHECK (status <> 'APPLIED' OR applied_at IS NOT NULL)
);

CREATE UNIQUE INDEX uq_ticket_change_open
    ON metro.ticket_change_requests (operator_id, ticket_id)
    WHERE status IN ('REQUESTED','APPROVED');
CREATE UNIQUE INDEX uq_ticket_change_applied
    ON metro.ticket_change_requests (operator_id, ticket_id)
    WHERE status = 'APPLIED';

CREATE TABLE metro.lost_reports (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL REFERENCES metro.operators (id),
    report_code varchar(60) NOT NULL,
    submitted_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    owner_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    item_category varchar(60) NOT NULL,
    color text,
    public_description text NOT NULL,
    secret_characteristics text,
    lost_from timestamptz NOT NULL,
    lost_to timestamptz,
    station_id uuid,
    line_id uuid,
    planned_trip_id uuid,
    car_id uuid,
    attachment_reference text,
    status text NOT NULL DEFAULT 'OPEN'
        CHECK (status IN ('OPEN','MATCHED','CLAIM_PENDING','RESOLVED','WITHDRAWN','OVERDUE','CLOSED')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, report_code),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, car_id) REFERENCES metro.cars (operator_id, id),
    CHECK (lost_to IS NULL OR lost_to >= lost_from)
);

CREATE INDEX ix_lost_reports_owner
    ON metro.lost_reports (operator_id, owner_user_id, created_at DESC);
CREATE INDEX ix_lost_reports_filter
    ON metro.lost_reports (operator_id, status, item_category, lost_from DESC);

CREATE TABLE metro.found_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL REFERENCES metro.operators (id),
    item_code varchar(60) NOT NULL,
    received_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    holding_station_id uuid NOT NULL,
    found_at timestamptz,
    planned_trip_id uuid,
    car_id uuid,
    public_description text NOT NULL,
    secret_characteristics text,
    attachment_reference text,
    status text NOT NULL DEFAULT 'HELD'
        CHECK (status IN ('HELD','CLAIM_PENDING','RELEASED','OVERDUE','DISPOSED')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, item_code),
    FOREIGN KEY (operator_id, holding_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, car_id) REFERENCES metro.cars (operator_id, id)
);

CREATE INDEX ix_found_items_search
    ON metro.found_items (operator_id, status, holding_station_id, received_at DESC);

CREATE TABLE metro.lost_item_claims (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    claim_code varchar(60) NOT NULL,
    lost_report_id uuid,
    found_item_id uuid NOT NULL,
    claimant_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    evidence_reference text,
    status text NOT NULL DEFAULT 'SUBMITTED'
        CHECK (status IN ('SUBMITTED','APPROVED','REJECTED','WITHDRAWN','EXPIRED')),
    decided_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    decided_at timestamptz,
    decision_reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, claim_code),
    FOREIGN KEY (operator_id, lost_report_id) REFERENCES metro.lost_reports (operator_id, id),
    FOREIGN KEY (operator_id, found_item_id) REFERENCES metro.found_items (operator_id, id),
    CHECK (status NOT IN ('APPROVED','REJECTED')
           OR (decided_by_user_id IS NOT NULL AND decided_at IS NOT NULL
               AND nullif(btrim(decision_reason), '') IS NOT NULL))
);

CREATE UNIQUE INDEX uq_lost_item_claims_approved
    ON metro.lost_item_claims (operator_id, found_item_id)
    WHERE status = 'APPROVED';
CREATE UNIQUE INDEX uq_lost_item_claims_open_per_user
    ON metro.lost_item_claims (operator_id, found_item_id, claimant_user_id)
    WHERE status IN ('SUBMITTED','APPROVED');

CREATE TABLE metro.lost_item_handovers (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    found_item_id uuid NOT NULL,
    claim_id uuid NOT NULL,
    handed_to_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    handed_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    handed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    evidence_reference text,
    confirmation_note text,
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, found_item_id),
    UNIQUE (operator_id, claim_id),
    FOREIGN KEY (operator_id, found_item_id) REFERENCES metro.found_items (operator_id, id),
    FOREIGN KEY (operator_id, claim_id) REFERENCES metro.lost_item_claims (operator_id, id)
);

CREATE TABLE metro.station_support_capacities (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    station_id uuid NOT NULL,
    support_date date NOT NULL,
    slot_start time NOT NULL,
    slot_end time NOT NULL,
    capacity smallint NOT NULL DEFAULT 2 CHECK (capacity > 0),
    used_count smallint NOT NULL DEFAULT 0 CHECK (used_count >= 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, station_id, support_date, slot_start),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    CHECK (slot_end > slot_start),
    CHECK (used_count <= capacity)
);

CREATE TABLE metro.accessibility_requests (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    request_code varchar(60) NOT NULL,
    submitted_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    passenger_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    station_id uuid NOT NULL,
    slot_capacity_id uuid NOT NULL,
    support_date date NOT NULL,
    slot_start time NOT NULL,
    slot_end time NOT NULL,
    support_type text NOT NULL
        CHECK (support_type IN ('WHEELCHAIR','VISUAL','HEARING','ELDERLY','OTHER')),
    needs_description text,
    meeting_point text,
    status text NOT NULL DEFAULT 'PENDING'
        CHECK (status IN ('PENDING','CONFIRMED','IN_PROGRESS','COMPLETED','REJECTED','CANCELLED','EXPIRED')),
    assigned_to_user_id text REFERENCES public."AspNetUsers" ("Id"),
    confirmed_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    rejection_reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, request_code),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, slot_capacity_id)
        REFERENCES metro.station_support_capacities (operator_id, id),
    CHECK (slot_end > slot_start),
    CHECK (status <> 'REJECTED' OR nullif(btrim(rejection_reason), '') IS NOT NULL),
    CHECK (status <> 'COMPLETED' OR completed_at IS NOT NULL)
);

CREATE INDEX ix_accessibility_requests_slot
    ON metro.accessibility_requests (operator_id, station_id, support_date, slot_start, status);

-- DATA-015 history: every status decision keeps actor, time and reason.
CREATE TABLE metro.accessibility_request_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    request_id uuid NOT NULL,
    from_status text,
    to_status text NOT NULL,
    actor_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id),
    FOREIGN KEY (operator_id, request_id)
        REFERENCES metro.accessibility_requests (operator_id, id)
);

CREATE INDEX ix_accessibility_request_events
    ON metro.accessibility_request_events (operator_id, request_id, created_at);

CREATE TABLE metro.notification_deliveries (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL,
    notification_id uuid NOT NULL,
    attempt_no smallint NOT NULL CHECK (attempt_no BETWEEN 1 AND 10),
    status text NOT NULL CHECK (status IN ('QUEUED','SENT','FAILED')),
    attempted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    provider_reference text,
    error_code varchar(80),
    error_message text,
    next_retry_at timestamptz,
    UNIQUE (operator_id, id),
    UNIQUE (operator_id, notification_id, attempt_no),
    FOREIGN KEY (operator_id, notification_id) REFERENCES metro.notifications (operator_id, id)
);

-- ---------------------------------------------------------------------------
-- 3. Orders and tickets: v1.2 identity, validity and one_active
-- ---------------------------------------------------------------------------

ALTER TABLE metro.orders
    ADD COLUMN IF NOT EXISTS service_date date,
    ADD COLUMN IF NOT EXISTS free_confirmed_at timestamptz,
    ADD COLUMN IF NOT EXISTS order_version integer NOT NULL DEFAULT 1 CHECK (order_version > 0),
    ADD COLUMN IF NOT EXISTS eligibility_group varchar(50),
    ADD COLUMN IF NOT EXISTS age_reference_date date,
    ADD COLUMN IF NOT EXISTS consent_id uuid;

ALTER TABLE metro.orders
    ADD CONSTRAINT fk_orders_consent
        FOREIGN KEY (operator_id, consent_id) REFERENCES metro.purchase_consents (operator_id, id);

ALTER TABLE metro.tickets
    ADD COLUMN IF NOT EXISTS order_id uuid,
    ADD COLUMN IF NOT EXISTS order_version integer CHECK (order_version > 0),
    ADD COLUMN IF NOT EXISTS service_date date,
    ADD COLUMN IF NOT EXISTS origin_station_id uuid,
    ADD COLUMN IF NOT EXISTS destination_station_id uuid,
    ADD COLUMN IF NOT EXISTS related_ticket_id uuid,
    ADD COLUMN IF NOT EXISTS relation_type text,
    ADD COLUMN IF NOT EXISTS guardian_link_id uuid,
    ADD COLUMN IF NOT EXISTS guardian_user_id text REFERENCES public."AspNetUsers" ("Id"),
    ADD COLUMN IF NOT EXISTS change_count smallint NOT NULL DEFAULT 0 CHECK (change_count >= 0);

ALTER TABLE metro.tickets
    ADD CONSTRAINT fk_tickets_origin
        FOREIGN KEY (operator_id, origin_station_id) REFERENCES metro.stations (operator_id, id),
    ADD CONSTRAINT fk_tickets_destination
        FOREIGN KEY (operator_id, destination_station_id) REFERENCES metro.stations (operator_id, id),
    ADD CONSTRAINT fk_tickets_related
        FOREIGN KEY (operator_id, related_ticket_id) REFERENCES metro.tickets (operator_id, id),
    ADD CONSTRAINT fk_tickets_guardian_link
        FOREIGN KEY (operator_id, guardian_link_id) REFERENCES metro.guardian_links (operator_id, id),
    ADD CONSTRAINT ck_tickets_route
        CHECK (origin_station_id IS NULL OR destination_station_id IS NULL
               OR origin_station_id <> destination_station_id),
    ADD CONSTRAINT ck_tickets_relation
        CHECK (relation_type IS NULL
               OR relation_type IN ('CHILD_OF_GUARDIAN','GUARDIAN_OF_CHILD')),
    ADD CONSTRAINT ck_tickets_relation_links
        CHECK (relation_type IS NULL
               OR (related_ticket_id IS NOT NULL AND guardian_link_id IS NOT NULL
                   AND guardian_user_id IS NOT NULL)),
    ADD CONSTRAINT ck_tickets_related_self
        CHECK (related_ticket_id IS NULL OR related_ticket_id <> id);

CREATE INDEX ix_tickets_service_date
    ON metro.tickets (operator_id, service_date, status);

-- SRS "trùng hiệu lực": a passenger may not hold two usable tickets whose
-- validity windows overlap. NULL validity means an open-ended window.
DO $overlap_preflight$
DECLARE
    v_conflict text;
BEGIN
    SELECT string_agg(DISTINCT format('%s/%s', a.operator_id, a.holder_user_id), ', ')
    INTO v_conflict
    FROM metro.tickets a
    JOIN metro.tickets b
      ON b.operator_id = a.operator_id
     AND b.holder_user_id = a.holder_user_id
     AND b.id <> a.id
     AND b.status IN ('ACTIVE','IN_USE')
     AND tstzrange(a.effective_from, a.expires_at, '[)')
         && tstzrange(b.effective_from, b.expires_at, '[)')
    WHERE a.status IN ('ACTIVE','IN_USE')
      AND a.holder_user_id IS NOT NULL;
    IF v_conflict IS NOT NULL THEN
        RAISE EXCEPTION 'Ticket validity overlaps for holder(s): %', v_conflict;
    END IF;
END;
$overlap_preflight$;

ALTER TABLE metro.tickets
    ADD CONSTRAINT ex_tickets_no_overlapping_validity
    EXCLUDE USING gist (
        operator_id WITH =,
        holder_user_id WITH =,
        tstzrange(effective_from, expires_at, '[)') WITH &&
    ) WHERE (status IN ('ACTIVE','IN_USE') AND holder_user_id IS NOT NULL);

-- Fill ticket travel context from the order item so callers cannot forget it.
CREATE OR REPLACE FUNCTION metro.fill_ticket_travel_context()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_service_date date;
    v_origin uuid;
    v_destination uuid;
    v_expiry_rule text;
    v_order_id uuid;
    v_order_version integer;
BEGIN
    SELECT COALESCE(o.service_date, pt.service_date),
           oi.origin_station_id,
           oi.destination_station_id,
           tp.expiry_rule,
           o.id,
           o.order_version
    INTO v_service_date, v_origin, v_destination, v_expiry_rule, v_order_id, v_order_version
    FROM metro.order_items oi
    JOIN metro.orders o
      ON o.operator_id = oi.operator_id AND o.id = oi.order_id
    JOIN metro.ticket_products tp
      ON tp.operator_id = oi.operator_id AND tp.id = oi.ticket_product_id
    LEFT JOIN metro.planned_trips pt
      ON pt.operator_id = oi.operator_id AND pt.id = oi.planned_trip_id
    WHERE oi.operator_id = NEW.operator_id AND oi.id = NEW.order_item_id;

    NEW.order_id := COALESCE(NEW.order_id, v_order_id);
    NEW.order_version := COALESCE(NEW.order_version, v_order_version);
    NEW.service_date := COALESCE(NEW.service_date, v_service_date);
    NEW.origin_station_id := COALESCE(NEW.origin_station_id, v_origin);
    NEW.destination_station_id := COALESCE(NEW.destination_station_id, v_destination);

    IF NEW.effective_from IS NULL AND NEW.expires_at IS NULL
       AND v_expiry_rule = 'SINGLE_JOURNEY' AND NEW.service_date IS NOT NULL THEN
        NEW.effective_from := (NEW.service_date::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh');
        NEW.expires_at := ((NEW.service_date + 1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh');
    END IF;

    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_tickets_fill_travel_context
BEFORE INSERT ON metro.tickets
FOR EACH ROW EXECUTE FUNCTION metro.fill_ticket_travel_context();

-- Auto-supersede the previous current suggestion for the same order version.
CREATE OR REPLACE FUNCTION metro.supersede_trip_suggestion()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
BEGIN
    UPDATE metro.trip_suggestions
    SET superseded_at = NEW.computed_at,
        status = CASE WHEN status = 'SUGGESTED' THEN 'SUPERSEDED' ELSE status END
    WHERE operator_id = NEW.operator_id
      AND order_id = NEW.order_id
      AND order_version = NEW.order_version
      AND superseded_at IS NULL
      AND id <> NEW.id;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_trip_suggestions_supersede
BEFORE INSERT ON metro.trip_suggestions
FOR EACH ROW EXECUTE FUNCTION metro.supersede_trip_suggestion();

-- ---------------------------------------------------------------------------
-- 4. Backfill order versions from existing single-item orders
-- ---------------------------------------------------------------------------

INSERT INTO metro.order_versions (
    operator_id, order_id, version, status, service_date, ticket_product_id,
    origin_station_id, destination_station_id, beneficiary_user_id,
    eligibility_group, age_reference_date, subtotal_minor, discount_minor,
    total_minor, fare_policy_version_id, fare_snapshot, created_by_user_id,
    created_at, confirmed_at
)
SELECT o.operator_id,
       o.id,
       1,
       CASE WHEN o.status IN ('PAID','ISSUED','REFUND_PENDING','REFUNDED')
            THEN 'CONFIRMED' ELSE 'DRAFT' END,
       COALESCE(o.service_date, pt.service_date),
       oi.ticket_product_id,
       oi.origin_station_id,
       oi.destination_station_id,
       o.beneficiary_user_id,
       o.eligibility_group,
       o.age_reference_date,
       o.subtotal_minor,
       o.discount_minor,
       o.total_minor,
       o.fare_policy_version_id,
       o.fare_snapshot,
       o.created_by_user_id,
       o.created_at,
       CASE WHEN o.status IN ('PAID','ISSUED','REFUND_PENDING','REFUNDED')
            THEN COALESCE(o.paid_at, o.issued_at, o.created_at) END
FROM metro.orders o
JOIN LATERAL (
    SELECT i.*
    FROM metro.order_items i
    WHERE i.operator_id = o.operator_id AND i.order_id = o.id
    ORDER BY i.created_at, i.id
    LIMIT 1
) oi ON true
LEFT JOIN metro.planned_trips pt
  ON pt.operator_id = oi.operator_id AND pt.id = oi.planned_trip_id
WHERE NOT EXISTS (
    SELECT 1 FROM metro.order_versions v
    WHERE v.operator_id = o.operator_id AND v.order_id = o.id
);

-- Attach legacy capacity profiles to their trips where unambiguous.
UPDATE metro.planned_trips pt
SET load_profile_id = sub.capacity_profile_id
FROM (
    SELECT operator_id, planned_trip_id,
           (array_agg(capacity_profile_id))[1] AS capacity_profile_id
    FROM metro.trip_capacity_segments
    GROUP BY operator_id, planned_trip_id
    HAVING count(DISTINCT capacity_profile_id) = 1
) sub
WHERE pt.operator_id = sub.operator_id
  AND pt.id = sub.planned_trip_id
  AND pt.load_profile_id IS NULL;

-- Backfill existing tickets' travel context.
UPDATE metro.tickets t
SET service_date = COALESCE(t.service_date, pt.service_date, o.service_date),
    origin_station_id = COALESCE(t.origin_station_id, oi.origin_station_id),
    destination_station_id = COALESCE(t.destination_station_id, oi.destination_station_id)
FROM metro.order_items oi
JOIN metro.orders o
  ON o.operator_id = oi.operator_id AND o.id = oi.order_id
LEFT JOIN metro.planned_trips pt
  ON pt.operator_id = oi.operator_id AND pt.id = oi.planned_trip_id
WHERE t.operator_id = oi.operator_id
  AND t.order_item_id = oi.id
  AND (t.service_date IS NULL OR t.origin_station_id IS NULL OR t.destination_station_id IS NULL);

-- Backfill the order/version reference carried by DATA-008 tickets.
UPDATE metro.tickets t
SET order_id = oi.order_id,
    order_version = 1
FROM metro.order_items oi
WHERE t.operator_id = oi.operator_id
  AND t.order_item_id = oi.id
  AND (t.order_id IS NULL OR t.order_version IS NULL);

ALTER TABLE metro.tickets
    ADD CONSTRAINT fk_tickets_order
        FOREIGN KEY (operator_id, order_id) REFERENCES metro.orders (operator_id, id);

-- ---------------------------------------------------------------------------
-- 5. Workflow functions
-- ---------------------------------------------------------------------------

-- BR-007: a verified free ticket (child under six, under an approved guardian link)
-- is issued without a fake payment. The order must still be fully discounted.
CREATE OR REPLACE FUNCTION metro.issue_free_order(
    p_operator_id uuid,
    p_order_id uuid,
    p_actor_user_id text,
    p_reason text DEFAULT 'Free ticket confirmed'
) RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_order metro.orders%ROWTYPE;
    v_issued integer;
BEGIN
    SELECT * INTO v_order
    FROM metro.orders
    WHERE operator_id = p_operator_id AND id = p_order_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Order not found';
    END IF;

    IF v_order.free_confirmed_at IS NOT NULL AND v_order.status = 'ISSUED' THEN
        RETURN 0;
    END IF;
    IF v_order.status NOT IN ('DRAFT','HELD','PENDING_PAYMENT') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = format('Order %s cannot be issued free from status %s', v_order.id, v_order.status);
    END IF;
    IF v_order.total_minor <> 0
       OR v_order.discount_minor <> v_order.subtotal_minor
       OR v_order.subtotal_minor <= 0 THEN
        RAISE EXCEPTION USING ERRCODE = '22023',
            MESSAGE = 'Free issuance requires a fully discounted, zero-payable order';
    END IF;
    IF v_order.beneficiary_user_id IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '23502', MESSAGE = 'Free order requires a beneficiary';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM metro.order_items oi
        WHERE oi.operator_id = p_operator_id AND oi.order_id = p_order_id
    ) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Free order has no order item';
    END IF;
    IF EXISTS (
        SELECT 1 FROM metro.tickets t
        WHERE t.operator_id = p_operator_id
          AND t.holder_user_id = v_order.beneficiary_user_id
          AND t.status IN ('ACTIVE','IN_USE')
    ) THEN
        RAISE EXCEPTION USING ERRCODE = '23505',
            MESSAGE = 'Beneficiary already has an active ticket (BR-012 one_active)';
    END IF;

    UPDATE metro.orders
    SET status = 'ISSUED',
        free_confirmed_at = clock_timestamp(),
        payment_started_at = COALESCE(payment_started_at, clock_timestamp()),
        issued_at = clock_timestamp(),
        updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_order_id;

    INSERT INTO metro.tickets (
        operator_id, ticket_code, order_item_id, ticket_product_id, holder_user_id,
        sequence_no, status, fare_policy_version_id
    )
    SELECT oi.operator_id,
           'TKT-' || replace(oi.id::text, '-', '') || '-' || gs::text,
           oi.id,
           oi.ticket_product_id,
           v_order.beneficiary_user_id,
           gs,
           'ISSUED',
           v_order.fare_policy_version_id
    FROM metro.order_items oi
    CROSS JOIN LATERAL generate_series(1, oi.passenger_count) AS gs
    WHERE oi.operator_id = p_operator_id AND oi.order_id = p_order_id
    ON CONFLICT (operator_id, order_item_id, sequence_no) DO NOTHING;
    GET DIAGNOSTICS v_issued = ROW_COUNT;

    INSERT INTO metro.audit_logs (
        operator_id, actor_user_id, action, entity_type, entity_id,
        changed_fields, reason, outcome
    ) VALUES (
        p_operator_id, p_actor_user_id, 'ISSUE_FREE_ORDER', 'ORDER', p_order_id::text,
        ARRAY['status','free_confirmed_at','issued_at'], p_reason, 'SUCCEEDED'
    );

    RETURN v_issued;
END;
$function$;

-- UC42: apply an approved disruption change, revoke the old QR and keep history.
CREATE OR REPLACE FUNCTION metro.apply_ticket_change(
    p_operator_id uuid,
    p_change_request_id uuid,
    p_actor_user_id text
) RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_request metro.ticket_change_requests%ROWTYPE;
    v_ticket metro.tickets%ROWTYPE;
    v_product_type text;
    v_new_date date;
    v_new_origin uuid;
    v_new_destination uuid;
BEGIN
    SELECT * INTO v_request
    FROM metro.ticket_change_requests
    WHERE operator_id = p_operator_id AND id = p_change_request_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Ticket change request not found';
    END IF;
    IF v_request.status <> 'APPROVED' THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = format('Ticket change request is %s, expected APPROVED', v_request.status);
    END IF;

    SELECT * INTO v_ticket
    FROM metro.tickets
    WHERE operator_id = p_operator_id AND id = v_request.ticket_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Ticket not found';
    END IF;
    IF v_ticket.status NOT IN ('ISSUED','ACTIVE')
       OR v_ticket.first_used_at IS NOT NULL
       OR EXISTS (
           SELECT 1 FROM metro.journeys j
           WHERE j.operator_id = p_operator_id
             AND j.ticket_id = v_ticket.id
             AND j.status = 'OPEN'
       ) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = 'Only an unused ticket can be changed';
    END IF;
    IF v_ticket.change_count >= 1 THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = 'Ticket change limit reached (one change per ticket)';
    END IF;

    SELECT product_type INTO v_product_type
    FROM metro.ticket_products
    WHERE operator_id = p_operator_id AND id = v_ticket.ticket_product_id;

    v_new_date := COALESCE(v_request.requested_service_date, v_ticket.service_date);
    v_new_origin := COALESCE(v_request.new_origin_station_id, v_ticket.origin_station_id);
    v_new_destination := COALESCE(v_request.new_destination_station_id, v_ticket.destination_station_id);

    UPDATE metro.tickets
    SET service_date = v_new_date,
        origin_station_id = v_new_origin,
        destination_station_id = v_new_destination,
        effective_from = CASE
            WHEN v_product_type = 'SINGLE_TRIP' AND v_new_date IS NOT NULL
            THEN (v_new_date::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh')
            ELSE effective_from END,
        expires_at = CASE
            WHEN v_product_type = 'SINGLE_TRIP' AND v_new_date IS NOT NULL
            THEN ((v_new_date + 1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh')
            ELSE expires_at END,
        change_count = change_count + 1,
        updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = v_ticket.id;

    IF v_request.new_planned_trip_id IS NOT NULL THEN
        UPDATE metro.order_items
        SET planned_trip_id = v_request.new_planned_trip_id
        WHERE operator_id = p_operator_id AND id = v_ticket.order_item_id;
    END IF;
    IF v_request.requested_service_date IS NOT NULL THEN
        UPDATE metro.orders
        SET service_date = v_request.requested_service_date,
            updated_at = clock_timestamp()
        WHERE operator_id = p_operator_id AND id = v_request.order_id;
    END IF;

    UPDATE metro.ticket_credentials
    SET status = 'REVOKED', revoked_at = clock_timestamp()
    WHERE operator_id = p_operator_id
      AND ticket_id = v_ticket.id
      AND status = 'ACTIVE';

    UPDATE metro.ticket_change_requests
    SET status = 'APPLIED', applied_at = clock_timestamp(), updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_change_request_id;

    INSERT INTO metro.audit_logs (
        operator_id, actor_user_id, action, entity_type, entity_id,
        changed_fields, reason, outcome
    ) VALUES (
        p_operator_id, p_actor_user_id, 'APPLY_TICKET_CHANGE', 'TICKET', v_ticket.id::text,
        ARRAY['service_date','origin_station_id','destination_station_id','expires_at','change_count'],
        v_request.decision_reason, 'SUCCEEDED'
    );

    RETURN 1;
END;
$function$;

-- UC44: confirming a slot consumes one of the two station slots and is atomic.
CREATE OR REPLACE FUNCTION metro.confirm_accessibility_request(
    p_operator_id uuid,
    p_request_id uuid,
    p_actor_user_id text
) RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_request metro.accessibility_requests%ROWTYPE;
    v_slot metro.station_support_capacities%ROWTYPE;
    v_local_now timestamp;
BEGIN
    SELECT * INTO v_request
    FROM metro.accessibility_requests
    WHERE operator_id = p_operator_id AND id = p_request_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Accessibility request not found';
    END IF;
    IF v_request.status <> 'PENDING' THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = format('Accessibility request is %s, expected PENDING', v_request.status);
    END IF;

    SELECT * INTO v_slot
    FROM metro.station_support_capacities
    WHERE operator_id = p_operator_id AND id = v_request.slot_capacity_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Support slot not found';
    END IF;

    v_local_now := clock_timestamp() AT TIME ZONE 'Asia/Ho_Chi_Minh';
    IF (v_slot.support_date + v_slot.slot_end) <= v_local_now THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Support slot has already ended';
    END IF;
    IF v_slot.used_count >= v_slot.capacity THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Support slot is full';
    END IF;

    UPDATE metro.station_support_capacities
    SET used_count = used_count + 1, updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = v_slot.id;

    UPDATE metro.accessibility_requests
    SET status = 'CONFIRMED',
        assigned_to_user_id = p_actor_user_id,
        confirmed_at = clock_timestamp(),
        updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_request_id;

    RETURN v_slot.capacity - v_slot.used_count - 1;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.release_accessibility_capacity(
    p_operator_id uuid,
    p_request_id uuid
) RETURNS boolean
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_request metro.accessibility_requests%ROWTYPE;
BEGIN
    SELECT * INTO v_request
    FROM metro.accessibility_requests
    WHERE operator_id = p_operator_id AND id = p_request_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Accessibility request not found';
    END IF;
    IF v_request.status IN ('CANCELLED','REJECTED','EXPIRED','COMPLETED') THEN
        RETURN false;
    END IF;
    IF v_request.status = 'IN_PROGRESS' THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = 'In-progress support cannot be cancelled by the requester';
    END IF;
    IF v_request.status = 'CONFIRMED' THEN
        UPDATE metro.station_support_capacities
        SET used_count = GREATEST(used_count - 1, 0), updated_at = clock_timestamp()
        WHERE operator_id = p_operator_id AND id = v_request.slot_capacity_id;
    END IF;

    UPDATE metro.accessibility_requests
    SET status = 'CANCELLED', updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_request_id;
    RETURN true;
END;
$function$;

-- UC46: items received more than p_days ago move to OVERDUE without deletion.
CREATE OR REPLACE FUNCTION metro.mark_overdue_lost_items(
    p_operator_id uuid,
    p_days integer DEFAULT 30
) RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_found integer;
    v_lost integer;
BEGIN
    IF p_days < 1 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'p_days must be positive';
    END IF;

    UPDATE metro.found_items
    SET status = 'OVERDUE', updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id
      AND status = 'HELD'
      AND received_at < clock_timestamp() - make_interval(days => p_days);
    GET DIAGNOSTICS v_found = ROW_COUNT;

    UPDATE metro.lost_reports
    SET status = 'OVERDUE', updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id
      AND status = 'OPEN'
      AND created_at < clock_timestamp() - make_interval(days => p_days);
    GET DIAGNOSTICS v_lost = ROW_COUNT;

    RETURN v_found + v_lost;
END;
$function$;

-- UC37: total car limits must not exceed the versioned trip limit.
CREATE OR REPLACE FUNCTION metro.validate_load_profile(
    p_operator_id uuid,
    p_capacity_profile_id uuid
) RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_profile metro.capacity_profiles%ROWTYPE;
    v_total numeric;
    v_missing integer;
BEGIN
    SELECT * INTO v_profile
    FROM metro.capacity_profiles
    WHERE operator_id = p_operator_id AND id = p_capacity_profile_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Capacity profile not found';
    END IF;
    IF v_profile.max_load_kg IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '23514',
            MESSAGE = 'Capacity profile has no max_load_kg';
    END IF;
    IF v_profile.trainset_id IS NULL THEN
        RETURN;
    END IF;

    SELECT count(*) INTO v_missing
    FROM metro.cars
    WHERE operator_id = p_operator_id
      AND trainset_id = v_profile.trainset_id
      AND max_load_kg IS NULL;
    IF v_missing > 0 THEN
        RAISE EXCEPTION USING ERRCODE = '23514',
            MESSAGE = 'Every car must declare max_load_kg before publishing a load profile';
    END IF;

    SELECT COALESCE(sum(max_load_kg), 0) INTO v_total
    FROM metro.cars
    WHERE operator_id = p_operator_id AND trainset_id = v_profile.trainset_id;

    IF v_total > v_profile.max_load_kg THEN
        RAISE EXCEPTION USING ERRCODE = '23514',
            MESSAGE = format('Car load limits (%s kg) exceed trip limit (%s kg)', v_total, v_profile.max_load_kg);
    END IF;
END;
$function$;

-- Replace the state guard so free issuance and free cancellation are legal.
CREATE OR REPLACE FUNCTION metro.enforce_workflow_state_transition()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
BEGIN
    IF OLD.status = NEW.status THEN
        RETURN NEW;
    END IF;

    IF TG_TABLE_NAME = 'orders' THEN
        IF NOT (
            (OLD.status = 'DRAFT' AND NEW.status IN ('HELD','PENDING_PAYMENT','EXPIRED','CANCELLED')) OR
            (OLD.status = 'HELD' AND NEW.status IN ('PENDING_PAYMENT','PAID','EXPIRED','CANCELLED')) OR
            (OLD.status = 'PENDING_PAYMENT' AND NEW.status IN ('HELD','PAID','EXPIRED','CANCELLED')) OR
            (OLD.status = 'PAID' AND NEW.status IN ('ISSUED','REFUND_PENDING')) OR
            (OLD.status = 'ISSUED' AND NEW.status IN ('REFUND_PENDING')) OR
            (OLD.status = 'REFUND_PENDING' AND NEW.status IN ('REFUNDED','ISSUED')) OR
            (OLD.status = 'EXPIRED' AND NEW.status = 'CANCELLED')
        ) THEN
            -- BR-007: zero-payable free ticket, no payment attempt exists.
            -- FR-42.05: an un-entered free child ticket may be cancelled.
            IF NOT (
                (OLD.status IN ('DRAFT','HELD','PENDING_PAYMENT')
                 AND NEW.status = 'ISSUED' AND NEW.total_minor = 0
                 AND NEW.free_confirmed_at IS NOT NULL) OR
                (OLD.status = 'ISSUED' AND NEW.status = 'CANCELLED'
                 AND OLD.free_confirmed_at IS NOT NULL)
            ) THEN
                RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = format('Invalid order state transition: %s -> %s', OLD.status, NEW.status);
            END IF;
        END IF;
    ELSIF TG_TABLE_NAME = 'payments' AND NOT (
        (OLD.status = 'PENDING' AND NEW.status IN ('SUCCEEDED','FAILED','EXPIRED','CANCELLED','REVIEW_REQUIRED')) OR
        (OLD.status = 'SUCCEEDED' AND NEW.status IN ('PARTIALLY_REFUNDED','REFUNDED','REVIEW_REQUIRED')) OR
        (OLD.status = 'PARTIALLY_REFUNDED' AND NEW.status IN ('REFUNDED','REVIEW_REQUIRED')) OR
        (OLD.status = 'REVIEW_REQUIRED' AND NEW.status IN ('SUCCEEDED','FAILED','CANCELLED','PARTIALLY_REFUNDED','REFUNDED'))
    ) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = format('Invalid payment state transition: %s -> %s', OLD.status, NEW.status);
    ELSIF TG_TABLE_NAME = 'tickets' AND NOT (
        (OLD.status = 'ISSUED' AND NEW.status IN ('ACTIVE','EXPIRED','REFUND_PENDING','REVOKED')) OR
        (OLD.status = 'ACTIVE' AND NEW.status IN ('IN_USE','USED','EXPIRED','REFUND_PENDING','REVOKED')) OR
        (OLD.status = 'IN_USE' AND NEW.status IN ('USED','REFUND_PENDING','REVOKED')) OR
        (OLD.status = 'REFUND_PENDING' AND NEW.status IN ('REFUNDED','ISSUED','ACTIVE','REVOKED'))
    ) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = format('Invalid ticket state transition: %s -> %s', OLD.status, NEW.status);
    END IF;
    RETURN NEW;
END;
$function$;

-- New protected entity types must not request physical deletion.
CREATE OR REPLACE FUNCTION metro.validate_retention_policy_target()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
BEGIN
    IF upper(NEW.entity_type) = ANY (ARRAY[
        'AUDIT_LOG','ORDER','ORDER_ITEM','ORDER_VERSION','PURCHASE_CONSENT',
        'CAPACITY_HOLD','PAYMENT','PAYMENT_EVENT','TICKET','TICKET_CREDENTIAL',
        'TICKET_CHANGE_REQUEST','JOURNEY','AFC_EVENT','TRIP_SUGGESTION',
        'LOST_REPORT','FOUND_ITEM','LOST_ITEM_CLAIM','LOST_ITEM_HANDOVER',
        'ACCESSIBILITY_REQUEST','ACCESSIBILITY_REQUEST_EVENT','REFUND_REQUEST','REFUND_TRANSACTION',
        'REFUND_ATTEMPT','REFUND_PAYOUT_REVISION'
    ]) AND NEW.action = 'DELETE_EVIDENCE' THEN
        RAISE EXCEPTION USING ERRCODE = '23514',
            MESSAGE = 'Reconciliation entities cannot have a physical-delete retention action';
    END IF;
    RETURN NEW;
END;
$function$;

-- ---------------------------------------------------------------------------
-- 6. Protect new history tables from physical deletion
-- ---------------------------------------------------------------------------

DO $retention_triggers_006$
DECLARE
    v_table text;
BEGIN
    FOREACH v_table IN ARRAY ARRAY[
        'order_versions', 'purchase_consents', 'ticket_change_requests',
        'trip_suggestions', 'lost_reports', 'found_items', 'lost_item_claims',
        'lost_item_handovers', 'accessibility_requests',
        'accessibility_request_events'
    ]
    LOOP
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE DELETE OR TRUNCATE ON metro.%I FOR EACH STATEMENT EXECUTE FUNCTION metro.reject_physical_history_deletion()',
            'trg_' || v_table || '_retain_history', v_table
        );
    END LOOP;
END;
$retention_triggers_006$;

-- ---------------------------------------------------------------------------
-- 7. Views
-- ---------------------------------------------------------------------------

CREATE OR REPLACE VIEW metro.v_current_trip_suggestion AS
SELECT s.*
FROM metro.trip_suggestions s
WHERE s.superseded_at IS NULL;

CREATE OR REPLACE VIEW metro.v_trip_load_status AS
SELECT pt.operator_id,
       pt.id AS planned_trip_id,
       pt.trip_code,
       pt.service_date,
       pt.boarding_status,
       lo.observed_load_kg,
       lo.observed_at,
       cp.max_load_kg,
       CASE
           WHEN lo.observed_load_kg IS NULL THEN 'NO_DATA'
           WHEN cp.max_load_kg IS NULL THEN 'NO_LIMIT'
           WHEN lo.observed_load_kg >= cp.max_load_kg THEN 'FULL'
           ELSE 'AVAILABLE'
       END AS load_status
FROM metro.planned_trips pt
LEFT JOIN LATERAL (
    SELECT o.observed_load_kg, o.observed_at
    FROM metro.load_observations o
    WHERE o.operator_id = pt.operator_id AND o.planned_trip_id = pt.id
    ORDER BY o.observed_at DESC, o.recorded_at DESC
    LIMIT 1
) lo ON true
LEFT JOIN metro.capacity_profiles cp
  ON cp.operator_id = pt.operator_id AND cp.id = pt.load_profile_id;

CREATE OR REPLACE VIEW metro.v_support_slot_availability AS
SELECT c.operator_id,
       c.id AS slot_capacity_id,
       c.station_id,
       c.support_date,
       c.slot_start,
       c.slot_end,
       c.capacity,
       c.used_count,
       (c.capacity - c.used_count) AS remaining
FROM metro.station_support_capacities c;

-- ---------------------------------------------------------------------------
-- 8. Grants and documentation
-- ---------------------------------------------------------------------------

REVOKE EXECUTE ON FUNCTION metro.issue_free_order(uuid, uuid, text, text) FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION metro.apply_ticket_change(uuid, uuid, text) FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION metro.confirm_accessibility_request(uuid, uuid, text) FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION metro.release_accessibility_capacity(uuid, uuid) FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION metro.mark_overdue_lost_items(uuid, integer) FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION metro.validate_load_profile(uuid, uuid) FROM PUBLIC;

COMMENT ON FUNCTION metro.issue_free_order(uuid, uuid, text, text)
IS 'Issues a fully discounted free ticket without creating a fake payment; blocks a second active ticket.';
COMMENT ON FUNCTION metro.apply_ticket_change(uuid, uuid, text)
IS 'Applies one approved disruption change, revokes the old QR and keeps the change history.';
COMMENT ON FUNCTION metro.confirm_accessibility_request(uuid, uuid, text)
IS 'Atomically reserves one station support slot (default two per station per 30-minute slot).';
COMMENT ON FUNCTION metro.release_accessibility_capacity(uuid, uuid)
IS 'Cancels a pending/confirmed support request and releases its slot capacity.';
COMMENT ON FUNCTION metro.mark_overdue_lost_items(uuid, integer)
IS 'Moves unclaimed found items and open lost reports to OVERDUE after the retention window.';
COMMENT ON FUNCTION metro.validate_load_profile(uuid, uuid)
IS 'Checks that the sum of car load limits does not exceed the versioned trip limit.';

COMMENT ON TABLE metro.capacity_holds IS
'LEGACY (SRS v1.1 quota model). SRS v1.2/UC v1.6 replaced holds with trip suggestions; retained for compatibility only.';
COMMENT ON TABLE metro.capacity_hold_segments IS
'LEGACY (SRS v1.1 quota model); do not use for new orders.';
COMMENT ON TABLE metro.trip_capacity_segments IS
'LEGACY per-segment quota model. SRS v1.2 uses kg load observations and no sellable quota.';
COMMENT ON TABLE metro.trip_load_profiles IS
'LEGACY passenger-count load model. SRS v1.2 records manual kg in metro.load_observations.';
COMMENT ON COLUMN metro.ticket_products.capacity_controlled IS
'LEGACY flag. New SRS v1.2 flows never hold capacity; keep false for new products.';
COMMENT ON COLUMN metro.order_items.passenger_count IS
'SRS v1.2 expects one person per order. Values above one are legacy/counter group sales and can only activate one ticket per holder.';
COMMENT ON TABLE metro.trip_suggestions IS
'DATA-020: one current trip suggestion per order version; never a hold or quota.';
COMMENT ON TABLE metro.guardian_links IS
'DATA-003: approved guardian relationship; an active link is required for a free child ticket.';
COMMENT ON TABLE metro.load_observations IS
'DATA-012: manual kg load per trip, one decimal, with actor and history.';
COMMENT ON TABLE metro.data_quality_days IS
'DATA-019: seven-day forecast source quality, confirmed by an administrator.';
