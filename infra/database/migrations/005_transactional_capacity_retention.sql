-- Concurrency-safe capacity allocation, atomic payment/ticket issuance,
-- cancellation operations, lifecycle guards, and reconciliation retention.

CREATE UNIQUE INDEX uq_active_capacity_hold_per_order_item
    ON metro.capacity_holds (operator_id, order_item_id)
    WHERE status IN ('HELD', 'CONFIRMED');

CREATE UNIQUE INDEX uq_succeeded_payment_per_order
    ON metro.payments (operator_id, order_id)
    WHERE status IN ('SUCCEEDED', 'PARTIALLY_REFUNDED', 'REFUNDED');

CREATE OR REPLACE FUNCTION metro.hold_trip_capacity(
    p_operator_id uuid,
    p_order_item_id uuid,
    p_idempotency_key varchar(120),
    p_expires_at timestamptz
) RETURNS uuid
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_item metro.order_items%ROWTYPE;
    v_order_status text;
    v_capacity_controlled boolean;
    v_origin_sequence smallint;
    v_destination_sequence smallint;
    v_hold_id uuid;
    v_existing metro.capacity_holds%ROWTYPE;
    v_segment metro.trip_capacity_segments%ROWTYPE;
    v_segment_count integer := 0;
BEGIN
    IF p_idempotency_key IS NULL OR btrim(p_idempotency_key) = '' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Capacity hold idempotency key is required';
    END IF;
    IF p_expires_at <= clock_timestamp() THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Capacity hold expiry must be in the future';
    END IF;

    SELECT * INTO v_existing
    FROM metro.capacity_holds
    WHERE operator_id = p_operator_id AND idempotency_key = p_idempotency_key
    FOR UPDATE;

    IF FOUND THEN
        IF v_existing.order_item_id <> p_order_item_id THEN
            RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Capacity hold idempotency key belongs to another order item';
        END IF;
        IF v_existing.status IN ('HELD', 'CONFIRMED') THEN
            RETURN v_existing.id;
        END IF;
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'A terminal capacity hold cannot be reused';
    END IF;

    SELECT * INTO v_item
    FROM metro.order_items
    WHERE operator_id = p_operator_id AND id = p_order_item_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Order item not found';
    END IF;

    SELECT o.status, tp.capacity_controlled
      INTO v_order_status, v_capacity_controlled
    FROM metro.orders o
    JOIN metro.ticket_products tp
      ON tp.operator_id = p_operator_id AND tp.id = v_item.ticket_product_id
    WHERE o.operator_id = p_operator_id AND o.id = v_item.order_id
    FOR UPDATE OF o;
    IF NOT v_capacity_controlled THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Ticket product is not capacity controlled';
    END IF;
    IF v_order_status NOT IN ('DRAFT', 'HELD', 'PENDING_PAYMENT') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Order is not eligible for a capacity hold';
    END IF;
    IF v_item.planned_trip_id IS NULL OR v_item.origin_station_id IS NULL OR v_item.destination_station_id IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Capacity-controlled order item requires trip, origin and destination';
    END IF;

    SELECT sequence_no INTO STRICT v_origin_sequence
    FROM metro.planned_stop_times
    WHERE operator_id = p_operator_id
      AND planned_trip_id = v_item.planned_trip_id
      AND station_id = v_item.origin_station_id;

    SELECT sequence_no INTO STRICT v_destination_sequence
    FROM metro.planned_stop_times
    WHERE operator_id = p_operator_id
      AND planned_trip_id = v_item.planned_trip_id
      AND station_id = v_item.destination_station_id;

    IF v_destination_sequence <= v_origin_sequence THEN
        RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Destination must follow origin on the selected trip';
    END IF;

    -- Every booking touching the same trip segments locks them in route order.
    -- This deterministic row-lock order prevents lost updates and minimizes deadlocks.
    FOR v_segment IN
        SELECT *
        FROM metro.trip_capacity_segments
        WHERE operator_id = p_operator_id
          AND planned_trip_id = v_item.planned_trip_id
          AND from_sequence >= v_origin_sequence
          AND from_sequence < v_destination_sequence
        ORDER BY from_sequence
        FOR UPDATE
    LOOP
        v_segment_count := v_segment_count + 1;
        IF v_segment.held_count + v_segment.confirmed_count + v_item.passenger_count > v_segment.permitted_capacity THEN
            RAISE EXCEPTION USING
                ERRCODE = 'P0001',
                MESSAGE = format('Capacity exceeded on segment %s-%s', v_segment.from_sequence, v_segment.to_sequence),
                DETAIL = format('permitted=%s held=%s confirmed=%s requested=%s',
                    v_segment.permitted_capacity, v_segment.held_count,
                    v_segment.confirmed_count, v_item.passenger_count);
        END IF;
    END LOOP;

    IF v_segment_count <> v_destination_sequence - v_origin_sequence THEN
        RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'Trip capacity segments are incomplete for the requested journey';
    END IF;

    INSERT INTO metro.capacity_holds (
        operator_id, order_item_id, idempotency_key, passenger_count, expires_at
    ) VALUES (
        p_operator_id, p_order_item_id, p_idempotency_key, v_item.passenger_count, p_expires_at
    ) RETURNING id INTO v_hold_id;

    INSERT INTO metro.capacity_hold_segments (
        operator_id, capacity_hold_id, capacity_segment_id, passenger_count
    )
    SELECT p_operator_id, v_hold_id, id, v_item.passenger_count
    FROM metro.trip_capacity_segments
    WHERE operator_id = p_operator_id
      AND planned_trip_id = v_item.planned_trip_id
      AND from_sequence >= v_origin_sequence
      AND from_sequence < v_destination_sequence;

    UPDATE metro.trip_capacity_segments
    SET held_count = held_count + v_item.passenger_count,
        updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id
      AND planned_trip_id = v_item.planned_trip_id
      AND from_sequence >= v_origin_sequence
      AND from_sequence < v_destination_sequence;

    UPDATE metro.orders
    SET status = CASE WHEN status = 'DRAFT' THEN 'HELD' ELSE status END,
        hold_expires_at = GREATEST(COALESCE(hold_expires_at, p_expires_at), p_expires_at),
        updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = v_item.order_id;

    RETURN v_hold_id;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.confirm_capacity_hold(
    p_operator_id uuid,
    p_capacity_hold_id uuid
) RETURNS boolean
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_hold metro.capacity_holds%ROWTYPE;
    v_segment record;
