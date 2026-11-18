# BE-02A verification

Run with the repository .NET 9 runtime and unchanged package versions:

```powershell
dotnet build backend/Metro.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test backend/Metro.sln --configuration Release --no-build --no-restore
```

## Dedicated PostgreSQL acceptance run

Provision a **dedicated disposable database** whose name begins with
`metro_test_`. Apply the official SQL migrations 001-006 using the
repository deployment procedure; do not use EF migrations or EnsureCreated.
Do not point these tests at an application/shared database. Do not load the
legacy capacity fixture. Tests create synthetic data in one transaction and
roll it back; they never commit, reset or delete the database.

Set these process environment variables privately (do not paste credentials
into command history or test output):

- `METRO_TEST_POSTGRES_CONNECTION_STRING`: Npgsql connection string to that test database.
- `METRO_TEST_POSTGRES_DATABASE`: exact dedicated database name (second explicit target check).
- `METRO_TEST_POSTGRES_REQUIRED=1`: mandatory acceptance mode; missing configuration fails.

```powershell
dotnet test backend/tests/Metro.IntegrationTests/Metro.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter Category=PostgreSQL
```

Without test configuration, optional PostgreSQL facts have an explicit Skip
reason. A configured connection, wrong target, schema or constraint error
fails; no application connection environment variable is used as fallback.
A skipped run is not PostgreSQL acceptance evidence. Backend CI checks deploy.sql
against migration sources and invokes the same acceptance runner used locally:
`backend/tests/Run-PostgresTests.ps1`. CI starts PostgreSQL directly on the
hosted Ubuntu runner, creates a fresh `metro_test_` database, masks its generated
password, deploys the official SQL package, runs `09_verify.sql`, and sets all
`METRO_TEST_POSTGRES_*` variables including `REQUIRED=1`. It rejects failed or
skipped tests, requires PostgreSQL execution and publishes TRX results. No
Docker/Testcontainers, application database, or fixed password is used.

The live suite covers all 27 mapped core tables plus Identity users, reads
fresh rows after clearing tracking, checks changed fields/JSON/arrays, PK,
unique, source checks, composite operator FK, validity exclusion, operator
filtering, forged attached UPDATE/DELETE, and compares nullable time bounds
and empty date ranges against PostgreSQL. Optional load_profile_id stays NULL
because provisioning a capacity profile is outside this task; its scalar
mapping is checked by the provider metadata tests.

## Operator context integration boundary

IOperatorContext and DbContext are scoped. No existing BE-01 request contract
populates an authorized operator, and BE-03 auth is a later task. Until a
trusted caller authorizes and sets the context, scoped reads return nothing
and scoped writes throw. Do not wire a raw header/query parameter into it.
No cross-operator/admin bypass was introduced. Shared Identity/reference
entities remain shared; query filters do not grant authorization.

## Review boundaries

The entire BE-02A branch exceeds 400 source lines. Suggested stacked PR order
(each PR must build/test on its predecessor, and measure logic before opening):

1. Common date/time rules with domain tests.
2. Identity entities/configurations with the corresponding DbSets/model tests.
3. Network topology/calendar entities/configurations and their model tests.
4. Network timetable/trip entities/configurations and their model tests.
5. Order/ticket domain entities together (their navigations refer to each other).
6. Fare/order/order-version configurations and their DbSets/model tests.
7. Ticket/credential configurations and their DbSets/model tests.
8. Operator isolation (filters, write guard, concurrency predicates) and its tests.
9. Dedicated PostgreSQL acceptance suite and run instructions.

Keep every slice below the measured 400 logic-line limit; combine a tiny slice
only when the result still meets that limit. No endpoint uses these intermediate
mapping slices; deploy the completed stack only. Do not split assertions away
from their implementation to hide change size. These are proposed review
boundaries, not PRs created or reviews obtained. Do not mark Done until runtime/
DB acceptance, PR sizing and required reviews (including M01 approval) are verified.
