\set ON_ERROR_STOP on
-- Deterministic local/demo data. Requires infra/database/deploy.sql first.
-- Demo accounts use the documented password `MetroDemo2026!`; the stored value is
-- an ASP.NET Core Identity v3 PBKDF2 hash (never a plain-text password).
BEGIN;
SET LOCAL search_path = metro, public, pg_catalog;

INSERT INTO public."AspNetRoles" ("Id", "Name", "NormalizedName") VALUES
    ('demo-role-passenger', 'Passenger', 'PASSENGER'),
    ('demo-role-station-staff', 'StationStaff', 'STATIONSTAFF'),
    ('demo-role-admin', 'Admin', 'ADMIN')
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO public."AspNetUsers" (
    "Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
    "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
    "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount"
) VALUES
    ('demo-passenger', 'demo-passenger', 'DEMO-PASSENGER', 'passenger@example.invalid', 'PASSENGER@EXAMPLE.INVALID', true, 'AQAAAAEAAYagAAAAEAABAgMEBQYHCAkKCwwNDg/EfT/zR0RYmdpLhPYyQArKU4iI/ucErDNYYCaIlKL/3g==', 'DEMO-PASSENGER-SEED', 'DEMO-PASSENGER-SEED', false, false, false, 0),
    ('demo-staff', 'demo-staff', 'DEMO-STAFF', 'station-staff@example.invalid', 'STATION-STAFF@EXAMPLE.INVALID', true, 'AQAAAAEAAYagAAAAEAABAgMEBQYHCAkKCwwNDg/EfT/zR0RYmdpLhPYyQArKU4iI/ucErDNYYCaIlKL/3g==', 'DEMO-STAFF-SEED', 'DEMO-STAFF-SEED', false, false, false, 0),
    ('demo-admin', 'demo-admin', 'DEMO-ADMIN', 'admin@example.invalid', 'ADMIN@EXAMPLE.INVALID', true, 'AQAAAAEAAYagAAAAEAABAgMEBQYHCAkKCwwNDg/EfT/zR0RYmdpLhPYyQArKU4iI/ucErDNYYCaIlKL/3g==', 'DEMO-ADMIN-SEED', 'DEMO-ADMIN-SEED', false, false, false, 0)
ON CONFLICT ("Id") DO UPDATE SET "PasswordHash" = EXCLUDED."PasswordHash";

INSERT INTO public."AspNetUserRoles" ("UserId", "RoleId") VALUES
    ('demo-passenger', 'demo-role-passenger'),
    ('demo-staff', 'demo-role-station-staff'),
    ('demo-admin', 'demo-role-admin')
ON CONFLICT DO NOTHING;

-- Profiles are required by GET /api/v1/me (UC05/UC06). Without these rows the
-- demo accounts can log in but their profile endpoints return 404.
INSERT INTO metro.user_profiles (user_id, full_name, normalized_full_name, birth_date, status) VALUES
    ('demo-passenger', 'Demo Passenger', 'DEMO PASSENGER', DATE '1990-01-01', 'ACTIVE'),
    ('demo-staff', 'Demo Station Staff', 'DEMO STATION STAFF', DATE '1990-01-01', 'ACTIVE'),
    ('demo-admin', 'Demo Administrator', 'DEMO ADMINISTRATOR', DATE '1990-01-01', 'ACTIVE')
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO metro.operators (id, code, name) VALUES
    ('10000000-0000-0000-0000-000000000018', 'DEMO-METRO', 'Metro Demo Operator')
ON CONFLICT (code) DO NOTHING;

INSERT INTO metro.lines (id, operator_id, code, name, created_by_user_id) VALUES
    ('20000000-0000-0000-0000-000000000018', '10000000-0000-0000-0000-000000000018', 'DEMO-L1', 'Cát Linh - Hà Đông', 'demo-admin'),
    ('20000000-0000-0000-0000-000000000019', '10000000-0000-0000-0000-000000000018', 'DEMO-L2', 'Demo Line 2', 'demo-admin')
ON CONFLICT (operator_id, code) DO UPDATE SET name = EXCLUDED.name;

