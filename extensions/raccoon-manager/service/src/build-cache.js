import fs from "node:fs/promises";
import { createReadStream } from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { minimatch } from "minimatch";
import { insideRoot, config } from "./config.js";
import { shellCommand } from "./process-manager.js";
import { withHeavyPermit } from "./resource-governor.js";

const execFileAsync = promisify(execFile);
const projectDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const cacheRoot = path.join(process.env.RACCOON_RUNTIME_DIR || path.join(projectDir, ".raccoon-runtime"), "build-cache");
const ignoredDirs = new Set([".git", ".raccoon-runtime", "node_modules"]);

function normalizeRel(value) {
  return value.split(path.sep).join("/");
}

function ensureUnder(base, relative) {
  const resolved = path.resolve(base, relative);
  const rel = path.relative(base, resolved);
  if (rel === ".." || rel.startsWith(".." + path.sep) || path.isAbsolute(rel)) {
    throw new Error("Build-cache path escapes cwd: " + relative);
  }
  return resolved;
}

async function walkFiles(base, current = base, out = []) {
  const entries = await fs.readdir(current, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory() && ignoredDirs.has(entry.name)) continue;
    const full = path.join(current, entry.name);
    if (entry.isDirectory()) await walkFiles(base, full, out);
    else if (entry.isFile()) out.push(normalizeRel(path.relative(base, full)));
  }
  return out;
}

function matchesInputs(rel, inputs) {
  if (!inputs?.length) return true;
  return inputs.some(pattern => {
    const normalized = normalizeRel(pattern);
    if (!/[*?\[\]{}]/.test(normalized)) {
      return rel === normalized || rel.startsWith(normalized.replace(/\/$/, "") + "/");
    }
    return minimatch(rel, normalized, { dot: true, nocase: process.platform === "win32" });
  });
}

async function hashFile(file) {
  return new Promise((resolve, reject) => {
    const hash = createHash("sha256");
    const stream = createReadStream(file, { highWaterMark: 1024 * 1024 });
    stream.on("data", chunk => hash.update(chunk));
    stream.on("error", reject);
    stream.on("end", () => resolve(hash.digest("hex")));
  });
}

async function computeKey(cwd, command, inputs, environment) {
  const all = await walkFiles(cwd);
  const selected = all.filter(rel => matchesInputs(rel, inputs)).sort();
  const rootHash = createHash("sha256");
  rootHash.update("command\0" + command + "\0");
  rootHash.update("environment\0" + JSON.stringify(environment || {}) + "\0");
  for (const rel of selected) {
    const full = path.join(cwd, rel);
    const stat = await fs.stat(full);
    rootHash.update(rel + "\0" + stat.size + "\0" + await hashFile(full) + "\0");
  }
  return { key: rootHash.digest("hex"), files: selected };
}

async function copyOutputToCache(cwd, output, entryDir) {
  const source = ensureUnder(cwd, output);
  const stat = await fs.stat(source).catch(() => null);
  if (!stat) return { output, cached: false, reason: "missing" };
  const target = path.join(entryDir, "files", output);
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.cp(source, target, { recursive: true, force: true });
  return { output, cached: true, type: stat.isDirectory() ? "directory" : "file" };
}

async function restoreOutput(cwd, output, entryDir) {
  const source = path.join(entryDir, "files", output);
  const stat = await fs.stat(source).catch(() => null);
  if (!stat) return { output, restored: false, reason: "not-cached" };
  const target = ensureUnder(cwd, output);
  await fs.rm(target, { recursive: true, force: true }).catch(() => {});
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.cp(source, target, { recursive: true, force: true });
  return { output, restored: true, type: stat.isDirectory() ? "directory" : "file" };
}

async function runCommand(command, cwd, timeoutMs, maxOutputBytes, env) {
  const shell = shellCommand(command);
  const started = Date.now();
  try {
    const { stdout, stderr } = await withHeavyPermit(() => execFileAsync(shell.executable, shell.args, {
      cwd,
      env: { ...process.env, ...(env || {}) },
      windowsHide: true,
      timeout: timeoutMs,
      maxBuffer: maxOutputBytes,
      encoding: "utf8"
    }));
    return { ok: true, exitCode: 0, durationMs: Date.now() - started, stdout, stderr };
  } catch (error) {
    return {
      ok: false,
      exitCode: typeof error?.code === "number" ? error.code : null,
      signal: error?.signal ?? null,
      durationMs: Date.now() - started,
      stdout: error?.stdout || "",
      stderr: error?.stderr || String(error?.message || error)
    };
  }
}

