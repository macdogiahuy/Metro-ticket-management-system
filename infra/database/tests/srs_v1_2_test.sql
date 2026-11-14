\set ON_ERROR_STOP on
SET search_path = metro, public, pg_catalog;

-- Coverage for the SRS v1.2 alignment migration (006): guardian links, purchase
-- consent, trip suggestions, kg load, ETA, free issuance, ticket change,
-- accessibility capacity, lost and found, data quality and history retention.
-- Everything runs inside one transaction and is rolled back at the end.
BEGIN;

INSERT INTO public."AspNetUsers" (
    "Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed",
    "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount"
) VALUES (
    'test-child', 'test-child', 'TEST-CHILD', 'child@example.invalid',
    'CHILD@EXAMPLE.INVALID', true, false, false, false, 0
);

-- 1. Guardian link: an active relationship requires a verifier and timestamp.
DO $guardian$
BEGIN
    BEGIN
        INSERT INTO metro.guardian_links (
            operator_id, guardian_user_id, child_user_id, relationship, status
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', 'test-user', 'test-child', 'PARENT', 'ACTIVE'
        );
        RAISE EXCEPTION 'Expected an unverified active guardian link to fail';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    INSERT INTO metro.guardian_links (
        id, operator_id, guardian_user_id, child_user_id, relationship, status,
        verified_by_user_id, verified_at, valid_from, valid_to
    ) VALUES (
        'a1000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001', 'test-user', 'test-child', 'PARENT',
        'ACTIVE', 'test-user', clock_timestamp(), DATE '2026-11-01', DATE '2030-01-01'
    );

    BEGIN
        INSERT INTO metro.guardian_links (
            operator_id, guardian_user_id, child_user_id, relationship, status,
            verified_by_user_id, verified_at
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', 'test-user', 'test-child', 'PARENT',
            'ACTIVE', 'test-user', clock_timestamp()
        );
        RAISE EXCEPTION 'Expected a duplicate active guardian pair to fail';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
END;
$guardian$;

-- 2. Order versions, consent and trip suggestion (one current row per version).
INSERT INTO metro.orders (
    id, operator_id, order_code, channel, payer_user_id, beneficiary_user_id, status,
    subtotal_minor, discount_minor, total_minor, fare_policy_version_id, service_date
) VALUES (
    '80000000-0000-0000-0000-0000000000f1', '10000000-0000-0000-0000-000000000001',
    'ORDER-V12', 'WEB', 'test-user', 'test-child', 'DRAFT', 10000, 0, 10000,
    '71000000-0000-0000-0000-000000000001', DATE '2026-11-20'
);

INSERT INTO metro.order_versions (
    operator_id, order_id, version, status, service_date, ticket_product_id,
    origin_station_id, destination_station_id, beneficiary_user_id,
    subtotal_minor, discount_minor, total_minor, fare_policy_version_id,
    created_by_user_id, confirmed_at
) VALUES (
    '10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-0000000000f1', 1,
    'CONFIRMED', DATE '2026-11-20', '70000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000003',
    'test-child', 10000, 0, 10000, '71000000-0000-0000-0000-000000000001',
    'test-user', clock_timestamp()
);

INSERT INTO metro.purchase_consents (
    id, operator_id, order_id, order_version, buyer_user_id, beneficiary_user_id,
    status, expires_at
) VALUES (
    'a2000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-0000000000f1', 1,
    'test-user', 'test-child', 'REQUESTED', clock_timestamp() + interval '24 hours'
);

DO $suggestions$
DECLARE
    v_count integer;
