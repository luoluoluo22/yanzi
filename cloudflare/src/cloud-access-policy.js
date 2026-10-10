// Server-side cloud entitlement rules. Version strings and client headers never grant access.
// Default-deny cloud API policy. Only account enrollment, membership renewal,
// support and update discovery work without a paid entitlement.
const publicV1Routes = new Set([
  '/v1/auth/login', '/v1/auth/register', '/v1/auth/send-code',
  '/v1/auth/send-reset-code', '/v1/auth/reset-password', '/v1/auth/me',
  '/v1/licenses/redeem', '/v1/licenses/generate', '/v1/licenses/list',
  '/v1/user/vip-status', '/v1/user/cloud-access', '/v1/user/cloud-export',
  '/v1/app/update/latest'
]);
export function isCloudServicePath(pathname) {
  if (typeof pathname !== 'string') return false;
  if (pathname.startsWith('/v1/')) return !publicV1Routes.has(pathname);
  // A delegated invite or polling request also uses cloud resources.
  return /^\/connect\/[a-f0-9]{64}(?:\/(?:requests|resources))?$/.test(pathname);
}
export function needsTopLevelCloudAuth(pathname) {
  // Endpoints allowing anonymous access in their original implementation.
  return pathname === '/v1/extensions' || pathname.startsWith('/v1/extensions/') ||
    pathname === '/v1/applications/catalog' || pathname === '/v1/debug/list-packages' ||
    pathname === '/v1/wishes' || pathname.startsWith('/v1/wishes/');
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
