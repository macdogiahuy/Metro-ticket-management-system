-- BE-10. Receipts include rejected/unknown QR requests without storing QR tokens or invented ticket IDs.
CREATE TABLE metro.gate_requests (
    operator_id uuid NOT NULL REFERENCES metro.operators(id), request_id uuid NOT NULL,
    actor_user_id text NOT NULL REFERENCES public."AspNetUsers"("Id"),
    payload_hash char(64) NOT NULL CHECK (payload_hash ~ '^[0-9a-f]{64}$'),
    kind text NOT NULL CHECK (kind IN ('SCAN','RESOLUTION')),
    response jsonb NOT NULL CHECK (jsonb_typeof(response)='object'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY(operator_id,request_id)
);
CREATE TABLE metro.gate_changes (
    change_no bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    operator_id uuid NOT NULL, request_id uuid NOT NULL, ticket_id uuid NOT NULL, journey_id uuid NOT NULL,
    action text NOT NULL CHECK (action IN ('ENTRY','EXIT','MANUAL_ENTRY','MANUAL_EXIT')),
    before_state jsonb NOT NULL, after_state jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE(operator_id,change_no),
    FOREIGN KEY(operator_id,request_id) REFERENCES metro.gate_requests(operator_id,request_id) DEFERRABLE INITIALLY DEFERRED,
    FOREIGN KEY(operator_id,ticket_id) REFERENCES metro.tickets(operator_id,id),
    FOREIGN KEY(operator_id,journey_id) REFERENCES metro.journeys(operator_id,id)
);
CREATE INDEX ix_gate_changes_ticket ON metro.gate_changes(operator_id,ticket_id,change_no DESC);
CREATE TABLE metro.gate_resolutions (
    operator_id uuid NOT NULL, request_id uuid NOT NULL, source_request_id uuid NOT NULL,
    action text NOT NULL CHECK (action IN ('CLOSE_JOURNEY','SUPPLY_ENTRY_AND_EXIT','UNDO_SCAN','ESCALATE')),
    reason text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 1000),
    evidence_reference text NOT NULL CHECK (length(btrim(evidence_reference)) BETWEEN 1 AND 1000),
    identity_verified boolean NOT NULL, passenger_has_not_travelled boolean NOT NULL,
    PRIMARY KEY(operator_id,request_id),
    FOREIGN KEY(operator_id,request_id) REFERENCES metro.gate_requests(operator_id,request_id) DEFERRABLE INITIALLY DEFERRED,
    FOREIGN KEY(operator_id,source_request_id) REFERENCES metro.gate_requests(operator_id,request_id),
    CHECK(action='ESCALATE' OR identity_verified),
    CHECK(action<>'UNDO_SCAN' OR passenger_has_not_travelled)
);
CREATE TABLE metro.gate_undo_authorizations (
    operator_id uuid NOT NULL, source_change_no bigint NOT NULL, request_id uuid NOT NULL, ticket_id uuid NOT NULL,
    target_status text NOT NULL CHECK(target_status IN ('ACTIVE','IN_USE')), transaction_id text NOT NULL,
    PRIMARY KEY(operator_id,source_change_no),
    FOREIGN KEY(operator_id,source_change_no) REFERENCES metro.gate_changes(operator_id,change_no),
    FOREIGN KEY(operator_id,request_id) REFERENCES metro.gate_resolutions(operator_id,request_id) DEFERRABLE INITIALLY DEFERRED,
    FOREIGN KEY(operator_id,ticket_id) REFERENCES metro.tickets(operator_id,id)
);
-- A valid child/guardian entry inserts both journeys in one transaction with mutual links.
ALTER TABLE metro.journeys ALTER CONSTRAINT fk_journeys_pair DEFERRABLE INITIALLY DEFERRED;

CREATE FUNCTION metro.reject_gate_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN RAISE EXCEPTION USING ERRCODE='P0001', MESSAGE='Gate history is immutable'; END $f$;
CREATE TRIGGER trg_gate_requests_immutable BEFORE UPDATE OR DELETE ON metro.gate_requests
 FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_gate_changes_immutable BEFORE UPDATE OR DELETE ON metro.gate_changes
 FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_gate_resolutions_immutable BEFORE UPDATE OR DELETE ON metro.gate_resolutions
 FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_gate_undo_immutable BEFORE UPDATE OR DELETE ON metro.gate_undo_authorizations
 FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_afc_events_immutable BEFORE UPDATE ON metro.afc_events
 FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();