BEGIN
    INSERT INTO metro.trip_suggestions (
        operator_id, order_id, order_version, suggestion_code, beneficiary_user_id,
        service_date, origin_station_id, destination_station_id, line_id, direction,
        suggested_planned_trip_id, suggested_departure_at, status, schedule_version_id
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-0000000000f1', 1,
        'SUG-1', 'test-child', DATE '2026-11-20',
        '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001', 0,
        '60000000-0000-0000-0000-000000000001', TIMESTAMPTZ '2026-11-20 09:00:00+07',
        'SUGGESTED', '50000000-0000-0000-0000-000000000001'
    );
    INSERT INTO metro.trip_suggestions (
        operator_id, order_id, order_version, suggestion_code, beneficiary_user_id,
        service_date, origin_station_id, destination_station_id, line_id, direction,
        suggested_planned_trip_id, suggested_departure_at, status, schedule_version_id
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-0000000000f1', 1,
        'SUG-2', 'test-child', DATE '2026-11-20',
        '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000003',
        '20000000-0000-0000-0000-000000000001', 0,
        '60000000-0000-0000-0000-000000000001', TIMESTAMPTZ '2026-11-20 09:10:00+07',
        'SUGGESTED', '50000000-0000-0000-0000-000000000001'
    );

    SELECT count(*) INTO v_count FROM metro.v_current_trip_suggestion
    WHERE order_id = '80000000-0000-0000-0000-0000000000f1';
    IF v_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one current trip suggestion, got %', v_count;
    END IF;

    BEGIN
        INSERT INTO metro.trip_suggestions (
            operator_id, order_id, order_version, suggestion_code, beneficiary_user_id,
            service_date, origin_station_id, destination_station_id, status, no_trip_reason
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', '80000000-0000-0000-0000-0000000000f1', 1,
            'SUG-3', 'test-child', DATE '2026-11-20',
            '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000003',
            'NO_TRIP', NULL
        );
        RAISE EXCEPTION 'Expected NO_TRIP without a reason to fail';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END;
$suggestions$;

-- 3. Manual kg load and ETA, including view status.
DO $load$
DECLARE
    v_status text;
BEGIN
    INSERT INTO metro.load_observations (
        operator_id, planned_trip_id, capacity_profile_id, observed_load_kg,
        recorded_by_user_id, observed_at
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001',
        '72000000-0000-0000-0000-000000000001', 1250.5, 'test-user', clock_timestamp()
    );

    UPDATE metro.capacity_profiles SET max_load_kg = 2000
    WHERE id = '72000000-0000-0000-0000-000000000001';
    UPDATE metro.planned_trips SET load_profile_id = '72000000-0000-0000-0000-000000000001'
    WHERE id = '60000000-0000-0000-0000-000000000001';

    SELECT load_status INTO v_status FROM metro.v_trip_load_status
    WHERE planned_trip_id = '60000000-0000-0000-0000-000000000001';
    IF v_status <> 'AVAILABLE' THEN
        RAISE EXCEPTION 'Expected AVAILABLE load status, got %', v_status;
    END IF;

    BEGIN
        INSERT INTO metro.load_observations (
            operator_id, planned_trip_id, observed_load_kg, recorded_by_user_id, observed_at
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001',
            -1, 'test-user', clock_timestamp()
        );
        RAISE EXCEPTION 'Expected a negative load to fail';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO metro.load_observations (
            operator_id, planned_trip_id, observed_load_kg, recorded_by_user_id, observed_at
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001',
            1.25, 'test-user', clock_timestamp()
        );
        IF (SELECT observed_load_kg FROM metro.load_observations
            WHERE observed_load_kg = 1.3) IS NULL THEN
            RAISE EXCEPTION 'Expected numeric(8,1) to round to one decimal';
        END IF;
    END;

    INSERT INTO metro.trip_eta_updates (
        operator_id, planned_trip_id, station_id, expected_arrival_at,
        recorded_by_user_id
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001',
        '30000000-0000-0000-0000-000000000001', TIMESTAMPTZ '2026-11-20 09:05:00+07',
        'test-user'
    );
END;
$load$;

-- 4. Free ticket issuance without a payment and the one-passenger guard.
INSERT INTO metro.orders (
    id, operator_id, order_code, channel, payer_user_id, beneficiary_user_id, status,
    subtotal_minor, discount_minor, total_minor, fare_policy_version_id, service_date,
    eligibility_group, age_reference_date
) VALUES (
    '80000000-0000-0000-0000-0000000000f2', '10000000-0000-0000-0000-000000000001',
    'ORDER-FREE', 'WEB', 'test-user', 'test-child', 'DRAFT', 10000, 10000, 0,
    '71000000-0000-0000-0000-000000000001', DATE '2026-11-20', 'CHILD_UNDER_6',
    DATE '2026-11-20'
);

