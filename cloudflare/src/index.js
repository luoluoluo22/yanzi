const mobilePollRateLimitMap = new Map();
const mobilePollRateLimitMaxEntries = 5000;
const TOKEN_TTL_SECONDS = 60 * 60 * 24 * 30;
const PASSWORD_ITERATIONS = 100000;
const VERIFICATION_CODE_TTL_MINUTES = 10;
const PUBLIC_SITE_ORIGIN = "https://yanzi.luoluoluo.cc.cd";
const DEFAULT_APP_UPDATE_CHANNEL = "stable";
const DEFAULT_APP_RELEASE = {
  channel: DEFAULT_APP_UPDATE_CHANNEL,
  version: "0.1.0",
  title: "燕子启动器 for Windows",
  notes: "默认稳定版下载渠道。",
  download_url: "https://wwbnh.lanzout.com/b0pnkaj6j",
  file_name: "YanziSetup-0.1.0.exe",
  download_code: "62yn",
  provider: "lanzou",
  sha256: "",
  published_at: "2026-05-04T00:00:00.000Z"
};
const PUBLIC_STORE_EXTENSIONS = [
  {
    extension_id: "open-yanzi-homepage",
    display_name: "打开燕子官网",
    latest_version: "1.0.0",
    description: "一键打开燕子官网首页。",
    category: "测试扩展",
    keywords: ["yanzi", "官网", "测试", "open-yanzi-homepage"],
    icon: `${PUBLIC_SITE_ORIGIN}/assets/logo-white-transparent.png`,
    package_path: "/downloads/open-yanzi-homepage.zip"
  },
  {
    extension_id: "open-yanzi-github",
    display_name: "打开燕子 GitHub",
    latest_version: "1.0.0",
    description: "一键打开燕子 GitHub 仓库。",
    category: "测试扩展",
    keywords: ["yanzi", "github", "测试", "open-yanzi-github"],
    icon: "https://github.githubassets.com/favicons/favicon.png",
    package_path: "/downloads/open-yanzi-github.zip"
  }
];
const PUBLIC_STORE_EXTENSION_MAP = new Map(
  PUBLIC_STORE_EXTENSIONS.map((item) => [item.extension_id, item])
);
const PUBLIC_STORE_EXTENSION_IDS_SQL = PUBLIC_STORE_EXTENSIONS
  .map((item) => `'${item.extension_id.replace(/'/g, "''")}'`)
  .join(", ");


export default {
  async fetch(request, env) {
    try {
      return await handleRequest(request, env);
    } catch (error) {
      if (error instanceof HttpError) {
        return withCors(
          json(
            {
              error: error.code,
              message: error.message,
              ...(error.details ? { details: error.details } : {})
            },
            error.status
          )
        );
      }

      return withCors(
        json(
          {
            error: "internal_error",
            message: error instanceof Error ? error.message : "Unknown error"
          },
          500
        )
      );
    }
  }
};

