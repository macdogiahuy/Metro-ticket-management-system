-- Network, timetable, fleet, workforce, maintenance and incident domain.
CREATE TABLE metro.lines (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(40) NOT NULL, name text NOT NULL, status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('PLANNED','ACTIVE','SUSPENDED','CLOSED')),
    opened_on date, created_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    updated_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code)
);

CREATE TABLE metro.stations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(40) NOT NULL, name text NOT NULL, station_type text NOT NULL DEFAULT 'ELEVATED'
        CHECK (station_type IN ('AT_GRADE','ELEVATED','UNDERGROUND','INTERCHANGE')),
    latitude numeric(9,6), longitude numeric(9,6), status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('PLANNED','ACTIVE','SUSPENDED','CLOSED')),
    created_by_user_id text REFERENCES public."AspNetUsers" ("Id"), updated_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code),
    CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90), CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180)
);

CREATE TABLE metro.line_stations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL,
    line_id uuid NOT NULL, station_id uuid NOT NULL, direction smallint NOT NULL CHECK (direction IN (0,1)),
    sequence_no smallint NOT NULL CHECK (sequence_no > 0), distance_from_origin_m integer NOT NULL CHECK (distance_from_origin_m >= 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, line_id, direction, sequence_no), UNIQUE (operator_id, line_id, direction, station_id),
    UNIQUE (operator_id, line_id, direction, id)
);

CREATE TABLE metro.service_calendars (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(50) NOT NULL, name text NOT NULL, day_type text NOT NULL CHECK (day_type IN ('WEEKDAY','WEEKEND','HOLIDAY','EVENT','DISRUPTION')),
    days_of_week smallint[] NOT NULL DEFAULT '{}', effective_from date NOT NULL, effective_to date,
    priority smallint NOT NULL DEFAULT 0, status text NOT NULL DEFAULT 'DRAFT'
        CHECK (status IN ('DRAFT','PUBLISHED','SUPERSEDED','EXPIRED','CANCELLED')),
    created_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), CHECK (effective_to IS NULL OR effective_to >= effective_from),
    CHECK (days_of_week <@ ARRAY[1,2,3,4,5,6,7]::smallint[])
);

CREATE TABLE metro.schedule_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    line_id uuid NOT NULL, service_calendar_id uuid NOT NULL, version integer NOT NULL CHECK (version > 0),
    code varchar(60) NOT NULL, status text NOT NULL DEFAULT 'DRAFT'
        CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    effective_from timestamptz NOT NULL, effective_to timestamptz,
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    approved_at timestamptz, published_by_user_id text REFERENCES public."AspNetUsers" ("Id"), published_at timestamptz,
    decision_reason text, created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, service_calendar_id) REFERENCES metro.service_calendars (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), UNIQUE (operator_id, line_id, service_calendar_id, version),
    CHECK (effective_to IS NULL OR effective_to > effective_from),
    CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id),
    CHECK (published_by_user_id IS NULL OR published_by_user_id <> created_by_user_id),
    EXCLUDE USING gist (operator_id WITH =, line_id WITH =, tstzrange(effective_from, effective_to, '[)') WITH &&)
        WHERE (status = 'PUBLISHED')
);

CREATE TABLE metro.planned_trips (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, schedule_version_id uuid NOT NULL,
    line_id uuid NOT NULL, trip_code varchar(60) NOT NULL, service_date date NOT NULL, direction smallint NOT NULL CHECK (direction IN (0,1)),
    planned_start_at timestamptz NOT NULL, planned_end_at timestamptz NOT NULL,
    status text NOT NULL DEFAULT 'PLANNED' CHECK (status IN ('PLANNED','READY','IN_SERVICE','COMPLETED','DELAYED','CANCELLED','DISRUPTED')),
    cancellation_reason text, created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, schedule_version_id) REFERENCES metro.schedule_versions (operator_id, id),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, service_date, trip_code),
    CHECK (planned_end_at > planned_start_at), CHECK (status <> 'CANCELLED' OR nullif(btrim(cancellation_reason),'') IS NOT NULL)
);