INSERT INTO metro.order_items (
    id, operator_id, order_id, ticket_product_id, planned_trip_id, origin_station_id,
    destination_station_id, passenger_count, unit_price_minor, discount_minor, line_total_minor
) VALUES (
    '81000000-0000-0000-0000-0000000000f2', '10000000-0000-0000-0000-000000000001',
    '80000000-0000-0000-0000-0000000000f2', '70000000-0000-0000-0000-000000000001',
    '60000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000003', 1, 10000, 10000, 0
);

DO $free$
DECLARE
    v_issued integer;
    v_status text;
    v_ticket_id uuid;
BEGIN
    v_issued := metro.issue_free_order(
        '10000000-0000-0000-0000-000000000001',
        '80000000-0000-0000-0000-0000000000f2', 'test-user');

    IF v_issued <> 1 THEN
        RAISE EXCEPTION 'Expected one free ticket, got %', v_issued;
    END IF;

    SELECT status INTO v_status FROM metro.orders
    WHERE id = '80000000-0000-0000-0000-0000000000f2';
    IF v_status <> 'ISSUED' THEN
        RAISE EXCEPTION 'Free order was not issued, status %', v_status;
    END IF;

    IF EXISTS (
        SELECT 1 FROM metro.payments
        WHERE order_id = '80000000-0000-0000-0000-0000000000f2'
    ) THEN
        RAISE EXCEPTION 'Free issuance created a fake payment';
    END IF;

    SELECT id INTO v_ticket_id FROM metro.tickets
    WHERE order_id = '80000000-0000-0000-0000-0000000000f2';
    IF v_ticket_id IS NULL THEN
        RAISE EXCEPTION 'Free ticket was not created';
    END IF;

    -- SRS one-passenger rule applies to non capacity-controlled products.
    BEGIN
        INSERT INTO metro.ticket_products (
            id, operator_id, code, name, product_type, expiry_rule,
            capacity_controlled, created_by_user_id
        ) VALUES (
            '70000000-0000-0000-0000-0000000000f2',
            '10000000-0000-0000-0000-000000000001', 'SRS-SINGLE', 'SRS single',
            'SINGLE_TRIP', 'SINGLE_JOURNEY', false, 'test-user'
        );
        INSERT INTO metro.order_items (
            operator_id, order_id, ticket_product_id, passenger_count,
            unit_price_minor, line_total_minor
        ) VALUES (
            '10000000-0000-0000-0000-000000000001',
            '80000000-0000-0000-0000-0000000000f2',
            '70000000-0000-0000-0000-0000000000f2', 2, 10000, 20000
        );
        RAISE EXCEPTION 'Expected a two-passenger SRS order item to fail';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END;
$free$;

-- 5. Overlapping usable tickets are rejected; non-overlapping tickets pass.
DO $overlap$
DECLARE
    v_ticket_id uuid;
BEGIN
    SELECT id INTO v_ticket_id FROM metro.tickets
    WHERE order_id = '80000000-0000-0000-0000-0000000000f2';

    UPDATE metro.tickets SET status = 'ACTIVE' WHERE id = v_ticket_id;

    BEGIN
        INSERT INTO metro.tickets (
            operator_id, ticket_code, order_id, order_version, order_item_id,
            ticket_product_id, holder_user_id, sequence_no, status,
            fare_policy_version_id, effective_from, expires_at
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', 'TKT-OVERLAP',
            '80000000-0000-0000-0000-0000000000f2', 1,
            '81000000-0000-0000-0000-0000000000f2',
            '70000000-0000-0000-0000-000000000001', 'test-child', 2, 'ACTIVE',
            '71000000-0000-0000-0000-000000000001',
            TIMESTAMPTZ '2026-11-20 05:00:00+07', TIMESTAMPTZ '2026-11-21 05:00:00+07'
        );
        RAISE EXCEPTION 'Expected overlapping active tickets to fail';
    EXCEPTION WHEN exclusion_violation THEN
        NULL;
    END;

    INSERT INTO metro.tickets (
        operator_id, ticket_code, order_id, order_version, order_item_id,
        ticket_product_id, holder_user_id, sequence_no, status,
        fare_policy_version_id, effective_from, expires_at
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', 'TKT-NONOVERLAP',
        '80000000-0000-0000-0000-0000000000f2', 1,
        '81000000-0000-0000-0000-0000000000f2',
        '70000000-0000-0000-0000-000000000001', 'test-child', 3, 'ACTIVE',
        '71000000-0000-0000-0000-000000000001',
        TIMESTAMPTZ '2026-11-22 05:00:00+07', TIMESTAMPTZ '2026-11-23 05:00:00+07'
    );
