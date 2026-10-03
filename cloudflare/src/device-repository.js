// Domain implementation; dependencies are supplied by the composition root.
export function createDeviceRepository(api) {
  const { DEVICE_MESSAGE_PROTOCOL, DEVICE_ONLINE_WINDOW_MS, HttpError, acceptsAccountChat, ensureUser, isoNow } = api;
async function ensureOwnedDevice(env, userId, deviceId) {
  const device = await env.DB.prepare(
    `select device_id, platform, display_name, capabilities_json
     from user_devices
     where user_id = ? and device_id = ?`
  )
    .bind(userId, deviceId)
    .first();

  if (!device || parseJsonObject(device.capabilities_json).disabled === true) {
    throw new HttpError(404, "device_not_found", "Device was not found in this account");
  }

  return {
    deviceId: device.device_id,
    platform: device.platform,
    displayName: device.display_name,
    capabilities: parseJsonObject(device.capabilities_json)
  };
}

async function touchDevice(env, userId, deviceId, request) {
  const location = deviceNetworkLocation(request);
  await env.DB.prepare(
    `update user_devices
     set last_seen_at = ?,
         updated_at = ?,
         capabilities_json = CASE WHEN ? <> '' THEN json_set(capabilities_json, '$.networkLocation', ?) ELSE capabilities_json END
     where user_id = ? and device_id = ?`
  )
    .bind(isoNow(), isoNow(), location, location, userId, deviceId)
    .run();
}

async function getPendingDeviceMessageItems(env, userId, deviceId, platform, limit = 20, capabilities = {}, after = 0) {
  await ensureOwnedDevice(env, userId, deviceId);
  await expireDeviceCommands(env, userId);
  const rows = await env.DB.prepare(
    `SELECT m.*, q.sequence FROM device_messages m JOIN device_message_sequence q ON q.message_id = m.message_id
     WHERE m.user_id = ? AND q.sequence > ? AND (m.expires_at IS NULL OR m.expires_at > ?)
       AND (
         (m.target_device_id IS NULL AND json_extract(m.payload_json, '$.accountChat') = 1
          AND m.source_device_id <> ? AND ?
          AND NOT EXISTS (SELECT 1 FROM account_chat_receipts r
            WHERE r.user_id = m.user_id AND r.message_id = m.message_id
              AND r.device_id = ?))
         OR ((m.target_device_id IS NOT NULL OR coalesce(json_extract(m.payload_json, '$.accountChat'), 0) <> 1)
          AND m.status = 'pending'
          AND (m.target_device_id = ? OR (m.target_device_id IS NULL AND m.target_platform = ?)))
       ) ORDER BY q.sequence ASC LIMIT ?`
  ).bind(userId, after, isoNow(), deviceId,
    acceptsAccountChat({platform,capabilities}) ? 1 : 0,
    deviceId, deviceId, platform, limit).all();

  return (rows.results ?? []).map(serializeDeviceMessageRecord);
}

async function markDeviceMessagesDelivered(env, userId, items) {
  if (!items || items.length === 0) {
    return;
  }

  await env.DB.prepare(
    `update device_messages
     set delivered_at = coalesce(delivered_at, ?)
     where user_id = ?
       and message_id in (${items.map(() => "?").join(",")})`
  )
    .bind(isoNow(), userId, ...items.map((item) => item.messageId))
    .run();
}

async function notifyDeviceRelay(env, userId, event) {
  if (!env.DEVICE_RELAY) {
    return;
  }

  try {
    await env.DEVICE_RELAY.get(env.DEVICE_RELAY.idFromName(userId)).publish(event);
  } catch (error) {
    // The HTTP polling fallback will pick up pending messages if the relay is unavailable.
    console.warn(JSON.stringify({ event: 'mobile_relay_fallback', error: error.name }));
  }
}

function serializeDeviceRecord(row, referenceNowMs = Date.now()) {
  const lastSeenAtMs = Date.parse(String(row.last_seen_at || ""));
  const online = Number.isFinite(lastSeenAtMs)
    && Math.max(0, referenceNowMs - lastSeenAtMs) <= DEVICE_ONLINE_WINDOW_MS;
  return {
    deviceId: row.device_id,
    platform: row.platform,
    displayName: row.display_name,
    capabilities: parseJsonObject(row.capabilities_json),
    needsMessageUpgrade: row.platform === 'android' && !parseJsonObject(row.capabilities_json).receiveMobileMessages,
    lastSeenAt: row.last_seen_at,
    lastLocation: parseJsonObject(row.capabilities_json).networkLocation || null,
    online,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
}

function deviceNetworkLocation(request) {
  // Network region supplied by Cloudflare, never inferred from the device name or client payload.
  return [...new Set([request.cf?.country, request.cf?.region, request.cf?.city]
    .filter(x => typeof x === 'string' && x.trim()).map(x => x.trim().slice(0, 80)))].join(' 路 ');
}

function serializeDeviceMessageRecord(row) {
  return {
    protocolVersion: DEVICE_MESSAGE_PROTOCOL.version,
    sequence: row.sequence || null,
    ...parseJsonObject(row.payload_json).messageContext,
    messageId: row.message_id,
    sourceDeviceId: row.source_device_id || null,
    targetDeviceId: row.target_device_id || null,
    targetPlatform: row.target_platform || null,
    kind: row.kind,
    title: row.title || "",
    text: row.body_text || "",
    payload: parseJsonObject(row.payload_json),
    status: row.status,
    createdAt: row.created_at,
    deliveredAt: row.delivered_at || null,
    ackedAt: row.acked_at || null,
    expiresAt: row.expires_at || null
  };
}

function parseJsonObject(value) {
  try {
    const parsed = JSON.parse(value || "{}");
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : {};
  } catch {
    return {};
  }
}

async function expireDeviceCommands(env, userId) {
  await env.DB.prepare(`UPDATE device_messages SET status = CASE WHEN status = 'executing' THEN 'unknown' ELSE 'expired' END,
    acked_at = ? WHERE user_id = ? AND status IN ('pending', 'executing') AND expires_at <= ?
    AND (kind LIKE 'run-%' OR kind LIKE 'fs-%' OR kind = 'capability.invoke')`).bind(isoNow(), userId, isoNow()).run();
}


  return { ensureOwnedDevice, touchDevice, getPendingDeviceMessageItems, markDeviceMessagesDelivered, notifyDeviceRelay, serializeDeviceRecord, deviceNetworkLocation, serializeDeviceMessageRecord, parseJsonObject, expireDeviceCommands };
}