CREATE TABLE metro.planned_stop_times (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, planned_trip_id uuid NOT NULL,
    station_id uuid NOT NULL, sequence_no smallint NOT NULL CHECK (sequence_no > 0),
    planned_arrival_at timestamptz NOT NULL, planned_departure_at timestamptz NOT NULL, dwell_seconds integer NOT NULL DEFAULT 0 CHECK (dwell_seconds >= 0),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, planned_trip_id, sequence_no), UNIQUE (operator_id, planned_trip_id, station_id),
    CHECK (planned_departure_at >= planned_arrival_at)
);

CREATE TABLE metro.actual_trips (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, planned_trip_id uuid NOT NULL,
    status text NOT NULL CHECK (status IN ('READY','IN_SERVICE','COMPLETED','DELAYED','CANCELLED','DISRUPTED')),
    actual_start_at timestamptz, actual_end_at timestamptz, source text NOT NULL CHECK (source IN ('MANUAL','OCC_IMPORT','SIMULATION')),
    source_reference text, delay_seconds integer CHECK (delay_seconds >= 0), reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, planned_trip_id), CHECK (actual_end_at IS NULL OR actual_start_at IS NULL OR actual_end_at >= actual_start_at)
);

CREATE TABLE metro.actual_stop_times (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, actual_trip_id uuid NOT NULL,
    station_id uuid NOT NULL, sequence_no smallint NOT NULL CHECK (sequence_no > 0),
    actual_arrival_at timestamptz, actual_departure_at timestamptz, source text NOT NULL CHECK (source IN ('MANUAL','OCC_IMPORT','SIMULATION')),
    FOREIGN KEY (operator_id, actual_trip_id) REFERENCES metro.actual_trips (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, actual_trip_id, sequence_no), UNIQUE (operator_id, actual_trip_id, station_id),
    CHECK (actual_departure_at IS NULL OR actual_arrival_at IS NULL OR actual_departure_at >= actual_arrival_at)
);

CREATE TABLE metro.occ_import_batches (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    file_name text NOT NULL, schema_version varchar(30) NOT NULL, checksum char(64) NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    status text NOT NULL DEFAULT 'VALIDATING' CHECK (status IN ('VALIDATING','VALIDATED','COMMITTED','REJECTED','FAILED')),
    row_count integer NOT NULL DEFAULT 0 CHECK (row_count >= 0), valid_count integer NOT NULL DEFAULT 0 CHECK (valid_count >= 0),
    error_count integer NOT NULL DEFAULT 0 CHECK (error_count >= 0), imported_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), committed_at timestamptz,
    UNIQUE (operator_id, id), UNIQUE (operator_id, checksum)
);

CREATE TABLE metro.import_row_errors (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, operator_id uuid NOT NULL, batch_id uuid NOT NULL,
    row_number integer NOT NULL CHECK (row_number > 0), field_name text, error_code varchar(60) NOT NULL, error_message text NOT NULL,
    raw_row_hash char(64) CHECK (raw_row_hash IS NULL OR raw_row_hash ~ '^[0-9a-f]{64}$'),
    FOREIGN KEY (operator_id, batch_id) REFERENCES metro.occ_import_batches (operator_id, id),
    UNIQUE (operator_id, batch_id, row_number, error_code, field_name)
);

CREATE TABLE metro.trainsets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(40) NOT NULL, depot_code varchar(40), status text NOT NULL DEFAULT 'AVAILABLE'
        CHECK (status IN ('AVAILABLE','ASSIGNED','IN_SERVICE','MAINTENANCE','OUT_OF_SERVICE')),
    status_changed_at timestamptz NOT NULL DEFAULT clock_timestamp(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(), UNIQUE (operator_id, id), UNIQUE (operator_id, code)
);

CREATE TABLE metro.cars (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, trainset_id uuid NOT NULL,
    code varchar(40) NOT NULL, sequence_no smallint NOT NULL CHECK (sequence_no > 0), car_type varchar(40),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), FOREIGN KEY (operator_id, trainset_id) REFERENCES metro.trainsets (operator_id, id),
    UNIQUE (operator_id, trainset_id, sequence_no), UNIQUE (operator_id, code)
);