END;
$overlap$;

-- 6. Disruption change: one change per ticket, QR revoked, history kept.
DO $change$
DECLARE
    v_ticket_id uuid;
    v_request_id uuid;
    v_changes integer;
BEGIN
    UPDATE metro.tickets SET status = 'EXPIRED'
    WHERE order_item_id = '81000000-0000-0000-0000-0000000000f2'
      AND sequence_no = 3;

    SELECT id INTO v_ticket_id FROM metro.tickets
    WHERE order_item_id = '81000000-0000-0000-0000-0000000000f2'
      AND sequence_no = 1;

    INSERT INTO metro.ticket_change_requests (
        id, operator_id, request_code, ticket_id, order_id, requested_by_user_id,
        reason_type, original_service_date, requested_service_date, status,
        decided_by_user_id, decided_at, decision_reason
    ) VALUES (
        'a3000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001', 'CHG-1', v_ticket_id,
        '80000000-0000-0000-0000-0000000000f2', 'test-user', 'SERVICE_DISRUPTION',
        DATE '2026-11-20', DATE '2026-11-21', 'APPROVED', 'test-user',
        clock_timestamp(), 'Published disruption'
    );

    PERFORM metro.apply_ticket_change(
        '10000000-0000-0000-0000-000000000001',
        'a3000000-0000-0000-0000-000000000001', 'test-user');

    SELECT change_count INTO v_changes FROM metro.tickets WHERE id = v_ticket_id;
    IF v_changes <> 1 THEN
        RAISE EXCEPTION 'Expected one applied ticket change, got %', v_changes;
    END IF;

    BEGIN
        INSERT INTO metro.ticket_change_requests (
            id, operator_id, request_code, ticket_id, order_id, requested_by_user_id,
            reason_type, requested_service_date, status, decided_by_user_id,
            decided_at, decision_reason
        ) VALUES (
            'a3000000-0000-0000-0000-000000000002',
            '10000000-0000-0000-0000-000000000001', 'CHG-2', v_ticket_id,
            '80000000-0000-0000-0000-0000000000f2', 'test-user', 'OTHER',
            DATE '2026-11-22', 'APPROVED', 'test-user', clock_timestamp(), 'Second attempt'
        );
        PERFORM metro.apply_ticket_change(
            '10000000-0000-0000-0000-000000000001',
            'a3000000-0000-0000-0000-000000000002', 'test-user');
        RAISE EXCEPTION 'Expected the second ticket change to fail';
    EXCEPTION WHEN SQLSTATE 'P0001' THEN
        NULL;
    END;
END;
$change$;

-- 7. Station support capacity: two slots, atomic confirmation and release.
DO $accessibility$
DECLARE
    v_remaining integer;
    v_request uuid;
    i integer;
