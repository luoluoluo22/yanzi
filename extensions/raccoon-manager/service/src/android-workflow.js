import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { z } from "zod";
import { config, insideRoot } from "./config.js";
import { shellCommand } from "./process-manager.js";
import { withHeavyPermit } from "./resource-governor.js";
import { runnerExecutable, sleep } from "./pipeline-runtime.js";
import {
  adbDevices,
  adbInstall,
  adbApp,
  adbUiDump,
  androidAppSnapshot
} from "./adb-tools.js";

const execFileAsync = promisify(execFile);

function applyPowerShellExitGuard(shell) {
  if (process.platform === "win32" && /(?:powershell|pwsh)(?:\.exe)?$/i.test(shell.executable)) {
    shell.args[shell.args.length - 1] = "$ErrorActionPreference = 'Stop'; " + shell.args.at(-1) + "; $_raccoonAndroidLastSuccess = $?; if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; if (-not $_raccoonAndroidLastSuccess) { exit 1 }";
  }
  return shell;
}

function tail(value, maxBytes) {
  const buffer = Buffer.from(String(value || ""), "utf8");
  let start = Math.max(0, buffer.length - maxBytes);
  while (start < buffer.length && (buffer[start] & 0xc0) === 0x80) start++;
  return buffer.subarray(start).toString("utf8");
}

async function runBuild({ command, cwd, runner, timeoutMs, maxOutputBytes }) {
  const shell = applyPowerShellExitGuard(shellCommand(command, runnerExecutable(runner)));
  const started = Date.now();
  try {
    const { stdout, stderr } = await withHeavyPermit(() => execFileAsync(shell.executable, shell.args, {
      cwd: insideRoot(cwd),
      timeout: timeoutMs,
      windowsHide: true,
      maxBuffer: maxOutputBytes
    }));
    return { ok: true, exitCode: 0, durationMs: Date.now() - started, stdout: tail(stdout, maxOutputBytes), stderr: tail(stderr, maxOutputBytes) };
  } catch (error) {
    return {
      ok: false,
      exitCode: typeof error?.code === "number" ? error.code : null,
      signal: error?.signal ?? null,
      durationMs: Date.now() - started,
      stdout: tail(error?.stdout, maxOutputBytes),
      stderr: tail(error?.stderr || error?.message || error, maxOutputBytes)
    };
  }
}

async function chooseDevice(serial) {
  const devices = (await adbDevices()).devices.filter(item => item.state === "device");
  if (serial) {
    const device = devices.find(item => item.serial === serial);
    if (!device) throw new Error(`ADB device is not online: ${serial}`);
    return device;
  }
  if (devices.length !== 1) {
    throw new Error(devices.length ? "Multiple ADB devices are online; specify serial." : "No online ADB device.");
  }
  return devices[0];
}

function versionIdentity(snapshot) {
  return snapshot ? { installed: snapshot.installed, versionName: snapshot.versionName, versionCode: snapshot.versionCode } : null;
}

function versionEqual(before, after) {
  if (!before || !after) return true;
  return before.installed === after.installed &&
    before.versionName === after.versionName &&
    before.versionCode === after.versionCode;
}

function uiContains(nodes, expected, match) {
  return nodes.some(node => {
    const fields = [node.text, node.contentDesc, node.resourceId].map(value => String(value || ""));
    return fields.some(value => match === "exact" ? value === expected : value.includes(expected));
  });
}

