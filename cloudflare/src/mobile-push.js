import { messageMatchesDevice } from './device-message-protocol.js';
// Credentials are deployment secrets. Never accept provider URLs or keys from clients.
function b64(bytes) { return btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', ''); }
function encoded(value) { return b64(new TextEncoder().encode(JSON.stringify(value))); }
async function firebaseAccessToken(account) {
  const now = Math.floor(Date.now() / 1000);
  const claims = { iss: account.client_email, scope: 'https://www.googleapis.com/auth/firebase.messaging',
    aud: 'https://oauth2.googleapis.com/token', iat: now, exp: now + 3600 };
  const input = `${encoded({ alg: 'RS256', typ: 'JWT' })}.${encoded(claims)}`;
  const pem = account.private_key.replace(/-----[^-]+-----|\s/g, '');
  const key = await crypto.subtle.importKey('pkcs8', Uint8Array.from(atob(pem), c => c.charCodeAt(0)),
    { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' }, false, ['sign']);
  const signature = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key, new TextEncoder().encode(input));
  const response = await fetch('https://oauth2.googleapis.com/token', { method: 'POST', signal: AbortSignal.timeout(10000),
    body: new URLSearchParams({ grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer', assertion: `${input}.${b64(new Uint8Array(signature))}` }) });
  if (!response.ok) throw new Error('push_oauth_failed');
  return (await response.json()).access_token;
}
export async function sendProviderPush(env, provider, token, messageId) {
  if (provider === 'fcm' && env.FIREBASE_SERVICE_ACCOUNT) {
    const account = JSON.parse(env.FIREBASE_SERVICE_ACCOUNT);
    const access = await firebaseAccessToken(account);
    const response = await fetch(`https://fcm.googleapis.com/v1/projects/${encodeURIComponent(account.project_id)}/messages:send`, {
      method: 'POST', signal: AbortSignal.timeout(10000), headers: { Authorization: `Bearer ${access}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: { token, data: { type: 'messages-ready', messageId }, android: { priority: 'HIGH', ttl: '3600s' } } })
    });
    if (!response.ok) throw new Error(`push_provider_http_${response.status}`);
    return 'sent';
  }
  if (provider === 'webhook' && env.PUSH_WEBHOOK_URL && env.PUSH_WEBHOOK_TOKEN) {
    const url = new URL(env.PUSH_WEBHOOK_URL);
    if (url.protocol !== 'https:') throw new Error('push_webhook_requires_https');
    const response = await fetch(url, { method: 'POST', signal: AbortSignal.timeout(10000),
      headers: { Authorization: `Bearer ${env.PUSH_WEBHOOK_TOKEN}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ token, messageId, type: 'messages-ready' }) });
    if (!response.ok) throw new Error(`push_provider_http_${response.status}`);
    return 'sent';
  }
  return 'not_configured';
}
export async function sendOfflinePush(env, userId, message) {
  const rows = (await env.DB.prepare('SELECT device_id, push_token, capabilities_json FROM user_devices WHERE user_id = ? AND platform = ? AND push_token IS NOT NULL')
    .bind(userId, 'android').all()).results || [];
  for (const row of rows) {
    if (!messageMatchesDevice(message, {deviceId: row.device_id, platform: 'android'})) continue;
    try {
      // A retained socket does not prove that a sleeping phone is consuming frames.
      // Send the small wakeup signal even when the relay still reports connected.
      const provider = JSON.parse(row.capabilities_json || '{}').pushProvider;
      const status = await sendProviderPush(env, provider, row.push_token, message.messageId);
      console.log(JSON.stringify({ event: 'mobile_push', messageId: message.messageId, status }));
    } catch (error) { console.warn(JSON.stringify({ event: 'mobile_push_failed', messageId: message.messageId, error: error.name })); }
  }
}
