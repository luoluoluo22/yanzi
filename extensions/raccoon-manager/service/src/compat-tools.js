import os from "node:os";
import fs from "node:fs/promises";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { z } from "zod";
import { config, insideRoot } from "./config.js";
import {assertOrdinaryWriteAllowed} from './source-integrity.js';
import { version } from "./version.js";
import { getRuntimeSettings, setRuntimeSetting, runtimeSettingsPath, getRuntimeSetting } from "./runtime-settings.js";
import { getUsageStats, getRecentToolCalls } from "./telemetry.js";
import { localDeviceId, listFederatedDevices, routeDeviceTool } from "./peer-manager.js";
import { startSearch, getSearchResults, stopSearch, listSearches } from "./search-manager.js";
import { readRichFile, readManyRich, writeRichFile, writePdf, getFileInfo, editRichBlock } from "./file-tools.js";
import {
  startManagedProcess,
  listManagedProcesses,
  getManagedProcessSnapshot,
  writeManagedProcessByPid,
  killManagedProcessByPid,
  waitManagedProcessByPid,
  shellCommand
} from "./process-manager.js";

const execFileAsync = promisify(execFile);
const processReadOffsets = new Map();

function transcriptLines(transcript) {
  // An empty transcript is not a blank output line. Advancing the cursor here
  // would discard the first real line when a cold shell eventually starts.
  return transcript ? transcript.replaceAll("\r\n", "\n").split("\n") : [];
}

function text(value) {
  return {
    content: [{
      type: "text",
      text: typeof value === "string" ? value : JSON.stringify(value, null, 2)
    }]
  };
}