BEGIN
    SELECT * INTO v_hold
    FROM metro.capacity_holds
    WHERE operator_id = p_operator_id AND id = p_capacity_hold_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Capacity hold not found';
    END IF;
    IF v_hold.status = 'CONFIRMED' THEN
        RETURN true;
    END IF;
    IF v_hold.status <> 'HELD' THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Only a held capacity allocation can be confirmed';
    END IF;
    IF v_hold.expires_at <= clock_timestamp() THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Capacity hold has expired';
    END IF;

    FOR v_segment IN
        SELECT s.id, hs.passenger_count
        FROM metro.capacity_hold_segments hs
        JOIN metro.trip_capacity_segments s
          ON s.operator_id = hs.operator_id AND s.id = hs.capacity_segment_id
        WHERE hs.operator_id = p_operator_id AND hs.capacity_hold_id = p_capacity_hold_id
        ORDER BY s.from_sequence
        FOR UPDATE OF s
    LOOP
        UPDATE metro.trip_capacity_segments
        SET held_count = held_count - v_segment.passenger_count,
            confirmed_count = confirmed_count + v_segment.passenger_count,
            updated_at = clock_timestamp()
        WHERE operator_id = p_operator_id AND id = v_segment.id;
    END LOOP;

    UPDATE metro.capacity_holds
    SET status = 'CONFIRMED', confirmed_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_capacity_hold_id;

    RETURN true;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.release_capacity_hold(
    p_operator_id uuid,
    p_capacity_hold_id uuid,
    p_terminal_status text DEFAULT 'RELEASED',
    p_reason text DEFAULT 'Released by workflow'
) RETURNS boolean
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_hold metro.capacity_holds%ROWTYPE;
    v_segment record;
