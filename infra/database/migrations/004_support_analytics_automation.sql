-- Support, notification, reporting, OD/crowding analytics and controlled automation.
CREATE TABLE metro.support_requests (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    request_code varchar(60) NOT NULL, submitted_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    request_type varchar(60) NOT NULL, related_entity_type varchar(60), related_entity_id text,
    priority text NOT NULL DEFAULT 'NORMAL' CHECK (priority IN ('LOW','NORMAL','HIGH','URGENT')),
    status text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','ASSIGNED','WAITING_CUSTOMER','RESOLVED','CLOSED','CANCELLED')),
    subject text NOT NULL, description text NOT NULL, assigned_to_user_id text REFERENCES public."AspNetUsers" ("Id"),
    resolved_at timestamptz, closed_at timestamptz, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, request_code), UNIQUE (operator_id, correlation_id)
);

CREATE TABLE metro.support_messages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, support_request_id uuid NOT NULL,
    author_user_id text REFERENCES public."AspNetUsers" ("Id"), author_type text NOT NULL CHECK (author_type IN ('CUSTOMER','STAFF','SYSTEM')),
    body text NOT NULL, attachment_reference text, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, support_request_id) REFERENCES metro.support_requests (operator_id, id)
);

CREATE TABLE metro.notifications (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid REFERENCES metro.operators (id),
    recipient_user_id text REFERENCES public."AspNetUsers" ("Id"), notification_type varchar(60) NOT NULL,
    channel text NOT NULL CHECK (channel IN ('IN_APP','EMAIL_MOCK')), template_code varchar(60), template_version integer,
    content_reference jsonb NOT NULL DEFAULT '{}', mandatory boolean NOT NULL DEFAULT false,
    status text NOT NULL DEFAULT 'QUEUED' CHECK (status IN ('QUEUED','SENT','FAILED','CANCELLED','READ')),
    attempts smallint NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 10), queued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    sent_at timestamptz, read_at timestamptz, last_error text, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, correlation_id)
);

CREATE TABLE metro.report_jobs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    job_code varchar(60) NOT NULL, report_type varchar(80) NOT NULL, template_version integer NOT NULL CHECK (template_version > 0),
    filters_snapshot jsonb NOT NULL DEFAULT '{}', timezone text NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
    requested_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), status text NOT NULL DEFAULT 'QUEUED'
        CHECK (status IN ('QUEUED','RUNNING','SUCCEEDED','FAILED','CANCELLED','EXPIRED')),
    requested_at timestamptz NOT NULL DEFAULT clock_timestamp(), started_at timestamptz, completed_at timestamptz, error_summary text,
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), UNIQUE (operator_id, id), UNIQUE (operator_id, job_code), UNIQUE (operator_id, correlation_id)
);

CREATE TABLE metro.export_artifacts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, report_job_id uuid NOT NULL,
    format text NOT NULL CHECK (format IN ('PDF','CSV','XLSX','JSON')), storage_reference text NOT NULL,
    checksum char(64) NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'), masked boolean NOT NULL DEFAULT true,
    metadata jsonb NOT NULL DEFAULT '{}', created_at timestamptz NOT NULL DEFAULT clock_timestamp(), expires_at timestamptz,
    FOREIGN KEY (operator_id, report_job_id) REFERENCES metro.report_jobs (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, checksum), CHECK (expires_at IS NULL OR expires_at > created_at)
);

CREATE TABLE metro.od_matrix_versions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(70) NOT NULL, line_id uuid NOT NULL, direction smallint NOT NULL CHECK (direction IN (0,1)),
    service_date date NOT NULL, time_band tstzrange NOT NULL, algorithm_version varchar(60) NOT NULL,
    source_type text NOT NULL CHECK (source_type IN ('MEASURED','IMPUTED','SIMULATED','FORECAST','MIXED')),
    quality_score numeric(6,3) NOT NULL CHECK (quality_score BETWEEN 0 AND 100), completeness_ratio numeric(7,6) NOT NULL CHECK (completeness_ratio BETWEEN 0 AND 1),
    imputed_ratio numeric(7,6) NOT NULL DEFAULT 0 CHECK (imputed_ratio BETWEEN 0 AND 1),
    checksum char(64) NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'), status text NOT NULL DEFAULT 'DRAFT'
        CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    published_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_at timestamptz, published_at timestamptz, exception_reason text,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code),
    UNIQUE (operator_id, line_id, direction, service_date, time_band, algorithm_version),
    CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id),
    CHECK (published_by_user_id IS NULL OR published_by_user_id <> created_by_user_id)
);

