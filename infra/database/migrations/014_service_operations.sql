-- BE-14: responses and service changes commit atomically; no capacity holds.
CREATE TABLE metro.operations_requests (
    operator_id uuid NOT NULL REFERENCES metro.operators(id),
    actor_user_id text NOT NULL REFERENCES public."AspNetUsers"("Id"),
    operation varchar(40) NOT NULL,
    request_key_hash char(64) NOT NULL,
    payload_hash char(64) NOT NULL,
    response jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(operator_id,actor_user_id,operation,request_key_hash)
);

-- Versioned cache namespaces prevent an in-flight old read from re-caching stale service data.
CREATE TABLE metro.operation_revisions (
    operator_id uuid PRIMARY KEY REFERENCES metro.operators(id),
    revision bigint NOT NULL DEFAULT 0 CHECK(revision >= 0)
);

CREATE TABLE metro.service_disruptions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id uuid NOT NULL REFERENCES metro.operators(id),
    line_id uuid, station_id uuid, planned_trip_id uuid,
    effect text NOT NULL CHECK(effect IN ('DELAYED','SUSPENDED','CLOSED')),
    status text NOT NULL DEFAULT 'DRAFT' CHECK(status IN ('DRAFT','PUBLISHED','RESOLVED')),
    title text NOT NULL, description text NOT NULL,
    occurred_at timestamptz NOT NULL,
    published_at timestamptz, resolved_at timestamptz,
    previous_status text, applied_status text, previous_boarding_status text,
    created_by_user_id text NOT NULL REFERENCES public."AspNetUsers"("Id"),
    updated_by_user_id text NOT NULL REFERENCES public."AspNetUsers"("Id"),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE(operator_id,id),
    FOREIGN KEY(operator_id,line_id) REFERENCES metro.lines(operator_id,id),
    FOREIGN KEY(operator_id,station_id) REFERENCES metro.stations(operator_id,id),
    FOREIGN KEY(operator_id,planned_trip_id) REFERENCES metro.planned_trips(operator_id,id),
    CHECK(num_nonnulls(line_id,station_id,planned_trip_id)=1),
    CHECK((station_id IS NOT NULL AND effect='CLOSED') OR (station_id IS NULL AND effect IN ('DELAYED','SUSPENDED'))),
    CHECK((status='DRAFT' AND published_at IS NULL AND resolved_at IS NULL) OR
          (status='PUBLISHED' AND published_at IS NOT NULL AND resolved_at IS NULL) OR
          (status='RESOLVED' AND published_at IS NOT NULL AND resolved_at>=published_at))
);
CREATE INDEX ix_service_disruptions_active ON metro.service_disruptions(operator_id,status);

-- A stable final tie-breaker agrees with the API's load reader for concurrent observations.
CREATE OR REPLACE VIEW metro.v_trip_load_status AS
SELECT pt.operator_id, pt.id AS planned_trip_id, pt.trip_code, pt.service_date, pt.boarding_status,
       lo.observed_load_kg, lo.observed_at, cp.max_load_kg,
       CASE WHEN lo.observed_load_kg IS NULL THEN 'NO_DATA'
            WHEN cp.max_load_kg IS NULL THEN 'NO_LIMIT'
            WHEN lo.observed_load_kg >= cp.max_load_kg THEN 'FULL'
            ELSE 'AVAILABLE' END AS load_status
FROM metro.planned_trips pt
LEFT JOIN LATERAL (
    SELECT o.observed_load_kg,o.observed_at FROM metro.load_observations o
    WHERE o.operator_id=pt.operator_id AND o.planned_trip_id=pt.id
    ORDER BY o.observed_at DESC,o.recorded_at DESC,o.id DESC LIMIT 1
) lo ON true
LEFT JOIN metro.capacity_profiles cp ON cp.operator_id=pt.operator_id AND cp.id=pt.load_profile_id;
