import os from "node:os";
import { randomUUID } from "node:crypto";
import { config } from "./config.js";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const watchers = new Map();

const governor = {
  enabled: config.governorEnabled,
  maxConcurrentHeavyOps: config.governorMaxConcurrentHeavyOps,
  maxSystemCpuPercent: config.governorMaxSystemCpuPercent,
  minFreeMemoryBytes: config.governorMinFreeMemoryBytes,
  pollMs: config.governorPollMs,
  active: 0,
  queue: [],
  lastCpuPercent: 0,
  lastSampleAt: Date.now()
};

let previousCpu = cpuCounters();
let governorTimer = null;

function cpuCounters() {
  let idle = 0;
  let total = 0;
  for (const cpu of os.cpus()) {
    const times = cpu.times;
    idle += times.idle;
    total += times.user + times.nice + times.sys + times.idle + times.irq;
  }
  return { idle, total };
}

function refreshCpu() {
  const next = cpuCounters();
  const idleDelta = next.idle - previousCpu.idle;
  const totalDelta = next.total - previousCpu.total;
  governor.lastCpuPercent = totalDelta > 0 ? (1 - idleDelta / totalDelta) * 100 : 0;
  governor.lastSampleAt = Date.now();
  previousCpu = next;
  drainGovernor();
}

function ensureGovernorTimer() {
  if (governor.enabled && !governorTimer) {
    previousCpu = cpuCounters();
    governorTimer = setInterval(refreshCpu, governor.pollMs);
    governorTimer.unref();
  } else if (!governor.enabled && governorTimer) {
    clearInterval(governorTimer);
    governorTimer = null;
  }
}

function resourceAllowsHeavyOp() {
  if (!governor.enabled) return true;
  return governor.active < governor.maxConcurrentHeavyOps &&
    os.freemem() >= governor.minFreeMemoryBytes &&
    governor.lastCpuPercent <= governor.maxSystemCpuPercent;
}

function drainGovernor() {
  while (governor.queue.length && resourceAllowsHeavyOp()) {
    const queued = governor.queue.shift();
    queued.signal?.removeEventListener("abort", queued.abort);
    governor.active += 1;
    queued.resolve();
  }
}

async function acquireHeavyPermit(signal) {
  signal?.throwIfAborted();
  if (!governor.enabled) return () => {};
  if (resourceAllowsHeavyOp()) {
    governor.active += 1;
  } else {
    await new Promise((resolve, reject) => {
      const queued = { resolve, signal, abort: () => {
        const index = governor.queue.indexOf(queued);
        if (index >= 0) governor.queue.splice(index, 1);
        reject(signal.reason || new Error("Resource wait cancelled"));
      } };
      governor.queue.push(queued);
      signal?.addEventListener("abort", queued.abort, { once: true });
    });
  }

  let released = false;
  return () => {
    if (released) return;
    released = true;
    governor.active = Math.max(0, governor.active - 1);
    drainGovernor();
  };
}

ensureGovernorTimer();

export async function withHeavyPermit(fn, { signal } = {}) {
  const release = await acquireHeavyPermit(signal);
  try {
    return await fn();
  } finally {
    release();
  }
}

export function configureGovernor(options = {}) {
  if (options.enabled != null) governor.enabled = Boolean(options.enabled);
  if (options.maxConcurrentHeavyOps != null) {
    const value = Number(options.maxConcurrentHeavyOps);
    if (!Number.isInteger(value) || value < 1 || value > 1024) throw new Error("maxConcurrentHeavyOps must be 1..1024");
    governor.maxConcurrentHeavyOps = value;
  }
  if (options.maxSystemCpuPercent != null) {
    const value = Number(options.maxSystemCpuPercent);
    if (!(value > 0 && value <= 100)) throw new Error("maxSystemCpuPercent must be >0 and <=100");
    governor.maxSystemCpuPercent = value;
  }
  if (options.minFreeMemoryBytes != null) {
    const value = Number(options.minFreeMemoryBytes);
    if (!Number.isSafeInteger(value) || value < 0) throw new Error("minFreeMemoryBytes must be a non-negative integer");
    governor.minFreeMemoryBytes = value;
  }
  if (options.pollMs != null) {
    const value = Number(options.pollMs);
    if (!Number.isInteger(value) || value < 100 || value > 60000) throw new Error("pollMs must be 100..60000");
    governor.pollMs = value;
    if (governorTimer) {
      clearInterval(governorTimer);
      governorTimer = null;
    }
  }
  ensureGovernorTimer();
  drainGovernor();
  return governorStatus();
}

