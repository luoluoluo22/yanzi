#!/usr/bin/env node
const http = require("http");
const fs = require("fs");
const crypto = require("crypto");
const path = require("path");
const {createHandler: createYanziToolHandler} = require("./tool-broker.cjs");

const HOST = process.env.CHATGPT_GATEWAY_HOST || "127.0.0.1";
const PORT = Number(process.env.CHATGPT_GATEWAY_PORT || 8791);
const CREDENTIAL_FILE = process.env.CHATGPT_GATEWAY_CREDENTIAL_FILE || "/var/lib/chatgpt-gateway/credentials.json";
const TOKEN_FILE = process.env.CHATGPT_GATEWAY_TOKEN_FILE || "/etc/chatgpt-gateway/token";
const AUTH_ISSUER = "https://auth.openai.com";
const TOKEN_ENDPOINT = "https://auth.openai.com/api/accounts/oauth/token";
const JWKS_URL = "https://auth.openai.com/.well-known/jwks.json";
const API_BASE = "https://api.openai.com/v1";
const RESOURCE = "https://api.openai.com/v1";
const REQUIRED_SCOPE = "chatgpt.tokens.use.direct";
const MAX_BODY = 512 * 1024;

let refreshPromise = null;
let jwksCache = null;
const yanziAuthCache = new Map();
const YANZI_AUTH_URL = process.env.YANZI_AUTH_URL || "https://sync.luoluoluo.cc.cd/v1/auth/me";

function readGatewayToken() {
  const token = fs.readFileSync(TOKEN_FILE, "utf8").trim();
  if (token.length < 32) throw new Error("gateway token missing or too short");
  return token;
}
const GATEWAY_TOKEN = readGatewayToken();

function b64urlJson(part) {
  const pad = "=".repeat((4 - (part.length % 4)) % 4);
  return JSON.parse(Buffer.from(part.replace(/-/g, "+").replace(/_/g, "/") + pad, "base64").toString("utf8"));
}

function decodeJwt(token) {
  const parts = String(token || "").split(".");
  if (parts.length !== 3) throw new Error("invalid JWT");
  return { header: b64urlJson(parts[0]), payload: b64urlJson(parts[1]), parts };
}

async function getJwks() {
  if (jwksCache && jwksCache.expires > Date.now()) return jwksCache.value;
  const res = await fetch(JWKS_URL, { headers: { accept: "application/json" } });
  if (!res.ok) throw new Error("JWKS fetch failed: " + res.status);
  const value = await res.json();
  if (!value || !Array.isArray(value.keys)) throw new Error("invalid JWKS response");
  jwksCache = { value, expires: Date.now() + 10 * 60 * 1000 };
  return value;
}

async function verifyIdToken(idToken, clientId, expectedSub) {
  const { header, payload, parts } = decodeJwt(idToken);
  if (header.alg !== "RS256" || !header.kid) throw new Error("unsupported ID token");
  const jwks = await getJwks();
  const jwk = jwks.keys.find(k => k.kid === header.kid);
  if (!jwk) {
    jwksCache = null;
    throw new Error("ID token signing key not found");
  }
  const key = crypto.createPublicKey({ key: jwk, format: "jwk" });
  const sigPart = parts[2];
  const sig = Buffer.from(sigPart.replace(/-/g, "+").replace(/_/g, "/") + "=".repeat((4 - sigPart.length % 4) % 4), "base64");
  const ok = crypto.verify("RSA-SHA256", Buffer.from(parts[0] + "." + parts[1]), key, sig);
  if (!ok) throw new Error("ID token signature invalid");
  if (payload.iss !== AUTH_ISSUER) throw new Error("ID token issuer invalid");
  const audOk = Array.isArray(payload.aud) ? payload.aud.includes(clientId) : payload.aud === clientId;
  if (!audOk) throw new Error("ID token audience invalid");
  if (!payload.exp || payload.exp <= Math.floor(Date.now() / 1000)) throw new Error("ID token expired");
  if (!payload.sub) throw new Error("ID token subject missing");
  if (expectedSub && payload.sub !== expectedSub) throw new Error("ID token account changed");
  return payload;
}

