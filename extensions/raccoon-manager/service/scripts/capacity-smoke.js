import fs from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import net from "node:net";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";

const project = process.cwd();
const temp = path.join(project, ".raccoon-capacity-smoke");
let httpPort;
const httpToken = "capacity-smoke-token";
const MB = 1024 * 1024;
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

async function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const address = server.address();
      const port = typeof address === "object" && address ? address.port : null;
      server.close(error => error ? reject(error) : resolve(port));
    });
  });
}

function textPart(result) {
  return result.content?.find(part => part.type === "text")?.text ?? "";
}
function json(result) {
  return JSON.parse(textPart(result));
}
function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function childEnv(extra = {}) {
  return {
    ...process.env,
    RACCOON_ROOT: temp,
    RACCOON_ENABLE_SHELL: "1",
    RACCOON_READ_ONLY: "0",
    RACCOON_PUBLIC_URL: "",
    RACCOON_OAUTH_PASSWORD: "",
    RACCOON_AUDIT_LOG: "",
    RACCOON_SETTINGS_FILE: path.join(temp, "settings-" + Math.random().toString(16).slice(2) + ".json"),
    ...extra
  };
}

async function waitHealth(url) {
  for (let i = 0; i < 100; i++) {
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {}
    await sleep(100);
  }
  throw new Error("HTTP capacity server did not become healthy.");
}

await fs.rm(temp, { recursive: true, force: true });
await fs.mkdir(temp, { recursive: true });

const stdio = new StdioClientTransport({
  command: process.execPath,
  args: ["src/index.js"],
  env: childEnv({ RACCOON_TRANSPORT: "stdio" }),
  cwd: project,
  stderr: "pipe"
});
const client = new Client({ name: "raccoon-capacity-smoke", version: "0.7.0" });

