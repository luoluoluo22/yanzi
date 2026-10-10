import fs from "node:fs";
import path from "node:path";
import { spawn, execFile } from "node:child_process";
import { promisify } from "node:util";
import { randomUUID, createHash } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { z } from "zod";
import { config, insideRoot } from "./config.js";
import { shellCommand } from "./process-manager.js";
import { withHeavyPermit } from "./resource-governor.js";
import {
  buildPipelineContext,
  captureOutputs,
  evaluateExpression,
  interpolateTemplate,
  matchesProbe,
  runnerExecutable,
  sleep
} from "./pipeline-runtime.js";

const execFileAsync = promisify(execFile);

const identifier = z.string().regex(/^[a-zA-Z0-9_-]{1,80}$/);
const runnerSchema = z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default");
const captureSelectorSchema = z.object({
  source: z.enum(["stdout", "stderr"]).default("stdout"),
  regex: z.string().max(16384).optional(),
  flags: z.string().max(16).default(""),
  group: z.number().int().min(0).max(100).default(1),
  jsonPath: z.string().max(1024).optional(),
  path: z.string().max(1024).optional()
});
const probeConditionSchema = z.object({
  exitCode: z.number().int().optional(),
  stdoutIncludes: z.string().max(16384).optional(),
  stderrIncludes: z.string().max(16384).optional(),
  stdoutRegex: z.string().max(16384).optional(),
  stderrRegex: z.string().max(16384).optional(),
  jsonPathExists: z.string().max(1024).optional(),
  jsonPathEquals: z.object({ path: z.string().max(1024), value: z.any() }).optional()
}).default({ exitCode: 0 });

export const pipelineSchema = z.object({
  name: z.string().min(1).max(200),
  cwd: z.string().default("."),
  parallelism: z.number().int().min(1).max(16).default(3),
  nodes: z.array(z.object({
    id: identifier,
    command: z.string().min(1).max(65536),
    cwd: z.string().optional(),
    runner: runnerSchema,
    dependsOn: z.array(identifier).max(256).default([]),
    locks: z.array(z.string().min(1).max(200)).max(32).default([]),
    when: z.enum(["success", "always", "failure"]).default("success"),
    timeoutMs: z.number().int().min(100).max(86400000).default(300000),
    retries: z.number().int().min(0).max(5).default(0),
    idempotent: z.boolean().default(false),
    retryDelayMs: z.number().int().min(0).max(60000).default(1000),
    retryUntil: z.object({
      condition: probeConditionSchema,
      intervalMs: z.number().int().min(100).max(60000).default(10000),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(300000),
      maxAttempts: z.number().int().min(1).max(10000).default(60)
    }).optional(),
    if: z.string().min(1).max(4096).optional(),
    capture: z.record(z.string().regex(/^[a-zA-Z0-9_-]{1,80}$/), captureSelectorSchema).default({}),
    maxLogBytes: z.number().int().min(1024).max(16 * 1024 * 1024).default(1024 * 1024),
    expect: z.object({
      stdoutIncludes: z.string().max(16384).optional(),
      artifacts: z.array(z.string().min(1)).max(100).default([])
    }).default({ artifacts: [] })
  })).min(1).max(256)
});