-- Preserve the existing order/payment/ticket transition rules verbatim, adding only guarded gate transitions.
DO $gate_workflow$
DECLARE definition text;
BEGIN
 SELECT pg_get_functiondef('metro.enforce_workflow_state_transition()'::regprocedure) INTO definition;
 IF position('IF OLD.status = NEW.status THEN' IN definition)=0 THEN
   RAISE EXCEPTION 'Unexpected workflow baseline; review the gate transition extension';
 END IF;
 definition := replace(definition,'IF OLD.status = NEW.status THEN',$branch$
 IF TG_TABLE_NAME='tickets' AND OLD.status<>NEW.status THEN
   -- A pass returns to ACTIVE after its current journey closes; its expiry is never extended.
   IF OLD.status='IN_USE' AND NEW.status='ACTIVE'
      AND EXISTS(SELECT 1 FROM metro.ticket_products p WHERE p.operator_id=NEW.operator_id AND p.id=NEW.ticket_product_id
                 AND p.product_type IN ('DAY','WEEK','MONTH'))
      AND EXISTS(SELECT 1 FROM metro.journeys j WHERE j.operator_id=NEW.operator_id AND j.ticket_id=NEW.id AND j.status='COMPLETED')
      AND NOT EXISTS(SELECT 1 FROM metro.journeys j WHERE j.operator_id=NEW.operator_id AND j.ticket_id=NEW.id AND j.status IN ('OPEN','MANUAL_REVIEW'))
   THEN RETURN NEW; END IF;
   -- Undo is possible only with immutable evidence and an authorization created in this very transaction.
   IF EXISTS(SELECT 1 FROM metro.gate_undo_authorizations a JOIN metro.gate_changes c
      ON c.operator_id=a.operator_id AND c.change_no=a.source_change_no
      WHERE a.operator_id=NEW.operator_id AND a.ticket_id=NEW.id AND c.ticket_id=NEW.id
        AND a.transaction_id=pg_current_xact_id()::text AND a.target_status=NEW.status
        AND c.before_state->'ticket'->>'status'=NEW.status AND c.after_state->'ticket'->>'status'=OLD.status
        AND NOT EXISTS(SELECT 1 FROM metro.gate_changes later WHERE later.operator_id=c.operator_id AND later.ticket_id=c.ticket_id AND later.change_no>c.change_no))
   THEN RETURN NEW; END IF;
 END IF;
 IF OLD.status = NEW.status THEN
$branch$);
 EXECUTE definition;
END $gate_workflow$;

-- Deterministic choice when multiple observations have the same timestamps. No automatic load expiry.
CREATE OR REPLACE VIEW metro.v_trip_load_status AS
SELECT pt.operator_id,pt.id AS planned_trip_id,pt.trip_code,pt.service_date,pt.boarding_status,
 lo.observed_load_kg,lo.observed_at,cp.max_load_kg,
 CASE WHEN lo.observed_load_kg IS NULL THEN 'NO_DATA' WHEN cp.max_load_kg IS NULL THEN 'NO_LIMIT'
      WHEN lo.observed_load_kg>=cp.max_load_kg THEN 'FULL' ELSE 'AVAILABLE' END AS load_status
FROM metro.planned_trips pt
LEFT JOIN LATERAL (SELECT o.observed_load_kg,o.observed_at FROM metro.load_observations o
 WHERE o.operator_id=pt.operator_id AND o.planned_trip_id=pt.id ORDER BY o.observed_at DESC,o.recorded_at DESC,o.id DESC LIMIT 1) lo ON true
LEFT JOIN metro.capacity_profiles cp ON cp.operator_id=pt.operator_id AND cp.id=pt.load_profile_id;

COMMENT ON TABLE metro.gate_requests IS 'BE-10 atomic scan receipts. Pending means unconfirmed, not a stored REVIEW decision. Never store raw QR tokens.';
