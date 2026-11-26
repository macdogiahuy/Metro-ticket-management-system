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

BE-10 adds GateRulesTests, GateQrRulesTests and GateEndpointsTests: scan/replay/races,
paired entry/exit, evidence/corrections, Pending recovery, verified QR display/replacement,
shared email quotas, encrypted delivery/retry, administrator support review and audit rollback.
Apply migrations through 017 first: unchanged BE-14 schema prerequisite 014 followed
by BE-10 gate/QR/support migrations 015–017. The 100-request/20-client performance smoke includes
TestServer HTTP/auth, database execution, connection-pool waits and application-to-database
transport latency (including a remote or pooled endpoint). It excludes the camera and
browser-to-API network. It logs loopback/non-loopback transport and five sequential
`SELECT 1` timings without disclosing the hostname or credentials. Those diagnostic
timings are not subtracted from scan P95, and the 1000 ms assertion always applies.
For reproducible local smoke results, use PostgreSQL directly on the same machine,
as CI does. A remote result above 1000 ms remains a failed performance smoke;
functional tests passing alone neither prove the cause nor satisfy NFR-002.
The smoke does not replace the full SRS reference-environment/dataset benchmark.
Email worker is disabled in the factory; tests explicitly process the private queue through
TestGateEmailSender. No production endpoint exposes OTP. See [BE-10 handover](../../docs/handover/BE-10-GATE-VALIDATION.md).

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