export function validatePipeline(input) {
  const spec = pipelineSchema.parse(input);
  const ids = new Map();
  for (const node of spec.nodes) {
    if (ids.has(node.id)) throw new Error(`Duplicate node: ${node.id}`);
    if (node.retries && !node.idempotent) throw new Error(`Retries require idempotent=true: ${node.id}`);
    if (node.retryUntil && !node.idempotent) throw new Error(`retryUntil requires idempotent=true: ${node.id}`);
    if (node.retryUntil && node.retries) throw new Error(`Use retries or retryUntil, not both: ${node.id}`);
    const templatedCwd = node.cwd?.includes("${");
    const cwd = templatedCwd ? insideRoot(spec.cwd) : insideRoot(node.cwd ? path.resolve(insideRoot(spec.cwd), node.cwd) : spec.cwd);
    if (!fs.statSync(cwd).isDirectory()) throw new Error(`Not a directory: ${cwd}`);
    for (const artifact of node.expect.artifacts) if (!artifact.includes("${")) insideRoot(path.resolve(cwd, artifact));
    ids.set(node.id, node);
  }
  const visiting = new Set(), visited = new Set();
  function visit(id) {
    if (!ids.has(id)) throw new Error(`Unknown dependency: ${id}`);
    if (visiting.has(id)) throw new Error(`Dependency cycle: ${id}`);
    if (visited.has(id)) return;
    visiting.add(id);
    for (const dependency of ids.get(id).dependsOn) visit(dependency);
    visiting.delete(id); visited.add(id);
  }
  for (const id of ids.keys()) visit(id);
  return spec;
}

const terminal = status => !["pending", "running"].includes(status);
function utf8Tail(text, maxBytes) {
  const buffer = Buffer.from(text);
  let offset = Math.max(0, buffer.length - maxBytes);
  while (offset < buffer.length && (buffer[offset] & 0xc0) === 0x80) offset++;
  return buffer.subarray(offset).toString("utf8");
}

function applyPowerShellExitGuard(shell) {
  if (process.platform === "win32" && /(?:powershell|pwsh)(?:\.exe)?$/i.test(shell.executable)) {
    shell.args[shell.args.length - 1] = "$ErrorActionPreference = 'Stop'; " + shell.args.at(-1) + "; $_raccoonLastSuccess = $?; if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; if (-not $_raccoonLastSuccess) { exit 1 }";
  }
  return shell;
}

async function executeProbeCommand({ command, cwd, runner = "default", timeoutMs = 120000, maxOutputBytes = 1024 * 1024 }) {
  const started = Date.now();
  const shell = applyPowerShellExitGuard(shellCommand(command, runnerExecutable(runner)));
  try {
    const { stdout, stderr } = await withHeavyPermit(() => execFileAsync(shell.executable, shell.args, {
      cwd,
      timeout: timeoutMs,
      windowsHide: true,
      maxBuffer: maxOutputBytes
    }));
    return { ok: true, exitCode: 0, durationMs: Date.now() - started, stdout: utf8Tail(stdout || "", maxOutputBytes), stderr: utf8Tail(stderr || "", maxOutputBytes) };
  } catch (error) {
    return {
      ok: false,
      exitCode: typeof error?.code === "number" ? error.code : null,
      signal: error?.signal ?? null,
      killed: Boolean(error?.killed),
      durationMs: Date.now() - started,
      stdout: utf8Tail(error?.stdout || "", maxOutputBytes),
      stderr: utf8Tail(error?.stderr || String(error?.message || error), maxOutputBytes)
    };
  }
}

export async function waitUntil({ command, cwd = ".", runner = "default", condition = { exitCode: 0 }, intervalMs = 10000, timeoutMs = 300000, commandTimeoutMs = 120000, maxAttempts = 60, maxOutputBytes = 1024 * 1024, idempotent = false }) {
  if (!config.shellEnabled) throw new Error("wait_until requires RACCOON_ENABLE_SHELL=1.");
  if (!idempotent && maxAttempts > 1) throw new Error("wait_until repeats commands; set idempotent=true to acknowledge safe repetition.");
  const workdir = insideRoot(cwd);
  const started = Date.now();
  let last = null;
  let attempts = 0;
  while (attempts < maxAttempts) {
    const remainingBeforeAttempt = timeoutMs - (Date.now() - started);
    if (remainingBeforeAttempt <= 0) break;
    attempts++;
    last = await executeProbeCommand({ command, cwd: workdir, runner, timeoutMs: Math.min(commandTimeoutMs, remainingBeforeAttempt), maxOutputBytes });
    const matched = matchesProbe(last, condition);
    if (matched) return { ok: true, matched: true, attempts, durationMs: Date.now() - started, last };
    const remaining = timeoutMs - (Date.now() - started);
    if (attempts >= maxAttempts || remaining <= 0) break;
    await sleep(Math.min(intervalMs, remaining));
  }
  return { ok: false, matched: false, attempts, durationMs: Date.now() - started, last, error: "Condition was not satisfied before timeout/attempt limit." };
}

