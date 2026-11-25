-- SRS FR-18.02/03 and section 8.2: shared email-code policy, isolated TicketQrSupport purpose.
ALTER TABLE metro.email_verification_challenges DROP CONSTRAINT email_verification_challenges_purpose_check;
ALTER TABLE metro.email_verification_challenges ADD CONSTRAINT email_verification_challenges_purpose_check
 CHECK(purpose IN ('VERIFY_EMAIL','RESET_PASSWORD','MFA','TICKET_QR_SUPPORT'));
ALTER TABLE metro.email_verification_challenges DROP CONSTRAINT email_verification_challenges_check;
UPDATE metro.email_verification_challenges SET expires_at=created_at+interval '5 minutes';
ALTER TABLE metro.email_verification_challenges ADD CONSTRAINT email_verification_challenges_check
 CHECK(expires_at=created_at+interval '5 minutes');

CREATE TABLE metro.gate_qr_intents (
 operator_id uuid NOT NULL, id uuid NOT NULL, ticket_id uuid NOT NULL,
 actor_user_id text NOT NULL REFERENCES public."AspNetUsers"("Id"), session_id uuid NOT NULL REFERENCES metro.auth_sessions(id),
 action text NOT NULL CHECK(action IN ('DISPLAY','REPLACE')), profile_hash char(64) NOT NULL, reason_hash char(64) NOT NULL,
 credential_id uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(operator_id,id), FOREIGN KEY(operator_id,ticket_id) REFERENCES metro.tickets(operator_id,id),
 FOREIGN KEY(operator_id,credential_id) REFERENCES metro.ticket_credentials(operator_id,id)
);
CREATE TABLE metro.gate_qr_challenges (
 operator_id uuid NOT NULL, challenge_id uuid NOT NULL REFERENCES metro.email_verification_challenges(id), intent_id uuid NOT NULL,
 PRIMARY KEY(operator_id,challenge_id), FOREIGN KEY(operator_id,intent_id) REFERENCES metro.gate_qr_intents(operator_id,id)
);
CREATE TABLE metro.gate_qr_requests (
 operator_id uuid NOT NULL REFERENCES metro.operators(id), request_id uuid NOT NULL, operation_id uuid NOT NULL,
 actor_user_id text NOT NULL REFERENCES public."AspNetUsers"("Id"), session_id uuid NOT NULL REFERENCES metro.auth_sessions(id),
 kind text NOT NULL CHECK(kind IN ('CHALLENGE','ACCESS')), status text NOT NULL CHECK(status IN ('QUEUED','READY','DENIED')),
 payload_hash char(64) NOT NULL, response jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(operator_id,request_id)
);
CREATE UNIQUE INDEX uq_qr_operation_ready ON metro.gate_qr_requests(operator_id,operation_id) WHERE kind='ACCESS' AND status='READY';
CREATE TABLE metro.gate_qr_verifications (
 operator_id uuid NOT NULL, request_id uuid NOT NULL, ticket_id uuid NOT NULL, method text NOT NULL CHECK(method IN ('EMAIL','DOCUMENT')),
 challenge_id uuid REFERENCES metro.email_verification_challenges(id), evidence_reference text, reason text NOT NULL,
 before_credential_id uuid NOT NULL, after_credential_id uuid NOT NULL,
 PRIMARY KEY(operator_id,request_id), FOREIGN KEY(operator_id,request_id) REFERENCES metro.gate_qr_requests(operator_id,request_id) DEFERRABLE INITIALLY DEFERRED,
 FOREIGN KEY(operator_id,ticket_id) REFERENCES metro.tickets(operator_id,id),
 FOREIGN KEY(operator_id,before_credential_id) REFERENCES metro.ticket_credentials(operator_id,id),
 FOREIGN KEY(operator_id,after_credential_id) REFERENCES metro.ticket_credentials(operator_id,id),
 CHECK((method='EMAIL' AND challenge_id IS NOT NULL AND evidence_reference IS NULL) OR
       (method='DOCUMENT' AND challenge_id IS NULL AND length(btrim(evidence_reference)) BETWEEN 1 AND 1000))
);
CREATE TABLE metro.gate_email_outbox (
 id uuid PRIMARY KEY, operator_id uuid NOT NULL REFERENCES metro.operators(id),
 challenge_id uuid REFERENCES metro.email_verification_challenges(id), notification_id uuid,
 kind text NOT NULL CHECK(kind IN ('OTP','QR_CHANGED')), protected_payload text,
 status text NOT NULL DEFAULT 'QUEUED' CHECK(status IN ('QUEUED','SENDING','ACCEPTED','FAILED','CANCELLED','EXPIRED')),
 attempts smallint NOT NULL DEFAULT 0, created_at timestamptz NOT NULL DEFAULT clock_timestamp(), next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 expires_at timestamptz, last_error_code varchar(60), accepted_at timestamptz,
 FOREIGN KEY(operator_id,notification_id) REFERENCES metro.notifications(operator_id,id),
 CHECK((kind='OTP' AND challenge_id IS NOT NULL AND expires_at IS NOT NULL AND attempts BETWEEN 0 AND 2)
    OR (kind='QR_CHANGED' AND challenge_id IS NULL AND attempts BETWEEN 0 AND 3))
);
CREATE INDEX ix_gate_email_due ON metro.gate_email_outbox(next_attempt_at) WHERE status='QUEUED';
CREATE TABLE metro.gate_email_attempts (
 delivery_id uuid NOT NULL REFERENCES metro.gate_email_outbox(id), attempt_no smallint NOT NULL,
 status text NOT NULL CHECK(status IN ('ACCEPTED','FAILED')), attempted_at timestamptz NOT NULL DEFAULT clock_timestamp(), error_code varchar(60),
 PRIMARY KEY(delivery_id,attempt_no)
);

