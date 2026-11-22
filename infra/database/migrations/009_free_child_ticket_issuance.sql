-- BE-09: confirmed free child order, approved guardian and no fake payment.
CREATE FUNCTION metro.eligible_guardian_ticket(p_operator uuid,p_order uuid)
RETURNS TABLE(link_id uuid,ticket_id uuid,guardian_user_id text) LANGUAGE sql STABLE SET search_path=metro,public,pg_catalog AS $f$
 SELECT g.id,t.id,g.guardian_user_id FROM metro.orders o
 JOIN metro.order_items i ON i.operator_id=o.operator_id AND i.order_id=o.id
 JOIN metro.guardian_links g ON g.operator_id=o.operator_id AND g.child_user_id=o.beneficiary_user_id
 JOIN metro.tickets t ON t.operator_id=g.operator_id AND t.holder_user_id=g.guardian_user_id
 JOIN metro.ticket_products p ON p.operator_id=t.operator_id AND p.id=t.ticket_product_id
 WHERE o.operator_id=p_operator AND o.id=p_order AND g.status='ACTIVE' AND g.verification_type='GUARDIANSHIP' AND g.verified_at IS NOT NULL AND g.verified_by_user_id IS NOT NULL
 AND (g.valid_from IS NULL OR g.valid_from<=o.service_date) AND (g.valid_to IS NULL OR g.valid_to>=o.service_date)
 AND t.status IN ('ISSUED','ACTIVE','IN_USE') AND EXISTS(SELECT 1 FROM metro.orders paid WHERE paid.operator_id=t.operator_id AND paid.id=t.order_id AND paid.paid_at IS NOT NULL)
 AND ((p.product_type='SINGLE_TRIP' AND t.service_date=o.service_date AND t.origin_station_id=i.origin_station_id AND t.destination_station_id=i.destination_station_id)
 OR (p.product_type IN ('DAY','WEEK','MONTH') AND t.effective_from<(o.service_date+1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh'
 AND t.expires_at>o.service_date::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh'))
 ORDER BY g.id,t.id
$f$;
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
    v_locked_user text;
BEGIN
    SELECT beneficiary_user_id INTO v_locked_user FROM metro.orders WHERE operator_id=p_operator_id AND id=p_order_id;
    PERFORM pg_advisory_xact_lock(hashtextextended(p_operator_id::text||':'||COALESCE(v_locked_user,''),0));
    SELECT * INTO v_order
    FROM metro.orders
    WHERE operator_id = p_operator_id AND id = p_order_id
    FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'Order not found';
    END IF;

    IF v_order.beneficiary_user_id IS DISTINCT FROM v_locked_user THEN
        RAISE EXCEPTION USING ERRCODE='40001',MESSAGE='ORDER_VERSION_CONFLICT';
    END IF;

    IF v_order.free_confirmed_at IS NOT NULL AND v_order.status IN ('PENDING_ISSUE','ISSUED') THEN
        RETURN 0;
    END IF;
    IF v_order.status NOT IN ('DRAFT','HELD','PENDING_PAYMENT') THEN
        RAISE EXCEPTION USING ERRCODE = 'P0001',
            MESSAGE = format('Order %s cannot be issued free from status %s', v_order.id, v_order.status);
    END IF;
    PERFORM metro.check_issuance_order(p_operator_id,p_order_id);
    IF NOT EXISTS(SELECT 1 FROM metro.user_profiles u JOIN public."AspNetUsers" a ON a."Id"=u.user_id
      JOIN metro.order_items i ON i.operator_id=p_operator_id AND i.order_id=p_order_id
      JOIN metro.ticket_products p ON p.operator_id=i.operator_id AND p.id=i.ticket_product_id
      WHERE u.user_id=v_order.beneficiary_user_id AND a."EmailConfirmed" AND u.status NOT IN ('LOCKED','DISABLED','CLOSED')
      AND p.product_type='SINGLE_TRIP' AND u.birth_date<=v_order.service_date AND (u.birth_date+interval '6 years')::date>v_order.service_date) THEN
      RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='CHILD_NOT_ELIGIBLE'; END IF;
    IF NOT EXISTS(SELECT 1 FROM metro.eligible_guardian_ticket(p_operator_id,p_order_id) g
       WHERE p_actor_user_id=g.guardian_user_id) THEN
      RAISE EXCEPTION USING ERRCODE='28000',MESSAGE='GUARDIAN_REQUIRED'; END IF;
    IF EXISTS(SELECT 1 FROM metro.payments WHERE operator_id=p_operator_id AND order_id=p_order_id) THEN
      RAISE EXCEPTION USING ERRCODE='22023',MESSAGE='FREE_ORDER_HAS_PAYMENT'; END IF;
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
    UPDATE metro.orders
    SET status = 'PENDING_ISSUE',
        free_confirmed_at = clock_timestamp(),
        payment_started_at = NULL,
        issued_at = NULL,
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

    UPDATE metro.tickets t SET related_ticket_id=g.ticket_id,guardian_link_id=g.link_id,guardian_user_id=g.guardian_user_id,relation_type='CHILD_OF_GUARDIAN'
    FROM (SELECT * FROM metro.eligible_guardian_ticket(p_operator_id,p_order_id)
      WHERE guardian_user_id=p_actor_user_id ORDER BY link_id,ticket_id LIMIT 1) g
    WHERE t.operator_id=p_operator_id AND t.order_id=p_order_id;
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


REVOKE ALL ON FUNCTION metro.eligible_guardian_ticket(uuid,uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION metro.issue_free_order(uuid,uuid,text,text) FROM PUBLIC;