function loadCredentials() {
  if (!fs.existsSync(CREDENTIAL_FILE)) return null;
  const raw = JSON.parse(fs.readFileSync(CREDENTIAL_FILE, "utf8"));
  const required = ["client_id", "access_token", "refresh_token"];
  for (const key of required) if (!raw[key] || typeof raw[key] !== "string") throw new Error("credential file missing " + key);
  const scopes = String(raw.scope || "").split(/\s+/).filter(Boolean);
  if (!scopes.includes(REQUIRED_SCOPE)) throw new Error("credential lacks " + REQUIRED_SCOPE);
  return raw;
}

function saveCredentials(value) {
  const dir = path.dirname(CREDENTIAL_FILE);
  fs.mkdirSync(dir, { recursive: true });
  const tmp = CREDENTIAL_FILE + ".tmp-" + process.pid + "-" + Date.now();
  fs.writeFileSync(tmp, JSON.stringify(value, null, 2), { mode: 0o600 });
  fs.renameSync(tmp, CREDENTIAL_FILE);
  try { fs.chmodSync(CREDENTIAL_FILE, 0o600); } catch {}
}

async function refreshCredentials(current) {
  const body = new URLSearchParams({
    grant_type: "refresh_token",
    client_id: current.client_id,
    refresh_token: current.refresh_token,
    resource: RESOURCE
  });
  const res = await fetch(TOKEN_ENDPOINT, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded", accept: "application/json" },
    body
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error("token refresh failed: HTTP " + res.status);
  if (!data.access_token || !data.refresh_token || !data.expires_in) throw new Error("token refresh returned incomplete credentials");
  const scope = typeof data.scope === "string" && data.scope.trim() ? data.scope : current.scope;
  if (!String(scope).split(/\s+/).includes(REQUIRED_SCOPE)) throw new Error("refreshed token lost ChatGPT plan scope");

  let subject = current.subject || null;
  let idToken = current.id_token || null;
  if (data.id_token) {
    const payload = await verifyIdToken(data.id_token, current.client_id, subject || undefined);
    subject = payload.sub;
    idToken = data.id_token;
  }

  const next = {
    ...current,
    access_token: data.access_token,
    refresh_token: data.refresh_token,
    id_token: idToken,
    subject,
    scope,
    token_type: data.token_type || "Bearer",
    expires_in: data.expires_in,
    earliest_refresh_at: data.earliest_refresh_at ?? current.earliest_refresh_at ?? null,
    access_token_expires_at: Date.now() + Number(data.expires_in) * 1000,
    refreshed_at: new Date().toISOString()
  };
  saveCredentials(next);
  return next;
}

async function usableCredentials() {
  let current = loadCredentials();
  if (!current) throw Object.assign(new Error("ChatGPT credentials are not imported yet"), { statusCode: 503 });
  let exp = Number(current.access_token_expires_at || 0);
  if (!exp) {
    try {
      const payload = decodeJwt(current.access_token).payload;
      exp = Number(payload.exp || 0) * 1000;
    } catch {}
  }
  if (exp > Date.now() + 2 * 60 * 1000) return current;
  if (!refreshPromise) {
    refreshPromise = refreshCredentials(current).finally(() => { refreshPromise = null; });
  }
  return await refreshPromise;
}

function authorized(req) {
  const expected = "Bearer " + GATEWAY_TOKEN;
  const actual = req.headers.authorization || "";
  if (actual.length !== expected.length) return false;
  return crypto.timingSafeEqual(Buffer.from(actual), Buffer.from(expected));
}

function bearerToken(req) {
  const raw = String(req.headers.authorization || "");
  const match = raw.match(/^Bearer\s+(.+)$/i);
  return match ? match[1].trim() : "";
}

async function validateYanziToken(token) {
  if (!token || token.length < 16) throw Object.assign(new Error("yanzi login required"), { statusCode: 401 });
  const key = crypto.createHash("sha256").update(token).digest("hex");
  const cached = yanziAuthCache.get(key);
  if (cached && cached.expiresAt > Date.now()) return cached.user;
  const res = await fetch(YANZI_AUTH_URL, {
    headers: { authorization: "Bearer " + token, accept: "application/json" }
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok || !body?.userId) throw Object.assign(new Error("yanzi token invalid"), { statusCode: 401 });
  const user = { userId: body.userId, username: body.username || "" };
  yanziAuthCache.set(key, { user, expiresAt: Date.now() + 5 * 60 * 1000 });
  if (yanziAuthCache.size > 500) {
    for (const [k, v] of yanziAuthCache) if (v.expiresAt <= Date.now()) yanziAuthCache.delete(k);
  }
  return user;
}

function extractJsonPayload(text) {
  const raw = String(text || "").trim();
  const fenced = raw.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i);
  const candidate = fenced ? fenced[1].trim() : raw;
  return JSON.parse(candidate);
}

function sendJson(res, status, payload) {
  const body = Buffer.from(JSON.stringify(payload));
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "content-length": body.length,
    "cache-control": "no-store"
  });
  res.end(body);
}

