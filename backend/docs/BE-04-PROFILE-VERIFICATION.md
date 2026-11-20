# BE-04 Profile and concession verification

Implements UC05/UC06 from SRS v1.2. Authenticate through `/api/v1/auth/login`
and send `Authorization: Bearer <accessToken>` for every route below.

## Profile

`GET /api/v1/me` returns name, birth date, email, phone, locale, notification
preference, masked identity/bank values and `identityFieldsLocked`.
`PUT /api/v1/me` accepts:

```json
{
  "fullName": "Passenger name",
  "birthDate": "2000-01-01",
  "phoneNumber": "+84912345678",
  "locale": "vi-VN",
  "emailNotifications": true
}
```

Birth date and phone are optional. Email cannot be changed. Name and birth date
are locked after citizen identity verification or any historical VERIFIED
concession decision, including after revocation or expiry. A no-op update creates
no extra audit entry. Profile changes and their audit commit together.

## Passenger concession requests

- `POST /api/v1/me/eligibility`: `{ "category": "STUDENT", "validFrom": "2026-11-20", "validTo": "2027-05-20" }`.
  Categories are STUDENT and ELDERLY. Dates describe the supporting document's
  validity; an approval cannot extend past its end. Only one pending request per
  passenger/category is accepted. A rejected/expired request may be followed by a
  new request; the original version and decisions remain available.
- `GET /api/v1/me/eligibility` and `GET /api/v1/me/eligibility/{id}` show owned
  requests, attachment metadata and decision history.
- `POST /api/v1/me/eligibility/{id}/evidence`: multipart `file` or `files`
  fields, one to three JPG/PNG/PDF files, each at most 5 MiB. The total per request
  is three current files. MIME, extension and file signatures are checked before any storage write.
  Files append rather than replace; old references are preserved.
  To correct a file (including when all three slots are filled), upload one file
  with `?replaceEvidenceId={attachmentId}`. The previous version is marked
  `replacedAt`; its metadata and private content remain until the retention purge.
- `GET /api/v1/me/eligibility/{id}/evidence?evidenceId={attachmentId}` streams
  a private attachment. Without `evidenceId`, the latest attachment is selected.
  Other passengers receive 404. No storage credentials, object keys or signed
  public URLs appear in responses or audit entries.

## Administrator review

Only the Admin role can access these routes; StationStaff cannot review.

- `GET /api/v1/eligibility?status=PENDING&page=1&pageSize=20`: review queue;
  default includes PENDING and NEEDS_INFO, maximum page size 100.
- `GET /api/v1/eligibility/{id}`: request, attachments and decision history.
- `GET /api/v1/eligibility/{id}/evidence?evidenceId={attachmentId}`: audited
  private evidence download.
- `POST /api/v1/eligibility/{id}/decision`: `decision`, required `reason`,
  optional `validFrom`, `validTo`, and `documentBirthDate`.
- `PUT /api/v1/eligibility/{id}/hold`: `{ "hold": true, "reason": "Open dispute" }`
  prevents automatic evidence deletion until an Admin releases the hold.

PENDING permits NEEDS_INFO, VERIFIED or REJECTED. Uploading additional evidence
for NEEDS_INFO resubmits the same request as PENDING without erasing its previous
decision. VERIFIED permits REJECTED (revocation); legacy input REVOKED also stores
the request as REJECTED while preserving the REVOKED action in its history. Repeated
and invalid transitions return 409. Approvals require evidence and a reason;
self-approval is rejected. Validity is capped at twelve months from approval and
the document end date. For ELDERLY, the reviewer must supply documentBirthDate
matching the profile. Age eligibility is separately calculated at payment/travel
by the ticketing workflow; this API does not grant age eligibility by itself.

Decisions, history, audit, and IN_APP/EMAIL_MOCK notification outbox rows commit
in the same SQL transaction. EMAIL_MOCK is the repository's delivery convention;
no external email service or real email is sent here. Use the existing
notification consumer to deliver/read those rows.

## Expiry and retention

The lifecycle worker runs every minute outside Testing. It marks overdue VERIFIED
requests EXPIRED using the Vietnam calendar day. Once a request has been expired,
rejected or revoked for 30 days, the worker can queue its private evidence for
purge unless `evidence_hold` is active.

The worker writes a purge task to the outbox and detaches live object references
in one database transaction. A rollback preserves the live references and the
files. After commit the worker claims the task with `FOR UPDATE SKIP LOCKED`.
It keeps the task for retry if storage fails and logs only the exception type.
It removes the task and audits completion after the files have been deleted.
Attachment and decision metadata remain for the required minimum 24 months.

## Database deployment and checks

Migration 008 adds attachment and decision history tables and extends the status
and hold fields while preserving existing 007 uploads. Neon production was
verified at migrations 001–009 after the 20 November 2026 deployment. See the
[production deployment record](BE-04-PRODUCTION-007-008.md).

The revised worker requires migration 009 and its
`metro.profile_eligibility_purge_queue` table. Apply 009 to the target database
before starting this version of the API and worker. Keep the checksum of the
deployed 008 migration. If a future migration changes the state checks, refer
to `ck_profile_eligibility_status` and `ck_profile_eligibility_decision` by name.

Never point tests at Neon production. Use an isolated UTF8 PostgreSQL 17+
database whose name begins `metro_test_`:

```powershell
python infra/database/build_deploy.py --check
psql -v ON_ERROR_STOP=1 -d metro_test_be04_local -f infra/database/deploy.sql
psql -v ON_ERROR_STOP=1 -d metro_test_be04_local -f infra/database/scripts/09_verify.sql
dotnet build backend/Metro.sln --configuration Release --warnaserror
# Set METRO_TEST_POSTGRES_* as described in backend/tests/README.md first.
./backend/tests/Run-PostgresTests.ps1 -NoBuild -NoRestore
```

After review, an authorized database owner must apply the deploy package to the
desired Neon branch before running the new API against it. No test or local
development setup command above deploys to Neon automatically.
