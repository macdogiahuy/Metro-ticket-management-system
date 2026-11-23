-- Durable mark-then-purge: detach evidence references and enqueue in one transaction.
-- Preserve checksums of migrations 001-008, including databases already using 008.
CREATE TABLE metro.profile_eligibility_purge_queue (
    request_id uuid CONSTRAINT pk_profile_eligibility_purge_queue PRIMARY KEY,
    object_keys text[] NOT NULL,
    queued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT fk_profile_eligibility_purge_request FOREIGN KEY (request_id)
        REFERENCES metro.profile_eligibility_requests(id) ON DELETE RESTRICT,
    CONSTRAINT ck_profile_eligibility_purge_keys CHECK (
        cardinality(object_keys) > 0 AND array_position(object_keys, NULL) IS NULL)
);
COMMENT ON TABLE metro.profile_eligibility_purge_queue IS
    'Private cleanup outbox; delete objects only after enqueue commits. Retry until all deletes succeed; never log object_keys.';