export function governorStatus() {
  return {
    enabled: governor.enabled,
    maxConcurrentHeavyOps: governor.maxConcurrentHeavyOps,
    maxSystemCpuPercent: governor.maxSystemCpuPercent,
    minFreeMemoryBytes: governor.minFreeMemoryBytes,
    pollMs: governor.pollMs,
    activeHeavyOps: governor.active,
    queuedHeavyOps: governor.queue.length,
    systemCpuPercent: governor.lastCpuPercent,
    freeMemoryBytes: os.freemem(),
    totalMemoryBytes: os.totalmem(),
    lastSampleAt: new Date(governor.lastSampleAt).toISOString()
  };
}

async function powershellProcessSnapshot(pid, sampleMs = 250) {
  if (process.platform !== "win32") {
    throw new Error("Detailed process metrics are currently implemented for Windows.");
  }
  const logical = os.cpus().length;
  const script = [
    "$ErrorActionPreference='Stop'",
    "$p1=Get-Process -Id " + Number(pid),
    "$cpu1=[double]($p1.CPU)",
    "Start-Sleep -Milliseconds " + Number(sampleMs),
    "$p2=Get-Process -Id " + Number(pid),
    "$cpu2=[double]($p2.CPU)",
    "$delta=[math]::Max(0,$cpu2-$cpu1)",
    "$pct=($delta/(" + Number(sampleMs) + "/1000.0)/" + logical + ")*100",
    "$start=try{$p2.StartTime.ToString('o')}catch{$null}",
    "$procPath=try{$p2.Path}catch{$null}",
    "$obj=[PSCustomObject]@{pid=$p2.Id;name=$p2.ProcessName;cpuPercent=$pct;cpuSeconds=[double]($p2.CPU);workingSetBytes=[int64]$p2.WorkingSet64;privateMemoryBytes=[int64]$p2.PrivateMemorySize64;virtualMemoryBytes=[int64]$p2.VirtualMemorySize64;handles=$p2.HandleCount;threadCount=$p2.Threads.Count;priorityClass=[string]$p2.PriorityClass;responding=$p2.Responding;startTime=$start;path=$procPath}",
    "$obj | ConvertTo-Json -Compress"
  ].join(";");
  const { stdout } = await execFileAsync("powershell.exe", [
    "-NoLogo", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script
  ], {
    windowsHide: true,
    timeout: Math.max(30000, sampleMs + 10000),
    maxBuffer: 1024 * 1024,
    encoding: "utf8"
  });
  return JSON.parse(stdout.trim());
}

async function topProcesses(limit = 10) {
  if (process.platform !== "win32") return [];
  const script = [
    "$items=Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First " + Number(limit) + " Id,ProcessName,CPU,WorkingSet64,PrivateMemorySize64,HandleCount",
    "$items | ConvertTo-Json -Compress"
  ].join(";");
  const { stdout } = await execFileAsync("powershell.exe", [
    "-NoLogo", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script
  ], {
    windowsHide: true,
    timeout: 30000,
    maxBuffer: 4 * 1024 * 1024,
    encoding: "utf8"
  });
  const parsed = stdout.trim() ? JSON.parse(stdout) : [];
  return Array.isArray(parsed) ? parsed : [parsed];
}

export async function systemResourceSnapshot({ sampleMs = 250, top = 10 } = {}) {
  const before = cpuCounters();
  await new Promise(resolve => setTimeout(resolve, sampleMs));
  const after = cpuCounters();
  const idleDelta = after.idle - before.idle;
  const totalDelta = after.total - before.total;
  const cpuPercent = totalDelta > 0 ? (1 - idleDelta / totalDelta) * 100 : 0;
  return {
    cpuPercent,
    logicalCpus: os.cpus().length,
    totalMemoryBytes: os.totalmem(),
    freeMemoryBytes: os.freemem(),
    usedMemoryBytes: os.totalmem() - os.freemem(),
    uptimeSeconds: os.uptime(),
    loadAverage: os.loadavg(),
    topProcesses: await topProcesses(top),
    governor: governorStatus()
  };
}

export async function processResourceSnapshot({ pid, sampleMs = 250 }) {
  return powershellProcessSnapshot(pid, sampleMs);
}

