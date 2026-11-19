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

CI starts PostgreSQL directly on the hosted Ubuntu runner, creates a fresh
`metro_test_` database, applies the same SQL package and runs the same script.
Credentials are generated for one run, masked, and never committed.
