import fs from "node:fs/promises";
import path from "node:path";
import { execFile, spawn } from "node:child_process";
import { promisify } from "node:util";
import { insideRoot, config } from "./config.js";
import { startManagedProcess } from "./process-manager.js";

const execFileAsync = promisify(execFile);

function adbExecutable() {
  return process.env.RACCOON_ADB || "adb";
}

function serialArgs(serial) {
  return serial ? ["-s", serial] : [];
}

async function adbText(args, { timeout = 120000, maxBuffer = config.maxShellOutputBytes } = {}) {
  try {
    const { stdout, stderr } = await execFileAsync(adbExecutable(), args, {
      windowsHide: true,
      timeout,
      maxBuffer,
      encoding: "utf8"
    });
    return { ok: true, stdout, stderr };
  } catch (error) {
    return {
      ok: false,
      exitCode: typeof error?.code === "number" ? error.code : null,
      signal: error?.signal ?? null,
      stdout: error?.stdout || "",
      stderr: error?.stderr || String(error?.message || error)
    };
  }
}

async function adbBuffer(args, { timeout = 120000, maxBytes = 64 * 1024 * 1024 } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(adbExecutable(), args, {
      windowsHide: true,
      stdio: ["ignore", "pipe", "pipe"]
    });
    const stdout = [];
    const stderr = [];
    let outBytes = 0;
    let errBytes = 0;
    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`ADB command timed out after ${timeout} ms.`));
    }, timeout);
    timer.unref();

    child.stdout.on("data", chunk => {
      outBytes += chunk.length;
      if (outBytes > maxBytes) {
        child.kill();
        reject(new Error(`ADB binary output exceeded ${maxBytes} bytes.`));
        return;
      }
      stdout.push(chunk);
    });
    child.stderr.on("data", chunk => {
      errBytes += chunk.length;
      if (errBytes <= 4 * 1024 * 1024) stderr.push(chunk);
    });
    child.on("error", error => {
      clearTimeout(timer);
      reject(error);
    });
    child.on("close", code => {
      clearTimeout(timer);
      const err = Buffer.concat(stderr).toString("utf8");
      if (code !== 0) {
        reject(new Error(err || `adb exited with code ${code}`));
        return;
      }
      resolve({ buffer: Buffer.concat(stdout), stderr: err });
    });
  });
}

function parseDevices(stdout) {
  const rows = [];
  for (const line of stdout.split(/\r?\n/).slice(1)) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    const [serial, state, ...rest] = trimmed.split(/\s+/);
    const attrs = {};
    for (const token of rest) {
      const index = token.indexOf(":");
      if (index > 0) attrs[token.slice(0, index)] = token.slice(index + 1);
    }
    rows.push({
      serial,
      state,
      model: attrs.model || null,
      product: attrs.product || null,
      device: attrs.device || null,
      transportId: attrs.transport_id || null,
      attributes: attrs
    });
  }
  return rows;
}

function inputTextValue(value) {
  return String(value).replaceAll(" ", "%s");
}

function xmlDecode(value) {
  return String(value || "")
    .replaceAll("&quot;", "\"")
    .replaceAll("&apos;", "'")
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&amp;", "&");
}

export function parseUiAutomatorXml(xml) {
  const nodes = [];
  for (const match of String(xml || "").matchAll(/<node\b([^>]*)\/?\s*>/g)) {
    const attrs = {};
    for (const attr of match[1].matchAll(/([\w:-]+)="([^"]*)"/g)) attrs[attr[1]] = xmlDecode(attr[2]);
    const boundsMatch = /^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$/.exec(attrs.bounds || "");
    const bounds = boundsMatch ? {
      left: Number(boundsMatch[1]), top: Number(boundsMatch[2]),
      right: Number(boundsMatch[3]), bottom: Number(boundsMatch[4])
    } : null;
    nodes.push({
      text: attrs.text || "",
      resourceId: attrs["resource-id"] || "",
      className: attrs.class || "",
      packageName: attrs.package || "",
      contentDesc: attrs["content-desc"] || "",
      bounds,
      clickable: attrs.clickable === "true",
      enabled: attrs.enabled !== "false",
      checked: attrs.checked === "true",
      selected: attrs.selected === "true",
      focused: attrs.focused === "true",
      scrollable: attrs.scrollable === "true"
    });
  }
  return nodes;
}

function packagePattern(packageName) {
  const special = new Set(["\\", ".", "^", "$", "*", "+", "?", "(", ")", "[", "]", "{", "}", "|"]);
  return [...String(packageName)].map(char => special.has(char) ? "\\" + char : char).join("");
}

