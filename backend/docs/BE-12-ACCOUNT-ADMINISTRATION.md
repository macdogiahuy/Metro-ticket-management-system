# BE-12 Account administration and audited passenger access

Implements UC22/UC23 against SRS v1.2, FR-22/23 and BR-008/012/013.
The existing Identity role names remain the API codes: `Admin` means
Administrator and `StationStaff` means Ticket Inspector. Identity roles are
the authentication authority; the operator-specific business-role tables are
not used to introduce station/shift restrictions. Accounts register through
BE-03 and must verify their email before an administrator assigns staff roles.

## API contract

All routes below start with `/api/v1/admin`, require the `Admin` policy,
return `Cache-Control: no-store`, and use existing ProblemDetails errors.
Guest requests receive 401; authenticated Passenger/StationStaff requests
receive 403 and create an `ADMIN_ACCESS_DENIED` audit event.

| Method | Route | Request and result |
| --- | --- | --- |
| GET | `/accounts` | Optional `search` (email/name, literal substring, up to 200 characters), `role`, `status`, `page=1`, `pageSize=20` (1–100). Verified accounts only. Returns `{items,total,page,pageSize}`; each item contains userId, email, fullName, status and roles. Search access is audited without storing the query text. |
| GET | `/roles` | Fixed supported roles and capability descriptions. Passenger, StationStaff and Admin; no arbitrary custom roles or permission editing. |
| PUT | `/accounts/{userId}/roles` | `{ "roles": ["StationStaff"], "reason": "Assigned inspection duties" }`; 204. Passenger is retained automatically. At most one staff role; duplicates, unknown roles and unverified accounts are rejected. |
| PUT | `/accounts/{userId}/status` | `{ "status": "LOCKED", "reason": "Account administration" }`; 204. ACTIVE unlocks; LOCKED locks. CLOSED/DISABLED accounts cannot be reactivated through this API. |
| GET | `/passengers/{userId}?purpose=Support%20case` | Requires a nonblank purpose (up to 1000 characters), returns profile and eligibility metadata. Phone, citizen ID and bank account show only their last four characters. No ciphertext, lookup hash, document contents, filenames, object keys or private download URLs. |
| PUT | `/passengers/{userId}` | `{ "fullName": "Corrected Name", "birthDate": "2000-01-01", "reason": "Correct reviewed identity" }`; 204. Name is trimmed and limited to 200 characters; birth date is checked against the business date in UTC+7 (today through 130 years ago). |

Mutation reasons are mandatory, trimmed and limited to 1000 characters.
Unknown request properties are rejected, including attempts to change email.
Account creation/email verification stays in the existing registration flow.
The administrative profile API does not provide document download/export;
private evidence review stays in the existing BE-04 workflow.

## Consistency and audit

- Role changes and locking revoke **every** refresh session and rotate the
  Identity security stamp. Every bearer request checks its security stamp,
  server-side session, email verification and account status. Old access and
  refresh tokens stop working immediately after commit. Unlocking requires a
  new login; it does not revive old sessions.
- Login/refresh share the per-user PostgreSQL advisory lock used by BE-04
  and account administration. Concurrent token issuance cannot leave an active
  session behind after an administrative lock. Refresh rotation also rechecks
  its session after acquiring the lock.
- A second transaction lock serializes administrative mutations, preserving
  at least one verified, unblocked Admin even for concurrent demotions/locks.
  The acting administrator is checked again inside the transaction. Staff may
  use Passenger features; the Admin policy also grants inspector functionality.
- Role/status changes, session revocation, successful audit and IN_APP plus
  EMAIL_MOCK outbox records commit together. EMAIL_MOCK is not live delivery.
  Exact mutation replays create no extra successful audit/outbox records and
  do not revoke newly issued sessions.
- Viewing a passenger saves `PASSENGER_VIEWED` with actor, target, time and
  purpose **before** returning data. Missing purpose saves a denied access
  event and returns 400. Audit failure prevents disclosure.
- Identity corrections clear citizen-ID verification and move all VERIFIED
  profile eligibility requests to NEEDS_INFO. Their original decision history,
  validity dates and private evidence are retained, with new decision/audit
  entries. BE-04 can accept replacement/resubmitted evidence for review.
- Audit stores changed field names and SHA-256 before/after hashes rather
  than raw profile values. Do not put document contents, credentials or PII
  into free-text reasons/purposes. Existing ticket, order, fare, journey and
  expiry records are not updated by these operations.

No schema migration or new configuration secret is required. Deployments
must have the existing SQL migrations 001–009. Tokens issued before the
session/security-stamp claims were added require a new login after upgrade.

## Verification

Use an isolated PostgreSQL 17+ database named `metro_test_*`, apply
`infra/database/deploy.sql` and `scripts/09_verify.sql`, then run the repository
acceptance harness as described in `backend/tests/README.md`:

```powershell
$env:METRO_TEST_POSTGRES_DATABASE = 'metro_test_be12'
$env:METRO_TEST_POSTGRES_CONNECTION_STRING = '<dedicated test Npgsql connection>'
$env:METRO_TEST_POSTGRES_REQUIRED = '1'
# Restricted Windows sessions may not be allowed to write Windows Event Log.
$env:Logging__EventLog__LogLevel__Default = 'None'
powershell -NoProfile -ExecutionPolicy Bypass -File backend/tests/Run-PostgresTests.ps1 -NoRestore
```

`AdministrationEndpointsTests` exercises access denial/audit, verified search,
pagination/filter validation, supported role rules, all-session revocation,
locking/unlocking, immutable email, masked passenger views, audit failure,
identity correction/review history/idempotency, concurrent last-admin changes,
concurrent lock/login/refresh and transaction rollback. Swagger contracts and
the existing auth/profile/payment suites cover integration compatibility.

Local acceptance on 22 November 2026: the SQL deploy/verification and repository
acceptance runner passed against a fresh isolated PostgreSQL database: **112
passed (40 unit, 72 integration), zero failed, zero skipped**. The BE-12 suite
contains 11 PostgreSQL integration tests, plus the administration Swagger test.

Manual requests are in `docs/postman/Metro.BE12.postman_collection.json`.
Use test accounts, review the target user ID and send mutations individually.
