import { spawn } from "node:child_process";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";

const port = 39766;
const token = "raccoon-smoke-token";
const base = `http://127.0.0.1:${port}`;

function data(result) {
  const item = result.content?.find(part => part.type === "text");
  if (!item) throw new Error("Tool returned no text content");
  return JSON.parse(item.text);
}

const childEnv = { ...process.env };
for (const key of Object.keys(childEnv)) {
  if (key.toLowerCase().startsWith("npm_")) delete childEnv[key];
}
Object.assign(childEnv, {
  RACCOON_ROOT: process.cwd(),
  RACCOON_HOST: "127.0.0.1",
  RACCOON_MAX_MESSAGE_BYTES: "16777216",
  RACCOON_PORT: String(port),
  RACCOON_TOKEN: token,
  RACCOON_ENABLE_SHELL: "1", RACCOON_READ_ONLY: "0", RACCOON_TRANSPORT: "stdio"
});

const child = spawn(process.execPath, ["src/index.js", "--http"], {
  cwd: process.cwd(),
  env: childEnv,
  stdio: ["ignore", "ignore", "pipe"],
  windowsHide: true
});

async function waitForHealth() {
  for (let i = 0; i < 50; i += 1) {
    try {
      const response = await fetch(base + "/health");
      if (response.ok) return response.json();
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error("HTTP server did not become healthy");
}

try {
  const health = await waitForHealth();
  const transport = new StreamableHTTPClientTransport(new URL(base + "/mcp"), {
    requestInit: {
      headers: { Authorization: `Bearer ${token}` }
    }
  });

  const client = new Client({ name: "raccoon-http-smoke", version: "0.7.0" });
  await client.connect(transport);

  const tools = await client.listTools();
  const ping = data(await client.callTool({ name: "ping", arguments: {} }));

  const started = data(await client.callTool({
    name: "process_start",
    arguments: {
      cwd: ".",
      command: process.platform === "win32" ? "Write-Output 'http-one'; Start-Sleep -Milliseconds 150; Write-Output 'http-two'" : "printf 'http-one\n'; sleep 0.15; printf 'http-two\n'"
    }
  }));

  let cursor = 0;
  let output = "";
  let processState = null;
  for (let i = 0; i < 30; i += 1) {
    processState = data(await client.callTool({
      name: "process_read",
      arguments: {
        sessionId: started.sessionId,
        cursor,
        waitMs: 1000
      }
    }));
    cursor = processState.nextCursor;
    output += processState.events.map(event => event.text).join("");
    if (processState.status !== "running") break;
  }

  if (!output.includes("http-one") || !output.includes("http-two")) {
    throw new Error("HTTP process state did not survive across MCP requests");
  }

  console.log(JSON.stringify({
    health,
    toolCount: tools.tools.length,
    ping,
    persistentProcessAcrossHttpRequests: {
      sessionId: started.sessionId,
      status: processState.status,
      output: output.trim()
    }
  }, null, 2));

  await client.close();
} finally {
  child.kill();
}