async function handleRequest(request, env) {
  const url = new URL(request.url);

  if (request.method === "OPTIONS") {
    return withCors(new Response(null, { status: 204 }));
  }

  if (url.pathname.startsWith("/downloads/") && request.method === "GET") {
    const key = url.pathname.substring(1);
    const object = await env.PACKAGES.get(key);
    if (!object) {
      return new Response("File not found in storage bucket", { status: 404 });
    }
    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("etag", object.httpEtag || "");
    headers.set("Content-Type", "application/zip");
    headers.set("Access-Control-Allow-Origin", "*");
    return new Response(object.body, { headers });
  }

  if (url.pathname === "/v1/debug/list-packages" && request.method === "GET") {
    const list = await env.PACKAGES.list();
    const keys = list.objects.map(obj => obj.key);
    return json({ count: keys.length, keys });
  }

  if (url.pathname === "/health") {
    const result = await env.DB.prepare("select datetime('now') as now").first();
    return json({ ok: true, now: result?.now ?? null });
  }

  if (url.pathname === "/v1/auth/register" && request.method === "POST") {
    const payload = await readJson(request);
    const email = normalizeEmail(payload.email);
    const username = normalizeUsername(payload.username);
    const password = validatePassword(payload.password);
    const code = normalizeVerificationCode(payload.code);
    const now = isoNow();
    const userId = `usr_${randomHex(8)}`;

    const existing = await env.DB.prepare(
      "select user_id from auth_users where username = ? or email = ?"
    )
      .bind(username, email)
      .first();

    if (existing) {
      const existingByUsername = await env.DB.prepare(
        "select user_id from auth_users where username = ?"
      )
        .bind(username)
        .first();
      if (existingByUsername) {
        throw new HttpError(409, "username_taken", "Username already exists");
      }

      throw new HttpError(409, "email_taken", "Email already exists");
    }

    const verification = await env.DB.prepare(
      `select
        email,
        username,
        code_hash,
        code_salt,
        expires_at
      from auth_email_verifications
      where email = ?`
    )
      .bind(email)
      .first();

    if (!verification) {
      throw new HttpError(400, "verification_required", "Email verification is required");
    }

    if (String(verification.username).trim().toLowerCase() !== username.trim().toLowerCase()) {
      throw new HttpError(400, "verification_mismatch", "Verification code does not match this username");
    }

    if (Date.parse(String(verification.expires_at)) <= Date.now()) {
      throw new HttpError(400, "verification_expired", "Verification code expired");
    }

    const codeHash = await hashVerificationCode(email, code, verification.code_salt);
    if (codeHash !== verification.code_hash) {
      throw new HttpError(400, "invalid_verification_code", "Invalid verification code");
    }

    const passwordSalt = randomHex(16);
    const passwordHash = await hashPassword(password, passwordSalt, PASSWORD_ITERATIONS);

    await env.DB.batch([
      env.DB.prepare(
        `insert into users (user_id, created_at, updated_at)
         values (?, ?, ?)`
      ).bind(userId, now, now),
      env.DB.prepare(
        `insert into auth_users (
          user_id,
          username,
          email,
          email_verified_at,
          password_hash,
          password_salt,
          password_iterations,
          created_at,
          updated_at
        ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).bind(
        userId,
        username,
        email,
        now,
        passwordHash,
        passwordSalt,
        PASSWORD_ITERATIONS,
        now,
        now
      ),
      env.DB.prepare(
        `delete from auth_email_verifications
         where email = ?`
      ).bind(email)
    ]);

    return json(await buildAuthResponse(env, { userId, username, email }));
  }

  if (url.pathname === "/v1/auth/send-code" && request.method === "POST") {
    const payload = await readJson(request);
    const email = normalizeEmail(payload.email);
    const username = normalizeUsername(payload.username);

    const existing = await env.DB.prepare(
      "select user_id from auth_users where username = ? or email = ?"
    )
      .bind(username, email)
      .first();

    if (existing) {
      const existingByUsername = await env.DB.prepare(
        "select user_id from auth_users where username = ?"
      )
        .bind(username)
        .first();
      if (existingByUsername) {
        throw new HttpError(409, "username_taken", "Username already exists");
      }

      throw new HttpError(409, "email_taken", "Email already exists");
    }

    const code = generateVerificationCode();
    const salt = randomHex(8);
    const codeHash = await hashVerificationCode(email, code, salt);
    const now = isoNow();
    const expiresAt = new Date(Date.now() + VERIFICATION_CODE_TTL_MINUTES * 60 * 1000).toISOString();

    await env.DB.prepare(
      `insert into auth_email_verifications (
        email,
        username,
        code_hash,
        code_salt,
        expires_at,
        created_at,
        updated_at
      ) values (?, ?, ?, ?, ?, ?, ?)
      on conflict(email) do update set
        username = excluded.username,
        code_hash = excluded.code_hash,
        code_salt = excluded.code_salt,
        expires_at = excluded.expires_at,
        updated_at = excluded.updated_at`
    )
      .bind(email, username, codeHash, salt, expiresAt, now, now)
      .run();

    await sendVerificationEmail(env, email, username, code);
    return json({
      ok: true,
      email,
      expiresInSeconds: VERIFICATION_CODE_TTL_MINUTES * 60
    });
  }

  if (url.pathname === "/v1/auth/login" && request.method === "POST") {
    const payload = await readJson(request);
    const email = normalizeEmail(payload.email || payload.username);
    const password = validatePassword(payload.password);
    const authVersion = payload.authVersion || "";
    const legacyPassword = payload.legacyPassword ? validatePassword(payload.legacyPassword) : null;

    const user = await env.DB.prepare(
      `select
        user_id,
        username,
        email,
        password_hash,
        password_salt,
        password_iterations
      from auth_users
      where email = ?`
    )
      .bind(email)
      .first();

    if (!user) {
      throw new HttpError(404, "user_not_found", "User does not exist");
    }

    const salt = user.password_salt;
    const iterations = Number(user.password_iterations || PASSWORD_ITERATIONS);
    let authenticated = false;
    let silentUpgradeRequired = false;
    let finalLoginHash = "";

    const isLoginHash = typeof password === "string" && /^[0-9a-f]{64}$/i.test(password);

    if (isLoginHash) {
      const hashAsUpgraded = await hashPassword(password, salt, iterations);
      if (hashAsUpgraded === user.password_hash) {
        authenticated = true;
      } else if (legacyPassword) {
        const hashAsLegacy = await hashPassword(legacyPassword, salt, iterations);
        if (hashAsLegacy === user.password_hash) {
          authenticated = true;
          silentUpgradeRequired = true;
          finalLoginHash = password;
        }
      }
    } else {
      const hashAsLegacy = await hashPassword(password, salt, iterations);
      if (hashAsLegacy === user.password_hash) {
        authenticated = true;
      } else {
        const serverDerivedLoginHash = await deriveLoginHashInServer(password, email);
        const hashAsUpgraded = await hashPassword(serverDerivedLoginHash, salt, iterations);
        if (hashAsUpgraded === user.password_hash) {
          authenticated = true;
        }
      }
    }

    if (!authenticated) {
      throw new HttpError(401, "invalid_credentials", "Invalid email or password");
    }

    if (silentUpgradeRequired && finalLoginHash) {
      const newHash = await hashPassword(finalLoginHash, salt, iterations);
      await env.DB.prepare(
        `update auth_users set password_hash = ? where user_id = ?`
      )
        .bind(newHash, user.user_id)
        .run();
    }

    await touchUser(env, user.user_id);
    return json(await buildAuthResponse(env, { userId: user.user_id, username: user.username, email: user.email }));
  }

  if (url.pathname === "/v1/auth/send-reset-code" && request.method === "POST") {
    const payload = await readJson(request);
    const email = normalizeEmail(payload.email);
    const user = await env.DB.prepare(
      `select
        user_id,
        username,
        email
      from auth_users
      where email = ?`
    )
      .bind(email)
      .first();

    if (!user) {
      throw new HttpError(404, "email_not_found", "Email does not exist");
    }

    const code = generateVerificationCode();
    const salt = randomHex(8);
    const codeHash = await hashVerificationCode(email, code, salt);
    const now = isoNow();
    const expiresAt = new Date(Date.now() + VERIFICATION_CODE_TTL_MINUTES * 60 * 1000).toISOString();

    await env.DB.prepare(
      `insert into auth_password_resets (
        email,
        user_id,
        code_hash,
        code_salt,
        expires_at,
        created_at,
        updated_at
      ) values (?, ?, ?, ?, ?, ?, ?)
      on conflict(email) do update set
        user_id = excluded.user_id,
        code_hash = excluded.code_hash,
        code_salt = excluded.code_salt,
        expires_at = excluded.expires_at,
        updated_at = excluded.updated_at`
    )
      .bind(email, user.user_id, codeHash, salt, expiresAt, now, now)
      .run();

    await sendPasswordResetEmail(env, email, user.username, code);
    return json({
      ok: true,
      email,
      expiresInSeconds: VERIFICATION_CODE_TTL_MINUTES * 60
    });
  }

  if (url.pathname === "/v1/auth/reset-password" && request.method === "POST") {
    const payload = await readJson(request);
    const email = normalizeEmail(payload.email);
    const password = validatePassword(payload.password);
    const code = normalizeVerificationCode(payload.code);

    const reset = await env.DB.prepare(
      `select
        email,
        user_id,
        code_hash,
        code_salt,
        expires_at
      from auth_password_resets
      where email = ?`
    )
      .bind(email)
      .first();

    if (!reset) {
      throw new HttpError(400, "reset_required", "Password reset verification is required");
    }

    if (Date.parse(String(reset.expires_at)) <= Date.now()) {
      throw new HttpError(400, "verification_expired", "Verification code expired");
    }

    const codeHash = await hashVerificationCode(email, code, reset.code_salt);
    if (codeHash !== reset.code_hash) {
      throw new HttpError(400, "invalid_verification_code", "Invalid verification code");
    }

    const user = await env.DB.prepare(
      `select
        user_id,
        username,
        email
      from auth_users
      where user_id = ? and email = ?`
    )
      .bind(reset.user_id, email)
      .first();

    if (!user) {
      throw new HttpError(404, "user_not_found", "User does not exist");
    }

    const passwordSalt = randomHex(16);
    const passwordHash = await hashPassword(password, passwordSalt, PASSWORD_ITERATIONS);
    const now = isoNow();

    await env.DB.batch([
      env.DB.prepare(
        `update auth_users
         set password_hash = ?,
             password_salt = ?,
             password_iterations = ?,
             updated_at = ?
         where user_id = ?`
      ).bind(passwordHash, passwordSalt, PASSWORD_ITERATIONS, now, user.user_id),
      env.DB.prepare(
        `delete from auth_password_resets
         where email = ?`
      ).bind(email),
      env.DB.prepare(
        `update users
         set updated_at = ?
         where user_id = ?`
      ).bind(now, user.user_id)
    ]);

    return json(await buildAuthResponse(env, { userId: user.user_id, username: user.username, email: user.email }));
  }

  if (url.pathname === "/v1/auth/me" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    return json({
      userId: auth.userId,
      username: auth.username,
      email: auth.email,
      isAdmin: isAdminUser(auth, env)
    });
  }

  if (url.pathname === "/v1/sync/capabilities" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    await ensureUser(env, auth.userId);
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
  if (syncObjectMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const objectId = normalizeSyncObjectId(decodeURIComponent(syncObjectMatch[1]));
    const payload = await readJson(request);
    const result = await writeUserSyncObject(env, auth.userId, objectId, payload);
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

  if (url.pathname === "/v1/app/update/latest" && request.method === "GET") {
    try {
      const ghResponse = await fetch("https://api.github.com/repos/luoluoluo22/yanzi/releases/latest", {
        headers: {
          "User-Agent": "Yanzi-Updater-Worker"
        }
      });
      if (!ghResponse.ok) {
        throw new Error(`GitHub API returned status ${ghResponse.status}`);
      }
      const data = await ghResponse.json();

      // 寻找 .exe 结尾的 asset 作为 Windows 的安装包
      const winAsset = (data.assets || []).find(asset => asset.name.endsWith(".exe"));
      const version = data.tag_name ? data.tag_name.replace(/^v/, "") : "";

      const payload = {
        channel: "stable",
        version: version,
        title: data.name || `燕子启动器 v${version}`,
        notes: data.body || "",
        download_url: winAsset ? winAsset.browser_download_url : `https://github.com/luoluoluo22/yanzi/releases/download/${data.tag_name}/Yanzi-win-Setup-${version}.exe`,
        file_name: winAsset ? winAsset.name : `Yanzi-win-Setup-${version}.exe`,
        download_code: "",
        provider: "github",
        sha256: "",
        published_at: data.published_at || new Date().toISOString()
      };

      return withCors(json(payload));
    } catch (err) {
      console.error("Fetch GitHub releases failed:", err);
      // 容错：如果 GitHub 接口报错，返回一个兜底的 0.2.15 配置
      return withCors(json({
        channel: "stable",
        version: "0.3.5",
        title: "燕子启动器 v0.3.5",
        notes: "从 GitHub 抓取最新版失败，已启用本地缓存兜底",
        download_url: "https://github.com/luoluoluo22/yanzi/releases/download/v0.3.5/Yanzi-win-Setup-0.3.5.exe",
        file_name: "Yanzi-win-Setup-0.3.5.exe",
        download_code: "",
        provider: "github",
        sha256: "",
        published_at: "2026-08-20T02:00:00Z"
      }));
    }
  }

  if (url.pathname === "/v1/extensions" && request.method === "GET") {
    const search = String(url.searchParams.get("q") || "").trim().toLowerCase();
    const requestedPage = Number.parseInt(String(url.searchParams.get("page") || "1"), 10);
    const requestedPageSize = Number.parseInt(String(url.searchParams.get("pageSize") || "24"), 10);
    const page = Number.isFinite(requestedPage) && requestedPage > 0 ? requestedPage : 1;
    const pageSize = Number.isFinite(requestedPageSize)
      ? Math.max(1, Math.min(requestedPageSize, 60))
      : 24;
    const publicExtensionRows = await env.DB.prepare(
      `select
        extension_id,
        display_name,
        latest_version,
        manifest_json,
        icon_key,
        archive_key,
        archive_sha256,
        publisher_user_id,
        publisher_username,
        published_at,
        is_published,
        updated_at,
        (
          select count(*)
          from user_extensions ue
          where ue.extension_id = extensions.extension_id
            and ue.enabled = 1
        ) as install_count
      from extensions
      where extension_id in (${PUBLIC_STORE_EXTENSIONS.map(() => "?").join(", ")})`
    )
      .bind(...PUBLIC_STORE_EXTENSIONS.map((item) => item.extension_id))
      .all();
    const publicRowMap = new Map(
      (publicExtensionRows.results ?? []).map((row) => [row.extension_id, row])
    );
    const publicItems = PUBLIC_STORE_EXTENSIONS
      .map((definition) => serializeExtensionListItem(url, publicRowMap.get(definition.extension_id) || null, definition))
      .filter((item) => matchesStoreSearch(item, search));
    const publicCount = publicItems.length;
    const start = (page - 1) * pageSize;

    const dynamicWhere = search
      ? `where is_published = 1
           and extension_id not in (${PUBLIC_STORE_EXTENSION_IDS_SQL})
           and extension_id not in ('yanzi-webdav-settings', 'yanzi-quickpanel-settings')
           and lower(ifnull(manifest_json, '')) not like '%"category":"系统配置"%'
           and (
             lower(extension_id) like ?
             or lower(display_name) like ?
             or lower(ifnull(manifest_json, '')) like ?
           )`
      : `where is_published = 1
           and extension_id not in (${PUBLIC_STORE_EXTENSION_IDS_SQL})
           and extension_id not in ('yanzi-webdav-settings', 'yanzi-quickpanel-settings')
           and lower(ifnull(manifest_json, '')) not like '%"category":"系统配置"%'`;
    const dynamicBindings = search
      ? [`%${search}%`, `%${search}%`, `%${search}%`]
      : [];
    const dynamicCountQuery = await env.DB.prepare(
      `select count(*) as total
       from extensions
       ${dynamicWhere}`
    )
      .bind(...dynamicBindings)
      .first();
    const dynamicTotal = Number(dynamicCountQuery?.total || 0);
    const total = publicCount + dynamicTotal;
    const totalPages = Math.max(1, Math.ceil(Math.max(total, 1) / pageSize));
    const safePage = Math.min(page, totalPages);
    const safeStart = (safePage - 1) * pageSize;

    const publicSlice = publicItems.slice(safeStart, safeStart + pageSize);
    const remainingSlots = pageSize - publicSlice.length;
    const dynamicOffset = Math.max(0, safeStart - publicCount);
    let dynamicItems = [];

    if (remainingSlots > 0)
    {
      const rows = await env.DB.prepare(
        `select
          extension_id,
          display_name,
          latest_version,
          manifest_json,
          icon_key,
          archive_key,
          archive_sha256,
          publisher_user_id,
          publisher_username,
          published_at,
          is_published,
          updated_at,
          (
            select count(*)
            from user_extensions ue
            where ue.extension_id = extensions.extension_id
              and ue.enabled = 1
          ) as install_count
        from extensions
        ${dynamicWhere}
        order by updated_at desc
        limit ?
        offset ?`
      )
        .bind(...dynamicBindings, remainingSlots, dynamicOffset)
        .all();

      dynamicItems = (rows.results ?? [])
        .filter((row) => !PUBLIC_STORE_EXTENSION_MAP.has(row.extension_id))
        .map((row) => serializeExtensionListItem(url, row));
    }

    const pagedItems = [...publicSlice, ...dynamicItems];

    return json({
      items: pagedItems,
      page: safePage,
      page_size: pageSize,
      total,
      total_pages: totalPages,
      has_more: safePage < totalPages
    });
  }

  const extensionMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)$/);
  if (extensionMatch) {
    const extensionId = decodeURIComponent(extensionMatch[1]);

    if (request.method === "GET") {
      const publicDefinition = PUBLIC_STORE_EXTENSION_MAP.get(extensionId);
      if (publicDefinition) {
        const row = await env.DB.prepare(
          `select
            extension_id,
            display_name,
            latest_version,
            manifest_json,
            icon_key,
            archive_key,
            archive_sha256,
            publisher_user_id,
            publisher_username,
            published_at,
            is_published,
            updated_at,
            (
              select count(*)
              from user_extensions ue
              where ue.extension_id = extensions.extension_id
                and ue.enabled = 1
            ) as install_count
          from extensions
          where extension_id = ?`
        )
          .bind(extensionId)
          .first();

        return json(serializeStoreExtensionRecord(url, row, publicDefinition));
      }

      const row = await env.DB.prepare(
        `select
          extension_id,
          display_name,
          latest_version,
          manifest_json,
          icon_key,
          archive_key,
          archive_sha256,
          publisher_user_id,
          publisher_username,
          published_at,
          is_published,
          updated_at,
          (
            select count(*)
            from user_extensions ue
            where ue.extension_id = extensions.extension_id
              and ue.enabled = 1
          ) as install_count
        from extensions
        where extension_id = ?`
      )
        .bind(extensionId)
        .first();

      if (!row) {
        return json({ error: "not_found", message: "Extension not found" }, 404);
      }

      if (!isStoreVisibleExtension(row)) {
        return json({ error: "not_found", message: "Extension not found" }, 404);
      }

      return json(serializeExtensionRecord(url, row));
    }

    if (request.method !== "PUT" && request.method !== "DELETE") {
      return json({ error: "method_not_allowed", message: "Method not allowed" }, 405);
    }

    if (request.method === "DELETE") {
      const auth = await requireAuth(request, env);
      const existing = await env.DB.prepare(
        `select publisher_user_id
         from extensions
         where extension_id = ?`
      )
        .bind(extensionId)
        .first();

      if (!existing) {
        return json({ error: "not_found", message: "Extension not found" }, 404);
      }

      if (!existing.publisher_user_id ||
          String(existing.publisher_user_id) !== auth.userId) {
        throw new HttpError(403, "forbidden", "Only the original publisher can unpublish this extension");
      }

      await env.DB.prepare(
        `update extensions
         set is_published = 0,
             updated_at = ?
         where extension_id = ?`
      )
        .bind(isoNow(), extensionId)
        .run();

      return json({ ok: true, extensionId });
    }

    const auth = await requireAuth(request, env);
    const existing = await env.DB.prepare(
      `select publisher_user_id
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    if (existing?.publisher_user_id &&
        String(existing.publisher_user_id) !== auth.userId) {
      throw new HttpError(403, "forbidden", "Only the original publisher can update this extension");
    }

    const payload = await readJson(request);
    const now = isoNow();
    const manifest = payload.manifest ?? payload;
    const displayName = String(
      manifest.displayName ?? manifest.name ?? extensionId
    ).slice(0, 200);
    const latestVersion = String(manifest.version ?? "0.0.0").slice(0, 50);

    await env.DB.prepare(
      `insert into extensions (
        extension_id,
        display_name,
        latest_version,
        manifest_json,
        publisher_user_id,
        publisher_username,
        published_at,
        is_published,
        updated_at
      ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
      on conflict(extension_id) do update set
        display_name = excluded.display_name,
        latest_version = excluded.latest_version,
        manifest_json = excluded.manifest_json,
        publisher_user_id = coalesce(extensions.publisher_user_id, excluded.publisher_user_id),
        publisher_username = excluded.publisher_username,
        published_at = coalesce(extensions.published_at, excluded.published_at),
        is_published = coalesce(extensions.is_published, excluded.is_published),
        updated_at = excluded.updated_at`
    )
      .bind(
        extensionId,
        displayName,
        latestVersion,
        JSON.stringify(manifest),
        auth.userId,
        auth.username,
        now,
        0,
        now
      )
      .run();

    return json({
      ok: true,
      extensionId,
      latestVersion
    });
  }

  const iconUploadMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)\/icon$/);
  if (iconUploadMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(iconUploadMatch[1]);
    const existing = await env.DB.prepare(
      `select publisher_user_id
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    if (existing?.publisher_user_id &&
        String(existing.publisher_user_id) !== auth.userId) {
      throw new HttpError(403, "forbidden", "Only the original publisher can upload this extension icon");
    }

    const version = (url.searchParams.get("version") || "0.0.0").slice(0, 50);
    const filename = String(url.searchParams.get("filename") || "icon.png").slice(0, 200);
    const bytes = await request.arrayBuffer();
    if (!bytes || bytes.byteLength === 0) {
      throw new HttpError(400, "invalid_icon", "Icon payload is empty");
    }

    const contentType = request.headers.get("content-type") || "application/octet-stream";
    const extension = resolveIconExtension(filename, contentType);
    const iconKey = `extension-icons/${extensionId}/${version}${extension}`;

    await env.PACKAGES.put(iconKey, bytes, {
      httpMetadata: {
        contentType
      },
      customMetadata: {
        extensionId,
        version
      }
    });

    await env.DB.prepare(
      `insert into extensions (
        extension_id,
        display_name,
        latest_version,
        icon_key,
        publisher_user_id,
        publisher_username,
        published_at,
        is_published,
        updated_at
      ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
      on conflict(extension_id) do update set
        latest_version = excluded.latest_version,
        icon_key = excluded.icon_key,
        publisher_user_id = coalesce(extensions.publisher_user_id, excluded.publisher_user_id),
        publisher_username = excluded.publisher_username,
        published_at = coalesce(extensions.published_at, excluded.published_at),
        is_published = 1,
        updated_at = excluded.updated_at`
    )
      .bind(extensionId, extensionId, version, iconKey, auth.userId, auth.username, isoNow(), 1, isoNow())
      .run();

    return json({
      ok: true,
      extensionId,
      icon_url: buildExtensionIconUrl(url, extensionId, Date.now())
    });
  }

  const iconDownloadMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)\/icon$/);
  if (iconDownloadMatch && request.method === "GET") {
    const extensionId = decodeURIComponent(iconDownloadMatch[1]);
    const row = await env.DB.prepare(
      `select extension_id, manifest_json, icon_key, is_published
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    if (row && !isStoreVisibleExtension(row)) {
      return json({ error: "not_found", message: "Icon not found" }, 404);
    }

    if (!row?.icon_key) {
      return json({ error: "not_found", message: "Icon not found" }, 404);
    }

    const object = await env.PACKAGES.get(row.icon_key);
    if (!object) {
      return json({ error: "not_found", message: "Stored icon is missing" }, 404);
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("cache-control", "public, max-age=3600");
    return withCors(new Response(object.body, { headers }));
  }

  const archiveUploadMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)\/archive$/);
  if (archiveUploadMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(archiveUploadMatch[1]);
    const existing = await env.DB.prepare(
      `select publisher_user_id
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    if (existing?.publisher_user_id &&
        String(existing.publisher_user_id) !== auth.userId) {
      throw new HttpError(403, "forbidden", "Only the original publisher can upload this extension archive");
    }

    const version = (url.searchParams.get("version") || "0.0.0").slice(0, 50);
    const bytes = await request.arrayBuffer();
    const sha256 = await digestHex(bytes);
    const archiveKey = `extensions/${extensionId}/${version}.zip`;

    await env.PACKAGES.put(archiveKey, bytes, {
      httpMetadata: {
        contentType: request.headers.get("content-type") || "application/zip"
      },
      customMetadata: {
        extensionId,
        version,
        sha256
      }
    });

    await env.DB.prepare(
      `insert into extensions (
        extension_id,
        display_name,
        latest_version,
        archive_key,
        archive_sha256,
        publisher_user_id,
        publisher_username,
        published_at,
        is_published,
        updated_at
      ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      on conflict(extension_id) do update set
        latest_version = excluded.latest_version,
        archive_key = excluded.archive_key,
        archive_sha256 = excluded.archive_sha256,
        publisher_user_id = coalesce(extensions.publisher_user_id, excluded.publisher_user_id),
        publisher_username = excluded.publisher_username,
        published_at = coalesce(extensions.published_at, excluded.published_at),
        is_published = 1,
        updated_at = excluded.updated_at`
    )
      .bind(extensionId, extensionId, version, archiveKey, sha256, auth.userId, auth.username, isoNow(), 1, isoNow())
      .run();

    return json({
      ok: true,
      extensionId,
      version,
      archiveKey,
      sha256
    });
  }

  const archiveDownloadMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)\/archive$/);
  if (archiveDownloadMatch && request.method === "GET") {
    const extensionId = decodeURIComponent(archiveDownloadMatch[1]);
    const row = await env.DB.prepare(
      `select extension_id, manifest_json, archive_key, latest_version, archive_sha256, is_published
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    let archiveKey = null;
    let latestVersion = "latest";
    let sha256 = "";

    if (row?.archive_key && isStoreVisibleExtension(row)) {
      archiveKey = row.archive_key;
      latestVersion = row.latest_version || "latest";
      sha256 = row.archive_sha256 || "";
    } else {
      const preset = PUBLIC_STORE_EXTENSION_MAP.get(extensionId);
      if (preset && preset.package_path) {
        archiveKey = preset.package_path.startsWith("/") 
          ? preset.package_path.substring(1) 
          : preset.package_path;
        latestVersion = preset.latest_version || "latest";
      }
    }

    if (!archiveKey) {
      return json({ error: "not_found", message: "Archive not found" }, 404);
    }

    const object = await env.PACKAGES.get(archiveKey);
    if (!object) {
      return json({ error: "not_found", message: `Stored package is missing: ${archiveKey}` }, 404);
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("etag", sha256 || object.httpEtag || "");
    headers.set(
      "content-disposition",
      `attachment; filename="${extensionId}-${latestVersion}.zip"`
    );
    return withCors(new Response(object.body, { headers }));
  }

  if (url.pathname === "/v1/me/extensions" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    await ensureUser(env, auth.userId);

    const rows = await env.DB.prepare(
      `select
        ue.user_id,
        ue.extension_id,
        ue.installed_version,
        ue.enabled,
        ue.settings_json,
        ue.updated_at,
        e.display_name,
        coalesce(archive_head.version, e.latest_version) as latest_version,
        e.manifest_json,
        e.icon_key,
        coalesce(archive_head.archive_key, e.archive_key) as archive_key,
        coalesce(archive_head.archive_sha256, e.archive_sha256) as archive_sha256,
        coalesce(archive_head.revision, 0) as archive_revision,
        coalesce(archive_head.updated_at, '') as archive_updated_at,
        coalesce(archive_head.updated_by_device_id, '') as archive_updated_by_device_id,
        coalesce(archive_head.updated_by_device_name, '') as archive_updated_by_device_name,
        e.publisher_user_id,
        e.publisher_username,
        e.published_at,
        e.is_published,
        e.updated_at as extension_updated_at
      from user_extensions ue
      left join extensions e on e.extension_id = ue.extension_id
      left join user_extension_archive_heads archive_head
        on archive_head.user_id = ue.user_id and archive_head.extension_id = ue.extension_id
      where ue.user_id = ?
      order by ue.updated_at desc`
    )
      .bind(auth.userId)
      .all();

    return json({
      userId: auth.userId,
      items: (rows.results ?? []).map((row) => serializeUserExtensionRecord(url, row, auth.userId))
    });
  }

  const myPrivateExtensionMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)\/private$/);
  if (myPrivateExtensionMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myPrivateExtensionMatch[1]);
    const payload = await readJson(request);
    const manifest = payload.manifest ?? payload;
    await ensureUser(env, auth.userId);
    await upsertPrivateExtensionMetadata(env, auth, extensionId, manifest);

    return json({
      ok: true,
      userId: auth.userId,
      extensionId,
      latestVersion: String(manifest.version ?? "0.0.0").slice(0, 50)
    });
  }

  const myIconMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)\/icon$/);
  if (myIconMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myIconMatch[1]);
    await ensureUser(env, auth.userId);
    await ensurePrivateExtensionWritable(env, auth, extensionId);

    const version = (url.searchParams.get("version") || "0.0.0").slice(0, 50);
    const filename = String(url.searchParams.get("filename") || "icon.png").slice(0, 200);
    const bytes = await request.arrayBuffer();
    if (!bytes || bytes.byteLength === 0) {
      throw new HttpError(400, "invalid_icon", "Icon payload is empty");
    }

    const contentType = request.headers.get("content-type") || "application/octet-stream";
    const extension = resolveIconExtension(filename, contentType);
    const iconKey = `users/${auth.userId}/extensions/${extensionId}/${version}/icon${extension}`;
    const now = isoNow();

    await env.PACKAGES.put(iconKey, bytes, {
      httpMetadata: { contentType },
      customMetadata: {
        userId: auth.userId,
        extensionId,
        version,
        private: "true"
      }
    });

    await env.DB.prepare(
      `update extensions
       set icon_key = ?,
           latest_version = coalesce(nullif(?, ''), latest_version),
           updated_at = ?
       where extension_id = ?`
    )
      .bind(iconKey, version, now, extensionId)
      .run();

    return json({
      ok: true,
      userId: auth.userId,
      extensionId,
      icon_url: buildMyExtensionIconUrl(url, extensionId, Date.now())
    });
  }

  if (myIconMatch && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myIconMatch[1]);
    await ensureUserCanReadExtension(env, auth.userId, extensionId);

    const row = await env.DB.prepare(
      `select icon_key
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    if (!row?.icon_key) {
      return json({ error: "not_found", message: "Icon not found" }, 404);
    }

    const object = await env.PACKAGES.get(row.icon_key);
    if (!object) {
      return json({ error: "not_found", message: "Stored icon is missing" }, 404);
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("cache-control", "private, max-age=300");
    return withCors(new Response(object.body, { headers }));
  }

  const myArchiveHistoryMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)\/archive\/history$/);
  if (myArchiveHistoryMatch && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myArchiveHistoryMatch[1]);
    await ensureUser(env, auth.userId);
    await ensureUserCanReadExtension(env, auth.userId, extensionId);
    const rows = await env.DB.prepare(
      `select revision, version, archive_sha256, updated_at,
              updated_by_device_id, updated_by_device_name,
              operation, restored_from_revision
       from user_extension_archive_history
       where user_id = ? and extension_id = ?
       order by revision desc limit 100`
    ).bind(auth.userId, extensionId).all();
    return json({
      extensionId,
      items: rows.results ?? []
    });
  }

  const myArchiveRestoreMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)\/archive\/restore$/);
  if (myArchiveRestoreMatch && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myArchiveRestoreMatch[1]);
    await ensureUser(env, auth.userId);
    await ensurePrivateExtensionWritable(env, auth, extensionId);
    const payload = await readJson(request);
    const sourceRevision = Number(payload.revision);
    const expectedRevision = Number(payload.expectedRevision);
    if (!Number.isInteger(sourceRevision) || sourceRevision <= 0 ||
        !Number.isInteger(expectedRevision) || expectedRevision < 0) {
      throw new HttpError(400, "invalid_revision", "revision must be positive and expectedRevision must be non-negative");
    }

    const current = await env.DB.prepare(
      `select revision from user_extension_archive_heads where user_id = ? and extension_id = ?`
    ).bind(auth.userId, extensionId).first();
    const currentRevision = Number(current?.revision || 0);
    if (currentRevision !== expectedRevision) {
      const error = new HttpError(409, "archive_revision_conflict", "private archive revision does not match expectedRevision");
      error.details = { extensionId, expectedRevision, currentRevision };
      throw error;
    }
    const source = await env.DB.prepare(
      `select version, archive_key, archive_sha256
       from user_extension_archive_history
       where user_id = ? and extension_id = ? and revision = ?`
    ).bind(auth.userId, extensionId, sourceRevision).first();
    if (!source?.archive_key) {
      throw new HttpError(404, "not_found", "archive history revision not found");
    }

    const revision = currentRevision + 1;
    const now = isoNow();
    const deviceId = String(payload.updatedByDeviceId || request.headers.get("x-yanzi-device-id") || "").slice(0, 200);
    const deviceName = String(payload.updatedByDeviceName || request.headers.get("x-yanzi-device-name") || "").slice(0, 200);
    const results = await env.DB.batch([
      env.DB.prepare(
        `update user_extension_archive_heads
         set revision = ?, version = ?, archive_key = ?, archive_sha256 = ?,
             updated_at = ?, updated_by_device_id = ?, updated_by_device_name = ?
         where user_id = ? and extension_id = ? and revision = ?`
      ).bind(
        revision, source.version, source.archive_key, source.archive_sha256,
        now, deviceId, deviceName, auth.userId, extensionId, expectedRevision
      ),
      env.DB.prepare(
        `insert into user_extension_archive_history (
           user_id, extension_id, revision, version, archive_key, archive_sha256,
           updated_at, updated_by_device_id, updated_by_device_name,
           operation, restored_from_revision
         )
         select user_id, extension_id, revision, version, archive_key, archive_sha256,
                updated_at, updated_by_device_id, updated_by_device_name, 'restore', ?
         from user_extension_archive_heads
         where user_id = ? and extension_id = ? and revision = ?`
      ).bind(sourceRevision, auth.userId, extensionId, revision),
      env.DB.prepare(
        `update extensions
         set latest_version = ?, archive_key = ?, archive_sha256 = ?, updated_at = ?
         where extension_id = ? and exists (
           select 1 from user_extension_archive_heads head
           where head.user_id = ? and head.extension_id = ? and head.revision = ?
         )`
      ).bind(source.version, source.archive_key, source.archive_sha256, now,
             extensionId, auth.userId, extensionId, revision)
    ]);
    if (Number(results?.[0]?.meta?.changes || 0) === 0) {
      const latest = await env.DB.prepare(
        `select revision from user_extension_archive_heads where user_id = ? and extension_id = ?`
      ).bind(auth.userId, extensionId).first();
      const error = new HttpError(409, "archive_revision_conflict", "private archive changed during restore");
      error.details = { extensionId, expectedRevision, currentRevision: Number(latest?.revision || 0) };
      throw error;
    }
    return json({
      ok: true,
      extensionId,
      revision,
      restoredFromRevision: sourceRevision,
      version: source.version,
      sha256: source.archive_sha256,
      updatedAtUtc: now,
      updatedByDeviceId: deviceId,
      updatedByDeviceName: deviceName,
      archive_download_url: buildMyExtensionArchiveUrl(url, extensionId)
    });
  }

  const myArchiveMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)\/archive$/);
  if (myArchiveMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myArchiveMatch[1]);
    await ensureUser(env, auth.userId);
    await ensurePrivateExtensionWritable(env, auth, extensionId);

    const version = (url.searchParams.get("version") || "0.0.0").slice(0, 50);
    const bytes = await request.arrayBuffer();
    if (!bytes || bytes.byteLength === 0) {
      throw new HttpError(400, "invalid_archive", "Archive payload is empty");
    }

    const sha256 = await digestHex(bytes);
    const current = await env.DB.prepare(
      `select revision, version, archive_key, archive_sha256, updated_at,
              updated_by_device_id, updated_by_device_name
       from user_extension_archive_heads
       where user_id = ? and extension_id = ?`
    ).bind(auth.userId, extensionId).first();
    const currentRevision = Number(current?.revision || 0);
    if (current?.archive_sha256 && String(current.archive_sha256) === sha256) {
      return json({
        ok: true,
        unchanged: true,
        userId: auth.userId,
        extensionId,
        version: current.version || version,
        archiveKey: current.archive_key,
        sha256,
        revision: currentRevision,
        updatedAtUtc: current.updated_at || "",
        archive_download_url: buildMyExtensionArchiveUrl(url, extensionId)
      });
    }

    const expectedRaw = url.searchParams.get("expectedRevision");
    if (expectedRaw == null && currentRevision > 0) {
      const error = new HttpError(428, "archive_revision_required", "expectedRevision is required for an existing private archive");
      error.details = { extensionId, currentRevision };
      throw error;
    }
    const expectedRevision = expectedRaw == null ? 0 : Number(expectedRaw);
    if (!Number.isInteger(expectedRevision) || expectedRevision < 0) {
      throw new HttpError(400, "invalid_expected_revision", "expectedRevision must be a non-negative integer");
    }
    if (expectedRevision !== currentRevision) {
      const error = new HttpError(409, "archive_revision_conflict", "private archive revision does not match expectedRevision");
      error.details = { extensionId, expectedRevision, currentRevision };
      throw error;
    }

    const revision = currentRevision + 1;
    const archiveKey = `users/${auth.userId}/extensions/${extensionId}/archive-history/${revision}-${sha256}.zip`;
    const now = isoNow();
    const deviceId = String(request.headers.get("x-yanzi-device-id") || "").slice(0, 200);
    const deviceName = String(request.headers.get("x-yanzi-device-name") || "").slice(0, 200);

    await env.PACKAGES.put(archiveKey, bytes, {
      httpMetadata: {
        contentType: request.headers.get("content-type") || "application/zip"
      },
      customMetadata: {
        userId: auth.userId,
        extensionId,
        version,
        sha256,
        private: "true"
      }
    });

    const results = await env.DB.batch([
      env.DB.prepare(
        `insert into user_extension_archive_heads (
           user_id, extension_id, revision, version, archive_key, archive_sha256,
           updated_at, updated_by_device_id, updated_by_device_name
         ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
         on conflict(user_id, extension_id) do update set
           revision = excluded.revision,
           version = excluded.version,
           archive_key = excluded.archive_key,
           archive_sha256 = excluded.archive_sha256,
           updated_at = excluded.updated_at,
           updated_by_device_id = excluded.updated_by_device_id,
           updated_by_device_name = excluded.updated_by_device_name
         where user_extension_archive_heads.revision = ?`
      ).bind(
        auth.userId, extensionId, revision, version, archiveKey, sha256,
        now, deviceId, deviceName, expectedRevision
      ),
      env.DB.prepare(
        `insert into user_extension_archive_history (
           user_id, extension_id, revision, version, archive_key, archive_sha256,
           updated_at, updated_by_device_id, updated_by_device_name, operation
         )
         select user_id, extension_id, revision, version, archive_key, archive_sha256,
                updated_at, updated_by_device_id, updated_by_device_name, 'put'
         from user_extension_archive_heads
         where user_id = ? and extension_id = ? and revision = ? and archive_key = ?
         on conflict(user_id, extension_id, revision) do nothing`
      ).bind(auth.userId, extensionId, revision, archiveKey),
      env.DB.prepare(
        `update extensions
         set latest_version = ?, archive_key = ?, archive_sha256 = ?, updated_at = ?
         where extension_id = ?
           and exists (
             select 1 from user_extension_archive_heads head
             where head.user_id = ? and head.extension_id = ?
               and head.revision = ? and head.archive_key = ?
           )`
      ).bind(version, archiveKey, sha256, now, extensionId, auth.userId, extensionId, revision, archiveKey)
    ]);

    if (Number(results?.[0]?.meta?.changes || 0) === 0) {
      await env.PACKAGES.delete(archiveKey);
      const latest = await env.DB.prepare(
        `select revision from user_extension_archive_heads where user_id = ? and extension_id = ?`
      ).bind(auth.userId, extensionId).first();
      const error = new HttpError(409, "archive_revision_conflict", "private archive changed during upload");
      error.details = { extensionId, expectedRevision, currentRevision: Number(latest?.revision || 0) };
      throw error;
    }

    return json({
      ok: true,
      userId: auth.userId,
      extensionId,
      version,
      archiveKey,
      sha256,
      revision,
      updatedAtUtc: now,
      updatedByDeviceId: deviceId,
      updatedByDeviceName: deviceName,
      archive_download_url: buildMyExtensionArchiveUrl(url, extensionId)
    });
  }

  if (myArchiveMatch && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myArchiveMatch[1]);
    await ensureUser(env, auth.userId);
    await ensureUserCanReadExtension(env, auth.userId, extensionId);

    const requestedRevision = Number(url.searchParams.get("revision") || 0);
    if (!Number.isInteger(requestedRevision) || requestedRevision < 0) {
      throw new HttpError(400, "invalid_revision", "revision must be a non-negative integer");
    }
    let row = requestedRevision > 0
      ? await env.DB.prepare(
          `select archive_key, version as latest_version, archive_sha256, revision
           from user_extension_archive_history
           where user_id = ? and extension_id = ? and revision = ?`
        ).bind(auth.userId, extensionId, requestedRevision).first()
      : await env.DB.prepare(
          `select archive_key, version as latest_version, archive_sha256, revision
           from user_extension_archive_heads
           where user_id = ? and extension_id = ?`
        ).bind(auth.userId, extensionId).first();
    if (!row && requestedRevision === 0) {
      row = await env.DB.prepare(
        `select archive_key, latest_version, archive_sha256, 0 as revision
         from extensions where extension_id = ?`
      ).bind(extensionId).first();
    }

    if (!row?.archive_key) {
      return json({ error: "not_found", message: "Archive not found" }, 404);
    }

    const object = await env.PACKAGES.get(row.archive_key);
    if (!object) {
      return json({ error: "not_found", message: `Stored package is missing: ${row.archive_key}` }, 404);
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("etag", row.archive_sha256 || object.httpEtag || "");
    headers.set("x-yanzi-archive-revision", String(row.revision || 0));
    headers.set("cache-control", "private, max-age=60");
    headers.set(
      "content-disposition",
      `attachment; filename="${extensionId}-${row.latest_version || "latest"}.zip"`
    );
    return withCors(new Response(object.body, { headers }));
  }

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
      where user_id = ?
      order by updated_at desc`
    )
      .bind(auth.userId)
      .all();

    return json({
      ok: true,
      userId: auth.userId,
      items: (rows.results ?? []).map(serializeDeviceRecord)
    });
  }

  if (url.pathname === "/v1/me/devices" && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const payload = await readJson(request);
    await ensureUser(env, auth.userId);

    const device = normalizeDevicePayload(payload);
    const existingDeviceOwner = await env.DB.prepare(
      `select user_id
       from user_devices
       where device_id = ?`
    )
      .bind(device.deviceId)
      .first();
    if (existingDeviceOwner && String(existingDeviceOwner.user_id) !== auth.userId) {
      throw new HttpError(409, "device_id_taken", "Device ID is already bound to another account");
    }

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
        push_token = excluded.push_token,
        capabilities_json = excluded.capabilities_json,
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

  if (url.pathname === "/v1/me/mobile/messages" && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const payload = await readJson(request);
    await ensureUser(env, auth.userId);

    const message = normalizeDeviceMessagePayload(payload);
    if (message.sourceDeviceId) {
      await ensureOwnedDevice(env, auth.userId, message.sourceDeviceId);
      await touchDevice(env, auth.userId, message.sourceDeviceId);
    }

    if (message.targetDeviceId) {
      await ensureOwnedDevice(env, auth.userId, message.targetDeviceId);
    }

    const now = isoNow();
    const messageId = `msg_${randomHex(12)}`;
    await env.DB.prepare(
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
      .run();

    await notifyDeviceRelay(env, auth.userId);

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
    await ensureOwnedDevice(env, auth.userId, deviceId);

    if (!env.DEVICE_RELAY) {
      throw new HttpError(503, "relay_unavailable", "Device relay is not configured");
    }

    return env.DEVICE_RELAY.getByName(auth.userId).fetch(request);
  }

  if (url.pathname === "/v1/me/mobile/messages/events" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const deviceId = normalizeDeviceId(url.searchParams.get("deviceId"));
    await ensureUser(env, auth.userId);
    const device = await ensureOwnedDevice(env, auth.userId, deviceId);
    await touchDevice(env, auth.userId, deviceId);

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
          const rows = await env.DB.prepare(
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
              and status = 'pending'
              and (expires_at is null or expires_at > ?)
              and (
                target_device_id = ?
                or (
                  target_device_id is null
                  and target_platform = ?
                )
              )
            order by created_at asc`
          )
            .bind(auth.userId, isoNow(), deviceId, device.platform)
            .all();

          const items = (rows.results ?? []).map(serializeDeviceMessageRecord);
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

    const now = Date.now();
    const rateLimitKey = `${auth.userId}:${deviceId}`;
    const lastRequestTime = mobilePollRateLimitMap.get(rateLimitKey) || 0;
    mobilePollRateLimitMap.set(rateLimitKey, now);
    if (mobilePollRateLimitMap.size > mobilePollRateLimitMaxEntries) {
      const oldestKey = mobilePollRateLimitMap.keys().next().value;
      if (oldestKey !== undefined) {
        mobilePollRateLimitMap.delete(oldestKey);
      }
    }
    if (now - lastRequestTime < 3000) {
      return json({
        ok: true,
        userId: auth.userId,
        deviceId,
        items: []
      });
    }

    const limit = normalizeMessageLimit(url.searchParams.get("limit"));
    await ensureUser(env, auth.userId);
    const device = await ensureOwnedDevice(env, auth.userId, deviceId);
    await touchDevice(env, auth.userId, deviceId);

    const rows = await env.DB.prepare(
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
        and status = 'pending'
        and (expires_at is null or expires_at > ?)
        and (
          target_device_id = ?
          or (
            target_device_id is null
            and target_platform = ?
          )
        )
      order by created_at asc
      limit ?`
    )
      .bind(auth.userId, isoNow(), deviceId, device.platform, limit)
      .all();

    const items = (rows.results ?? []).map(serializeDeviceMessageRecord);
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
      items
    });
  }

  const deviceMessageAckMatch = url.pathname.match(/^\/v1\/me\/mobile\/messages\/([^/]+)\/ack$/);
  if (deviceMessageAckMatch && request.method === "POST") {
    const auth = await requireAuth(request, env);
    const messageId = normalizeMessageId(decodeURIComponent(deviceMessageAckMatch[1]));
    const payload = await readJson(request);
    const deviceId = normalizeDeviceId(payload.deviceId);
    await ensureUser(env, auth.userId);
    await ensureOwnedDevice(env, auth.userId, deviceId);
    await touchDevice(env, auth.userId, deviceId);

    const success = payload.success;
    const resultText = payload.result || "";

    let newStatus = "acked";
    let updatedPayloadJson = null;

    if (success !== undefined) {
      newStatus = success ? "completed" : "failed";
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
        success: !!success,
        output: resultText,
        time: isoNow()
      };
      updatedPayloadJson = JSON.stringify(originPayload);
    }

    let result;
    if (updatedPayloadJson) {
      result = await env.DB.prepare(
        `update device_messages
         set status = ?,
             acked_at = ?,
             payload_json = ?
         where user_id = ?
           and message_id = ?
           and status = 'pending'`
      )
        .bind(newStatus, isoNow(), updatedPayloadJson, auth.userId, messageId)
        .run();
    } else {
      result = await env.DB.prepare(
        `update device_messages
         set status = 'acked',
             acked_at = ?
         where user_id = ?
           and message_id = ?
           and status = 'pending'`
      )
        .bind(isoNow(), auth.userId, messageId)
        .run();
    }

    return json({
      ok: true,
      userId: auth.userId,
      messageId,
      acked: Number(result.meta?.changes ?? 0) > 0
    });
  }

  const singleMessageMatch = url.pathname.match(/^\/v1\/me\/mobile\/messages\/([^/]+)$/);
  if (singleMessageMatch && request.method === "GET") {
    const auth = await requireAuth(request, env);
    const messageId = normalizeMessageId(decodeURIComponent(singleMessageMatch[1]));
    await ensureUser(env, auth.userId);

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

    return json(serializeDeviceMessageRecord(row));
  }

  const myExtensionMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)$/);
  if (myExtensionMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myExtensionMatch[1]);
    const payload = await readJson(request);
    await ensureUser(env, auth.userId);

    let settings = payload.settings ?? {};
    if (extensionId === "yanzi-quickpanel-settings") {
      const existing = await env.DB.prepare(
        `select settings_json from user_extensions where user_id = ? and extension_id = ?`
      )
        .bind(auth.userId, extensionId)
        .first();
      let existingSettings = {};
      if (existing?.settings_json) {
        try {
          existingSettings = JSON.parse(String(existing.settings_json));
        } catch {
          existingSettings = {};
        }
      }

      const incomingUpdatedAt = Date.parse(String(settings.updatedAtUtc || ""));
      const existingUpdatedAt = Date.parse(String(existingSettings.updatedAtUtc || ""));
      const incomingIsStale = Number.isFinite(existingUpdatedAt) &&
        (!Number.isFinite(incomingUpdatedAt) || incomingUpdatedAt + 1000 < existingUpdatedAt);

      // 主配置和燕幕状态共用兼容存储行。按字段合并可避免任一写入
      // 把另一条同步链维护的字段整包擦除；过期客户端提交则不覆盖新快照。
      settings = incomingIsStale ? existingSettings : { ...existingSettings, ...settings };
    }
    if (extensionId === "yanzi-quickpanel-settings" || extensionId === "yanzi-ai-settings") {
      settings = scrubAiSecretsFromValue(settings);
    }
    if (extensionId === "yanzi-personal-sync-settings" || extensionId === "yanzi-webdav-settings") {
      settings = scrubPersonalSyncSecretsFromValue(settings);
    }

    await env.DB.prepare(
      `insert into user_extensions (
        user_id,
        extension_id,
        installed_version,
        enabled,
        settings_json,
        updated_at
      ) values (?, ?, ?, ?, ?, ?)
      on conflict(user_id, extension_id) do update set
        installed_version = excluded.installed_version,
        enabled = excluded.enabled,
        settings_json = excluded.settings_json,
        updated_at = excluded.updated_at`
    )
      .bind(
        auth.userId,
        extensionId,
        String(payload.installedVersion ?? payload.version ?? "0.0.0").slice(0, 50),
        payload.enabled === false ? 0 : 1,
        JSON.stringify(settings),
        isoNow()
      )
      .run();

    return json({ ok: true, userId: auth.userId, extensionId });
  }

  if (myExtensionMatch && request.method === "DELETE") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myExtensionMatch[1]);
    await ensureUser(env, auth.userId);

    const result = await env.DB.prepare(
      `delete from user_extensions
       where user_id = ? and extension_id = ?`
    )
      .bind(auth.userId, extensionId)
      .run();

    return json({
      ok: true,
      userId: auth.userId,
      extensionId,
      deleted: Number(result.meta?.changes ?? 0) > 0
    });
  }

  return json({ error: "not_found", message: "Route not found" }, 404);
}

async function buildAuthResponse(env, user) {
  const now = Math.floor(Date.now() / 1000);
  const expiresAt = now + TOKEN_TTL_SECONDS;
  const accessToken = await signToken(env, {
    sub: user.userId,
    username: user.username,
    email: user.email ?? null,
    iat: now,
    exp: expiresAt
  });

  return {
    accessToken,
    expiresAt,
    userId: user.userId,
    username: user.username,
    email: user.email ?? null
  };
}

async function requireAuth(request, env) {
  const header = request.headers.get("authorization") || "";

  // 1. 优先尝试以 Bearer Token 进行验证
  if (header.startsWith("Bearer ")) {
    const token = header.slice(7).trim();
    try {
      const payload = await verifyToken(env, token);
      if (payload?.sub && payload?.username) {
        return {
          userId: String(payload.sub),
          username: String(payload.username),
          email: payload.email ? String(payload.email) : null
        };
      }
    } catch (tokenErr) {
      if (tokenErr.status === 401 && tokenErr.code === "token_expired") {
        throw tokenErr;
      }
    }
  }

  // 2. 尝试以 Basic Auth 账户密码直接鉴权作为备用
  if (header.startsWith("Basic ")) {
    const credentials = header.slice(6).trim();
    try {
      const decoded = atob(credentials);
      const colonIndex = decoded.indexOf(":");
      if (colonIndex !== -1) {
        const usernameOrEmail = decoded.substring(0, colonIndex);
        const password = decoded.substring(colonIndex + 1);

        if (usernameOrEmail && password) {
          const email = normalizeEmail(usernameOrEmail);
          const user = await env.DB.prepare(
            `select
              user_id,
              username,
              email,
              password_hash,
              password_salt,
              password_iterations
            from auth_users
            where email = ?`
          )
            .bind(email)
            .first();

          if (user) {
            const passwordHash = await hashPassword(
              password,
              user.password_salt,
              Number(user.password_iterations || PASSWORD_ITERATIONS)
          );
            if (passwordHash === user.password_hash) {
              return {
                userId: String(user.user_id),
                username: String(user.username),
                email: user.email ? String(user.email) : null
              };
            }
          }
        }
      }
    } catch (basicAuthErr) {
      // 捕获 Base64 编码或数据库请求异常
    }
  }

  throw new HttpError(401, "unauthorized", "Invalid credentials or token");
}

async function requireAdmin(request, env) {
  const auth = await requireAuth(request, env);
  if (!isAdminUser(auth, env)) {
    throw new HttpError(403, "forbidden", "Administrator access required");
  }

  return auth;
}

function isAdminUser(auth, env) {
  const configured = String(env.ADMIN_USERNAMES || "")
    .split(",")
    .map((item) => item.trim().toLowerCase())
    .filter(Boolean);
  const allowed = configured.length > 0 ? configured : ["luoluo"];
  return allowed.includes(String(auth?.username || "").trim().toLowerCase());
}

async function signToken(env, payload) {
  const header = { alg: "HS256", typ: "JWT" };
  const encodedHeader = base64UrlEncode(JSON.stringify(header));
  const encodedPayload = base64UrlEncode(JSON.stringify(payload));
  const data = `${encodedHeader}.${encodedPayload}`;
  const signature = await hmacSha256(env.AUTH_TOKEN_SECRET, data);
  return `${data}.${signature}`;
}

async function verifyToken(env, token) {
  const parts = token.split(".");
  if (parts.length !== 3) {
    throw new HttpError(401, "unauthorized", "Malformed token");
  }

  const [encodedHeader, encodedPayload, signature] = parts;
  const expected = await hmacSha256(env.AUTH_TOKEN_SECRET, `${encodedHeader}.${encodedPayload}`);
  if (signature !== expected) {
    throw new HttpError(401, "invalid_token_signature", "Token signature verification failed");
  }

  const payload = JSON.parse(base64UrlDecode(encodedPayload));
  const now = Math.floor(Date.now() / 1000);
  if (!payload.exp || Number(payload.exp) <= now) {
    throw new HttpError(401, "token_expired", "Token expired");
  }

  return payload;
}

async function deriveLoginHashInServer(password, email) {
  const enc = new TextEncoder();
  const passwordBytes = enc.encode(password);
  const saltBytes = enc.encode(email.trim().toLowerCase());

  const keyMaterial = await crypto.subtle.importKey(
    "raw",
    passwordBytes,
    "PBKDF2",
    false,
    ["deriveBits"]
  );

  const masterKeyBits = await crypto.subtle.deriveBits(
    {
      name: "PBKDF2",
      salt: saltBytes,
      iterations: 50000,
      hash: "SHA-256"
    },
    keyMaterial,
    256
  );

  const hmacKey = await crypto.subtle.importKey(
    "raw",
    masterKeyBits,
    {
      name: "HMAC",
      hash: "SHA-256"
    },
    false,
    ["sign"]
  );

  const messageBytes = enc.encode("login-verification");
  const signature = await crypto.subtle.sign(
    "HMAC",
    hmacKey,
    messageBytes
  );

  return bytesToHex(new Uint8Array(signature));
}

async function hashPassword(password, salt, iterations) {
  const keyMaterial = await crypto.subtle.importKey(
    "raw",
    textEncoder.encode(password),
    "PBKDF2",
    false,
    ["deriveBits"]
  );
  const bits = await crypto.subtle.deriveBits(
    {
      name: "PBKDF2",
      hash: "SHA-256",
      salt: textEncoder.encode(salt),
      iterations
    },
    keyMaterial,
    256
  );
  return bytesToHex(new Uint8Array(bits));
}

async function hmacSha256(secret, data) {
  const key = await crypto.subtle.importKey(
    "raw",
    textEncoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const signature = await crypto.subtle.sign("HMAC", key, textEncoder.encode(data));
  return base64UrlEncodeBytes(new Uint8Array(signature));
}

async function hashVerificationCode(email, code, salt) {
  const content = `${email.toLowerCase()}|${code}|${salt}`;
  const hash = await crypto.subtle.digest("SHA-256", textEncoder.encode(content));
  return bytesToHex(new Uint8Array(hash));
}

function normalizeUsername(value) {
  const username = String(value || "").trim();
  if (!/^[\p{L}\p{N}_-]{3,32}$/u.test(username)) {
    throw new HttpError(400, "invalid_username", "Username must be 3-32 chars: letters, numbers, _, -");
  }

  return username;
}

function normalizeEmail(value) {
  const email = String(value || "").trim().toLowerCase();
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    throw new HttpError(400, "invalid_email", "Email format is invalid");
  }

  return email;
}

function normalizeVerificationCode(value) {
  const code = String(value || "").trim();
  if (!/^\d{6}$/.test(code)) {
    throw new HttpError(400, "invalid_verification_code", "Verification code must be 6 digits");
  }

  return code;
}

function validatePassword(value) {
  const password = String(value || "");
  if (password.length < 8 || password.length > 128) {
    throw new HttpError(400, "invalid_password", "Password must be 8-128 characters");
  }

  return password;
}

function normalizeDevicePayload(payload) {
  return {
    deviceId: normalizeDeviceId(payload.deviceId || payload.device_id),
    platform: normalizeDevicePlatform(payload.platform),
    displayName: normalizeShortText(payload.displayName || payload.display_name || payload.name, "displayName", 80),
    pushToken: normalizeOptionalString(payload.pushToken || payload.push_token, 512),
    capabilities: normalizeJsonObject(payload.capabilities, "capabilities")
  };
}

function normalizeDeviceMessagePayload(payload) {
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

function normalizeDeviceId(value) {
  const id = String(value || "").trim();
  if (!/^[a-zA-Z0-9_.:-]{6,96}$/.test(id)) {
    throw new HttpError(400, "invalid_device_id", "deviceId must be 6-96 chars: a-z, 0-9, _, ., :, -");
  }

  return id;
}

function normalizeOptionalDeviceId(value) {
  if (value == null || String(value).trim() === "") {
    return null;
  }

  return normalizeDeviceId(value);
}

function normalizeDevicePlatform(value) {
  const platform = String(value || "").trim().toLowerCase();
  if (!/^(desktop|android|ios|web)$/.test(platform)) {
    throw new HttpError(400, "invalid_platform", "platform must be desktop, android, ios, or web");
  }

  return platform;
}

function normalizeOptionalDevicePlatform(value) {
  if (value == null || String(value).trim() === "") {
    return null;
  }

  return normalizeDevicePlatform(value);
}

function normalizeMessageKind(value) {
  const kind = String(value || "text").trim().toLowerCase();
  if (!/^[a-z0-9_.:-]{1,40}$/.test(kind)) {
    throw new HttpError(400, "invalid_message_kind", "kind must be 1-40 lowercase chars");
  }

  return kind;
}

function normalizeMessageId(value) {
  const id = String(value || "").trim();
  if (!/^msg_[a-f0-9]{24}$/.test(id)) {
    throw new HttpError(400, "invalid_message_id", "messageId format is invalid");
  }

  return id;
}

function normalizeMessageLimit(value) {
  const limit = Number.parseInt(String(value || "20"), 10);
  if (!Number.isFinite(limit)) {
    return 20;
  }

  return Math.min(Math.max(limit, 1), 50);
}

function normalizeShortText(value, fieldName, maxLength) {
  const text = String(value || "").trim();
  if (!text) {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} is required`);
  }

  return text.slice(0, maxLength);
}