async function readJson(req) {
  const length = Number(req.headers["content-length"] || 0);
  if (!Number.isFinite(length) || length <= 0 || length > MAX_BODY) throw Object.assign(new Error("invalid request size"), { statusCode: 413 });
  const chunks = [];
  let total = 0;
  for await (const chunk of req) {
    total += chunk.length;
    if (total > MAX_BODY) throw Object.assign(new Error("request too large"), { statusCode: 413 });
    chunks.push(chunk);
  }
  try { return JSON.parse(Buffer.concat(chunks).toString("utf8")); }
  catch { throw Object.assign(new Error("invalid JSON"), { statusCode: 400 }); }
}

async function upstreamModels(accessToken) {
  const res = await fetch(API_BASE + "/models", {
    headers: { authorization: "Bearer " + accessToken, accept: "application/json" }
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw Object.assign(new Error("OpenAI models request failed: HTTP " + res.status), { statusCode: 502 });
  const raw = Array.isArray(data.models) ? data.models : [];
  return raw.filter(m => m && m.visibility === "list" && typeof m.slug === "string")
    .map(m => ({ slug: m.slug, display_name: m.display_name || m.slug }));
}

function selectDefaultModel(models) {
  const preferred = ["gpt-6-astra", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"];
  for (const slug of preferred) if (models.some(m => m.slug === slug)) return slug;
  if (!models.length) throw new Error("No ChatGPT-plan models available");
  return models[0].slug;
}

async function responseText(accessToken, model, input, instructions, options = {}) {
  const payload = {
    model,
    input: typeof input === "string" ? [{ role: "user", content: input }] : input,
    store: false,
    stream: true
  };
  if (typeof instructions === "string" && instructions.trim()) payload.instructions = instructions;

  const verbosity = typeof options.verbosity === "string" ? options.verbosity.trim().toLowerCase() : "";
  if (["low", "medium", "high"].includes(verbosity)) {
    payload.text = { verbosity };
  }

  const reasoningEffort = typeof options.reasoningEffort === "string"
    ? options.reasoningEffort.trim().toLowerCase()
    : "";
  if (["none", "minimal", "low", "medium", "high", "xhigh", "max"].includes(reasoningEffort)) {
    payload.reasoning = { effort: reasoningEffort };
  }

  const res = await fetch(API_BASE + "/responses", {
    method: "POST",
    headers: {
      authorization: "Bearer " + accessToken,
      "content-type": "application/json",
      accept: "text/event-stream"
    },
    body: JSON.stringify(payload)
  });
  if (!res.ok) {
    const text = await res.text();
    throw Object.assign(new Error("OpenAI responses request failed: HTTP " + res.status + " " + text.slice(0, 1000)), { statusCode: 502 });
  }

  const decoder = new TextDecoder();
  let buffer = "";
  let output = "";
  let completed = false;
  let failure = null;

  for await (const chunk of res.body) {
    buffer += decoder.decode(chunk, { stream: true });
    for (;;) {
      const idx = buffer.indexOf("\n\n");
      if (idx < 0) break;
      const block = buffer.slice(0, idx);
      buffer = buffer.slice(idx + 2);
      for (const line of block.split(/\r?\n/)) {
        if (!line.startsWith("data:")) continue;
        const raw = line.slice(5).trim();
        if (!raw || raw === "[DONE]") continue;
        let evt;
        try { evt = JSON.parse(raw); } catch { continue; }
        if (evt.type === "response.output_text.delta" && typeof evt.delta === "string") output += evt.delta;
        if (evt.type === "response.completed") completed = true;
        if (evt.type === "response.failed") failure = evt.response?.error || { code: "response_failed" };
        if (evt.type === "response.incomplete") failure = evt.response?.incomplete_details || { code: "response_incomplete" };
      }
    }
  }

  if (failure) throw Object.assign(new Error("OpenAI response failed: " + JSON.stringify(failure)), { statusCode: 502 });
  if (!completed) throw Object.assign(new Error("OpenAI stream ended without response.completed"), { statusCode: 502 });
  return output;
}

async function streamChatCompletionCompatible(httpRes, accessToken, model, input, options = {}) {
  const payload = {
    model,
    input: typeof input === "string" ? [{ role: "user", content: input }] : input,
    store: false,
    stream: true
  };

  const verbosity = typeof options.verbosity === "string" ? options.verbosity.trim().toLowerCase() : "";
  if (["low", "medium", "high"].includes(verbosity)) {
    payload.text = { verbosity };
  }

  const reasoningEffort = typeof options.reasoningEffort === "string"
    ? options.reasoningEffort.trim().toLowerCase()
    : "";
  if (["none", "minimal", "low", "medium", "high", "xhigh", "max"].includes(reasoningEffort)) {
    payload.reasoning = { effort: reasoningEffort };
  }

  const upstream = await fetch(API_BASE + "/responses", {
    method: "POST",
    headers: {
      authorization: "Bearer " + accessToken,
      "content-type": "application/json",
      accept: "text/event-stream"
    },
    body: JSON.stringify(payload)
  });

  if (!upstream.ok) {
    const text = await upstream.text();
    throw Object.assign(new Error("OpenAI responses request failed: HTTP " + upstream.status + " " + text.slice(0, 1000)), { statusCode: 502 });
  }

  const id = "chatcmpl_yanzi_" + crypto.randomUUID().replace(/-/g, "");
  const created = Math.floor(Date.now() / 1000);

  httpRes.writeHead(200, {
    "content-type": "text/event-stream; charset=utf-8",
    "cache-control": "no-cache, no-store",
    connection: "keep-alive",
    "x-accel-buffering": "no"
  });
  httpRes.flushHeaders?.();

  const writeChunk = (delta, finishReason = null) => {
    httpRes.write("data: " + JSON.stringify({
      id,
      object: "chat.completion.chunk",
      created,
      model,
      choices: [{
        index: 0,
        delta,
        finish_reason: finishReason
      }]
    }) + "\n\n");
  };

  writeChunk({ role: "assistant" });

  const heartbeat = setInterval(() => {
    if (!httpRes.writableEnded) {
      httpRes.write(": keep-alive\n\n");
    }
  }, 10000);
  heartbeat.unref?.();

  const decoder = new TextDecoder();
  let buffer = "";
  let completed = false;
  let failure = null;

  try {
    for await (const chunk of upstream.body) {
      buffer += decoder.decode(chunk, { stream: true });

      for (;;) {
        const idx = buffer.indexOf("\n\n");
        if (idx < 0) break;

        const block = buffer.slice(0, idx);
        buffer = buffer.slice(idx + 2);

        for (const line of block.split(/\r?\n/)) {
          if (!line.startsWith("data:")) continue;
          const raw = line.slice(5).trim();
          if (!raw || raw === "[DONE]") continue;

          let evt;
          try { evt = JSON.parse(raw); } catch { continue; }

          if (evt.type === "response.output_text.delta" && typeof evt.delta === "string") {
            writeChunk({ content: evt.delta });
          }
          if (evt.type === "response.completed") completed = true;
          if (evt.type === "response.failed") failure = evt.response?.error || { code: "response_failed" };
          if (evt.type === "response.incomplete") failure = evt.response?.incomplete_details || { code: "response_incomplete" };
        }
      }
    }

    if (failure) {
      httpRes.write("data: " + JSON.stringify({ error: failure }) + "\n\n");
    } else if (!completed) {
      httpRes.write("data: " + JSON.stringify({ error: { message: "OpenAI stream ended before completion" } }) + "\n\n");
    } else {
      writeChunk({}, "stop");
    }
    httpRes.write("data: [DONE]\n\n");
  } finally {
    clearInterval(heartbeat);
    if (!httpRes.writableEnded) httpRes.end();
  }
}

const yanziToolChat = createYanziToolHandler({
  readJson, sendJson, usableCredentials, upstreamModels, selectDefaultModel,
  audit: entry => console.log(JSON.stringify({event: "gateway_tool_call", ...entry}))
});

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, "http://" + HOST + ":" + PORT);
    if (req.method === "GET" && url.pathname === "/health") {
      let credentialPresent = false;
      let scopeOk = false;
      try {
        const c = loadCredentials();
        credentialPresent = !!c;
        scopeOk = !!c && String(c.scope || "").split(/\s+/).includes(REQUIRED_SCOPE);
      } catch {}
      return sendJson(res, 200, { ok: true, service: "chatgpt-gateway", credential_present: credentialPresent, plan_scope: scopeOk });
    }

    if (req.method === "POST" && url.pathname === "/yanji/feed") {
      const mobileToken = bearerToken(req);
      const user = await validateYanziToken(mobileToken);
      const body = await readJson(req);
      const limit = Math.max(2, Math.min(8, Number(body.limit || 6)));
      const cursor = Math.max(0, Number(body.cursor || 0));
      const seen = Array.isArray(body.seenTitles)
        ? body.seenTitles.map(x => String(x || "").trim()).filter(Boolean).slice(-30)
        : [];
      const interests = Array.isArray(body.interests)
        ? body.interests.map(x => String(x || "").trim()).filter(Boolean).slice(0, 20)
        : [];
      const liked = Array.isArray(body.liked)
        ? body.liked.slice(-10).map(x => ({
            title: String(x?.title || "").trim().slice(0, 100),
            body: String(x?.body || "").trim().slice(0, 180),
            topic: String(x?.topic || "").trim().slice(0, 60)
          })).filter(x => x.title || x.body)
        : [];
      const disliked = Array.isArray(body.disliked)
        ? body.disliked.slice(-10).map(x => ({
            title: String(x?.title || "").trim().slice(0, 100),
            body: String(x?.body || "").trim().slice(0, 180),
            topic: String(x?.topic || "").trim().slice(0, 60)
          })).filter(x => x.title || x.body)
        : [];
      const userSystemPrompt = typeof body.systemPrompt === "string"
        ? body.systemPrompt.trim().slice(0, 6000)
        : "";
      const c = await usableCredentials();
      const models = await upstreamModels(c.access_token);
      const model = selectDefaultModel(models);
      const prompt = [
        "你正在为一个名为“燕记”的个人双列图文信息流生成一批中文短笔记。",
        "请返回严格 JSON，不要 Markdown，不要解释。格式必须是：",
        '{"items":[{"title":"标题","body":"正文","topic":"主题标签"}]}',
        "要求：",
        "1. 一次生成 " + limit + " 篇，彼此主题和角度尽量不同。",
        "2. 每篇 title 8-28 字；body 120-320 字；topic 2-10 字。",
        "3. 内容应有现实意义、可验证、避免鸡汤、广告、虚构新闻和伪造亲身经历。",
        "4. 优先讲清一个具体问题、机制、案例、边界或可迁移应用。",
        "5. 不要重复最近已经出现的标题或相近结论。",
        "6. 当前批次游标为 " + cursor + "，请把它当作新一轮探索，而不是续写上一条。",
        interests.length ? "用户近期兴趣：" + interests.join("、") : "用户未设置明确兴趣，请跨现实生活、科技、社会、效率、创作、经济等领域探索。",
        liked.length ? "用户最近标记“感兴趣”的笔记：" + liked.map(x => [x.topic, x.title, x.body].filter(Boolean).join("｜")).join("\n") : "",
        disliked.length ? "用户最近标记“不感兴趣”的笔记：" + disliked.map(x => [x.topic, x.title, x.body].filter(Boolean).join("｜")).join("\n") : "",
        seen.length ? "最近已经出现的标题：" + seen.join("｜") : ""
      ].filter(Boolean).join("\n");
      const instructions = userSystemPrompt || "优先服从用户明确设置的兴趣主题与喜欢/不感兴趣反馈；每篇只讲清一个具体问题、机制、案例、边界或迁移应用；避免空泛总结、鸡汤、广告、标题党和虚构新闻。只输出可被 JSON.parse 直接解析的 JSON 对象。";
      const output = await responseText(c.access_token, model, prompt, instructions, {
        verbosity: "medium",
        reasoningEffort: "low"
      });
      const parsed = extractJsonPayload(output);
      const items = Array.isArray(parsed?.items) ? parsed.items : [];
      const clean = items.slice(0, limit).map((item, index) => ({
        id: "remote-" + Date.now().toString(36) + "-" + cursor + "-" + index,
        title: String(item?.title || "").trim().slice(0, 160),
        body: String(item?.body || "").trim().slice(0, 20000),
        topic: String(item?.topic || "").trim().slice(0, 40),
        ai: true,
        draft: false,
        remote: true,
        createdAt: Date.now()
      })).filter(x => x.title && x.body);
      if (clean.length < 2) throw Object.assign(new Error("yanji feed returned too few valid items"), { statusCode: 502 });
      return sendJson(res, 200, {
        ok: true,
        accountId: user.userId,
        cursor,
        nextCursor: cursor + 1,
        model,
        items: clean
      });
    }

    if (!authorized(req)) return sendJson(res, 401, { ok: false, error: "unauthorized" });

    if (req.method === "POST" && url.pathname === "/v1/tool-chat") {
      return await yanziToolChat(req, res);
    }

    if (req.method === "GET" && url.pathname === "/v1/models") {
      const c = await usableCredentials();
      const models = await upstreamModels(c.access_token);
      const now = Math.floor(Date.now() / 1000);
      return sendJson(res, 200, {
        object: "list",
        data: models.map(model => ({
          id: model.slug,
          object: "model",
          created: now,
          owned_by: "chatgpt-plan"
        })),
        ok: true,
        models,
        default_model: selectDefaultModel(models)
      });
    }

    if (req.method === "POST" && url.pathname === "/v1/chat") {
      const body = await readJson(req);
      const input = body.input ?? body.prompt;
      if (!(typeof input === "string" && input.trim()) && !Array.isArray(input)) {
        throw Object.assign(new Error("input or prompt is required"), { statusCode: 400 });
      }
      const c = await usableCredentials();
      const models = await upstreamModels(c.access_token);
      const model = typeof body.model === "string" && body.model.trim() ? body.model.trim() : selectDefaultModel(models);
      if (!models.some(m => m.slug === model)) throw Object.assign(new Error("model is not available to this ChatGPT account"), { statusCode: 400 });
      const text = await responseText(c.access_token, model, input, body.instructions, {
        verbosity: body.verbosity,
        reasoningEffort: body.reasoning_effort ?? body.reasoning?.effort
      });
      return sendJson(res, 200, { ok: true, model, text });
    }

    if (req.method === "POST" && url.pathname === "/v1/chat/completions") {
      const body = await readJson(req);
      if (!Array.isArray(body.messages) || body.messages.length === 0) {
        throw Object.assign(new Error("messages must be a non-empty array"), { statusCode: 400 });
      }

      const c = await usableCredentials();
      const models = await upstreamModels(c.access_token);
      const model = typeof body.model === "string" && body.model.trim()
        ? body.model.trim()
        : selectDefaultModel(models);
      if (!models.some(m => m.slug === model)) {
        throw Object.assign(new Error("model is not available to this ChatGPT account"), { statusCode: 400 });
      }

      const normalized = [];
      for (const message of body.messages) {
        if (!message || typeof message !== "object") continue;
        let role = String(message.role || "").trim().toLowerCase();
        if (role === "system") role = "developer";
        if (!["developer", "user", "assistant"].includes(role)) continue;

        let content = message.content;
        if (Array.isArray(content)) {
          content = content
            .map(part => {
              if (typeof part === "string") return part;
              if (part && typeof part === "object" && typeof part.text === "string") return part.text;
              return "";
            })
            .filter(Boolean)
            .join("\n");
        }
        if (typeof content !== "string") continue;
        normalized.push({ role, content });
      }
      if (normalized.length === 0) {
        throw Object.assign(new Error("messages contain no supported text content"), { statusCode: 400 });
      }

      if (body.stream === true) {
        await streamChatCompletionCompatible(res, c.access_token, model, normalized, {
          verbosity: body.verbosity,
          reasoningEffort: body.reasoning_effort ?? body.reasoning?.effort
        });
        return;
      }

      const text = await responseText(c.access_token, model, normalized, undefined, {
        verbosity: body.verbosity,
        reasoningEffort: body.reasoning_effort ?? body.reasoning?.effort
      });
      const created = Math.floor(Date.now() / 1000);
      return sendJson(res, 200, {
        id: "chatcmpl_yanzi_" + crypto.randomUUID().replace(/-/g, ""),
        object: "chat.completion",
        created,
        model,
        choices: [{
          index: 0,
          message: { role: "assistant", content: text },
          finish_reason: "stop"
        }]
      });
    }

    return sendJson(res, 404, { ok: false, error: "not found" });
  } catch (error) {
    const status = Number(error?.statusCode || 500);
    console.error(new Date().toISOString(), error?.stack || error);
    return sendJson(res, status, { ok: false, error: error?.message || "internal error" });
  }
});

server.listen(PORT, HOST, () => {
  console.log(`chatgpt-gateway listening on http://${HOST}:${PORT}`);
});