export class PipelineEngine {
  constructor(directory, { maxRunningNodes = 8 } = {}) {
    this.directory = directory;
    this.maxRunningNodes = maxRunningNodes;
    this.runs = new Map(); this.locks = new Map(); this.active = new Map();
    this.waiters = new Set(); this.stopping = false;
    fs.mkdirSync(directory, { recursive: true });
    this.ownerFile = path.join(directory, "owner.json");
    try {
      const owner = JSON.parse(fs.readFileSync(this.ownerFile, "utf8"));
      let alive = true;
      try { process.kill(owner.pid, 0); } catch (error) { if (error.code === "ESRCH") alive = false; }
      if (alive) throw new Error(`Pipeline store is owned by process ${owner.pid}`);
      fs.unlinkSync(this.ownerFile);
    } catch (error) { if (error.code !== "ENOENT") throw error; }
    fs.writeFileSync(this.ownerFile, JSON.stringify({ pid: process.pid }), { flag: "wx" });
    this.db = new DatabaseSync(path.join(directory, "state.sqlite"));
    this.db.exec("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, data TEXT NOT NULL)");
    this.saveRow = this.db.prepare("INSERT OR REPLACE INTO runs VALUES (?, ?)");
    for (const row of this.db.prepare("SELECT data FROM runs").all()) {
      const run = JSON.parse(row.data);
      if (run.status === "running") {
        run.status = "interrupted";
        for (const node of run.nodes) if (node.status === "running") node.status = "interrupted";
        run.recoveryNote = "Execution outcome is unknown. Reconcile external effects and orphan processes before confirming resume.";
      }
      this.runs.set(run.runId, run);
      for (const node of run.nodes.filter(n => n.status === "interrupted")) {
        for (const lock of this.nodeSpec(run, node.id).locks) this.locks.set(this.lockKey(lock), `${run.runId}:${node.id}`);
      }
      this.save(run);
    }
  }

