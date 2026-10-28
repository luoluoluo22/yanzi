CREATE TABLE IF NOT EXISTS external_access_invites (
  invite_hash TEXT PRIMARY KEY, user_id TEXT NOT NULL, extension_id TEXT NOT NULL,
  data_key TEXT NOT NULL, access TEXT NOT NULL, created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL, revoked INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS external_access_invites_owner ON external_access_invites(user_id, expires_at);
CREATE TABLE IF NOT EXISTS external_access_requests (
  request_id TEXT PRIMARY KEY, invite_hash TEXT NOT NULL, user_id TEXT NOT NULL,
  extension_id TEXT NOT NULL, data_key TEXT NOT NULL, access TEXT NOT NULL,
  client_name TEXT NOT NULL, user_code TEXT NOT NULL, secret_hash TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending', created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL, last_polled_at INTEGER NOT NULL DEFAULT 0,
  token_expires_at INTEGER, decided_at INTEGER
);
CREATE INDEX IF NOT EXISTS external_access_requests_owner ON external_access_requests(user_id, status, expires_at);
CREATE INDEX IF NOT EXISTS external_access_requests_invite ON external_access_requests(invite_hash, created_at);
