// Domain implementation; dependencies are supplied by the composition root.
export function createSyncApi(api) {
  const { HttpError, ensureUser, getUserWebDavConfig, getYanmStateViewUrl, handleAttachments, hmacSha256, isoNow, json, normalizeOptionalIsoDate, normalizeSyncObjectId, normalizeSyncRevision, normalizeYanmComponentStatePatch, notifyDeviceRelay, patchYanmComponentStateForUser, readJson, readUserSyncObject, readUserSyncObjectHistory, readUserSyncObjects, readYanmStateForUser, requireAuth, writeUserSyncObject, writeYanmStateForUser } = api;
async function handleSyncApi(request, env, ctx) {
  const url = new URL(request.url);
  if (url.pathname === "/v1/sync/capabilities" && request.method === "GET") {
    await requireAuth(request, env);
    const table = await env.DB.prepare(
      `select name from sqlite_master where type = 'table' and name = 'user_sync_objects'`
    ).first();
    const historyTable = await env.DB.prepare(
      `select name from sqlite_master where type = 'table' and name = 'user_sync_object_history'`
    ).first();
    const objectSyncAvailable = Boolean(table?.name);
    const objectsAuthoritative = objectSyncAvailable &&
      String(env.SYNC_OBJECTS_AUTHORITATIVE || "").trim().toLowerCase() === "true";
    return json({
      ok: true,
      protocolVersion: 2,
      objectSyncAvailable,
      objectHistoryAvailable: Boolean(historyTable?.name),
      objectsAuthoritative,
      legacySnapshotReadSupported: true,
      legacySnapshotWriteRequired: !objectsAuthoritative,
      maxObjectPayloadBytes: 1024 * 1024
    });
  }

  if (url.pathname === "/v1/sync/vault-key" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const recoverySecret = String(env.VAULT_RECOVERY_SECRET || env.AUTH_TOKEN_SECRET || "").trim();
    if (!recoverySecret) throw new HttpError(503, "vault_recovery_unavailable", "Secret vault recovery is unavailable");
    const key = await hmacSha256(recoverySecret, `yanzi-secret-vault-recovery-v1:${auth.userId}`);
    const response = json({ ok: true, version: 1, key });
    response.headers.set("cache-control", "no-store");
    return response;
  }

  if (url.pathname === "/v1/sync/objects" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const result = await readUserSyncObjects(env, auth.userId, 0, 1000);
    return json({ ok: true, userId: auth.userId, ...result });
  }

  if (url.pathname === "/v1/sync/changes" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const sinceRevision = normalizeSyncRevision(url.searchParams.get("since"), "since");
    const requestedLimit = Number(url.searchParams.get("limit") || 200);
    const limit = Number.isInteger(requestedLimit) ? Math.min(Math.max(requestedLimit, 1), 500) : 200;
    const result = await readUserSyncObjects(env, auth.userId, sinceRevision, limit);
    return json({ ok: true, userId: auth.userId, sinceRevision, ...result });
  }

  if (url.pathname === "/v1/sync/history" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const objectId = normalizeSyncObjectId(url.searchParams.get("objectId"));
    const beforeRevision = normalizeSyncRevision(url.searchParams.get("before"), "before");
    const requestedLimit = Number(url.searchParams.get("limit") || 50);
    const limit = Number.isInteger(requestedLimit) ? Math.min(Math.max(requestedLimit, 1), 200) : 50;
    const result = await readUserSyncObjectHistory(env, auth.userId, objectId, beforeRevision, limit);
    return json({ ok: true, userId: auth.userId, objectId, ...result });
  }

  const syncObjectRestoreMatch = url.pathname.match(/^\/v1\/sync\/objects\/([^/]+)\/restore$/);
  if (syncObjectRestoreMatch && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const objectId = normalizeSyncObjectId(decodeURIComponent(syncObjectRestoreMatch[1]));
    const payload = await readJson(request);
    const restoreRevision = normalizeSyncRevision(payload.restoreRevision, "restoreRevision");
    if (restoreRevision <= 0) {
      throw new HttpError(400, "invalid_restore_revision", "restoreRevision must be a positive revision");
    }
    const historical = await env.DB.prepare(
      `select schema_version, deleted, payload_json
       from user_sync_object_history
       where user_id = ? and object_id = ? and revision = ?`
    ).bind(auth.userId, objectId, restoreRevision).first();
    if (!historical) {
      throw new HttpError(404, "sync_history_not_found", "requested sync object version was not found");
    }
    let historicalPayload = {};
    try {
      historicalPayload = JSON.parse(String(historical.payload_json || "{}"));
    } catch {
      historicalPayload = {};
    }
    const result = await writeUserSyncObject(env, auth.userId, objectId, {
      schemaVersion: Number(historical.schema_version || 1),
      expectedRevision: payload.expectedRevision,
      deleted: Boolean(historical.deleted),
      payload: historicalPayload,
      updatedByDeviceId: payload.updatedByDeviceId,
      updatedByDeviceName: payload.updatedByDeviceName
    }, { operation: "restore", restoredFromRevision: restoreRevision });
    return json({ ok: true, userId: auth.userId, object: result, restoredFromRevision: restoreRevision });
  }

  const syncObjectMatch = url.pathname.match(/^\/v1\/sync\/objects\/([^/]+)$/);
  if (syncObjectMatch && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const objectId = normalizeSyncObjectId(decodeURIComponent(syncObjectMatch[1]));
    const result = await readUserSyncObject(env, auth.userId, objectId);
    if (!result) {
      throw new HttpError(404, "sync_object_not_found", "sync object was not found");
    }
    return json({ ok: true, userId: auth.userId, object: result });
  }
  if (url.pathname.startsWith('/v1/me/mobile/attachments')) {
    const auth = await requireAuth(request, env);
    return handleAttachments(request, env, auth.userId);
  }

  if (syncObjectMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const objectId = normalizeSyncObjectId(decodeURIComponent(syncObjectMatch[1]));
    const payload = await readJson(request);
    const result = await writeUserSyncObject(env, auth.userId, objectId, payload);
    ctx.waitUntil(notifyDeviceRelay(env, auth.userId, {type:'sync-ready', userId:auth.userId, revision:result.revision, objectId, updatedByDeviceId:payload.updatedByDeviceId || null}));
    return json({ ok: true, userId: auth.userId, object: result });
  }

  if ((url.pathname === "/v1/me/yanm-state" || url.pathname === "/v1/me/yanm-webdav-state") && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const snapshot = await readYanmStateForUser(env, auth.userId);
    if (!snapshot) {
      throw new HttpError(404, "yanm_state_missing", "Yanm state was not found in account cloud snapshot");
    }

    const viewUrl = await getYanmStateViewUrl(env, auth.userId);

    return json({
      ok: true,
      userId: auth.userId,
      source: snapshot.source || "cloud-config",
      warning: snapshot.warning || "",
      diagnostics: snapshot.diagnostics || null,
      updatedAtUtc: snapshot.updatedAtUtc || null,
      yanm: snapshot.yanm || null,
      bytes: snapshot.bytes,
      viewUrl: viewUrl || null
    });
  }

  if (url.pathname === "/v1/sync/webdav-config" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const config = await getUserWebDavConfig(env, auth.userId);
    return json({
      ok: true,
      enabled: config.enabled,
      serverUrl: config.serverUrl,
      rootPath: config.rootPath,
      username: config.username,
      password: config.password
    });
  }

  if ((url.pathname === "/v1/me/yanm-state" || url.pathname === "/v1/me/yanm-webdav-state") && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const payload = await readJson(request);
    if (!payload.yanm || typeof payload.yanm !== "object") {
      throw new HttpError(400, "invalid_yanm", "Yanm payload is required");
    }

    const updatedAtUtc = normalizeOptionalIsoDate(payload.updatedAtUtc) || isoNow();
    const result = await writeYanmStateForUser(env, auth.userId, {
      updatedAtUtc,
      yanm: payload.yanm
    });

    const viewUrl = await getYanmStateViewUrl(env, auth.userId);

    return json({
      ok: true,
      userId: auth.userId,
      source: result.source,
      updatedAtUtc: result.updatedAtUtc || updatedAtUtc,
      changed: result.changed !== false,
      bytes: result.bytes,
      viewUrl: viewUrl || null
    });
  }

  if (url.pathname === "/v1/me/yanm-state/component-state" && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const payload = await readJson(request);
    const componentStatePatch = normalizeYanmComponentStatePatch(payload);
    // 组件状态是服务端按 key 合并的显式变更，使用服务端时间避免设备时钟漂移
    // 把一个刚写入的补丁伪装成旧状态。
    const updatedAtUtc = isoNow();
    const result = await patchYanmComponentStateForUser(env, auth.userId, componentStatePatch, updatedAtUtc);
    const viewUrl = await getYanmStateViewUrl(env, auth.userId);

    return json({
      ok: true,
      userId: auth.userId,
      source: result.source,
      updatedAtUtc: result.updatedAtUtc || updatedAtUtc,
      changedKeys: result.changedKeys || Object.keys(componentStatePatch),
      changed: result.changed !== false,
      bytes: result.bytes,
      viewUrl: viewUrl || null
    });
  }

  return null;
}

  return { handleSyncApi };
}
