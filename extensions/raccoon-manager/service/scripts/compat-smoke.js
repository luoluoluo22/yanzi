import fs from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import net from "node:net";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const root = process.cwd();
const work = ".raccoon-compat-smoke";
const absWork = path.join(root, work);
let peerPort;
const peerToken = "raccoon-peer-smoke-token";
const peerId = "peer-smoke";
let peerChild = null;

function textPart(result) {
  return result.content?.find(part => part.type === "text")?.text ?? "";
}
function json(result) {
  if (result.isError) throw new Error(textPart(result));
  return JSON.parse(textPart(result));
}
function assert(condition, message) {
  if (!condition) throw new Error(message);
}
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

async function waitForPeerHealth() {
  for (let i = 0; i < 50; i += 1) {
    try {
      const response = await fetch(`http://127.0.0.1:${peerPort}/health`);
      if (response.ok) return;
    } catch {}
    await sleep(100);
  }
  throw new Error("Federated peer did not become healthy.");
}

const expected = [
  "list_devices","who_am_i","ping","shutdown","get_config","set_config_value",
  "read_file","read_multiple_files","write_file","write_pdf","create_directory",
  "list_directory","move_file","start_search","get_more_search_results","stop_search",
  "list_searches","get_file_info","edit_block","start_process","read_process_output",
  "interact_with_process","force_terminate","list_sessions","list_processes","kill_process",
  "get_usage_stats","get_recent_tool_calls","give_feedback_to_desktop_commander","get_prompts"
];

const transport = new StdioClientTransport({
  command: process.execPath,
  args: ["src/index.js"],
  env: {
    RACCOON_ROOT: root,
    RACCOON_ENABLE_SHELL: "1",
    RACCOON_READ_ONLY: "0",
    RACCOON_TRANSPORT: "stdio",
    RACCOON_NO_OPEN_BROWSER: "1",
    RACCOON_SETTINGS_FILE: path.join(absWork, "main-settings.json")
  },
  cwd: root,
  stderr: "pipe"
});
const client = new Client({ name: "raccoon-compat-smoke", version: "0.7.0" });

