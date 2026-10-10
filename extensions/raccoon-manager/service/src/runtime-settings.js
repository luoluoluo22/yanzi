import fs from "node:fs";
import fsp from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const projectDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const runtimeDir = process.env.RACCOON_RUNTIME_DIR || path.join(projectDir, ".raccoon-runtime");
const settingsFile = process.env.RACCOON_SETTINGS_FILE
  ? path.resolve(process.env.RACCOON_SETTINGS_FILE)
  : path.join(runtimeDir, "settings.json");

const defaults = {
  blockedCommands: [],
  allowedDirectories: [],
  defaultShell: process.platform === "win32" ? "powershell.exe" : (process.env.SHELL || "/bin/sh"),
  telemetryEnabled: true,
  fileReadLineLimit: 10000,
  fileWriteLineLimit: 10000,
  remoteDevices: []
};

let settings = { ...defaults };
try {
  if (fs.existsSync(settingsFile)) {
    settings = { ...defaults, ...JSON.parse(fs.readFileSync(settingsFile, "utf8")) };
  }
} catch {
  settings = { ...defaults };
}

export function getRuntimeSettings() {
  return structuredClone(settings);
}

export function getRuntimeSetting(key) {
  return settings[key];
}

export async function setRuntimeSetting(key, value) {
  if (!(key in defaults)) throw new Error(`Unknown runtime setting: ${key}`);
  if (key === "blockedCommands" || key === "allowedDirectories") {
    if (!Array.isArray(value) || value.some(item => typeof item !== "string")) {
      throw new Error(`${key} must be an array of strings.`);
    }
  } else if (key === "remoteDevices") {
    if (!Array.isArray(value) || value.some(item =>
      !item || typeof item !== "object" ||
      typeof item.url !== "string" || !/^https?:\/\//i.test(item.url) ||
      (item.id != null && typeof item.id !== "string") ||
      (item.name != null && typeof item.name !== "string") ||
      (item.token != null && typeof item.token !== "string")
    )) {
      throw new Error("remoteDevices must be an array of {id?, name?, url, token?, headers?} objects.");
    }
  } else if (key === "defaultShell") {
    if (typeof value !== "string" || !value.trim()) throw new Error("defaultShell must be a non-empty string.");
  } else if (key === "telemetryEnabled") {
    if (typeof value !== "boolean") throw new Error("telemetryEnabled must be boolean.");
  } else if (key === "fileReadLineLimit" || key === "fileWriteLineLimit") {
    if (!Number.isSafeInteger(value) || value < 1 || value > 1000000) {
      throw new Error(`${key} must be an integer between 1 and 1000000.`);
    }
  }

  settings = { ...settings, [key]: value };
  await fsp.mkdir(path.dirname(settingsFile), { recursive: true });
  await fsp.writeFile(settingsFile, JSON.stringify(settings, null, 2) + "\n", "utf8");
  return getRuntimeSettings();
}

export function runtimeSettingsPath() {
  return settingsFile;
}