function normalizeOptionalString(value, maxLength) {
  if (value == null) {
    return null;
  }

  const text = String(value).trim();
  return text ? text.slice(0, maxLength) : null;
}

function normalizeJsonObject(value, fieldName) {
  if (value == null) {
    return {};
  }

  if (typeof value !== "object" || Array.isArray(value)) {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} must be an object`);
  }

  return value;
}

async function ensureOwnedDevice(env, userId, deviceId) {
  const device = await env.DB.prepare(
    `select device_id, platform, display_name
     from user_devices
     where user_id = ? and device_id = ?`
  )
    .bind(userId, deviceId)
    .first();

  if (!device) {
    throw new HttpError(404, "device_not_found", "Device was not found in this account");
  }

  return {
    deviceId: device.device_id,
    platform: device.platform,
    displayName: device.display_name
  };
}

async function touchDevice(env, userId, deviceId) {
  await env.DB.prepare(
    `update user_devices
     set last_seen_at = ?,
         updated_at = ?
     where user_id = ? and device_id = ?`
  )
    .bind(isoNow(), isoNow(), userId, deviceId)
    .run();
}

async function getPendingDeviceMessageItems(env, userId, deviceId, platform, limit = 20) {
  const rows = await env.DB.prepare(
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
      and status = 'pending'
      and (expires_at is null or expires_at > ?)
      and (
        target_device_id = ?
        or (
          target_device_id is null
          and target_platform = ?
        )
      )
    order by created_at asc
    limit ?`
  )
    .bind(userId, isoNow(), deviceId, platform, limit)
    .all();

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