CREATE TABLE metro.od_matrix_cells (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, od_matrix_version_id uuid NOT NULL,
    origin_station_id uuid NOT NULL, destination_station_id uuid NOT NULL, trip_count integer NOT NULL CHECK (trip_count >= 0),
    imputed_ratio numeric(7,6) NOT NULL DEFAULT 0 CHECK (imputed_ratio BETWEEN 0 AND 1),
    source_mix jsonb NOT NULL DEFAULT '{}', quality_flag text NOT NULL CHECK (quality_flag IN ('GOOD','WARNING','POOR','SUPPRESSED')),
    suppressed boolean NOT NULL DEFAULT false,
    FOREIGN KEY (operator_id, od_matrix_version_id) REFERENCES metro.od_matrix_versions (operator_id, id),
    FOREIGN KEY (operator_id, origin_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, destination_station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, od_matrix_version_id, origin_station_id, destination_station_id),
    CHECK (origin_station_id <> destination_station_id), CHECK (NOT suppressed OR trip_count < 10)
);

CREATE TABLE metro.apc_import_batches (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    source_code varchar(60) NOT NULL, checksum char(64) NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'), schema_version varchar(30) NOT NULL,
    status text NOT NULL DEFAULT 'VALIDATING' CHECK (status IN ('VALIDATING','VALIDATED','COMMITTED','REJECTED','FAILED')),
    row_count integer NOT NULL DEFAULT 0 CHECK (row_count >= 0), error_count integer NOT NULL DEFAULT 0 CHECK (error_count >= 0),
    imported_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    committed_at timestamptz, UNIQUE (operator_id, id), UNIQUE (operator_id, source_code, checksum)
);

CREATE TABLE metro.apc_counts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, batch_id uuid NOT NULL,
    planned_trip_id uuid, station_id uuid, from_station_id uuid, to_station_id uuid,
    window_start timestamptz NOT NULL, window_end timestamptz NOT NULL, passenger_count integer NOT NULL CHECK (passenger_count >= 0),
    device_code varchar(80) NOT NULL, source_event_key varchar(120) NOT NULL, confidence numeric(6,5) NOT NULL CHECK (confidence BETWEEN 0 AND 1),
    quality_flag text NOT NULL DEFAULT 'UNVERIFIED' CHECK (quality_flag IN ('UNVERIFIED','MATCHED','MISMATCHED','REJECTED')),
    afc_variance integer, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, batch_id) REFERENCES metro.apc_import_batches (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, from_station_id) REFERENCES metro.stations (operator_id, id),
    FOREIGN KEY (operator_id, to_station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, source_event_key), CHECK (window_end > window_start),
    CHECK (station_id IS NOT NULL OR (from_station_id IS NOT NULL AND to_station_id IS NOT NULL AND from_station_id <> to_station_id))
);

CREATE TABLE metro.trip_load_profiles (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, od_matrix_version_id uuid NOT NULL,
    planned_trip_id uuid NOT NULL, capacity_profile_id uuid NOT NULL, from_sequence smallint NOT NULL CHECK (from_sequence > 0),
    to_sequence smallint NOT NULL CHECK (to_sequence = from_sequence + 1), onboard_count integer NOT NULL CHECK (onboard_count >= 0),
    load_factor numeric(8,5) NOT NULL CHECK (load_factor >= 0), passengers_per_square_metre numeric(8,4) CHECK (passengers_per_square_metre >= 0),
    source_freshness_seconds integer NOT NULL CHECK (source_freshness_seconds >= 0), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, od_matrix_version_id) REFERENCES metro.od_matrix_versions (operator_id, id),
    FOREIGN KEY (operator_id, planned_trip_id) REFERENCES metro.planned_trips (operator_id, id),
    FOREIGN KEY (operator_id, capacity_profile_id) REFERENCES metro.capacity_profiles (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, od_matrix_version_id, planned_trip_id, from_sequence)
);

