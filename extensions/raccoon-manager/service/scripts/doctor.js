import { config } from "../src/config.js";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";

const endpoint = new URL(`http://${config.host.includes(":") ? `[${config.host}]` : config.host}:${config.port}/mcp`);
const health = await fetch(new URL("/health", endpoint), { signal: AbortSignal.timeout(5000) });
if (!health.ok) throw new Error(`Health failed: ${health.status}`);
const client = new Client({ name: "raccoon-doctor", version: "1" });
try {
  await client.connect(new StreamableHTTPClientTransport(endpoint, { requestInit: { headers: config.token ? { Authorization: `Bearer ${config.token}` } : {} } }));
  const tools = await client.listTools();
  const ping = await client.callTool({ name: "ping", arguments: {} });
  if (ping.isError) throw new Error("Ping failed");
  console.log(JSON.stringify({ ok: true, health: await health.json(), endpoint: endpoint.href, toolCount: tools.tools.length }, null, 2));
} finally { await client.close(); }
