import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { config } from "./config.js";
import { createRaccoonServer } from "./server.js";
import { createHttpApp } from "./http.js";
import { shutdownManagedProcesses } from "./process-manager.js";
import { shutdownPipelines } from "./dev-pipeline.js";
import { initializeForegroundControl, shutdownForegroundControl } from "./foreground-control.js";

await initializeForegroundControl();

let close;
if (config.transport === "http") {
  const listener = createHttpApp().listen(config.port, config.host, () => {
    console.error(`Raccoon listening on http://${config.host}:${config.port}/mcp`);
  });
  listener.on("error", error => { console.error(error.message); process.exit(1); });
  listener.requestTimeout = config.httpRequestTimeoutMs;
  listener.headersTimeout = config.httpHeadersTimeoutMs;
  close = () => new Promise(resolve => listener.close(resolve));
} else if (config.transport === "stdio") {
  const server = createRaccoonServer();
  await server.connect(new StdioServerTransport(undefined, undefined, { maxBufferSize: config.maxMessageBytes }));
  close = () => server.close();
  process.stdin.once("end", shutdown);
} else {
  throw new Error(`Unsupported RACCOON_TRANSPORT: ${config.transport}`);
}

let stopping = false;
async function shutdown() {
  if (stopping) return;
  stopping = true;
  const deadline = setTimeout(() => process.exit(1), 5000);
  deadline.unref();
  await shutdownPipelines();
  await shutdownManagedProcesses();
  await shutdownForegroundControl();
  await close();
  process.exit(0);
}
process.once("SIGTERM", shutdown);
process.once("SIGINT", shutdown);
