-- BE-09: durable QR readiness; existing paid/free SQL entry points are preserved.
-- Metro travel rights select stations, not a reserved planned train.
ALTER TABLE metro.order_items DROP CONSTRAINT order_items_check2;
ALTER TABLE metro.order_items ADD CONSTRAINT ck_order_item_metro_route CHECK (
 (planned_trip_id IS NULL AND origin_station_id IS NULL AND destination_station_id IS NULL)
 OR (origin_station_id IS NOT NULL AND destination_station_id IS NOT NULL AND origin_station_id<>destination_station_id));
ALTER TABLE metro.payments ADD COLUMN order_version integer;
UPDATE metro.payments p SET order_version=o.order_version FROM metro.orders o
WHERE o.operator_id=p.operator_id AND o.id=p.order_id;
ALTER TABLE metro.payments ADD CONSTRAINT fk_payment_order_version FOREIGN KEY(operator_id,order_id,order_version)
REFERENCES metro.order_versions(operator_id,order_id,version);
-- Keep historical callers/inserts compatible while freezing the version at payment creation.
CREATE FUNCTION metro.fill_payment_version() RETURNS trigger LANGUAGE plpgsql SET search_path=metro,public,pg_catalog AS $f$
BEGIN
 IF TG_OP='INSERT' THEN
  SELECT COALESCE(NEW.order_version,order_version) INTO NEW.order_version FROM metro.orders WHERE operator_id=NEW.operator_id AND id=NEW.order_id;
 ELSIF NEW.order_version IS DISTINCT FROM OLD.order_version OR NEW.order_id<>OLD.order_id OR NEW.operator_id<>OLD.operator_id THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='PAYMENT_VERSION_IMMUTABLE';
 END IF;
 RETURN NEW;
END $f$;
CREATE TRIGGER trg_payment_version BEFORE INSERT OR UPDATE ON metro.payments FOR EACH ROW EXECUTE FUNCTION metro.fill_payment_version();
-- A frozen payment version must never silently acquire different passenger/price data.
CREATE FUNCTION metro.freeze_confirmed_order_version() RETURNS trigger LANGUAGE plpgsql AS $f$
BEGIN
 IF OLD.status IN ('CONFIRMED','SUPERSEDED') AND
  (to_jsonb(OLD)-ARRAY['status','change_reason']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['status','change_reason']) THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='CONFIRMED_VERSION_IMMUTABLE';
 END IF;
 RETURN NEW;
END $f$;
CREATE TRIGGER trg_freeze_confirmed_order_version BEFORE UPDATE ON metro.order_versions
FOR EACH ROW EXECUTE FUNCTION metro.freeze_confirmed_order_version();
ALTER TABLE metro.ticket_credentials ADD COLUMN version integer NOT NULL DEFAULT 1 CHECK(version>0), ADD COLUMN key_id text;
CREATE UNIQUE INDEX uq_signed_qr_version ON metro.ticket_credentials(operator_id,ticket_id,version) WHERE credential_type='QR' AND key_id IS NOT NULL;
CREATE UNIQUE INDEX uq_current_signed_qr ON metro.ticket_credentials(operator_id,ticket_id) WHERE credential_type='QR' AND status='ACTIVE' AND key_id IS NOT NULL;
CREATE TABLE metro.ticket_issuance_jobs (
 ticket_id uuid PRIMARY KEY, operator_id uuid NOT NULL, order_id uuid NOT NULL,
 due_at timestamptz NOT NULL, next_attempt_at timestamptz NOT NULL,
 status text NOT NULL DEFAULT 'PENDING' CHECK(status IN ('PENDING','READY','NEEDS_SUPPORT','CANCELLED')),
 first_failed_at timestamptz, last_error_code text, completed_at timestamptz,
 FOREIGN KEY(operator_id,ticket_id) REFERENCES metro.tickets(operator_id,id),
 FOREIGN KEY(operator_id,order_id) REFERENCES metro.orders(operator_id,id)
);
CREATE INDEX ix_issuance_due ON metro.ticket_issuance_jobs(next_attempt_at) WHERE status='PENDING';
CREATE TABLE metro.ticket_issue_requests (
 operator_id uuid NOT NULL, actor_user_id text NOT NULL, request_key varchar(120) NOT NULL,
 payload_hash char(64) NOT NULL, order_id uuid NOT NULL, order_version integer NOT NULL,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(operator_id,actor_user_id,request_key),
 FOREIGN KEY(operator_id,order_id,order_version) REFERENCES metro.order_versions(operator_id,order_id,version)
);
ALTER TABLE metro.orders DROP CONSTRAINT orders_status_check;
ALTER TABLE metro.orders ADD CONSTRAINT orders_status_check CHECK(status IN
 ('DRAFT','HELD','PENDING_PAYMENT','PENDING_ISSUE','PAID','ISSUED','EXPIRED','CANCELLED','REFUND_PENDING','REFUNDED'));

