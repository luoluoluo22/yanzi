import path from "node:path";
import fs from "node:fs/promises";
import fsSync from "node:fs";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { fileURLToPath } from "node:url";
import { z } from "zod";

const runtimeDir = path.resolve(process.env.RACCOON_FOREGROUND_RUNTIME_DIR || ".raccoon-runtime/foreground-control");
const stateFile = path.join(runtimeDir, "state.json");
const commandFile = path.join(runtimeDir, "command.json");
const overlayScript = fileURLToPath(new URL("./foreground-overlay.ps1", import.meta.url));

let lease = null;
let overlayProcess = null;
let monitor = null;
let monitorBusy = false;

function nowIso() {
  return new Date().toISOString();
}

function overlayEnabled() {
  return process.platform === "win32" && process.env.RACCOON_FOREGROUND_OVERLAY !== "0";
}

function snapshot() {
  if (!lease) return { active: false, updatedAt: nowIso() };
  const now = Date.now();
  return {
    active: true,
    leaseId: lease.leaseId,
    owner: lease.owner,
    task: lease.task,
    paused: lease.paused,
    acquiredAt: new Date(lease.acquiredAt).toISOString(),
    heartbeatAt: new Date(lease.heartbeatAt).toISOString(),
    expiresAt: new Date(lease.expiresAt).toISOString(),
    remainingMs: Math.max(0, lease.expiresAt - now)
  };
}

async function writeJsonAtomic(file, value) {
  await fs.mkdir(path.dirname(file), { recursive: true });
  const temp = file + "." + process.pid + ".tmp";
  await fs.writeFile(temp, JSON.stringify(value, null, 2), "utf8");
  await fs.rename(temp, file);
}

async function persist() {
  await writeJsonAtomic(stateFile, snapshot());
}

async function expireIfNeeded() {
  if (!lease || Date.now() <= lease.expiresAt) return false;
  lease = null;
  await persist();
  return true;
}

async function readOverlayCommand() {
  let command;
  try {
    command = JSON.parse(await fs.readFile(commandFile, "utf8"));
  } catch (error) {
    if (error.code === "ENOENT") return null;
    await fs.rm(commandFile, { force: true }).catch(() => {});
    return null;
  }
  await fs.rm(commandFile, { force: true }).catch(() => {});
  return command;
}

async function applyOverlayCommand(command) {
  if (!command || !lease || command.leaseId !== lease.leaseId) return;
  if (command.action === "release") {
    lease = null;
    await persist();
    return;
  }
  if (command.action === "togglePause") {
    lease.paused = !lease.paused;
    lease.heartbeatAt = Date.now();
    lease.expiresAt = lease.heartbeatAt + lease.ttlMs;
    await persist();
  }
}

function ensureMonitor() {
  if (monitor) return;
  monitor = setInterval(async () => {
    if (monitorBusy) return;
    monitorBusy = true;
    try {
      await expireIfNeeded();
      await applyOverlayCommand(await readOverlayCommand());
    } catch (error) {
      console.error("Foreground-control monitor:", error.message);
    } finally {
      monitorBusy = false;
    }
  }, 250);
  monitor.unref();
}

function ensureOverlay() {
  if (!overlayEnabled()) return;
  if (overlayProcess && overlayProcess.exitCode === null && !overlayProcess.killed) return;
  fsSync.mkdirSync(runtimeDir, { recursive: true });
  const stdoutFd = fsSync.openSync(path.join(runtimeDir, "overlay.stdout.log"), "a");
  const stderrFd = fsSync.openSync(path.join(runtimeDir, "overlay.stderr.log"), "a");
  let child;
  try {
    child = spawn("powershell.exe", [
      "-NoLogo",
      "-NoProfile",
      "-Sta",
      "-WindowStyle", "Hidden",
      "-ExecutionPolicy", "Bypass",
      "-File", overlayScript,
      "-StateFile", stateFile,
      "-CommandFile", commandFile
    ], {
      // PowerShell itself is hidden by -WindowStyle Hidden. Do not also set
      // STARTF_USESHOWWINDOW/SW_HIDE here, because that suppresses the child
      // WinForms overlay when Raccoon launches it directly from Node.
      windowsHide: false,
      stdio: ["ignore", stdoutFd, stderrFd]
    });
  } finally {
    fsSync.closeSync(stdoutFd);
    fsSync.closeSync(stderrFd);
  }
  overlayProcess = child;
  const processLog = path.join(runtimeDir, "overlay-process.log");
  fsSync.appendFileSync(processLog, `spawn pid=${child.pid ?? "unknown"} at=${nowIso()}\n`, "utf8");
  child.once("error", error => {
    fsSync.appendFileSync(processLog, `error at=${nowIso()} message=${String(error?.message || error)}\n`, "utf8");
  });
  child.once("exit", (code, signal) => {
    fsSync.appendFileSync(processLog, `exit at=${nowIso()} code=${code} signal=${signal || ""}\n`, "utf8");
    if (overlayProcess === child) overlayProcess = null;
  });
}

function requireLease(leaseId) {
  if (!lease) throw new Error("No foreground-control lease is active.");
  if (lease.leaseId !== leaseId) {
    throw new Error(`Foreground control is owned by ${lease.owner}; the supplied leaseId is not valid.`);
  }
  return lease;
}