BEGIN
    IF p_terminal_status NOT IN ('RELEASED', 'EXPIRED', 'CANCELLED') THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Invalid terminal capacity hold status';
    END IF;
    IF p_reason IS NULL OR btrim(p_reason) = '' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Capacity release reason is required';
    END IF;

    SELECT * INTO v_hold
    FROM metro.capacity_holds
    WHERE operator_id = p_operator_id AND id = p_capacity_hold_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Capacity hold not found';
    END IF;
    IF v_hold.status IN ('RELEASED', 'EXPIRED', 'CANCELLED') THEN
        RETURN false;
    END IF;

    FOR v_segment IN
        SELECT s.id, hs.passenger_count
        FROM metro.capacity_hold_segments hs
        JOIN metro.trip_capacity_segments s
          ON s.operator_id = hs.operator_id AND s.id = hs.capacity_segment_id
        WHERE hs.operator_id = p_operator_id AND hs.capacity_hold_id = p_capacity_hold_id
        ORDER BY s.from_sequence
        FOR UPDATE OF s
    LOOP
        IF v_hold.status = 'HELD' THEN
            UPDATE metro.trip_capacity_segments
            SET held_count = held_count - v_segment.passenger_count,
                updated_at = clock_timestamp()
            WHERE operator_id = p_operator_id AND id = v_segment.id;
        ELSE
            UPDATE metro.trip_capacity_segments
            SET confirmed_count = confirmed_count - v_segment.passenger_count,
                updated_at = clock_timestamp()
            WHERE operator_id = p_operator_id AND id = v_segment.id;
        END IF;
    END LOOP;

    UPDATE metro.capacity_holds
    SET status = p_terminal_status,
        released_at = clock_timestamp(),
        release_reason = p_reason
    WHERE operator_id = p_operator_id AND id = p_capacity_hold_id;

    RETURN true;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.expire_capacity_holds(p_batch_size integer DEFAULT 500)
RETURNS integer
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_hold record;
    v_count integer := 0;
BEGIN
    IF p_batch_size < 1 OR p_batch_size > 10000 THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Batch size must be between 1 and 10000';
    END IF;

    FOR v_hold IN
        SELECT operator_id, id
        FROM metro.capacity_holds
        WHERE status = 'HELD' AND expires_at <= clock_timestamp()
        ORDER BY expires_at, id
        LIMIT p_batch_size
        FOR UPDATE SKIP LOCKED
    LOOP
        PERFORM metro.release_capacity_hold(v_hold.operator_id, v_hold.id, 'EXPIRED', 'Capacity hold TTL elapsed');
        v_count := v_count + 1;
    END LOOP;
    RETURN v_count;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.confirm_payment_and_issue_tickets(
    p_operator_id uuid,
    p_payment_id uuid,
    p_provider_event_id text,
    p_event_idempotency_key varchar(120),
    p_payload_hash char(64),
    p_received_amount_minor bigint,
    p_received_currency char(3),
    p_signature_verified boolean
) RETURNS integer
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_payment metro.payments%ROWTYPE;
    v_order metro.orders%ROWTYPE;
    v_item record;
    v_hold_id uuid;
    v_issued_count integer;