  lockKey(key) { return process.platform === "win32" ? key.toLowerCase() : key; }
  nodeSpec(run, id) { return run.spec.nodes.find(n => n.id === id); }
  get(id) { const run = this.runs.get(id); if (!run) throw new Error(`Unknown pipeline: ${id}`); return run; }
  save(run) {
    run.revision = (run.revision || 0) + 1; run.updatedAt = new Date().toISOString();
    this.saveRow.run(run.runId, JSON.stringify(run));
    for (const wake of this.waiters) wake();
  }
  report(id) {
    const run = this.get(id);
    return structuredClone({ runId: id, name: run.spec.name, status: run.status, revision: run.revision,
      createdAt: run.createdAt, updatedAt: run.updatedAt, recoveryNote: run.recoveryNote,
      nodes: run.nodes.map(node => ({ ...node,
        history: node.history.map(({ attempt, ok, exitCode, error, logFile }) => ({ attempt, ok, exitCode, error, logFile })),
        ...(node.result ? { result: { ...node.result, stdout: utf8Tail(node.result.stdout || "",2048), stderr: utf8Tail(node.result.stderr || "",2048),
          outputTailTruncated: Buffer.byteLength(node.result.stdout || "") > 2048 || Buffer.byteLength(node.result.stderr || "") > 2048 } } : {})
      })), needsAttention: ["failed", "interrupted"].includes(run.status) });
  }
  list(limit = 20) { return [...this.runs.values()].sort((a,b) => b.createdAt.localeCompare(a.createdAt)).slice(0,limit).map(r => ({runId:r.runId,name:r.spec.name,status:r.status,revision:r.revision,createdAt:r.createdAt,updatedAt:r.updatedAt,nodes:r.nodes.map(n=>({id:n.id,status:n.status,attempts:n.attempts}))})); }
  start(input, idempotencyKey) {
    if (this.stopping) throw new Error("Pipeline engine is stopping");
    const spec = validatePipeline(input);
    const hash = createHash("sha256").update(JSON.stringify(spec)).digest("hex");
    const existing = idempotencyKey && [...this.runs.values()].find(r => r.idempotencyKey === idempotencyKey);
    if (existing) {
      if (existing.hash !== hash) throw new Error("Idempotency key already used for a different task graph");
      return this.report(existing.runId);
    }
    if ([...this.runs.values()].filter(r => r.status === "running").length >= 32) throw new Error("Too many active pipelines");
    const run = { runId: randomUUID(), spec, hash, idempotencyKey, status: "running", createdAt: new Date().toISOString(),
      nodes: spec.nodes.map(n => ({ id: n.id, status: "pending", attempts: 0, history: [] })) };
    this.runs.set(run.runId, run); this.save(run); this.schedule();
    return this.report(run.runId);
  }
  schedule() {
    if (this.stopping) return;
    for (const run of this.runs.values()) {
      if (run.status !== "running") continue;
      let changed = true;
      while (changed) {
        changed = false;
        for (const node of run.nodes.filter(n => n.status === "pending")) {
          const spec = this.nodeSpec(run, node.id);
          const dependencies = spec.dependsOn.map(id => run.nodes.find(n => n.id === id));
          if (!dependencies.every(n => terminal(n.status))) continue;
          const failed = dependencies.some(n => n.status !== "succeeded");
          if ((spec.when === "success" && failed) || (spec.when === "failure" && !failed)) {
            node.status = "skipped"; node.reason = "Dependency condition not met"; this.save(run); changed = true; continue;
          }
          if (spec.if) {
            try {
              if (!evaluateExpression(spec.if, buildPipelineContext(run, node.id))) {
                node.status = "skipped"; node.reason = `Condition evaluated false: ${spec.if}`; this.save(run); changed = true; continue;
              }
            } catch (error) {
              node.status = "failed"; node.error = `Condition evaluation failed: ${error.message}`; this.save(run); changed = true; continue;
            }
          }
          if (this.active.size >= this.maxRunningNodes || run.nodes.filter(n => n.status === "running").length >= run.spec.parallelism) continue;
          if (spec.locks.some(lock => this.locks.has(this.lockKey(lock)))) continue;
          const key = `${run.runId}:${node.id}`;
          for (const lock of spec.locks) this.locks.set(this.lockKey(lock), key);
          node.status = "running"; this.save(run);
          const task = { run, node, child: null, abort: null, controller: new AbortController() };
          this.active.set(key, task);
          task.promise = this.execute(task).catch(error => {
            node.status = this.stopping ? "interrupted" : run.status === "cancelled" ? "cancelled" : "failed";
            node.error = error.message; this.save(run);
          }).finally(() => {
            this.active.delete(key);
            for (const lock of spec.locks) if (this.locks.get(this.lockKey(lock)) === key && node.status !== "interrupted") this.locks.delete(this.lockKey(lock));
            this.schedule();
          });
          changed = true;
        }
      }
      if (run.nodes.every(n => terminal(n.status))) {
        run.status = run.nodes.some(n => n.status === "failed") ? "failed" : "succeeded";
        this.save(run);
      }
    }
  }
  async execute(task) {
    const {run, node} = task, spec = this.nodeSpec(run, node.id);
    const loopStarted = Date.now();
    const maxAttempts = spec.retryUntil?.maxAttempts ?? (spec.retries + 1);
    for (let attemptIndex = 0; attemptIndex < maxAttempts; attemptIndex++) {
      if (this.stopping || run.status !== "running") break;
      if (spec.retryUntil && Date.now() - loopStarted >= spec.retryUntil.timeoutMs) { node.status = "failed"; break; }
      node.attempts++; node.startedAt ||= new Date().toISOString(); this.save(run);
      let result = await withHeavyPermit(async () => {
        if (this.stopping || run.status !== "running") return { ok: false, error: "Cancelled before launch" };
        const remaining = spec.retryUntil ? spec.retryUntil.timeoutMs - (Date.now() - loopStarted) : spec.timeoutMs;
        if (remaining <= 0) return { ok: false, error: "retryUntil timed out" };
        return this.executeCommand(task, { ...spec, timeoutMs: Math.min(spec.timeoutMs, remaining) });
      }, { signal: task.controller.signal });

      let outputs = {};
      if (result.ok && Object.keys(spec.capture || {}).length) {
        try { outputs = captureOutputs(spec.capture, result); }
        catch (error) { result = { ...result, ok: false, error: error.message, captureError: true }; }
      }
      node.outputs = outputs;

      const commandOk = result.ok;
      const conditionMatched = commandOk && (!spec.retryUntil || matchesProbe(result, spec.retryUntil.condition));
      if (spec.retryUntil && commandOk && !conditionMatched) {
        result = { ...result, ok: false, commandOk: true, conditionMatched: false, error: "retryUntil condition not met" };
      } else if (spec.retryUntil) {
        result = { ...result, commandOk, conditionMatched };
      }

      node.history.push({ attempt: node.attempts, ...result });
      node.history = node.history.slice(-20);
      node.result = result;
      if (this.stopping || run.status !== "running") break;
      if (result.ok) { node.status = "succeeded"; break; }

      const exhaustedAttempts = attemptIndex + 1 >= maxAttempts;
      const retryTimeout = spec.retryUntil?.timeoutMs;
      const exhaustedTime = retryTimeout != null && Date.now() - loopStarted >= retryTimeout;
      if (exhaustedAttempts || exhaustedTime) { node.status = "failed"; break; }

      this.save(run);
      const delay = spec.retryUntil?.intervalMs ?? spec.retryDelayMs;
      const remainingRetryTime = retryTimeout == null ? delay : Math.max(0, retryTimeout - (Date.now() - loopStarted));
      if (retryTimeout != null && remainingRetryTime <= 0) { node.status = "failed"; break; }
      task.abort = () => task.controller.abort(new Error("Pipeline node wait cancelled"));
      try { await sleep(Math.min(delay, remainingRetryTime), task.controller.signal); }
      catch { break; }
      finally { task.abort = null; }
    }
    if (node.status === "running") node.status = this.stopping ? "interrupted" : run.status === "cancelled" ? "cancelled" : "failed";
    node.endedAt = new Date().toISOString(); this.save(run);
  }
  executeCommand(task, spec) {
    const {run, node} = task;
    const context = buildPipelineContext(run, node.id);
    const baseCwd = insideRoot(run.spec.cwd);
    const relativeCwd = spec.cwd ? interpolateTemplate(spec.cwd, context) : ".";
    const cwd = insideRoot(path.resolve(baseCwd, relativeCwd));
    const command = interpolateTemplate(spec.command, context);
    const shell = applyPowerShellExitGuard(shellCommand(command, runnerExecutable(spec.runner)));
    const logFile = path.join(this.directory, `${run.runId}-${node.id}-${node.attempts}.log`);
    node.logFile = path.basename(logFile);
    return new Promise(resolve => {
      const child = spawn(shell.executable, shell.args, { cwd, windowsHide: true, detached: process.platform !== "win32", stdio: ["ignore", "pipe", "pipe"] });
      task.child = child; node.pid = child.pid ?? null; this.save(run);
      let stdout = "", stderr = "", written = 0, logTruncated = false, timedOut = false, spawnError;
      const append = (stream, text) => {
        if (stream === "stdout") stdout = utf8Tail(stdout + text, 65536);
        else stderr = utf8Tail(stderr + text, 65536);
        const bytes = Buffer.from(`[${stream}] ${text}`);
        const remaining = spec.maxLogBytes - written;
        if (remaining > 0) {
          const part = bytes.subarray(0, remaining);
          try { fs.appendFileSync(logFile, part); written += part.length; }
          catch (error) { spawnError = `Log write failed: ${error.message}`; this.terminate(task, false); }
        }
        if (bytes.length > remaining) logTruncated = true;
      };
      child.stdout.setEncoding("utf8").on("data", data => append("stdout", data));
      child.stderr.setEncoding("utf8").on("data", data => append("stderr", data));
      child.on("error", error => { spawnError = error.message; });
      const timer = setTimeout(() => { timedOut = true; this.terminate(task, false); }, spec.timeoutMs);
      child.once("close", async (exitCode, signal) => {
        clearTimeout(timer); task.child = null;
        let error = spawnError || (timedOut ? "Node timed out" : null);
        const artifacts = [];
        if (!error && exitCode === 0) {
          try {
            const expectedText = spec.expect.stdoutIncludes ? interpolateTemplate(spec.expect.stdoutIncludes, context) : null;
            if (expectedText && !stdout.includes(expectedText)) error = "Expected text absent from bounded stdout tail";
          } catch (verificationError) { error = verificationError.message; }
          for (const input of spec.expect.artifacts) {
            try {
              const resolvedInput = interpolateTemplate(input, context);
              const file = insideRoot(path.resolve(cwd, resolvedInput)); const stat = fs.statSync(file);
              artifacts.push({ path: file, bytes: stat.size, type: stat.isFile() ? "file" : "directory" });
            } catch { error = `Expected artifact missing or outside workspace: ${input}`; }
          }
        }
        resolve({ ok: !error && exitCode === 0 && run.status === "running" && !this.stopping,
          exitCode, signal, timedOut, error, stdout: utf8Tail(stdout, 16384), stderr: utf8Tail(stderr, 16384), logTruncated,
          logFile: path.basename(logFile), artifacts, endedAt: new Date().toISOString() });
      });
    });
  }
  terminate(task, cancelWait = true) {
    if (cancelWait) task.controller.abort();
    task.abort?.();
    const child = task.child;
    if (!child?.pid) return;
    if (process.platform === "win32") {
      execFile("taskkill.exe", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true }, error => { if (error) child.kill(); });
    } else {
      try { process.kill(-child.pid, "SIGKILL"); } catch { child.kill("SIGKILL"); }
    }
  }
  async wait(id, afterRevision = -1, waitMs = 30000) {
    const run = this.get(id);
    const settled = () => run.status !== "running" && !run.nodes.some(n => n.status === "running");
    if (run.revision > afterRevision || settled() || waitMs === 0) return this.report(id);
    await new Promise(resolve => {
      const done = () => { if (run.revision > afterRevision || settled() || this.stopping) finish(); };
      const finish = () => { clearTimeout(timer); this.waiters.delete(done); resolve(); };
      const timer = setTimeout(finish, Math.min(waitMs, 30000)); this.waiters.add(done);
    });
    return this.report(id);
  }
  async cancel(id, confirmInterrupted = false) {
    const run = this.get(id);
    if (run.status === "interrupted" && !confirmInterrupted) throw new Error("Reconcile orphan processes before confirmInterrupted=true");
    if (!["running", "interrupted"].includes(run.status)) return this.report(id);
    run.status = "cancelled";
    for (const node of run.nodes) if (node.status === "pending" || node.status === "interrupted") node.status = "cancelled";
    this.save(run);
    const tasks = [...this.active.values()].filter(t => t.run === run);
    for (const task of tasks) this.terminate(task);
    // Do not block the MCP call on process shutdown or a resource permit.
    for (const [lock, owner] of this.locks) if (owner.startsWith(`${id}:`) && !this.active.has(owner)) this.locks.delete(lock);
    this.schedule(); return this.report(id);
  }
  resume(id, { nodes, confirmInterrupted = false } = {}) {
    const run = this.get(id);
    if (this.stopping || [...this.active.values()].some(t => t.run === run)) throw new Error("Pipeline still has active nodes or engine is stopping");
    if (!["failed", "cancelled", "interrupted"].includes(run.status)) throw new Error("Only failed, cancelled or interrupted pipelines can resume");
    if (run.nodes.some(n => n.status === "interrupted") && !confirmInterrupted) throw new Error("Reconcile uncertain side effects and orphan processes before confirmInterrupted=true");
    const selected = new Set(nodes || run.nodes.filter(n => ["failed", "cancelled", "interrupted"].includes(n.status) || (run.status === "interrupted" && n.status === "pending")).map(n => n.id));
    for (const id of selected) if (!run.nodes.some(n => n.id === id)) throw new Error(`Unknown node: ${id}`);
    if (!selected.size) throw new Error("No nodes selected for resume");
    let changed = true;
    while (changed) { changed = false; for (const n of run.spec.nodes) if (!selected.has(n.id) && n.dependsOn.some(d => selected.has(d))) { selected.add(n.id); changed = true; } }
    for (const node of run.nodes) if (selected.has(node.id)) {
      node.status = "pending"; delete node.reason; delete node.result; delete node.error; delete node.pid;
    }
    for (const [lock, owner] of this.locks) if (owner.startsWith(`${id}:`)) this.locks.delete(lock);
    run.status = "running"; delete run.recoveryNote; this.save(run); this.schedule(); return this.report(id);
  }
  logs(id, nodeId, attempt, offset = 0, maxBytes = 65536) {
    const node = this.get(id).nodes.find(n => n.id === nodeId);
    if (!node) throw new Error(`Unknown node: ${nodeId}`);
    const file = path.join(this.directory, `${id}-${nodeId}-${attempt || node.attempts}.log`);
    if (!fs.existsSync(file)) return { text: "", nextOffset: offset, eof: true };
    const fd = fs.openSync(file, "r");
    try {
      const buffer = Buffer.alloc(Math.min(maxBytes, 65536)); const count = fs.readSync(fd, buffer, 0, buffer.length, offset);
      return { text: buffer.subarray(0,count).toString("utf8"), nextOffset: offset + count, eof: offset + count >= fs.fstatSync(fd).size };
    } finally { fs.closeSync(fd); }
  }
  async close() {
    this.stopping = true;
    for (const run of this.runs.values()) if (run.status === "running") { run.status = "interrupted"; this.save(run); }
    const tasks = [...this.active.values()]; for (const task of tasks) this.terminate(task);
    await Promise.all(tasks.map(t => t.promise));
    this.db.close(); fs.unlinkSync(this.ownerFile);
  }
}

