\set ON_ERROR_STOP on
SET search_path = metro, public, pg_catalog;

DO $test$
BEGIN
    BEGIN
        UPDATE metro.trip_capacity_segments
        SET held_count = permitted_capacity + 1
        WHERE id = '73000000-0000-0000-0000-000000000001';
        RAISE EXCEPTION 'Expected capacity CHECK constraint to reject overflow';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        UPDATE metro.payments SET status = 'PENDING'
        WHERE id = '82000000-0000-0000-0000-000000000001';
        RAISE EXCEPTION 'Expected invalid payment transition to fail' USING ERRCODE = 'M0002';
    EXCEPTION WHEN SQLSTATE 'P0001' THEN
        NULL;
    END;

    BEGIN
        INSERT INTO metro.retention_policies (
            operator_id, entity_type, version, action, status, enabled, effective_from
        ) VALUES (
            '10000000-0000-0000-0000-000000000001', 'PAYMENT', 1,
            'DELETE_EVIDENCE', 'DRAFT', false, clock_timestamp());
        RAISE EXCEPTION 'Expected protected retention target to reject physical deletion';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        DELETE FROM metro.payments
        WHERE id = '82000000-0000-0000-0000-000000000001';
        RAISE EXCEPTION 'Expected payment hard delete to fail';
    EXCEPTION WHEN SQLSTATE '55000' THEN
        NULL;
    END;

    BEGIN
        DELETE FROM metro.tickets WHERE operator_id = '10000000-0000-0000-0000-000000000001';
        RAISE EXCEPTION 'Expected ticket hard delete to fail';
    EXCEPTION WHEN SQLSTATE '55000' THEN
        NULL;
    END;
END;
$test$;

SELECT 'constraints_test passed' AS result;