async function notifyDeviceRelay(env, userId) {
  if (!env.DEVICE_RELAY) {
    return;
  }

  try {
    await env.DEVICE_RELAY
      .getByName(userId)
      .fetch(new Request("https://device-relay.internal/internal/device-relay/notify", {
        method: "POST"
      }));
  } catch {
    // The HTTP polling fallback will pick up pending messages if the relay is unavailable.
  }
}

function serializeDeviceRecord(row) {
  return {
    deviceId: row.device_id,
    platform: row.platform,
    displayName: row.display_name,
    capabilities: parseJsonObject(row.capabilities_json),
    lastSeenAt: row.last_seen_at,
    createdAt: row.created_at,
    updatedAt: row.updated_at
  };
}

function serializeDeviceMessageRecord(row) {
  return {
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

async function sendVerificationEmail(env, email, username, code) {
  return sendAuthEmail(
    env,
    email,
    "燕子注册验证码",
    `
    <div style="font-family:Arial,sans-serif;padding:24px;color:#111827">
      <h2 style="margin:0 0 16px">燕子注册验证码</h2>
      <p style="margin:0 0 12px">你好，${escapeHtml(username)}：</p>
      <p style="margin:0 0 12px">你的验证码是：</p>
      <p style="font-size:28px;font-weight:700;letter-spacing:6px;margin:0 0 16px">${code}</p>
      <p style="margin:0;color:#6b7280">验证码 ${VERIFICATION_CODE_TTL_MINUTES} 分钟内有效。</p>
    </div>
  `
  );
}

async function sendPasswordResetEmail(env, email, username, code) {
  return sendAuthEmail(
    env,
    email,
    "燕子密码重置验证码",
    `
    <div style="font-family:Arial,sans-serif;padding:24px;color:#111827">
      <h2 style="margin:0 0 16px">燕子密码重置验证码</h2>
      <p style="margin:0 0 12px">你好，${escapeHtml(username)}：</p>
      <p style="margin:0 0 12px">你正在重置燕子账号密码，验证码是：</p>
      <p style="font-size:28px;font-weight:700;letter-spacing:6px;margin:0 0 16px">${code}</p>
      <p style="margin:0;color:#6b7280">验证码 ${VERIFICATION_CODE_TTL_MINUTES} 分钟内有效。</p>
    </div>
  `
  );
}

async function sendAuthEmail(env, email, subject, html) {
  if (!env.RESEND_API_KEY || !env.RESEND_FROM_EMAIL) {
    throw new HttpError(503, "email_provider_not_configured", "Email provider is not configured");
  }

  const response = await fetch("https://api.resend.com/emails", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${env.RESEND_API_KEY}`,
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      from: env.RESEND_FROM_EMAIL,
      to: [email],
      subject,
      html
    })
  });

  if (!response.ok) {
    const body = await response.text();
    throw new HttpError(502, "email_delivery_failed", `Verification email failed: ${body}`);
  }
}

function serializeExtensionRecord(url, row) {
  const manifest = parseManifestJson(row.manifest_json);
  return {
    extension_id: row.extension_id,
    display_name: row.display_name,
    latest_version: row.latest_version,
    manifest_json: row.manifest_json,
    archive_key: row.archive_key,
    archive_sha256: row.archive_sha256,
    icon_key: row.icon_key || "",
    publisher_user_id: row.publisher_user_id || "",
    publisher_username: row.publisher_username || "",
    published_at: row.published_at || "",
    is_published: Number(row.is_published ?? 1),
    updated_at: row.updated_at,
    install_count: Number(row.install_count || 0),
    description: manifest.description || "",
    category: manifest.category || "扩展",
    icon: resolveStoreIcon(url, row, manifest.icon || "", row.extension_id),
    accent_hex: manifest.accentHex || manifest.accent_hex || "",
    keywords: Array.isArray(manifest.keywords) ? manifest.keywords : [],
    manifest,
    archive_download_url: row.archive_key
      ? `${url.origin}/v1/extensions/${encodeURIComponent(row.extension_id)}/archive`
      : null,
    install_protocol_url: row.archive_key
      ? buildInstallProtocolUrl(
          row.extension_id,
          `${url.origin}/v1/extensions/${encodeURIComponent(row.extension_id)}/archive`
        )
      : null
  };
}

function serializeExtensionListItem(url, row, publicDefinition = null) {
  if (publicDefinition) {
    const full = serializeStoreExtensionRecord(url, row, publicDefinition);
    return {
      extension_id: full.extension_id,
      display_name: full.display_name,
      latest_version: full.latest_version,
      publisher_user_id: full.publisher_user_id,
      publisher_username: full.publisher_username,
      published_at: full.published_at,
      is_published: full.is_published,
      install_count: full.install_count,
      description: full.description,
      category: full.category,
      icon: full.icon,
      accent_hex: full.accent_hex,
      keywords: full.keywords,
      archive_download_url: full.archive_download_url,
      install_protocol_url: full.install_protocol_url
    };
  }

  const full = serializeExtensionRecord(url, row);
  return {
    extension_id: full.extension_id,
    display_name: full.display_name,
    latest_version: full.latest_version,
    publisher_user_id: full.publisher_user_id,
    publisher_username: full.publisher_username,
    published_at: full.published_at,
    is_published: full.is_published,
    install_count: full.install_count,
    description: full.description,
    category: full.category,
    icon: full.icon,
    accent_hex: full.accent_hex,
    keywords: full.keywords,
    archive_download_url: full.archive_download_url,
    install_protocol_url: full.install_protocol_url
  };
}

function matchesStoreSearch(item, search) {
  if (!search) {
    return true;
  }

  const haystacks = [
    item.extension_id,
    item.display_name,
    item.latest_version,
    item.description,
    item.category,
    ...(item.keywords || [])
  ];
  return haystacks.some((value) =>
    String(value || "").toLowerCase().includes(search)
  );
}

function serializeStoreExtensionRecord(url, row, definition) {
  const rowManifest = row ? parseManifestJson(row.manifest_json) : {};
  const manifest = {
    id: definition.extension_id,
    name: definition.display_name,
    version: row?.latest_version || definition.latest_version,
    description: definition.description,
    category: definition.category,
    keywords: definition.keywords,
    ...(rowManifest && typeof rowManifest === "object" ? rowManifest : {})
  };
  const archiveUrl = `${PUBLIC_SITE_ORIGIN}${definition.package_path}`;

  return {
    extension_id: definition.extension_id,
    display_name: definition.display_name,
    latest_version: row?.latest_version || definition.latest_version,
    manifest_json: JSON.stringify(manifest),
    archive_key: row?.archive_key || definition.package_path,
    archive_sha256: row?.archive_sha256 || null,
    icon_key: row?.icon_key || "",
    publisher_user_id: row?.publisher_user_id || "",
    publisher_username: row?.publisher_username || "燕子团队",
    published_at: row?.published_at || "",
    is_published: Number(row?.is_published ?? 1),
    updated_at: row?.updated_at || null,
    install_count: Number(row?.install_count || 0),
    description: definition.description,
    category: definition.category,
    icon: resolveStoreIcon(url, row, definition.icon || rowManifest.icon || "", definition.extension_id),
    accent_hex: manifest.accentHex || manifest.accent_hex || "",
    keywords: definition.keywords,
    manifest,
    archive_download_url: archiveUrl,
    install_protocol_url: buildInstallProtocolUrl(definition.extension_id, archiveUrl)
  };
}

function serializeUserExtensionRecord(url, row, userId) {
  const manifest = parseManifestJson(row.manifest_json);
  const settings = parseManifestJson(row.settings_json);
  const extensionId = row.extension_id;
  const displayName = row.display_name || settings.displayName || settings.title || manifest.displayName || manifest.name || extensionId;
  const icon = row.icon_key
    ? buildMyExtensionIconUrl(url, extensionId, row.extension_updated_at || row.updated_at || "")
    : (manifest.icon || settings.icon || settings.manifest?.icon || "");
  return {
    user_id: row.user_id,
    extension_id: extensionId,
    installed_version: row.installed_version,
    enabled: Number(row.enabled ?? 1),
    settings_json: row.settings_json || "{}",
    settings,
    updated_at: row.updated_at,
    display_name: displayName,
    latest_version: row.latest_version || row.installed_version,
    manifest_json: row.manifest_json || "",
    manifest,
    icon_key: row.icon_key || "",
    icon,
    archive_key: row.archive_key || "",
    archive_sha256: row.archive_sha256 || "",
    archive_revision: Number(row.archive_revision || 0),
    archive_updated_at: row.archive_updated_at || "",
    archive_updated_by_device_id: row.archive_updated_by_device_id || "",
    archive_updated_by_device_name: row.archive_updated_by_device_name || "",
    has_archive: Boolean(row.archive_key),
    archive_download_url: row.archive_key ? buildMyExtensionArchiveUrl(url, extensionId) : null,
    publisher_user_id: row.publisher_user_id || "",
    publisher_username: row.publisher_username || "",
    published_at: row.published_at || "",
    is_published: Number(row.is_published ?? 0),
    is_private: String(row.publisher_user_id || "") === String(userId) && Number(row.is_published ?? 0) === 0,
    description: manifest.description || settings.description || settings.manifest?.description || "",
    category: manifest.category || settings.manifest?.category || "扩展",
    accent_hex: manifest.accentHex || manifest.accent_hex || settings.accentHex || settings.accent_hex || settings.manifest?.accentHex || "",
    keywords: Array.isArray(manifest.keywords) ? manifest.keywords : []
  };
}

function resolveStoreIcon(url, row, manifestIcon, extensionId) {
  if (row?.icon_key) {
    return buildExtensionIconUrl(url, extensionId, row.updated_at || row.published_at || "");
  }

  return manifestIcon || "";
}

function isStoreVisibleExtension(row) {
  const manifest = parseManifestJson(row?.manifest_json);
  const extensionId = String(row?.extension_id || "").trim().toLowerCase();
  const category = String(manifest.category || "").trim().toLowerCase();

  if (!extensionId) {
    return false;
  }

  if (extensionId === "yanzi-webdav-settings") {
    return false;
  }

  if (category === "系统配置") {
    return false;
  }

  if (Number(row?.is_published ?? 1) === 0) {
    return false;
  }

  return true;
}

function parseManifestJson(value) {
  try {
    const parsed = JSON.parse(String(value || "{}"));
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
}

function buildInstallProtocolUrl(extensionId, archiveUrl) {
  return `yanzi://install?extensionId=${encodeURIComponent(extensionId)}&source=${encodeURIComponent(archiveUrl)}`;
}

function buildExtensionIconUrl(url, extensionId, cacheBust = "") {
  const iconUrl = new URL(`/v1/extensions/${encodeURIComponent(extensionId)}/icon`, url.origin);
  if (cacheBust) {
    iconUrl.searchParams.set("v", String(cacheBust));
  }

  return iconUrl.toString();
}

function buildMyExtensionIconUrl(url, extensionId, cacheBust = "") {
  const iconUrl = new URL(`/v1/me/extensions/${encodeURIComponent(extensionId)}/icon`, url.origin);
  if (cacheBust) {
    iconUrl.searchParams.set("v", String(cacheBust));
  }

  return iconUrl.toString();
}

function buildMyExtensionArchiveUrl(url, extensionId) {
  return `${url.origin}/v1/me/extensions/${encodeURIComponent(extensionId)}/archive`;
}

function resolveIconExtension(filename, contentType) {
  const lowerFilename = String(filename || "").toLowerCase();
  const lowerType = String(contentType || "").toLowerCase();

  if (lowerFilename.endsWith(".png") || lowerType.includes("image/png")) {
    return ".png";
  }

  if (lowerFilename.endsWith(".jpg") || lowerFilename.endsWith(".jpeg") || lowerType.includes("image/jpeg")) {
    return ".jpg";
  }

  if (lowerFilename.endsWith(".gif") || lowerType.includes("image/gif")) {
    return ".gif";
  }

  if (lowerFilename.endsWith(".webp") || lowerType.includes("image/webp")) {
    return ".webp";
  }

  if (lowerFilename.endsWith(".bmp") || lowerType.includes("image/bmp")) {
    return ".bmp";
  }

  if (lowerFilename.endsWith(".ico") || lowerType.includes("image/x-icon") || lowerType.includes("image/vnd.microsoft.icon")) {
    return ".ico";
  }

  if (lowerFilename.endsWith(".svg") || lowerType.includes("image/svg+xml")) {
    return ".svg";
  }

  return ".img";
}

function normalizeReleaseChannel(value) {
  const channel = String(value || DEFAULT_APP_UPDATE_CHANNEL).trim().toLowerCase();
  if (!/^[a-z0-9_-]{1,32}$/.test(channel)) {
    throw new HttpError(400, "invalid_channel", "Channel must be 1-32 chars: a-z, 0-9, _, -");
  }

  return channel;
}

function normalizeAppReleasePayload(payload, channel) {
  const version = String(payload.version || "").trim();
  if (!version || version.length > 50) {
    throw new HttpError(400, "invalid_version", "Version is required and must be 1-50 characters");
  }

  const downloadUrl = normalizePublicHttpUrl(payload.download_url ?? payload.downloadUrl, "download_url");
  const title = String(payload.title || "燕子启动器 for Windows").trim().slice(0, 120);
  const fileName = String(payload.file_name ?? payload.fileName ?? "").trim().slice(0, 200);
  const downloadCode = String(payload.download_code ?? payload.downloadCode ?? "").trim().slice(0, 50);
  const provider = String(payload.provider || "custom").trim().slice(0, 50) || "custom";
  const notes = String(payload.notes || "").trim().slice(0, 8000);
  const sha256 = String(payload.sha256 || "").trim().toLowerCase();
  if (sha256 && !/^[a-f0-9]{64}$/.test(sha256)) {
    throw new HttpError(400, "invalid_sha256", "sha256 must be a 64-character lowercase hex string");
  }

  const publishedAtInput = String(payload.published_at ?? payload.publishedAt ?? "").trim();
  const publishedAt = publishedAtInput ? new Date(publishedAtInput) : new Date();
  if (Number.isNaN(publishedAt.getTime())) {
    throw new HttpError(400, "invalid_published_at", "published_at must be a valid datetime");
  }

  return {
    channel,
    version,
    title,
    notes,
    download_url: downloadUrl,
    file_name: fileName,
    download_code: downloadCode,
    provider,
    sha256,
    published_at: publishedAt.toISOString()
  };
}

function normalizePublicHttpUrl(value, fieldName) {
  const raw = String(value || "").trim();
  if (!raw) {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} is required`);
  }

  let parsed;
  try {
    parsed = new URL(raw);
  } catch {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} must be a valid URL`);
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    throw new HttpError(400, `invalid_${fieldName}`, `${fieldName} must use http or https`);
  }

  return parsed.toString();
}

function serializeAppRelease(row, channel = DEFAULT_APP_UPDATE_CHANNEL) {
  const fallback = {
    ...DEFAULT_APP_RELEASE,
    channel
  };
  const merged = row
    ? {
        ...fallback,
        channel: row.channel || channel,
        version: row.version || fallback.version,
        title: row.title || fallback.title,
        notes: row.notes || "",
        download_url: row.download_url || fallback.download_url,
        file_name: row.file_name || fallback.file_name,
        download_code: row.download_code || fallback.download_code,
        provider: row.provider || fallback.provider,
        sha256: row.sha256 || "",
        published_at: row.published_at || fallback.published_at,
        updated_at: row.updated_at || row.published_at || fallback.published_at,
        updated_by_user_id: row.updated_by_user_id || "",
        updated_by_username: row.updated_by_username || ""
      }
    : {
        ...fallback,
        notes: fallback.notes || "",
        updated_at: fallback.published_at,
        updated_by_user_id: "",
        updated_by_username: ""
      };

  return {
    channel: merged.channel,
    version: merged.version,
    title: merged.title,
    notes: merged.notes,
    download_url: merged.download_url,
    downloadUrl: merged.download_url,
    file_name: merged.file_name,
    fileName: merged.file_name,
    download_code: merged.download_code,
    downloadCode: merged.download_code,
    provider: merged.provider,
    sha256: merged.sha256,
    published_at: merged.published_at,
    publishedAt: merged.published_at,
    updated_at: merged.updated_at,
    updatedAt: merged.updated_at,
    updated_by_user_id: merged.updated_by_user_id,
    updated_by_username: merged.updated_by_username,
    managed: Boolean(row)
  };
}

async function ensureUser(env, userId) {
  await env.DB.prepare(
    `insert into users (user_id, created_at, updated_at)
     values (?, ?, ?)
     on conflict(user_id) do update set updated_at = excluded.updated_at`
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
  await ensureUser(env, userId);
  const rows = await env.DB.prepare(
    `select object_id, schema_version, object_revision, updated_at,
            updated_by_device_id, updated_by_device_name, deleted, payload_json
     from user_sync_objects
     where user_id = ? and object_revision > ?
     order by object_revision asc
     limit ?`
  ).bind(userId, sinceRevision, limit + 1).all();
  const allRows = rows.results || [];
  const hasMore = allRows.length > limit;
  const selectedRows = hasMore ? allRows.slice(0, limit) : allRows;
  const objects = selectedRows.map(serializeUserSyncObject);
  const currentRevision = await getUserSyncRevision(env, userId);
  const cursorRevision = objects.length > 0
    ? objects[objects.length - 1].revision
    : currentRevision;
  return { currentRevision, cursorRevision, hasMore, objects };
}

async function readUserSyncObjectHistory(env, userId, objectId, beforeRevision, limit) {
  await ensureUser(env, userId);
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

async function upsertPrivateExtensionMetadata(env, auth, extensionId, manifest) {
  const existing = await env.DB.prepare(
    `select publisher_user_id
     from extensions
     where extension_id = ?`
  )
    .bind(extensionId)
    .first();

  if (existing?.publisher_user_id &&
      String(existing.publisher_user_id) !== auth.userId) {
    throw new HttpError(403, "forbidden", "Only the owner can update this private extension");
  }

  const now = isoNow();
  const displayName = String(manifest.displayName ?? manifest.name ?? extensionId).slice(0, 200);
  const latestVersion = String(manifest.version ?? "0.0.0").slice(0, 50);

  await env.DB.prepare(
    `insert into extensions (
      extension_id,
      display_name,
      latest_version,
      manifest_json,
      publisher_user_id,
      publisher_username,
      published_at,
      is_published,
      updated_at
    ) values (?, ?, ?, ?, ?, ?, ?, 0, ?)
    on conflict(extension_id) do update set
      display_name = excluded.display_name,
      latest_version = excluded.latest_version,
      manifest_json = excluded.manifest_json,
      publisher_user_id = coalesce(extensions.publisher_user_id, excluded.publisher_user_id),
      publisher_username = excluded.publisher_username,
      is_published = extensions.is_published,
      updated_at = excluded.updated_at`
  )
    .bind(
      extensionId,
      displayName,
      latestVersion,
      JSON.stringify(manifest),
      auth.userId,
      auth.username,
      now,
      now
    )
    .run();
}

async function ensurePrivateExtensionWritable(env, auth, extensionId) {
  let row = await env.DB.prepare(
    `select publisher_user_id, display_name, latest_version
     from extensions
     where extension_id = ?`
  )
    .bind(extensionId)
    .first();

  if (!row) {
    await upsertPrivateExtensionMetadata(env, auth, extensionId, {
      name: extensionId,
      displayName: extensionId,
      version: "0.0.0"
    });
    row = await env.DB.prepare(
      `select publisher_user_id
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();
  }

  if (row?.publisher_user_id && String(row.publisher_user_id) !== auth.userId) {
    throw new HttpError(403, "forbidden", "Only the owner can update this extension");
  }
}

async function ensureUserCanReadExtension(env, userId, extensionId) {
  const row = await env.DB.prepare(
    `select e.publisher_user_id, e.is_published, ue.user_id
     from extensions e
     left join user_extensions ue
       on ue.extension_id = e.extension_id
      and ue.user_id = ?
     where e.extension_id = ?`
  )
    .bind(userId, extensionId)
    .first();

  if (!row) {
    throw new HttpError(404, "extension_not_found", "Extension not found");
  }

  if (Number(row.is_published ?? 0) === 1 ||
      String(row.publisher_user_id || "") === String(userId) ||
      String(row.user_id || "") === String(userId)) {
    return;
  }

  throw new HttpError(403, "forbidden", "You do not have access to this extension");
}

async function touchUser(env, userId) {
  await env.DB.prepare(
    "update users set updated_at = ? where user_id = ?"
  )
    .bind(isoNow(), userId)
    .run();
}

async function getUserWebDavConfig(env, userId) {
  const row = await env.DB.prepare(
    `select settings_json
     from user_extensions
     where user_id = ? and extension_id = ?`
  )
    .bind(userId, "yanzi-webdav-settings")
    .first();

  if (!row?.settings_json) {
    throw new HttpError(404, "webdav_config_missing", "WebDAV config is not configured for this account");
  }

  let settings;
  try {
    settings = JSON.parse(String(row.settings_json));
  } catch {
    throw new HttpError(500, "webdav_config_invalid", "Stored WebDAV config is invalid");
  }

  const config = {
    enabled: Boolean(readFirst(settings, ["enabled", "enableWebDavSync"])),
    serverUrl: String(readFirst(settings, ["serverUrl", "webDavServerUrl"]) || "").trim(),
    rootPath: String(readFirst(settings, ["rootPath", "webDavRootPath"]) || "/yanzi").trim(),
    username: String(readFirst(settings, ["username", "webDavUsername"]) || "").trim(),
    password: String(readFirst(settings, ["password", "webDavPassword"]) || "").trim()
  };

  if (!config.enabled) {
    throw new HttpError(409, "webdav_disabled", "WebDAV sync is disabled for this account");
  }

  if (!config.serverUrl || !config.username || !config.password) {
    throw new HttpError(409, "webdav_config_incomplete", "WebDAV server, username, or app password is missing");
  }

  return config;
}

async function readYanmStateForUser(env, userId) {
  try {
    const syncConfig = await getUserPersonalSyncConfig(env, userId);
    if (!hasPersonalSyncCredential(syncConfig)) {
      throw new HttpError(409, "sync_credentials_device_local", "Personal sync credentials are stored on the desktop device");
    }
    let result;
    const provider = syncConfig.provider;
    if (provider === "github") {
      result = await readYanmStateFromGitHub(syncConfig);
    } else if (provider === "gitee") {
      result = await readYanmStateFromGitee(syncConfig);
    } else if (provider === "gitlab") {
      result = await readYanmStateFromGitLab(syncConfig);
    } else if (provider === "gitea") {
      result = await readYanmStateFromGitea(syncConfig);
    } else if (provider === "s3") {
      result = await readYanmStateFromS3(syncConfig);
    } else if (provider === "webdav") {
      const config = buildLegacyWebDavConfig(syncConfig);
      result = await readYanmStateFromWebDav(config);
    } else {
      throw new HttpError(400, "unsupported_provider", `Provider ${provider} is not supported for web sync`);
    }

    return {
      ...result,
      source: provider
    };
  } catch (error) {
    const fallback = await readYanmStateFromCloudConfig(env, userId);
    if (fallback) {
      const diagnostics = buildSafeErrorDiagnostics(error);
      console.warn("Yanm sync read failed, falling back to cloud config", {
        userId,
        diagnostics
      });
      return {
        ...fallback,
        source: "cloud-config",
        warning: error instanceof Error ? error.message : "Sync read failed",
        diagnostics
      };
    }

    if (error instanceof HttpError && error.code === "sync_credentials_device_local") {
      return null;
    }

    throw error;
  }
}

async function readYanmStateFromCloudConfig(env, userId) {
  const row = await env.DB.prepare(
    `select settings_json, updated_at
     from user_extensions
     where user_id = ? and extension_id = ?`
  )
    .bind(userId, "yanzi-quickpanel-settings")
    .first();

  if (!row?.settings_json) {
    return null;
  }

  let settings;
  try {
    settings = JSON.parse(String(row.settings_json));
  } catch {
    return null;
  }

  const yanm = settings.yanm || settings.Yanm || null;
  if (!yanm) {
    return null;
  }

  const text = JSON.stringify(yanm);
  return {
    updatedAtUtc: String(settings.yanmUpdatedAtUtc || row.updated_at || ""),
    yanm,
    bytes: textEncoder.encode(text).length
  };
}

async function writeYanmStateToCloudConfig(env, userId, snapshot) {
  await ensureUser(env, userId);

  const existing = await env.DB.prepare(
    `select settings_json
     from user_extensions
     where user_id = ? and extension_id = ?`
  )
    .bind(userId, "yanzi-quickpanel-settings")
    .first();

  let settings = {};
  if (existing?.settings_json) {
    try {
      settings = JSON.parse(String(existing.settings_json));
    } catch {
      settings = {};
    }
  }

  settings.yanm = snapshot.yanm;
  settings.yanmUpdatedAtUtc = snapshot.updatedAtUtc || isoNow();

  await ensureSystemConfigExtension(env, "yanzi-quickpanel-settings", {
    displayName: "Yanzi Quick Panel Settings",
    description: "Stores quick panel and Yanm configuration for the current account."
  });

  const now = snapshot.updatedAtUtc || isoNow();
  const settingsJson = JSON.stringify(settings);
  await env.DB.prepare(
    `insert into user_extensions (
      user_id,
      extension_id,
      installed_version,
      enabled,
      settings_json,
      updated_at
    ) values (?, ?, ?, ?, ?, ?)
    on conflict(user_id, extension_id) do update set
      installed_version = excluded.installed_version,
      enabled = excluded.enabled,
      settings_json = excluded.settings_json,
      updated_at = excluded.updated_at`
  )
    .bind(userId, "yanzi-quickpanel-settings", "1", 1, settingsJson, now)
    .run();

  return {
    bytes: textEncoder.encode(JSON.stringify(snapshot.yanm)).length
  };
}

async function getUserPersonalSyncConfig(env, userId) {
  const row = await env.DB.prepare(
    `select settings_json
     from user_extensions
     where user_id = ? and extension_id = ?`
  )
    .bind(userId, "yanzi-personal-sync-settings")
    .first();

  if (!row?.settings_json) {
    return getLegacyUserWebDavConfig(env, userId);
  }

  let snap;
  try {
    snap = JSON.parse(String(row.settings_json));
  } catch {
    throw new HttpError(500, "sync_config_invalid", "Stored personal sync config is invalid");
  }

  if (!snap.enabled) {
    throw new HttpError(409, "sync_disabled", "Personal sync is disabled for this account");
  }

  const provider = String(snap.provider || "").toLowerCase().trim();
  if (!provider || provider === "none") {
    throw new HttpError(409, "sync_disabled", "Personal sync provider is set to none");
  }

  return {
    provider,
    settings: snap.settings || {},
    secrets: snap.secrets || {}
  };
}

async function getLegacyUserWebDavConfig(env, userId) {
  const row = await env.DB.prepare(
    `select settings_json
     from user_extensions
     where user_id = ? and extension_id = ?`
  )
    .bind(userId, "yanzi-webdav-settings")
    .first();

  if (!row?.settings_json) {
    throw new HttpError(404, "sync_config_missing", "Sync config is not configured for this account");
  }

  let settings;
  try {
    settings = JSON.parse(String(row.settings_json));
  } catch {
    throw new HttpError(500, "webdav_config_invalid", "Stored WebDAV config is invalid");
  }

  const config = {
    enabled: Boolean(readFirst(settings, ["enabled", "enableWebDavSync"])),
    serverUrl: String(readFirst(settings, ["serverUrl", "webDavServerUrl"]) || "").trim(),
    rootPath: String(readFirst(settings, ["rootPath", "webDavRootPath"]) || "/yanzi").trim(),
    username: String(readFirst(settings, ["username", "webDavUsername"]) || "").trim(),
    password: String(readFirst(settings, ["password", "webDavPassword"]) || "").trim()
  };

  if (!config.enabled) {
    throw new HttpError(409, "webdav_disabled", "WebDAV sync is disabled for this account");
  }

  return {
    provider: "webdav",
    settings: {
      webDav: {
        url: config.serverUrl,
        username: config.username,
        pathPrefix: config.rootPath
      }
    },
    secrets: {
      webDavPassword: config.password
    }
  };
}

function buildLegacyWebDavConfig(syncConfig) {
  const webDav = syncConfig.settings.WebDav || syncConfig.settings.webDav || {};
  return {
    enabled: true,
    serverUrl: String(webDav.url || webDav.Url || "").trim(),
    rootPath: String(webDav.pathPrefix || webDav.PathPrefix || "/yanzi").trim(),
    username: String(webDav.username || webDav.Username || "").trim(),
    password: String(syncConfig.secrets.WebDavPassword || syncConfig.secrets.webDavPassword || "").trim()
  };
}

function hasPersonalSyncCredential(syncConfig) {
  const secrets = syncConfig?.secrets || {};
  if (syncConfig?.provider === "github") return Boolean(String(secrets.githubToken || secrets.GitHubToken || "").trim());
  if (syncConfig?.provider === "gitee") return Boolean(String(secrets.giteeToken || secrets.GiteeToken || "").trim());
  if (syncConfig?.provider === "gitlab") return Boolean(String(secrets.gitLabToken || secrets.gitlabToken || secrets.GitLabToken || "").trim());
  if (syncConfig?.provider === "gitea") return Boolean(String(secrets.giteaToken || secrets.GiteaToken || "").trim());
  if (syncConfig?.provider === "s3") return Boolean(String(secrets.s3SecretAccessKey || secrets.S3SecretAccessKey || "").trim());
  if (syncConfig?.provider === "webdav") return Boolean(String(secrets.webDavPassword || secrets.WebDavPassword || "").trim());
  return false;
}

function base64ToUtf8(str) {
  const binary = atob(str);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return new TextDecoder("utf-8").decode(bytes);
}

function utf8ToBase64(str) {
  const bytes = new TextEncoder().encode(str);
  let binary = "";
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

async function readYanmStateFromGitHub(syncConfig) {
  const { token, owner, repo, branch, pathPrefix } = await resolveGitHubRepoTarget(syncConfig);

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const url = `https://api.github.com/repos/${owner}/${repo}/contents/${path}?ref=${branch}`;

  const response = await fetch(url, {
    method: "GET",
    headers: {
      "accept": "application/vnd.github+json",
      "authorization": `Bearer ${token}`,
      "user-agent": "YanziClient-Web"
    }
  });

  if (response.status === 404) {
    throw new HttpError(404, "yanm_state_missing", "Yanm state was not found in GitHub");
  }

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "read_github_failed", `Failed to read Yanm state from GitHub (${response.status}): ${errText}`);
  }

  const data = await response.json();
  const contentBase64 = data.content.replace(/\s/g, "");
  const text = base64ToUtf8(contentBase64);
  let snapshot;
  try {
    snapshot = JSON.parse(text);
  } catch {
    throw new HttpError(502, "yanm_state_invalid", "Yanm state JSON in GitHub is invalid");
  }

  return {
    updatedAtUtc: snapshot.updatedAtUtc || snapshot.UpdatedAtUtc || "",
    yanm: snapshot.yanm || snapshot.Yanm || null,
    bytes: textEncoder.encode(text).length
  };
}

async function resolveGitHubRepoTarget(syncConfig) {
  const github = syncConfig.settings.GitHub || syncConfig.settings.gitHub || {};
  const token = (syncConfig.secrets.GitHubToken || syncConfig.secrets.gitHubToken || "").trim();
  const repoRaw = String(github.repo || github.Repo || "yanzi-sync").trim();
  const branch = String(github.branch || github.Branch || "main").trim();
  const pathPrefix = String(github.pathPrefix || github.PathPrefix || "").trim();

  let owner = String(github.username || github.Username || "").trim();
  let repo = repoRaw;
  if (repoRaw.includes("/")) {
    const parts = repoRaw.split("/");
    owner = parts[0].trim();
    repo = parts[1].trim();
  }

  if (!token || !repo) {
    throw new HttpError(400, "github_config_incomplete", "GitHub token or repo is incomplete");
  }

  if (!owner) {
    owner = await resolveGitHubOwnerFromToken(token);
  }

  return { token, owner, repo, branch, pathPrefix };
}

async function resolveGitHubOwnerFromToken(token) {
  const response = await fetch("https://api.github.com/user", {
    method: "GET",
    headers: {
      "accept": "application/vnd.github+json",
      "authorization": `Bearer ${token}`,
      "user-agent": "YanziClient-Web"
    }
  });

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "github_owner_resolve_failed", `Failed to resolve GitHub owner from token (${response.status}): ${errText}`);
  }

  const data = await response.json();
  const owner = String(data.login || "").trim();
  if (!owner) {
    throw new HttpError(400, "github_owner_missing", "GitHub token did not return an account login");
  }

  return owner;
}

async function writeYanmStateToGitHub(syncConfig, snapshot) {
  const { token, owner, repo, branch, pathPrefix } = await resolveGitHubRepoTarget(syncConfig);

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const url = `https://api.github.com/repos/${owner}/${repo}/contents/${path}`;

  let existingSha = null;
  try {
    const getResp = await fetch(`${url}?ref=${branch}`, {
      method: "GET",
      headers: {
        "accept": "application/vnd.github+json",
        "authorization": `Bearer ${token}`,
        "user-agent": "YanziClient-Web"
      }
    });
    if (getResp.ok) {
      const getData = await getResp.json();
      existingSha = getData.sha;
    }
  } catch (e) {
    console.warn("Failed to fetch GitHub sha", e);
  }

  const bodyObj = {
    updatedAtUtc: snapshot.updatedAtUtc,
    yanm: snapshot.yanm
  };
  const bodyText = JSON.stringify(bodyObj, null, 2);
  const base64Content = utf8ToBase64(bodyText);

  const putPayload = {
    message: "Update yanm-state.json from Web",
    content: base64Content,
    branch
  };
  if (existingSha) {
    putPayload.sha = existingSha;
  }

  const putResp = await fetch(url, {
    method: "PUT",
    headers: {
      "accept": "application/vnd.github+json",
      "authorization": `Bearer ${token}`,
      "content-type": "application/json",
      "user-agent": "YanziClient-Web"
    },
    body: JSON.stringify(putPayload)
  });

  if (!putResp.ok) {
    const errText = await putResp.text();
    throw new HttpError(putResp.status || 502, "write_github_failed", `Failed to write Yanm state to GitHub (${putResp.status}): ${errText}`);
  }

  return {
    bytes: textEncoder.encode(bodyText).length
  };
}

async function hmacSha256Bytes(key, data) {
  const cryptoKey = await crypto.subtle.importKey(
    "raw",
    typeof key === "string" ? new TextEncoder().encode(key) : key,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const signature = await crypto.subtle.sign(
    "HMAC",
    cryptoKey,
    typeof data === "string" ? new TextEncoder().encode(data) : data
  );
  return new Uint8Array(signature);
}

async function sha256(data) {
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(data));
  return Array.from(new Uint8Array(hash)).map(b => b.toString(16).padStart(2, '0')).join('');
}

async function awsV4Sign(method, urlStr, headers, bodyText, accessKeyId, secretAccessKey, region, service = "s3") {
  const url = new URL(urlStr);
  const now = new Date();
  const amzDate = now.toISOString().replace(/[:-]/g, "").split(".")[0] + "Z";
  const dateStamp = amzDate.slice(0, 8);

  const newHeaders = { ...headers };
  newHeaders["host"] = url.host;
  newHeaders["x-amz-date"] = amzDate;

  const bodyHash = await sha256(bodyText || "");
  newHeaders["x-amz-content-sha256"] = bodyHash;

  const headerKeys = Object.keys(newHeaders).map(k => k.toLowerCase()).sort();
  const canonicalHeaders = headerKeys.map(k => `${k}:${newHeaders[k].trim()}`).join("\n") + "\n";
  const signedHeaders = headerKeys.join(";");

  const canonicalUri = encodeURI(url.pathname);
  const queryKeys = Array.from(url.searchParams.keys()).sort();
  const canonicalQueryString = queryKeys.map(k => `${encodeURIComponent(k)}=${encodeURIComponent(url.searchParams.get(k))}`).join("&");

  const canonicalRequest = [
    method.toUpperCase(),
    canonicalUri,
    canonicalQueryString,
    canonicalHeaders,
    signedHeaders,
    bodyHash
  ].join("\n");

  const canonicalRequestHash = await sha256(canonicalRequest);
  const credentialScope = [dateStamp, region, service, "aws4_request"].join("/");
  const stringToSign = [
    "AWS4-HMAC-SHA256",
    amzDate,
    credentialScope,
    canonicalRequestHash
  ].join("\n");

  const kDate = await hmacSha256Bytes(`AWS4${secretAccessKey}`, dateStamp);
  const kRegion = await hmacSha256Bytes(kDate, region);
  const kService = await hmacSha256Bytes(kRegion, service);
  const kSigning = await hmacSha256Bytes(kService, "aws4_request");

  const signatureBytes = await hmacSha256Bytes(kSigning, stringToSign);
  const signature = Array.from(signatureBytes).map(b => b.toString(16).padStart(2, '0')).join('');

  newHeaders["Authorization"] = `AWS4-HMAC-SHA256 Credential=${accessKeyId}/${credentialScope}, SignedHeaders=${signedHeaders}, Signature=${signature}`;
  return newHeaders;
}

async function readYanmStateFromGitee(syncConfig) {
  const gitee = syncConfig.settings.Gitee || syncConfig.settings.gitee || {};
  const token = (syncConfig.secrets.GiteeToken || syncConfig.secrets.giteeToken || "").trim();
  const repoRaw = String(gitee.repo || gitee.Repo || "yanzi-sync").trim();
  const branch = String(gitee.branch || gitee.Branch || "master").trim();
  const pathPrefix = String(gitee.pathPrefix || gitee.PathPrefix || "").trim();

  let owner = String(gitee.username || gitee.Username || "").trim();
  let repo = repoRaw;
  if (repoRaw.includes("/")) {
    const parts = repoRaw.split("/");
    owner = parts[0].trim();
    repo = parts[1].trim();
  }

  if (!token || !owner || !repo) {
    throw new HttpError(400, "gitee_config_incomplete", "Gitee token, owner, or repo is incomplete");
  }

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const url = `https://gitee.com/api/v5/repos/${owner}/${repo}/contents/${path}?access_token=${token}&ref=${branch}`;

  const response = await fetch(url, {
    method: "GET",
    headers: {
      "user-agent": "YanziClient-Web"
    }
  });

  if (response.status === 404) {
    throw new HttpError(404, "yanm_state_missing", "Yanm state was not found in Gitee");
  }

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "read_gitee_failed", `Failed to read Yanm state from Gitee (${response.status}): ${errText}`);
  }

  const data = await response.json();
  const contentBase64 = data.content.replace(/\s/g, "");
  const text = base64ToUtf8(contentBase64);
  let snapshot = JSON.parse(text);

  return {
    updatedAtUtc: snapshot.updatedAtUtc || snapshot.UpdatedAtUtc || "",
    yanm: snapshot.yanm || snapshot.Yanm || null,
    bytes: textEncoder.encode(text).length
  };
}