CREATE FUNCTION metro.issuance_bounds(p_operator uuid,p_order uuid)
RETURNS TABLE(start_at timestamptz,end_at timestamptz) LANGUAGE sql STABLE SET search_path=metro,public,pg_catalog AS $f$
 SELECT CASE WHEN tp.product_type='SINGLE_TRIP' THEN o.service_date::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh' ELSE COALESCE(o.paid_at,statement_timestamp()) END,
 CASE WHEN tp.product_type='SINGLE_TRIP' THEN (o.service_date+1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh'
 ELSE (((COALESCE(o.paid_at,statement_timestamp()) AT TIME ZONE 'Asia/Ho_Chi_Minh')::date+
  CASE tp.product_type WHEN 'DAY' THEN 1 WHEN 'WEEK' THEN 7 WHEN 'MONTH' THEN 30 END)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh') END
 FROM metro.orders o JOIN metro.order_items i ON i.operator_id=o.operator_id AND i.order_id=o.id
 JOIN metro.ticket_products tp ON tp.operator_id=i.operator_id AND tp.id=i.ticket_product_id
 WHERE o.operator_id=p_operator AND o.id=p_order
$f$;

CREATE FUNCTION metro.check_issuance_order(p_operator uuid,p_order uuid) RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,metro,public AS $f$
DECLARE o metro.orders%ROWTYPE; b record;
BEGIN
 SELECT * INTO STRICT o FROM metro.orders WHERE operator_id=p_operator AND id=p_order;
 IF o.beneficiary_user_id IS NULL THEN RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='BENEFICIARY_REQUIRED'; END IF;
 IF NOT EXISTS(SELECT 1 FROM metro.user_profiles p JOIN public."AspNetUsers" a ON a."Id"=p.user_id
   WHERE p.user_id=o.beneficiary_user_id AND p.status='ACTIVE' AND a."EmailConfirmed") THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='BENEFICIARY_NOT_VERIFIED'; END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended(p_operator::text||':'||o.beneficiary_user_id,0));
 PERFORM 1 FROM metro.order_items WHERE operator_id=p_operator AND order_id=p_order ORDER BY id FOR UPDATE;
 PERFORM 1 FROM metro.order_versions WHERE operator_id=p_operator AND order_id=p_order AND version=o.order_version FOR SHARE;
 IF (SELECT count(*) FROM metro.order_items WHERE operator_id=p_operator AND order_id=p_order)<>1
 OR EXISTS(SELECT 1 FROM metro.order_items i JOIN metro.ticket_products p ON p.operator_id=i.operator_id AND p.id=i.ticket_product_id
 WHERE i.operator_id=p_operator AND i.order_id=p_order AND (i.passenger_count<>1 OR p.capacity_controlled OR p.product_type NOT IN ('SINGLE_TRIP','DAY','WEEK','MONTH'))) THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='ONE_PERSON_ONE_PRODUCT_REQUIRED'; END IF;
 IF NOT EXISTS(SELECT 1 FROM metro.order_versions v JOIN metro.order_items i ON i.operator_id=v.operator_id AND i.order_id=v.order_id
 WHERE v.operator_id=p_operator AND v.order_id=p_order AND v.version=o.order_version AND v.status='CONFIRMED'
 AND v.beneficiary_user_id=o.beneficiary_user_id AND v.ticket_product_id=i.ticket_product_id
 AND v.service_date IS NOT DISTINCT FROM o.service_date AND v.total_minor=o.total_minor
 AND v.subtotal_minor=o.subtotal_minor AND v.discount_minor=o.discount_minor AND v.fare_policy_version_id=o.fare_policy_version_id AND v.fare_snapshot=o.fare_snapshot
 AND v.origin_station_id IS NOT DISTINCT FROM i.origin_station_id AND v.destination_station_id IS NOT DISTINCT FROM i.destination_station_id) THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='ORDER_VERSION_CONFLICT'; END IF;
 SELECT * INTO b FROM metro.issuance_bounds(p_operator,p_order);
 IF EXISTS(SELECT 1 FROM metro.order_items i JOIN metro.ticket_products p ON p.operator_id=i.operator_id AND p.id=i.ticket_product_id
  WHERE i.operator_id=p_operator AND i.order_id=p_order AND p.product_type='SINGLE_TRIP'
   AND (o.service_date IS NULL OR o.service_date<(o.created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date
     OR o.service_date>(o.created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date+7
     OR i.origin_station_id IS NULL OR i.destination_station_id IS NULL)) THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='INVALID_TRAVEL_DATE_OR_ROUTE'; END IF;
 IF b.start_at IS NULL OR b.end_at IS NULL OR b.end_at<=statement_timestamp() THEN
  RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='INVALID_TRAVEL_DATE'; END IF;
 IF EXISTS(SELECT 1 FROM metro.tickets t LEFT JOIN metro.journeys j ON j.operator_id=t.operator_id AND j.ticket_id=t.id AND j.status IN ('OPEN','MANUAL_REVIEW')
 WHERE t.operator_id=p_operator AND t.holder_user_id=o.beneficiary_user_id AND t.order_id IS DISTINCT FROM p_order
 AND (j.id IS NOT NULL OR (t.status IN ('ISSUED','ACTIVE','IN_USE') AND tstzrange(t.effective_from,t.expires_at,'[)') && tstzrange(b.start_at,b.end_at,'[)'))))
 OR EXISTS(SELECT 1 FROM metro.orders q CROSS JOIN LATERAL metro.issuance_bounds(q.operator_id,q.id) qb
 WHERE q.operator_id=p_operator AND q.beneficiary_user_id=o.beneficiary_user_id AND q.id<>o.id
 AND q.status IN ('PAID','PENDING_ISSUE') AND (q.paid_at IS NOT NULL OR q.free_confirmed_at IS NOT NULL)
 AND tstzrange(qb.start_at,qb.end_at,'[)') && tstzrange(b.start_at,b.end_at,'[)')) THEN
  RAISE EXCEPTION USING ERRCODE='23505',MESSAGE='BENEFICIARY_RIGHT_CONFLICT'; END IF;
END $f$;

CREATE FUNCTION metro.prepare_ticket_issuance() RETURNS trigger LANGUAGE plpgsql SET search_path=metro,public,pg_catalog AS $f$
DECLARE o metro.orders%ROWTYPE; b record;
BEGIN
 SELECT * INTO o FROM metro.orders WHERE operator_id=NEW.operator_id AND id=NEW.order_id;
 IF NEW.status<>'ISSUED' OR (o.paid_at IS NULL AND o.free_confirmed_at IS NULL) THEN RETURN NEW; END IF;
 SELECT * INTO b FROM metro.issuance_bounds(NEW.operator_id,NEW.order_id);
 NEW.effective_from:=b.start_at; NEW.expires_at:=b.end_at;
 RETURN NEW;
END $f$;
CREATE TRIGGER trg_tickets_prepare_issuance BEFORE INSERT ON metro.tickets FOR EACH ROW EXECUTE FUNCTION metro.prepare_ticket_issuance();
CREATE FUNCTION metro.enqueue_ticket_issuance() RETURNS trigger LANGUAGE plpgsql SET search_path=metro,public,pg_catalog AS $f$
DECLARE o metro.orders%ROWTYPE;
BEGIN
 SELECT * INTO o FROM metro.orders WHERE operator_id=NEW.operator_id AND id=NEW.order_id;
 IF NEW.status='ISSUED' AND (o.paid_at IS NOT NULL OR o.free_confirmed_at IS NOT NULL) THEN
  INSERT INTO metro.ticket_issuance_jobs(ticket_id,operator_id,order_id,due_at,next_attempt_at)
  VALUES(NEW.id,NEW.operator_id,NEW.order_id,NEW.effective_from,GREATEST(NEW.effective_from,statement_timestamp())) ON CONFLICT DO NOTHING;
 END IF; RETURN NEW;
END $f$;
CREATE TRIGGER trg_tickets_queue_issuance AFTER INSERT ON metro.tickets FOR EACH ROW EXECUTE FUNCTION metro.enqueue_ticket_issuance();

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
            (OLD.status = 'PENDING_ISSUE' AND NEW.status IN ('ISSUED','CANCELLED')) OR
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
                 AND NEW.status IN ('PENDING_ISSUE','ISSUED') AND NEW.total_minor = 0
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


REVOKE ALL ON FUNCTION metro.check_issuance_order(uuid,uuid) FROM PUBLIC;
