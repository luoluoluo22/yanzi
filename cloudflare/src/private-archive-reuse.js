// Share immutable ZIP bytes only within the same account and extension. This
// does not remove history revisions: D1 retains their independent references.
export async function findReusablePrivateArchive(env, userId, extensionId, sha256, byteLength) {
  if (!/^[0-9a-f]{64}$/i.test(String(sha256)) || !Number.isSafeInteger(byteLength) || byteLength < 1) {
    return null;
  }
  const prefix = 'users/' + userId + '/extensions/' + extensionId + '/archive-history/';
  try {
    const rows = await env.DB.prepare(
      'SELECT archive_key FROM user_extension_archive_history ' +
      'WHERE user_id = ? AND extension_id = ? AND archive_sha256 = ? ' +
      'ORDER BY revision DESC LIMIT 5'
    ).bind(userId, extensionId, sha256).all();
    for (const row of rows.results ?? []) {
      const key = String(row.archive_key || '');
      if (!key.startsWith(prefix) || !key.endsWith('-' + sha256 + '.zip')) continue;
      const object = await env.PACKAGES.head(key);
      if (object && Number(object.size) === byteLength) return key;
    }
  } catch {
    // Optional deduplication must not interrupt ordinary archive uploads.
  }
  return null;
}