async function writeYanmStateToGitee(syncConfig, snapshot) {
  const gitee = syncConfig.settings.Gitee || syncConfig.settings.gitee || {};
  const token = (syncConfig.secrets.GiteeToken || syncConfig.secrets.giteeToken || "").trim();
  const repoRaw = String(gitee.repo || gitee.Repo || "yanzi-sync").trim();
  const branch = String(gitee.branch || gitee.Branch || "master").trim();
  const pathPrefix = String(gitee.pathPrefix || gitee.PathPrefix || "").trim();

  let owner = String(gitee.username || gitee.Username || "").trim();
  let repo = repoRaw;
  if (repoRaw.includes("/")) {
    const parts = repoRaw.split("/");
    owner = parts[0].trim();
    repo = parts[1].trim();
  }

  if (!token || !owner || !repo) {
    throw new HttpError(400, "gitee_config_incomplete", "Gitee token, owner, or repo is incomplete");
  }

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const url = `https://gitee.com/api/v5/repos/${owner}/${repo}/contents/${path}`;

  let existingSha = null;
  try {
    const getResp = await fetch(`${url}?access_token=${token}&ref=${branch}`, {
      method: "GET",
      headers: {
        "user-agent": "YanziClient-Web"
      }
    });
    if (getResp.ok) {
      const getData = await getResp.json();
      existingSha = getData.sha;
    }
  } catch (e) {
    console.warn("Failed to fetch Gitee sha", e);
  }

  const bodyObj = {
    updatedAtUtc: snapshot.updatedAtUtc,
    yanm: snapshot.yanm
  };
  const bodyText = JSON.stringify(bodyObj, null, 2);
  const base64Content = utf8ToBase64(bodyText);

  const putPayload = {
    access_token: token,
    message: "Update yanm-state.json from Web",
    content: base64Content,
    branch
  };
  if (existingSha) {
    putPayload.sha = existingSha;
  }

  const putResp = await fetch(url, {
    method: "PUT",
    headers: {
      "content-type": "application/json",
      "user-agent": "YanziClient-Web"
    },
    body: JSON.stringify(putPayload)
  });

  if (!putResp.ok) {
    const errText = await putResp.text();
    throw new HttpError(putResp.status || 502, "write_gitee_failed", `Failed to write Yanm state to Gitee (${putResp.status}): ${errText}`);
  }

  return {
    bytes: textEncoder.encode(bodyText).length
  };
}

