// Account-owned labels survive device heartbeats and do not require the target online.
export function normalizeDeviceRemark(value, HttpError) {
  if (typeof value !== 'string') throw new HttpError(400, 'invalid_remark', 'remark must be a string');
  const remark = value.trim().normalize('NFC');
  if ([...remark].length > 40 || /[\u0000-\u001f\u007f]/u.test(remark))
    throw new HttpError(400, 'invalid_remark', 'remark must be at most 40 characters without control characters');
  return remark;
}

export async function handleDeviceRemark(request, env, api) {
  const match = new URL(request.url).pathname.match(/^\/v1\/me\/devices\/([^/]+)\/remark$/);
  if (!match || request.method !== 'PUT') return null;
  const { HttpError, requireAuth, normalizeDeviceId, ensureOwnedDevice, readJson, isoNow, json } = api;
  const auth = await requireAuth(request, env);
  if (auth.grant) throw new HttpError(403, 'account_owner_required', 'Account login is required');
  const id = normalizeDeviceId(decodeURIComponent(match[1]));
  await ensureOwnedDevice(env, auth.userId, id);
  const remark = normalizeDeviceRemark((await readJson(request)).remark, HttpError);
  const updatedAt = isoNow();
  await env.DB.prepare(`UPDATE user_devices SET
    capabilities_json = json_set(capabilities_json, '$.accountRemark', ?, '$.accountRemarkUpdatedAt', ?),
    updated_at = ? WHERE user_id = ? AND device_id = ?`)
    .bind(remark, updatedAt, updatedAt, auth.userId, id).run();
  const response = json({ ok: true, deviceId: id, remark, updatedAt });
  response.headers.set('Cache-Control', 'no-store');
  return response;
}
