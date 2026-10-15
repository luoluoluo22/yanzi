-- A chat ACK belongs to a device. Other devices must still be able to catch up.
CREATE TABLE IF NOT EXISTS account_chat_receipts (
  user_id TEXT NOT NULL,
  message_id TEXT NOT NULL,
  device_id TEXT NOT NULL,
  status TEXT NOT NULL,
  acked_at TEXT NOT NULL,
  PRIMARY KEY (user_id, message_id, device_id)
);

-- Recover pending chat from the old single-target queue, preserving IDs and content.
-- Completed historic deliveries and device commands are left untouched.
UPDATE device_messages
SET payload_json = json_set(payload_json, '$.accountChat', json('true')),
    expires_at = coalesce(expires_at, strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '+30 days'))
WHERE status = 'pending' AND title = 'YanziChat' AND kind IN ('text', 'photo', 'file');