BEGIN
    IF p_provider_event_id IS NULL OR btrim(p_provider_event_id) = ''
       OR p_event_idempotency_key IS NULL OR btrim(p_event_idempotency_key) = '' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Provider event and idempotency keys are required';
    END IF;
    IF NOT p_signature_verified THEN
        RAISE EXCEPTION USING ERRCODE = '28000', MESSAGE = 'Payment event signature was not verified';
    END IF;
    IF p_payload_hash !~ '^[0-9a-f]{64}$' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Payment payload hash must be lowercase SHA-256 hex';
    END IF;

    SELECT * INTO v_payment
    FROM metro.payments
    WHERE operator_id = p_operator_id AND id = p_payment_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Payment not found';
    END IF;

    SELECT * INTO v_order
    FROM metro.orders
    WHERE operator_id = p_operator_id AND id = v_payment.order_id
    FOR UPDATE;

    IF EXISTS (
        SELECT 1 FROM metro.payment_events
        WHERE operator_id = p_operator_id
          AND (provider_event_id = p_provider_event_id OR idempotency_key = p_event_idempotency_key)
          AND payment_id <> p_payment_id
    ) THEN
        RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Payment event idempotency key belongs to another payment';
    END IF;

    IF v_payment.status IN ('SUCCEEDED', 'PARTIALLY_REFUNDED', 'REFUNDED') THEN
        INSERT INTO metro.payment_events (
            operator_id, payment_id, provider_event_id, idempotency_key, event_type,
            signature_verified, received_amount_minor, received_currency, payload_hash,
            outcome, processed_at
        ) VALUES (
            p_operator_id, p_payment_id, p_provider_event_id, p_event_idempotency_key,
            'PAYMENT_SUCCEEDED', true, p_received_amount_minor, p_received_currency,
            p_payload_hash, 'ALREADY_PROCESSED', clock_timestamp()
        )
        ON CONFLICT DO NOTHING;

        SELECT count(*) INTO v_issued_count
        FROM metro.tickets t
        JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
        WHERE oi.operator_id = p_operator_id AND oi.order_id = v_order.id;
        RETURN v_issued_count;
    END IF;

    IF v_payment.status <> 'PENDING' OR v_order.status NOT IN ('HELD', 'PENDING_PAYMENT') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Payment or order is not in a confirmable state';
    END IF;
    IF p_received_amount_minor <> v_payment.amount_minor OR p_received_amount_minor <> v_order.total_minor
       OR p_received_currency <> v_payment.currency OR p_received_currency <> v_order.currency THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Payment amount or currency does not match the order snapshot';
    END IF;

    FOR v_item IN
        SELECT oi.id, tp.capacity_controlled
        FROM metro.order_items oi
        JOIN metro.ticket_products tp
          ON tp.operator_id = oi.operator_id AND tp.id = oi.ticket_product_id
        WHERE oi.operator_id = p_operator_id AND oi.order_id = v_order.id
        ORDER BY oi.id
        FOR UPDATE OF oi
    LOOP
        IF v_item.capacity_controlled THEN
            SELECT id INTO v_hold_id
            FROM metro.capacity_holds
            WHERE operator_id = p_operator_id
              AND order_item_id = v_item.id
              AND status IN ('HELD', 'CONFIRMED')
            FOR UPDATE;
            IF NOT FOUND THEN
                RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Capacity-controlled item has no active hold';
            END IF;
            PERFORM metro.confirm_capacity_hold(p_operator_id, v_hold_id);
        END IF;
    END LOOP;

    INSERT INTO metro.payment_events (
        operator_id, payment_id, provider_event_id, idempotency_key, event_type,
        signature_verified, received_amount_minor, received_currency, payload_hash,
        outcome, processed_at
    ) VALUES (
        p_operator_id, p_payment_id, p_provider_event_id, p_event_idempotency_key,
        'PAYMENT_SUCCEEDED', true, p_received_amount_minor, p_received_currency,
        p_payload_hash, 'ACCEPTED', clock_timestamp()
    )
    ON CONFLICT DO NOTHING;

    UPDATE metro.payments
    SET status = 'SUCCEEDED', verified_at = clock_timestamp(), succeeded_at = clock_timestamp(),
        reconciliation_status = 'UNRECONCILED', updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_payment_id;

    UPDATE metro.orders
    SET status = 'PAID', paid_at = clock_timestamp(), updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = v_order.id;

    INSERT INTO metro.tickets (
        operator_id, ticket_code, order_item_id, ticket_product_id, holder_user_id,
        sequence_no, status, latest_activation_at, fare_policy_version_id
    )
    SELECT oi.operator_id,
           v_order.order_code || '-' || lpad(gs::text, 4, '0') || '-' || substr(replace(oi.id::text, '-', ''), 1, 8),
           oi.id, oi.ticket_product_id, v_order.beneficiary_user_id,
           gs, 'ISSUED',
           CASE WHEN tp.latest_activation_interval IS NULL THEN NULL
                ELSE clock_timestamp() + tp.latest_activation_interval END,
           v_order.fare_policy_version_id
    FROM metro.order_items oi
    JOIN metro.ticket_products tp
      ON tp.operator_id = oi.operator_id AND tp.id = oi.ticket_product_id
    CROSS JOIN LATERAL generate_series(1, oi.passenger_count) AS gs
    WHERE oi.operator_id = p_operator_id AND oi.order_id = v_order.id
    ON CONFLICT (operator_id, order_item_id, sequence_no) DO NOTHING;

    GET DIAGNOSTICS v_issued_count = ROW_COUNT;

    UPDATE metro.orders
    SET status = 'ISSUED', issued_at = clock_timestamp(), updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = v_order.id;

    RETURN v_issued_count;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.cancel_unpaid_order(
    p_operator_id uuid,
    p_order_id uuid,
    p_cancelled_by_user_id text,
    p_reason text
) RETURNS boolean
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_order metro.orders%ROWTYPE;
    v_hold record;