BEGIN
    INSERT INTO metro.station_support_capacities (
        id, operator_id, station_id, support_date, slot_start, slot_end, capacity
    ) VALUES (
        'a4000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001',
        DATE '2027-01-05', TIME '09:00', TIME '09:30', 2
    );

    FOR i IN 1..2 LOOP
        INSERT INTO metro.accessibility_requests (
            id, operator_id, request_code, submitted_by_user_id, passenger_user_id,
            station_id, slot_capacity_id, support_date, slot_start, slot_end, support_type
        ) VALUES (
            gen_random_uuid(), '10000000-0000-0000-0000-000000000001',
            'ACC-' || i, 'test-user', 'test-child',
            '30000000-0000-0000-0000-000000000001',
            'a4000000-0000-0000-0000-000000000001', DATE '2027-01-05',
            TIME '09:00', TIME '09:30', 'WHEELCHAIR'
        );
    END LOOP;

    SELECT id INTO v_request FROM metro.accessibility_requests
    WHERE request_code = 'ACC-1';

    v_remaining := metro.confirm_accessibility_request(
        '10000000-0000-0000-0000-000000000001', v_request, 'test-user');
    IF v_remaining <> 1 THEN
        RAISE EXCEPTION 'Expected one remaining slot, got %', v_remaining;
    END IF;

    PERFORM metro.confirm_accessibility_request(
        '10000000-0000-0000-0000-000000000001',
        (SELECT id FROM metro.accessibility_requests WHERE request_code = 'ACC-2'),
        'test-user');

    BEGIN
        INSERT INTO metro.accessibility_requests (
            id, operator_id, request_code, submitted_by_user_id, passenger_user_id,
            station_id, slot_capacity_id, support_date, slot_start, slot_end, support_type
        ) VALUES (
            gen_random_uuid(), '10000000-0000-0000-0000-000000000001', 'ACC-3',
            'test-user', 'test-child', '30000000-0000-0000-0000-000000000001',
            'a4000000-0000-0000-0000-000000000001', DATE '2027-01-05',
            TIME '09:00', TIME '09:30', 'VISUAL'
        );
        PERFORM metro.confirm_accessibility_request(
            '10000000-0000-0000-0000-000000000001',
            (SELECT id FROM metro.accessibility_requests WHERE request_code = 'ACC-3'),
            'test-user');
        RAISE EXCEPTION 'Expected a full support slot to reject a third request';
    EXCEPTION WHEN SQLSTATE 'P0001' THEN
        NULL;
    END;

    PERFORM metro.release_accessibility_capacity(
        '10000000-0000-0000-0000-000000000001', v_request);

    UPDATE metro.accessibility_requests SET status = 'CANCELLED' WHERE id = v_request;

    IF (SELECT used_count FROM metro.station_support_capacities
        WHERE id = 'a4000000-0000-0000-0000-000000000001') <> 1 THEN
        RAISE EXCEPTION 'Released slot did not return capacity';
    END IF;
END;
$accessibility$;

-- 8. Data quality day: one row per operator and date.
DO $quality$
BEGIN
    INSERT INTO metro.data_quality_days (
        operator_id, quality_date, status, entry_count, confirmed_by_user_id
    ) VALUES (
        '10000000-0000-0000-0000-000000000001', DATE '2026-11-18', 'COMPLETE', 0, 'test-user'
    );
    BEGIN
        INSERT INTO metro.data_quality_days (
            operator_id, quality_date, status, entry_count, confirmed_by_user_id
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', DATE '2026-11-18', 'INCOMPLETE', 5, 'test-user'
        );
        RAISE EXCEPTION 'Expected a duplicate quality day to fail';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;
END;
$quality$;

-- 9. Lost and found: overdue keeps the item, it is never deleted.
DO $lost$
DECLARE
    v_overdue integer;
BEGIN
    INSERT INTO metro.found_items (
        id, operator_id, item_code, received_by_user_id, received_at,
        holding_station_id, public_description, status
    ) VALUES (
        'a5000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001', 'FOUND-1', 'test-user',
        clock_timestamp() - interval '40 days', '30000000-0000-0000-0000-000000000001',
        'Black umbrella', 'HELD'
    );

    v_overdue := metro.mark_overdue_lost_items(
        '10000000-0000-0000-0000-000000000001', 30);
    IF v_overdue < 1 THEN
        RAISE EXCEPTION 'Expected at least one overdue item, got %', v_overdue;
    END IF;
    IF (SELECT status FROM metro.found_items
        WHERE id = 'a5000000-0000-0000-0000-000000000001') <> 'OVERDUE' THEN
        RAISE EXCEPTION 'Found item did not move to OVERDUE';
    END IF;
END;
$lost$;

-- 10. New history tables reject physical deletion.
DO $retention$
BEGIN
    BEGIN
        DELETE FROM metro.trip_suggestions
        WHERE order_id = '80000000-0000-0000-0000-0000000000f1';
        RAISE EXCEPTION 'Expected trip suggestion deletion to be blocked';
    EXCEPTION WHEN SQLSTATE '55000' THEN
        NULL;
    END;

    BEGIN
        DELETE FROM metro.accessibility_request_events;
        RAISE EXCEPTION 'Expected accessibility history deletion to be blocked';
    EXCEPTION WHEN SQLSTATE '55000' THEN
        NULL;
    END;
END;
$retention$;

ROLLBACK;

SELECT 'srs_v1_2_test passed' AS result;