let httpChild;
let httpClient;
try {
  await client.connect(stdio);

  const tools = await client.listTools();
  const names = new Set(tools.tools.map(t => t.name));
  for (const required of ["fs_read_chunk", "fs_write_chunk", "fs_write_many"]) {
    assert(names.has(required), "missing capacity tool: " + required);
  }

  const info = json(await client.callTool({ name: "system_info", arguments: {} }));
  assert(info.maxReadBytes >= 512 * MB, "maxReadBytes below 512 MiB");
  assert(info.maxMessageBytes >= 512 * MB, "maxMessageBytes below 512 MiB");
  assert(info.maxProcessLogBytes >= 2 * 1024 * MB, "maxProcessLogBytes below 2 GiB");
  assert(info.maxBatchConcurrency >= 256, "batch concurrency below 256");
  assert(info.maxProcessSessions >= 4096, "process sessions below 4096");
  assert(info.maxSearchResults >= 1000000, "search result ceiling below 1,000,000");

  // 1 GiB sparse/random-access file: proves whole-file size is decoupled from request size.
  const oneGiBMinusOne = 1024 * MB - 1;
  const sparseWrite = json(await client.callTool({
    name: "fs_write_chunk",
    arguments: { file: "sparse.bin", content: "Z", mode: "at", offset: oneGiBMinusOne }
  }));
  assert(sparseWrite.size === 1024 * MB, "1 GiB sparse write failed");
  const sparseRead = json(await client.callTool({
    name: "fs_read_chunk",
    arguments: { file: "sparse.bin", offset: -16, maxBytes: 16, encoding: "base64" }
  }));
  assert(sparseRead.size === 1024 * MB && sparseRead.eof, "1 GiB sparse read metadata failed");
  assert(Buffer.from(sparseRead.data, "base64").at(-1) === "Z".charCodeAt(0), "1 GiB tail byte mismatch");

  // 128-way-ish file workload (32 workers) for write/read/patch throughput.
  const fileCount = 256;
  const files = Array.from({ length: fileCount }, (_, i) => ({
    file: "batch/f-" + String(i).padStart(3, "0") + ".txt",
    content: "seed-" + i + "\n"
  }));
  const writeMany = json(await client.callTool({
    name: "fs_write_many",
    arguments: { files, concurrency: 256 }
  }));
  assert(writeMany.length === fileCount && writeMany.every(x => x.ok), "fs_write_many concurrency failed");

  const readMany = json(await client.callTool({
    name: "fs_read_many",
    arguments: {
      paths: files.map(x => x.file),
      maxBytesPerFile: 4096,
      maxTotalBytes: 4 * MB,
      concurrency: 256
    }
  }));
  assert(readMany.length === fileCount && readMany.every(x => x.content?.startsWith("seed-")), "fs_read_many concurrency failed");

  const patchMany = json(await client.callTool({
    name: "fs_apply_patch_many",
    arguments: {
      files: files.map((x, i) => ({
        file: x.file,
        replacements: [{ oldText: "seed-" + i, newText: "patched-" + i }]
      })),
      concurrency: 256
    }
  }));
  assert(patchMany.length === fileCount && patchMany.every(x => x.ok), "parallel patch failed");

  // Old search skipped files > 8 MiB. Build a 12 MiB text file and match near the end.
  const largeText = path.join(temp, "large-search.txt");
  const handle = await fs.open(largeText, "w");
  try {
    const block = Buffer.from(("alpha beta gamma delta\n").repeat(4096), "utf8");
    let written = 0;
    while (written < 12 * MB) {
      await handle.write(block);
      written += block.length;
    }
    await handle.write(Buffer.from("CAPACITY_NEEDLE_AT_END\n"));
  } finally {
    await handle.close();
  }
  const search = json(await client.callTool({
    name: "start_search",
    arguments: {
      path: ".",
      pattern: "CAPACITY_NEEDLE_AT_END",
      searchType: "content",
      literalSearch: true,
      filePattern: "large-search.txt",
      maxResults: 10,
      timeout_ms: 60000
    }
  }));
  let searchResult;
  for (let i = 0; i < 100; i++) {
    searchResult = json(await client.callTool({
      name: "get_more_search_results",
      arguments: { sessionId: search.sessionId, offset: 0, length: 10 }
    }));
    if (searchResult.status !== "running") break;
    await sleep(50);
  }
  assert(searchResult.results.some(x => x.text?.includes("CAPACITY_NEEDLE_AT_END")), "streaming search >8 MiB failed");

  const richTail = json(await client.callTool({
    name: "read_file",
    arguments: { path: "large-search.txt", offset: -3 }
  }));
  assert(richTail.text.includes("CAPACITY_NEEDLE_AT_END"), "streaming read_file tail failed");

  const fileInfo = json(await client.callTool({
    name: "get_file_info",
    arguments: { path: "large-search.txt" }
  }));
  assert(fileInfo.size > 8 * MB && fileInfo.lineCount > 1000, "streaming get_file_info failed");

  // Parallel project workflow.
  const parallelSteps = Array.from({ length: 16 }, (_, i) => ({
    name: "p" + i,
    command: "node -e \"process.stdout.write('p" + i + "')\""
  }));
  const workflow = json(await client.callTool({
    name: "project_build_test",
    arguments: {
      cwd: ".",
      steps: parallelSteps,
      parallelism: 256,
      stopOnFailure: true,
      timeoutMsPerStep: 30000,
      maxOutputBytesPerStep: 1024 * 1024
    }
  }));
  assert(workflow.ok && workflow.stepsCompleted === 16 && workflow.parallelism === 256, "parallel workflow failed");

  // Multi-megabyte persistent process output, read via cursor.
  const proc = json(await client.callTool({
    name: "process_start",
    arguments: {
      cwd: ".",
      command: "node -e \"process.stdout.write('X'.repeat(8*1024*1024))\"",
      maxLogBytes: 32 * MB
    }
  }));
  let cursor = 0;
  let processBytes = 0;
  for (let i = 0; i < 100; i++) {
    const state = json(await client.callTool({
      name: "process_read",
      arguments: { sessionId: proc.sessionId, cursor, maxBytes: 4 * MB, waitMs: 1000 }
    }));
    cursor = state.nextCursor;
    processBytes += state.events.reduce((sum, e) => sum + e.bytes, 0);
    if (state.status !== "running" && state.events.length === 0) break;
    if (state.status !== "running" && cursor >= state.nextCursor) {
      const final = json(await client.callTool({
        name: "process_read",
        arguments: { sessionId: proc.sessionId, cursor, maxBytes: 4 * MB, waitMs: 0 }
      }));
      processBytes += final.events.reduce((sum, e) => sum + e.bytes, 0);
      break;
    }
  }
  assert(processBytes >= 8 * MB, "large process log cursor read failed");

  // HTTP single-call request > old 16 MiB limit.
  httpPort = await getFreePort();
  httpChild = spawn(process.execPath, ["src/index.js", "--http"], {
    cwd: project,
    env: childEnv({
      RACCOON_TRANSPORT: "http",
      RACCOON_HOST: "127.0.0.1",
      RACCOON_PORT: String(httpPort),
      RACCOON_TOKEN: httpToken
    }),
    stdio: ["ignore", "ignore", "pipe"],
    windowsHide: true
  });
  await waitHealth("http://127.0.0.1:" + httpPort + "/health");

  const httpTransport = new StreamableHTTPClientTransport(new URL("http://127.0.0.1:" + httpPort + "/mcp"), {
    requestInit: { headers: { Authorization: "Bearer " + httpToken } }
  });
  httpClient = new Client({ name: "raccoon-capacity-http", version: "0.7.0" });
  await httpClient.connect(httpTransport);

  const twentyMiB = "Q".repeat(20 * MB);
  const bigWrite = json(await httpClient.callTool({
    name: "fs_write_text",
    arguments: { file: "http-20m.txt", content: twentyMiB }
  }));
  assert(bigWrite.bytes === 20 * MB, "HTTP >16 MiB single write failed");

  const bigInfo = json(await httpClient.callTool({
    name: "get_file_info",
    arguments: { path: "http-20m.txt" }
  }));
  assert(bigInfo.size === 20 * MB, "HTTP >16 MiB file size mismatch");

  // 64 independent concurrent HTTP MCP calls: simulates multiple chats/agents.
  const httpConcurrent = 64;
  const httpWrites = await Promise.all(Array.from({ length: httpConcurrent }, (_, i) =>
    httpClient.callTool({
      name: "fs_write_text",
      arguments: { file: "http-concurrent/f-" + i + ".txt", content: "http-" + i }
    })
  ));
  assert(httpWrites.every(result => json(result).ok), "concurrent HTTP MCP writes failed");

  // Same-file concurrency must serialize rather than lose appends.
  await httpClient.callTool({
    name: "fs_write_chunk",
    arguments: { file: "shared-append.txt", content: "", mode: "rewrite" }
  });
  const sharedAppends = await Promise.all(Array.from({ length: httpConcurrent }, (_, i) =>
    httpClient.callTool({
      name: "fs_write_chunk",
      arguments: { file: "shared-append.txt", content: "line-" + i + "\n", mode: "append" }
    })
  ));
  assert(sharedAppends.every(result => json(result).ok), "same-file concurrent append calls failed");
  const shared = json(await httpClient.callTool({
    name: "read_file",
    arguments: { path: "shared-append.txt", offset: 0, length: 1000 }
  }));
  const sharedLines = shared.text.split(/\r?\n/).filter(Boolean);
  assert(sharedLines.length === httpConcurrent, "same-file lock lost concurrent appends");

  console.log(JSON.stringify({
    ok: true,
    limits: {
      maxReadBytes: info.maxReadBytes,
      maxMessageBytes: info.maxMessageBytes,
      maxProcessLogBytes: info.maxProcessLogBytes,
      maxShellOutputBytes: info.maxShellOutputBytes,
      maxBuildOutputBytes: info.maxBuildOutputBytes,
      maxGitPatchBytes: info.maxGitPatchBytes,
      maxProcessReadBytes: info.maxProcessReadBytes,
      maxBatchFiles: info.maxBatchFiles,
      maxBatchConcurrency: info.maxBatchConcurrency,
      maxPatchFiles: info.maxPatchFiles,
      maxReplacementsPerFile: info.maxReplacementsPerFile,
      maxProcessSessions: info.maxProcessSessions,
      maxSearchResults: info.maxSearchResults
    },
    proven: {
      sparseFileBytes: sparseWrite.size,
      parallelFiles: fileCount,
      searchFileBytes: fileInfo.size,
      workflowParallelism: workflow.parallelism,
      processOutputBytes: processBytes,
      httpSingleWriteBytes: bigWrite.bytes,
      concurrentHttpCalls: httpConcurrent,
      concurrentSameFileAppends: sharedLines.length
    }
  }, null, 2));
} finally {
  await httpClient?.close().catch(() => {});
  if (httpChild && !httpChild.killed) httpChild.kill();
  await client.close().catch(() => {});
  await fs.rm(temp, { recursive: true, force: true }).catch(() => {});
}
