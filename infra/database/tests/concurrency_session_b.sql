\set ON_ERROR_STOP on
SELECT pg_sleep(0.25);
SELECT metro.hold_trip_capacity(
    '10000000-0000-0000-0000-000000000001',
    '81000000-0000-0000-0000-000000000004',
    'concurrent-hold-d', clock_timestamp() + interval '15 minutes');
