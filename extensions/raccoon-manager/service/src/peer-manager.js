import os from "node:os";
import { createHash } from "node:crypto";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import { getRuntimeSetting } from "./runtime-settings.js";
import { version } from "./version.js";
import { refreshAccountDevices, cachedAccountDevices, invokeAccountDevice } from "./yanzi-federation.js";

const localId = createHash("sha256")
  .update(`${os.hostname()}|${process.platform}|${process.arch}`)
  .digest("hex")
  .slice(0, 24);

export function localDeviceId() {
  return localId;
}

function normalizedPeers() {
  const peers = getRuntimeSetting("remoteDevices") || [];
  const manual = peers.map(peer => ({
    ...peer,
    id: peer.id || createHash("sha256").update(String(peer.url)).digest("hex").slice(0, 24),
    name: peer.name || peer.id || peer.url,
    source: "manual"
  }));
  const known = new Set(manual.map(peer => peer.id));
  return [...manual, ...cachedAccountDevices().filter(peer => !known.has(peer.id))];
}

export function resolveDeviceSelector(selector) {
  const key = String(selector ?? "").trim();
  if (!key) return localId;
  const localName = os.hostname();
  const peers = normalizedPeers();
  const matches = [
    ...(key.toLowerCase() === localName.toLowerCase() || key === localId ? [{ id: localId, name: localName }] : []),
    ...peers.filter(peer => peer.id === key || peer.name.toLowerCase() === key.toLowerCase() ||
      peer.displayName?.toLowerCase() === key.toLowerCase())
  ];
  const ids = [...new Set(matches.map(item => item.id))];
  if (!ids.length) throw new Error(`Unknown Raccoon device "${key}". Available: ${[localName, ...peers.map(peer => peer.name)].join(", ")}`);
  if (ids.length > 1) throw new Error(`Ambiguous Raccoon device "${key}"; choose a unique deviceId.`);
  return ids[0];
}

export function findPeer(selector) {
  const id = resolveDeviceSelector(selector);
  if (id === localId) return null;
  return normalizedPeers().find(peer => peer.id === id) || null;
}

function healthUrl(mcpUrl) {
  const url = new URL(mcpUrl);
  url.pathname = url.pathname.replace(/\/mcp\/?$/, "/health");
  url.search = "";
  return url;
}

async function peerStatus(peer) {
  try {
    const response = await fetch(healthUrl(peer.url), {
      signal: AbortSignal.timeout(2000),
      headers: peer.healthHeaders || undefined
    });
    if (!response.ok) return { status: "offline", error: `HTTP ${response.status}` };
    const health = await response.json().catch(() => ({}));
    return { status: "online", health };
  } catch (error) {
    return { status: "offline", error: error.message };
  }
}

export async function listFederatedDevices() {
  await refreshAccountDevices();
  const local = {
    id: localId,
    name: os.hostname(),
    status: "online",
    local: true,
    platform: process.platform,
    arch: process.arch,
    version
  };
  const peers = await Promise.all(normalizedPeers().map(async peer => {
    const state = peer.source === "yanzi" ? { status: peer.status } : await peerStatus(peer);
    return {
      id: peer.id,
      name: peer.name,
      status: state.status,
      local: false,
      url: peer.url,
      version: state.health?.version ?? null,
      source: peer.source,
      error: state.error ?? null
    };
  }));
  return [local, ...peers];
}

export async function callPeerTool(peer, name, args = {}) {
  const headers = { ...(peer.headers || {}) };
  if (peer.token) headers.Authorization = `Bearer ${peer.token}`;
  const transport = new StreamableHTTPClientTransport(new URL(peer.url), {
    requestInit: { headers }
  });
  const client = new Client({ name: "raccoon-federation", version });
  try {
    await client.connect(transport);
    return await client.callTool({ name, arguments: args });
  } finally {
    await client.close().catch(() => {});
  }
}

export async function callPeerToolByDeviceId(deviceId, name, args = {}) {
  await refreshAccountDevices();
  if (!deviceId || resolveDeviceSelector(deviceId) === localId) return null;
  const peer = findPeer(deviceId);
  if (!peer) throw new Error(`Unknown Raccoon deviceId: ${deviceId}`);
  if (peer.source === "yanzi") return invokeAccountDevice(peer.id, name, args);
  return callPeerTool(peer, name, args);
}

export async function routeDeviceTool(name, args, localHandler) {
  await refreshAccountDevices();
  const input = args || {};
  const targetId = input.deviceId;
  if (targetId && resolveDeviceSelector(targetId) !== localId) {
    const peer = findPeer(targetId);
    if (!peer) throw new Error(`Unknown Raccoon deviceId: ${targetId}`);
    const forwarded = { ...input };
    delete forwarded.deviceId;
    return peer.source === "yanzi" ? invokeAccountDevice(peer.id, name, forwarded) : callPeerTool(peer, name, forwarded);
  }
  return localHandler(input);
}
