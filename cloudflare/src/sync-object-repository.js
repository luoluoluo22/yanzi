// Domain implementation; dependencies are supplied by the composition root.
export function createSyncObjectRepository(api) {
  const { HttpError, isoNow, scrubAiSecretsFromValue, textEncoder } = api;
function getSyncDatabase(env) {
  return typeof env.DB?.withSession === "function"
    ? env.DB.withSession("first-primary")
    : env.DB;
}

async function ensureUser(env, userId) {
  await env.DB.prepare(
    `insert into users (user_id, created_at, updated_at)
     values (?, ?, ?)
     on conflict(user_id) do nothing`
  )
    .bind(userId, isoNow(), isoNow())
    .run();
}

function normalizeSyncObjectId(value) {
  const objectId = String(value || "").trim();
  if (!/^[A-Za-z0-9._:-]{1,120}$/.test(objectId)) {
    throw new HttpError(400, "invalid_sync_object_id", "objectId contains unsupported characters or is too long");
  }
  return objectId;
}

function normalizeSyncRevision(value, fieldName = "revision") {
  if (value === null || value === undefined || value === "") {
    return 0;
  }
  const revision = Number(value);
  if (!Number.isSafeInteger(revision) || revision < 0) {
    throw new HttpError(400, "invalid_sync_revision", `${fieldName} must be a non-negative safe integer`);
  }
  return revision;
}

function serializeUserSyncObject(row) {
  let payload = {};
  try {
    payload = JSON.parse(String(row.payload_json || "{}"));
  } catch {
    payload = {};
  }
  return {
    objectId: String(row.object_id || ""),
    schemaVersion: Number(row.schema_version || 1),
    revision: Number(row.object_revision || 0),
    updatedAtUtc: String(row.updated_at || ""),
    updatedByDeviceId: row.updated_by_device_id ? String(row.updated_by_device_id) : null,
    updatedByDeviceName: row.updated_by_device_name ? String(row.updated_by_device_name) : null,
    deleted: Boolean(row.deleted),
    payload
  };
}

function serializeUserSyncObjectHistory(row) {
  return {
    ...serializeUserSyncObject({ ...row, object_revision: row.revision }),
    operation: String(row.operation || "update"),
    restoredFromRevision: row.restored_from_revision == null
      ? null
      : Number(row.restored_from_revision)
  };
}

async function getUserSyncRevision(env, userId) {
  const row = await env.DB.prepare(
    `select revision from user_sync_revisions where user_id = ?`
  ).bind(userId).first();
  return Number(row?.revision || 0);
}

async function readUserSyncObjects(env, userId, sinceRevision, limit) {
  const currentRevision = await getUserSyncRevision(env, userId);
  const rows = await env.DB.prepare(
    `select object_id, schema_version, object_revision, updated_at,
            updated_by_device_id, updated_by_device_name, deleted, payload_json
     from user_sync_objects
     where user_id = ? and object_revision > ? and object_revision <= ?
     order by object_revision asc
     limit ?`
  ).bind(userId, sinceRevision, currentRevision, limit + 1).all();
  const allRows = rows.results || [];
  const hasMore = allRows.length > limit;
  const selectedRows = hasMore ? allRows.slice(0, limit) : allRows;
  const objects = selectedRows.map(serializeUserSyncObject);
  const cursorRevision = objects.length > 0
    ? objects[objects.length - 1].revision
    : currentRevision;
  return { currentRevision, cursorRevision, hasMore, objects };
}

async function readUserSyncObject(env, userId, objectId) {
  const db = getSyncDatabase(env);
  const row = await db.prepare(
    `select object_id, schema_version, object_revision, updated_at,
            updated_by_device_id, updated_by_device_name, deleted, payload_json
     from user_sync_objects
     where user_id = ? and object_id = ?`
  ).bind(userId, objectId).first();
  return row ? serializeUserSyncObject(row) : null;
}

async function readUserSyncObjectHistory(env, userId, objectId, beforeRevision, limit) {
  const rows = await env.DB.prepare(
    `select object_id, revision, schema_version, updated_at,
            updated_by_device_id, updated_by_device_name, deleted, payload_json,
            operation, restored_from_revision
     from user_sync_object_history
     where user_id = ? and object_id = ? and (? = 0 or revision < ?)
     order by revision desc
     limit ?`
  ).bind(userId, objectId, beforeRevision, beforeRevision, limit + 1).all();
  const allRows = rows.results || [];
  const hasMore = allRows.length > limit;
  const selectedRows = hasMore ? allRows.slice(0, limit) : allRows;
  const versions = selectedRows.map(serializeUserSyncObjectHistory);
  const currentRevision = await getUserSyncRevision(env, userId);
  const nextBeforeRevision = versions.length > 0
    ? versions[versions.length - 1].revision
    : beforeRevision;
  return { currentRevision, nextBeforeRevision, hasMore, versions };
}

async function writeUserSyncObject(env, userId, objectId, input, writeMetadata = {}) {
  const expectedRevision = normalizeSyncRevision(input.expectedRevision, "expectedRevision");
  const schemaVersion = Number(input.schemaVersion ?? 1);
  if (!Number.isInteger(schemaVersion) || schemaVersion < 1 || schemaVersion > 1000) {
    throw new HttpError(400, "invalid_sync_schema_version", "schemaVersion must be an integer between 1 and 1000");
  }
  if (!Object.prototype.hasOwnProperty.call(input, "payload")) {
    throw new HttpError(400, "sync_payload_required", "payload is required");
  }

  const deleted = input.deleted === true;
  const updatedByDeviceId = String(input.updatedByDeviceId || "").trim().slice(0, 200) || null;
  const updatedByDeviceName = String(input.updatedByDeviceName || "").trim().slice(0, 200) || null;
  const safePayload = objectId === "settings.ai"
    ? scrubAiSecretsFromValue(input.payload ?? {})
    : input.payload ?? {};
  const payloadJson = JSON.stringify(safePayload);
  if (textEncoder.encode(payloadJson).length > 1024 * 1024) {
    throw new HttpError(413, "sync_payload_too_large", "sync object payload exceeds 1 MiB");
  }

  await ensureUser(env, userId);
  const now = isoNow();
  const operation = writeMetadata.operation === "restore"
    ? "restore"
    : deleted ? "delete" : expectedRevision === 0 ? "create" : "update";
  const restoredFromRevision = operation === "restore"
    ? normalizeSyncRevision(writeMetadata.restoredFromRevision, "restoredFromRevision")
    : null;
  const results = await env.DB.batch([
    env.DB.prepare(
      `insert into user_sync_revisions (user_id, revision, updated_at)
       values (?, 0, ?)
       on conflict(user_id) do nothing`
    ).bind(userId, now),
    env.DB.prepare(
      `insert into user_sync_objects (
         user_id, object_id, schema_version, object_revision, updated_at,
         updated_by_device_id, updated_by_device_name, deleted, payload_json
       )
       select ?, ?, ?, revisions.revision + 1, ?, ?, ?, ?, ?
       from user_sync_revisions revisions
       where revisions.user_id = ?
         and (
           (? = 0 and not exists (
             select 1 from user_sync_objects existing
             where existing.user_id = ? and existing.object_id = ?
           ))
           or
           (? > 0 and exists (
             select 1 from user_sync_objects existing
             where existing.user_id = ? and existing.object_id = ? and existing.object_revision = ?
           ))
         )
       on conflict(user_id, object_id) do update set
         schema_version = excluded.schema_version,
         object_revision = excluded.object_revision,
         updated_at = excluded.updated_at,
         updated_by_device_id = excluded.updated_by_device_id,
         updated_by_device_name = excluded.updated_by_device_name,
         deleted = excluded.deleted,
         payload_json = excluded.payload_json`
    ).bind(
      userId, objectId, schemaVersion, now,
      updatedByDeviceId, updatedByDeviceName, deleted ? 1 : 0, payloadJson,
      userId,
      expectedRevision, userId, objectId,
      expectedRevision, userId, objectId, expectedRevision
    ),
    env.DB.prepare(
      `insert into user_sync_object_history (
         user_id, object_id, revision, schema_version, updated_at,
         updated_by_device_id, updated_by_device_name, deleted, payload_json,
         operation, restored_from_revision
       )
       select objects.user_id, objects.object_id, objects.object_revision,
              objects.schema_version, objects.updated_at, objects.updated_by_device_id,
              objects.updated_by_device_name, objects.deleted, objects.payload_json, ?, ?
       from user_sync_objects objects
       join user_sync_revisions revisions on revisions.user_id = objects.user_id
       where objects.user_id = ? and objects.object_id = ?
         and objects.object_revision = revisions.revision + 1`
    ).bind(operation, restoredFromRevision, userId, objectId),
    env.DB.prepare(
      `update user_sync_revisions
       set revision = revision + 1, updated_at = ?
       where user_id = ?
         and exists (
           select 1 from user_sync_objects objects
           where objects.user_id = user_sync_revisions.user_id
             and objects.object_id = ?
             and objects.object_revision = user_sync_revisions.revision + 1
         )`
    ).bind(now, userId, objectId)
  ]);

  if (Number(results?.[1]?.meta?.changes || 0) === 0) {
    const current = await env.DB.prepare(
      `select object_revision from user_sync_objects where user_id = ? and object_id = ?`
    ).bind(userId, objectId).first();
    const error = new HttpError(409, "sync_revision_conflict", "sync object revision does not match expectedRevision");
    error.details = { objectId, expectedRevision, currentRevision: Number(current?.object_revision || 0) };
    throw error;
  }

  const row = await env.DB.prepare(
    `select object_id, schema_version, object_revision, updated_at,
            updated_by_device_id, updated_by_device_name, deleted, payload_json
     from user_sync_objects where user_id = ? and object_id = ?`
  ).bind(userId, objectId).first();
  return serializeUserSyncObject(row);
}


  return { getSyncDatabase, ensureUser, normalizeSyncObjectId, normalizeSyncRevision, serializeUserSyncObject, serializeUserSyncObjectHistory, getUserSyncRevision, readUserSyncObjects, readUserSyncObject, readUserSyncObjectHistory, writeUserSyncObject };
}