export async function initializeForegroundControl() {
  ensureMonitor();
  await fs.mkdir(runtimeDir, { recursive: true });
  await fs.rm(commandFile, { force: true }).catch(() => {});
  lease = null;
  await persist();
}

export async function acquireForegroundControl({
  owner = "ChatGPT",
  task = "操作电脑",
  ttlMs = 10000
} = {}) {
  ensureMonitor();
  await expireIfNeeded();
  if (lease) {
    throw new Error(`Foreground control is already owned by ${lease.owner}: ${lease.task}`);
  }
  const now = Date.now();
  lease = {
    leaseId: randomUUID(),
    owner: String(owner).trim() || "ChatGPT",
    task: String(task).trim() || "操作电脑",
    paused: false,
    ttlMs,
    acquiredAt: now,
    heartbeatAt: now,
    expiresAt: now + ttlMs
  };
  await persist();
  ensureOverlay();
  return snapshot();
}

export async function heartbeatForegroundControl({ leaseId, task } = {}) {
  await expireIfNeeded();
  const current = requireLease(leaseId);
  const now = Date.now();
  current.heartbeatAt = now;
  current.expiresAt = now + current.ttlMs;
  if (typeof task === "string" && task.trim()) current.task = task.trim();
  await persist();
  return snapshot();
}

export async function pauseForegroundControl({ leaseId, paused = true } = {}) {
  await expireIfNeeded();
  const current = requireLease(leaseId);
  current.paused = Boolean(paused);
  current.heartbeatAt = Date.now();
  current.expiresAt = current.heartbeatAt + current.ttlMs;
  await persist();
  return snapshot();
}

export async function releaseForegroundControl({ leaseId } = {}) {
  await expireIfNeeded();
  if (!lease) return snapshot();
  requireLease(leaseId);
  lease = null;
  await persist();
  return snapshot();
}

export async function foregroundControlStatus() {
  ensureMonitor();
  await expireIfNeeded();
  return snapshot();
}

export function foregroundControlStatePath() {
  return stateFile;
}

export async function assertForegroundControlReady(leaseId) {
  await expireIfNeeded();
  const current = requireLease(leaseId);
  if (current.paused) throw new Error("Foreground control is paused by the user.");
  return snapshot();
}

export async function withForegroundControl(meta, operation) {
  const acquired = await acquireForegroundControl(meta);
  const heartbeatMs = Math.max(500, Math.floor((meta?.ttlMs || 10000) / 3));
  const timer = setInterval(() => {
    heartbeatForegroundControl({ leaseId: acquired.leaseId }).catch(() => {});
  }, heartbeatMs);
  timer.unref();
  try {
    return await operation(acquired);
  } finally {
    clearInterval(timer);
    await releaseForegroundControl({ leaseId: acquired.leaseId }).catch(() => {});
  }
}

export async function shutdownForegroundControl() {
  if (monitor) {
    clearInterval(monitor);
    monitor = null;
  }
  lease = null;
  await persist().catch(() => {});
  if (overlayProcess?.pid) {
    try { process.kill(overlayProcess.pid); } catch {}
  }
  overlayProcess = null;
}

export function registerForegroundControlTools(server) {
  server.registerTool("foreground_control_acquire", {
    title: "Acquire foreground control",
    description: "Reserve the human-interaction foreground for AI GUI work and show a topmost Windows overlay until released or heartbeat expiry.",
    inputSchema: {
      owner: z.string().min(1).max(100).default("ChatGPT"),
      task: z.string().min(1).max(300).default("操作电脑"),
      ttlMs: z.number().int().min(1000).max(120000).default(10000)
    }
  }, async args => ({
    content: [{ type: "text", text: JSON.stringify(await acquireForegroundControl(args), null, 2) }]
  }));

  server.registerTool("foreground_control_heartbeat", {
    title: "Heartbeat foreground control",
    description: "Renew the current foreground-control lease and optionally update the task shown in the overlay.",
    inputSchema: {
      leaseId: z.string().uuid(),
      task: z.string().min(1).max(300).optional()
    }
  }, async args => ({
    content: [{ type: "text", text: JSON.stringify(await heartbeatForegroundControl(args), null, 2) }]
  }));

  server.registerTool("foreground_control_pause", {
    title: "Pause or resume foreground control",
    description: "Pause or resume the active AI foreground-control lease. GUI automation should refuse input while paused.",
    inputSchema: {
      leaseId: z.string().uuid(),
      paused: z.boolean().default(true)
    }
  }, async args => ({
    content: [{ type: "text", text: JSON.stringify(await pauseForegroundControl(args), null, 2) }]
  }));

  server.registerTool("foreground_control_release", {
    title: "Release foreground control",
    description: "Release the active foreground-control lease and dismiss the Windows overlay.",
    inputSchema: {
      leaseId: z.string().uuid()
    }
  }, async args => ({
    content: [{ type: "text", text: JSON.stringify(await releaseForegroundControl(args), null, 2) }]
  }));

  server.registerTool("foreground_control_status", {
    title: "Foreground control status",
    description: "Return the current foreground-control owner, task, pause state and heartbeat expiry.",
    inputSchema: {}
  }, async () => ({
    content: [{ type: "text", text: JSON.stringify(await foregroundControlStatus(), null, 2) }]
  }));
}
