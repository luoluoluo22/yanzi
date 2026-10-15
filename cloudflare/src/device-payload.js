import { HttpError } from './http-error.js';

export function normalizeDevicePayload(payload) {
  return {
    deviceId: normalizeDeviceId(payload.deviceId || payload.device_id),
    platform: normalizeDevicePlatform(payload.platform),
    displayName: normalizeShortText(payload.displayName || payload.display_name || payload.name, "displayName", 80),
    pushToken: normalizeOptionalString(payload.pushToken || payload.push_token, 512),
    capabilities: normalizeJsonObject(payload.capabilities, "capabilities")
  };
}

export function normalizeDeviceMessagePayload(payload) {
  const targetDeviceId = normalizeOptionalDeviceId(payload.targetDeviceId || payload.target_device_id);
  const targetPlatform = targetDeviceId
    ? null
    : normalizeOptionalDevicePlatform(payload.targetPlatform || payload.target_platform) || "desktop";

  return {
    sourceDeviceId: normalizeOptionalDeviceId(payload.sourceDeviceId || payload.source_device_id),
    targetDeviceId,
    targetPlatform,
    kind: normalizeMessageKind(payload.kind),
    title: normalizeOptionalString(payload.title, 120),
    bodyText: normalizeOptionalString(payload.text || payload.bodyText || payload.body_text || payload.body, 4000),
    payload: normalizeJsonObject(payload.payload, "payload"),
    expiresAt: normalizeOptionalIsoDate(payload.expiresAt || payload.expires_at)
  };
}

export function normalizeDeviceId(value) {
  const id = String(value || "").trim();
  if (!/^[a-zA-Z0-9_.:-]{6,96}$/.test(id)) {
    throw new HttpError(400, "invalid_device_id", "deviceId must be 6-96 chars: a-z, 0-9, _, ., :, -");
  }

  return id;
}

export function normalizeOptionalDeviceId(value) {
  if (value == null || String(value).trim() === "") {
    return null;
  }

  return normalizeDeviceId(value);
}

export function normalizeDevicePlatform(value) {
  const platform = String(value || "").trim().toLowerCase();
  if (!/^[a-z][a-z0-9_.-]{0,31}$/.test(platform)) {
    throw new HttpError(400, "invalid_platform", "platform must be a lowercase identifier of 1-32 characters");
  }

  return platform;
}

export function normalizeOptionalDevicePlatform(value) {
  if (value == null || String(value).trim() === "") {
    return null;
  }

  return normalizeDevicePlatform(value);
}

export function normalizeMessageKind(value) {
  const kind = String(value || "text").trim().toLowerCase();
  if (!/^[a-z0-9_.:-]{1,40}$/.test(kind)) {
    throw new HttpError(400, "invalid_message_kind", "kind must be 1-40 lowercase chars");
  }

  return kind;
}

export function normalizeMessageId(value) {
  const id = String(value || "").trim();
  if (!/^msg_[a-f0-9]{24}$/.test(id)) {
    throw new HttpError(400, "invalid_message_id", "messageId format is invalid");
  }

  return id;
}

export function normalizeMessageLimit(value) {
  const limit = Number.parseInt(String(value || "20"), 10);
  if (!Number.isFinite(limit)) {
    return 20;
  }

  return Math.min(Math.max(limit, 1), 50);
}

export function normalizeShortText(value, fieldName, maxLength) {
  const text = String(value || "").trim();
  if (!text) {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} is required`);
  }

  return text.slice(0, maxLength);
}

export function normalizeOptionalString(value, maxLength) {
  if (value == null) {
    return null;
  }

  const text = String(value).trim();
  return text ? text.slice(0, maxLength) : null;
}

export function normalizeJsonObject(value, fieldName) {
  if (value == null) {
    return {};
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} must be an object`);
  }

  return value;
}

export function normalizeOptionalIsoDate(value) {
  if (!value) {
    return null;
  }

  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) {
    throw new HttpError(400, "invalid_updated_at", "updatedAtUtc must be a valid datetime");
  }

  return date.toISOString();
}
