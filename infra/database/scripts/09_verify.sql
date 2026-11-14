\set ON_ERROR_STOP on
SET search_path = metro, public, pg_catalog;

DO $verify$
DECLARE
    v_missing text;
BEGIN
    IF current_setting('server_version_num')::integer < 170000 THEN
        RAISE EXCEPTION 'PostgreSQL 17 or newer is required';
    END IF;
    IF current_setting('server_encoding') <> 'UTF8' THEN
        RAISE EXCEPTION 'UTF8 server encoding is required';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM metro.schema_migrations WHERE version = 6) THEN
        RAISE EXCEPTION 'Migration 006 is not installed';
    END IF;

    SELECT string_agg(name, ', ') INTO v_missing
    FROM (VALUES
        ('hold_trip_capacity'), ('confirm_capacity_hold'), ('release_capacity_hold'),
        ('expire_capacity_holds'), ('confirm_payment_and_issue_tickets'),
        ('cancel_unpaid_order'), ('revoke_unused_ticket'),
        ('issue_free_order'), ('apply_ticket_change'),
        ('confirm_accessibility_request'), ('release_accessibility_capacity'),
        ('mark_overdue_lost_items'), ('validate_load_profile'),
        ('enforce_single_passenger_item')
    ) required(name)
    WHERE to_regprocedure('metro.' || name || CASE name
        WHEN 'hold_trip_capacity' THEN '(uuid,uuid,character varying,timestamp with time zone)'
        WHEN 'confirm_capacity_hold' THEN '(uuid,uuid)'
        WHEN 'release_capacity_hold' THEN '(uuid,uuid,text,text)'
        WHEN 'expire_capacity_holds' THEN '(integer)'
        WHEN 'confirm_payment_and_issue_tickets' THEN '(uuid,uuid,text,character varying,character,bigint,character,boolean)'
        WHEN 'cancel_unpaid_order' THEN '(uuid,uuid,text,text)'
        WHEN 'revoke_unused_ticket' THEN '(uuid,uuid,text)'
        WHEN 'issue_free_order' THEN '(uuid,uuid,text,text)'
        WHEN 'apply_ticket_change' THEN '(uuid,uuid,text)'
        WHEN 'confirm_accessibility_request' THEN '(uuid,uuid,text)'
        WHEN 'release_accessibility_capacity' THEN '(uuid,uuid)'
        WHEN 'mark_overdue_lost_items' THEN '(uuid,integer)'
        WHEN 'validate_load_profile' THEN '(uuid,uuid)'
        WHEN 'enforce_single_passenger_item' THEN '()'
    END) IS NULL;
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing transactional functions: %', v_missing;
    END IF;

    SELECT string_agg(name, ', ') INTO v_missing
    FROM (VALUES
        ('trip_suggestions'), ('order_versions'), ('purchase_consents'),
        ('guardian_links'), ('load_observations'), ('trip_eta_updates'),
        ('ticket_change_requests'), ('lost_reports'), ('found_items'),
        ('lost_item_claims'), ('lost_item_handovers'),
        ('station_support_capacities'), ('accessibility_requests'),
        ('accessibility_request_events'), ('data_quality_days'),
        ('notification_deliveries')
    ) required(name)
    WHERE to_regclass('metro.' || name) IS NULL;
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing SRS v1.2 tables: %', v_missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE n.nspname = 'metro' AND t.relname = 'tickets'
          AND c.conname = 'ex_tickets_no_overlapping_validity'
    ) THEN
        RAISE EXCEPTION 'Missing ticket validity overlap constraint';
    END IF;

    IF (SELECT count(*) FROM pg_trigger t
        JOIN pg_class c ON c.oid=t.tgrelid
        JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='metro' AND t.tgname LIKE 'trg_%_retain_history' AND NOT t.tgisinternal) <> 25 THEN
        RAISE EXCEPTION 'Expected 25 reconciliation retention triggers';
    END IF;

    IF EXISTS (
        SELECT 1 FROM metro.trip_capacity_segments
        WHERE held_count < 0 OR confirmed_count < 0 OR held_count + confirmed_count > permitted_capacity
    ) THEN
        RAISE EXCEPTION 'Capacity counters violate permitted capacity';
    END IF;

    IF EXISTS (
        SELECT operator_id, order_id FROM metro.payments
        WHERE status IN ('SUCCEEDED','PARTIALLY_REFUNDED','REFUNDED')
        GROUP BY operator_id, order_id HAVING count(*) > 1
    ) THEN
        RAISE EXCEPTION 'More than one successful payment exists for an order';
    END IF;
END;
$verify$;

SELECT version, name, applied_at, applied_by
FROM metro.schema_migrations
ORDER BY version;

SELECT 'database verification passed' AS result;