async function readYanmStateFromGitLab(syncConfig) {
  const gitlab = syncConfig.settings.GitLab || syncConfig.settings.gitlab || {};
  const token = (syncConfig.secrets.GitLabToken || syncConfig.secrets.gitlabToken || "").trim();
  const projectPath = String(gitlab.projectPath || gitlab.ProjectPath || "").trim();
  const branch = String(gitlab.branch || gitlab.Branch || "main").trim();
  const pathPrefix = String(gitlab.pathPrefix || gitlab.PathPrefix || "").trim();
  let baseUrl = String(gitlab.baseUrl || gitlab.BaseUrl || "https://gitlab.com").trim();

  if (!token || !projectPath) {
    throw new HttpError(400, "gitlab_config_incomplete", "GitLab token or project path is incomplete");
  }

  if (!baseUrl.startsWith("http://") && !baseUrl.startsWith("https://")) {
    baseUrl = "https://" + baseUrl;
  }
  baseUrl = baseUrl.replace(/\/+$/, "");

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const projectPathEnc = encodeURIComponent(projectPath);
  const pathEnc = encodeURIComponent(path);
  const url = `${baseUrl}/api/v4/projects/${projectPathEnc}/repository/files/${pathEnc}?ref=${branch}`;

  const response = await fetch(url, {
    method: "GET",
    headers: {
      "PRIVATE-TOKEN": token,
      "user-agent": "YanziClient-Web"
    }
  });

  if (response.status === 404) {
    throw new HttpError(404, "yanm_state_missing", "Yanm state was not found in GitLab");
  }

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "read_gitlab_failed", `Failed to read Yanm state from GitLab (${response.status}): ${errText}`);
  }

  const data = await response.json();
  const contentBase64 = data.content.replace(/\s/g, "");
  const text = base64ToUtf8(contentBase64);
  let snapshot = JSON.parse(text);

  return {
    updatedAtUtc: snapshot.updatedAtUtc || snapshot.UpdatedAtUtc || "",
    yanm: snapshot.yanm || snapshot.Yanm || null,
    bytes: textEncoder.encode(text).length
  };
}