export function parseAndroidPackageSnapshot(packageName, packageOutput, activityOutput, pidOutput, crashOutput) {
  const missing = /Unable to find package|Unknown package/i.test(packageOutput || "");
  const versionName = /\bversionName=([^\s]+)/.exec(packageOutput || "")?.[1] || null;
  const versionCodeText = /\bversionCode=(\d+)/.exec(packageOutput || "")?.[1];
  const resumed = /(?:mResumedActivity|topResumedActivity)[^\n]*?\s([A-Za-z0-9._$]+\/[A-Za-z0-9._$]+)/.exec(activityOutput || "")?.[1] || null;
  const pidText = String(pidOutput || "").trim().split(/\s+/).find(Boolean);
  const crash = String(crashOutput || "");
  const relevantCrash = new RegExp(packagePattern(packageName)).test(crash) && /FATAL EXCEPTION|Process:/i.test(crash);
  return {
    installed: !missing && Boolean(versionName || versionCodeText || new RegExp(packagePattern(packageName)).test(packageOutput || "")),
    versionName,
    versionCode: versionCodeText ? Number(versionCodeText) : null,
    pid: /^\d+$/.test(pidText || "") ? Number(pidText) : null,
    resumedActivity: resumed,
    hasCrash: relevantCrash,
    crashTail: relevantCrash ? crash.slice(-16384) : ""
  };
}

export async function adbDevices() {
  const result = await adbText(["devices", "-l"], { timeout: 30000 });
  if (!result.ok) throw new Error(result.stderr || "adb devices failed");
  return {
    adb: adbExecutable(),
    devices: parseDevices(result.stdout)
  };
}

export async function adbShell({ serial, command, timeoutMs = 120000, maxOutputBytes = 8 * 1024 * 1024 }) {
  const result = await adbText([...serialArgs(serial), "shell", command], {
    timeout: timeoutMs,
    maxBuffer: Math.min(maxOutputBytes, config.maxShellOutputBytes)
  });
  return { serial: serial || null, command, ...result };
}

export async function adbPush({ serial, localPath, remotePath, timeoutMs = 300000 }) {
  const local = insideRoot(localPath);
  const stat = await fs.stat(local);
  if (!stat.isFile()) throw new Error("adb_push localPath must be a regular file.");
  const result = await adbText([...serialArgs(serial), "push", local, remotePath], { timeout: timeoutMs });
  return { serial: serial || null, localPath, remotePath, bytes: stat.size, ...result };
}

export async function adbPull({ serial, remotePath, localPath, timeoutMs = 300000 }) {
  const local = insideRoot(localPath);
  await fs.mkdir(path.dirname(local), { recursive: true });
  const result = await adbText([...serialArgs(serial), "pull", remotePath, local], { timeout: timeoutMs });
  const stat = result.ok ? await fs.stat(local).catch(() => null) : null;
  return { serial: serial || null, remotePath, localPath, bytes: stat?.size ?? null, ...result };
}

export async function adbInstall({ serial, apkPath, replace = true, grantPermissions = false, timeoutMs = 600000 }) {
  const apk = insideRoot(apkPath);
  const args = [...serialArgs(serial), "install"];
  if (replace) args.push("-r");
  if (grantPermissions) args.push("-g");
  args.push(apk);
  const result = await adbText(args, { timeout: timeoutMs });
  return { serial: serial || null, apkPath, replace, grantPermissions, ...result };
}

export async function adbUninstall({ serial, packageName, keepData = false, timeoutMs = 120000 }) {
  const args = [...serialArgs(serial), "uninstall"];
  if (keepData) args.push("-k");
  args.push(packageName);
  const result = await adbText(args, { timeout: timeoutMs });
  return { serial: serial || null, packageName, keepData, ...result };
}

export async function adbScreenshot({ serial, savePath, maxBytes = 64 * 1024 * 1024 }) {
  const { buffer, stderr } = await adbBuffer(
    [...serialArgs(serial), "exec-out", "screencap", "-p"],
    { timeout: 60000, maxBytes: Math.min(maxBytes, config.maxReadBytes) }
  );
  let savedPath = null;
  if (savePath) {
    const target = insideRoot(savePath);
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, buffer);
    savedPath = target;
  }
  return {
    serial: serial || null,
    buffer,
    stderr,
    bytes: buffer.length,
    savedPath
  };
}

export async function adbInput({ serial, type, x, y, x2, y2, durationMs = 300, text, keyCode }) {
  let args;
  if (type === "tap") args = ["shell", "input", "tap", String(x), String(y)];
  else if (type === "swipe") args = ["shell", "input", "swipe", String(x), String(y), String(x2), String(y2), String(durationMs)];
  else if (type === "text") args = ["shell", "input", "text", inputTextValue(text)];
  else if (type === "keyevent") args = ["shell", "input", "keyevent", String(keyCode)];
  else throw new Error(`Unsupported adb input type: ${type}`);
  const result = await adbText([...serialArgs(serial), ...args], { timeout: 30000 });
  return { serial: serial || null, type, ...result };
}

