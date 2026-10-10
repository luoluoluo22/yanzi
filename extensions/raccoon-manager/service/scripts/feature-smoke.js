import fs from "node:fs/promises";
import path from "node:path";
import { createCanvas } from "@napi-rs/canvas";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const project = process.cwd();
const tempName = ".raccoon-feature-smoke";
const temp = path.join(project, tempName);
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function textPart(result) {
  return result.content?.find(part => part.type === "text")?.text ?? "";
}
function json(result) {
  if (result.isError) throw new Error(textPart(result));
  return JSON.parse(textPart(result));
}
function assert(condition, message) {
  if (!condition) throw new Error(message);
}

await fs.rm(temp, { recursive: true, force: true });
await fs.mkdir(temp, { recursive: true });

const transport = new StdioClientTransport({
  command: process.execPath,
  args: ["src/index.js"],
  env: {
    ...process.env,
    RACCOON_ROOT: temp,
    RACCOON_ENABLE_SHELL: "1",
    RACCOON_READ_ONLY: "0",
    RACCOON_TRANSPORT: "stdio",
    RACCOON_SETTINGS_FILE: path.join(temp, "settings.json"),
    RACCOON_PUBLIC_URL: "",
    RACCOON_OAUTH_PASSWORD: "",
    RACCOON_AUDIT_LOG: ""
  },
  cwd: project,
  stderr: "pipe"
});
const client = new Client({ name: "raccoon-feature-smoke", version: "0.7.0" });

