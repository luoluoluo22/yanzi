import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { randomBytes, createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import express from "express";
import { installOAuth, OwnerOAuthProvider } from "../src/oauth.js";

const root = await fs.mkdtemp(path.join(os.tmpdir(), "raccoon-oauth-"));
const options = { issuer: "https://mcp.example.com", password: randomBytes(32).toString("base64url"), stateFile: path.join(root, "state.json") };
const app = express();
app.use(installOAuth(app, options));
app.post("/mcp", (_req, res) => res.json({ authorized: true }));
const listener = app.listen(0, "127.0.0.1");
await new Promise(resolve => listener.once("listening", resolve));
const base = `http://127.0.0.1:${listener.address().port}`;
const resource = `${options.issuer}/mcp`;
const redirect = "https://chatgpt.com/connector_platform_oauth_redirect";
let client;
test.after(async () => { await new Promise(resolve => listener.close(resolve)); await fs.rm(root, { recursive: true, force: true }); });
const post = (endpoint, body, headers = {}) => fetch(base + endpoint, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded", ...headers }, body: new URLSearchParams(body), redirect: "manual" });

test("OAuth publishes discovery and protects anonymous MCP requests", async () => {
  const meta = await (await fetch(base + "/.well-known/oauth-authorization-server")).json();
  assert.deepEqual(meta.code_challenge_methods_supported, ["S256"]);
  assert.equal(meta.registration_endpoint, options.issuer + "/register");
  const protectedMeta = await (await fetch(base + "/.well-known/oauth-protected-resource/mcp")).json();
  assert.equal(protectedMeta.resource, resource);
  const response = await post("/mcp", {});
  assert.equal(response.status, 401);
  assert.match(response.headers.get("www-authenticate"), /resource_metadata/);
});

test("registration rejects arbitrary redirects and retains valid clients", async () => {
  const register = redirectUri => fetch(base + "/register", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ client_name: "ChatGPT", redirect_uris: [redirectUri], token_endpoint_auth_method: "none", grant_types: ["authorization_code", "refresh_token"], response_types: ["code"] }) });
  assert.equal((await register("https://attacker.example/callback")).status, 400);
  const response = await register(redirect);
  assert.equal(response.status, 201);
  client = await response.json();
  const restored = new OwnerOAuthProvider(options);
  assert.equal(restored.clientsStore.getClient(client.client_id).client_name, "ChatGPT");
});

test("owner approval, PKCE, resource binding, replay prevention, refresh and revocation", async () => {
  const verifier = randomBytes(32).toString("base64url");
  const params = { response_type: "code", client_id: client.client_id, redirect_uri: redirect, code_challenge: createHash("sha256").update(verifier).digest("base64url"), code_challenge_method: "S256", resource, scope: "workspace:access", state: "test-state" };
  const start = await fetch(base + "/authorize?" + new URLSearchParams(params), { redirect: "manual" });
  assert.equal(start.status, 303);
  const consent = new URL(start.headers.get("location"), base);
  const id = consent.searchParams.get("id");
  assert.equal((await post("/oauth/consent", { id, password: options.password, decision: "approve" })).status, 403);
  assert.equal((await post("/oauth/consent", { id, password: "wrong", decision: "approve" }, { Origin: options.issuer })).status, 403);
  const accepted = await post("/oauth/consent", { id, password: options.password, decision: "approve" }, { Origin: options.issuer });
  assert.equal(accepted.status, 303);
  const callback = new URL(accepted.headers.get("location"));
  assert.equal(callback.searchParams.get("state"), "test-state");
  const exchange = { grant_type: "authorization_code", client_id: client.client_id, code: callback.searchParams.get("code"), code_verifier: verifier, redirect_uri: redirect, resource };
  assert.equal((await post("/token", { ...exchange, code_verifier: "x".repeat(43) })).status, 400);
  assert.equal((await post("/token", { ...exchange, resource: "https://other.example/mcp" })).status, 400);
  assert.equal((await post("/token", { ...exchange, redirect_uri: "https://chatgpt.com/wrong" })).status, 400);
  const issued = await post("/token", exchange);
  assert.equal(issued.status, 200);
  const tokens = await issued.json();
  assert.equal((await post("/token", exchange)).status, 400);
  assert.equal((await post("/mcp", {}, { Authorization: `Bearer ${tokens.access_token}` })).status, 200);
  const persisted = new OwnerOAuthProvider(options);
  assert.equal((await persisted.verifyAccessToken(tokens.access_token)).clientId, client.client_id);
  assert.ok(!(await fs.readFile(options.stateFile, "utf8")).includes(tokens.access_token));
  const refresh = { grant_type: "refresh_token", client_id: client.client_id, refresh_token: tokens.refresh_token, resource };
  assert.equal((await post("/token", { ...refresh, scope: "admin" })).status, 400);
  const rotated = await post("/token", refresh);
  assert.equal(rotated.status, 200);
  const newTokens = await rotated.json();
  assert.equal((await post("/token", refresh)).status, 400);
  const revoked = await post("/revoke", { client_id: client.client_id, token: newTokens.refresh_token });
  assert.equal(revoked.status, 200);
  assert.equal((await post("/mcp", {}, { Authorization: `Bearer ${newTokens.access_token}` })).status, 401);
  assert.equal((await post("/mcp", {}, { Authorization: `Bearer ${tokens.access_token}` })).status, 401);
});