CREATE TABLE metro.crowding_thresholds (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(60) NOT NULL, version integer NOT NULL CHECK (version > 0), line_id uuid, station_id uuid,
    time_band tstzrange, level text NOT NULL CHECK (level IN ('NORMAL','BUSY','CROWDED','SEVERE')),
    load_factor_threshold numeric(8,5), psm_threshold numeric(8,4), status text NOT NULL DEFAULT 'DRAFT'
        CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','PUBLISHED','SUPERSEDED','EXPIRED','REJECTED','CANCELLED')),
    effective_from timestamptz NOT NULL, effective_to timestamptz, created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"), approved_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, line_id) REFERENCES metro.lines (operator_id, id),
    FOREIGN KEY (operator_id, station_id) REFERENCES metro.stations (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code, version), CHECK (num_nonnulls(line_id, station_id) >= 1),
    CHECK (num_nonnulls(load_factor_threshold, psm_threshold) >= 1), CHECK (effective_to IS NULL OR effective_to > effective_from),
    CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id)
);

CREATE TABLE metro.crowding_alerts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, alert_code varchar(70) NOT NULL,
    trip_load_profile_id uuid NOT NULL, crowding_threshold_id uuid NOT NULL,
    severity text NOT NULL CHECK (severity IN ('INFO','WARNING','HIGH','CRITICAL')),
    confidence numeric(6,5) NOT NULL CHECK (confidence BETWEEN 0 AND 1), data_freshness_seconds integer NOT NULL CHECK (data_freshness_seconds >= 0),
    status text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','ACKNOWLEDGED','ASSIGNED','RESOLVED','CLOSED','FALSE_POSITIVE')),
    assigned_to_user_id text REFERENCES public."AspNetUsers" ("Id"), acknowledged_at timestamptz, closed_at timestamptz,
    action_taken text, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, trip_load_profile_id) REFERENCES metro.trip_load_profiles (operator_id, id),
    FOREIGN KEY (operator_id, crowding_threshold_id) REFERENCES metro.crowding_thresholds (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, alert_code), UNIQUE (operator_id, correlation_id)
);

CREATE TABLE metro.demand_forecasts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, od_matrix_version_id uuid NOT NULL,
    code varchar(70) NOT NULL, horizon interval NOT NULL, model_version varchar(60) NOT NULL,
    forecast_start timestamptz NOT NULL, forecast_end timestamptz NOT NULL,
    point_estimate numeric(16,4) NOT NULL CHECK (point_estimate >= 0), lower_bound numeric(16,4) NOT NULL CHECK (lower_bound >= 0),
    upper_bound numeric(16,4) NOT NULL CHECK (upper_bound >= lower_bound), backtest_metrics jsonb NOT NULL DEFAULT '{}',
    scenario boolean NOT NULL DEFAULT false, scenario_assumptions jsonb NOT NULL DEFAULT '{}', created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, od_matrix_version_id) REFERENCES metro.od_matrix_versions (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code), CHECK (forecast_end > forecast_start)
);

CREATE TABLE metro.automation_rules (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL REFERENCES metro.operators (id),
    code varchar(60) NOT NULL, version integer NOT NULL CHECK (version > 0), name text NOT NULL,
    owner_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), domain varchar(40) NOT NULL,
    scope jsonb NOT NULL DEFAULT '{}', trigger_type text NOT NULL CHECK (trigger_type IN ('SCHEDULE','EVENT','MANUAL')),
    trigger_definition jsonb NOT NULL DEFAULT '{}', action_type varchar(80) NOT NULL,
    risk_level text NOT NULL CHECK (risk_level IN ('LOW','MEDIUM','HIGH','CRITICAL')),
    approval_mode text NOT NULL CHECK (approval_mode IN ('AUTO_TASK','HUMAN_CONFIRM','HUMAN_APPROVE')),
    status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','SHADOW','PENDING_APPROVAL','ACTIVE','PAUSED','RETIRED','REJECTED')),
    kill_switch boolean NOT NULL DEFAULT false, quality_thresholds jsonb NOT NULL DEFAULT '{}',
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), approved_by_user_id text REFERENCES public."AspNetUsers" ("Id"),
    approved_at timestamptz, created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (operator_id, id), UNIQUE (operator_id, code, version),
    CHECK (approved_by_user_id IS NULL OR approved_by_user_id <> created_by_user_id),
    CHECK (action_type !~* '(ATP|ATO|ATS|SCADA|INTERLOCK|SIGNAL|OCC_COMMAND)')
);

