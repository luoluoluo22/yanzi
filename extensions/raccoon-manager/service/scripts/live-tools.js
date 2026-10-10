import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";

try { process.loadEnvFile(); } catch {}
const port = Number(process.env.RACCOON_PORT || 3766);
const token = process.env.RACCOON_TOKEN || "";
if (!token) throw new Error("RACCOON_TOKEN is not configured.");

const transport = new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${port}/mcp`), {
  requestInit: { headers: { Authorization: `Bearer ${token}` } }
});
const client = new Client({ name: "raccoon-live-inspector", version: "0.7.0" });

function data(result) {
  const item = result.content?.find(part => part.type === "text");
  return item ? JSON.parse(item.text) : null;
}

try {
  await client.connect(transport);
  const serverInfo = client.getServerVersion();
  const listed = await client.listTools();
  const config = data(await client.callTool({ name: "get_config", arguments: {} }));
  const system = data(await client.callTool({ name: "system_info", arguments: {} }));
  const devices = data(await client.callTool({ name: "list_devices", arguments: {} }));
  const adb = data(await client.callTool({ name: "adb_devices", arguments: {} }));
  const governor = data(await client.callTool({ name: "resource_governor_status", arguments: {} }));
  const cache = data(await client.callTool({ name: "build_cache_stats", arguments: {} }));
  const screenshot = await client.callTool({ name: "screenshot_capture", arguments: {} });
  const screenshotMeta = data(screenshot);
  const screenshotHasImage = screenshot.content?.some(part => part.type === "image") || false;
  console.log(JSON.stringify({
    serverInfo: {
      name: serverInfo?.name,
      title: serverInfo?.title,
      version: serverInfo?.version,
      iconCount: serverInfo?.icons?.length || 0,
      iconMimeType: serverInfo?.icons?.[0]?.mimeType || null,
      iconSizes: serverInfo?.icons?.[0]?.sizes || null,
      iconIsDataUri: serverInfo?.icons?.[0]?.src?.startsWith("data:image/png;base64,") || false
    },
    toolCount: listed.tools.length,
    tools: listed.tools.map(tool => tool.name),
    version: config?.version,
    root: config?.root,
    shellEnabled: config?.shellEnabled,
    capacity: {
      defaultReadBytes: system?.defaultReadBytes,
      maxReadBytes: system?.maxReadBytes,
      maxMessageBytes: system?.maxMessageBytes,
      defaultProcessLogBytes: system?.defaultProcessLogBytes,
      maxProcessLogBytes: system?.maxProcessLogBytes,
      maxShellOutputBytes: system?.maxShellOutputBytes,
      maxBuildOutputBytes: system?.maxBuildOutputBytes,
      maxGitPatchBytes: system?.maxGitPatchBytes,
      maxProcessReadBytes: system?.maxProcessReadBytes,
      maxBatchFiles: system?.maxBatchFiles,
      batchConcurrency: system?.batchConcurrency,
      maxBatchConcurrency: system?.maxBatchConcurrency,
      maxPatchFiles: system?.maxPatchFiles,
      maxReplacementsPerFile: system?.maxReplacementsPerFile,
      maxWorkflowSteps: system?.maxWorkflowSteps,
      maxProcessSessions: system?.maxProcessSessions,
      maxSearchResults: system?.maxSearchResults
    },
    fileReadLineLimit: config?.fileReadLineLimit,
    fileWriteLineLimit: config?.fileWriteLineLimit,
    federationEnabled: Array.isArray(config?.remoteDevices),
    deviceCount: devices?.length,
    devices,
    native: {
      adbDeviceCount: adb?.devices?.length ?? null,
      adbOnlineCount: adb?.devices?.filter(device => device.state === "device").length ?? null,
      governor,
      buildCacheEntries: cache?.entries ?? null,
      desktopScreenshotBytes: screenshotMeta?.bytes ?? null,
      desktopScreenshotHasImage: screenshotHasImage
    }
  }, null, 2));
} finally {
  await client.close().catch(() => {});
}
