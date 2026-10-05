-- Reduce D1 rows_read for device message recovery and expiry maintenance.
CREATE INDEX IF NOT EXISTS idx_device_message_sequence_user_cover
  ON device_message_sequence(user_id, sequence, message_id);

-- Pre-cursor clients still need account-chat recovery; keep the JSON predicate indexed.
CREATE INDEX IF NOT EXISTS idx_device_messages_account_chat
  ON device_messages(user_id, json_extract(payload_json, '$.accountChat'), target_device_id, source_device_id, message_id);

-- Lifecycle cleanup is no longer part of every inbox read; when it runs, bound the scan.
CREATE INDEX IF NOT EXISTS idx_device_messages_expiry
  ON device_messages(user_id, status, expires_at);
