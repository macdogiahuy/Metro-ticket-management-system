\set ON_ERROR_STOP on
BEGIN;
SELECT metro.hold_trip_capacity(
    '10000000-0000-0000-0000-000000000001',
    '81000000-0000-0000-0000-000000000003',
    'concurrent-hold-c', clock_timestamp() + interval '15 minutes');
SELECT pg_sleep(2);
COMMIT;