BEGIN
    IF p_reason IS NULL OR btrim(p_reason) = '' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Cancellation reason is required';
    END IF;

    SELECT * INTO v_order
    FROM metro.orders
    WHERE operator_id = p_operator_id AND id = p_order_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Order not found';
    END IF;
    IF v_order.status = 'CANCELLED' THEN
        RETURN false;
    END IF;
    IF v_order.status NOT IN ('DRAFT', 'HELD', 'PENDING_PAYMENT', 'EXPIRED') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Paid or issued orders require the refund workflow';
    END IF;

    FOR v_hold IN
        SELECT ch.id
        FROM metro.capacity_holds ch
        JOIN metro.order_items oi
          ON oi.operator_id = ch.operator_id AND oi.id = ch.order_item_id
        WHERE oi.operator_id = p_operator_id AND oi.order_id = p_order_id
          AND ch.status IN ('HELD', 'CONFIRMED')
        ORDER BY ch.id
        FOR UPDATE OF ch
    LOOP
        PERFORM metro.release_capacity_hold(p_operator_id, v_hold.id, 'CANCELLED', p_reason);
    END LOOP;

    UPDATE metro.payments
    SET status = 'CANCELLED', updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND order_id = p_order_id AND status = 'PENDING';

    UPDATE metro.orders
    SET status = 'CANCELLED', cancelled_at = clock_timestamp(),
        cancelled_by_user_id = p_cancelled_by_user_id,
        cancellation_reason = p_reason, updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_order_id;

    RETURN true;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.revoke_unused_ticket(
    p_operator_id uuid,
    p_ticket_id uuid,
    p_reason text
) RETURNS boolean
LANGUAGE plpgsql
SECURITY INVOKER
SET search_path = metro, public, pg_catalog
AS $function$
DECLARE
    v_ticket metro.tickets%ROWTYPE;
BEGIN
    IF p_reason IS NULL OR btrim(p_reason) = '' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Ticket revocation reason is required';
    END IF;
    SELECT * INTO v_ticket
    FROM metro.tickets
    WHERE operator_id = p_operator_id AND id = p_ticket_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Ticket not found';
    END IF;
    IF v_ticket.status = 'REVOKED' THEN
        RETURN false;
    END IF;
    IF v_ticket.status NOT IN ('ISSUED', 'ACTIVE') OR v_ticket.first_used_at IS NOT NULL
       OR EXISTS (SELECT 1 FROM metro.journeys j
                  WHERE j.operator_id = p_operator_id AND j.ticket_id = p_ticket_id AND j.status = 'OPEN') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Used or in-progress ticket cannot be directly revoked';
    END IF;

    UPDATE metro.ticket_credentials
    SET status = 'REVOKED', revoked_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND ticket_id = p_ticket_id AND status = 'ACTIVE';

    UPDATE metro.tickets
    SET status = 'REVOKED', revoked_at = clock_timestamp(), revocation_reason = p_reason,
        updated_at = clock_timestamp()
    WHERE operator_id = p_operator_id AND id = p_ticket_id;
    RETURN true;
END;
$function$;

CREATE OR REPLACE FUNCTION metro.enforce_workflow_state_transition()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
BEGIN
    IF OLD.status = NEW.status THEN
        RETURN NEW;
    END IF;

    IF TG_TABLE_NAME = 'orders' AND NOT (
        (OLD.status = 'DRAFT' AND NEW.status IN ('HELD','PENDING_PAYMENT','EXPIRED','CANCELLED')) OR
        (OLD.status = 'HELD' AND NEW.status IN ('PENDING_PAYMENT','PAID','EXPIRED','CANCELLED')) OR
        (OLD.status = 'PENDING_PAYMENT' AND NEW.status IN ('HELD','PAID','EXPIRED','CANCELLED')) OR
        (OLD.status = 'PAID' AND NEW.status IN ('ISSUED','REFUND_PENDING')) OR
        (OLD.status = 'ISSUED' AND NEW.status IN ('REFUND_PENDING')) OR
        (OLD.status = 'REFUND_PENDING' AND NEW.status IN ('REFUNDED','ISSUED')) OR
        (OLD.status = 'EXPIRED' AND NEW.status = 'CANCELLED')
    ) THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = format('Invalid order state transition: %s -> %s', OLD.status, NEW.status);
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