CREATE FUNCTION metro.enforce_email_code_policy() RETURNS trigger LANGUAGE plpgsql SET search_path=metro,public,pg_catalog AS $f$
BEGIN
 IF NEW.purpose NOT IN ('VERIFY_EMAIL','RESET_PASSWORD','TICKET_QR_SUPPORT') THEN RETURN NEW; END IF;
 -- Unique immutable account email: count across all purposes/operators/devices under the same user lock.
 PERFORM pg_advisory_xact_lock(hashtextextended(NEW.user_id,35));
 IF EXISTS(SELECT 1 FROM metro.email_verification_challenges c WHERE c.user_id=NEW.user_id
   AND c.purpose IN ('VERIFY_EMAIL','RESET_PASSWORD','TICKET_QR_SUPPORT') AND c.created_at>NEW.created_at-interval '60 seconds') OR
   (SELECT count(*) FROM metro.email_verification_challenges c WHERE c.user_id=NEW.user_id
     AND c.purpose IN ('VERIFY_EMAIL','RESET_PASSWORD','TICKET_QR_SUPPORT') AND c.created_at>NEW.created_at-interval '15 minutes')>=3 THEN
   RAISE EXCEPTION USING ERRCODE='P0001', MESSAGE='EMAIL_CODE_RATE_LIMIT';
 END IF;
 UPDATE metro.email_verification_challenges SET superseded_at=NEW.created_at WHERE user_id=NEW.user_id AND purpose=NEW.purpose
   AND consumed_at IS NULL AND superseded_at IS NULL;
 RETURN NEW;
END $f$;
CREATE TRIGGER trg_shared_email_code_policy BEFORE INSERT ON metro.email_verification_challenges
 FOR EACH ROW EXECUTE FUNCTION metro.enforce_email_code_policy();
CREATE FUNCTION metro.erase_finished_qr_email() RETURNS trigger LANGUAGE plpgsql SET search_path=metro,public,pg_catalog AS $f$
BEGIN
 IF NEW.consumed_at IS NOT NULL OR NEW.superseded_at IS NOT NULL THEN
  UPDATE metro.gate_email_outbox SET protected_payload=NULL,status=CASE WHEN status='ACCEPTED' THEN status ELSE 'CANCELLED' END
   WHERE challenge_id=NEW.id AND protected_payload IS NOT NULL;
 END IF; RETURN NEW;
END $f$;
CREATE TRIGGER trg_erase_finished_qr_email AFTER UPDATE OF consumed_at,superseded_at ON metro.email_verification_challenges
 FOR EACH ROW EXECUTE FUNCTION metro.erase_finished_qr_email();
CREATE TRIGGER trg_qr_intents_immutable BEFORE UPDATE OR DELETE ON metro.gate_qr_intents FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_qr_requests_immutable BEFORE UPDATE OR DELETE ON metro.gate_qr_requests FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_qr_verifications_immutable BEFORE UPDATE OR DELETE ON metro.gate_qr_verifications FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
CREATE TRIGGER trg_gate_email_attempts_immutable BEFORE UPDATE OR DELETE ON metro.gate_email_attempts FOR EACH ROW EXECUTE FUNCTION metro.reject_gate_history_mutation();
