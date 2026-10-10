import fs from "node:fs";
import path from "node:path";
import { randomBytes } from "node:crypto";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const runtime = path.join(root, ".raccoon-runtime");
const hostname = process.argv[2];
if (!hostname || !/^[a-z0-9.-]+$/.test(hostname)) throw new Error("Usage: node scripts/cloudflare-setup.js mcp.example.com");
const tokenFile = path.join(runtime, "cloudflare-api-token.txt");
const apiToken = fs.readFileSync(tokenFile, "utf8").trim();
if (!apiToken || /\s/.test(apiToken)) throw new Error("Invalid Cloudflare API token file.");

async function api(endpoint, method = "GET", body) {
  const response = await fetch(`https://api.cloudflare.com/client/v4/${endpoint}`, {
    method, headers: { Authorization: `Bearer ${apiToken}`, "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(30000)
  });
  const data = await response.json();
  if (!response.ok || !data.success) throw new Error(`Cloudflare ${method} ${endpoint}: HTTP ${response.status}, error codes ${data.errors?.map(e => e.code).join(",") || "unknown"}. Check Zone DNS Edit and Account Cloudflare Tunnel Edit permissions.`);
  return data.result;
}

try {
  const zones = await api("zones?per_page=50");
  const zone = zones.filter(z => hostname.endsWith(`.${z.name}`)).sort((a, b) => b.name.length - a.name.length)[0];
  if (!zone) throw new Error("No accessible Cloudflare zone matches this hostname.");
  const accountId = zone.account.id;
  // Check DNS permissions and conflicts before creating any resources.
  const records = await api(`zones/${zone.id}/dns_records?name=${encodeURIComponent(hostname)}`);
  const stateFile = path.join(runtime, "cloudflare.json");
  let state = fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, "utf8")) : null;
  if (state && (state.hostname !== hostname || state.accountId !== accountId)) throw new Error("A different Cloudflare tunnel is already configured. Preserve or remove its local configuration before switching.");
  if (records.length && (!state || records.some(r => r.type !== "CNAME" || r.content !== `${state.tunnelId}.cfargotunnel.com`))) throw new Error("The selected DNS hostname is already in use. No changes made.");
  if (!state) {
    const tunnel = await api(`accounts/${accountId}/cfd_tunnel`, "POST", { name: "raccoon-mcp-local", config_src: "cloudflare" });
    state = { accountId, zoneId: zone.id, hostname, tunnelId: tunnel.id, createdAt: new Date().toISOString() };
    fs.mkdirSync(runtime, { recursive: true });
    fs.writeFileSync(stateFile, JSON.stringify(state, null, 2), { mode: 0o600 });
  }
  const current = await api(`accounts/${accountId}/cfd_tunnel/${state.tunnelId}`);
  if (current.name !== "raccoon-mcp-local") throw new Error("Refusing to configure a tunnel owned by another application.");
  await api(`accounts/${accountId}/cfd_tunnel/${state.tunnelId}/configurations`, "PUT", {
    config: { ingress: [
      { hostname, path: "^(/mcp|/authorize|/token|/register|/revoke|/oauth/consent|/health|/\\.well-known/oauth-(authorization-server|protected-resource(/mcp)?))$", service: "http://127.0.0.1:3766", originRequest: { httpHostHeader: hostname } },
      { service: "http_status:404" }
    ] }
  });
  const tunnelToken = await api(`accounts/${accountId}/cfd_tunnel/${state.tunnelId}/token`);
  fs.writeFileSync(path.join(runtime, "cloudflare-tunnel-token.txt"), tunnelToken, { mode: 0o600 });
  const passwordFile = path.join(runtime, "oauth-owner-password.txt");
  const password = fs.existsSync(passwordFile) ? fs.readFileSync(passwordFile, "utf8").trim() : randomBytes(32).toString("base64url");
  fs.writeFileSync(passwordFile, password, { mode: 0o600 });
  const envFile = path.join(root, ".env");
  let env = fs.readFileSync(envFile, "utf8");
  const values = { RACCOON_PUBLIC_URL: `https://${hostname}`, RACCOON_OAUTH_PASSWORD: password, RACCOON_ALLOWED_HOSTS: `127.0.0.1,localhost,[::1],${hostname}`, RACCOON_TUNNEL_PROVIDER: "cloudflare" };
  for (const [key, value] of Object.entries(values)) {
    const pattern = new RegExp(`^${key}=.*$`, "m");
    env = pattern.test(env) ? env.replace(pattern, () => `${key}=${value}`) : `${env.trimEnd()}\n${key}=${value}\n`;
  }
  fs.writeFileSync(envFile, env, { mode: 0o600 });
  if (!records.length) await api(`zones/${zone.id}/dns_records`, "POST", { type: "CNAME", name: hostname, content: `${state.tunnelId}.cfargotunnel.com`, proxied: true, ttl: 1, comment: "Raccoon local MCP Cloudflare Tunnel" });
  console.log(JSON.stringify({ configured: true, url: `https://${hostname}/mcp`, tunnelId: state.tunnelId, secrets: "saved locally; omitted" }));
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
