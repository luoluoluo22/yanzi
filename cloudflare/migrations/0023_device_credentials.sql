CREATE TABLE device_credentials (
  credential_id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  application_id TEXT NOT NULL,
  scopes_json TEXT NOT NULL,
  targets_json TEXT NOT NULL,
  roots_json TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  revoked_at TEXT,
  created_at TEXT NOT NULL
);
CREATE INDEX device_credentials_owner ON device_credentials(user_id, device_id, expires_at);
