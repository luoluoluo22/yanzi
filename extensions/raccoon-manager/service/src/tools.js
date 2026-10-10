import os from "node:os";
import path from "node:path";
import fs from "node:fs/promises";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { z } from "zod";
import { config, insideRoot } from "./config.js";
import { assertOrdinaryWriteAllowed, isProtectedSource, decodeSource } from './source-integrity.js';
import { withFileLock, fileLockCount } from "./state.js";
import {
  startManagedProcess,
  readManagedProcess,
  writeManagedProcess,
  killManagedProcess,
  listManagedProcesses,
  shellCommand
} from "./process-manager.js";
import { registerCompatibilityTools } from "./compat-tools.js";
import { recordToolCall } from "./telemetry.js";
import { version } from "./version.js";
import { callPeerToolByDeviceId, localDeviceId, resolveDeviceSelector } from "./peer-manager.js";
import { refreshAccountDevices } from "./yanzi-federation.js";
import {
  adbDevices, adbShell, adbPush, adbPull, adbInstall, adbUninstall,
  adbScreenshot, adbInput, adbApp, adbPackages, adbLogcatStart,
  adbUiDump, adbFindAndTap, androidAppSnapshot
} from "./adb-tools.js";
import { captureDesktop, compareImages } from "./visual-tools.js";
import { dbInfo, dbQueryStart, dbQueryNext, dbQueryClose, dbQuerySessions, dbExecute } from "./db-tools.js";
import { buildCacheRun, buildCacheStats, buildCacheClear } from "./build-cache.js";
import { registerPipelineTools } from "./dev-pipeline.js";
import { runnerExecutable } from "./pipeline-runtime.js";
import { registerFsTransactionTool } from "./fs-transaction.js";
import { registerGitScopeTool } from "./git-scope.js";
import { registerWorktreeTaskTool } from "./worktree-tools.js";
import { registerHttpProbeTools } from "./http-probe.js";
import { registerAndroidWorkflowTool } from "./android-workflow.js";
import { registerCloudflareWorkflowTool } from "./cloudflare-workflow.js";
import { registerForegroundControlTools } from "./foreground-control.js";
import { registerDesktopAutomationTools } from "./desktop-automation.js";
import {
  withHeavyPermit, configureGovernor, governorStatus, systemResourceSnapshot,
  processResourceSnapshot, processWatchStart, processWatchRead, processWatchStop, processWatchList
} from "./resource-governor.js";

const execFileAsync = promisify(execFile);

function text(value) {
  return {
    content: [{
      type: "text",
      text: typeof value === "string" ? value : JSON.stringify(value, null, 2)
    }]
  };
}

function clip(value, maxBytes) {
  const buf = Buffer.from(value || "", "utf8");
  if (buf.length <= maxBytes) return { text: value || "", clipped: false };
  return {
    text: buf.subarray(0, maxBytes).toString("utf8"),
    clipped: true
  };
}

async function runShell(command, cwd, timeoutMs, maxOutputBytes, shellOverride) {
  const started = Date.now();
  try {
    const shell = shellCommand(command, shellOverride);
    const { stdout, stderr } = await withHeavyPermit(() => execFileAsync(shell.executable, shell.args, {
      cwd,
      timeout: timeoutMs,
      windowsHide: true,
      maxBuffer: maxOutputBytes
    }));
    return {
      ok: true,
      exitCode: 0,
      durationMs: Date.now() - started,
      stdout: clip(stdout, maxOutputBytes),
      stderr: clip(stderr, maxOutputBytes)
    };
  } catch (error) {
    return {
      ok: false,
      exitCode: typeof error?.code === "number" ? error.code : null,
      signal: error?.signal ?? null,
      killed: Boolean(error?.killed),
      durationMs: Date.now() - started,
      stdout: clip(error?.stdout || "", maxOutputBytes),
      stderr: clip(error?.stderr || String(error?.message || error), maxOutputBytes)
    };
  }
}

function replaceOnce(content, oldText, newText) {
  const index = content.indexOf(oldText);
  if (index < 0) return null;
  return content.slice(0, index) + newText + content.slice(index + oldText.length);
}

function countOccurrences(content, needle) {
  if (!needle) return 0;
  let count = 0;
  let offset = 0;
  while (true) {
    const index = content.indexOf(needle, offset);
    if (index < 0) return count;
    count += 1;
    offset = index + needle.length;
  }
}

async function mapLimit(items, concurrency, fn) {
  const results = new Array(items.length);
  let next = 0;
  const workers = Array.from({ length: Math.min(concurrency, items.length) }, async () => {
    while (true) {
      const index = next++;
      if (index >= items.length) return;
      results[index] = await fn(items[index], index);
    }
  });
  await Promise.all(workers);
  return results;
}

