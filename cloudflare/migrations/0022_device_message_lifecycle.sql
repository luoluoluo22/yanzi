-- Stable insertion order is independent of client clocks and survives reconnects.
CREATE TABLE device_message_sequence (
  sequence INTEGER PRIMARY KEY AUTOINCREMENT,
  user_id TEXT NOT NULL,
  message_id TEXT NOT NULL UNIQUE
);
INSERT INTO device_message_sequence(user_id, message_id)
  SELECT user_id, message_id FROM device_messages ORDER BY created_at, message_id;
CREATE INDEX device_message_sequence_user ON device_message_sequence(user_id, sequence);
CREATE TRIGGER device_message_sequence_insert AFTER INSERT ON device_messages BEGIN
  INSERT INTO device_message_sequence(user_id, message_id) VALUES (NEW.user_id, NEW.message_id);
END;
-- Old execution requests must not suddenly run after a long offline interval.
UPDATE device_messages SET expires_at = strftime('%Y-%m-%dT%H:%M:%fZ', created_at, '+2 minutes')
  WHERE expires_at IS NULL AND (kind LIKE 'run-%' OR kind LIKE 'fs-%' OR kind = 'capability.invoke');
