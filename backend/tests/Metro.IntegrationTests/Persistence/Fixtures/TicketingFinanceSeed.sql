-- Test-only data. Each test runs this inside a transaction that is rolled back.
INSERT INTO public."AspNetUsers" (
    "Id", "UserName", "NormalizedUserName", "EmailConfirmed", "PhoneNumberConfirmed",
    "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount"
)
SELECT name, name, upper(name), true, false, false, false, 0
FROM unnest(ARRAY['ef-owner','ef-buyer','ef-child','ef-inspector','ef-admin']) name;

INSERT INTO metro.operators (id, code, name) VALUES
('a0000000-0000-0000-0000-000000000001', 'EF1', 'EF test operator'),
('a0000000-0000-0000-0000-000000000002', 'EF2', 'Other EF test operator');

INSERT INTO metro.banks (code, name) VALUES ('EF-BANK', 'Test bank');
INSERT INTO metro.lines (id, operator_id, code, name, created_by_user_id) VALUES
('a1000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-L1', 'Test line', 'ef-admin');
INSERT INTO metro.stations (id, operator_id, code, name, created_by_user_id) VALUES
('a2000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-S1', 'Origin', 'ef-admin'),
('a2000000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000001', 'EF-S2', 'Destination', 'ef-admin');
INSERT INTO metro.service_calendars (id, operator_id, code, name, day_type, days_of_week, effective_from, created_by_user_id) VALUES
('a3000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-CALENDAR', 'Test calendar', 'WEEKDAY', ARRAY[1,2,3,4,5]::smallint[], '2026-01-01', 'ef-admin');
INSERT INTO metro.schedule_versions (id, operator_id, line_id, service_calendar_id, version, code, effective_from, created_by_user_id) VALUES
('a4000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000001', 'a3000000-0000-0000-0000-000000000001', 1, 'EF-SCHEDULE', '2026-01-01T00:00:00Z', 'ef-admin');
INSERT INTO metro.planned_trips (id, operator_id, schedule_version_id, line_id, trip_code, service_date, direction, planned_start_at, planned_end_at, status) VALUES
('a5000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'a4000000-0000-0000-0000-000000000001', 'a1000000-0000-0000-0000-000000000001', 'EF-TRIP', '2026-11-16', 0, '2026-11-16T01:00:00Z', '2026-11-16T02:00:00Z', 'READY');

INSERT INTO metro.ticket_products (id, operator_id, code, name, product_type, expiry_rule) VALUES
('a6000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-SINGLE', 'Single trip', 'SINGLE_TRIP', 'SINGLE_JOURNEY');
INSERT INTO metro.fare_policy_versions (id, operator_id, code, version, scope_key, effective_from, created_by_user_id) VALUES
('a7000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-FARE', 1, 'EF', '2026-01-01T00:00:00Z', 'ef-admin');
INSERT INTO metro.orders (id, operator_id, order_code, channel, payer_user_id, beneficiary_user_id, subtotal_minor, total_minor, fare_policy_version_id, service_date) VALUES
('a8000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-ORDER', 'WEB', 'ef-buyer', 'ef-owner', 10000, 10000, 'a7000000-0000-0000-0000-000000000001', '2026-11-16');
INSERT INTO metro.order_versions (operator_id, order_id, version, ticket_product_id, beneficiary_user_id, service_date, subtotal_minor, total_minor, fare_policy_version_id, fare_snapshot)
SELECT 'a0000000-0000-0000-0000-000000000001', 'a8000000-0000-0000-0000-000000000001', version,
       'a6000000-0000-0000-0000-000000000001', 'ef-owner', '2026-11-16', 10000, 10000,
       'a7000000-0000-0000-0000-000000000001', '{"version":1}'::jsonb
FROM generate_series(1,2) version;
INSERT INTO metro.order_items (id, operator_id, order_id, ticket_product_id, planned_trip_id, origin_station_id, destination_station_id, unit_price_minor, line_total_minor) VALUES
('a9000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'a8000000-0000-0000-0000-000000000001', 'a6000000-0000-0000-0000-000000000001', 'a5000000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000001', 'a2000000-0000-0000-0000-000000000002', 10000, 10000);
INSERT INTO metro.tickets (id, operator_id, ticket_code, order_item_id, ticket_product_id, sequence_no, fare_policy_version_id) VALUES
('aa000000-0000-0000-0000-000000000001', 'a0000000-0000-0000-0000-000000000001', 'EF-TICKET-1', 'a9000000-0000-0000-0000-000000000001', 'a6000000-0000-0000-0000-000000000001', 1, 'a7000000-0000-0000-0000-000000000001'),
('aa000000-0000-0000-0000-000000000002', 'a0000000-0000-0000-0000-000000000001', 'EF-TICKET-2', 'a9000000-0000-0000-0000-000000000001', 'a6000000-0000-0000-0000-000000000001', 2, 'a7000000-0000-0000-0000-000000000001');
