CREATE TABLE IF NOT EXISTS mobile_attachments (
  attachment_id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  object_key TEXT NOT NULL UNIQUE,
  file_name TEXT NOT NULL,
  content_type TEXT NOT NULL,
  size INTEGER NOT NULL,
  sha256 TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_mobile_attachments_expiry ON mobile_attachments(expires_at);
CREATE INDEX IF NOT EXISTS idx_mobile_attachments_owner ON mobile_attachments(user_id, attachment_id);
CREATE TABLE IF NOT EXISTS mobile_message_idempotency (
  user_id TEXT NOT NULL,
  client_message_id TEXT NOT NULL,
  message_id TEXT NOT NULL,
  request_hash TEXT NOT NULL,
  PRIMARY KEY(user_id, client_message_id)
);
