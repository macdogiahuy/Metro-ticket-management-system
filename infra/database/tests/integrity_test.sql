\set ON_ERROR_STOP on
SET search_path = metro, public, pg_catalog;

DO $test$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM metro.trip_capacity_segments s
        WHERE s.held_count <> COALESCE((
            SELECT sum(hs.passenger_count)
            FROM metro.capacity_hold_segments hs
            JOIN metro.capacity_holds h
              ON h.operator_id = hs.operator_id AND h.id = hs.capacity_hold_id
            WHERE hs.operator_id = s.operator_id AND hs.capacity_segment_id = s.id AND h.status = 'HELD'
        ), 0)
        OR s.confirmed_count <> COALESCE((
            SELECT sum(hs.passenger_count)
            FROM metro.capacity_hold_segments hs
            JOIN metro.capacity_holds h
              ON h.operator_id = hs.operator_id AND h.id = hs.capacity_hold_id
            WHERE hs.operator_id = s.operator_id AND hs.capacity_segment_id = s.id AND h.status = 'CONFIRMED'
        ), 0)
    ) THEN
        RAISE EXCEPTION 'Capacity counters do not reconcile to active hold details';
    END IF;

    IF EXISTS (
        SELECT 1 FROM metro.tickets t
        LEFT JOIN metro.order_items oi ON oi.operator_id=t.operator_id AND oi.id=t.order_item_id
        LEFT JOIN metro.orders o ON o.operator_id=oi.operator_id AND o.id=oi.order_id
        WHERE oi.id IS NULL OR o.id IS NULL OR o.status NOT IN ('PAID','ISSUED','REFUND_PENDING','REFUNDED')
    ) THEN
        RAISE EXCEPTION 'Ticket exists without a paid/issued order lineage';
    END IF;

    IF EXISTS (
        SELECT operator_id, order_id
        FROM metro.payments
        WHERE status IN ('SUCCEEDED','PARTIALLY_REFUNDED','REFUNDED')
        GROUP BY operator_id, order_id HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'Order has more than one successful payment';
    END IF;
END;
$test$;

SELECT 'integrity_test passed' AS result;
