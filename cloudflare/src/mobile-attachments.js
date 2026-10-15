export class MessagingError extends Error {
  constructor(status, code, message) { super(message); this.status = status; this.code = code; }
}
const MAX_SIZE = 30 * 1024 * 1024;
function metadata(row) {
  return { attachmentId: row.attachment_id, fileName: row.file_name, contentType: row.content_type,
    size: row.size, sha256: row.sha256, expiresAt: row.expires_at };
}
export async function ownedAttachment(env, userId, id) {
  if (!/^att_[a-f0-9]{32}$/.test(id)) throw new MessagingError(400, 'invalid_attachment', 'Invalid attachment ID');
  const row = await env.DB.prepare('SELECT * FROM mobile_attachments WHERE user_id = ? AND attachment_id = ?')
    .bind(userId, id).first();
  if (!row || row.expires_at <= new Date().toISOString())
    throw new MessagingError(404, 'attachment_not_found', 'Attachment is absent or expired');
  return row;
}
export async function handleAttachments(request, env, userId) {
  const url = new URL(request.url);
  const prefix = '/v1/me/mobile/attachments';
  if (url.pathname === prefix && request.method === 'POST') {
    const size = Number(request.headers.get('Content-Length'));
    const hash = request.headers.get('X-Content-Sha256') || '';
    if (!Number.isSafeInteger(size) || size <= 0 || size > MAX_SIZE)
      throw new MessagingError(413, 'attachment_size', 'Attachments must be between 1 byte and 30 MiB');
    if (!/^[a-f0-9]{64}$/.test(hash)) throw new MessagingError(400, 'attachment_hash', 'SHA256 is required');
    const name = (url.searchParams.get('name') || 'file').replace(/[\x00-\x1f\/\\]/g, '_').slice(0, 180);
    const contentType = /^image\/(png|jpeg|gif|webp|bmp)$/.test(request.headers.get('Content-Type') || '')
      ? request.headers.get('Content-Type') : 'application/octet-stream';
    const id = 'att_' + crypto.randomUUID().replaceAll('-', '');
    const key = `private-mobile/${userId}/${id}`;
    const now = new Date().toISOString();
    const expiry = new Date(Date.now() + 7 * 86400000).toISOString();
    try {
      const object = await env.PACKAGES.put(key, request.body, {
        sha256: hash, httpMetadata: { contentType }, customMetadata: { userId, attachmentId: id }
      });
      if (object.size !== size) throw new MessagingError(400, 'attachment_size_mismatch', 'Upload size mismatch');
      await env.DB.prepare('INSERT INTO mobile_attachments VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)')
        .bind(id, userId, key, name, contentType, size, hash, now, expiry).run();
      return Response.json({ attachmentId: id, fileName: name, contentType, size, sha256: hash, expiresAt: expiry });
    } catch (error) {
      await env.PACKAGES.delete(key);
      if (error instanceof MessagingError) throw error;
      throw new MessagingError(400, 'attachment_upload_failed', 'Upload failed or checksum did not match');
    }
  }
  const match = url.pathname.match(/^\/v1\/me\/mobile\/attachments\/(att_[a-f0-9]{32})(\/content)?$/);
  if (!match) throw new MessagingError(404, 'not_found', 'Unknown attachment route');
  const row = await ownedAttachment(env, userId, match[1]);
  if (request.method === 'DELETE') {
    await env.PACKAGES.delete(row.object_key);
    await env.DB.prepare('DELETE FROM mobile_attachments WHERE attachment_id = ? AND user_id = ?').bind(match[1], userId).run();
    return Response.json({ ok: true });
  }
  if (request.method !== 'GET') throw new MessagingError(405, 'method_not_allowed', 'GET or DELETE required');
  if (!match[2]) return Response.json(metadata(row));
  const range = request.headers.get('Range');
  if (range && !/^bytes=\d+-\d*$/.test(range)) throw new MessagingError(416, 'invalid_range', 'Single byte range required');
  if (range) {
    const [start, end] = range.slice(6).split('-');
    if (!Number.isSafeInteger(Number(start)) || Number(start) >= row.size ||
        (end !== '' && (!Number.isSafeInteger(Number(end)) || Number(end) < Number(start))))
      throw new MessagingError(416, 'invalid_range', 'Range is outside attachment content');
  }
  const object = await env.PACKAGES.get(row.object_key, range ? { range: request.headers } : undefined);
  if (!object) throw new MessagingError(404, 'attachment_not_found', 'Attachment content absent');
  const headers = new Headers({ 'Content-Type': row.content_type, 'Cache-Control': 'private, no-store',
    'X-Content-Sha256': row.sha256, 'Accept-Ranges': 'bytes',
    'Content-Disposition': `attachment; filename*=UTF-8''${encodeURIComponent(row.file_name)}` });
  let status = 200;
  if (object.range) {
    const { offset, length } = object.range;
    headers.set('Content-Range', `bytes ${offset}-${offset + length - 1}/${row.size}`);
    headers.set('Content-Length', String(length)); status = 206;
  } else headers.set('Content-Length', String(row.size));
  return new Response(object.body, { status, headers });
}
export async function cleanupAttachments(env) {
  const rows = (await env.DB.prepare('SELECT attachment_id, object_key FROM mobile_attachments WHERE expires_at <= ? LIMIT 100')
    .bind(new Date().toISOString()).all()).results;
  for (const row of rows) {
    await env.PACKAGES.delete(row.object_key);
    await env.DB.prepare('DELETE FROM mobile_attachments WHERE attachment_id = ?').bind(row.attachment_id).run();
  }
}