CREATE TABLE metro.automation_runs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, automation_rule_id uuid NOT NULL,
    od_matrix_version_id uuid, run_code varchar(80) NOT NULL, input_hash char(64) NOT NULL CHECK (input_hash ~ '^[0-9a-f]{64}$'),
    input_reference jsonb NOT NULL DEFAULT '{}', output_payload jsonb, explanation jsonb NOT NULL DEFAULT '{}',
    confidence numeric(6,5) CHECK (confidence BETWEEN 0 AND 1), status text NOT NULL DEFAULT 'QUEUED'
        CHECK (status IN ('QUEUED','RUNNING','SUCCEEDED','FAILED','TIMED_OUT','MANUAL_REVIEW','CANCELLED')),
    human_decision text CHECK (human_decision IN ('APPROVED','REJECTED','NEEDS_INFO','OVERRIDDEN')),
    decided_by_user_id text REFERENCES public."AspNetUsers" ("Id"), decision_reason text,
    started_at timestamptz, completed_at timestamptz, duration_ms integer CHECK (duration_ms >= 0),
    correlation_id uuid NOT NULL DEFAULT gen_random_uuid(), created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, automation_rule_id) REFERENCES metro.automation_rules (operator_id, id),
    FOREIGN KEY (operator_id, od_matrix_version_id) REFERENCES metro.od_matrix_versions (operator_id, id),
    UNIQUE (operator_id, id), UNIQUE (operator_id, run_code), UNIQUE (operator_id, correlation_id),
    CHECK (human_decision IS NULL OR (decided_by_user_id IS NOT NULL AND nullif(btrim(decision_reason),'') IS NOT NULL))
);

CREATE TABLE metro.automation_overrides (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, automation_run_id uuid NOT NULL,
    actor_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"), reason text NOT NULL,
    scope jsonb NOT NULL DEFAULT '{}', before_value jsonb, after_value jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, automation_run_id) REFERENCES metro.automation_runs (operator_id, id)
);

CREATE TABLE metro.automation_work_queue (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(), operator_id uuid NOT NULL, automation_run_id uuid,
    domain varchar(40) NOT NULL, required_role_code varchar(60), priority text NOT NULL DEFAULT 'NORMAL'
        CHECK (priority IN ('LOW','NORMAL','HIGH','URGENT')),
    status text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','ASSIGNED','IN_REVIEW','COMPLETED','REJECTED','EXPIRED','CANCELLED')),
    sla_due_at timestamptz NOT NULL, assigned_to_user_id text REFERENCES public."AspNetUsers" ("Id"),
    manual_fallback boolean NOT NULL DEFAULT false, result_summary text, correlation_id uuid NOT NULL DEFAULT gen_random_uuid(),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(), updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (operator_id, automation_run_id) REFERENCES metro.automation_runs (operator_id, id),
    FOREIGN KEY (required_role_code) REFERENCES metro.business_roles (code),
    UNIQUE (operator_id, id), UNIQUE (operator_id, correlation_id)
);

CREATE INDEX ix_support_queue ON metro.support_requests (operator_id, status, priority, created_at);
CREATE INDEX ix_notifications_delivery ON metro.notifications (status, queued_at) WHERE status IN ('QUEUED','FAILED');
CREATE INDEX ix_report_jobs_queue ON metro.report_jobs (operator_id, status, requested_at);
CREATE INDEX ix_od_lookup ON metro.od_matrix_versions (operator_id, line_id, service_date, direction, status);
CREATE INDEX ix_od_cells_origin ON metro.od_matrix_cells (operator_id, od_matrix_version_id, origin_station_id);
CREATE INDEX ix_apc_trip_window ON metro.apc_counts (operator_id, planned_trip_id, window_start);
CREATE INDEX ix_load_trip ON metro.trip_load_profiles (operator_id, planned_trip_id, from_sequence);
CREATE INDEX ix_crowding_open ON metro.crowding_alerts (operator_id, status, severity, created_at DESC);
CREATE INDEX ix_automation_run_status ON metro.automation_runs (operator_id, status, created_at DESC);
CREATE INDEX ix_automation_queue_sla ON metro.automation_work_queue (operator_id, status, sla_due_at);