CREATE TABLE metro.trip_allocations (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, planned_trip_id uuid NOT NULL, trainset_id uuid NOT NULL,
    allocated_from timestamptz NOT NULL, allocated_to timestamptz NOT NULL, status text NOT NULL DEFAULT 'ALLOCATED'
        CHECK (status IN ('ALLOCATED','RELEASED','CANCELLED','COMPLETED')),
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, trainset_id) REFERENCES metro.trainsets (operator_id, id),
    UNIQUE (operator_id, id), CHECK (allocated_to > allocated_from),
    EXCLUDE USING gist (operator_id WITH =, trainset_id WITH =, tstzrange(allocated_from, allocated_to, '[)') WITH &&)
        WHERE (status = 'ALLOCATED')
);

CREATE TABLE metro.employees (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    employee_code varchar(40) NOT NULL, user_id text REFERENCES public."AspNetUsers" ("Id"), full_name text NOT NULL,
    profession_code varchar(50) NOT NULL, home_station_id uuid, status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('ACTIVE','SUSPENDED','INACTIVE','TERMINATED')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, home_station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, employee_code), UNIQUE (operator_id, user_id)
);

CREATE TABLE metro.employee_certificates (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, employee_id uuid NOT NULL,
    certificate_type varchar(60) NOT NULL, certificate_reference_hash char(64), issued_on date NOT NULL, expires_on date,
    revoked_at timestamptz, revocation_reason text, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, employee_id) REFERENCES metro.employees (operator_id, id),
    UNIQUE (operator_id, employee_id, certificate_type, issued_on), CHECK (expires_on IS NULL OR expires_on >= issued_on),
    CHECK (revoked_at IS NULL OR nullif(btrim(revocation_reason),'') IS NOT NULL)
);

CREATE TABLE metro.shifts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(50) NOT NULL, station_id uuid, starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL,
    profession_group varchar(50) NOT NULL, status text NOT NULL DEFAULT 'PLANNED' CHECK (status IN ('PLANNED','ACTIVE','COMPLETED','CANCELLED')),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), CHECK (ends_at > starts_at)
);

CREATE TABLE metro.staff_assignments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, shift_id uuid NOT NULL, employee_id uuid NOT NULL,
    planned_trip_id uuid, station_id uuid, duty_type varchar(50) NOT NULL, status text NOT NULL DEFAULT 'ASSIGNED'
        CHECK (status IN ('ASSIGNED','ACCEPTED','IN_PROGRESS','COMPLETED','CANCELLED','REJECTED')),
    eligibility_checked_at timestamptz NOT NULL, eligibility_result boolean NOT NULL, eligibility_detail jsonb NOT NULL DEFAULT '{}',
    starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, shift_id) REFERENCES metro.shifts (operator_id, id),
    FOREIGN KEY (operator_id, employee_id) REFERENCES metro.employees (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, id), CHECK (ends_at > starts_at),
    CHECK (num_nonnulls(planned_trip_id, station_id) >= 1), CHECK (status = 'REJECTED' OR eligibility_result),
    EXCLUDE USING gist (operator_id WITH =, employee_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
        WHERE (status IN ('ASSIGNED','ACCEPTED','IN_PROGRESS'))
);

CREATE TABLE metro.maintenance_plans (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, trainset_id uuid NOT NULL,
    code varchar(50) NOT NULL, maintenance_type varchar(60) NOT NULL, interval_days integer CHECK (interval_days > 0),
    interval_km integer CHECK (interval_km > 0), effective_from date NOT NULL, effective_to date, status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('DRAFT','ACTIVE','SUSPENDED','RETIRED')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, trainset_id) REFERENCES metro.trainsets (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), CHECK (num_nonnulls(interval_days, interval_km) >= 1),
    CHECK (effective_to IS NULL OR effective_to >= effective_from)
);

