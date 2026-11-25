# Backend test harness

The backend uses xUnit, `WebApplicationFactory<Program>` and a real PostgreSQL
database. Testcontainers and Docker are not required. Never point integration
tests at a shared or production database.

## Local acceptance run

BE-14 operations coverage is in `OperationsRulesTests` and
`OperationsEndpointsTests`: auth/operator boundaries, ETA/kg histories,
idempotency/races, disruption publication/recovery, cache refresh, suggestion
fallback and audit-failure rollback. The PostgreSQL acceptance schema includes
migration 014; the runner counts Operations endpoint tests as PostgreSQL evidence.

Create a completely new disposable database for **each acceptance run**, whose
name starts with `metro_test_`. Never reuse a database from an earlier run or
share one between concurrent runs. Apply the official SQL package and export
an Npgsql connection string:

```powershell
$testDatabase = 'metro_test_local_' + [Guid]::NewGuid().ToString('N')
$env:METRO_TEST_POSTGRES_DATABASE = $testDatabase
$env:METRO_TEST_POSTGRES_CONNECTION_STRING = "Host=localhost;Port=5432;Database=$testDatabase;Username=metro_test;Password=<local-secret>"
$env:METRO_TEST_POSTGRES_REQUIRED = '1'

createdb --encoding=UTF8 $env:METRO_TEST_POSTGRES_DATABASE
psql -v ON_ERROR_STOP=1 -d $env:METRO_TEST_POSTGRES_DATABASE -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d $env:METRO_TEST_POSTGRES_DATABASE -f infra/database/scripts/09_verify.sql
pwsh backend/tests/Run-PostgresTests.ps1
```

The runner rejects failed or skipped tests and requires at least one PostgreSQL
integration test. Without configuration, PostgreSQL facts may be skipped for a
quick offline unit-test run; that result is not acceptance evidence.

Review evidence on head `e9ffbbf`: **116/116 unit + 124/124 integration =
240/240 passed, zero failed, zero skipped** on a completely fresh dedicated
database. An initial full run on a previously shared database failed
`Suggestion_uses_published_ETA_preserves_history_and_stops_after_entry`; the
test passed alone and the full suite passed on a clean database. The review
did not identify a code defect, but this signals a risk of shared test state.
Use a fresh database even when rerunning after a failure. The runner's
`metro_test_` name guard does not establish that a database is new or exclusive.

CI starts PostgreSQL directly on the hosted Ubuntu runner, creates a fresh
`metro_test_` database, applies the same SQL package and runs the same script.
Credentials are generated for one run, masked, and never committed.
