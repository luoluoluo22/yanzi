import fs from "node:fs";
import path from "node:path";
import { randomBytes, createHash, timingSafeEqual } from "node:crypto";
import express from "express";
import { mcpAuthRouter } from "@modelcontextprotocol/sdk/server/auth/router.js";
import { requireBearerAuth } from "@modelcontextprotocol/sdk/server/auth/middleware/bearerAuth.js";
import { InvalidGrantError, InvalidTokenError, InvalidTargetError, InvalidScopeError, InvalidClientMetadataError } from "@modelcontextprotocol/sdk/server/auth/errors.js";

const scope = "workspace:access";
const raccoonIcon = fs.readFileSync(new URL("../assets/raccoon-128.png", import.meta.url)).toString("base64");
const nonce = () => randomBytes(32).toString("base64url");
const digest = value => createHash("sha256").update(value).digest("hex");
const html = value => String(value).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const now = () => Math.floor(Date.now() / 1000);

// A single-owner OAuth provider. Protocol routing, PKCE and client authentication
// are supplied by the MCP SDK; secrets and grants stay in the protected runtime directory.
export class OwnerOAuthProvider {
  constructor({ issuer, password, stateFile, allowedRedirectHosts = ["chatgpt.com"] }) {
    const url = new URL(issuer);
    if (url.protocol !== "https:" || url.pathname !== "/" || url.search || url.hash || url.username || url.password) throw new Error("OAuth issuer must be an HTTPS origin.");
    if (!password || password.length < 32) throw new Error("OAuth owner password must contain at least 32 characters.");
    this.issuer = url.href;
    this.resource = new URL("/mcp", url).href;
    this.passwordHash = digest(password);
    this.stateFile = stateFile;
    this.allowedRedirectHosts = allowedRedirectHosts;
    this.pending = new Map();
    this.codes = new Map();
    this.attempts = [];
    this.state = { clients: {}, access: {}, refresh: {} };
    try { this.state = JSON.parse(fs.readFileSync(stateFile, "utf8")); } catch (error) { if (error.code !== "ENOENT") throw error; }
    this.clientsStore = {
      getClient: id => Object.hasOwn(this.state.clients, id) ? this.state.clients[id] : undefined,
      registerClient: client => {
        if (!client.redirect_uris?.length || client.redirect_uris.length > 5 || client.redirect_uris.some(uri => {
          try { const u = new URL(uri); return u.protocol !== "https:" || !this.allowedRedirectHosts.includes(u.hostname) || u.username || u.password || u.hash; } catch { return true; }
        })) throw new InvalidClientMetadataError("Only approved HTTPS ChatGPT callbacks are supported.");
        if (Object.keys(this.state.clients).length >= 100) throw new InvalidClientMetadataError("Client registration limit reached.");
        const registered = { ...client, client_id: client.client_id || nonce(), client_id_issued_at: now() };
        this.state.clients[registered.client_id] = registered;
        this.save();
        return registered;
      }
    };
  }

  save() {
    for (const kind of ["access", "refresh"]) for (const [key, record] of Object.entries(this.state[kind])) if (record.expiresAt <= now()) delete this.state[kind][key];
    fs.mkdirSync(path.dirname(this.stateFile), { recursive: true });
    const temp = `${this.stateFile}.tmp`;
    fs.writeFileSync(temp, JSON.stringify(this.state), { mode: 0o600 });
    fs.renameSync(temp, this.stateFile);
  }

  checkResource(resource) {
    if (!resource || resource.href !== this.resource) throw new InvalidTargetError("The resource must match this MCP endpoint.");
  }

  checkScopes(scopes = []) {
    if (scopes.some(s => s !== scope)) throw new InvalidScopeError("Unsupported scope.");
  }

  async authorize(client, params, res) {
    this.checkResource(params.resource);
    this.checkScopes(params.scopes);
    for (const [id, record] of this.pending) if (record.expiresAt <= now()) this.pending.delete(id);
    for (const [id, record] of this.codes) if (record.expiresAt <= now()) this.codes.delete(id);
    if (this.pending.size >= 100) throw new InvalidGrantError("Too many pending requests.");
    const id = nonce();
    this.pending.set(id, { clientId: client.client_id, ...params, expiresAt: now() + 600 });
    res.redirect(303, `/oauth/consent?id=${id}`);
  }

  consentPage(req, res) {
    const request = this.pending.get(req.query.id);
    if (!request || request.expiresAt <= now()) return res.status(400).send("Authorization request expired. Reconnect from ChatGPT.");
    // Chromium applies form-action to the post-login redirect too.
    const callbackOrigins = this.allowedRedirectHosts.map(host => `https://${host}`).join(" ");
    res.setHeader("Content-Security-Policy", `default-src 'none'; style-src 'unsafe-inline'; img-src data:; form-action 'self' ${callbackOrigins}; frame-ancestors 'none'; base-uri 'none'`);
    // Chromium can send an opaque Origin on a form POST with no-referrer.
    // Same-origin preserves the CSRF check without leaking the transaction to ChatGPT.
    res.setHeader("Referrer-Policy", "same-origin");
    const client = this.state.clients[request.clientId];
    res.type("html").send(`<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><link rel="icon" href="data:image/png;base64,${raccoonIcon}"><title>Raccoon 授权</title><style>body{font:16px system-ui;max-width:560px;margin:70px auto;padding:24px;line-height:1.7}.logo{display:block;width:64px;height:64px;margin:0 0 18px;border-radius:18px}input,button{font:inherit;padding:12px;box-sizing:border-box;width:100%;margin:10px 0}button{cursor:pointer}</style><img class="logo" src="data:image/png;base64,${raccoonIcon}" alt="Raccoon"><h1>连接 Raccoon</h1><p>应用：${html(client?.client_name || "ChatGPT")}</p><p>授权后，这个应用可以读取和修改本机配置工作区内的文件。Shell 执行权限由本机配置决定。</p><p>回调地址：${html(request.redirectUri)}</p><form method="post" action="/oauth/consent"><input type="hidden" name="id" value="${html(req.query.id)}"><label>本机生成的连接密码<input name="password" type="password" autocomplete="current-password" required></label><button name="decision" value="approve">授权连接</button><button name="decision" value="deny" formnovalidate>取消</button></form><p>密码保存在本机 .raccoon-runtime/oauth-owner-password.txt。</p></html>`);
  }

