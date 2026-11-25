-- FR-17.05: an administrator can review/close an escalated case without granting passage.
ALTER TABLE metro.gate_resolutions DROP CONSTRAINT gate_resolutions_action_check;
ALTER TABLE metro.gate_resolutions ADD CONSTRAINT gate_resolutions_action_check
 CHECK(action IN ('CLOSE_JOURNEY','SUPPLY_ENTRY_AND_EXIT','UNDO_SCAN','ESCALATE','CLOSE_SUPPORT'));
ALTER TABLE metro.gate_resolutions DROP CONSTRAINT gate_resolutions_check;
ALTER TABLE metro.gate_resolutions ADD CONSTRAINT gate_resolutions_check CHECK(action IN ('ESCALATE','CLOSE_SUPPORT') OR identity_verified);
