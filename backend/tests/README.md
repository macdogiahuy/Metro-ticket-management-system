# Backend test harness

The backend uses xUnit, `WebApplicationFactory<Program>` and a real PostgreSQL
database. Testcontainers and Docker are not required. Never point integration
tests at a shared or production database.

## Local acceptance run

Create a disposable database whose name starts with `metro_test_`, apply the
official SQL package, and export an Npgsql connection string:

```powershell
$env:METRO_TEST_POSTGRES_DATABASE = 'metro_test_local_yourname'
$env:METRO_TEST_POSTGRES_CONNECTION_STRING = 'Host=localhost;Port=5432;Database=metro_test_local_yourname;Username=metro_test;Password=<local-secret>'
$env:METRO_TEST_POSTGRES_REQUIRED = '1'

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

CI starts PostgreSQL directly on the hosted Ubuntu runner, creates a fresh
`metro_test_` database, applies the same SQL package and runs the same script.
Credentials are generated for one run, masked, and never committed.