export async function buildCacheRun({
  cwd = ".",
  command,
  inputs = [],
  outputs = [],
  environment = {},
  timeoutMs = 30 * 60 * 1000,
  maxOutputBytes = 16 * 1024 * 1024,
  restore = true,
  force = false
}) {
  const workdir = insideRoot(cwd);
  const computed = await computeKey(workdir, command, inputs, environment);
  const entryDir = path.join(cacheRoot, computed.key);
  const metadataPath = path.join(entryDir, "metadata.json");
  const metadata = await fs.readFile(metadataPath, "utf8").then(JSON.parse).catch(() => null);

  if (metadata && !force) {
    const restored = restore
      ? await Promise.all(outputs.map(output => restoreOutput(workdir, output, entryDir)))
      : [];
    metadata.lastHitAt = new Date().toISOString();
    metadata.hits = (metadata.hits || 0) + 1;
    await fs.writeFile(metadataPath, JSON.stringify(metadata, null, 2) + "\n", "utf8");
    return {
      ok: true,
      cacheHit: true,
      key: computed.key,
      inputFiles: computed.files.length,
      restored,
      cachedResult: metadata.result
    };
  }

  const result = await runCommand(
    command,
    workdir,
    timeoutMs,
    Math.min(maxOutputBytes, config.maxBuildOutputBytes),
    environment
  );

  if (!result.ok) {
    return {
      ok: false,
      cacheHit: false,
      key: computed.key,
      inputFiles: computed.files.length,
      result
    };
  }

  await fs.rm(entryDir, { recursive: true, force: true });
  await fs.mkdir(entryDir, { recursive: true });
  const cachedOutputs = await Promise.all(outputs.map(output => copyOutputToCache(workdir, output, entryDir)));
  const newMetadata = {
    key: computed.key,
    createdAt: new Date().toISOString(),
    lastHitAt: null,
    hits: 0,
    cwd,
    command,
    inputs,
    outputs,
    environmentKeys: Object.keys(environment),
    inputFiles: computed.files.length,
    result: {
      ok: true,
      exitCode: result.exitCode,
      durationMs: result.durationMs,
      stdout: result.stdout.slice(0, 2 * 1024 * 1024),
      stderr: result.stderr.slice(0, 2 * 1024 * 1024)
    },
    cachedOutputs
  };
  await fs.writeFile(metadataPath, JSON.stringify(newMetadata, null, 2) + "\n", "utf8");

  return {
    ok: true,
    cacheHit: false,
    key: computed.key,
    inputFiles: computed.files.length,
    cachedOutputs,
    result
  };
}

async function directorySize(dir) {
  let bytes = 0;
  const entries = await fs.readdir(dir, { withFileTypes: true }).catch(() => []);
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) bytes += await directorySize(full);
    else if (entry.isFile()) bytes += (await fs.stat(full)).size;
  }
  return bytes;
}

export async function buildCacheStats() {
  await fs.mkdir(cacheRoot, { recursive: true });
  const entries = await fs.readdir(cacheRoot, { withFileTypes: true });
  const rows = [];
  for (const entry of entries) {
    if (!entry.isDirectory()) continue;
    const dir = path.join(cacheRoot, entry.name);
    const metadata = await fs.readFile(path.join(dir, "metadata.json"), "utf8").then(JSON.parse).catch(() => null);
    rows.push({
      key: entry.name,
      bytes: await directorySize(dir),
      createdAt: metadata?.createdAt ?? null,
      lastHitAt: metadata?.lastHitAt ?? null,
      hits: metadata?.hits ?? 0,
      command: metadata?.command ?? null,
      inputFiles: metadata?.inputFiles ?? null
    });
  }
  return {
    entries: rows.length,
    bytes: rows.reduce((sum, row) => sum + row.bytes, 0),
    cacheRoot,
    items: rows.sort((a, b) => String(b.lastHitAt || b.createdAt).localeCompare(String(a.lastHitAt || a.createdAt)))
  };
}

export async function buildCacheClear({ key }) {
  await fs.mkdir(cacheRoot, { recursive: true });
  if (key) {
    await fs.rm(path.join(cacheRoot, key), { recursive: true, force: true });
    return { ok: true, cleared: [key] };
  }
  const entries = await fs.readdir(cacheRoot, { withFileTypes: true });
  const cleared = entries.filter(entry => entry.isDirectory()).map(entry => entry.name);
  await Promise.all(cleared.map(name => fs.rm(path.join(cacheRoot, name), { recursive: true, force: true })));
  return { ok: true, cleared };
}
