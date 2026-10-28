-- Reusing a same-user, same-extension immutable ZIP must be an indexed lookup,
-- not a scan of the full historical revision list (which can exceed 1,000).
CREATE INDEX IF NOT EXISTS idx_user_extension_archive_history_sha
  ON user_extension_archive_history(user_id, extension_id, archive_sha256, revision DESC);