export function registerTools(server) {
  const register = server.registerTool.bind(server);
  const readOnlyTools = new Set([
    "ping", "system_info", "fs_list", "fs_read_many", "fs_read_chunk", "git_summary", "process_read", "process_list",
    "list_devices", "who_am_i", "get_config", "read_file", "read_multiple_files", "list_directory",
    "start_search", "get_more_search_results", "list_searches", "get_file_info", "read_process_output",
    "list_sessions", "list_processes", "get_usage_stats", "get_recent_tool_calls", "get_prompts",
    "adb_devices", "adb_packages", "adb_ui_dump", "android_app_snapshot",
    "db_info", "db_query_start", "db_query_next", "db_query_close", "db_query_sessions",
    "build_cache_stats", "secret_list",
    "resource_snapshot", "process_resource_snapshot", "resource_governor_status",
    "process_resource_watch_read", "process_resource_watch_list",
    "dev_pipeline_validate", "dev_pipeline_status", "dev_pipeline_list", "dev_pipeline_wait", "dev_pipeline_logs",
    "foreground_control_status", "desktop_windows"
  ]);
  server.registerTool = (name, definition, handler) => register(name, {
    ...definition,
    inputSchema: {
      ...(definition.inputSchema || {}),
      deviceId: z.string().optional(),
      deviceName: z.string().optional()
    },

    ...(config.publicUrl ? { _meta: { ...definition._meta, securitySchemes: [{ type: "oauth2", scopes: ["workspace:access"] }] } } : {}),
    annotations: {
      readOnlyHint: readOnlyTools.has(name),
      destructiveHint: !readOnlyTools.has(name),
      openWorldHint: [
        "shell_run", "project_build_test", "process_start", "process_write",
        "adb_shell", "adb_push", "adb_pull", "adb_install", "adb_uninstall",
        "adb_input", "adb_app", "adb_logcat_start", "adb_find_and_tap", "android_dev_cycle", "db_execute", "build_cache_run",
        "dev_pipeline_start", "dev_pipeline_resume", "dev_pipeline_cancel", "wait_until", "http_probe", "cloudflare_deploy_verify",
        "desktop_actions"
      ].includes(name)
    }
  }, async (...args) => {
    const started = Date.now();
    let ok = false;
    try {
      if (config.readOnly && !readOnlyTools.has(name)) throw new Error(`${name} is disabled by RACCOON_READ_ONLY.`);
      const request = args[0] ?? {};
      const selector = request.deviceName || request.deviceId;
      if (selector) await refreshAccountDevices();
      if (selector && resolveDeviceSelector(selector) !== localDeviceId()) {
        const forwarded = { ...request };
        delete forwarded.deviceId;
        delete forwarded.deviceName;
        const remote = await callPeerToolByDeviceId(selector, name, forwarded);
        ok = !remote?.isError;
        recordToolCall({ tool: name, args: { device: selector }, result: remote, ok, durationMs: Date.now() - started });
        return remote;
      }
      if (selector) {
        args[0] = { ...request };
        delete args[0].deviceId;
        delete args[0].deviceName;
      }
      const result = await handler(...args);
      ok = !result.isError;
      recordToolCall({ tool: name, args: args[0] ?? {}, result, ok, durationMs: Date.now() - started });
      return result;
    } catch (error) {
      const result = { content: [{ type: "text", text: error.message }], isError: true };
      recordToolCall({ tool: name, args: args[0] ?? {}, result, ok: false, durationMs: Date.now() - started });
      return result;
    } finally {
      if (config.auditLog) {
        // Record metadata only: file contents, commands and tokens are omitted.
        await fs.appendFile(config.auditLog, JSON.stringify({ at: new Date().toISOString(), tool: name, ok, durationMs: Date.now() - started }) + "\n")
          .catch(error => console.error("Audit log write failed:", error.message));
      }
    }
  });
  server.registerTool("ping", {
    title: "Ping",
    description: "Verify that Raccoon is online locally or on a configured federated device.",
    inputSchema: { deviceId: z.string().optional() }
  }, async ({ deviceId } = {}) => {
    if (deviceId && deviceId !== localDeviceId()) {
      return await callPeerToolByDeviceId(deviceId, "ping", {});
    }
    return text({
      ok: true,
      time: new Date().toISOString(),
      root: config.root,
      transport: config.transport
    });
  });

  server.registerTool("system_info", {
    title: "System info",
    description: "Return basic host and Raccoon runtime information."
  }, async () => text({
    version,
    platform: process.platform,
    arch: process.arch,
    hostname: os.hostname(),
    node: process.version,
    root: config.root,
    transport: config.transport,
    shellEnabled: config.shellEnabled,
    filesystemUnrestricted: config.filesystemUnrestricted,
    sourceIntegrity: {protectedFile:'main.cs', writeTool:'fs_transaction', encoding:'utf-8-sig', compileValidationRequired:true},
    readOnly: config.readOnly,
    defaultReadBytes: config.defaultReadBytes,
    maxReadBytes: config.maxReadBytes,
    maxMessageBytes: config.maxMessageBytes,
    defaultProcessLogBytes: config.defaultProcessLogBytes,
    maxProcessLogBytes: config.maxProcessLogBytes,
    maxShellOutputBytes: config.maxShellOutputBytes,
    maxBuildOutputBytes: config.maxBuildOutputBytes,
    maxGitPatchBytes: config.maxGitPatchBytes,
    maxProcessReadBytes: config.maxProcessReadBytes,
    maxBatchFiles: config.maxBatchFiles,
    batchConcurrency: config.batchConcurrency,
    maxBatchConcurrency: config.maxBatchConcurrency,
    maxDirectoryEntries: config.maxDirectoryEntries,
    maxTreeDepth: config.maxTreeDepth,
    maxPatchFiles: config.maxPatchFiles,
    maxReplacementsPerFile: config.maxReplacementsPerFile,
    maxWorkflowSteps: config.maxWorkflowSteps,
    maxSearchContextLines: config.maxSearchContextLines,
    maxProcessSessions: config.maxProcessSessions,
    processEventChunkBytes: config.processEventChunkBytes,
    maxSearchResults: config.maxSearchResults,
    maxRecentToolCalls: config.maxRecentToolCalls,
    httpRequestTimeoutMs: config.httpRequestTimeoutMs,
    httpHeadersTimeoutMs: config.httpHeadersTimeoutMs,
    activeFileLocks: fileLockCount(),
    processSessions: listManagedProcesses().length
  }));

  server.registerTool("fs_list", {
    title: "List files",
    description: "List one directory. Accepts absolute paths when RACCOON_FILESYSTEM_UNRESTRICTED=1; relative paths start at RACCOON_ROOT.",
    inputSchema: {
      dir: z.string().default("."),
      limit: z.number().int().min(1).max(config.maxDirectoryEntries).default(Math.min(5000, config.maxDirectoryEntries))
    }
  }, async ({ dir, limit }) => {
    const target = insideRoot(dir);
    const entries = await fs.readdir(target, { withFileTypes: true });
    return text(entries.slice(0, limit).map(entry => ({
      name: entry.name,
      type: entry.isDirectory() ? "directory" : entry.isFile() ? "file" : "other"
    })));
  });

  server.registerTool("fs_read_many", {
    title: "Read many text files",
    description: "Read multiple UTF-8 files in one call using byte budgets instead of line limits.",
    inputSchema: {
      paths: z.array(z.string()).min(1).max(config.maxBatchFiles),
      maxBytesPerFile: z.number().int().min(1024).max(config.maxReadBytes).optional(),
      maxTotalBytes: z.number().int().min(1024).max(Math.floor(config.maxMessageBytes * 0.75)).default(Math.min(128 * 1024 * 1024, Math.floor(config.maxMessageBytes * 0.25))),
      concurrency: z.number().int().min(1).max(config.maxBatchConcurrency).default(config.batchConcurrency)
    }
  }, async ({ paths, maxBytesPerFile, maxTotalBytes, concurrency }) => {
    const requestedPerFile = Math.min(maxBytesPerFile || config.defaultReadBytes, config.maxReadBytes);
    const budget = Math.max(1024, Math.min(requestedPerFile, Math.floor(maxTotalBytes / paths.length)));
    const results = await mapLimit(paths, concurrency, async file => {
      try {
          const target = insideRoot(file);
          if (isProtectedSource(target)) decodeSource(await fs.readFile(target));
        const handle = await fs.open(target, "r");
        let buf, size;
        try {
          const stat = await handle.stat();
          if (!stat.isFile()) throw new Error("Only regular files can be read.");
          size = stat.size;
          buf = Buffer.alloc(Math.min(size, budget));
          const { bytesRead } = await handle.read(buf, 0, buf.length, 0);
          buf = buf.subarray(0, bytesRead);
        } finally { await handle.close(); }
        return {
          path: file,
          bytes: size,
          clipped: size > budget,
          content: isProtectedSource(target)
            ? new TextDecoder('utf-8', {fatal:true}).decode(buf, {stream:size > budget})
            : buf.subarray(0, budget).toString("utf8")
        };
      } catch (error) {
        return { path: file, error: String(error?.message || error) };
      }
    });
    return text(results);
  });

  server.registerTool("fs_read_chunk", {
    title: "Read file chunk",
    description: "Read an arbitrary byte range from a text or binary file. Repeated calls can read files of any size without loading the whole file into memory.",
    inputSchema: {
      file: z.string().min(1),
      offset: z.number().int().default(0),
      maxBytes: z.number().int().min(1).max(config.maxReadBytes).default(Math.min(8 * 1024 * 1024, config.maxReadBytes)),
      encoding: z.enum(["utf8", "base64"]).default("utf8")
    }
  }, async ({ file, offset, maxBytes, encoding }) => {
    const target = insideRoot(file);
    const handle = await fs.open(target, "r");
    try {
      const stat = await handle.stat();
      if (!stat.isFile()) throw new Error("Only regular files can be read.");
      const start = offset < 0 ? Math.max(0, stat.size + offset) : Math.min(offset, stat.size);
      const length = Math.min(maxBytes, stat.size - start);
      const buf = Buffer.alloc(length);
      const { bytesRead } = length > 0 ? await handle.read(buf, 0, length, start) : { bytesRead: 0 };
      const data = buf.subarray(0, bytesRead);
      return text({
        file,
        size: stat.size,
        offset: start,
        bytesRead,
        nextOffset: start + bytesRead,
        eof: start + bytesRead >= stat.size,
        encoding,
        data: encoding === "base64" ? data.toString("base64") : data.toString("utf8")
      });
    } finally {
      await handle.close();
    }
  });

  server.registerTool("fs_write_chunk", {
    title: "Write file chunk",
    description: "Write UTF-8 or base64 data by rewrite, append, or absolute byte offset. Chunked calls remove any practical whole-file size limit.",
    inputSchema: {
      file: z.string().min(1),
      content: z.string(),
      encoding: z.enum(["utf8", "base64"]).default("utf8"),
      mode: z.enum(["rewrite", "append", "at"]).default("append"),
      offset: z.number().int().min(0).optional()
    }
  }, async ({ file, content, encoding, mode, offset }) => {
    const target = insideRoot(file);
    assertOrdinaryWriteAllowed(target);
    const data = Buffer.from(content, encoding);
    if (data.length > config.maxReadBytes) {
      throw new Error(`Chunk exceeds RACCOON_MAX_READ_BYTES (${config.maxReadBytes} bytes); split it into smaller chunks.`);
    }
    return text(await withFileLock(target, async () => {
      await fs.mkdir(path.dirname(target), { recursive: true });
      if (mode === "rewrite") {
        await fs.writeFile(target, data);
      } else if (mode === "append") {
        await fs.appendFile(target, data);
      } else {
        if (!Number.isSafeInteger(offset)) throw new Error("offset is required when mode='at'.");
        let handle;
        try { handle = await fs.open(target, "r+"); }
        catch (error) {
          if (error.code !== "ENOENT") throw error;
          handle = await fs.open(target, "w+");
        }
        try { await handle.write(data, 0, data.length, offset); }
        finally { await handle.close(); }
      }
      const stat = await fs.stat(target);
      return { ok: true, file, bytesWritten: data.length, size: stat.size, mode, offset: mode === "at" ? offset : null };
    }));
  });

  server.registerTool("fs_write_text", {
    title: "Write text file",
    description: "Create or replace a UTF-8 text file. main.cs requires fs_transaction with a compile validation command. Accepts absolute paths when RACCOON_FILESYSTEM_UNRESTRICTED=1; relative paths start at RACCOON_ROOT.",
    inputSchema: {
      file: z.string().min(1),
      content: z.string()
    }
  }, async ({ file, content }) => {
    const target = insideRoot(file);
    assertOrdinaryWriteAllowed(target);
    const result = await withFileLock(target, async () => {
      await fs.mkdir(path.dirname(target), { recursive: true });
      await fs.writeFile(target, content, "utf8");
      return { ok: true, file, bytes: Buffer.byteLength(content) };
    });
    return text(result);
  });

  server.registerTool("fs_write_many", {
    title: "Write many text files",
    description: "Create or replace many UTF-8 text files concurrently while preserving per-file locking.",
    inputSchema: {
      files: z.array(z.object({
        file: z.string().min(1),
        content: z.string()
      })).min(1).max(config.maxBatchFiles),
      concurrency: z.number().int().min(1).max(config.maxBatchConcurrency).default(config.batchConcurrency)
    }
  }, async ({ files, concurrency }) => {
    const results = await mapLimit(files, concurrency, async item => {
      const target = insideRoot(item.file);
      try {
        assertOrdinaryWriteAllowed(target);
        return await withFileLock(target, async () => {
          await fs.mkdir(path.dirname(target), { recursive: true });
          await fs.writeFile(target, item.content, "utf8");
          return { ok: true, file: item.file, bytes: Buffer.byteLength(item.content) };
        });
      } catch (error) {
        return { ok: false, file: item.file, error: String(error?.message || error) };
      }
    });
    return text(results);
  });

  server.registerTool("fs_apply_patch_many", {
    title: "Apply many exact-text patches",
    description: "Apply validated exact-text replacements to many files in one call. main.cs requires fs_transaction with compile validation. A file is not written if any replacement for that file fails validation.",
    inputSchema: {
      files: z.array(z.object({
        file: z.string().min(1),
        replacements: z.array(z.object({
          oldText: z.string().min(1),
          newText: z.string(),
          replaceAll: z.boolean().default(false)
        })).min(1).max(config.maxReplacementsPerFile)
      })).min(1).max(config.maxPatchFiles),
      dryRun: z.boolean().default(false),
      concurrency: z.number().int().min(1).max(config.maxBatchConcurrency).default(config.batchConcurrency)
    }
  }, async ({ files, dryRun, concurrency }) => {
    const results = await mapLimit(files, concurrency, async item => {
      try {
        const target = insideRoot(item.file);
        assertOrdinaryWriteAllowed(target);
        return await withFileLock(target, async () => {
          const original = await fs.readFile(target, "utf8");
          let updated = original;
          let applied = 0;

          for (const replacement of item.replacements) {
            const matches = countOccurrences(updated, replacement.oldText);
            if (matches === 0) {
              throw new Error("oldText not found");
            }
            if (!replacement.replaceAll && matches > 1) {
              throw new Error(`oldText is ambiguous (${matches} matches); use a larger oldText or replaceAll=true`);
            }

            if (replacement.replaceAll) {
              updated = updated.split(replacement.oldText).join(replacement.newText);
              applied += matches;
            } else {
              updated = replaceOnce(updated, replacement.oldText, replacement.newText);
              applied += 1;
            }
          }

          if (!dryRun) await fs.writeFile(target, updated, "utf8");

          return {
            file: item.file,
            ok: true,
            dryRun,
            replacementsApplied: applied,
            bytesBefore: Buffer.byteLength(original),
            bytesAfter: Buffer.byteLength(updated),
            changed: updated !== original
          };
        });
      } catch (error) {
        return {
          file: item.file,
          ok: false,
          error: String(error?.message || error)
        };
      }
    });
    return text(results);
  });

  server.registerTool("shell_run", {
    title: "Run shell command",
    description: "Run one shell command in the specified working directory. Requires RACCOON_ENABLE_SHELL=1. Absolute working directories are allowed when RACCOON_FILESYSTEM_UNRESTRICTED=1.",
    inputSchema: {
      command: z.string().min(1),
      cwd: z.string().default("."),
      runner: z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default"),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(120000),
      maxOutputBytes: z.number().int().min(1024).max(config.maxShellOutputBytes).default(Math.min(4 * 1024 * 1024, config.maxShellOutputBytes))
    }
  }, async ({ command, cwd, runner, timeoutMs, maxOutputBytes }) => {
    if (!config.shellEnabled) {
      throw new Error("shell_run is disabled. Set RACCOON_ENABLE_SHELL=1.");
    }
    return text(await runShell(command, insideRoot(cwd), timeoutMs, maxOutputBytes, runnerExecutable(runner)));
  });

  server.registerTool("project_build_test", {
    title: "Run project workflow",
    description: "Run a sequence of build/test/lint commands in one call and return compact per-step results. Requires RACCOON_ENABLE_SHELL=1.",
    inputSchema: {
      cwd: z.string().default("."),
      steps: z.array(z.object({
        name: z.string().min(1),
        command: z.string().min(1),
        runner: z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default")
      })).min(1).max(config.maxWorkflowSteps),
      stopOnFailure: z.boolean().default(true),
      parallelism: z.number().int().min(1).max(config.maxBatchConcurrency).default(1),
      timeoutMsPerStep: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(300000),
      maxOutputBytesPerStep: z.number().int().min(1024).max(config.maxBuildOutputBytes).default(Math.min(2 * 1024 * 1024, config.maxBuildOutputBytes))
    }
  }, async ({ cwd, steps, stopOnFailure, parallelism, timeoutMsPerStep, maxOutputBytesPerStep }) => {
    if (!config.shellEnabled) {
      throw new Error("project_build_test is disabled. Set RACCOON_ENABLE_SHELL=1.");
    }
    const workdir = insideRoot(cwd);
    let results;
    if (parallelism === 1) {
      results = [];
      for (const step of steps) {
        const result = await runShell(step.command, workdir, timeoutMsPerStep, maxOutputBytesPerStep, runnerExecutable(step.runner));
        results.push({ name: step.name, command: step.command, ...result });
        if (!result.ok && stopOnFailure) break;
      }
    } else {
      let halted = false;
      results = await mapLimit(steps, parallelism, async step => {
        if (halted) return null;
        const result = await runShell(step.command, workdir, timeoutMsPerStep, maxOutputBytesPerStep, runnerExecutable(step.runner));
        if (!result.ok && stopOnFailure) halted = true;
        return { name: step.name, command: step.command, ...result };
      });
      results = results.filter(Boolean);
    }
    return text({
      ok: results.length === steps.length && results.every(step => step.ok),
      cwd,
      stepsRequested: steps.length,
      stepsCompleted: results.length,
      parallelism,
      results
    });
  });

  server.registerTool("git_summary", {
    title: "Git status and diff summary",
    description: "Return branch/status plus working-tree and staged diff summaries without invoking an arbitrary shell.",
    inputSchema: {
      cwd: z.string().default("."),
      includePatch: z.boolean().default(false),
      maxPatchBytes: z.number().int().min(1024).max(config.maxGitPatchBytes).default(Math.min(2 * 1024 * 1024, config.maxGitPatchBytes))
    }
  }, async ({ cwd, includePatch, maxPatchBytes }) => {
    const workdir = insideRoot(cwd);
    const git = async args => {
      const { stdout } = await execFileAsync("git", args, {
        cwd: workdir,
        windowsHide: true,
        maxBuffer: Math.max(maxPatchBytes, 1024 * 1024)
      });
      return stdout;
    };

    try {
      const [status, unstagedStat, stagedStat] = await Promise.all([
        git(["status", "--porcelain=v1", "-b"]),
        git(["diff", "--stat"]),
        git(["diff", "--cached", "--stat"])
      ]);
      const result = {
        ok: true,
        cwd,
        status,
        unstagedStat,
        stagedStat
      };
      if (includePatch) {
        const [unstaged, staged] = await Promise.all([
          git(["diff", "--no-ext-diff"]),
          git(["diff", "--cached", "--no-ext-diff"])
        ]);
        result.unstagedPatch = clip(unstaged, maxPatchBytes);
        result.stagedPatch = clip(staged, maxPatchBytes);
      }
      return text(result);
    } catch (error) {
      return text({ ok: false, cwd, error: String(error?.message || error) });
    }
  });

  server.registerTool("process_start", {
    title: "Start persistent process",
    description: "Start a long-running shell process and return a session ID for incremental output reads. Requires RACCOON_ENABLE_SHELL=1.",
    inputSchema: {
      command: z.string().min(1),
      cwd: z.string().default("."),
      runner: z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default"),
      maxLogBytes: z.number().int().min(64 * 1024).max(config.maxProcessLogBytes).default(config.defaultProcessLogBytes)
    }
  }, async ({ command, cwd, runner, maxLogBytes }) => {
    if (!config.shellEnabled) {
      throw new Error("process_start is disabled. Set RACCOON_ENABLE_SHELL=1.");
    }
    return text(startManagedProcess({
      command,
      cwd: insideRoot(cwd),
      maxLogBytes,
      shell: runnerExecutable(runner)
    }));
  });

  server.registerTool("process_read", {
    title: "Read persistent process output",
    description: "Read new stdout/stderr events from a process session using a cursor. Can wait briefly for new output.",
    inputSchema: {
      sessionId: z.string().min(1),
      cursor: z.number().int().min(0).default(0),
      maxBytes: z.number().int().min(1024).max(config.maxProcessReadBytes).default(Math.min(1024 * 1024, config.maxProcessReadBytes)),
      waitMs: z.number().int().min(0).max(10000).default(0)
    }
  }, async ({ sessionId, cursor, maxBytes, waitMs }) => {
    return text(await readManagedProcess({
      sessionId,
      cursor,
      maxBytes,
      waitMs
    }));
  });

  server.registerTool("process_write", {
    title: "Write to persistent process stdin",
    description: "Send input to a running process session. Requires RACCOON_ENABLE_SHELL=1.",
    inputSchema: {
      sessionId: z.string().min(1),
      input: z.string(),
      appendNewline: z.boolean().default(false)
    }
  }, async ({ sessionId, input, appendNewline }) => {
    if (!config.shellEnabled) {
      throw new Error("process_write is disabled. Set RACCOON_ENABLE_SHELL=1.");
    }
    return text(writeManagedProcess({ sessionId, input, appendNewline }));
  });

  server.registerTool("process_kill", {
    title: "Kill persistent process",
    description: "Terminate a process session started by Raccoon. Requires RACCOON_ENABLE_SHELL=1.",
    inputSchema: {
      sessionId: z.string().min(1)
    }
  }, async ({ sessionId }) => {
    if (!config.shellEnabled) {
      throw new Error("process_kill is disabled. Set RACCOON_ENABLE_SHELL=1.");
    }
    return text(killManagedProcess({ sessionId }));
  });

  server.registerTool("process_list", {
    title: "List process sessions",
    description: "List persistent process sessions currently retained by Raccoon."
  }, async () => text(listManagedProcesses()));

  // Android / ADB native tools.
  server.registerTool("adb_devices", {
    title: "List Android devices",
    description: "List ADB devices with state, model, product and transport metadata."
  }, async () => text(await adbDevices()));

  server.registerTool("adb_shell", {
    title: "Run Android shell command",
    description: "Run a command through adb shell on an optional target device.",
    inputSchema: {
      serial: z.string().optional(),
      command: z.string().min(1),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(120000),
      maxOutputBytes: z.number().int().min(1024).max(config.maxShellOutputBytes).default(8 * 1024 * 1024)
    }
  }, async args => text(await adbShell(args)));

  server.registerTool("adb_push", {
    title: "Push file to Android",
    description: "Push a local workspace file to an Android device.",
    inputSchema: {
      serial: z.string().optional(),
      localPath: z.string().min(1),
      remotePath: z.string().min(1),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(300000)
    }
  }, async args => text(await adbPush(args)));

  server.registerTool("adb_pull", {
    title: "Pull file from Android",
    description: "Pull a file from an Android device into the local workspace.",
    inputSchema: {
      serial: z.string().optional(),
      remotePath: z.string().min(1),
      localPath: z.string().min(1),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(300000)
    }
  }, async args => text(await adbPull(args)));

  server.registerTool("adb_install", {
    title: "Install Android APK",
    description: "Install an APK from the local workspace using adb install.",
    inputSchema: {
      serial: z.string().optional(),
      apkPath: z.string().min(1),
      replace: z.boolean().default(true),
      grantPermissions: z.boolean().default(false),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(600000)
    }
  }, async args => text(await adbInstall(args)));

  server.registerTool("adb_uninstall", {
    title: "Uninstall Android package",
    description: "Uninstall an Android package, optionally retaining its data.",
    inputSchema: {
      serial: z.string().optional(),
      packageName: z.string().min(1),
      keepData: z.boolean().default(false),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(120000)
    }
  }, async args => text(await adbUninstall(args)));

  server.registerTool("adb_screenshot", {
    title: "Capture Android screenshot",
    description: "Capture the current Android screen through adb exec-out and return it as an MCP image.",
    inputSchema: {
      serial: z.string().optional(),
      savePath: z.string().optional(),
      maxBytes: z.number().int().min(1024).max(config.maxReadBytes).default(64 * 1024 * 1024)
    }
  }, async args => {
    const shot = await adbScreenshot(args);
    return {
      content: [
        { type: "text", text: JSON.stringify({
          serial: shot.serial,
          bytes: shot.bytes,
          savedPath: shot.savedPath,
          stderr: shot.stderr
        }, null, 2) },
        { type: "image", data: shot.buffer.toString("base64"), mimeType: "image/png" }
      ]
    };
  });

  server.registerTool("adb_input", {
    title: "Send Android input",
    description: "Send tap, swipe, text or keyevent input through adb.",
    inputSchema: {
      serial: z.string().optional(),
      type: z.enum(["tap", "swipe", "text", "keyevent"]),
      x: z.number().optional(),
      y: z.number().optional(),
      x2: z.number().optional(),
      y2: z.number().optional(),
      durationMs: z.number().int().min(0).max(60000).default(300),
      text: z.string().optional(),
      keyCode: z.union([z.string(), z.number().int()]).optional()
    }
  }, async args => text(await adbInput(args)));

  server.registerTool("adb_app", {
    title: "Control Android app",
    description: "Start, stop or clear an Android package.",
    inputSchema: {
      serial: z.string().optional(),
      action: z.enum(["start", "stop", "clear"]),
      packageName: z.string().min(1),
      activity: z.string().optional(),
      deepLink: z.string().optional()
    }
  }, async args => text(await adbApp(args)));

  server.registerTool("adb_packages", {
    title: "List Android packages",
    description: "List installed Android packages with optional third-party and text filtering.",
    inputSchema: {
      serial: z.string().optional(),
      thirdPartyOnly: z.boolean().default(false),
      filter: z.string().default("")
    }
  }, async args => text(await adbPackages(args)));

  server.registerTool("adb_ui_dump", {
    title: "Read Android UI tree",
    description: "Run uiautomator dump and return structured UI nodes with text, resourceId, content description, bounds and interaction flags.",
    inputSchema: {
      serial: z.string().optional(),
      includeXml: z.boolean().default(false)
    }
  }, async args => text(await adbUiDump(args)));

  server.registerTool("adb_find_and_tap", {
    title: "Find and tap Android UI node",
    description: "Find an Android UI node by text/resourceId/content description and tap its center instead of guessing screen coordinates.",
    inputSchema: {
      serial: z.string().optional(),
      text: z.string().optional(),
      resourceId: z.string().optional(),
      contentDesc: z.string().optional(),
      match: z.enum(["exact", "contains"]).default("exact"),
      requireClickable: z.boolean().default(false),
      index: z.number().int().min(0).max(10000).default(0)
    }
  }, async args => text(await adbFindAndTap(args)));

  server.registerTool("android_app_snapshot", {
    title: "Android app state snapshot",
    description: "Return package version, PID, resumed activity and relevant crash-buffer state for one Android package.",
    inputSchema: {
      serial: z.string().optional(),
      packageName: z.string().regex(/^[A-Za-z0-9_.]+$/),
      crashLines: z.number().int().min(1).max(5000).default(200)
    }
  }, async args => text(await androidAppSnapshot(args)));

  server.registerTool("adb_logcat_start", {
    title: "Start Android logcat",
    description: "Start persistent adb logcat and return a normal Raccoon process session for cursor reads.",
    inputSchema: {
      serial: z.string().optional(),
      filters: z.array(z.string()).max(100).default([]),
      clearFirst: z.boolean().default(false),
      maxLogBytes: z.number().int().min(64 * 1024).max(config.maxProcessLogBytes).default(config.defaultProcessLogBytes)
    }
  }, async args => text(adbLogcatStart(args)));

  // Desktop screenshots and visual feedback.
  server.registerTool("screenshot_capture", {
    title: "Capture Windows screenshot",
    description: "Capture the virtual desktop or one monitor and return the PNG directly as an MCP image.",
    inputSchema: {
      monitorIndex: z.number().int().min(0).optional(),
      savePath: z.string().optional()
    }
  }, async args => {
    const shot = await captureDesktop(args);
    return {
      content: [
        { type: "text", text: JSON.stringify({
          bytes: shot.bytes,
          bounds: shot.bounds,
          monitorIndex: shot.monitorIndex,
          savedPath: shot.savedPath
        }, null, 2) },
        { type: "image", data: shot.buffer.toString("base64"), mimeType: "image/png" }
      ]
    };
  });

  server.registerTool("visual_compare", {
    title: "Compare screenshots",
    description: "Compute pixel-level visual change metrics and optionally save a red-channel difference image.",
    inputSchema: {
      beforePath: z.string().min(1),
      afterPath: z.string().min(1),
      threshold: z.number().min(0).max(255).default(24),
      diffSavePath: z.string().optional()
    }
  }, async args => text(await compareImages(args)));

  // SQLite streaming query tools.
  server.registerTool("db_info", {
    title: "Inspect SQLite database",
    description: "Inspect SQLite schema and basic page/database metadata.",
    inputSchema: { file: z.string().min(1) }
  }, async args => text(dbInfo(args)));

  server.registerTool("db_query_start", {
    title: "Start streaming SQLite query",
    description: "Prepare a SQLite query, return its first batch, and retain the iterator for subsequent batches.",
    inputSchema: {
      file: z.string().min(1),
      sql: z.string().min(1),
      params: z.any().optional(),
      batchRows: z.number().int().min(1).max(100000).default(500),
      readOnly: z.boolean().default(true)
    }
  }, async args => text(dbQueryStart(args)));

  server.registerTool("db_query_next", {
    title: "Read next SQLite query batch",
    description: "Read the next rows from a retained SQLite streaming query iterator.",
    inputSchema: {
      sessionId: z.string().min(1),
      maxRows: z.number().int().min(1).max(100000).default(500)
    }
  }, async args => text(dbQueryNext(args)));

  server.registerTool("db_query_close", {
    title: "Close SQLite query",
    description: "Close a retained SQLite query session and database handle.",
    inputSchema: { sessionId: z.string().min(1) }
  }, async args => text(dbQueryClose(args)));

  server.registerTool("db_query_sessions", {
    title: "List SQLite query sessions",
    description: "List currently retained SQLite streaming query sessions."
  }, async () => text(dbQuerySessions()));

  server.registerTool("db_execute", {
    title: "Execute SQLite statement",
    description: "Execute mutating SQLite SQL or a parameterized statement.",
    inputSchema: {
      file: z.string().min(1),
      sql: z.string().min(1),
      params: z.any().optional()
    }
  }, async args => text(dbExecute(args)));

  // Content-addressed build cache.
  server.registerTool("build_cache_run", {
    title: "Run cached build",
    description: "Hash declared inputs and command, restore cached outputs on hit, or run and cache outputs on miss.",
    inputSchema: {
      cwd: z.string().default("."),
      command: z.string().min(1),
      inputs: z.array(z.string()).max(5000).default([]),
      outputs: z.array(z.string()).max(5000).default([]),
      environment: z.record(z.string(), z.string()).default({}),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(30 * 60 * 1000),
      maxOutputBytes: z.number().int().min(1024).max(config.maxBuildOutputBytes).default(16 * 1024 * 1024),
      restore: z.boolean().default(true),
      force: z.boolean().default(false)
    }
  }, async args => text(await buildCacheRun(args)));

  server.registerTool("build_cache_stats", {
    title: "Build cache statistics",
    description: "List build-cache entries, hit counts and disk usage."
  }, async () => text(await buildCacheStats()));

  server.registerTool("build_cache_clear", {
    title: "Clear build cache",
    description: "Clear one content-addressed build-cache key or the entire cache.",
    inputSchema: { key: z.string().optional() }
  }, async args => text(await buildCacheClear(args)));

  // Resource monitoring and automatic throttling.
  server.registerTool("resource_snapshot", {
    title: "System resource snapshot",
    description: "Sample system CPU/memory and return top memory-consuming processes plus governor state.",
    inputSchema: {
      sampleMs: z.number().int().min(50).max(5000).default(250),
      top: z.number().int().min(0).max(100).default(10)
    }
  }, async args => text(await systemResourceSnapshot(args)));

  server.registerTool("process_resource_snapshot", {
    title: "Process resource snapshot",
    description: "Sample CPU, memory, handles, threads and priority for one PID.",
    inputSchema: {
      pid: z.number().int().positive(),
      sampleMs: z.number().int().min(50).max(5000).default(250)
    }
  }, async args => text(await processResourceSnapshot(args)));

  server.registerTool("resource_governor_configure", {
    title: "Configure resource governor",
    description: "Enable/disable automatic admission throttling for heavy shell/build work based on CPU, memory and concurrency.",
    inputSchema: {
      enabled: z.boolean().optional(),
      maxConcurrentHeavyOps: z.number().int().min(1).max(1024).optional(),
      maxSystemCpuPercent: z.number().min(1).max(100).optional(),
      minFreeMemoryBytes: z.number().int().min(0).optional(),
      pollMs: z.number().int().min(100).max(60000).optional()
    }
  }, async args => text(configureGovernor(args)));

  server.registerTool("resource_governor_status", {
    title: "Resource governor status",
    description: "Return automatic throttling configuration, active heavy operations and queued work."
  }, async () => text(governorStatus()));

  server.registerTool("process_resource_watch_start", {
    title: "Start process resource watch",
    description: "Continuously sample one PID and optionally lower priority or terminate it when CPU/memory thresholds are exceeded.",
    inputSchema: {
      pid: z.number().int().positive(),
      intervalMs: z.number().int().min(250).max(60000).default(1000),
      maxCpuPercent: z.number().min(0).max(100).optional(),
      maxMemoryBytes: z.number().int().min(1).optional(),
      action: z.enum(["none", "lower_priority", "kill"]).default("none"),
      priority: z.enum(["Idle", "BelowNormal", "Normal", "AboveNormal", "High"]).default("BelowNormal"),
      maxSamples: z.number().int().min(1).max(100000).default(1000)
    }
  }, async args => text(processWatchStart(args)));

  server.registerTool("process_resource_watch_read", {
    title: "Read process resource watch",
    description: "Read recent resource samples and threshold violations from a process watch.",
    inputSchema: {
      watchId: z.string().min(1),
      tail: z.number().int().min(1).max(10000).default(20)
    }
  }, async args => text(processWatchRead(args)));

  server.registerTool("process_resource_watch_stop", {
    title: "Stop process resource watch",
    description: "Stop and remove a process resource watch.",
    inputSchema: { watchId: z.string().min(1) }
  }, async args => text(processWatchStop(args)));

  server.registerTool("process_resource_watch_list", {
    title: "List process resource watches",
    description: "List active resource watches."
  }, async () => text(processWatchList()));

  registerFsTransactionTool(server);
  registerGitScopeTool(server);
  registerWorktreeTaskTool(server);
  registerHttpProbeTools(server);
  registerAndroidWorkflowTool(server);
  registerCloudflareWorkflowTool(server);
  registerPipelineTools(server);
  registerForegroundControlTools(server);
  registerDesktopAutomationTools(server);
  registerCompatibilityTools(server);
}