async function setPriority(pid, priority) {
  const allowed = new Set(["Idle", "BelowNormal", "Normal", "AboveNormal", "High"]);
  if (!allowed.has(priority)) throw new Error("Unsupported priority: " + priority);
  const script = "$p=Get-Process -Id " + Number(pid) + "; $p.PriorityClass='" + priority + "'; [string]$p.PriorityClass";
  const { stdout } = await execFileAsync("powershell.exe", [
    "-NoLogo", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script
  ], {
    windowsHide: true,
    timeout: 30000,
    maxBuffer: 1024 * 1024,
    encoding: "utf8"
  });
  return stdout.trim();
}

async function killPid(pid) {
  if (process.platform === "win32") {
    await execFileAsync("taskkill.exe", ["/PID", String(pid), "/T", "/F"], {
      windowsHide: true,
      timeout: 30000,
      encoding: "utf8"
    });
  } else {
    process.kill(pid, "SIGKILL");
  }
}

function watcherSummary(watcher) {
  return {
    watchId: watcher.id,
    pid: watcher.pid,
    intervalMs: watcher.intervalMs,
    maxCpuPercent: watcher.maxCpuPercent,
    maxMemoryBytes: watcher.maxMemoryBytes,
    action: watcher.action,
    priority: watcher.priority,
    status: watcher.status,
    startedAt: watcher.startedAt,
    violations: watcher.violations,
    lastSample: watcher.samples.at(-1) || null,
    samplesBuffered: watcher.samples.length
  };
}

async function watchTick(watcher) {
  if (watcher.runningTick || watcher.status !== "running") return;
  watcher.runningTick = true;
  try {
    const sample = await powershellProcessSnapshot(watcher.pid, Math.min(250, Math.max(100, watcher.intervalMs - 50)));
    sample.at = new Date().toISOString();
    watcher.samples.push(sample);
    if (watcher.samples.length > watcher.maxSamples) watcher.samples.shift();

    const cpuViolation = watcher.maxCpuPercent != null && sample.cpuPercent > watcher.maxCpuPercent;
    const memoryViolation = watcher.maxMemoryBytes != null && sample.workingSetBytes > watcher.maxMemoryBytes;
    if (cpuViolation || memoryViolation) {
      watcher.violations += 1;
      watcher.lastViolation = {
        at: sample.at,
        cpuViolation,
        memoryViolation,
        sample
      };
      if (watcher.action === "lower_priority") {
        await setPriority(watcher.pid, watcher.priority);
      } else if (watcher.action === "kill") {
        await killPid(watcher.pid);
        watcher.status = "terminated";
        clearInterval(watcher.timer);
      }
    }
  } catch (error) {
    watcher.lastError = String(error?.message || error);
    if (/Cannot find a process|No process|process.*not found/i.test(watcher.lastError)) {
      watcher.status = "ended";
      clearInterval(watcher.timer);
    }
  } finally {
    watcher.runningTick = false;
  }
}

export function processWatchStart({
  pid,
  intervalMs = 1000,
  maxCpuPercent,
  maxMemoryBytes,
  action = "none",
  priority = "BelowNormal",
  maxSamples = 1000
}) {
  if (!Number.isInteger(pid) || pid <= 0) throw new Error("pid must be a positive integer");
  if (!["none", "lower_priority", "kill"].includes(action)) throw new Error("action must be none, lower_priority, or kill");
  const id = randomUUID();
  const watcher = {
    id,
    pid,
    intervalMs,
    maxCpuPercent,
    maxMemoryBytes,
    action,
    priority,
    maxSamples,
    status: "running",
    startedAt: new Date().toISOString(),
    violations: 0,
    samples: [],
    lastViolation: null,
    lastError: null,
    runningTick: false,
    timer: null
  };
  watcher.timer = setInterval(() => void watchTick(watcher), intervalMs);
  watcher.timer.unref();
  watchers.set(id, watcher);
  void watchTick(watcher);
  return watcherSummary(watcher);
}

export function processWatchRead({ watchId, tail = 20 }) {
  const watcher = watchers.get(watchId);
  if (!watcher) throw new Error("Unknown resource watch: " + watchId);
  return {
    ...watcherSummary(watcher),
    lastViolation: watcher.lastViolation,
    lastError: watcher.lastError,
    samples: watcher.samples.slice(-tail)
  };
}

export function processWatchStop({ watchId }) {
  const watcher = watchers.get(watchId);
  if (!watcher) return { ok: true, watchId, alreadyStopped: true };
  clearInterval(watcher.timer);
  watcher.status = "stopped";
  watchers.delete(watchId);
  return { ok: true, watchId, alreadyStopped: false };
}

export function processWatchList() {
  return [...watchers.values()].map(watcherSummary);
}
