import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { getRuntimeSetting } from "./runtime-settings.js";
import { config } from "./config.js";

const sessions = new Map();

function notify(session) {
  for (const resolve of session.waiters) resolve();
  session.waiters.clear();
}

function appendEvent(session, stream, data) {
  const buffer = Buffer.isBuffer(data) ? data : Buffer.from(String(data), "utf8");

  for (let offset = 0; offset < buffer.length;) {
    let end = Math.min(offset + config.processEventChunkBytes, buffer.length);
    while (end < buffer.length && (buffer[end] & 0xc0) === 0x80) end--;
    const chunk = buffer.subarray(offset, end);
    offset = end;
    const event = {
      seq: session.nextSeq++,
      at: new Date().toISOString(),
      stream,
      text: chunk.toString("utf8"),
      bytes: chunk.length
    };
    session.events.push(event);
    session.logBytes += event.bytes;
  }

  while (session.logBytes > session.maxLogBytes && session.events.length > 1) {
    const removed = session.events.shift();
    session.logBytes -= removed.bytes;
    session.droppedBeforeSeq = removed.seq + 1;
  }

  notify(session);
}

function markEnded(session, status, exitCode, signal) {
  session.status = status;
  session.exitCode = exitCode ?? null;
  session.signal = signal ?? null;
  session.endedAt = new Date().toISOString();
  notify(session);
}

function cleanupOldSessions() {
  const cutoff = Date.now() - 60 * 60 * 1000;
  for (const [id, session] of sessions) {
    if (session.status !== "running" && Date.parse(session.endedAt || 0) < cutoff) {
      sessions.delete(id);
    }
  }

  if (sessions.size >= config.maxProcessSessions) {
    const ended = [...sessions.values()]
      .filter(session => session.status !== "running")
      .sort((a, b) => (a.endedAt || "").localeCompare(b.endedAt || ""));

    while (sessions.size >= config.maxProcessSessions && ended.length > 0) {
      sessions.delete(ended.shift().id);
    }
  }
}

function windowsUtf8Command(command) {
  // Console encoding also sets the native code page; avoid spawning chcp.com.
  const init = [
    "$utf8 = New-Object System.Text.UTF8Encoding($false)",
    "[Console]::InputEncoding = $utf8",
    "[Console]::OutputEncoding = $utf8",
    "$OutputEncoding = $utf8"
  ].join("; ");
  return `${init}; ${command}`;
}

export function shellCommand(command, shellOverride) {
  if (process.platform === "win32") {
    const executable = shellOverride || process.env.RACCOON_SHELL || getRuntimeSetting("defaultShell") || "powershell.exe";
    const base = executable.toLowerCase().split(/[\\/]/).pop();
    if (base === "cmd.exe" || base === "cmd") {
      return {
        executable,
        args: ["/d", "/s", "/c", `chcp 65001>nul & ${command}`]
      };
    }
    if (base === "bash.exe" || base === "bash") {
      return {
        executable,
        args: ["-lc", command]
      };
    }
    return {
      executable,
      args: ["-NoLogo", "-NoProfile", "-WindowStyle", "Hidden", "-Command", windowsUtf8Command(command)]
    };
  }

  return {
    executable: shellOverride || process.env.RACCOON_SHELL || getRuntimeSetting("defaultShell") || process.env.SHELL || "/bin/sh",
    args: ["-lc", command]
  };
}

export function startManagedProcess({ command, cwd, maxLogBytes, shell: shellOverride }) {
  cleanupOldSessions();
  if (sessions.size >= config.maxProcessSessions) {
    throw new Error(`Process session limit reached (${config.maxProcessSessions})`);
  }

  const id = randomUUID();
  const shell = shellCommand(command, shellOverride);
  const child = spawn(shell.executable, shell.args, {
    cwd,
    detached: process.platform !== "win32",
    windowsHide: true,
    stdio: ["pipe", "pipe", "pipe"]
  });

  const session = {
    id,
    child,
    command,
    cwd,
    shell: shell.executable,
    pid: child.pid ?? null,
    status: "running",
    exitCode: null,
    signal: null,
    createdAt: new Date().toISOString(),
    endedAt: null,
    events: [],
    nextSeq: 0,
    droppedBeforeSeq: 0,
    logBytes: 0,
    maxLogBytes,
    waiters: new Set()
  };

  sessions.set(id, session);

  child.stdout.setEncoding("utf8").on("data", data => appendEvent(session, "stdout", data));
  child.stderr.setEncoding("utf8").on("data", data => appendEvent(session, "stderr", data));

  child.on("error", error => {
    appendEvent(session, "stderr", String(error?.stack || error));
    markEnded(session, "error", null, null);
  });

  child.on("close", (code, signal) => {
    if (session.status !== "error") markEnded(session, code === 0 ? "exited" : "failed", code, signal);
  });
  child.stdin.on("error", error => appendEvent(session, "stderr", `stdin: ${error.message}`));

  return summarize(session);
}

