-- BE-09: verified payment creates the pending ticket; QR readiness is completed separately.
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
SECURITY DEFINER
SET search_path = pg_catalog, metro, public
AS $function$
DECLARE
    v_locked_user text;
    v_payment metro.payments%ROWTYPE;
    v_order metro.orders%ROWTYPE;
    v_item record;
    v_hold_id uuid;
    v_issued_count integer;
    v_event_id uuid;
    v_conflict metro.payment_events%ROWTYPE;
BEGIN
    IF p_provider_event_id IS NULL OR btrim(p_provider_event_id) = ''
       OR p_event_idempotency_key IS NULL OR btrim(p_event_idempotency_key) = '' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Provider event and idempotency keys are required';
    END IF;
    IF p_signature_verified IS NOT TRUE THEN
        RAISE EXCEPTION USING ERRCODE = '28000', MESSAGE = 'Payment event signature was not verified';
    END IF;
    IF p_payload_hash IS NULL OR p_payload_hash !~ '^[0-9a-f]{64}$' THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Payment payload hash must be lowercase SHA-256 hex';
    END IF;
    IF p_received_amount_minor IS NULL OR p_received_currency IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = '22023', MESSAGE = 'Received amount and currency are required';
    END IF;

    SELECT o.beneficiary_user_id INTO v_locked_user FROM metro.payments p JOIN metro.orders o ON o.operator_id=p.operator_id AND o.id=p.order_id WHERE p.operator_id=p_operator_id AND p.id=p_payment_id;
    PERFORM pg_advisory_xact_lock(hashtextextended(p_operator_id::text||':'||COALESCE(v_locked_user,''),0));
    -- Shared lock hierarchy for all ticketing workflows:
    -- payment -> order -> order_item -> capacity_hold -> trip_capacity_segments.
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
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Order not found';
    END IF;

    IF p_received_amount_minor IS DISTINCT FROM v_payment.amount_minor
       OR p_received_amount_minor IS DISTINCT FROM v_order.total_minor
       OR p_received_currency IS DISTINCT FROM v_payment.currency
       OR p_received_currency IS DISTINCT FROM v_order.currency THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Payment amount or currency does not match the order snapshot';
    END IF;

    -- Atomically claim the provider event before any success transition. A key owned
    -- by another payment must abort, including the already-succeeded branch where the
    -- previous ON CONFLICT DO NOTHING silently ignored the ownership clash.
    INSERT INTO metro.payment_events (
        operator_id, payment_id, provider_event_id, idempotency_key, event_type,
        signature_verified, received_amount_minor, received_currency, payload_hash,
        outcome, processed_at
    ) VALUES (
        p_operator_id, p_payment_id, p_provider_event_id, p_event_idempotency_key,
        'PAYMENT_SUCCEEDED', p_signature_verified, p_received_amount_minor, p_received_currency,
        p_payload_hash, 'CLAIMED', clock_timestamp()
    )
    ON CONFLICT DO NOTHING
    RETURNING id INTO v_event_id;

    IF v_event_id IS NULL THEN
        SELECT * INTO v_conflict
        FROM metro.payment_events
        WHERE operator_id = p_operator_id AND provider_event_id = p_provider_event_id
        FOR UPDATE;
        IF FOUND THEN
            IF v_conflict.payment_id <> p_payment_id THEN
                RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Payment event belongs to another payment';
            END IF;
            IF v_conflict.payload_hash IS DISTINCT FROM p_payload_hash THEN
                RAISE EXCEPTION USING ERRCODE='23505',MESSAGE='PAYMENT_EVENT_PAYLOAD_CONFLICT';
            END IF;
            UPDATE metro.payment_events SET outcome = 'DUPLICATE'
            WHERE operator_id = p_operator_id AND id = v_conflict.id;
            SELECT count(*) INTO v_issued_count
            FROM metro.tickets t
            JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
            WHERE oi.operator_id = p_operator_id AND oi.order_id = v_order.id;
            RETURN v_issued_count;
        END IF;

        SELECT * INTO v_conflict
        FROM metro.payment_events
        WHERE operator_id = p_operator_id AND idempotency_key = p_event_idempotency_key
        FOR UPDATE;
        IF FOUND THEN
            IF v_conflict.payment_id <> p_payment_id THEN
                RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Payment event idempotency key belongs to another payment';
            END IF;
            RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Payment event idempotency key is already used by another event';
        END IF;

        RAISE EXCEPTION USING ERRCODE = '23505', MESSAGE = 'Payment event could not be claimed';
    END IF;

    IF v_payment.status IN ('SUCCEEDED', 'PARTIALLY_REFUNDED', 'REFUNDED') THEN
        UPDATE metro.payment_events SET outcome = 'ALREADY_PROCESSED'
        WHERE operator_id = p_operator_id AND id = v_event_id;

        SELECT count(*) INTO v_issued_count
        FROM metro.tickets t
        JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
        WHERE oi.operator_id = p_operator_id AND oi.order_id = v_order.id;
        RETURN v_issued_count;
    END IF;

    IF v_payment.order_version IS DISTINCT FROM v_order.order_version OR v_order.beneficiary_user_id IS DISTINCT FROM v_locked_user THEN
        RAISE EXCEPTION USING ERRCODE='22023', MESSAGE='ORDER_VERSION_CONFLICT';
    END IF;
    PERFORM metro.check_issuance_order(p_operator_id,v_order.id);
    IF EXISTS(SELECT 1 FROM metro.user_profiles u WHERE u.user_id=v_order.beneficiary_user_id
      AND (u.birth_date+interval '6 years')::date>COALESCE(v_order.service_date,(statement_timestamp() AT TIME ZONE 'Asia/Ho_Chi_Minh')::date)) THEN
      RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='CHILD_REQUIRES_FREE_SINGLE'; END IF;
    IF v_payment.status <> 'PENDING' OR v_order.status NOT IN ('HELD', 'PENDING_PAYMENT') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'Payment or order is not in a confirmable state';
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

    UPDATE metro.payment_events SET outcome = 'ACCEPTED'
    WHERE operator_id = p_operator_id AND id = v_event_id;

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
           -- Full order-item UUID plus sequence is unique per ticket and stays under 80 chars.
           'TKT-' || replace(oi.id::text, '-', '') || '-' || gs::text,
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

    -- Ticket is durable; the QR worker finalizes orders.ISSUED only when ready.

    RETURN v_issued_count;
END;
$function$;


REVOKE ALL ON FUNCTION metro.confirm_payment_and_issue_tickets(uuid,uuid,text,varchar,char,bigint,char,boolean) FROM PUBLIC;
