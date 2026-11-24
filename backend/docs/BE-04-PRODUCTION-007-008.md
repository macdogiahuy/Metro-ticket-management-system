# Neon production deployment for BE-04

Project `raspy-butterfly-94762786`, branch `production`
(`br-hidden-grass-b3wdynvr`), database `neondb`. The verified 007/008 release
completed on 20 November 2026 at 22:54:44 (UTC+7).

## Backup and rehearsal

- Backup `backup-be04-pre-007-008-20261120-155017z`
  (`br-hidden-glitter-b3us1uqn`) was forked from production at 22:51:57 on
  20 November 2026 (UTC+7), from parent LSN `0/575E958`. Before and after
  deployment, the backup remained at 001–006. It had 93 `metro` tables and
  matching checked row counts. The branch was retained with no expiry.
- Rehearsal branch `rehearsal-be04-007-008-20261120-155200z`
  (`br-soft-leaf-b3fg1mu6`) was forked from the backup. The scoped package and
  schema checks passed there. Its configured expiry was 21 November 2026,
  22:52 (UTC+7).

## Verification on production

- The direct endpoint matched the production branch in Neon management metadata.
  The scoped package applied 007/008 and preserved the existing ledger entries.
  The ledger went from 001–006 to 001–008. The migration checksums matched the
  current source. A later read-only check reconfirmed both ledger entries and
  all three BE-04 profile tables.
- Columns, checks, foreign keys and indexes matched the rehearsal schema. The
  account and ticket eligibility counts checked before and after were equal.
  There were no existing profile eligibility requests to backfill.
- A read-only Npgsql connection using the backend's `NeonConnectionString`
  accessed the new schema through the pooled endpoint. HTTP smoke tests still
  need a deployed API endpoint and suitable test accounts.

| Migration | Source SHA-256 |
| --- | --- |
| 007 | `b294ace07e2cb3862b7fd423103d781fd64a05b027d471cd078649e63a965767` |
| 008 | `a6f4a8c072a6f9a6f0643755eb28ff583c961c6ed1591aaa2b62ac88d38be34e` |

## Migration 009 production release

Migration 009 was authorized and applied at 23:33:26 on 20 November 2026
(UTC+7). Production moved from 001–008 to 001–009. The checksum recorded in
the ledger matches the source:
`b50b99c95403d4f324e362e46921335281e367ea9758cb8ff9c6889e92c0f12a`.

- Backup `backup-be04-pre-009-20261120-163014z` (`br-noisy-waterfall-b3stx9mu`)
  was forked from production at 23:31:54 (UTC+7), parent LSN `0/592C168`.
  It was verified before and after deployment. It remains ready at schema 008
  with 96 `metro` tables and no expiry.
- Rehearsal branch `br-soft-wave-b3t0pm1f` was forked from that backup and
  upgraded from 008 to 009. Schema verification and row-count checks passed.
- Production `09_verify.sql` passed, and the purge queue columns, primary key,
  foreign key, check constraint and index matched the rehearsal. The queue was
  empty after deployment. Counts in the existing account, evidence, request,
  attachment and decision tables matched before and after.
- A read-only Npgsql smoke test using the pooled backend connection verified
  schema 009 and the runtime privileges for inserting, reading, deleting and
  row-locking queue entries. Backend rollout and HTTP smoke tests are separate.

If SQL fails before commit, the database transaction rolls back the migration
and ledger entry together. After commit, stop the new worker and roll back the
application if needed. Review post-backup writes before any branch restore
and reconcile any already purged files with object storage.
See the [database deployment guide](../../infra/database/documentation/DEPLOYMENT_GUIDE.md#be-04-production-release-gate).