export async function androidDevCycle(args) {
  if (!config.shellEnabled) throw new Error("android_dev_cycle requires RACCOON_ENABLE_SHELL=1.");
  if (args.preserveProduction && args.productionPackage && args.productionPackage === args.packageName) {
    throw new Error("productionPackage must differ from packageName when preserveProduction=true.");
  }

  const device = await chooseDevice(args.serial);
  const serial = device.serial;
  const stages = { device };
  let productionBefore = null;
  const developmentBefore = await androidAppSnapshot({ serial, packageName: args.packageName, crashLines: args.crashLines });
  stages.developmentBefore = versionIdentity(developmentBefore);

  if (args.productionPackage && args.preserveProduction) {
    productionBefore = await androidAppSnapshot({ serial, packageName: args.productionPackage, crashLines: 50 });
    stages.productionBefore = versionIdentity(productionBefore);
  }

  stages.build = await runBuild({
    command: args.buildCommand,
    cwd: args.cwd,
    runner: args.runner,
    timeoutMs: args.buildTimeoutMs,
    maxOutputBytes: args.maxBuildOutputBytes
  });
  if (!stages.build.ok) return { ok: false, stage: "build", serial, stages };

  stages.install = await adbInstall({
    serial,
    apkPath: path.resolve(insideRoot(args.cwd), args.apkPath),
    replace: true,
    grantPermissions: args.grantPermissions,
    timeoutMs: args.installTimeoutMs
  });
  if (!stages.install.ok) return { ok: false, stage: "install", serial, stages };

  stages.stop = await adbApp({ serial, action: "stop", packageName: args.packageName });
  stages.start = await adbApp({ serial, action: "start", packageName: args.packageName, activity: args.activity });
  if (!stages.start.ok) return { ok: false, stage: "launch", serial, stages };

  const startupStarted = Date.now();
  let app = null;
  while (Date.now() - startupStarted <= args.startupTimeoutMs) {
    app = await androidAppSnapshot({ serial, packageName: args.packageName, crashLines: args.crashLines });
    if (app.pid || app.resumedActivity?.startsWith(args.packageName + "/")) break;
    await sleep(Math.min(500, Math.max(0, args.startupTimeoutMs - (Date.now() - startupStarted))));
  }
  stages.app = app;
  if (!app?.installed || (!app.pid && !app.resumedActivity?.startsWith(args.packageName + "/"))) {
    return { ok: false, stage: "startup", serial, stages, error: "App did not become active before startup timeout." };
  }

  if (args.smoke) {
    const ui = await adbUiDump({ serial, includeXml: false });
    stages.ui = { ok: ui.ok, nodes: ui.nodes?.length || 0 };
    if (!ui.ok) return { ok: false, stage: "ui_dump", serial, stages };
    const missingTexts = args.expectedTexts.filter(value => !uiContains(ui.nodes, value, args.textMatch));
    stages.ui.missingTexts = missingTexts;
    if (missingTexts.length) return { ok: false, stage: "ui_assert", serial, stages, error: `Missing UI text: ${missingTexts.join(", ")}` };

    const afterSmoke = await androidAppSnapshot({ serial, packageName: args.packageName, crashLines: args.crashLines });
    stages.appAfterSmoke = afterSmoke;
    const newCrash = afterSmoke.hasCrash && afterSmoke.crashTail !== developmentBefore.crashTail;
    if (newCrash) return { ok: false, stage: "crash_check", serial, stages, error: "Crash buffer contains a new crash for the development package." };
  }

  if (args.productionPackage && args.preserveProduction) {
    const productionAfter = await androidAppSnapshot({ serial, packageName: args.productionPackage, crashLines: 50 });
    stages.productionAfter = versionIdentity(productionAfter);
    if (!versionEqual(versionIdentity(productionBefore), versionIdentity(productionAfter))) {
      return { ok: false, stage: "production_guard", serial, stages, error: "Production package version/install state changed during development cycle." };
    }
  }

  return { ok: true, stage: "complete", serial, packageName: args.packageName, stages };
}

export function registerAndroidWorkflowTool(server) {
  server.registerTool("android_dev_cycle", {
    title: "Android development cycle",
    description: "Build, install a development APK, restart it, wait for startup, assert UI text, inspect crash state, and verify an optional production package was not changed.",
    inputSchema: {
      cwd: z.string().default("."),
      buildCommand: z.string().min(1).max(65536),
      runner: z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default"),
      apkPath: z.string().min(1),
      packageName: z.string().regex(/^[A-Za-z0-9_.]+$/),
      activity: z.string().optional(),
      serial: z.string().optional(),
      smoke: z.boolean().default(true),
      expectedTexts: z.array(z.string()).max(100).default([]),
      textMatch: z.enum(["exact", "contains"]).default("contains"),
      productionPackage: z.string().regex(/^[A-Za-z0-9_.]+$/).optional(),
      preserveProduction: z.boolean().default(true),
      grantPermissions: z.boolean().default(false),
      buildTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(30 * 60 * 1000),
      installTimeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(10 * 60 * 1000),
      startupTimeoutMs: z.number().int().min(500).max(10 * 60 * 1000).default(30000),
      crashLines: z.number().int().min(1).max(5000).default(300),
      maxBuildOutputBytes: z.number().int().min(1024).max(config.maxBuildOutputBytes).default(Math.min(16 * 1024 * 1024, config.maxBuildOutputBytes))
    }
  }, async args => ({ content: [{ type: "text", text: JSON.stringify(await androidDevCycle(args), null, 2) }] }));
}