CREATE TRIGGER trg_orders_state_transition
BEFORE UPDATE OF status ON metro.orders
FOR EACH ROW EXECUTE FUNCTION metro.enforce_workflow_state_transition();

CREATE TRIGGER trg_payments_state_transition
BEFORE UPDATE OF status ON metro.payments
FOR EACH ROW EXECUTE FUNCTION metro.enforce_workflow_state_transition();

CREATE TRIGGER trg_tickets_state_transition
BEFORE UPDATE OF status ON metro.tickets
FOR EACH ROW EXECUTE FUNCTION metro.enforce_workflow_state_transition();

CREATE OR REPLACE FUNCTION metro.reject_physical_history_deletion()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
BEGIN
    RAISE EXCEPTION USING
        ERRCODE = '55000',
        MESSAGE = format('Physical deletion is prohibited for reconciliation table %I.%I', TG_TABLE_SCHEMA, TG_TABLE_NAME),
        HINT = 'Use lifecycle status changes and approved anonymization/retention workflows.';
END;
$function$;

DO $retention_triggers$
DECLARE
    v_table text;
BEGIN
    FOREACH v_table IN ARRAY ARRAY[
        'audit_logs', 'orders', 'order_items', 'capacity_holds', 'capacity_hold_segments',
        'payments', 'payment_events', 'tickets', 'ticket_credentials', 'journeys',
        'afc_events', 'refund_requests', 'refund_transactions', 'refund_attempts',
        'refund_payout_revisions'
    ]
    LOOP
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE DELETE OR TRUNCATE ON metro.%I FOR EACH STATEMENT EXECUTE FUNCTION metro.reject_physical_history_deletion()',
            'trg_' || v_table || '_retain_history', v_table
        );
    END LOOP;
END;
$retention_triggers$;

CREATE OR REPLACE FUNCTION metro.validate_retention_policy_target()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = metro, public, pg_catalog
AS $function$
BEGIN
    IF upper(NEW.entity_type) = ANY (ARRAY[
        'AUDIT_LOG','ORDER','ORDER_ITEM','CAPACITY_HOLD','PAYMENT','PAYMENT_EVENT',
        'TICKET','TICKET_CREDENTIAL','JOURNEY','AFC_EVENT','REFUND_REQUEST',
        'REFUND_TRANSACTION','REFUND_ATTEMPT','REFUND_PAYOUT_REVISION'
    ]) AND NEW.action = 'DELETE_EVIDENCE' THEN
        RAISE EXCEPTION USING ERRCODE = '23514',
            MESSAGE = 'Reconciliation entities cannot have a physical-delete retention action';
    END IF;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trg_retention_policy_protected_targets
BEFORE INSERT OR UPDATE ON metro.retention_policies
FOR EACH ROW EXECUTE FUNCTION metro.validate_retention_policy_target();

COMMENT ON FUNCTION metro.hold_trip_capacity(uuid, uuid, varchar, timestamptz)
IS 'Atomically locks all affected trip segments in route order and creates an idempotent capacity hold.';
COMMENT ON FUNCTION metro.confirm_payment_and_issue_tickets(uuid, uuid, text, varchar, char, bigint, char, boolean)
IS 'Atomically validates an idempotent payment callback, confirms capacity and issues exactly one ticket per passenger.';
COMMENT ON FUNCTION metro.cancel_unpaid_order(uuid, uuid, text, text)
IS 'Atomically cancels an unpaid order, pending payments and all active capacity holds.';
COMMENT ON FUNCTION metro.revoke_unused_ticket(uuid, uuid, text)
IS 'Revokes an unused ticket and its active credentials without deleting reconciliation history.';