INSERT INTO metro.stations (id, operator_id, code, name, station_type, created_by_user_id) VALUES
    ('30000000-0000-0000-0000-000000000018', '10000000-0000-0000-0000-000000000018', 'DEMO-L1-S01', 'Cát Linh', 'ELEVATED', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000019', '10000000-0000-0000-0000-000000000018', 'DEMO-L1-S02', 'La Thanh', 'ELEVATED', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000020', '10000000-0000-0000-0000-000000000018', 'DEMO-L1-S03', 'Thai Ha', 'ELEVATED', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000021', '10000000-0000-0000-0000-000000000018', 'DEMO-L1-S04', 'Láng', 'ELEVATED', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000022', '10000000-0000-0000-0000-000000000018', 'DEMO-L1-S05', 'Vành đai 3', 'ELEVATED', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000023', '10000000-0000-0000-0000-000000000018', 'DEMO-L1-S06', 'Yên Nghĩa', 'ELEVATED', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000024', '10000000-0000-0000-0000-000000000018', 'DEMO-L2-S01', 'Demo Station 2.1', 'UNDERGROUND', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000025', '10000000-0000-0000-0000-000000000018', 'DEMO-L2-S02', 'Demo Station 2.2', 'UNDERGROUND', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000026', '10000000-0000-0000-0000-000000000018', 'DEMO-L2-S03', 'Demo Station 2.3', 'UNDERGROUND', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000027', '10000000-0000-0000-0000-000000000018', 'DEMO-L2-S04', 'Demo Station 2.4', 'UNDERGROUND', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000028', '10000000-0000-0000-0000-000000000018', 'DEMO-L2-S05', 'Demo Station 2.5', 'UNDERGROUND', 'demo-admin'),
    ('30000000-0000-0000-0000-000000000029', '10000000-0000-0000-0000-000000000018', 'DEMO-L2-S06', 'Demo Station 2.6', 'UNDERGROUND', 'demo-admin')
ON CONFLICT (operator_id, code) DO UPDATE SET name = EXCLUDED.name;

INSERT INTO metro.line_stations (operator_id, line_id, station_id, direction, sequence_no, distance_from_origin_m)
SELECT '10000000-0000-0000-0000-000000000018', l.id, s.id, 0, right(s.code, 2)::smallint,
       (right(s.code, 2)::integer - 1) * 1800
FROM metro.lines l JOIN metro.stations s ON s.operator_id = l.operator_id
WHERE (l.code = 'DEMO-L1' AND s.code LIKE 'DEMO-L1-S%')
   OR (l.code = 'DEMO-L2' AND s.code LIKE 'DEMO-L2-S%')
ON CONFLICT (operator_id, line_id, direction, sequence_no) DO NOTHING;

INSERT INTO metro.service_calendars (id, operator_id, code, name, day_type, days_of_week, effective_from, status, created_by_user_id) VALUES
    ('40000000-0000-0000-0000-000000000018', '10000000-0000-0000-0000-000000000018', 'DEMO-WEEKDAY', 'Demo weekday service', 'WEEKDAY', ARRAY[1,2,3,4,5]::smallint[], DATE '2026-01-01', 'DRAFT', 'demo-admin')
ON CONFLICT (operator_id, code) DO NOTHING;

INSERT INTO metro.schedule_versions (id, operator_id, line_id, service_calendar_id, version, code, status, effective_from, created_by_user_id)
SELECT CASE l.code WHEN 'DEMO-L1' THEN '50000000-0000-0000-0000-000000000018'::uuid ELSE '50000000-0000-0000-0000-000000000019'::uuid END,
       l.operator_id, l.id, c.id, 1, l.code || '-SCHEDULE-V1', 'DRAFT', TIMESTAMPTZ '2026-01-01 00:00:00+07', 'demo-admin'
FROM metro.lines l
CROSS JOIN metro.service_calendars c
WHERE l.code IN ('DEMO-L1','DEMO-L2') AND c.operator_id = l.operator_id AND c.code = 'DEMO-WEEKDAY'
ON CONFLICT (operator_id, code) DO NOTHING;

INSERT INTO metro.planned_trips (id, operator_id, schedule_version_id, line_id, trip_code, service_date, direction, planned_start_at, planned_end_at, status)
SELECT CASE l.code || t.trip WHEN 'DEMO-L1A' THEN '60000000-0000-0000-0000-000000000018'::uuid WHEN 'DEMO-L1B' THEN '60000000-0000-0000-0000-000000000019'::uuid WHEN 'DEMO-L2A' THEN '60000000-0000-0000-0000-000000000020'::uuid ELSE '60000000-0000-0000-0000-000000000021'::uuid END,
       l.operator_id, sv.id, l.id, l.code || '-' || t.trip, DATE '2026-11-18', 0,
       TIMESTAMPTZ '2026-11-18 08:00:00+07' + t.start_offset, TIMESTAMPTZ '2026-11-18 09:00:00+07' + t.start_offset, 'READY'
FROM metro.lines l JOIN metro.schedule_versions sv ON sv.operator_id = l.operator_id AND sv.line_id = l.id AND sv.version = 1
CROSS JOIN (VALUES ('A', interval '0 minutes'), ('B', interval '30 minutes')) AS t(trip, start_offset)
WHERE l.code IN ('DEMO-L1','DEMO-L2')
ON CONFLICT (operator_id, service_date, trip_code) DO NOTHING;

INSERT INTO metro.planned_stop_times (operator_id, planned_trip_id, station_id, sequence_no, planned_arrival_at, planned_departure_at)
SELECT pt.operator_id, pt.id, s.id, ls.sequence_no,
       pt.planned_start_at + (ls.sequence_no - 1) * interval '10 minutes',
       pt.planned_start_at + (ls.sequence_no - 1) * interval '10 minutes' + interval '1 minute'
