-- Device listing uses account order regardless of platform. The existing
-- (user_id, platform, updated_at) index cannot satisfy this ORDER BY.
CREATE INDEX IF NOT EXISTS idx_user_devices_account_order
  ON user_devices(user_id, updated_at DESC);
