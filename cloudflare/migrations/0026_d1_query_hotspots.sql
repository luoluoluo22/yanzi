-- D1 Insights (2026-10-08): correlated install_count queries scanned hundreds of
-- thousands of user_extensions rows. Cover extension_id + enabled in one index.
CREATE INDEX IF NOT EXISTS idx_user_extensions_extension_enabled
  ON user_extensions(extension_id, enabled);

-- Legacy Android/desktop clients without the message cursor still read the
-- account-chat branch of the inbox. Keep that branch narrow and seekable by
-- expiry instead of scanning unrelated/expired message history.
CREATE INDEX IF NOT EXISTS idx_device_messages_account_chat_unreceived_expiry
  ON device_messages(user_id, expires_at, message_id)
  WHERE target_device_id IS NULL
    AND json_extract(payload_json, '$.accountChat') = 1;