function getSession(id) {
  const session = sessions.get(id);
  if (!session) throw new Error(`Unknown process session: ${id}`);
  return session;
}

function summarize(session) {
  return {
    sessionId: session.id,
    pid: session.pid,
    command: session.command,
    cwd: session.cwd,
    shell: session.shell,
    status: session.status,
    exitCode: session.exitCode,
    signal: session.signal,
    createdAt: session.createdAt,
    endedAt: session.endedAt,
    nextCursor: session.nextSeq,
    droppedBeforeCursor: session.droppedBeforeSeq,
    bufferedBytes: session.logBytes
  };
}

function waitForChange(session, waitMs) {
  if (waitMs <= 0 || session.status !== "running") return Promise.resolve();

  return new Promise(resolve => {
    const timer = setTimeout(() => {
      session.waiters.delete(done);
      resolve();
    }, waitMs);

    const done = () => {
      clearTimeout(timer);
      resolve();
    };

    session.waiters.add(done);
  });
}

export async function readManagedProcess({ sessionId, cursor, maxBytes, waitMs }) {
  const session = getSession(sessionId);
  let effectiveCursor = Math.max(cursor, session.droppedBeforeSeq);

  const hasData = () => session.events.some(event => event.seq >= effectiveCursor);

  if (!hasData()) {
    await waitForChange(session, waitMs);
  }

  effectiveCursor = Math.max(cursor, session.droppedBeforeSeq);
  const selected = [];
  let bytes = 0;

  for (const event of session.events) {
    if (event.seq < effectiveCursor) continue;
    if (selected.length > 0 && bytes + event.bytes > maxBytes) break;

    if (selected.length === 0 && event.bytes > maxBytes) {
      const clipped = Buffer.from(event.text, "utf8").subarray(0, maxBytes).toString("utf8");
      selected.push({ ...event, text: clipped, bytes: Buffer.byteLength(clipped), clipped: true });
      bytes += Buffer.byteLength(clipped);
      break;
    }

    selected.push({ ...event, clipped: false });
    bytes += event.bytes;
  }

  const nextCursor = selected.length > 0
    ? selected[selected.length - 1].seq + 1
    : effectiveCursor;

  return {
    ...summarize(session),
    requestedCursor: cursor,
    effectiveCursor,
    nextCursor,
    cursorWasDropped: cursor < session.droppedBeforeSeq,
    events: selected
  };
}

export function writeManagedProcess({ sessionId, input, appendNewline }) {
  const session = getSession(sessionId);
  if (session.status !== "running") {
    throw new Error(`Process session is not running: ${session.status}`);
  }
  if (!session.child.stdin?.writable) {
    throw new Error("Process stdin is not writable.");
  }

  const payload = appendNewline ? input + "\n" : input;
  session.child.stdin.write(payload);

  return {
    ok: true,
    sessionId,
    bytes: Buffer.byteLength(payload)
  };
}

export function killManagedProcess({ sessionId }) {
  const session = getSession(sessionId);
  if (session.status !== "running") {
    return { ok: true, alreadyEnded: true, ...summarize(session) };
  }

  const signaled = terminate(session);
  return { ok: signaled, alreadyEnded: false, ...summarize(session) };
}

export function listManagedProcesses() {
  cleanupOldSessions();
  return [...sessions.values()]
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    .map(summarize);
}

function findByPid(pid) {
  const numeric = Number(pid);
  const session = [...sessions.values()].find(item => item.pid === numeric);
  if (!session) throw new Error(`Unknown managed process PID: ${pid}`);
  return session;
}

export function getManagedProcessSnapshot(pid) {
  const session = findByPid(pid);
  return {
    ...summarize(session),
    transcript: session.events.map(event => event.text).join(""),
    events: session.events.map(event => ({ ...event }))
  };
}

export function writeManagedProcessByPid({ pid, input, appendNewline }) {
  const session = findByPid(pid);
  return writeManagedProcess({ sessionId: session.id, input, appendNewline });
}

export function killManagedProcessByPid(pid) {
  const session = findByPid(pid);
  return killManagedProcess({ sessionId: session.id });
}

export async function waitManagedProcessByPid({ pid, cursor = 0, maxBytes = Math.min(config.maxProcessReadBytes, 1024 * 1024), waitMs = 0 }) {
  const session = findByPid(pid);
  return readManagedProcess({ sessionId: session.id, cursor, maxBytes, waitMs });
}

function terminate(session) {
  if (process.platform === "win32" && session.child.pid) {
    const killer = spawn("taskkill.exe", ["/PID", String(session.child.pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" });
    killer.on("error", () => session.child.kill());
    return true;
  }
  try { process.kill(-session.child.pid, "SIGTERM"); return true; }
  catch { return session.child.kill(); }
}

export async function shutdownManagedProcesses() {
  await Promise.all([...sessions.values()].filter(s => s.status === "running").map(s => new Promise(resolve => {
    s.child.once("close", resolve);
    terminate(s);
    const timer = setTimeout(resolve, 3000);
    timer.unref();
  })));
}