async function writeYanmStateToGitLab(syncConfig, snapshot) {
  const gitlab = syncConfig.settings.GitLab || syncConfig.settings.gitlab || {};
  const token = (syncConfig.secrets.GitLabToken || syncConfig.secrets.gitlabToken || "").trim();
  const projectPath = String(gitlab.projectPath || gitlab.ProjectPath || "").trim();
  const branch = String(gitlab.branch || gitlab.Branch || "main").trim();
  const pathPrefix = String(gitlab.pathPrefix || gitlab.PathPrefix || "").trim();
  let baseUrl = String(gitlab.baseUrl || gitlab.BaseUrl || "https://gitlab.com").trim();

  if (!token || !projectPath) {
    throw new HttpError(400, "gitlab_config_incomplete", "GitLab token or project path is incomplete");
  }

  if (!baseUrl.startsWith("http://") && !baseUrl.startsWith("https://")) {
    baseUrl = "https://" + baseUrl;
  }
  baseUrl = baseUrl.replace(/\/+$/, "");

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const projectPathEnc = encodeURIComponent(projectPath);
  const pathEnc = encodeURIComponent(path);
  const url = `${baseUrl}/api/v4/projects/${projectPathEnc}/repository/files/${pathEnc}`;

  let exists = false;
  try {
    const checkResp = await fetch(`${url}?ref=${branch}`, {
      method: "GET",
      headers: {
        "PRIVATE-TOKEN": token,
        "user-agent": "YanziClient-Web"
      }
    });
    if (checkResp.ok) {
      exists = true;
    }
  } catch (e) {
    console.warn("Failed to check file existence in GitLab", e);
  }

  const bodyObj = {
    updatedAtUtc: snapshot.updatedAtUtc,
    yanm: snapshot.yanm
  };
  const bodyText = JSON.stringify(bodyObj, null, 2);
  const base64Content = utf8ToBase64(bodyText);

  const payload = {
    branch,
    commit_message: "Update yanm-state.json from Web",
    content: base64Content,
    encoding: "base64"
  };

  const response = await fetch(url, {
    method: exists ? "PUT" : "POST",
    headers: {
      "content-type": "application/json",
      "PRIVATE-TOKEN": token,
      "user-agent": "YanziClient-Web"
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "write_gitlab_failed", `Failed to write Yanm state to GitLab (${response.status}): ${errText}`);
  }

  return {
    bytes: textEncoder.encode(bodyText).length
  };
}

async function readYanmStateFromGitea(syncConfig) {
  const gitea = syncConfig.settings.Gitea || syncConfig.settings.gitea || {};
  const token = (syncConfig.secrets.GiteaToken || syncConfig.secrets.giteaToken || "").trim();
  const repoRaw = String(gitea.repo || gitea.Repo || "yanzi-sync").trim();
  const branch = String(gitea.branch || gitea.Branch || "main").trim();
  const pathPrefix = String(gitea.pathPrefix || gitea.PathPrefix || "").trim();
  let baseUrl = String(gitea.baseUrl || gitea.BaseUrl || "https://gitea.com").trim();

  let owner = String(gitea.username || gitea.Username || "").trim();
  let repo = repoRaw;
  if (repoRaw.includes("/")) {
    const parts = repoRaw.split("/");
    owner = parts[0].trim();
    repo = parts[1].trim();
  }

  if (!token || !owner || !repo) {
    throw new HttpError(400, "gitea_config_incomplete", "Gitea token, owner, or repo is incomplete");
  }

  if (!baseUrl.startsWith("http://") && !baseUrl.startsWith("https://")) {
    baseUrl = "https://" + baseUrl;
  }
  baseUrl = baseUrl.replace(/\/+$/, "");

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const url = `${baseUrl}/api/v1/repos/${owner}/${repo}/contents/${path}?ref=${branch}`;

  const response = await fetch(url, {
    method: "GET",
    headers: {
      "accept": "application/json",
      "authorization": `token ${token}`,
      "user-agent": "YanziClient-Web"
    }
  });

  if (response.status === 404) {
    throw new HttpError(404, "yanm_state_missing", "Yanm state was not found in Gitea");
  }

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "read_gitea_failed", `Failed to read Yanm state from Gitea (${response.status}): ${errText}`);
  }

  const data = await response.json();
  const contentBase64 = data.content.replace(/\s/g, "");
  const text = base64ToUtf8(contentBase64);
  let snapshot = JSON.parse(text);

  return {
    updatedAtUtc: snapshot.updatedAtUtc || snapshot.UpdatedAtUtc || "",
    yanm: snapshot.yanm || snapshot.Yanm || null,
    bytes: textEncoder.encode(text).length
  };
}

async function writeYanmStateToGitea(syncConfig, snapshot) {
  const gitea = syncConfig.settings.Gitea || syncConfig.settings.gitea || {};
  const token = (syncConfig.secrets.GiteaToken || syncConfig.secrets.giteaToken || "").trim();
  const repoRaw = String(gitea.repo || gitea.Repo || "yanzi-sync").trim();
  const branch = String(gitea.branch || gitea.Branch || "main").trim();
  const pathPrefix = String(gitea.pathPrefix || gitea.PathPrefix || "").trim();
  let baseUrl = String(gitea.baseUrl || gitea.BaseUrl || "https://gitea.com").trim();

  let owner = String(gitea.username || gitea.Username || "").trim();
  let repo = repoRaw;
  if (repoRaw.includes("/")) {
    const parts = repoRaw.split("/");
    owner = parts[0].trim();
    repo = parts[1].trim();
  }

  if (!token || !owner || !repo) {
    throw new HttpError(400, "gitea_config_incomplete", "Gitea token, owner, or repo is incomplete");
  }

  if (!baseUrl.startsWith("http://") && !baseUrl.startsWith("https://")) {
    baseUrl = "https://" + baseUrl;
  }
  baseUrl = baseUrl.replace(/\/+$/, "");

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
  const url = `${baseUrl}/api/v1/repos/${owner}/${repo}/contents/${path}`;

  let existingSha = null;
  try {
    const getResp = await fetch(`${url}?ref=${branch}`, {
      method: "GET",
      headers: {
        "accept": "application/json",
        "authorization": `token ${token}`,
        "user-agent": "YanziClient-Web"
      }
    });
    if (getResp.ok) {
      const getData = await getResp.json();
      existingSha = getData.sha;
    }
  } catch (e) {
    console.warn("Failed to fetch Gitea sha", e);
  }

  const bodyObj = {
    updatedAtUtc: snapshot.updatedAtUtc,
    yanm: snapshot.yanm
  };
  const bodyText = JSON.stringify(bodyObj, null, 2);
  const base64Content = utf8ToBase64(bodyText);

  const putPayload = {
    message: "Update yanm-state.json from Web",
    content: base64Content,
    branch
  };
  if (existingSha) {
    putPayload.sha = existingSha;
  }

  const putResp = await fetch(url, {
    method: "PUT",
    headers: {
      "accept": "application/json",
      "content-type": "application/json",
      "authorization": `token ${token}`,
      "user-agent": "YanziClient-Web"
    },
    body: JSON.stringify(putPayload)
  });

  if (!putResp.ok) {
    const errText = await putResp.text();
    throw new HttpError(putResp.status || 502, "write_gitea_failed", `Failed to write Yanm state to Gitea (${putResp.status}): ${errText}`);
  }

  return {
    bytes: textEncoder.encode(bodyText).length
  };
}

async function readYanmStateFromS3(syncConfig) {
  const s3 = syncConfig.settings.S3 || syncConfig.settings.s3 || {};
  const accessKeyId = (syncConfig.secrets.AccessKeyId || syncConfig.secrets.accessKeyId || "").trim();
  const secretAccessKey = (syncConfig.secrets.S3SecretAccessKey || syncConfig.secrets.s3SecretAccessKey || "").trim();
  const bucket = String(s3.bucket || s3.Bucket || "").trim();
  const region = String(s3.region || s3.Region || "us-east-1").trim();
  const pathPrefix = String(s3.pathPrefix || s3.PathPrefix || "").trim();
  let endpoint = String(s3.endpoint || s3.Endpoint || "").trim();

  if (!accessKeyId || !secretAccessKey || !bucket) {
    throw new HttpError(400, "s3_config_incomplete", "S3 credentials or bucket is incomplete");
  }

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");

  let urlStr;
  if (endpoint) {
    if (!endpoint.startsWith("http://") && !endpoint.startsWith("https://")) {
      endpoint = "https://" + endpoint;
    }
    const epUrl = new URL(endpoint);
    urlStr = `${epUrl.protocol}//${bucket}.${epUrl.host}/${path}`;
  } else {
    urlStr = `https://${bucket}.s3.${region}.amazonaws.com/${path}`;
  }

  const unsignedHeaders = {
    "accept": "application/json"
  };

  const signedHeaders = await awsV4Sign("GET", urlStr, unsignedHeaders, "", accessKeyId, secretAccessKey, region, "s3");

  const response = await fetch(urlStr, {
    method: "GET",
    headers: signedHeaders
  });

  if (response.status === 404) {
    throw new HttpError(404, "yanm_state_missing", "Yanm state was not found in S3 bucket");
  }

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "read_s3_failed", `Failed to read Yanm state from S3 (${response.status}): ${errText}`);
  }

  const text = await response.text();
  let snapshot;
  try {
    snapshot = JSON.parse(text);
  } catch {
    throw new HttpError(502, "yanm_state_invalid", "Yanm state JSON in S3 is invalid");
  }

  return {
    updatedAtUtc: snapshot.updatedAtUtc || snapshot.UpdatedAtUtc || "",
    yanm: snapshot.yanm || snapshot.Yanm || null,
    bytes: textEncoder.encode(text).length
  };
}

async function writeYanmStateToS3(syncConfig, snapshot) {
  const s3 = syncConfig.settings.S3 || syncConfig.settings.s3 || {};
  const accessKeyId = (syncConfig.secrets.AccessKeyId || syncConfig.secrets.accessKeyId || "").trim();
  const secretAccessKey = (syncConfig.secrets.S3SecretAccessKey || syncConfig.secrets.s3SecretAccessKey || "").trim();
  const bucket = String(s3.bucket || s3.Bucket || "").trim();
  const region = String(s3.region || s3.Region || "us-east-1").trim();
  const pathPrefix = String(s3.pathPrefix || s3.PathPrefix || "").trim();
  let endpoint = String(s3.endpoint || s3.Endpoint || "").trim();

  if (!accessKeyId || !secretAccessKey || !bucket) {
    throw new HttpError(400, "s3_config_incomplete", "S3 credentials or bucket is incomplete");
  }

  const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");

  let urlStr;
  if (endpoint) {
    if (!endpoint.startsWith("http://") && !endpoint.startsWith("https://")) {
      endpoint = "https://" + endpoint;
    }
    const epUrl = new URL(endpoint);
    urlStr = `${epUrl.protocol}//${bucket}.${epUrl.host}/${path}`;
  } else {
    urlStr = `https://${bucket}.s3.${region}.amazonaws.com/${path}`;
  }

  const bodyObj = {
    updatedAtUtc: snapshot.updatedAtUtc,
    yanm: snapshot.yanm
  };
  const bodyText = JSON.stringify(bodyObj, null, 2);

  const unsignedHeaders = {
    "content-type": "application/json; charset=utf-8"
  };

  const signedHeaders = await awsV4Sign("PUT", urlStr, unsignedHeaders, bodyText, accessKeyId, secretAccessKey, region, "s3");

  const response = await fetch(urlStr, {
    method: "PUT",
    headers: signedHeaders,
    body: bodyText
  });

  if (!response.ok) {
    const errText = await response.text();
    throw new HttpError(response.status || 502, "write_s3_failed", `Failed to write Yanm state to S3 (${response.status}): ${errText}`);
  }

  return {
    bytes: textEncoder.encode(bodyText).length
  };
}

async function writeYanmStateForUser(env, userId, snapshot) {
  let currentSnapshot = null;
  try {
    currentSnapshot = await readYanmStateForUser(env, userId);
    if (currentSnapshot?.yanm && areJsonValuesEquivalent(currentSnapshot.yanm, snapshot.yanm)) {
      return {
        source: currentSnapshot.source || "cloud-config",
        updatedAtUtc: currentSnapshot.updatedAtUtc || null,
        changed: false,
        bytes: currentSnapshot.bytes || textEncoder.encode(JSON.stringify(currentSnapshot.yanm)).length
      };
    }
  } catch (error) {
    if (!(error instanceof HttpError) || (error.status !== 404 && error.status !== 409)) {
      console.warn("Yanm sync unchanged check failed", {
        userId,
        error: error?.message || String(error)
      });
    }
  }

  let isSyncWritten = false;
  let syncProvider = "cloud-config";
  let syncError = null;

  try {
    const syncConfig = await getUserPersonalSyncConfig(env, userId);
    if (!hasPersonalSyncCredential(syncConfig)) {
      throw new HttpError(409, "sync_credentials_device_local", "Personal sync credentials are stored on the desktop device");
    }
    syncProvider = syncConfig.provider;
    if (syncConfig.provider === "github") {
      await writeYanmStateToGitHub(syncConfig, snapshot);
      isSyncWritten = true;
    } else if (syncConfig.provider === "gitee") {
      await writeYanmStateToGitee(syncConfig, snapshot);
      isSyncWritten = true;
    } else if (syncConfig.provider === "gitlab") {
      await writeYanmStateToGitLab(syncConfig, snapshot);
      isSyncWritten = true;
    } else if (syncConfig.provider === "gitea") {
      await writeYanmStateToGitea(syncConfig, snapshot);
      isSyncWritten = true;
    } else if (syncConfig.provider === "s3") {
      await writeYanmStateToS3(syncConfig, snapshot);
      isSyncWritten = true;
    } else if (syncConfig.provider === "webdav") {
      const config = buildLegacyWebDavConfig(syncConfig);
      await writeYanmStateToWebDav(config, snapshot);
      isSyncWritten = true;
    } else {
      throw new HttpError(400, "unsupported_provider", `Provider ${syncConfig.provider} is not supported for web sync`);
    }
  } catch (error) {
    syncError = error;
  }

  const dbResult = await writeYanmStateToCloudConfig(env, userId, snapshot);

  if (syncError) {
    if (syncError instanceof HttpError && (syncError.status === 404 || syncError.status === 409)) {
      // 忽略
    } else {
      throw syncError;
    }
  }

  return {
    source: isSyncWritten ? syncProvider : "cloud-config",
    updatedAtUtc: snapshot.updatedAtUtc,
    changed: true,
    bytes: dbResult.bytes
  };
}

