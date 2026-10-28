ALTER TABLE external_access_invites ADD COLUMN scopes_json TEXT;
ALTER TABLE external_access_requests ADD COLUMN scopes_json TEXT;
