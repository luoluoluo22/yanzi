// Account-device discovery via the already authenticated Yanzi desktop Agent API.
// No cloud credential, private account token, or remote MCP URL is shared with peers.
import fs from "node:fs/promises";
import path from "node:path";
import os from "node:os";

let lastLoaded = 0;
let cached = [];
let ongoing = null;

function settingsPath() {
  return process.env.YANZI_AGENT_SETTINGS_FILE ||
    path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"),
      "OpenQuickHost", "appsettings.local.json");
}
async function readAgent() {
  const raw = (await fs.readFile(settingsPath(), "utf8")).replace(/^\uFEFF/, "");
  const settings = JSON.parse(raw);
  if (!Number.isInteger(settings.agentApiPort) || settings.agentApiPort < 1 ||
      settings.agentApiPort > 65535 || !settings.agentApiToken) throw new Error("Yanzi Agent API not configured.");
  return { port: settings.agentApiPort, token: settings.agentApiToken };
}
async function invokeAgent(route, data, ms = 12000) {
  const { port, token } = await readAgent();
  const response = await fetch("http://127.0.0.1:" + port + route, {
    method: data === undefined ? "GET" : "POST",
    headers: { "X-Yanzi-Token": token, "Content-Type": "application/json" },
    body: data === undefined ? undefined : JSON.stringify(data),
    signal: AbortSignal.timeout(ms)
  });
  if (!response.ok) {
    // Do not echo raw server errors: they may contain untrusted data.
    throw new Error("Yanzi Agent API returned HTTP " + response.status);
  }
  return response.json();
}
function safeDeviceRows(rows) {
  const machine = os.hostname().toLowerCase();
  const result = [];
  for (const row of Array.isArray(rows) ? rows : []) {
    const id = String(row.deviceId || "");
    const name = String(row.displayName || "");
    if (!/^desktop-[0-9a-f]{32}$/i.test(id) || row.platform !== "desktop") continue;
    const host = name.replace(/^Windows\s*[·-]\s*/i, "").trim().toLowerCase();
    if (host === machine) continue;
    result.push({ id, name: name.replace(/^Windows\s*[·-]\s*/i, "").trim(), displayName: name,
      status: row.online === true ? "online" : "offline",
      source: "yanzi", local: false });
  }
  return result;
}
export async function refreshAccountDevices(force = false) {
  const now = Date.now();
  if (!force && now - lastLoaded < 15000) return cached;
  if (ongoing) return ongoing;
  ongoing = (async () => {
    try {
      const result = await invokeAgent("/v1/me/devices", undefined, 4500);
      cached = safeDeviceRows(result.items);
    } catch {
      cached = []; // Never silently retain an old authorized-account device list after logout.
    }
    lastLoaded = Date.now();
    return cached;
  })();
  try { return await ongoing; }
  finally { ongoing = null; }
}
export function cachedAccountDevices() { return cached; }
export async function invokeAccountDevice(deviceId, toolName, toolArgs) {
  // Fetch again to avoid routing by an outdated account after a logout or device removal.
  const peers = await refreshAccountDevices(true);
  const target = peers.find(row => row.id === deviceId);
  if (!target) throw new Error("The selected device is not registered in the active Yanzi account.");
  if (target.status !== "online") throw new Error("The selected Yanzi device is offline.");
  const response = await invokeAgent("/v1/capabilities/invoke", {
    name: "device.raccoon.invoke",
    payload: { target: deviceId, operation: "call", name: toolName, arguments: toolArgs }
  }, 125000);
  if (response.success !== true) throw new Error(response.error || "Yanzi device invocation denied.");
  const outcome = response.data;
  if (!outcome || outcome.success !== true || outcome.status === "unknown") {
    const status = outcome?.status || "failed";
    throw new Error("Yanzi device execution not verified (" + status + "); do not retry non-idempotent commands.");
  }
  const cloudResult = outcome.result;
  // Yanzi's device bridge wraps a JSON invocation result in executionResult.output.
  let parsed = cloudResult;
  if (parsed && typeof parsed.output === "string") {
    try { parsed = JSON.parse(parsed.output); }
    catch { throw new Error("Yanzi device response is not valid JSON."); }
  }
  if (parsed?.success === false) throw new Error(parsed.error || "Remote MCP execution failed.");
  const result = parsed?.data ?? parsed;
  if (!result || !Array.isArray(result.content)) throw new Error("Remote MCP returned an invalid result.");
  return result;
}
export function resetAccountDeviceCacheForTest() { lastLoaded = 0; cached = []; ongoing = null; }
