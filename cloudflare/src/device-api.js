// Domain implementation; dependencies are supplied by the composition root.
export function createDeviceApi(api) {
  const { DEVICE_MESSAGE_PROTOCOL, HttpError, acceptsAccountChat, accountLanLink, canonicalMessageJson, deviceNetworkLocation, ensureOwnedDevice, ensureUser, executionDeadline, expireDeviceCommands, getPendingDeviceMessageItems, isAccountChat, isExecutionMessage, isoNow, json, messageMatchesDevice, normalizeDeviceId, normalizeDeviceMessagePayload, normalizeDevicePayload, normalizeMessageId, normalizeMessageLimit, normalizeShortText, notifyDeviceRelay, ownedAttachment, parseJsonObject, randomHex, readJson, requireAuth, sendOfflinePush, serializeDeviceMessageRecord, serializeDeviceRecord, signToken, touchDevice, traceContext } = api;
async function handleDeviceApi(request, env, ctx) {
  const url = new URL(request.url);
  if (url.pathname === "/v1/me/devices" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    await ensureUser(env, auth.userId);

    const rows = await env.DB.prepare(
      `select
        device_id,
        platform,
        display_name,
        capabilities_json,
        last_seen_at,
        created_at,
        updated_at
      from user_devices
      where user_id = ? and coalesce(json_extract(capabilities_json, '$.disabled'), 0) <> 1
      order by updated_at desc`
    )
      .bind(auth.userId)
      .all();

    const serverNow = isoNow();
    const serverNowMs = Date.parse(serverNow);
    return json({
      ok: true,
      userId: auth.userId,
      serverNow,
      items: (rows.results ?? []).map((row) => serializeDeviceRecord(row, serverNowMs))
    });
  }

  if (url.pathname === "/v1/me/devices" && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const payload = await readJson(request);
    await ensureUser(env, auth.userId);

    const device = normalizeDevicePayload(payload);
    const existingDeviceOwner = await env.DB.prepare(
      `select user_id, capabilities_json
       from user_devices
       where device_id = ?`
    )
      .bind(device.deviceId)
      .first();
    if (existingDeviceOwner && String(existingDeviceOwner.user_id) !== auth.userId) {
      throw new HttpError(409, "device_id_taken", "Device ID is already bound to another account");
    }
    if (parseJsonObject(existingDeviceOwner?.capabilities_json).disabled === true)
      throw new HttpError(403, 'device_removed', 'This device registration has been removed');
    delete device.capabilities.disabled;
    delete device.capabilities.networkLocation;
    const location = deviceNetworkLocation(request);
    if (location) device.capabilities.networkLocation = location;

    const now = isoNow();
    await env.DB.prepare(
      `insert into user_devices (
        device_id,
        user_id,
        platform,
        display_name,
        push_token,
        capabilities_json,
        last_seen_at,
        created_at,
        updated_at
      ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
      on conflict(device_id) do update set
        platform = excluded.platform,
        display_name = excluded.display_name,
        push_token = coalesce(excluded.push_token, user_devices.push_token),
        capabilities_json = json_patch(user_devices.capabilities_json, excluded.capabilities_json),
        last_seen_at = excluded.last_seen_at,
        updated_at = excluded.updated_at`
    )
      .bind(
        device.deviceId,
        auth.userId,
        device.platform,
        device.displayName,
        device.pushToken,
        JSON.stringify(device.capabilities),
        now,
        now,
        now
      )
      .run();

    return json({
      ok: true,
      userId: auth.userId,
      device: {
        deviceId: device.deviceId,
        platform: device.platform,
        displayName: device.displayName,
        capabilities: device.capabilities,
        lastSeenAt: now
      }
    });
  }

  const removeDeviceMatch = url.pathname.match(/^\/v1\/me\/devices\/([^/]+)$/);
  if (removeDeviceMatch && request.method === 'DELETE') {
    const auth = await requireAuth(request, env);
    if (auth.grant) throw new HttpError(403, 'account_owner_required', 'Account login is required');
    const deviceId = normalizeDeviceId(decodeURIComponent(removeDeviceMatch[1]));
    await ensureOwnedDevice(env, auth.userId, deviceId);
    const now = isoNow();
    await env.DB.batch([
      env.DB.prepare(`UPDATE user_devices SET capabilities_json = json_set(capabilities_json, '$.disabled', json('true'), '$.autoAccountLan', json('false')), push_token = NULL, updated_at = ? WHERE user_id = ? AND device_id = ?`).bind(now, auth.userId, deviceId),
      env.DB.prepare('UPDATE device_credentials SET revoked_at = ? WHERE user_id = ? AND device_id = ? AND revoked_at IS NULL').bind(now, auth.userId, deviceId),
      env.DB.prepare(`UPDATE device_messages SET status = 'cancelled' WHERE user_id = ? AND target_device_id = ? AND status = 'pending'`).bind(auth.userId, deviceId)
    ]);
    if (env.DEVICE_RELAY) await env.DEVICE_RELAY.get(env.DEVICE_RELAY.idFromName(auth.userId)).revoke(auth.userId, deviceId);
    return json({ok:true, deviceId, removedAt:now});
  }

  if (url.pathname === '/v1/me/devices/lan-links' && request.method === 'POST') {
    const auth = await requireAuth(request, env);
    if (auth.grant) throw new HttpError(403, 'account_owner_required', 'Account login is required');
    const body = await readJson(request);
    const deviceId = normalizeDeviceId(body.deviceId);
    await ensureOwnedDevice(env, auth.userId, deviceId);
    const rows = (await env.DB.prepare('SELECT device_id, platform, display_name, capabilities_json FROM user_devices WHERE user_id = ?')
      .bind(auth.userId).all()).results || [];
    const source = rows.find(x => x.device_id === deviceId);
    if (!source || JSON.parse(source.capabilities_json || '{}').autoAccountLan !== true)
      throw new HttpError(409, 'account_lan_not_enabled', 'Device has not enabled account LAN');
    const peers = rows.filter(x => x.device_id !== deviceId && JSON.parse(x.capabilities_json || '{}').disabled !== true && JSON.parse(x.capabilities_json || '{}').autoAccountLan === true);
    if (peers.length > 128) throw new HttpError(409, 'peer_quota_exceeded', 'Too many LAN devices');
    const items = await Promise.all(peers.map(peer => accountLanLink(env.AUTH_TOKEN_SECRET, auth.userId, source, peer)));
    const response = json({ok:true, userId:auth.userId, items});
    response.headers.set('Cache-Control', 'no-store');
    return response;
  }

  const deviceCredentialMatch = url.pathname.match(/^\/v1\/me\/devices\/([^/]+)\/credentials$/);
  if (deviceCredentialMatch && request.method === 'GET') {
    const auth = await requireAuth(request, env);
    const deviceId = normalizeDeviceId(decodeURIComponent(deviceCredentialMatch[1]));
    await ensureOwnedDevice(env, auth.userId, deviceId);
    const rows = await env.DB.prepare('SELECT credential_id, application_id, scopes_json, targets_json, roots_json, expires_at, revoked_at, created_at FROM device_credentials WHERE user_id = ? AND device_id = ? ORDER BY created_at DESC LIMIT 100')
      .bind(auth.userId, deviceId).all();
    return json({ok:true, items:(rows.results || []).map(row => ({credentialId:row.credential_id, applicationId:row.application_id,
      scopes:JSON.parse(row.scopes_json), targetDeviceIds:JSON.parse(row.targets_json), fileRoots:JSON.parse(row.roots_json),
      expiresAt:row.expires_at, revokedAt:row.revoked_at, createdAt:row.created_at}))});
  }
  if (deviceCredentialMatch && request.method === 'POST') {
    const auth = await requireAuth(request, env);
    const deviceId = normalizeDeviceId(decodeURIComponent(deviceCredentialMatch[1]));
    await ensureOwnedDevice(env, auth.userId, deviceId);
    const body = await readJson(request);
    const scopes = body.scopes, targets = body.targetDeviceIds || [], roots = body.fileRoots || [];
    if (!Array.isArray(scopes) || scopes.length > 64 || scopes.some(x => typeof x !== 'string' ||
      !/^(?:device\.presence|messages\.receive|chat\.send|chat\.account|attachments\.send|attachments\.read|files\.read|files\.write|terminal\.execute|extension\.run:[a-zA-Z0-9_.-]{1,128}|capability\.invoke:[a-zA-Z0-9_.-]{1,128})$/.test(x)) ||
      !Array.isArray(targets) || targets.length > 128 || !Array.isArray(roots) || roots.length > 32 || roots.some(x => typeof x !== 'string' || x.length > 1000))
      throw new HttpError(400, 'invalid_device_grant', 'Invalid scopes, targets or resources');
    for (const target of targets) await ensureOwnedDevice(env, auth.userId, normalizeDeviceId(target));
    const applicationId = normalizeShortText(body.applicationId || 'device-runtime', 'application_id', 128);
    const credentialId = crypto.randomUUID();
    const now = Math.floor(Date.now()/1000);
    const seconds = Number(body.lifetimeSeconds ?? 86400);
    if (!Number.isSafeInteger(seconds) || seconds < 60 || seconds > 30 * 86400) throw new HttpError(400, 'invalid_credential_lifetime', 'Credential lifetime must be 60 seconds to 30 days');
    const expiresAt = now + seconds;
    await env.DB.prepare('INSERT INTO device_credentials VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL, ?)')
      .bind(credentialId, auth.userId, deviceId, applicationId, JSON.stringify(scopes), JSON.stringify(targets), JSON.stringify(roots), expiresAt, isoNow()).run();
    const accessToken = await signToken(env, {sub:auth.userId, username:auth.username, type:'device', deviceId, credentialId, iat:now, exp:expiresAt});
    return json({ok:true, credentialId, deviceId, applicationId, scopes, targetDeviceIds:targets, fileRoots:roots, accessToken, expiresAt});
  }
  const revokeDeviceCredential = url.pathname.match(/^\/v1\/me\/devices\/credentials\/([a-f0-9-]{36})$/);
  if (revokeDeviceCredential && request.method === 'DELETE') {
    const auth = await requireAuth(request, env);
    await env.DB.prepare('UPDATE device_credentials SET revoked_at = ? WHERE user_id = ? AND credential_id = ?')
      .bind(isoNow(), auth.userId, revokeDeviceCredential[1]).run();
    if (env.DEVICE_RELAY) await env.DEVICE_RELAY.get(env.DEVICE_RELAY.idFromName(auth.userId)).revokeCredential(auth.userId, revokeDeviceCredential[1]);
    return json({ok:true});
  }

  if (url.pathname === "/v1/me/devices/protocol" && request.method === "GET") {
    await requireAuth(request, env);
    return json(DEVICE_MESSAGE_PROTOCOL);
  }

  if (url.pathname === "/v1/me/mobile/messages" && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const payload = await readJson(request);
    await ensureUser(env, auth.userId);

    if (payload.protocolVersion != null && payload.protocolVersion !== DEVICE_MESSAGE_PROTOCOL.version)
      throw new HttpError(426, 'unsupported_message_protocol', 'Unsupported device messaging protocol version',
        { supportedVersions: DEVICE_MESSAGE_PROTOCOL.supportedVersions });
    const message = normalizeDeviceMessagePayload(payload);
    if (payload.routing === 'device' && !message.targetDeviceId) throw new HttpError(400, 'target_device_required', 'Device routing requires a target ID');
    delete message.payload.accountChat;
    // Only user chat is shared. Commands retain their explicit device/platform routing.
    if (!message.targetDeviceId && ['text', 'photo', 'file', 'screenshot'].includes(message.kind) &&
        (payload.routing === 'account-chat' || message.title === 'YanziChat' || ['android', 'android-mobile', 'desktop-chat'].includes(message.payload.source))) {
      message.payload.accountChat = true;
    }
    if (message.payload.attachmentId) {
      const attachment = await ownedAttachment(env, auth.userId, message.payload.attachmentId);
      if (!message.expiresAt || message.expiresAt > attachment.expires_at) message.expiresAt = attachment.expires_at;
    }
    const clientId = payload.clientMessageId;
    if (clientId && (typeof clientId !== 'string' || !/^[a-zA-Z0-9_-]{8,100}$/.test(clientId)))
      throw new HttpError(400, 'invalid_client_message_id', 'Invalid client message ID');
    let context;
    try { context = traceContext(payload, clientId); }
    catch (error) { throw new HttpError(400, 'invalid_trace_context', error.message); }
    const explicitTrace = Object.fromEntries(['operationId', 'correlationId', 'causationId', 'traceId']
      .filter(name => payload[name] != null).map(name => [name, payload[name]]));
    const hasExplicitTrace = Object.keys(explicitTrace).length > 0;
    const requestHash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify(message)))))
      .map(x => x.toString(16).padStart(2, '0')).join('');
    const canonicalHash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonicalMessageJson(hasExplicitTrace ? {...message, traceContext:explicitTrace} : message)))))
      .map(x => x.toString(16).padStart(2, '0')).join('');
    const existingMessage = async () => {
      if (!clientId) return null;
      const prior = await env.DB.prepare('SELECT message_id, request_hash FROM mobile_message_idempotency WHERE user_id = ? AND source_device_id = ? AND client_message_id = ?')
        .bind(auth.userId, message.sourceDeviceId || '', clientId).first();
      // Keep exact legacy hashes valid while new messages use canonical JSON.
      if (prior && (hasExplicitTrace || prior.request_hash !== requestHash) && prior.request_hash !== canonicalHash)
        throw new HttpError(409, 'message_id_reused', 'Client message ID was reused with different content');
      return prior;
    };
    const prior = await existingMessage();
    if (prior) return json({ ok: true, messageId: prior.message_id, deduplicated: true });
    if (message.sourceDeviceId) {
      await ensureOwnedDevice(env, auth.userId, message.sourceDeviceId);
      await touchDevice(env, auth.userId, message.sourceDeviceId, request);
    }

    if (message.targetDeviceId) {
      await ensureOwnedDevice(env, auth.userId, message.targetDeviceId);
    } else if (isExecutionMessage(message.kind)) {
      const targets = (await env.DB.prepare("SELECT device_id FROM user_devices WHERE user_id = ? AND platform = ? AND coalesce(json_extract(capabilities_json, '$.disabled'), 0) <> 1")
        .bind(auth.userId, message.targetPlatform).all()).results || [];
      if (targets.length !== 1)
        throw new HttpError(409, 'target_device_required', 'Execution requests require one explicit target device',
          { targetPlatform: message.targetPlatform, candidateDeviceIds: targets.map(item => item.device_id) });
      message.targetDeviceId = targets[0].device_id;
      message.targetPlatform = null;
    }

    message.payload.messageContext = context;
    delete message.payload.authorization;
    message.payload.authorization = auth.grant ? {type:"device-grant", credentialId:auth.grant.credentialId, applicationId:auth.grant.applicationId, scopes:auth.grant.scopes, fileRoots:auth.grant.roots} : {type:"account-owner"};
    if (isExecutionMessage(message.kind) && !message.payload.clientOperationId)
      message.payload.clientOperationId = context.operationId;
    message.expiresAt = executionDeadline(message.kind, message.expiresAt);
    if (message.expiresAt && message.expiresAt <= isoNow())
      throw new HttpError(410, 'message_expired', 'Message deadline has already elapsed');
    const now = isoNow();
    if (message.payload.accountChat && !message.expiresAt)
      message.expiresAt = new Date(Date.now() + 30 * 86400000).toISOString();
    const messageId = `msg_${randomHex(12)}`;
    const insertMessage = env.DB.prepare(
      `insert into device_messages (
        message_id,
        user_id,
        source_device_id,
        target_device_id,
        target_platform,
        kind,
        title,
        body_text,
        payload_json,
        status,
        created_at,
        expires_at
      ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending', ?, ?)`
    )
      .bind(
        messageId,
        auth.userId,
        message.sourceDeviceId,
        message.targetDeviceId,
        message.targetPlatform,
        message.kind,
        message.title,
        message.bodyText,
        JSON.stringify(message.payload),
        now,
        message.expiresAt
      )
      ;
    try {
      if (clientId) await env.DB.batch([insertMessage, env.DB.prepare('INSERT INTO mobile_message_idempotency VALUES (?, ?, ?, ?, ?)')
        .bind(auth.userId, message.sourceDeviceId || '', clientId, messageId, canonicalHash)]);
      else await insertMessage.run();
    } catch (error) {
      const raced = await existingMessage();
      if (raced) return json({ ok: true, messageId: raced.message_id, deduplicated: true });
      throw error;
    }
    const insertedSequence = await env.DB.prepare('SELECT sequence FROM device_message_sequence WHERE user_id = ? AND message_id = ?').bind(auth.userId, messageId).first();
    const event = { type: 'message', userId: auth.userId, message: { ...context, sequence:insertedSequence.sequence, messageId, sourceDeviceId: message.sourceDeviceId,
      targetDeviceId: message.targetDeviceId, targetPlatform: message.targetPlatform, kind: message.kind,
      title: message.title, text: message.bodyText, payload: message.payload, expiresAt: message.expiresAt, protocolVersion: DEVICE_MESSAGE_PROTOCOL.version, createdAt: now } };
    await notifyDeviceRelay(env, auth.userId, event);
    ctx.waitUntil(sendOfflinePush(env, auth.userId, event.message));

    return json({
      ok: true,
      userId: auth.userId,
      messageId,
      createdAt: now
    });
  }

  if (url.pathname === "/v1/me/mobile/messages/ws" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const deviceId = normalizeDeviceId(url.searchParams.get("deviceId"));
    await ensureUser(env, auth.userId);
    const relayDevice = await ensureOwnedDevice(env, auth.userId, deviceId);

    if (!env.DEVICE_RELAY) {
      throw new HttpError(503, "relay_unavailable", "Device relay is not configured");
    }

    const relayHeaders = new Headers(request.headers);
    relayHeaders.set('X-Yanzi-Relay-User', auth.userId);
    relayHeaders.set('X-Yanzi-Relay-Device', deviceId);
    relayHeaders.set('X-Yanzi-Relay-Platform', relayDevice.platform);
    relayHeaders.set('X-Yanzi-Relay-Credential', auth.grant?.credentialId || '');
    relayHeaders.set('X-Yanzi-Relay-Expires', String(auth.grant?.expiresAt || 0));
    relayHeaders.set('X-Yanzi-Relay-Account-Chat', String(acceptsAccountChat({platform:relayDevice.platform,
      capabilities:relayDevice.capabilities})));
    return env.DEVICE_RELAY.get(env.DEVICE_RELAY.idFromName(auth.userId)).fetch(new Request(request, { headers: relayHeaders }));
  }

  if (url.pathname === "/v1/me/mobile/messages/events" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const deviceId = normalizeDeviceId(url.searchParams.get("deviceId"));
    await ensureUser(env, auth.userId);
    const device = await ensureOwnedDevice(env, auth.userId, deviceId);
    await touchDevice(env, auth.userId, deviceId, request);

    const { readable, writable } = new TransformStream();
    const writer = writable.getWriter();
    const encoder = new TextEncoder();

    let isClosed = false;

    request.signal.addEventListener("abort", () => {
      isClosed = true;
    });

    const sendSseMessage = async (data) => {
      try {
        await writer.write(encoder.encode(`data: ${JSON.stringify(data)}\n\n`));
      } catch (err) {
        isClosed = true;
      }
    };

    const pollIntervalMs = 2000;
    const maxStreamDurationMs = 30 * 60 * 1000;
    const startedAt = Date.now();
    const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    (async () => {
      try {
        await sendSseMessage({ type: "connected" });

        while (!isClosed && Date.now() - startedAt < maxStreamDurationMs) {
          const items = await getPendingDeviceMessageItems(env, auth.userId, deviceId, device.platform, 100, device.capabilities);
          if (items.length > 0) {
            await sendSseMessage({ type: "messages", items });

            const deliveredAt = isoNow();
            await env.DB.prepare(
              `update device_messages
               set delivered_at = coalesce(delivered_at, ?)
               where user_id = ?
                 and message_id in (${items.map(() => "?").join(",")})`
            )
              .bind(deliveredAt, auth.userId, ...items.map((item) => item.messageId))
              .run();
          }

          if (isClosed || Date.now() - startedAt >= maxStreamDurationMs) {
            break;
          }

          await sleep(pollIntervalMs);
        }
      } catch (err) {
        // SSE loop exception
      } finally {
        isClosed = true;
        try {
          await writer.close();
        } catch (e) {}
      }
    })();

    return new Response(readable, {
      headers: {
        "Content-Type": "text/event-stream",
        "Cache-Control": "no-cache",
        "Connection": "keep-alive",
        "Access-Control-Allow-Origin": "*"
      }
    });
  }

  if (url.pathname === "/v1/me/mobile/messages" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const deviceId = normalizeDeviceId(url.searchParams.get("deviceId"));

    const limit = normalizeMessageLimit(url.searchParams.get("limit"));
    await ensureUser(env, auth.userId);
    const device = await ensureOwnedDevice(env, auth.userId, deviceId);
    await touchDevice(env, auth.userId, deviceId, request);

    const after = Number(url.searchParams.get('after') || 0);
    if (!Number.isSafeInteger(after) || after < 0) throw new HttpError(400, 'invalid_cursor', 'after must be a nonnegative integer');
    const items = await getPendingDeviceMessageItems(env, auth.userId, deviceId, device.platform, limit + 1, device.capabilities, after);
    const hasMore = items.length > limit;
    if (hasMore) items.pop();
    if (items.length > 0) {
      const deliveredAt = isoNow();
      await env.DB.prepare(
        `update device_messages
         set delivered_at = coalesce(delivered_at, ?)
         where user_id = ?
           and message_id in (${items.map(() => "?").join(",")})`
      )
        .bind(deliveredAt, auth.userId, ...items.map((item) => item.messageId))
        .run();
    }

    return json({
      ok: true,
      userId: auth.userId,
      deviceId,
      items,
      nextCursor: items.length ? items[items.length - 1].sequence : after,
      hasMore,
      serverNow: isoNow()
    });
  }

  const lifecycleMatch = url.pathname.match(/^\/v1\/me\/mobile\/messages\/([^/]+)\/(cancel|claim)$/);
  if (lifecycleMatch && request.method === 'POST') {
    const auth = await requireAuth(request, env);
    const messageId = normalizeMessageId(decodeURIComponent(lifecycleMatch[1]));
    const payload = await readJson(request);
    await expireDeviceCommands(env, auth.userId);
    const row = await env.DB.prepare('SELECT * FROM device_messages WHERE user_id = ? AND message_id = ?')
      .bind(auth.userId, messageId).first();
    if (!row) throw new HttpError(404, 'message_not_found', 'Message not found');
    if (!isExecutionMessage(row.kind)) throw new HttpError(400, 'not_execution_message', 'Only execution messages support this lifecycle');
    if (lifecycleMatch[2] === 'cancel') {
      const source = normalizeDeviceId(payload.deviceId);
      await ensureOwnedDevice(env, auth.userId, source);
      if (row.source_device_id !== source) throw new HttpError(403, 'sender_required', 'Only the sending device can cancel');
      const changed = await env.DB.prepare("UPDATE device_messages SET status = 'cancelled', acked_at = ? WHERE user_id = ? AND message_id = ? AND status = 'pending'")
        .bind(isoNow(), auth.userId, messageId).run();
      const latest = await env.DB.prepare('SELECT status FROM device_messages WHERE user_id = ? AND message_id = ?').bind(auth.userId, messageId).first();
      if (!changed.meta?.changes && latest.status !== 'cancelled')
        throw new HttpError(409, 'message_not_cancellable', 'Execution has begun or the message is already terminal', {status:latest.status});
      await notifyDeviceRelay(env, auth.userId, {type:'receipt', sourceDeviceId:source, messageId, status:'cancelled'});
      return json({ok:true, messageId, status:'cancelled'});
    }
    const deviceId = normalizeDeviceId(payload.deviceId);
    await ensureOwnedDevice(env, auth.userId, deviceId);
    if (row.target_device_id !== deviceId) throw new HttpError(404, 'message_not_found', 'Command is not addressed to this device');
    const changed = await env.DB.prepare("UPDATE device_messages SET status = 'executing' WHERE user_id = ? AND message_id = ? AND target_device_id = ? AND status = 'pending' AND expires_at > ?")
      .bind(auth.userId, messageId, deviceId, isoNow()).run();
    const latest = await env.DB.prepare('SELECT status FROM device_messages WHERE user_id = ? AND message_id = ?').bind(auth.userId, messageId).first();
    // A lost claim response is deliberately uncertain: retry must not execute a second time.
    return json({ok:true, messageId, acquired:!!changed.meta?.changes, status:latest.status, expiresAt:row.expires_at});
  }

  const deviceMessageAckMatch = url.pathname.match(/^\/v1\/me\/mobile\/messages\/([^/]+)\/ack$/);
  if (deviceMessageAckMatch && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const messageId = normalizeMessageId(decodeURIComponent(deviceMessageAckMatch[1]));
    const payload = await readJson(request);
    const deviceId = normalizeDeviceId(payload.deviceId);
    await ensureUser(env, auth.userId);
    const ackDevice = await ensureOwnedDevice(env, auth.userId, deviceId);
    await touchDevice(env, auth.userId, deviceId, request);

    await expireDeviceCommands(env, auth.userId);
    const success = payload.success;
    const ackMessage = await env.DB.prepare('SELECT source_device_id, target_device_id, target_platform, payload_json, status, kind FROM device_messages WHERE user_id = ? AND message_id = ?')
      .bind(auth.userId, messageId).first();
    const ackEnvelope = ackMessage && {sourceDeviceId:ackMessage.source_device_id, targetDeviceId:ackMessage.target_device_id,
      targetPlatform:ackMessage.target_platform, payload:parseJsonObject(ackMessage.payload_json)};
    const accountChat = ackEnvelope && isAccountChat(ackEnvelope);
    if (!ackEnvelope || !messageMatchesDevice(ackEnvelope, {deviceId, platform:ackDevice.platform,
      capabilities:ackDevice.capabilities}))
      throw new HttpError(404, 'message_not_found', 'Message is not addressed to this device');
    if (['expired', 'cancelled'].includes(ackMessage.status))
      return json({ok:true, messageId, acked:false, status:ackMessage.status});
    if (isExecutionMessage(ackMessage.kind) && success === undefined)
      throw new HttpError(400, 'execution_result_required', 'Commands require an execution result; use claim to save or begin execution');
    const resultText = payload.result || "";

    let newStatus = "acked";
    let updatedPayloadJson = null;

    if (success !== undefined) {
      if (payload.resultState !== undefined && !['unknown', 'executed'].includes(payload.resultState))
        throw new HttpError(400, 'invalid_result_state', 'Unsupported execution result state');
      newStatus = payload.resultState === 'unknown' ? 'unknown' : success ? "completed" : "failed";
      const msgRow = await env.DB.prepare(
        "select payload_json from device_messages where user_id = ? and message_id = ?"
      )
        .bind(auth.userId, messageId)
        .first();

      let originPayload = {};
      try {
        if (msgRow && msgRow.payload_json) {
          originPayload = JSON.parse(msgRow.payload_json);
        }
      } catch (e) {}

      originPayload.executionResult = {
        state: newStatus === 'unknown' ? 'unknown' : 'executed',
        success: !!success,
        output: resultText,
        time: isoNow()
      };
      updatedPayloadJson = JSON.stringify(originPayload);
    }

    let result;
    if (accountChat) {
      await env.DB.prepare(`INSERT INTO account_chat_receipts (user_id, message_id, device_id, status, acked_at)
        VALUES (?, ?, ?, ?, ?) ON CONFLICT(user_id, message_id, device_id)
        DO UPDATE SET status = excluded.status, acked_at = excluded.acked_at
        WHERE account_chat_receipts.status NOT IN ('completed', 'failed')
          AND (account_chat_receipts.status != 'acked' OR excluded.status IN ('completed', 'failed'))`)
        .bind(auth.userId, messageId, deviceId, newStatus, isoNow()).run();
    }
    if (updatedPayloadJson) {
      result = await env.DB.prepare(
        `update device_messages
         set status = ?,
             delivered_at = coalesce(delivered_at, ?),
             acked_at = ?,
             payload_json = ?
         where user_id = ?
           and message_id = ?
           and status IN ('pending', 'executing', 'unknown')`
      )
        .bind(newStatus, isoNow(), isoNow(), updatedPayloadJson, auth.userId, messageId)
        .run();
    } else {
      result = await env.DB.prepare(
        `update device_messages
         set status = 'acked',
             delivered_at = coalesce(delivered_at, ?),
             acked_at = ?
         where user_id = ?
           and message_id = ?
           and status IN ('pending', 'executing', 'unknown')`
      )
        .bind(isoNow(), isoNow(), auth.userId, messageId)
        .run();
    }

    const finalState = await env.DB.prepare('SELECT status FROM device_messages WHERE user_id = ? AND message_id = ?').bind(auth.userId, messageId).first();
    const deviceReceipt = accountChat ? await env.DB.prepare('SELECT status FROM account_chat_receipts WHERE user_id = ? AND message_id = ? AND device_id = ?').bind(auth.userId, messageId, deviceId).first() : null;
    await notifyDeviceRelay(env, auth.userId, { type: 'receipt', userId: auth.userId, sourceDeviceId: ackMessage.source_device_id,
      messageId, deviceId, status: accountChat ? deviceReceipt.status : finalState.status, serverNow: isoNow() });
    return json({
      ok: true,
      userId: auth.userId,
      messageId,
      acked: accountChat || Number(result.meta?.changes ?? 0) > 0,
      status: accountChat ? deviceReceipt.status : finalState.status
    });
  }

  const singleMessageMatch = url.pathname.match(/^\/v1\/me\/mobile\/messages\/([^/]+)$/);
  if (singleMessageMatch && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const messageId = normalizeMessageId(decodeURIComponent(singleMessageMatch[1]));
    await ensureUser(env, auth.userId);

    await expireDeviceCommands(env, auth.userId);
    const row = await env.DB.prepare(
      `select
        message_id,
        source_device_id,
        target_device_id,
        target_platform,
        kind,
        title,
        body_text,
        payload_json,
        status,
        created_at,
        delivered_at,
        acked_at,
        expires_at
      from device_messages
      where user_id = ?
        and message_id = ?`
    )
      .bind(auth.userId, messageId)
      .first();

    if (!row) {
      throw new HttpError(404, "message_not_found", "Message not found");
    }

    const record = serializeDeviceMessageRecord(row);
    if (record.payload.accountChat === true) {
      const receipts = await env.DB.prepare(`SELECT r.device_id, d.display_name, r.status, r.acked_at
        FROM account_chat_receipts r LEFT JOIN user_devices d
        ON d.user_id = r.user_id AND d.device_id = r.device_id
        WHERE r.user_id = ? AND r.message_id = ? ORDER BY r.acked_at`)
        .bind(auth.userId, messageId).all();
      record.receipts = (receipts.results || []).map(r => ({deviceId:r.device_id, displayName:r.display_name,
        status:r.status, ackedAt:r.acked_at}));
    }
    return json(record);
  }

  return null;
}

  return { handleDeviceApi };
}
