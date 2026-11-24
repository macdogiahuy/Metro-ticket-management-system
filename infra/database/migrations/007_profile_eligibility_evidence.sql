-- BE-04: concession evidence requests and private object-storage references.
-- Keep profile verification requests separate from the ticketing-domain
-- eligibility_evidence table created by migration 003.
CREATE TABLE metro.profile_eligibility_requests (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id text NOT NULL REFERENCES public."AspNetUsers" ("Id") ON DELETE RESTRICT,
    category varchar(40) NOT NULL CHECK (category IN ('STUDENT','ELDERLY','DISABILITY','PRIORITY','OTHER')),
    status varchar(20) NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','VERIFIED','REJECTED','REVOKED','EXPIRED')),
    valid_from date,
    valid_to date,
    evidence_object_key text,
    evidence_file_name text,
    evidence_content_type text,
    evidence_size bigint CHECK (evidence_size IS NULL OR evidence_size BETWEEN 1 AND 10485760),
    evidence_sha256 char(64) CHECK (evidence_sha256 IS NULL OR evidence_sha256 ~ '^[0-9a-f]{64}$'),
    decision_reason text,
    decided_by_user_id text REFERENCES public."AspNetUsers" ("Id") ON DELETE RESTRICT,
    decided_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from),
    CHECK ((evidence_object_key IS NULL AND evidence_file_name IS NULL AND evidence_content_type IS NULL AND evidence_size IS NULL AND evidence_sha256 IS NULL)
        OR (evidence_object_key IS NOT NULL AND evidence_file_name IS NOT NULL AND evidence_content_type IS NOT NULL AND evidence_size IS NOT NULL AND evidence_sha256 IS NOT NULL)),
    CHECK ((status = 'PENDING' AND decided_at IS NULL AND decided_by_user_id IS NULL)
        OR (status <> 'PENDING' AND decided_at IS NOT NULL AND decided_by_user_id IS NOT NULL))
);
CREATE INDEX ix_profile_eligibility_requests_user_time ON metro.profile_eligibility_requests (user_id, created_at DESC);
CREATE INDEX ix_profile_eligibility_requests_status ON metro.profile_eligibility_requests (status, created_at) WHERE status = 'PENDING';
COMMENT ON COLUMN metro.profile_eligibility_requests.evidence_object_key IS 'Private object storage key; never store document bytes or signed URLs in the database or audit log.';