let processSession;
let watchId;
let cacheKey;
try {
  await client.connect(transport);
  const listed = await client.listTools();
  const names = new Set(listed.tools.map(tool => tool.name));
  const required = [
    "adb_devices","adb_shell","adb_push","adb_pull","adb_install","adb_uninstall",
    "adb_screenshot","adb_input","adb_app","adb_packages","adb_logcat_start",
    "screenshot_capture","visual_compare",
    "db_info","db_query_start","db_query_next","db_query_close","db_query_sessions","db_execute",
    "build_cache_run","build_cache_stats","build_cache_clear",
    "resource_snapshot","process_resource_snapshot","resource_governor_configure","resource_governor_status",
    "process_resource_watch_start","process_resource_watch_read","process_resource_watch_stop","process_resource_watch_list"
  ];
  const missing = required.filter(name => !names.has(name));
  assert(missing.length === 0, "Missing native tools: " + missing.join(", "));

  // Windows visual feedback.
  if (process.platform === "win32") {
  const desktop = await client.callTool({
    name: "screenshot_capture",
    arguments: { savePath: "desktop.png" }
  });
  assert(desktop.content.some(part => part.type === "image"), "desktop screenshot did not return an image");
  const desktopMeta = json(desktop);
  assert(desktopMeta.bytes > 0 && desktopMeta.bounds.width > 0, "desktop screenshot metadata invalid");
  } else {
    // Image comparison is portable; desktop capture is a Windows capability.
    await fs.writeFile(path.join(temp, "desktop.png"), createCanvas(32, 32).toBuffer("image/png"));
    const desktop = await client.callTool({ name: "screenshot_capture", arguments: { savePath: "capture.png" } });
    assert(desktop.isError && textPart(desktop).includes("Windows"), "unsupported desktop capture should report its platform requirement");
  }
  const visual = json(await client.callTool({
    name: "visual_compare",
    arguments: { beforePath: "desktop.png", afterPath: "desktop.png", threshold: 1, diffSavePath: "desktop-diff.png" }
  }));
  assert(visual.changedPixels === 0, "same-image visual comparison should have zero changed pixels");

  // ADB native layer. Device-specific read-only validation is conditional on an attached device.
  const adb = json(await client.callTool({ name: "adb_devices", arguments: {} }));
  assert(Array.isArray(adb.devices), "adb_devices did not return device list");
  const online = adb.devices.find(device => device.state === "device");
  let adbScreenshot = false;
  let adbFileRoundTrip = false;
  let adbLogcatSession = false;
  if (online) {
    const shell = json(await client.callTool({
      name: "adb_shell",
      arguments: { serial: online.serial, command: "wm size" }
    }));
    assert(shell.ok && /Physical size|Override size|x/.test(shell.stdout), "adb_shell wm size failed");

    const packages = json(await client.callTool({
      name: "adb_packages",
      arguments: { serial: online.serial, thirdPartyOnly: true }
    }));
    assert(Array.isArray(packages.packages), "adb_packages failed");

    const phone = await client.callTool({
      name: "adb_screenshot",
      arguments: { serial: online.serial, savePath: "android.png" }
    });
    assert(phone.content.some(part => part.type === "image"), "adb_screenshot did not return image");
    adbScreenshot = true;

    const remoteTestPath = "/sdcard/Download/raccoon-feature-smoke.txt";
    await fs.writeFile(path.join(temp, "adb-push.txt"), "raccoon adb round trip\n", "utf8");
    const pushed = json(await client.callTool({
      name: "adb_push",
      arguments: { serial: online.serial, localPath: "adb-push.txt", remotePath: remoteTestPath }
    }));
    assert(pushed.ok, "adb_push failed");
    const pulled = json(await client.callTool({
      name: "adb_pull",
      arguments: { serial: online.serial, remotePath: remoteTestPath, localPath: "adb-pulled.txt" }
    }));
    assert(pulled.ok, "adb_pull failed");
    assert((await fs.readFile(path.join(temp, "adb-pulled.txt"), "utf8")) === "raccoon adb round trip\n", "ADB push/pull content mismatch");
    adbFileRoundTrip = true;
    await client.callTool({
      name: "adb_shell",
      arguments: { serial: online.serial, command: "rm -f " + remoteTestPath }
    });

    const logcat = json(await client.callTool({
      name: "adb_logcat_start",
      arguments: { serial: online.serial, filters: ["RaccoonNoSuchTag:D", "*:S"], maxLogBytes: 1024 * 1024 }
    }));
    assert(logcat.sessionId && logcat.pid, "adb_logcat_start failed");
    adbLogcatSession = true;
    await client.callTool({ name: "process_kill", arguments: { sessionId: logcat.sessionId } });
  }

  // SQLite streaming query: create 2,000 rows then consume in batches.
  json(await client.callTool({
    name: "db_execute",
    arguments: {
      file: "stream.db",
      sql: "CREATE TABLE items(id INTEGER PRIMARY KEY, value TEXT); " +
        "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM c WHERE x<2000) " +
        "INSERT INTO items(id,value) SELECT x,'row-'||x FROM c;"
    }
  }));
  const dbMeta = json(await client.callTool({ name: "db_info", arguments: { file: "stream.db" } }));
  assert(dbMeta.objects.some(item => item.name === "items"), "db_info missing items table");

  let batch = json(await client.callTool({
    name: "db_query_start",
    arguments: { file: "stream.db", sql: "SELECT id,value FROM items ORDER BY id", batchRows: 137 }
  }));
  let rowCount = batch.rows.length;
  const querySession = batch.sessionId;
  while (!batch.done) {
    batch = json(await client.callTool({
      name: "db_query_next",
      arguments: { sessionId: querySession, maxRows: 137 }
    }));
    rowCount += batch.rows.length;
  }
  assert(rowCount === 2000, "streaming SQLite row count mismatch: " + rowCount);

  // Content-addressed build cache: miss -> delete output -> hit restores output.
  await fs.writeFile(path.join(temp, "input.txt"), "cache me\n", "utf8");
  const buildCommand = "node -e \"const f=require('fs');f.mkdirSync('dist',{recursive:true});f.writeFileSync('dist/out.txt',f.readFileSync('input.txt','utf8').toUpperCase())\"";
  const firstBuild = json(await client.callTool({
    name: "build_cache_run",
    arguments: {
      cwd: ".",
      command: buildCommand,
      inputs: ["input.txt"],
      outputs: ["dist"]
    }
  }));
  assert(firstBuild.ok && firstBuild.cacheHit === false, "first cached build should miss");
  cacheKey = firstBuild.key;
  assert((await fs.readFile(path.join(temp, "dist", "out.txt"), "utf8")) === "CACHE ME\n", "build output invalid");

  await fs.rm(path.join(temp, "dist"), { recursive: true, force: true });
  const secondBuild = json(await client.callTool({
    name: "build_cache_run",
    arguments: {
      cwd: ".",
      command: buildCommand,
      inputs: ["input.txt"],
      outputs: ["dist"]
    }
  }));
  assert(secondBuild.ok && secondBuild.cacheHit === true, "second cached build should hit");
  assert((await fs.readFile(path.join(temp, "dist", "out.txt"), "utf8")) === "CACHE ME\n", "cache restore failed");
  const cacheStats = json(await client.callTool({ name: "build_cache_stats", arguments: {} }));
  assert(cacheStats.entries >= 1, "build cache stats missing entry");

  await fs.writeFile(path.join(temp, "input.txt"), "cache changed\n", "utf8");
  const thirdBuild = json(await client.callTool({
    name: "build_cache_run",
    arguments: {
      cwd: ".",
      command: buildCommand,
      inputs: ["input.txt"],
      outputs: ["dist"]
    }
  }));
  assert(thirdBuild.ok && thirdBuild.cacheHit === false && thirdBuild.key !== cacheKey, "input change did not invalidate build cache");
  const secondCacheKey = thirdBuild.key;
  await client.callTool({ name: "build_cache_clear", arguments: { key: secondCacheKey } });

  // Host resource snapshot and automatic governor.
  const resources = json(await client.callTool({
    name: "resource_snapshot",
    arguments: { sampleMs: 100, top: 5 }
  }));
  assert(resources.logicalCpus >= 1 && resources.totalMemoryBytes > 0, "resource snapshot invalid");

  const governor = json(await client.callTool({
    name: "resource_governor_configure",
    arguments: {
      enabled: true,
      maxConcurrentHeavyOps: 2,
      maxSystemCpuPercent: 100,
      minFreeMemoryBytes: 0,
      pollMs: 100
    }
  }));
  assert(governor.enabled && governor.maxConcurrentHeavyOps === 2, "governor configuration failed");

  const governorStartedAt = Date.now();
  const governed = json(await client.callTool({
    name: "project_build_test",
    arguments: {
      cwd: ".",
      parallelism: 8,
      steps: Array.from({ length: 6 }, (_, i) => ({
        name: "governed-" + i,
        command: "node -e \"setTimeout(()=>process.stdout.write('ok'),250)\""
      })),
      timeoutMsPerStep: 30000,
      maxOutputBytesPerStep: 1024 * 1024
    }
  }));
  const governorElapsedMs = Date.now() - governorStartedAt;
  assert(governed.ok && governed.stepsCompleted === 6, "governed workflow failed");
  assert(governorElapsedMs >= 600, "resource governor did not visibly throttle parallel heavy work");
  json(await client.callTool({ name: "resource_governor_configure", arguments: { enabled: false } }));

  // Process metrics + persistent watch. Memory threshold of 1 byte guarantees a violation.
  if (process.platform === "win32") {
  processSession = json(await client.callTool({
    name: "process_start",
    arguments: {
      cwd: ".",
      command: "node -e \"setInterval(()=>{},1000)\"",
      maxLogBytes: 1024 * 1024
    }
  }));
  const procMetrics = json(await client.callTool({
    name: "process_resource_snapshot",
    arguments: { pid: processSession.pid, sampleMs: 100 }
  }));
  assert(procMetrics.pid === processSession.pid && procMetrics.workingSetBytes > 0, "process resource snapshot invalid");

  const watch = json(await client.callTool({
    name: "process_resource_watch_start",
    arguments: {
      pid: processSession.pid,
      intervalMs: 300,
      maxMemoryBytes: 1,
      action: "lower_priority",
      priority: "BelowNormal",
      maxSamples: 100
    }
  }));
  watchId = watch.watchId;
  let watched;
  for (let attempt = 0; attempt < 20; attempt++) {
    await sleep(300);
    watched = json(await client.callTool({
      name: "process_resource_watch_read",
      arguments: { watchId, tail: 20 }
    }));
    if (watched.samples.length > 0 && watched.violations > 0) break;
  }
  assert(watched.samples.length > 0 && watched.violations > 0, "resource watch did not record guaranteed violation");
  const throttledMetrics = json(await client.callTool({
    name: "process_resource_snapshot",
    arguments: { pid: processSession.pid, sampleMs: 100 }
  }));
  assert(throttledMetrics.priorityClass === "BelowNormal", "automatic lower-priority throttling was not applied");
  }

  console.log(JSON.stringify({
    ok: true,
    toolCount: listed.tools.length,
    newToolCount: required.length,
    tested: {
      desktopScreenshotImage: process.platform === "win32",
      visualDiff: true,
      adbDevices: true,
      adbOnlineDevice: Boolean(online),
      adbScreenshot,
      adbFileRoundTrip,
      adbLogcatSession,
      sqliteStreamingRows: rowCount,
      buildCacheMissHitRestore: true,
      resourceSnapshot: true,
      resourceGovernor: true,
      resourceGovernorElapsedMs: governorElapsedMs,
      processResourceSnapshot: process.platform === "win32",
      processAutoThrottleWatch: process.platform === "win32"
    }
  }, null, 2));
} finally {
  if (watchId) await client.callTool({ name: "process_resource_watch_stop", arguments: { watchId } }).catch(() => {});
  if (processSession?.sessionId) await client.callTool({ name: "process_kill", arguments: { sessionId: processSession.sessionId } }).catch(() => {});
  if (cacheKey) await client.callTool({ name: "build_cache_clear", arguments: { key: cacheKey } }).catch(() => {});
  await client.close().catch(() => {});
  await fs.rm(temp, { recursive: true, force: true }).catch(() => {});
}
