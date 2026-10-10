// Server-side cloud entitlement rules. Version strings and client headers never grant access.
const cloudPrefixes = [
  '/v1/sync', '/v1/me', '/v1/device', '/v1/devices', '/v1/messaging',
  '/v1/messages', '/v1/records', '/v1/applications', '/v1/extension-data',
  '/v1/external-access', '/v1/repos', '/v1/extensions'
];
export function isCloudServicePath(pathname) {
  if (typeof pathname !== 'string') return false;
  return cloudPrefixes.some(prefix => pathname === prefix || pathname.startsWith(`${prefix}/`));
}
export function isVipActive(record, nowMs = Date.now()) {
  if (!record) return false;
  if (record.vip_type === 'lifetime') return true;
  const expiry = typeof record.vip_expire_at === 'string' ? Date.parse(record.vip_expire_at) : NaN;
  return Number.isFinite(expiry) && expiry > nowMs;
}

// Stateless entitlement lookup helper: callers can apply an isolate-local short TTL.
export async function readVipEntitlement(db, userId, nowMs = Date.now()) {
  const row = await db.prepare('select vip_type, vip_expire_at from users where user_id = ?')
    .bind(userId).first();
  return {row, active:isVipActive(row,nowMs)};
}
export async function passesLoginRateLimit(limiter, key) {
  if (!limiter?.limit || !key) return true;
  const result = await limiter.limit({key});
  return result.success === true;
}
