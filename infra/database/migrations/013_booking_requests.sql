-- BE-07: save a successful response in the same transaction as its booking mutation.
-- BE-08 keeps verified provider outcomes; BE-07 never treats an HTTP failure as no charge.
ALTER TABLE metro.payments ADD COLUMN attempt_number integer CHECK (attempt_number BETWEEN 1 AND 3),
    ADD COLUMN checkout_url text;
CREATE UNIQUE INDEX uq_booking_payment_attempt ON metro.payments(operator_id,order_id,attempt_number)
    WHERE attempt_number IS NOT NULL;

CREATE TABLE metro.booking_requests (
    operator_id uuid NOT NULL REFERENCES metro.operators (id),
    actor_user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id"),
    operation varchar(24) NOT NULL,
    request_key_hash char(64) NOT NULL,
    payload_hash char(64) NOT NULL,
    response jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (operator_id, actor_user_id, operation, request_key_hash)
);

CREATE INDEX ix_booking_suggestion_orders ON metro.orders (service_date, id)
    WHERE status IN ('PAID','PENDING_ISSUE','ISSUED') AND service_date IS NOT NULL;

-- A schedule search may legitimately have no published timetable or trip.
ALTER TABLE metro.trip_suggestions DROP CONSTRAINT trip_suggestions_check3;
ALTER TABLE metro.trip_suggestions ADD CONSTRAINT ck_suggestion_schedule_source CHECK (
    source <> 'SCHEDULE' OR status IN ('NO_TRIP','FAILED') OR schedule_version_id IS NOT NULL
    OR suggested_planned_trip_id IS NOT NULL
);