export async function adbApp({ serial, action, packageName, activity, deepLink }) {
  let command;
  if (action === "stop") {
    command = `am force-stop ${packageName}`;
  } else if (action === "start") {
    if (deepLink) command = `am start -a android.intent.action.VIEW -d "${deepLink.replaceAll('"', '\\"')}"`;
    else if (activity) command = `am start -n ${packageName}/${activity}`;
    else command = `monkey -p ${packageName} -c android.intent.category.LAUNCHER 1`;
  } else if (action === "clear") {
    command = `pm clear ${packageName}`;
  } else {
    throw new Error(`Unsupported app action: ${action}`);
  }
  return adbShell({ serial, command, timeoutMs: 60000 });
}

export async function adbPackages({ serial, thirdPartyOnly = false, filter = "" }) {
  const command = `pm list packages${thirdPartyOnly ? " -3" : ""}`;
  const result = await adbShell({ serial, command, timeoutMs: 60000 });
  if (!result.ok) return result;
  const packages = result.stdout
    .split(/\r?\n/)
    .map(line => line.replace(/^package:/, "").trim())
    .filter(Boolean)
    .filter(name => !filter || name.toLowerCase().includes(filter.toLowerCase()));
  return { serial: serial || null, packages };
}

export async function adbUiDump({ serial, includeXml = false }) {
  const remote = "/sdcard/.raccoon-window.xml";
  const result = await adbShell({
    serial,
    command: `uiautomator dump ${remote} >/dev/null 2>&1; cat ${remote}; rm -f ${remote}`,
    timeoutMs: 60000,
    maxOutputBytes: Math.min(config.maxShellOutputBytes, 16 * 1024 * 1024)
  });
  if (!result.ok) return { ...result, nodes: [] };
  const xml = result.stdout || "";
  return {
    ok: true,
    serial: serial || null,
    nodes: parseUiAutomatorXml(xml),
    ...(includeXml ? { xml } : {})
  };
}

export async function adbFindAndTap({ serial, text, resourceId, contentDesc, match = "exact", requireClickable = false, index = 0 }) {
  if (!text && !resourceId && !contentDesc) throw new Error("Provide text, resourceId or contentDesc.");
  const dump = await adbUiDump({ serial, includeXml: false });
  if (!dump.ok) return dump;
  const compare = (actual, expected) => expected == null || (match === "contains" ? String(actual).includes(expected) : String(actual) === expected);
  const matches = dump.nodes.filter(node =>
    compare(node.text, text) &&
    compare(node.resourceId, resourceId) &&
    compare(node.contentDesc, contentDesc) &&
    (!requireClickable || node.clickable)
  );
  const node = matches[index];
  if (!node) return { ok: false, serial: serial || null, error: "UI node not found.", matches: matches.length };
  if (!node.bounds) return { ok: false, serial: serial || null, error: "Matched UI node has no bounds.", node };
  const x = Math.round((node.bounds.left + node.bounds.right) / 2);
  const y = Math.round((node.bounds.top + node.bounds.bottom) / 2);
  const tap = await adbInput({ serial, type: "tap", x, y });
  return { ok: Boolean(tap.ok), serial: serial || null, x, y, node, tap };
}

export async function androidAppSnapshot({ serial, packageName, crashLines = 200 }) {
  const safePackage = String(packageName).replace(/[^A-Za-z0-9._]/g, "");
  if (safePackage !== packageName) throw new Error("Invalid Android package name.");
  const [pkg, activity, pid, crash] = await Promise.all([
    adbShell({ serial, command: `dumpsys package ${safePackage}`, timeoutMs: 60000, maxOutputBytes: 8 * 1024 * 1024 }),
    adbShell({ serial, command: "dumpsys activity activities", timeoutMs: 60000, maxOutputBytes: 8 * 1024 * 1024 }),
    adbShell({ serial, command: `pidof ${safePackage} || true`, timeoutMs: 30000, maxOutputBytes: 1024 * 1024 }),
    adbShell({ serial, command: `logcat -d -b crash -t ${Math.max(1, Math.min(5000, crashLines))}`, timeoutMs: 60000, maxOutputBytes: 8 * 1024 * 1024 })
  ]);
  return {
    serial: serial || null,
    packageName,
    ...parseAndroidPackageSnapshot(packageName, pkg.stdout, activity.stdout, pid.stdout, crash.stdout),
    commandsOk: { package: pkg.ok, activity: activity.ok, pid: pid.ok, crash: crash.ok }
  };
}

export function adbLogcatStart({ serial, filters = [], clearFirst = false, maxLogBytes = config.defaultProcessLogBytes }) {
  const quoted = filters.map(value => String(value).replaceAll('"', '\"')).join(" ");
  const prefix = clearFirst
    ? `${adbExecutable()} ${serial ? `-s "${serial}" ` : ""}logcat -c; `
    : "";
  const command = `${prefix}${adbExecutable()} ${serial ? `-s "${serial}" ` : ""}logcat ${quoted}`.trim();
  return startManagedProcess({
    command,
    cwd: config.root,
    maxLogBytes
  });
}