CREATE TABLE metro.work_orders (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, code varchar(50) NOT NULL,
    trainset_id uuid NOT NULL, maintenance_plan_id uuid, work_type varchar(60) NOT NULL, priority text NOT NULL DEFAULT 'NORMAL'
        CHECK (priority IN ('LOW','NORMAL','HIGH','CRITICAL')),
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','ASSIGNED','IN_PROGRESS','PENDING_ACCEPTANCE','COMPLETED','CANCELLED')),
    assigned_employee_id uuid, executed_by_employee_id uuid, accepted_by_employee_id uuid,
    scheduled_from timestamptz, scheduled_to timestamptz, started_at timestamptz, submitted_at timestamptz, accepted_at timestamptz,
    cancellation_reason text, result_summary text, created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, trainset_id) REFERENCES metro.trainsets (operator_id, id),
    FOREIGN KEY (operator_id, maintenance_plan_id) REFERENCES metro.maintenance_plans (operator_id, id),
    FOREIGN KEY (operator_id, assigned_employee_id) REFERENCES metro.employees (operator_id, id),
    FOREIGN KEY (operator_id, executed_by_employee_id) REFERENCES metro.employees (operator_id, id),
    FOREIGN KEY (operator_id, accepted_by_employee_id) REFERENCES metro.employees (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code),
    CHECK (scheduled_to IS NULL OR scheduled_from IS NULL OR scheduled_to > scheduled_from),
    CHECK (accepted_by_employee_id IS NULL OR executed_by_employee_id IS NULL OR accepted_by_employee_id <> executed_by_employee_id),
    CHECK (status <> 'CANCELLED' OR nullif(btrim(cancellation_reason),'') IS NOT NULL)
);

CREATE TABLE metro.incidents (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(50) NOT NULL, incident_type varchar(60) NOT NULL, severity text NOT NULL CHECK (severity IN ('LOW','MEDIUM','HIGH','CRITICAL')),
    status text NOT NULL DEFAULT 'REPORTED' CHECK (status IN ('REPORTED','ASSESSED','IN_RESPONSE','RECOVERED','UNDER_REVIEW','CLOSED','REJECTED')),
    station_id uuid, line_id uuid, occurred_at timestamptz NOT NULL, recovered_at timestamptz, closed_at timestamptz,
    summary text NOT NULL, impact_summary text, recovery_summary text, root_cause text,
    reported_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), closed_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code),
    CHECK (recovered_at IS NULL OR recovered_at >= occurred_at), CHECK (closed_at IS NULL OR recovered_at IS NOT NULL),
    CHECK (status <> 'CLOSED' OR (closed_at IS NOT NULL AND nullif(btrim(recovery_summary),'') IS NOT NULL))
);

CREATE TABLE metro.incident_trips (
    operator_id uuid NOT NULL, incident_id uuid NOT NULL, planned_trip_id uuid NOT NULL, impact_type varchar(40) NOT NULL,
    PRIMARY KEY (operator_id, incident_id, planned_trip_id),
    FOREIGN KEY (operator_id, incident_id) REFERENCES metro.incidents (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id)
);

CREATE TABLE metro.incident_timeline (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, incident_id uuid NOT NULL,
    event_at timestamptz NOT NULL, actor_user_id text REFERENCES public."AspNetUsers" ("Id"), action varchar(80) NOT NULL,
    notes text, attachment_reference text, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, incident_id) REFERENCES metro.incidents (operator_id, id), UNIQUE (operator_id, id)
);

CREATE TABLE metro.service_notices (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    incident_id uuid, code varchar(50) NOT NULL, audience text NOT NULL, title text NOT NULL, content text NOT NULL,
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','EXPIRED','REJECTED','CANCELLED')),
    emergency_template boolean NOT NULL DEFAULT false, valid_from timestamptz NOT NULL, valid_to timestamptz,
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    published_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_at timestamptz, published_at timestamptz, decision_reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, incident_id) REFERENCES metro.incidents (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), CHECK (valid_to IS NULL OR valid_to > valid_from),
    CHECK (emergency_template OR approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id)
);

CREATE INDEX ix_line_stations_station ON metro.line_stations (operator_id, station_id);
CREATE INDEX ix_planned_trips_service ON metro.planned_trips (operator_id, line_id, service_date, direction, planned_start_at);
CREATE INDEX ix_planned_stop_times_station ON metro.planned_stop_times (operator_id, station_id, planned_arrival_at);
CREATE INDEX ix_actual_trips_status ON metro.actual_trips (operator_id, status, actual_start_at);
CREATE INDEX ix_allocations_trip ON metro.trip_allocations (operator_id, planned_trip_id);
CREATE INDEX ix_employee_status ON metro.employees (operator_id, status, profession_code);
CREATE INDEX ix_work_orders_trainset ON metro.work_orders (operator_id, trainset_id, status, scheduled_from);
CREATE INDEX ix_incidents_status ON metro.incidents (operator_id, status, occurred_at DESC);
CREATE INDEX ix_service_notices_active ON metro.service_notices (operator_id, status, valid_from, valid_to);
