CREATE TABLE mobile_message_idempotency_scoped (
  user_id TEXT NOT NULL,
  source_device_id TEXT NOT NULL,
  client_message_id TEXT NOT NULL,
  message_id TEXT NOT NULL,
  request_hash TEXT NOT NULL,
  PRIMARY KEY(user_id, source_device_id, client_message_id)
);
INSERT INTO mobile_message_idempotency_scoped
  SELECT i.user_id, coalesce(m.source_device_id, ''), i.client_message_id, i.message_id, i.request_hash
  FROM mobile_message_idempotency i JOIN device_messages m ON m.message_id = i.message_id AND m.user_id = i.user_id;
DROP TABLE mobile_message_idempotency;
ALTER TABLE mobile_message_idempotency_scoped RENAME TO mobile_message_idempotency;