try {
  await fs.rm(absWork, { recursive: true, force: true });
  await fs.mkdir(absWork, { recursive: true });

  peerPort = await getFreePort();
  const peerEnv = {
    ...process.env,
    RACCOON_ROOT: root,
    RACCOON_HOST: "127.0.0.1",
    RACCOON_PORT: String(peerPort),
    RACCOON_TOKEN: peerToken,
    RACCOON_ENABLE_SHELL: "1",
    RACCOON_READ_ONLY: "0",
    RACCOON_TRANSPORT: "http",
    RACCOON_PUBLIC_URL: "",
    RACCOON_OAUTH_PASSWORD: "",
    RACCOON_AUDIT_LOG: "",
    RACCOON_NO_OPEN_BROWSER: "1",
    RACCOON_SETTINGS_FILE: path.join(absWork, "peer-settings.json")
  };
  peerChild = spawn(process.execPath, ["src/index.js", "--http"], {
    cwd: root,
    env: peerEnv,
    stdio: ["ignore", "ignore", "pipe"],
    windowsHide: true
  });
  await waitForPeerHealth();

  await client.connect(transport);

  const listed = await client.listTools();
  const toolNames = new Set(listed.tools.map(tool => tool.name));
  const missing = expected.filter(name => !toolNames.has(name));
  assert(missing.length === 0, "Missing compatibility tools: " + missing.join(", "));

  const ping = json(await client.callTool({ name: "ping", arguments: {} }));
  assert(ping.ok, "ping failed");

  await client.callTool({
    name: "set_config_value",
    arguments: {
      key: "remoteDevices",
      value: [{
        id: peerId,
        name: "Raccoon Peer Smoke",
        url: `http://127.0.0.1:${peerPort}/mcp`,
        token: peerToken
      }]
    }
  });
  const devices = json(await client.callTool({ name: "list_devices", arguments: {} }));
  assert(devices.length === 2, "federated list_devices count failed");
  const peer = devices.find(item => item.id === peerId);
  assert(peer?.status === "online", "federated peer status failed");

  const remotePing = json(await client.callTool({
    name: "ping",
    arguments: { deviceId: peerId }
  }));
  assert(remotePing.ok, "federated remote ping failed");
  const remoteRead = json(await client.callTool({
    name: "read_file",
    arguments: { deviceId: peerId, path: "package.json", offset: 0, length: 50 }
  }));
  assert(remoteRead.text.includes("raccoon-mcp"), "federated remote read_file failed");

  const who = json(await client.callTool({ name: "who_am_i", arguments: {} }));
  assert(who.username, "who_am_i failed");
  const config = json(await client.callTool({ name: "get_config", arguments: {} }));
  assert(config.version && config.systemInfo, "get_config failed");
  await client.callTool({ name: "set_config_value", arguments: { key: "fileReadLineLimit", value: 1000 } });

  await client.callTool({ name: "create_directory", arguments: { path: work + "/nested" } });
  await fs.writeFile(
    path.join(absWork, "pixel.png"),
    Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z5WAAAAAASUVORK5CYII=", "base64")
  );
  const imageRead = await client.callTool({ name: "read_file", arguments: { path: work + "/pixel.png" } });
  assert(imageRead.content.some(part => part.type === "image"), "native image read failed");
  const urlRead = json(await client.callTool({
    name: "read_file",
    arguments: { path: "data:text/plain;charset=utf-8,raccoon-url-ok", isUrl: true }
  }));
  assert(urlRead.text.includes("raccoon-url-ok"), "URL read failed");

  await client.callTool({
    name: "write_file",
    arguments: { path: work + "/alpha.txt", content: "line1\nneedle here\nline3\n", mode: "rewrite" }
  });
  const readText = json(await client.callTool({
    name: "read_file",
    arguments: { path: work + "/alpha.txt", offset: 1, length: 1 }
  }));
  assert(readText.text === "needle here", "text range read failed");

  await client.callTool({
    name: "edit_block",
    arguments: {
      file_path: work + "/alpha.txt",
      old_string: "needle here",
      new_string: "needle changed",
      expected_replacements: 1
    }
  });
  const info = json(await client.callTool({ name: "get_file_info", arguments: { path: work + "/alpha.txt" } }));
  assert(info.lineCount >= 3, "get_file_info line count failed");
  await client.callTool({
    name: "move_file",
    arguments: { source: work + "/alpha.txt", destination: work + "/nested/moved.txt" }
  });
  await client.callTool({
    name: "copy_file",
    arguments: { source: work + "/nested/moved.txt", destination: work + "/nested/copied.txt" }
  });
  await client.callTool({ name: "delete_path", arguments: { path: work + "/nested/copied.txt" } });
  const listing = json(await client.callTool({ name: "list_directory", arguments: { path: work, depth: 3 } }));
  assert(listing.entries.some(entry => entry.path.replaceAll("\\", "/") === "nested/moved.txt"), "recursive list/move failed");

  const search = json(await client.callTool({
    name: "start_search",
    arguments: {
      path: work,
      pattern: "needle changed",
      searchType: "content",
      literalSearch: true,
      maxResults: 20
    }
  }));
  await sleep(150);
  const searches = json(await client.callTool({ name: "list_searches", arguments: {} }));
  assert(searches.some(item => item.sessionId === search.sessionId), "list_searches failed");
  const searchResults = json(await client.callTool({
    name: "get_more_search_results",
    arguments: { sessionId: search.sessionId, offset: 0, length: 20 }
  }));
  assert(searchResults.results.some(item => item.text?.includes("needle changed")), "content search failed");
  await client.callTool({ name: "stop_search", arguments: { sessionId: search.sessionId } }).catch(() => {});

  await client.callTool({
    name: "write_file",
    arguments: {
      path: work + "/book.xlsx",
      content: JSON.stringify([["Name","Age"],["Alice",30],["Bob",40]]),
      mode: "rewrite"
    }
  });
  const excel = json(await client.callTool({
    name: "read_file",
    arguments: { path: work + "/book.xlsx", sheet: "0", range: "A1:B3" }
  }));
  assert(excel.data?.[1]?.[0] === "Alice", "Excel read failed");
  await client.callTool({
    name: "edit_block",
    arguments: { file_path: work + "/book.xlsx", range: "Sheet1!B2:B2", content: [[31]] }
  });
  const excelInfo = json(await client.callTool({ name: "get_file_info", arguments: { path: work + "/book.xlsx" } }));
  assert(excelInfo.sheets?.[0]?.rowCount >= 3, "Excel metadata failed");

  await client.callTool({
    name: "write_file",
    arguments: { path: work + "/doc.docx", content: "# Heading\n\nAlpha paragraph", mode: "rewrite" }
  });
  let doc = json(await client.callTool({ name: "read_file", arguments: { path: work + "/doc.docx" } }));
  assert(JSON.stringify(doc).includes("Alpha paragraph"), "DOCX outline failed");
  await client.callTool({
    name: "edit_block",
    arguments: {
      file_path: work + "/doc.docx",
      old_string: "Alpha paragraph",
      new_string: "Beta paragraph",
      expected_replacements: 1
    }
  });
  doc = json(await client.callTool({ name: "read_file", arguments: { path: work + "/doc.docx" } }));
  assert(JSON.stringify(doc).includes("Beta paragraph"), "DOCX edit failed");

  const pdfWrite = json(await client.callTool({
    name: "write_pdf",
    arguments: { path: work + "/sample.pdf", content: "# PDF Test\n\n中文 PDF smoke test." }
  }));
  assert(pdfWrite.ok && pdfWrite.bytes > 0, "PDF creation failed");
  const pdfRead = await client.callTool({
    name: "read_file",
    arguments: { path: work + "/sample.pdf", offset: 0, length: 1, options: { includeImages: true, maxRenderedPages: 1 } }
  });
  assert(textPart(pdfRead).includes("PDF:"), "PDF read failed");
  assert(pdfRead.content.some(part => part.type === "image"), "PDF page image rendering failed");

  const pdfModify = json(await client.callTool({
    name: "write_pdf",
    arguments: {
      path: work + "/sample.pdf",
      outputPath: work + "/modified.pdf",
      content: [
        { type: "insert", pageIndex: 1, markdown: "# Inserted Page\n\nPDF modify smoke test." },
        { type: "delete", pageIndexes: [0] }
      ]
    }
  }));
  assert(pdfModify.ok && pdfModify.outputPath.endsWith("modified.pdf"), "PDF modification failed");
  const modifiedPdfRead = await client.callTool({
    name: "read_file",
    arguments: { path: work + "/modified.pdf", offset: 0, length: 1, options: { includeImages: false } }
  });
  assert(textPart(modifiedPdfRead).includes("PDF:"), "modified PDF read failed");

  const many = await client.callTool({
    name: "read_multiple_files",
    arguments: { paths: [work + "/nested/moved.txt", work + "/sample.pdf"] }
  });
  assert(many.content.length >= 2, "read_multiple_files failed");

  const proc = json(await client.callTool({
    name: "start_process",
    arguments: {
      command: process.platform === "win32"
        ? "$line = [Console]::In.ReadLine(); Write-Output ('echo:' + $line)"
        : "read line; printf 'echo:%s\\n' \"$line\"",
      timeout_ms: 200
    }
  }));
  // Polling before the first output must not consume an imaginary blank line.
  const emptyPoll = json(await client.callTool({ name: "read_process_output", arguments: { pid: proc.pid } }));
  assert(emptyPoll.output === "" && emptyPoll.linesReturned === 0, "empty process output advanced the line cursor");
  let interaction = json(await client.callTool({
    name: "interact_with_process",
    arguments: { pid: proc.pid, input: "compat-ok", timeout_ms: 2000 }
  }));
  for (let attempt = 0; !interaction.output.includes("echo:compat-ok") && attempt < 30 && !interaction.isComplete; attempt++) {
    interaction = json(await client.callTool({ name: "read_process_output", arguments: { pid: proc.pid, timeout_ms: 1000 } }));
  }
  assert(interaction.output.includes("echo:compat-ok"), "interactive process failed: " + JSON.stringify(interaction));

  const cmdProc = json(await client.callTool({
    name: "start_process",
    arguments: {
      command: process.platform === "win32" ? "echo cmd-shell-ok" : "printf 'shell-ok\\n'",
      shell: process.platform === "win32" ? "cmd.exe" : undefined,
      timeout_ms: 2000
    }
  }));
  const cmdOut = json(await client.callTool({
    name: "read_process_output",
    arguments: { pid: cmdProc.pid, offset: -20, length: 20 }
  }));
  assert(cmdOut.output.includes(process.platform === "win32" ? "cmd-shell-ok" : "shell-ok"), "shell override/process output failed");

  const managedLong = json(await client.callTool({
    name: "start_process",
    arguments: {
      command: process.platform === "win32" ? "ping 127.0.0.1 -t" : "sleep 30",
      timeout_ms: 100
    }
  }));
  await client.callTool({ name: "force_terminate", arguments: { pid: managedLong.pid } });

  const systemKillLong = json(await client.callTool({
    name: "start_process",
    arguments: {
      command: process.platform === "win32" ? "ping 127.0.0.1 -t" : "sleep 30",
      timeout_ms: 100
    }
  }));
  await client.callTool({ name: "kill_process", arguments: { pid: systemKillLong.pid } });

  const sessions = json(await client.callTool({ name: "list_sessions", arguments: {} }));
  assert(Array.isArray(sessions), "list_sessions failed");
  const processes = json(await client.callTool({ name: "list_processes", arguments: {} }));
  assert(Array.isArray(processes) && processes.length > 0, "list_processes failed");

  const usage = json(await client.callTool({ name: "get_usage_stats", arguments: {} }));
  assert(usage.totalToolCalls > 0, "usage stats failed");
  const recent = json(await client.callTool({ name: "get_recent_tool_calls", arguments: { maxResults: 10 } }));
  assert(Array.isArray(recent) && recent.length > 0, "recent calls failed");
  const prompt = json(await client.callTool({
    name: "get_prompts",
    arguments: { action: "get_prompt", promptId: "onb2_02" }
  }));
  assert(prompt.prompt?.includes("codebase"), "get_prompts failed");
  const feedback = json(await client.callTool({
    name: "give_feedback_to_desktop_commander",
    arguments: {}
  }));
  assert(feedback.ok && feedback.browserOpened === false, "feedback compatibility action failed");

  const shutdown = json(await client.callTool({ name: "shutdown", arguments: {} }));
  assert(shutdown.ok, "shutdown failed");
  await sleep(250);

  console.log(JSON.stringify({
    ok: true,
    expectedToolCount: expected.length,
    actualToolCount: listed.tools.length,
    missing,
    tested: {
      deviceAndConfig: true,
      multiDeviceFederation: true,
      textFiles: true,
      recursiveListingAndMove: true,
      search: true,
      excel: true,
      docx: true,
      imageAndUrlRead: true,
      pdfCreateReadRender: true,
      pdfModify: true,
      multiRead: true,
      interactiveProcess: true,
      shellOverride: true,
      forceTerminate: true,
      systemProcessKill: true,
      sessionsAndProcesses: true,
      usageAndHistory: true,
      prompts: true,
      feedback: true,
      shutdown: true
    }
  }, null, 2));
} finally {
  await client.close().catch(() => {});
  if (peerChild && !peerChild.killed) peerChild.kill();
  await fs.rm(absWork, { recursive: true, force: true }).catch(() => {});
}
