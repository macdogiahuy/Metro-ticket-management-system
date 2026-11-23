-- UC05/UC06: preserve migration 007 and extend its profile-specific workflow.
ALTER TABLE metro.profile_eligibility_requests ADD COLUMN evidence_hold boolean NOT NULL DEFAULT false;

-- Replace only the state/decision checks; preserve file and date integrity checks.
DO $checks$
DECLARE item record;
BEGIN
    FOR item IN SELECT conname FROM pg_constraint
        WHERE conrelid = 'metro.profile_eligibility_requests'::regclass AND contype = 'c'
          AND pg_get_constraintdef(oid) LIKE '%status%'
    LOOP
        EXECUTE format('ALTER TABLE metro.profile_eligibility_requests DROP CONSTRAINT %I', item.conname);
    END LOOP;
END $checks$;
ALTER TABLE metro.profile_eligibility_requests
    ADD CONSTRAINT ck_profile_eligibility_status CHECK (status IN ('PENDING','NEEDS_INFO','VERIFIED','REJECTED','REVOKED','EXPIRED')),
    ADD CONSTRAINT ck_profile_eligibility_decision CHECK (
        (status = 'PENDING' AND decided_at IS NULL AND decided_by_user_id IS NULL)
        OR (status <> 'PENDING' AND decided_at IS NOT NULL AND decided_by_user_id IS NOT NULL));

CREATE TABLE metro.profile_eligibility_attachments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id uuid NOT NULL REFERENCES metro.profile_eligibility_requests(id) ON DELETE RESTRICT,
    slot integer NOT NULL CHECK (slot BETWEEN 1 AND 3),
    object_key text, file_name text NOT NULL, content_type text NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes BETWEEN 1 AND 10485760),
    sha256 char(64) NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    uploaded_at timestamptz NOT NULL DEFAULT clock_timestamp(), purged_at timestamptz, replaced_at timestamptz,
    CHECK ((object_key IS NOT NULL AND purged_at IS NULL) OR (object_key IS NULL AND purged_at IS NOT NULL))
);
CREATE UNIQUE INDEX ix_profile_eligibility_attachment_slots ON metro.profile_eligibility_attachments(request_id, slot) WHERE replaced_at IS NULL;
-- Preserve already uploaded 007 documents. New uploads are capped at 5 MiB in the API.
INSERT INTO metro.profile_eligibility_attachments
    (request_id, slot, object_key, file_name, content_type, size_bytes, sha256, uploaded_at)
SELECT id, 1, evidence_object_key, evidence_file_name, evidence_content_type,
       evidence_size, evidence_sha256, updated_at
FROM metro.profile_eligibility_requests WHERE evidence_object_key IS NOT NULL;

CREATE TABLE metro.profile_eligibility_decisions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id uuid NOT NULL REFERENCES metro.profile_eligibility_requests(id) ON DELETE RESTRICT,
    status varchar(20) NOT NULL, reason text NOT NULL,
    actor_user_id text REFERENCES public."AspNetUsers"("Id") ON DELETE RESTRICT,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(), valid_from date, valid_to date
);
INSERT INTO metro.profile_eligibility_decisions
    (request_id, status, reason, actor_user_id, occurred_at, valid_from, valid_to)
SELECT id, status, coalesce(decision_reason, 'Migrated decision'), decided_by_user_id,
       decided_at, valid_from, valid_to
FROM metro.profile_eligibility_requests WHERE decided_at IS NOT NULL;
CREATE INDEX ix_profile_eligibility_decisions_history ON metro.profile_eligibility_decisions(request_id, occurred_at);
COMMENT ON TABLE metro.profile_eligibility_decisions IS 'Retain decision metadata for at least 24 months; no document contents.';