let engine;
export function pipelineDeploymentState() {
  return { activePipelines: engine ? [...engine.runs.values()].filter(r=>r.status === "running").length : 0,
    activePipelineNodes: engine?.active.size || 0,
    quarantinedPipelineLocks: engine ? [...engine.runs.values()].filter(r=>r.status === "interrupted").length : 0 };
}
export function getPipelineEngine() {
  return engine ||= new PipelineEngine(config.pipelineDir, { maxRunningNodes: config.pipelineMaxRunningNodes });
}
export async function shutdownPipelines() { if (engine) { await engine.close(); engine = null; } }

export function registerPipelineTools(server) {
  const response = value => ({ content: [{ type: "text", text: JSON.stringify(value) }] });
  const enabled = () => { if (!config.shellEnabled) throw new Error("Pipelines require RACCOON_ENABLE_SHELL=1"); };
  const runId = z.string().uuid();
  server.registerTool("wait_until", {
    description: "Run an idempotent command repeatedly until a structured stdout/stderr/JSON condition matches or a timeout/attempt limit is reached. Replaces model-side polling loops.",
    inputSchema: {
      command: z.string().min(1).max(65536),
      cwd: z.string().default("."),
      runner: runnerSchema,
      condition: probeConditionSchema,
      intervalMs: z.number().int().min(100).max(60000).default(10000),
      timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(300000),
      commandTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(120000),
      maxAttempts: z.number().int().min(1).max(10000).default(60),
      maxOutputBytes: z.number().int().min(1024).max(config.maxShellOutputBytes).default(Math.min(1024 * 1024, config.maxShellOutputBytes)),
      idempotent: z.boolean().default(false)
    }
  }, async args => { enabled(); return response(await waitUntil(args)); });
  server.registerTool("dev_pipeline_validate", { description: "Validate a persistent task DAG with variable capture, safe conditions, retryUntil polling, locks and explicit runners without executing it.", inputSchema: { pipeline: pipelineSchema } }, ({pipeline}) => response({ok:true, pipeline:validatePipeline(pipeline)}));
  server.registerTool("dev_pipeline_start", { description: "Start a persistent background DAG. Returns immediately. Locks coordinate pipelines in this service; use matching lock names for shared resources. Retries require idempotent nodes.", inputSchema: { pipeline: pipelineSchema, idempotencyKey: z.string().min(1).max(200).optional() } }, ({pipeline,idempotencyKey}) => { enabled(); return response(getPipelineEngine().start(pipeline,idempotencyKey)); });
  server.registerTool("dev_pipeline_status", { description: "Read pipeline state, bounded result tails and artifact paths.", inputSchema: { runId } }, ({runId}) => response(getPipelineEngine().report(runId)));
  server.registerTool("dev_pipeline_list", { description: "List persistent pipeline runs, newest first.", inputSchema: { limit:z.number().int().min(1).max(100).default(20) } }, ({limit}) => response(getPipelineEngine().list(limit)));
  server.registerTool("dev_pipeline_wait", { description: "Wait up to 30s for revision change or completion; supply last revision to avoid polling unchanged state. Tasks continue if this request disconnects.", inputSchema: { runId, afterRevision:z.number().int().min(-1).default(-1), waitMs:z.number().int().min(0).max(30000).default(30000) } }, async args => response(await getPipelineEngine().wait(args.runId,args.afterRevision,args.waitMs)));
  server.registerTool("dev_pipeline_logs", { description: "Read bounded persisted node logs. Logs may contain command output; no automatic secret scrubbing.", inputSchema: { runId, nodeId:identifier, attempt:z.number().int().positive().optional(), offset:z.number().int().nonnegative().default(0), maxBytes:z.number().int().min(1).max(65536).default(65536) } }, args => response(getPipelineEngine().logs(args.runId,args.nodeId,args.attempt,args.offset,args.maxBytes)));
  server.registerTool("dev_pipeline_resume", { description: "Retry selected nodes and their descendants, preserving other completed results. Interrupted executions require reconciliation before confirmInterrupted=true. Resume does not change commands; submit a new graph to change them.", inputSchema: { runId, nodes:z.array(identifier).min(1).optional(), confirmInterrupted:z.boolean().default(false) } }, ({runId,...options}) => { enabled(); return response(getPipelineEngine().resume(runId,options)); });
  server.registerTool("dev_pipeline_cancel", { description: "Cancel queued nodes and terminate owned process trees. Already-started external side effects are not automatically rolled back. Poll state until active nodes settle.", inputSchema: { runId, confirmInterrupted:z.boolean().default(false) } }, async ({runId,confirmInterrupted}) => { enabled(); return response(await getPipelineEngine().cancel(runId,confirmInterrupted)); });
}
