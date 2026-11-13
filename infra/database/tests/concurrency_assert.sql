\set ON_ERROR_STOP on
DO $test$
BEGIN
    IF EXISTS (
        SELECT 1 FROM metro.trip_capacity_segments
        WHERE planned_trip_id = '60000000-0000-0000-0000-000000000002'
          AND (held_count <> 4 OR confirmed_count <> 0 OR held_count + confirmed_count > permitted_capacity)
    ) THEN
        RAISE EXCEPTION 'Concurrent capacity result is invalid';
    END IF;
    IF (SELECT count(*) FROM metro.capacity_holds h
        JOIN metro.order_items oi ON oi.operator_id=h.operator_id AND oi.id=h.order_item_id
        WHERE oi.planned_trip_id='60000000-0000-0000-0000-000000000002' AND h.status='HELD') <> 1 THEN
        RAISE EXCEPTION 'Exactly one concurrent hold should succeed';
    END IF;
END;
$test$;
SELECT 'concurrency_test passed' AS result;
