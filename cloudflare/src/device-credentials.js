import {isExecutionMessage} from './device-message-protocol.js';

export function commandScope(kind, payload = {}) {
  if (kind === 'fs-read' || kind === 'fs-list') return 'files.read';
  if (kind === 'fs-write') return 'files.write';
  if (kind === 'run-shell' || kind === 'run-powershell') return 'terminal.execute';
  if (kind === 'run-extension') return 'extension.run:' + (payload.extensionId || '');
  if (kind === 'capability.invoke') return 'capability.invoke:' + (payload.name || '');
  return null;
}
export function resourceAllowed(path, roots) {
  if (typeof path !== 'string' || /[\x00-\x1f]/.test(path)) return false;
  const normalize = value => {
    const slash = value.replaceAll('\\', '/');
    if (slash.split('/').includes('..') || slash.split('/').includes('.') || !/^(?:[a-zA-Z]:\/|\/)/.test(slash)) return null;
    return /^[a-zA-Z]:/.test(slash) ? slash.toLowerCase().replace(/\/+$/, '') : slash.replace(/\/+$/, '');
  };
  const candidate = normalize(path);
  return candidate !== null && roots.some(root => { const allowed = normalize(root); return allowed !== null &&
    (candidate === allowed || candidate.startsWith(allowed + '/')); });
}
export function messageGrantAllows(grant, envelope) {
  if (envelope.sourceDeviceId !== grant.deviceId) return false;
  const command = commandScope(envelope.kind, envelope.payload);
  if (isExecutionMessage(envelope.kind) && !command) return false;
  if (command) {
    if (!grant.scopes.includes(command) || !envelope.targetDeviceId || !grant.targets.includes(envelope.targetDeviceId)) return false;
    if (envelope.kind.startsWith('fs-') && !resourceAllowed(envelope.payload?.path, grant.roots)) return false;
    return true;
  }
  if (!grant.scopes.includes('chat.send')) return false;
  return envelope.targetDeviceId ? grant.targets.includes(envelope.targetDeviceId) : grant.scopes.includes('chat.account');
}
export async function authorizeDeviceCredential(request, env, claims, ErrorType) {
  const row = await env.DB.prepare('SELECT * FROM device_credentials WHERE user_id = ? AND credential_id = ?')
    .bind(claims.sub, claims.credentialId).first();
  if (!row || row.revoked_at || row.expires_at <= Date.now() / 1000 || row.device_id !== claims.deviceId)
    throw new ErrorType(401, 'device_credential_revoked', 'Device credential is expired or revoked');
  const grant = {credentialId:row.credential_id, deviceId:row.device_id, applicationId:row.application_id,
    scopes:JSON.parse(row.scopes_json), targets:JSON.parse(row.targets_json), roots:JSON.parse(row.roots_json), expiresAt:row.expires_at};
  const url = new URL(request.url), path = url.pathname, method = request.method;
  let allowed = false;
  if (path === '/v1/me/devices/protocol' && method === 'GET') allowed = true;
  else if (path === '/v1/me/devices' && method === 'POST') {
    const body = await request.clone().json(); allowed = body.deviceId === grant.deviceId && grant.scopes.includes('device.presence');
  } else if (path === '/v1/me/mobile/messages' && method === 'POST') allowed = messageGrantAllows(grant, await request.clone().json());
  else if (path === '/v1/me/mobile/messages' || path === '/v1/me/mobile/messages/ws' || path === '/v1/me/mobile/messages/events')
    allowed = method === 'GET' && url.searchParams.get('deviceId') === grant.deviceId && grant.scopes.includes('messages.receive');
  else if (/^\/v1\/me\/mobile\/messages\/msg_[a-f0-9]{24}(?:\/(ack|claim|cancel))?$/.test(path)) {
    const messageId = path.split('/')[5];
    const message = await env.DB.prepare('SELECT * FROM device_messages WHERE user_id = ? AND message_id = ?').bind(claims.sub, messageId).first();
    if (message) {
      const body = method === 'POST' ? await request.clone().json() : {};
      if (method === 'GET') allowed = message.source_device_id === grant.deviceId || message.target_device_id === grant.deviceId;
      else if (path.endsWith('/cancel')) allowed = message.source_device_id === grant.deviceId && body.deviceId === grant.deviceId;
      else if (path.endsWith('/ack')) allowed = body.deviceId === grant.deviceId && grant.scopes.includes('messages.receive');
      else if (path.endsWith('/claim')) allowed = body.deviceId === grant.deviceId && message.target_device_id === grant.deviceId &&
        grant.scopes.includes(commandScope(message.kind, JSON.parse(message.payload_json))) &&
        (!message.kind.startsWith('fs-') || resourceAllowed(JSON.parse(message.payload_json).path, grant.roots));
    }
  } else if (path === '/v1/me/mobile/attachments' && method === 'POST') allowed = grant.scopes.includes('attachments.send');
  else if (/^\/v1\/me\/mobile\/attachments\/[^/]+(?:\/content)?$/.test(path) && method === 'GET' && grant.scopes.includes('attachments.read')) {
    const id = path.split('/')[5];
    const reference = await env.DB.prepare(`SELECT 1 FROM device_messages m WHERE m.user_id = ? AND json_extract(m.payload_json, '$.attachmentId') = ?
      AND (m.source_device_id = ? OR m.target_device_id = ? OR (? = 1 AND m.target_device_id IS NULL
        AND json_extract(m.payload_json, '$.accountChat') = 1 AND m.source_device_id != ?
        AND EXISTS (SELECT 1 FROM user_devices d WHERE d.user_id = m.user_id AND d.device_id = ?
          AND (d.platform IN ('desktop', 'android') OR json_extract(d.capabilities_json, '$.receiveAccountChat') = 1)))) LIMIT 1`)
      .bind(claims.sub, id, grant.deviceId, grant.deviceId, grant.scopes.includes('messages.receive') ? 1 : 0, grant.deviceId, grant.deviceId).first();
    allowed = !!reference;
  }
  if (!allowed) throw new ErrorType(403, 'device_scope_denied', 'Device or application grant does not permit this operation');
  return {userId:String(claims.sub), username:String(claims.username), deviceId:grant.deviceId, grant};
}
