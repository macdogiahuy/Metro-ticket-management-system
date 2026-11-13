\set ON_ERROR_STOP on
SET search_path = metro, public, pg_catalog;

-- Regression coverage for the nullable-key, payment-callback ownership, ticket-code,
-- duration, fare-scope and cross-operator findings raised on the deployment PR.
-- Everything runs inside one transaction and is rolled back at the end.
BEGIN;

DO $test$
DECLARE
    v_ticket_1 uuid;
    v_ticket_2 uuid;
    v_issued integer;
    v_codes integer;
    v_before integer;
    v_after integer;
BEGIN
    -- 1. Fare natural key is NULL-safe: two identical scopes with NULL line/stations
    --    must not coexist with different amounts.
    INSERT INTO metro.fare_matrix_entries (
        operator_id, fare_policy_version_id, ticket_product_id, line_id,
        origin_station_id, destination_station_id, payment_channel, eligibility_group, amount_minor
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '71000000-0000-0000-0000-000000000001',
        '70000000-0000-0000-0000-000000000001', NULL, NULL, NULL, 'ANY', 'ORDINARY', 1000
    );
    BEGIN
        INSERT INTO metro.fare_matrix_entries (
            operator_id, fare_policy_version_id, ticket_product_id, line_id,
            origin_station_id, destination_station_id, payment_channel, eligibility_group, amount_minor
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', '71000000-0000-0000-0000-000000000001',
            '70000000-0000-0000-0000-000000000001', NULL, NULL, NULL, 'ANY', 'ORDINARY', 2000
        );
        RAISE EXCEPTION 'Duplicate NULL-scope fare was accepted' USING ERRCODE = 'M0010';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    -- 2. A non-single-trip product cannot omit its duration.
    BEGIN
        INSERT INTO metro.ticket_products (
            operator_id, code, name, product_type, duration_value, duration_unit,
            expiry_rule, created_by_user_id
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', 'BAD-DURATION', 'Bad duration', 'MONTH',
            NULL, 'MONTH', 'ELAPSED_DURATION', 'test-user'
        );
        RAISE EXCEPTION 'Product with a NULL duration was accepted' USING ERRCODE = 'M0011';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    -- 3. Multiple payments may wait for their provider reference at the same time.
    INSERT INTO metro.payments (
        operator_id, order_id, payment_code, provider, provider_payment_reference,
        method, amount_minor, status, idempotency_key
    ) VALUES
        ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001',
         'PAY-CASH-1', 'CASH', NULL, 'CASH', 0, 'PENDING', 'cash-1'),
        ('10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000001',
         'PAY-CASH-2', 'CASH', NULL, 'CASH', 0, 'PENDING', 'cash-2');

    -- 4. A NULL signature result must be rejected, not treated as verified.
    BEGIN
        PERFORM metro.confirm_payment_and_issue_tickets(
            '10000000-0000-0000-0000-000000000001', '82000000-0000-0000-0000-000000000001',
            'null-signature-event', 'null-signature-idem', repeat('a', 64)::char(64),
            30000, 'VND', NULL);
        RAISE EXCEPTION 'NULL signature verification was accepted' USING ERRCODE = 'M0012';
    EXCEPTION WHEN SQLSTATE '28000' THEN
        NULL;
    END;

    -- 5. NULL callback amount/currency must be rejected.
    BEGIN
        PERFORM metro.confirm_payment_and_issue_tickets(
            '10000000-0000-0000-0000-000000000001', '82000000-0000-0000-0000-000000000001',
            'null-amount-event', 'null-amount-idem', repeat('a', 64)::char(64),
            NULL, 'VND', true);
        RAISE EXCEPTION 'NULL received amount was accepted' USING ERRCODE = 'M0013';
    EXCEPTION WHEN SQLSTATE '22023' THEN
        NULL;
    END;

    -- 6. A provider event owned by another payment must abort instead of silently
    --    succeeding the second payment and issuing tickets.
    INSERT INTO metro.payments (
        id, operator_id, order_id, payment_code, provider, provider_payment_reference,
        method, amount_minor, status, idempotency_key
    ) VALUES (
        '82000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
        '80000000-0000-0000-0000-000000000002', 'PAY-B', 'SANDBOX', 'SANDBOX-B',
        'SANDBOX_GATEWAY', 30000, 'PENDING', 'payment-b'
    );
    INSERT INTO metro.payment_events (
        operator_id, payment_id, provider_event_id, idempotency_key, event_type,
        signature_verified, payload_hash, outcome
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '82000000-0000-0000-0000-000000000001',
        'shared-event', 'shared-event-idem', 'PAYMENT_SUCCEEDED', true, repeat('a', 64), 'ACCEPTED'
    );
    BEGIN
        PERFORM metro.confirm_payment_and_issue_tickets(
            '10000000-0000-0000-0000-000000000001', '82000000-0000-0000-0000-000000000002',
            'shared-event', 'other-event-idem', repeat('b', 64)::char(64),
            30000, 'VND', true);
        RAISE EXCEPTION 'Cross-payment event reuse was accepted' USING ERRCODE = 'M0014';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
    IF (SELECT status FROM metro.payments WHERE id = '82000000-0000-0000-0000-000000000002') <> 'PENDING' THEN
        RAISE EXCEPTION 'Payment B changed state after a rejected event claim';
    END IF;
    IF EXISTS (
        SELECT 1 FROM metro.tickets t
        JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
        WHERE oi.order_id = '80000000-0000-0000-0000-000000000002'
    ) THEN
        RAISE EXCEPTION 'Tickets were issued for a payment whose event belonged to another payment';
    END IF;

    -- 7. Ticket codes stay unique when two items of the same order share a UUID prefix.
    INSERT INTO metro.ticket_products (
        id, operator_id, code, name, product_type, expiry_rule, capacity_controlled, created_by_user_id
    ) VALUES (
        '70000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
        'SINGLE-NOCAP', 'No capacity single trip', 'SINGLE_TRIP', 'SINGLE_JOURNEY', false, 'test-user'
    );
    INSERT INTO metro.orders (
        id, operator_id, order_code, channel, payer_user_id, beneficiary_user_id, status,
        subtotal_minor, total_minor, fare_policy_version_id
    ) VALUES (
        '80000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000001',
        'ORDER-F', 'WEB', 'test-user', 'test-user', 'PENDING_PAYMENT', 20000, 20000,
        '71000000-0000-0000-0000-000000000001'
    );
    INSERT INTO metro.order_items (
        id, operator_id, order_id, ticket_product_id, passenger_count, unit_price_minor, line_total_minor
    ) VALUES
        ('8a000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
         '80000000-0000-0000-0000-000000000006', '70000000-0000-0000-0000-000000000002', 1, 10000, 10000),
        ('8a000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
         '80000000-0000-0000-0000-000000000006', '70000000-0000-0000-0000-000000000002', 1, 10000, 10000);
    INSERT INTO metro.payments (
        operator_id, order_id, payment_code, provider, provider_payment_reference,
        method, amount_minor, status, idempotency_key
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-000000000006',
        'PAY-F', 'SANDBOX', 'SANDBOX-F', 'SANDBOX_GATEWAY', 20000, 'PENDING', 'payment-f'
    );
    v_issued := metro.confirm_payment_and_issue_tickets(
        '10000000-0000-0000-0000-000000000001',
        (SELECT id FROM metro.payments WHERE payment_code = 'PAY-F'),
        'event-f', 'event-idem-f', repeat('c', 64)::char(64), 20000, 'VND', true);
    IF v_issued <> 2 THEN
        RAISE EXCEPTION 'Expected 2 issued tickets for order F, got %', v_issued;
    END IF;
    SELECT count(DISTINCT ticket_code), max(length(ticket_code)) INTO v_codes, v_after
    FROM metro.tickets t
    JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
    WHERE oi.order_id = '80000000-0000-0000-0000-000000000006';
    IF (SELECT count(*) FROM metro.tickets t
        JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
        WHERE oi.order_id = '80000000-0000-0000-0000-000000000006') <> 2
       OR v_codes <> 2 OR v_after > 80 THEN
        RAISE EXCEPTION 'Ticket codes are not unique or exceed 80 characters';
    END IF;

    -- 8. Refund transactions and attempts may have several unassigned provider references.
    SELECT t.id INTO v_ticket_1
    FROM metro.tickets t
    JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
    WHERE oi.id = '8a000000-0000-0000-0000-000000000001';
    SELECT t.id INTO v_ticket_2
    FROM metro.tickets t
    JOIN metro.order_items oi ON oi.operator_id = t.operator_id AND oi.id = t.order_item_id
    WHERE oi.id = '8a000000-0000-0000-0000-000000000002';
    INSERT INTO metro.refund_requests (
        id, operator_id, request_code, ticket_id, payment_id, reason_type, requested_amount_minor, status
    ) VALUES
        ('83000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
         'REF-A', v_ticket_1, (SELECT id FROM metro.payments WHERE payment_code = 'PAY-F'),
         'SERVICE_INTERRUPTION', 10000, 'SUBMITTED'),
        ('83000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
         'REF-B', v_ticket_2, (SELECT id FROM metro.payments WHERE payment_code = 'PAY-F'),
         'SERVICE_INTERRUPTION', 10000, 'SUBMITTED');
    INSERT INTO metro.refund_transactions (
        id, operator_id, refund_request_id, payment_id, refund_code, provider, method,
        amount_minor, status, idempotency_key
    ) VALUES
        ('84000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001',
         '83000000-0000-0000-0000-000000000001', (SELECT id FROM metro.payments WHERE payment_code = 'PAY-F'),
         'RF-A', 'SANDBOX', 'SANDBOX_GATEWAY', 10000, 'PENDING', 'refund-a'),
        ('84000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
         '83000000-0000-0000-0000-000000000002', (SELECT id FROM metro.payments WHERE payment_code = 'PAY-F'),
         'RF-B', 'SANDBOX', 'SANDBOX_GATEWAY', 10000, 'PENDING', 'refund-b');
    INSERT INTO metro.refund_attempts (
        operator_id, refund_transaction_id, attempt_no, provider_event_id, request_hash, status
    ) VALUES
        ('10000000-0000-0000-0000-000000000001', '84000000-0000-0000-0000-000000000001',
         1, NULL, repeat('d', 64), 'SENT'),
        ('10000000-0000-0000-0000-000000000001', '84000000-0000-0000-0000-000000000002',
         1, NULL, repeat('e', 64), 'TIMEOUT');

    -- 9. A replacement credential cannot point at another operator's credential.
    INSERT INTO metro.operators (id, code, name) VALUES
        ('10000000-0000-0000-0000-000000000002', 'OP2', 'Second operator');
    INSERT INTO metro.ticket_products (
        id, operator_id, code, name, product_type, expiry_rule, capacity_controlled, created_by_user_id
    ) VALUES (
        '70000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000002',
        'OP2-SINGLE', 'Operator 2 single trip', 'SINGLE_TRIP', 'SINGLE_JOURNEY', false, 'test-user'
    );
    INSERT INTO metro.fare_policy_versions (
        id, operator_id, code, version, scope_key, effective_from, created_by_user_id
    ) VALUES (
        '71000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002',
        'OP2-FARE', 1, 'OP2-SCOPE', clock_timestamp(), 'test-user'
    );
    INSERT INTO metro.orders (
        id, operator_id, order_code, channel, status, subtotal_minor, total_minor, fare_policy_version_id
    ) VALUES (
        '80000000-0000-0000-0000-000000000007', '10000000-0000-0000-0000-000000000002',
        'ORDER-G', 'ADMIN', 'DRAFT', 0, 0, '71000000-0000-0000-0000-000000000002'
    );
    INSERT INTO metro.order_items (
        id, operator_id, order_id, ticket_product_id, passenger_count, unit_price_minor, line_total_minor
    ) VALUES (
        '8b000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002',
        '80000000-0000-0000-0000-000000000007', '70000000-0000-0000-0000-000000000003', 1, 0, 0
    );
    INSERT INTO metro.tickets (
        id, operator_id, ticket_code, order_item_id, ticket_product_id, sequence_no,
        status, fare_policy_version_id
    ) VALUES (
        '85000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002',
        'OP2-TICKET-1', '8b000000-0000-0000-0000-000000000001',
        '70000000-0000-0000-0000-000000000003', 1, 'ISSUED',
        '71000000-0000-0000-0000-000000000002'
    );
    INSERT INTO metro.ticket_credentials (
        id, operator_id, ticket_id, credential_type, token_hash
    ) VALUES (
        '86000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002',
        '85000000-0000-0000-0000-000000000001', 'QR', repeat('a', 64)
    );
    BEGIN
        INSERT INTO metro.ticket_credentials (
            operator_id, ticket_id, credential_type, token_hash, replaced_by_id
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', v_ticket_1, 'QR', repeat('f', 64),
            '86000000-0000-0000-0000-000000000001'
        );
        RAISE EXCEPTION 'Cross-operator replacement credential was accepted' USING ERRCODE = 'M0015';
    EXCEPTION WHEN foreign_key_violation THEN
        NULL;
    END;
    BEGIN
        INSERT INTO metro.ticket_credentials (
            id, operator_id, ticket_id, credential_type, token_hash, replaced_by_id
        ) VALUES (
            '86000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001',
            v_ticket_1, 'QR', repeat('b', 64), '86000000-0000-0000-0000-000000000002'
        );
        RAISE EXCEPTION 'Self-referencing replacement credential was accepted' USING ERRCODE = 'M0016';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END;
$test$;

ROLLBACK;

SELECT 'review_regressions_test passed' AS result;