FROM metro.planned_trips pt JOIN metro.lines l ON l.operator_id = pt.operator_id AND l.id = pt.line_id
JOIN metro.line_stations ls ON ls.operator_id = l.operator_id AND ls.line_id = l.id AND ls.direction = pt.direction
JOIN metro.stations s ON s.operator_id = ls.operator_id AND s.id = ls.station_id
WHERE pt.trip_code IN ('DEMO-L1-A','DEMO-L1-B','DEMO-L2-A','DEMO-L2-B')
ON CONFLICT (operator_id, planned_trip_id, sequence_no) DO NOTHING;

INSERT INTO metro.ticket_products (id, operator_id, code, name, product_type, expiry_rule, created_by_user_id) VALUES
    ('70000000-0000-0000-0000-000000000018', '10000000-0000-0000-0000-000000000018', 'DEMO-SINGLE', 'Demo single journey', 'SINGLE_TRIP', 'SINGLE_JOURNEY', 'demo-admin')
ON CONFLICT (operator_id, code) DO NOTHING;

INSERT INTO metro.fare_policy_versions (id, operator_id, code, version, scope_key, status, currency, effective_from, created_by_user_id) VALUES
    ('71000000-0000-0000-0000-000000000018', '10000000-0000-0000-0000-000000000018', 'DEMO-FARE', 1, 'DEMO-NETWORK', 'DRAFT', 'VND', TIMESTAMPTZ '2026-01-01 00:00:00+07', 'demo-admin')
ON CONFLICT (operator_id, code) DO NOTHING;

INSERT INTO metro.fare_matrix_entries (operator_id, fare_policy_version_id, ticket_product_id, line_id, origin_station_id, destination_station_id, payment_channel, eligibility_group, amount_minor)
SELECT '10000000-0000-0000-0000-000000000018', fp.id, tp.id, l.id, o.id, d.id, 'ANY', 'ORDINARY', 10000 + (ds.sequence_no - os.sequence_no) * 5000
FROM metro.fare_policy_versions fp JOIN metro.ticket_products tp ON tp.operator_id = fp.operator_id AND tp.code = 'DEMO-SINGLE'
JOIN metro.lines l ON l.operator_id = fp.operator_id AND l.code IN ('DEMO-L1','DEMO-L2')
JOIN metro.line_stations os ON os.operator_id = l.operator_id AND os.line_id = l.id AND os.direction = 0
JOIN metro.line_stations ds ON ds.operator_id = l.operator_id AND ds.line_id = l.id AND ds.direction = os.direction AND ds.sequence_no > os.sequence_no
JOIN metro.stations o ON o.operator_id = os.operator_id AND o.id = os.station_id
JOIN metro.stations d ON d.operator_id = ds.operator_id AND d.id = ds.station_id
WHERE fp.code = 'DEMO-FARE'
ON CONFLICT DO NOTHING;

INSERT INTO metro.operator_accounts (id, operator_id, user_id, account_type, status, mfa_required) VALUES
    ('90000000-0000-0000-0000-000000000018', '10000000-0000-0000-0000-000000000018', 'demo-passenger', 'PASSENGER', 'ACTIVE', false),
    ('90000000-0000-0000-0000-000000000019', '10000000-0000-0000-0000-000000000018', 'demo-staff', 'STAFF', 'ACTIVE', true),
    ('90000000-0000-0000-0000-000000000020', '10000000-0000-0000-0000-000000000018', 'demo-admin', 'ADMIN', 'ACTIVE', true)
ON CONFLICT (operator_id, user_id) DO NOTHING;

INSERT INTO metro.business_roles (code, name, actor_code, is_privileged) VALUES
    ('DEMO_PASSENGER', 'Demo Passenger', 'PASSENGER', false),
    ('DEMO_STATION_STAFF', 'Demo Station Staff', 'STAFF', false),
    ('DEMO_ADMIN', 'Demo Administrator', 'ADMIN', true)
ON CONFLICT (code) DO NOTHING;

INSERT INTO metro.account_role_assignments (operator_id, account_id, role_code, valid_from, granted_by_user_id, reason)
SELECT oa.operator_id, oa.id, r.role_code, TIMESTAMPTZ '2026-01-01 00:00:00+07', 'demo-admin', 'Deterministic local demo seed'
FROM (VALUES ('demo-passenger', 'DEMO_PASSENGER'), ('demo-staff', 'DEMO_STATION_STAFF'), ('demo-admin', 'DEMO_ADMIN')) AS r(user_id, role_code)
JOIN metro.operator_accounts oa ON oa.operator_id = '10000000-0000-0000-0000-000000000018' AND oa.user_id = r.user_id
ON CONFLICT DO NOTHING;

COMMIT;
