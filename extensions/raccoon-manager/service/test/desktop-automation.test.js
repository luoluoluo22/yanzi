import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const runtime = await fs.mkdtemp(path.join(os.tmpdir(), "raccoon-desktop-"));
process.env.RACCOON_FOREGROUND_RUNTIME_DIR = runtime;
process.env.RACCOON_FOREGROUND_OVERLAY = "0";

const foreground = await import("../src/foreground-control.js");
const desktop = await import("../src/desktop-automation.js");

test("desktop actions automatically acquire and release foreground control", async () => {
  await foreground.initializeForegroundControl();
  let observedLease;
  const result = await desktop.runDesktopActions({
    owner: "test-agent",
    task: "自动控制测试",
    actions: [{ type: "wait", ms: 1 }]
  }, async (actions, leaseId) => {
    observedLease = await foreground.foregroundControlStatus();
    assert.equal(observedLease.active, true);
    assert.equal(observedLease.leaseId, leaseId);
    assert.equal(observedLease.owner, "test-agent");
    return { ok: true, actionsCompleted: actions.length };
  });

  assert.equal(result.ok, true);
  assert.equal(result.autoLease, true);
  assert.equal((await foreground.foregroundControlStatus()).active, false);
});

test("desktop actions can intentionally reuse a manual lease without releasing it", async () => {
  const lease = await foreground.acquireForegroundControl({
    owner: "manual",
    task: "manual session",
    ttlMs: 5000
  });

  const result = await desktop.runDesktopActions({
    leaseId: lease.leaseId,
    task: "manual batch",
    actions: [{ type: "wait", ms: 1 }]
  }, async (_actions, leaseId) => {
    assert.equal(leaseId, lease.leaseId);
    return { ok: true, actionsCompleted: 1 };
  });

  assert.equal(result.autoLease, false);
  const current = await foreground.foregroundControlStatus();
  assert.equal(current.active, true);
  assert.equal(current.leaseId, lease.leaseId);
  assert.equal(current.task, "manual batch");
  await foreground.releaseForegroundControl({ leaseId: lease.leaseId });
});

test("desktop action schema rejects partial coordinates before acquiring control", async () => {
  await assert.rejects(
    desktop.runDesktopActions({
      actions: [{ type: "click", x: 10 }]
    }, async () => ({ ok: true })),
    /provided together/
  );
  assert.equal((await foreground.foregroundControlStatus()).active, false);
});

test("Windows desktop helper aborts promptly when the user releases control", { skip: process.platform !== "win32" }, async () => {
  const lease = await foreground.acquireForegroundControl({
    owner: "manual",
    task: "release cancellation",
    ttlMs: 5000
  });

  const operation = desktop.runDesktopActions({
    leaseId: lease.leaseId,
    task: "release cancellation",
    actions: [{ type: "wait", ms: 5000 }]
  });

  await new Promise(resolve => setTimeout(resolve, 300));
  await foreground.releaseForegroundControl({ leaseId: lease.leaseId });
  await assert.rejects(operation, /released or replaced|No foreground-control lease/);
  assert.equal((await foreground.foregroundControlStatus()).active, false);
});

test.after(async () => {
  await foreground.shutdownForegroundControl();
  await fs.rm(runtime, { recursive: true, force: true });
});
