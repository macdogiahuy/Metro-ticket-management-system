\set ON_ERROR_STOP on
SET search_path = metro, public, pg_catalog;

DO $test$
DECLARE
    v_hold uuid;
    v_same_hold uuid;
    v_issued integer;
BEGIN
    v_hold := metro.hold_trip_capacity(
        '10000000-0000-0000-0000-000000000001',
        '81000000-0000-0000-0000-000000000001',
        'hold-a', clock_timestamp() + interval '15 minutes');
    v_same_hold := metro.hold_trip_capacity(
        '10000000-0000-0000-0000-000000000001',
        '81000000-0000-0000-0000-000000000001',
        'hold-a', clock_timestamp() + interval '15 minutes');
    IF v_hold <> v_same_hold THEN
        RAISE EXCEPTION 'Capacity hold was not idempotent';
    END IF;

    BEGIN
        PERFORM metro.hold_trip_capacity(
            '10000000-0000-0000-0000-000000000001',
            '81000000-0000-0000-0000-000000000002',
            'hold-b', clock_timestamp() + interval '15 minutes');
        RAISE EXCEPTION 'Expected over-capacity hold to fail' USING ERRCODE = 'M0001';
    EXCEPTION WHEN SQLSTATE 'P0001' THEN
        NULL;
    END;

    UPDATE metro.orders SET status = 'PENDING_PAYMENT'
    WHERE id = '80000000-0000-0000-0000-000000000001';

    v_issued := metro.confirm_payment_and_issue_tickets(
        '10000000-0000-0000-0000-000000000001',
        '82000000-0000-0000-0000-000000000001',
        'provider-event-a', 'provider-event-idem-a', repeat('a',64)::char(64),
        30000, 'VND', true);
    IF v_issued <> 3 THEN
        RAISE EXCEPTION 'Expected 3 issued tickets, got %', v_issued;
    END IF;

    IF metro.confirm_payment_and_issue_tickets(
        '10000000-0000-0000-0000-000000000001',
        '82000000-0000-0000-0000-000000000001',
        'provider-event-a', 'provider-event-idem-a', repeat('a',64)::char(64),
        30000, 'VND', true) <> 3 THEN
        RAISE EXCEPTION 'Duplicate payment callback changed issuance result';
    END IF;

    PERFORM metro.hold_trip_capacity(
        '10000000-0000-0000-0000-000000000001',
        '81000000-0000-0000-0000-000000000005',
        'hold-e', clock_timestamp() + interval '15 minutes');
    PERFORM metro.cancel_unpaid_order(
        '10000000-0000-0000-0000-000000000001',
        '80000000-0000-0000-0000-000000000005',
        'test-user', 'Test cancellation');
END;
$test$;

DO $assert$
BEGIN
    IF EXISTS (
        SELECT 1 FROM metro.trip_capacity_segments
        WHERE planned_trip_id = '60000000-0000-0000-0000-000000000001'
          AND (held_count <> 0 OR confirmed_count <> 3 OR held_count + confirmed_count > permitted_capacity)
    ) THEN
        RAISE EXCEPTION 'Capacity counters are inconsistent after confirmation/cancellation';
    END IF;
    IF (SELECT status FROM metro.payments WHERE id = '82000000-0000-0000-0000-000000000001') <> 'SUCCEEDED' THEN
        RAISE EXCEPTION 'Payment was not confirmed';
    END IF;
    IF (SELECT status FROM metro.orders WHERE id = '80000000-0000-0000-0000-000000000001') <> 'ISSUED' THEN
        RAISE EXCEPTION 'Order was not issued';
    END IF;
    IF (SELECT count(*) FROM metro.tickets t JOIN metro.order_items oi ON oi.id=t.order_item_id
        WHERE oi.order_id='80000000-0000-0000-0000-000000000001') <> 3 THEN
        RAISE EXCEPTION 'Ticket count does not match passenger quantity';
    END IF;
END;
$assert$;

SELECT 'business_rules_test passed' AS result;