test("misconfigured issuer or weak owner password fails closed", () => {
  assert.throws(() => new OwnerOAuthProvider({ ...options, issuer: "http://mcp.example.com" }), /HTTPS/);
  assert.throws(() => new OwnerOAuthProvider({ ...options, password: "weak" }), /32 characters/);
});

test("the public HTTP host rejects the local static token and advertises OAuth tools", () => {
  const child = spawnSync(process.execPath, ["--input-type=module", "-e", `
    import assert from 'node:assert/strict';
    import http from 'node:http';
    const {OwnerOAuthProvider}=await import('./src/oauth.js');
    const provider=new OwnerOAuthProvider({issuer:process.env.RACCOON_PUBLIC_URL,password:process.env.RACCOON_OAUTH_PASSWORD,stateFile:process.env.RACCOON_OAUTH_STATE_FILE});
    const tokens=provider.issue('isolation-test');
    const {createHttpApp}=await import('./src/http.js');
    const server=createHttpApp().listen(0,'127.0.0.1');
    await new Promise(r=>server.once('listening',r));
    try {
      const base='http://127.0.0.1:'+server.address().port;
      const call=(host,token)=>new Promise((resolve,reject)=>{
        const request=http.request(base+'/mcp',{method:'POST',headers:{Host:host,Authorization:'Bearer '+token,'Content-Type':'application/json',Accept:'application/json, text/event-stream'}},response=>{
          let body='';response.on('data',data=>body+=data);response.on('end',()=>resolve({status:response.statusCode,text:async()=>body}));
        });request.on('error',reject);request.end(JSON.stringify({jsonrpc:'2.0',id:1,method:'tools/list',params:{}}));
      });
      assert.equal((await call('mcp.example.com','local-secret')).status,401);
      assert.equal((await call('127.0.0.1','local-secret')).status,200);
      const result=await call('mcp.example.com',tokens.access_token);
      assert.equal(result.status,200);
      assert.match(await result.text(),/securitySchemes/);
    } finally {await new Promise(r=>server.close(r));}
  `], { encoding: "utf8", env: { ...process.env, RACCOON_ROOT: root, RACCOON_TRANSPORT: "http", RACCOON_PUBLIC_URL: options.issuer, RACCOON_OAUTH_PASSWORD: options.password, RACCOON_OAUTH_STATE_FILE: path.join(root, "isolation.json"), RACCOON_TOKEN: "local-secret", RACCOON_ALLOWED_HOSTS: "127.0.0.1,mcp.example.com", RACCOON_AUDIT_LOG: "" } });
  assert.equal(child.status, 0, child.stderr);
});
