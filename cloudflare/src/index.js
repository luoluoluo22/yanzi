const TOKEN_TTL_SECONDS = 60 * 60 * 12;
const PASSWORD_ITERATIONS = 100000;
const VERIFICATION_CODE_TTL_MINUTES = 10;

export default {
  async fetch(request, env) {
    try {
      return await handleRequest(request, env);
    } catch (error) {
      if (error instanceof HttpError) {
        return json(
          {
            error: error.code,
            message: error.message
          },
          error.status
        );
      }

      return json(
        {
          error: "internal_error",
          message: error instanceof Error ? error.message : "Unknown error"
        },
        500
      );
    }
  }
};

async function handleRequest(request, env) {
  const url = new URL(request.url);

  if (request.method === "OPTIONS") {
    return withCors(new Response(null, { status: 204 }));
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

    if (String(verification.username).toLowerCase() !== username) {
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

    const passwordHash = await hashPassword(
      password,
      user.password_salt,
      Number(user.password_iterations || PASSWORD_ITERATIONS)
    );

    if (passwordHash !== user.password_hash) {
      throw new HttpError(401, "invalid_credentials", "Invalid email or password");
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
      email: auth.email
    });
  }

  if (url.pathname === "/v1/extensions" && request.method === "GET") {
    const rows = await env.DB.prepare(
      `select
        extension_id,
        display_name,
        latest_version,
        manifest_json,
        archive_key,
        archive_sha256,
        updated_at
      from extensions
      order by updated_at desc`
    ).all();

    return json({ items: rows.results ?? [] });
  }

  const extensionMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)$/);
  if (extensionMatch && request.method === "PUT") {
    await requireAuth(request, env);
    const extensionId = decodeURIComponent(extensionMatch[1]);
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
        updated_at
      ) values (?, ?, ?, ?, ?)
      on conflict(extension_id) do update set
        display_name = excluded.display_name,
        latest_version = excluded.latest_version,
        manifest_json = excluded.manifest_json,
        updated_at = excluded.updated_at`
    )
      .bind(
        extensionId,
        displayName,
        latestVersion,
        JSON.stringify(manifest),
        now
      )
      .run();

    return json({
      ok: true,
      extensionId,
      latestVersion
    });
  }

  const archiveUploadMatch = url.pathname.match(/^\/v1\/extensions\/([^/]+)\/archive$/);
  if (archiveUploadMatch && request.method === "PUT") {
    await requireAuth(request, env);
    const extensionId = decodeURIComponent(archiveUploadMatch[1]);
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
        updated_at
      ) values (?, ?, ?, ?, ?, ?)
      on conflict(extension_id) do update set
        latest_version = excluded.latest_version,
        archive_key = excluded.archive_key,
        archive_sha256 = excluded.archive_sha256,
        updated_at = excluded.updated_at`
    )
      .bind(extensionId, extensionId, version, archiveKey, sha256, isoNow())
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
      `select archive_key, latest_version, archive_sha256
       from extensions
       where extension_id = ?`
    )
      .bind(extensionId)
      .first();

    if (!row?.archive_key) {
      return json({ error: "not_found", message: "Archive not found" }, 404);
    }

    const object = await env.PACKAGES.get(row.archive_key);
    if (!object) {
      return json({ error: "not_found", message: "Stored package is missing" }, 404);
    }

    const headers = new Headers();
    object.writeHttpMetadata(headers);
    headers.set("etag", row.archive_sha256 || "");
    headers.set(
      "content-disposition",
      `attachment; filename="${extensionId}-${row.latest_version || "latest"}.zip"`
    );
    return withCors(new Response(object.body, { headers }));
  }

  if (url.pathname === "/v1/me/extensions" && request.method === "GET") {
    const auth = await requireAuth(request, env);
    await ensureUser(env, auth.userId);

    const rows = await env.DB.prepare(
      `select
        user_id,
        extension_id,
        installed_version,
        enabled,
        settings_json,
        updated_at
      from user_extensions
      where user_id = ?
      order by updated_at desc`
    )
      .bind(auth.userId)
      .all();

    return json({
      userId: auth.userId,
      items: rows.results ?? []
    });
  }

  const myExtensionMatch = url.pathname.match(/^\/v1\/me\/extensions\/([^/]+)$/);
  if (myExtensionMatch && request.method === "PUT") {
    const auth = await requireAuth(request, env);
    const extensionId = decodeURIComponent(myExtensionMatch[1]);
    const payload = await readJson(request);
    await ensureUser(env, auth.userId);

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
        JSON.stringify(payload.settings ?? {}),
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
  const prefix = "Bearer ";
  if (!header.startsWith(prefix)) {
    throw new HttpError(401, "unauthorized", "Missing bearer token");
  }

  const token = header.slice(prefix.length).trim();
  const payload = await verifyToken(env, token);
  if (!payload?.sub || !payload?.username) {
    throw new HttpError(401, "unauthorized", "Invalid token payload");
  }

  return {
    userId: String(payload.sub),
    username: String(payload.username),
    email: payload.email ? String(payload.email) : null
  };
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
    throw new HttpError(401, "unauthorized", "Invalid token signature");
  }

  const payload = JSON.parse(base64UrlDecode(encodedPayload));
  const now = Math.floor(Date.now() / 1000);
  if (!payload.exp || Number(payload.exp) <= now) {
    throw new HttpError(401, "token_expired", "Token expired");
  }

  return payload;
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
  const username = String(value || "").trim().toLowerCase();
  if (!/^[a-z0-9_\-]{3,32}$/.test(username)) {
    throw new HttpError(400, "invalid_username", "Username must be 3-32 chars: a-z, 0-9, _, -");
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

async function ensureUser(env, userId) {
  await env.DB.prepare(
    `insert into users (user_id, created_at, updated_at)
     values (?, ?, ?)
     on conflict(user_id) do update set updated_at = excluded.updated_at`
  )
    .bind(userId, isoNow(), isoNow())
    .run();
}

async function touchUser(env, userId) {
  await env.DB.prepare(
    "update users set updated_at = ? where user_id = ?"
  )
    .bind(isoNow(), userId)
    .run();
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
  response.headers.set("access-control-allow-headers", "content-type,authorization");
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