  consent(req, res) {
    if (req.headers.origin !== new URL(this.issuer).origin) return res.status(403).send("Invalid origin.");
    const request = this.pending.get(req.body.id);
    if (!request || request.expiresAt <= now()) return res.status(400).send("Authorization request expired.");
    const redirect = new URL(request.redirectUri);
    if (request.state) redirect.searchParams.set("state", request.state);
    if (req.body.decision === "deny") {
      this.pending.delete(req.body.id);
      redirect.searchParams.set("error", "access_denied");
      return res.redirect(303, redirect.href);
    }
    this.attempts = this.attempts.filter(t => t > Date.now() - 15 * 60 * 1000);
    if (this.attempts.length >= 10) return res.status(429).send("Too many password attempts. Try again in 15 minutes.");
    this.attempts.push(Date.now());
    if (typeof req.body.password !== "string" || !timingSafeEqual(Buffer.from(digest(req.body.password)), Buffer.from(this.passwordHash))) return res.status(403).send("Invalid connection password. Return to the authorization page to retry.");
    this.pending.delete(req.body.id);
    const code = nonce();
    this.codes.set(code, { ...request, expiresAt: now() + 120 });
    redirect.searchParams.set("code", code);
    return res.redirect(303, redirect.href);
  }

  codeFor(client, code) {
    const record = this.codes.get(code);
    if (!record || record.expiresAt <= now() || record.clientId !== client.client_id) throw new InvalidGrantError("Invalid authorization code.");
    return record;
  }

  async challengeForAuthorizationCode(client, code) { return this.codeFor(client, code).codeChallenge; }

  issue(clientId, scopes = [scope], family = nonce()) {
    const access = nonce(), refresh = nonce();
    this.state.access[digest(access)] = { clientId, scopes, family, expiresAt: now() + 3600, resource: this.resource };
    this.state.refresh[digest(refresh)] = { clientId, scopes, family, expiresAt: now() + 30 * 86400, resource: this.resource };
    this.save();
    return { access_token: access, token_type: "Bearer", expires_in: 3600, refresh_token: refresh, scope: scopes.join(" ") };
  }

  async exchangeAuthorizationCode(client, code, _verifier, redirectUri, resource) {
    const record = this.codeFor(client, code);
    this.checkResource(resource);
    if (redirectUri !== record.redirectUri) throw new InvalidGrantError("Redirect URI mismatch.");
    this.codes.delete(code);
    return this.issue(client.client_id);
  }

  async exchangeRefreshToken(client, token, scopes, resource) {
    this.checkResource(resource);
    const key = digest(token), record = this.state.refresh[key];
    if (!record || record.clientId !== client.client_id || record.expiresAt <= now() || record.resource !== this.resource) throw new InvalidGrantError("Invalid refresh token.");
    this.checkScopes(scopes);
    if (scopes?.some(s => !record.scopes.includes(s))) throw new InvalidScopeError("Scope escalation denied.");
    delete this.state.refresh[key];
    return this.issue(client.client_id, scopes?.length ? scopes : record.scopes, record.family);
  }

  async verifyAccessToken(token) {
    const record = this.state.access[digest(token)];
    if (!record || record.expiresAt <= now() || record.resource !== this.resource) throw new InvalidTokenError("Invalid or expired access token.");
    return { token, clientId: record.clientId, scopes: record.scopes, expiresAt: record.expiresAt, resource: new URL(record.resource) };
  }

  async revokeToken(client, request) {
    const key = digest(request.token), record = this.state.access[key] || this.state.refresh[key];
    if (record?.clientId !== client.client_id) return;
    for (const kind of ["access", "refresh"]) for (const [id, entry] of Object.entries(this.state[kind])) if (entry.family === record.family) delete this.state[kind][id];
    this.save();
  }
}

export function installOAuth(app, options) {
  const provider = new OwnerOAuthProvider(options);
  app.locals.oauthProvider = provider;
  app.get("/oauth/consent", (req, res) => provider.consentPage(req, res));
  app.post("/oauth/consent", express.urlencoded({ extended: false, limit: "8kb" }), (req, res) => provider.consent(req, res));
  // Keep a shared single-owner limiter instead of trusting proxy-supplied IPs.
  const rateLimit = { validate: { xForwardedForHeader: false } };
  app.use(mcpAuthRouter({ provider, issuerUrl: new URL(provider.issuer), resourceServerUrl: new URL(provider.resource), scopesSupported: [scope], resourceName: "Raccoon", clientRegistrationOptions: { clientSecretExpirySeconds: 0, rateLimit }, authorizationOptions: { rateLimit }, tokenOptions: { rateLimit }, revocationOptions: { rateLimit } }));
  return requireBearerAuth({ verifier: provider, requiredScopes: [scope], resourceMetadataUrl: new URL("/.well-known/oauth-protected-resource/mcp", provider.issuer).href });
}