function areJsonValuesEquivalent(left, right) {
  return stableJsonStringify(left) === stableJsonStringify(right);
}

function stableJsonStringify(value) {
  if (value === null || typeof value !== "object") {
    return JSON.stringify(value);
  }

  if (Array.isArray(value)) {
    return `[${value.map(stableJsonStringify).join(",")}]`;
  }

  const keys = Object.keys(value).sort();
  return `{${keys.map((key) => `${JSON.stringify(key)}:${stableJsonStringify(value[key])}`).join(",")}}`;
}

function scrubAiSecretsFromValue(value) {
  if (Array.isArray(value)) {
    return value.map(scrubAiSecretsFromValue);
  }
  if (value === null || typeof value !== "object") {
    return value;
  }

  const sanitized = {};
  for (const [key, child] of Object.entries(value)) {
    const normalizedKey = key.toLowerCase();
    sanitized[key] = normalizedKey === "apikey" || normalizedKey === "aiapikey"
      ? ""
      : scrubAiSecretsFromValue(child);
  }
  return sanitized;
}

function scrubPersonalSyncSecretsFromValue(value) {
  const sanitized = value && typeof value === "object" && !Array.isArray(value)
    ? JSON.parse(JSON.stringify(value))
    : {};
  sanitized.secrets = {};
  for (const key of [
    "webDavPassword",
    "password",
    "token",
    "githubToken",
    "giteeToken",
    "gitLabToken",
    "giteaToken",
    "s3SecretAccessKey"
  ]) {
    if (Object.prototype.hasOwnProperty.call(sanitized, key)) {
      sanitized[key] = "";
    }
  }
  return sanitized;
}

function normalizeYanmComponentStatePatch(payload) {
  const patch = {};
  const source = payload && typeof payload.componentState === "object" && !Array.isArray(payload.componentState)
    ? payload.componentState
    : null;

  if (source) {
    for (const [key, value] of Object.entries(source)) {
      const normalizedKey = String(key || "").trim();
      if (normalizedKey) {
        patch[normalizedKey] = value == null ? "" : String(value);
      }
    }
  }

  const explicitKey = String(payload?.stateKey || payload?.key || "").trim();
  if (explicitKey) {
    patch[explicitKey] = payload.value == null ? "" : String(payload.value);
  }

  if (Object.keys(patch).length === 0) {
    throw new HttpError(400, "invalid_component_state", "componentState or stateKey/value is required");
  }

  return patch;
}

async function patchYanmComponentStateForUser(env, userId, componentStatePatch, updatedAtUtc) {
  const current = await readYanmStateForUser(env, userId);
  if (!current?.yanm || typeof current.yanm !== "object") {
    throw new HttpError(404, "yanm_state_missing", "Yanm state was not found before componentState patch");
  }

  const yanm = JSON.parse(JSON.stringify(current.yanm));
  const stateKey = yanm.componentState && typeof yanm.componentState === "object" && !Array.isArray(yanm.componentState)
    ? "componentState"
    : (yanm.ComponentState && typeof yanm.ComponentState === "object" && !Array.isArray(yanm.ComponentState) ? "ComponentState" : "componentState");
  const state = {
    ...(yanm[stateKey] && typeof yanm[stateKey] === "object" && !Array.isArray(yanm[stateKey]) ? yanm[stateKey] : {})
  };

  const changedKeys = [];
  for (const [key, value] of Object.entries(componentStatePatch)) {
    const hasExisting = Object.prototype.hasOwnProperty.call(state, key);
    const existingValue = hasExisting ? String(state[key] ?? "") : null;
    if (!hasExisting || existingValue !== value) {
      changedKeys.push(key);
    }

    state[key] = value;
  }

  if (changedKeys.length === 0) {
    return {
      source: current.source || "cloud-config",
      updatedAtUtc: current.updatedAtUtc || null,
      changed: false,
      changedKeys: [],
      bytes: current.bytes || textEncoder.encode(JSON.stringify(current.yanm)).length
    };
  }

  yanm[stateKey] = state;

  const effectiveUpdatedAtUtc = updatedAtUtc || isoNow();
  const result = await writeYanmStateForUser(env, userId, {
    updatedAtUtc: effectiveUpdatedAtUtc,
    yanm
  });
  return {
    ...result,
    updatedAtUtc: effectiveUpdatedAtUtc,
    changed: true,
    changedKeys
  };
}

async function ensureSystemConfigExtension(env, extensionId, metadata) {
  const now = isoNow();
  await env.DB.prepare(
    `insert into extensions (
      extension_id,
      display_name,
      latest_version,
      manifest_json,
      updated_at
    ) values (?, ?, ?, ?, ?)
    on conflict(extension_id) do update set
      display_name = coalesce(extensions.display_name, excluded.display_name),
      latest_version = coalesce(extensions.latest_version, excluded.latest_version),
      updated_at = excluded.updated_at`
  )
    .bind(
      extensionId,
      metadata.displayName,
      "1",
      JSON.stringify({
        name: extensionId,
        displayName: metadata.displayName,
        version: "1",
        category: "系统配置",
        description: metadata.description,
        keywords: ["yanzi", "settings", "yanm"]
      }),
      now
    )
    .run();
}

async function readYanmStateFromWebDav(config) {
  const target = buildWebDavTargetInfo(config, "state/yanm-state.json");
  const response = await fetchWebDav(config, "state/yanm-state.json", {
    method: "GET"
  });

  if (response.status === 404) {
    throw new WebDavHttpError(
      404,
      "yanm_state_missing",
      "Yanm state was not found in WebDAV",
      {
        method: "GET",
        target,
        upstreamStatus: response.status,
        upstreamStatusText: response.statusText,
        upstreamHeaders: readSafeResponseHeaders(response)
      }
    );
  }

  if (!response.ok) {
    await throwWebDavError(response, "read_yanm_failed", "Failed to read Yanm state from WebDAV", {
      method: "GET",
      target
    });
  }

  const text = await response.text();
  let snapshot;
  try {
    snapshot = JSON.parse(text);
  } catch {
    throw new HttpError(502, "yanm_state_invalid", "Yanm state JSON in WebDAV is invalid");
  }

  return {
    updatedAtUtc: snapshot.updatedAtUtc || snapshot.UpdatedAtUtc || "",
    yanm: snapshot.yanm || snapshot.Yanm || null,
    bytes: textEncoder.encode(text).length
  };
}

async function writeYanmStateToWebDav(config, snapshot) {
  const body = JSON.stringify({
    updatedAtUtc: snapshot.updatedAtUtc,
    yanm: snapshot.yanm
  }, null, 2);

  await ensureWebDavCollection(config, "");
  await ensureWebDavCollection(config, "state");

  const response = await fetchWebDav(config, "state/yanm-state.json", {
    method: "PUT",
    body,
    headers: {
      "content-type": "application/json; charset=utf-8"
    }
  });

  if (!response.ok) {
    await throwWebDavError(response, "write_yanm_failed", "Failed to write Yanm state to WebDAV");
  }

  return {
    bytes: textEncoder.encode(body).length
  };
}

async function ensureWebDavCollection(config, relativePath) {
  const response = await fetchWebDav(config, relativePath, {
    method: "MKCOL"
  });

  if (response.ok || response.status === 405) {
    return;
  }

  if (response.status === 409 && relativePath) {
    await ensureWebDavCollection(config, "");
    return ensureWebDavCollection(config, relativePath);
  }

  await throwWebDavError(response, "webdav_mkcol_failed", "Failed to ensure WebDAV folder");
}

async function fetchWebDav(config, relativePath, init) {
  const url = buildWebDavUrl(config, relativePath);
  const headers = new Headers(init.headers || {});
  headers.set("authorization", `Basic ${base64EncodeUtf8(`${config.username}:${config.password}`)}`);
  headers.set("user-agent", "YanziClient/1.0.0 (Windows; Cloudflare Worker)");
  return fetch(url, {
    ...init,
    headers
  });
}

function buildWebDavUrl(config, relativePath) {
  const base = new URL(ensureTrailingSlash(config.serverUrl));
  const segments = [
    ...normalizeRootPath(config.rootPath).split("/").filter(Boolean),
    ...normalizeRelativePath(relativePath).split("/").filter(Boolean)
  ];
  base.pathname = `${base.pathname.replace(/\/+$/, "")}/${segments.map(encodeURIComponent).join("/")}`;
  return base.toString();
}

async function throwWebDavError(response, code, fallbackMessage, context = {}) {
  const detail = await response.text().catch(() => "");
  const suffix = detail ? `: ${trimForMessage(detail)}` : "";
  throw new WebDavHttpError(
    response.status || 502,
    code,
    `${fallbackMessage} (${response.status})${suffix}`,
    {
      ...context,
      upstreamStatus: response.status,
      upstreamStatusText: response.statusText,
      upstreamHeaders: readSafeResponseHeaders(response),
      upstreamBodySnippet: trimForMessage(detail)
    }
  );
}

function readFirst(source, keys) {
  for (const key of keys) {
    if (source && Object.prototype.hasOwnProperty.call(source, key)) {
      return source[key];
    }
  }

  return undefined;
}

function normalizeRootPath(rootPath) {
  const value = String(rootPath || "/yanzi").trim().replace(/\\/g, "/");
  return value.replace(/^\/+/, "").replace(/\/+$/, "") || "yanzi";
}

function normalizeRelativePath(path) {
  return String(path || "").replace(/\\/g, "/").replace(/^\/+/, "").replace(/\/+$/, "");
}

function ensureTrailingSlash(value) {
  return String(value || "").endsWith("/") ? String(value) : `${value}/`;
}

function buildWebDavTargetInfo(config, relativePath) {
  const url = new URL(buildWebDavUrl(config, relativePath));
  return {
    origin: url.origin,
    path: url.pathname,
    rootPath: normalizeRootPath(config.rootPath),
    relativePath: normalizeRelativePath(relativePath)
  };
}

function readSafeResponseHeaders(response) {
  const names = [
    "cf-ray",
    "cf-cache-status",
    "server",
    "date",
    "content-type",
    "www-authenticate"
  ];
  const headers = {};
  for (const name of names) {
    const value = response.headers.get(name);
    if (value) {
      headers[name] = value;
    }
  }

  return headers;
}

function buildSafeErrorDiagnostics(error) {
  if (error instanceof WebDavHttpError) {
    return {
      code: error.code,
      status: error.status,
      message: error.message,
      ...error.diagnostics
    };
  }

  if (error instanceof HttpError) {
    return {
      code: error.code,
      status: error.status,
      message: error.message
    };
  }

  return {
    code: "webdav_unknown_error",
    status: 500,
    message: error instanceof Error ? error.message : "Unknown WebDAV error"
  };
}

function base64EncodeUtf8(value) {
  const bytes = textEncoder.encode(value);
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary);
}

function normalizeOptionalIsoDate(value) {
  if (!value) {
    return null;
  }

  const date = new Date(String(value));
  if (Number.isNaN(date.getTime())) {
    throw new HttpError(400, "invalid_updated_at", "updatedAtUtc must be a valid datetime");
  }

  return date.toISOString();
}

function trimForMessage(value) {
  const text = String(value || "").replace(/\s+/g, " ").trim();
  return text.length > 240 ? `${text.slice(0, 240)}...` : text;
}

async function readJson(request) {
  const body = await request.json();
  if (!body || typeof body !== "object") {
    throw new HttpError(400, "invalid_json", "Request body must be a JSON object");
  }

  return body;
}

function json(data, status = 200) {
  return withCors(
    new Response(JSON.stringify(data, null, 2), {
      status,
      headers: {
        "content-type": "application/json; charset=utf-8"
      }
    })
  );
}

function withCors(response) {
  response.headers.set("access-control-allow-origin", "*");
  response.headers.set("access-control-allow-methods", "GET,POST,PUT,DELETE,OPTIONS");
  response.headers.set(
    "access-control-allow-headers",
    "content-type,authorization,accept,origin,x-yanzi-client,x-yanzi-client-version,x-api-version,x-client-version"
  );
  return response;
}

function isoNow() {
  return new Date().toISOString();
}

function randomHex(bytes) {
  const values = crypto.getRandomValues(new Uint8Array(bytes));
  return bytesToHex(values);
}

async function digestHex(buffer) {
  const hash = await crypto.subtle.digest("SHA-256", buffer);
  return bytesToHex(new Uint8Array(hash));
}

function bytesToHex(bytes) {
  return [...bytes].map((value) => value.toString(16).padStart(2, "0")).join("");
}

function base64UrlEncode(value) {
  return base64UrlEncodeBytes(textEncoder.encode(value));
}

function base64UrlEncodeBytes(bytes) {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function base64UrlDecode(value) {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
  return atob(padded);
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function generateVerificationCode() {
  return String(Math.floor(100000 + Math.random() * 900000));
}

const textEncoder = new TextEncoder();

class HttpError extends Error {
  constructor(status, code, message) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

class WebDavHttpError extends HttpError {
  constructor(status, code, message, diagnostics) {
    super(status, code, message);
    this.diagnostics = diagnostics;
  }
}

async function getYanmStateViewUrl(env, userId) {
  try {
    const syncConfig = await getUserPersonalSyncConfig(env, userId);
    const provider = syncConfig.provider;
    if (provider === "github") {
      const { owner, repo, branch, pathPrefix } = await resolveGitHubRepoTarget(syncConfig);
      const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
      return `https://github.com/${owner}/${repo}/blob/${branch}/${path}`;
    }
    if (provider === "gitee") {
      const gitee = syncConfig.settings.Gitee || syncConfig.settings.gitee || {};
      const repoRaw = String(gitee.repo || gitee.Repo || "yanzi-sync").trim();
      const branch = String(gitee.branch || gitee.Branch || "master").trim();
      const pathPrefix = String(gitee.pathPrefix || gitee.PathPrefix || "").trim();
      let owner = String(gitee.username || gitee.Username || "").trim();
      let repo = repoRaw;
      if (repoRaw.includes("/")) {
        const parts = repoRaw.split("/");
        owner = parts[0].trim();
        repo = parts[1].trim();
      }
      const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
      return `https://gitee.com/${owner}/${repo}/blob/${branch}/${path}`;
    }
    if (provider === "gitlab") {
      const gitlab = syncConfig.settings.GitLab || syncConfig.settings.gitlab || {};
      const projectPath = String(gitlab.projectPath || gitlab.ProjectPath || "").trim();
      const branch = String(gitlab.branch || gitlab.Branch || "main").trim();
      const pathPrefix = String(gitlab.pathPrefix || gitlab.PathPrefix || "").trim();
      let baseUrl = String(gitlab.baseUrl || gitlab.BaseUrl || "https://gitlab.com").trim();
      while (baseUrl.endsWith("/")) {
        baseUrl = baseUrl.substring(0, baseUrl.length - 1);
      }
      const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
      return `${baseUrl}/${projectPath}/-/blob/${branch}/${path}`;
    }
    if (provider === "gitea") {
      const gitea = syncConfig.settings.Gitea || syncConfig.settings.gitea || {};
      const repoRaw = String(gitea.repo || gitea.Repo || "yanzi-sync").trim();
      const branch = String(gitea.branch || gitea.Branch || "main").trim();
      const pathPrefix = String(gitea.pathPrefix || gitea.PathPrefix || "").trim();
      let baseUrl = String(gitea.baseUrl || gitea.BaseUrl || "https://gitea.com").trim();
      while (baseUrl.endsWith("/")) {
        baseUrl = baseUrl.substring(0, baseUrl.length - 1);
      }
      let owner = String(gitea.username || gitea.Username || "").trim();
      let repo = repoRaw;
      if (repoRaw.includes("/")) {
        const parts = repoRaw.split("/");
        owner = parts[0].trim();
        repo = parts[1].trim();
      }
      const path = [pathPrefix, "state/yanm-state.json"].filter(Boolean).join("/").replace(/\/+/g, "/").replace(/^\//, "");
      return `${baseUrl}/${owner}/${repo}/src/branch/${branch}/${path}`;
    }
    if (provider === "webdav") {
      const webdav = syncConfig.settings.WebDAV || syncConfig.settings.webdav || {};
      let serverUrl = String(webdav.serverUrl || webdav.ServerUrl || "").trim();
      if (serverUrl) {
        return serverUrl;
      }
    }
  } catch (e) {
    // ignore
  }
  return null;
}