function processState(snapshot) {
  const transcript = snapshot.transcript || "";
  const blocked = snapshot.status === "running" && /(?:>>> |\.\.\. |> |\$ |# )$/m.test(transcript);
  return { blocked, running: snapshot.status === "running", status: snapshot.status };
}

async function listDirectoryRecursive(root, depth, maxEntriesPerDirectory) {
  const out = [];
  async function visit(dir, prefix, level) {
    let entries;
    try {
      entries = await fs.readdir(dir, { withFileTypes: true });
    } catch (error) {
      out.push({ type: "denied", path: prefix || ".", error: error.message });
      return;
    }

    const visible = entries.slice(0, maxEntriesPerDirectory);
    for (const entry of visible) {
      const rel = prefix ? path.join(prefix, entry.name) : entry.name;
      out.push({
        type: entry.isDirectory() ? "directory" : entry.isFile() ? "file" : "other",
        path: rel
      });
      if (entry.isDirectory() && level + 1 < depth) {
        await visit(path.join(dir, entry.name), rel, level + 1);
      }
    }
    if (level > 0 && entries.length > visible.length) {
      out.push({
        type: "warning",
        path: prefix || ".",
        hiddenCount: entries.length - visible.length,
        totalCount: entries.length
      });
    }
  }
  await visit(root, "", 0);
  return out;
}

async function systemProcesses() {
  if (process.platform === "win32") {
    const command = [
      "$ErrorActionPreference='SilentlyContinue'",
      "$items = Get-Process | Select-Object Id,ProcessName,CPU,WorkingSet64,Path,StartTime",
      "$items | ConvertTo-Json -Depth 3 -Compress"
    ].join("; ");
    const shell = shellCommand(command);
    const { stdout } = await execFileAsync(shell.executable, shell.args, {
      windowsHide: true,
      timeout: 30000,
      maxBuffer: 16 * 1024 * 1024,
      encoding: "utf8"
    });
    const parsed = stdout.trim() ? JSON.parse(stdout) : [];
    const rows = Array.isArray(parsed) ? parsed : [parsed];
    return rows.map(item => ({
      pid: item.Id,
      command: item.ProcessName,
      cpuSeconds: item.CPU ?? null,
      memoryBytes: item.WorkingSet64 ?? null,
      executablePath: item.Path ?? null,
      startedAt: item.StartTime ?? null
    }));
  }

  const { stdout } = await execFileAsync("ps", ["-eo", "pid,comm,%cpu,rss,args"], {
    timeout: 30000,
    maxBuffer: 16 * 1024 * 1024,
    encoding: "utf8"
  });
  return stdout.split(/\r?\n/).slice(1).filter(Boolean).map(line => ({ raw: line }));
}

function onboardingPrompt(id) {
  const prompts = {
    onb2_01: "Organize a chosen workspace folder: inventory files, identify duplicates/stale artifacts, propose a safe organization plan, then carry it out after the user selects the target.",
    onb2_02: "Explain a codebase or repository: inspect structure, entry points, dependencies, build/test workflow, major subsystems, and produce a concise architecture map grounded in the actual files.",
    onb2_03: "Create an organized local knowledge base: inventory source material, group it by topic and lifecycle, create a navigable structure, and preserve provenance.",
    onb2_04: "Analyze a local data file: inspect schema and quality, compute descriptive statistics, identify anomalies and useful patterns, and summarize actionable findings.",
    onb2_05: "Check system health and resources: inspect CPU, memory, disk, important processes, development runtimes, and report notable bottlenecks or failures."
  };
  if (!prompts[id]) throw new Error(`Unknown promptId: ${id}`);
  return prompts[id];
}

export function registerCompatibilityTools(server) {
  const compat = (name, definition, handler) => server.registerTool(name, {
    ...definition,
    inputSchema: {
      deviceId: z.string().optional(),
      ...(definition.inputSchema || {})
    }
  }, args => routeDeviceTool(name, args, handler));

  compat("list_devices", {
    title: "List devices",
    description: "List the local Raccoon node and configured federated Raccoon peers with online/offline state."
  }, async () => text(await listFederatedDevices()));

  compat("who_am_i", {
    title: "Who am I",
    description: "Return the operating-system identity running Raccoon and local tool usage."
  }, async () => {
    const user = os.userInfo();
    return text({
      username: user.username,
      homedir: user.homedir,
      hostname: os.hostname(),
      deviceId: localDeviceId(),
      usage: getUsageStats(),
      remoteQuota: null
    });
  });

  compat("shutdown", {
    title: "Shutdown Raccoon",
    description: "Gracefully request shutdown of the current Raccoon server."
  }, async () => {
    setTimeout(() => {
      try { process.kill(process.pid, "SIGTERM"); } catch { process.exit(0); }
    }, 100).unref();
    return text({ ok: true, message: "Raccoon shutdown requested." });
  });

  compat("get_config", {
    title: "Get configuration",
    description: "Get Raccoon runtime, host, workspace and compatibility configuration."
  }, async () => text({
    version,
    deviceId: localDeviceId(),
    root: config.root,
    transport: config.transport,
    host: config.host,
    port: config.port,
    shellEnabled: config.shellEnabled,
    filesystemUnrestricted: config.filesystemUnrestricted,
    readOnly: config.readOnly,
    maxReadBytes: config.maxReadBytes,
    maxMessageBytes: config.maxMessageBytes,
    maxProcessLogBytes: config.maxProcessLogBytes,
    settingsFile: runtimeSettingsPath(),
    ...getRuntimeSettings(),
    systemInfo: {
      hostname: os.hostname(),
      platform: process.platform,
      arch: process.arch,
      release: os.release(),
      node: process.version,
      cpus: os.cpus().length,
      totalMemoryBytes: os.totalmem(),
      freeMemoryBytes: os.freemem()
    }
  }));

  compat("set_config_value", {
    title: "Set configuration value",
    description: "Set a mutable Raccoon compatibility setting.",
    inputSchema: {
      key: z.string().min(1),
      value: z.any()
    }
  }, async ({ key, value }) => text({
    ok: true,
    settings: await setRuntimeSetting(key, value)
  }));

  compat("read_file", {
    title: "Read file or URL",
    description: "Read text with line pagination, Excel ranges, images, PDF pages, DOCX outline/XML, or a URL.",
    inputSchema: {
      path: z.string().min(1),
      isUrl: z.boolean().default(false),
      offset: z.number().int().optional(),
      length: z.number().int().positive().optional(),
      sheet: z.string().optional(),
      range: z.string().optional(),
      options: z.any().optional()
    }
  }, readRichFile);

  compat("read_multiple_files", {
    title: "Read multiple files",
    description: "Read multiple local files concurrently, including viewable images, without failing the whole batch.",
    inputSchema: {
      paths: z.array(z.string()).min(1).max(config.maxBatchFiles),
      concurrency: z.number().int().min(1).max(config.maxBatchConcurrency).default(config.batchConcurrency)
    }
  }, async ({ paths, concurrency }) => readManyRich(paths, concurrency));

  compat("write_file", {
    title: "Write file",
    description: "Write or append text; create Excel workbooks from JSON arrays; create DOCX from markdown-like text.",
    inputSchema: {
      path: z.string().min(1),
      content: z.string(),
      mode: z.enum(["rewrite", "append"]).default("rewrite")
    }
  }, async args => text(await writeRichFile(args)));

  compat("write_pdf", {
    title: "Write PDF",
    description: "Create a PDF from markdown/HTML/CSS/SVG or modify an existing PDF with insert/delete operations.",
    inputSchema: {
      path: z.string().min(1),
      content: z.union([z.string(), z.array(z.any())]),
      outputPath: z.string().optional(),
      options: z.any().optional()
    }
  }, async args => text(await writePdf(args)));

  compat("create_directory", {
    title: "Create directory",
    description: "Create a directory recursively.",
    inputSchema: { path: z.string().min(1) }
  }, async ({ path: input }) => {
    const target = insideRoot(input);
    await fs.mkdir(target, { recursive: true });
    return text({ ok: true, path: target });
  });

  compat("list_directory", {
    title: "List directory",
    description: "List a directory recursively with depth control and nested overflow protection.",
    inputSchema: {
      path: z.string().default("."),
      depth: z.number().int().min(1).max(config.maxTreeDepth).default(2),
      maxEntriesPerDirectory: z.number().int().min(1).max(config.maxDirectoryEntries).default(Math.min(1000, config.maxDirectoryEntries))
    }
  }, async ({ path: input, depth, maxEntriesPerDirectory }) => {
    const target = insideRoot(input);
    return text({
      root: target,
      depth,
      maxEntriesPerDirectory,
      entries: await listDirectoryRecursive(target, depth, maxEntriesPerDirectory)
    });
  });

  compat("move_file", {
    title: "Move file",
    description: "Move or rename a file or directory.",
    inputSchema: {
      source: z.string().min(1),
      destination: z.string().min(1)
    }
  }, async ({ source, destination }) => {
    const src = insideRoot(source);
    const dst = insideRoot(destination);
    assertOrdinaryWriteAllowed(dst);
    await fs.mkdir(path.dirname(dst), { recursive: true });
    await fs.rename(src, dst);
    return text({ ok: true, source: src, destination: dst });
  });

  compat("copy_file", {
    title: "Copy file or directory",
    description: "Copy a file or directory recursively.",
    inputSchema: {
      source: z.string().min(1),
      destination: z.string().min(1)
    }
  }, async ({ source, destination }) => {
    const src = insideRoot(source);
    const dst = insideRoot(destination);
    assertOrdinaryWriteAllowed(dst);
    await fs.mkdir(path.dirname(dst), { recursive: true });
    await fs.cp(src, dst, { recursive: true, force: true });
    return text({ ok: true, source: src, destination: dst });
  });

  compat("delete_path", {
    title: "Delete path",
    description: "Delete a file or directory recursively.",
    inputSchema: {
      path: z.string().min(1),
      recursive: z.boolean().default(false)
    }
  }, async ({ path: input, recursive }) => {
    const target = insideRoot(input);
    const stat = await fs.stat(target);
    if (stat.isDirectory()) await fs.rm(target, { recursive, force: false });
    else await fs.unlink(target);
    return text({ ok: true, path: target });
  });

  compat("start_search", {
    title: "Start search",
    description: "Start a progressive file-name or file-content search.",
    inputSchema: {
      path: z.string().default("."),
      pattern: z.string(),
      searchType: z.enum(["files", "content"]).default("files"),
      literalSearch: z.boolean().default(false),
      filePattern: z.string().optional(),
      ignoreCase: z.boolean().default(true),
      includeHidden: z.boolean().default(false),
      earlyTermination: z.boolean().optional(),
      maxResults: z.number().int().min(1).max(config.maxSearchResults).optional(),
      timeout_ms: z.number().int().positive().optional(),
      contextLines: z.number().int().min(0).max(config.maxSearchContextLines).default(5)
    }
  }, async args => text(startSearch(args)));

  compat("get_more_search_results", {
    title: "Get search results",
    description: "Read a range of results from a progressive search.",
    inputSchema: {
      sessionId: z.string(),
      offset: z.number().int().default(0),
      length: z.number().int().min(1).max(config.maxSearchResults).default(100)
    }
  }, async args => text(getSearchResults(args)));

  compat("stop_search", {
    title: "Stop search",
    description: "Stop a progressive search while retaining its results briefly.",
    inputSchema: { sessionId: z.string() }
  }, async ({ sessionId }) => text(stopSearch(sessionId)));

  compat("list_searches", {
    title: "List searches",
    description: "List retained progressive search sessions."
  }, async () => text(listSearches()));

  compat("get_file_info", {
    title: "Get file info",
    description: "Get file metadata, text line counts, and Excel sheet dimensions.",
    inputSchema: { path: z.string().min(1) }
  }, async ({ path: input }) => text(await getFileInfo(input)));

  compat("edit_block", {
    title: "Edit block",
    description: "Perform exact text replacement, DOCX XML replacement, or Excel range update.",
    inputSchema: {
      file_path: z.string().min(1),
      old_string: z.string().optional(),
      new_string: z.string().optional(),
      expected_replacements: z.number().int().positive().optional(),
      range: z.string().optional(),
      content: z.any().optional()
    }
  }, async args => text(await editRichBlock(args)));

  compat("start_process", {
    title: "Start process",
    description: "Start a persistent process/REPL and return its PID, initial output and state.",
    inputSchema: {
      command: z.string().min(1),
      timeout_ms: z.number().int().min(0).max(24 * 60 * 60 * 1000).default(8000),
      verbose_timing: z.boolean().default(false),
      shell: z.string().optional(),
      cwd: z.string().default(".")
    }
  }, async ({ command, timeout_ms, shell, cwd, verbose_timing }) => {
    const translated = command === "node:local" ? "node -i" : command;
    const startedAt = Date.now();
    const session = startManagedProcess({
      command: translated,
      cwd: insideRoot(cwd),
      maxLogBytes: config.defaultProcessLogBytes,
      shell
    });
    const first = await waitManagedProcessByPid({
      pid: session.pid,
      cursor: 0,
      maxBytes: Math.min(config.maxProcessReadBytes, 8 * 1024 * 1024),
      waitMs: Math.min(timeout_ms, 10000)
    });
    const snapshot = getManagedProcessSnapshot(session.pid);
    const state = processState(snapshot);
    return text({
      pid: session.pid,
      sessionId: session.sessionId,
      command: translated,
      ...state,
      output: first.events.map(event => event.text).join(""),
      ...(verbose_timing ? { timing: { totalMs: Date.now() - startedAt, firstEventCount: first.events.length } } : {})
    });
  });

  compat("read_process_output", {
    title: "Read process output",
    description: "Read managed process output with new-output, absolute and tail line pagination.",
    inputSchema: {
      pid: z.number().int().positive(),
      timeout_ms: z.number().int().min(0).max(10000).default(0),
      offset: z.number().int().default(0),
      length: z.number().int().positive().optional(),
      verbose_timing: z.boolean().default(false)
    }
  }, async ({ pid, timeout_ms, offset, length, verbose_timing }) => {
    const startedAt = Date.now();
    let snapshot = getManagedProcessSnapshot(pid);
    let lines = transcriptLines(snapshot.transcript);

    if (offset === 0) {
      let start = processReadOffsets.get(pid) || 0;
      if (start >= lines.length - 1 && snapshot.status === "running" && timeout_ms > 0) {
        await waitManagedProcessByPid({
          pid,
          cursor: snapshot.nextCursor,
          maxBytes: Math.min(config.maxProcessReadBytes, 8 * 1024 * 1024),
          waitMs: timeout_ms
        });
        snapshot = getManagedProcessSnapshot(pid);
        lines = transcriptLines(snapshot.transcript);
      }
      const count = length || getRuntimeSetting("fileReadLineLimit");
      const selected = lines.slice(start, start + count);
      processReadOffsets.set(pid, start + selected.length);
      return text({
        pid,
        ...processState(snapshot),
        startLine: start,
        totalLines: lines.length,
        linesReturned: selected.length,
        output: selected.join("\n"),
        ...(verbose_timing ? { timing: { totalMs: Date.now() - startedAt } } : {})
      });
    }

    const count = length || getRuntimeSetting("fileReadLineLimit");
    const start = offset < 0 ? Math.max(0, lines.length + offset) : Math.min(offset, lines.length);
    const selected = lines.slice(start, start + (offset < 0 && length == null ? -offset : count));
    return text({
      pid,
      ...processState(snapshot),
      startLine: start,
      totalLines: lines.length,
      linesReturned: selected.length,
      output: selected.join("\n"),
      ...(verbose_timing ? { timing: { totalMs: Date.now() - startedAt } } : {})
    });
  });

  compat("interact_with_process", {
    title: "Interact with process",
    description: "Send input to a managed process/REPL and wait for its response.",
    inputSchema: {
      pid: z.number().int().positive(),
      input: z.string(),
      timeout_ms: z.number().int().min(0).max(10000).default(8000),
      wait_for_prompt: z.boolean().default(true),
      verbose_timing: z.boolean().default(false)
    }
  }, async ({ pid, input, timeout_ms, wait_for_prompt, verbose_timing }) => {
    const startedAt = Date.now();
    const before = getManagedProcessSnapshot(pid);
    writeManagedProcessByPid({ pid, input, appendNewline: true });
    const response = await waitManagedProcessByPid({
      pid,
      cursor: before.nextCursor,
      maxBytes: Math.min(config.maxProcessReadBytes, 8 * 1024 * 1024),
      waitMs: wait_for_prompt ? timeout_ms : 0
    });
    const snapshot = getManagedProcessSnapshot(pid);
    return text({
      pid,
      ...processState(snapshot),
      output: response.events.map(event => event.text).join(""),
      ...(verbose_timing ? { timing: { totalMs: Date.now() - startedAt } } : {})
    });
  });

  compat("force_terminate", {
    title: "Force terminate managed process",
    description: "Terminate a persistent process started through Raccoon.",
    inputSchema: { pid: z.number().int().positive() }
  }, async ({ pid }) => text(killManagedProcessByPid(pid)));

  compat("list_sessions", {
    title: "List terminal sessions",
    description: "List retained Raccoon process sessions."
  }, async () => text(listManagedProcesses().map(item => {
    let blocked = false;
    try { blocked = processState(getManagedProcessSnapshot(item.pid)).blocked; } catch {}
    return { ...item, blocked };
  })));

  compat("list_processes", {
    title: "List system processes",
    description: "List all operating-system processes with PID, CPU and memory details."
  }, async () => text(await systemProcesses()));

  compat("kill_process", {
    title: "Kill system process",
    description: "Forcefully terminate an operating-system process by PID.",
    inputSchema: { pid: z.number().int().positive() }
  }, async ({ pid }) => {
    if (process.platform === "win32") {
      await execFileAsync("taskkill.exe", ["/PID", String(pid), "/T", "/F"], {
        windowsHide: true,
        timeout: 30000,
        encoding: "utf8"
      });
    } else {
      process.kill(pid, "SIGKILL");
    }
    return text({ ok: true, pid });
  });

  compat("get_usage_stats", {
    title: "Get usage stats",
    description: "Get Raccoon tool usage, success/failure and performance statistics."
  }, async () => text(getUsageStats()));

  compat("get_recent_tool_calls", {
    title: "Get recent tool calls",
    description: "Get recent in-memory Raccoon tool calls with arguments, results, status and duration.",
    inputSchema: {
      maxResults: z.number().int().min(1).max(config.maxRecentToolCalls).default(50),
      toolName: z.string().optional(),
      since: z.string().optional()
    }
  }, async args => text(getRecentToolCalls(args)));

  compat("give_feedback_to_raccoon", {
    title: "Give feedback to Raccoon",
    description: "Open the Raccoon GitHub issue form in the default browser."
  }, async () => {
    const url = "https://github.com/luoluoluo22/raccoon-mcp/issues/new";
    if (process.platform === "win32" && process.env.RACCOON_NO_OPEN_BROWSER !== "1") {
      execFile("cmd.exe", ["/c", "start", "", url], { windowsHide: true }, () => {});
    }
    return text({ ok: true, url, browserOpened: process.env.RACCOON_NO_OPEN_BROWSER !== "1" });
  });

  compat("give_feedback_to_desktop_commander", {
    title: "Compatibility feedback action",
    description: "Compatibility alias that opens the Raccoon feedback page."
  }, async () => {
    const url = "https://github.com/luoluoluo22/raccoon-mcp/issues/new";
    if (process.platform === "win32" && process.env.RACCOON_NO_OPEN_BROWSER !== "1") {
      execFile("cmd.exe", ["/c", "start", "", url], { windowsHide: true }, () => {});
    }
    return text({
      ok: true,
      product: "raccoon-mcp",
      url,
      browserOpened: process.env.RACCOON_NO_OPEN_BROWSER !== "1"
    });
  });

  compat("get_prompts", {
    title: "Get onboarding prompt",
    description: "Retrieve one of Raccoon's five built-in onboarding prompts.",
    inputSchema: {
      action: z.literal("get_prompt"),
      promptId: z.string()
    }
  }, async ({ promptId }) => text({
    promptId,
    prompt: onboardingPrompt(promptId)
  }));
}
