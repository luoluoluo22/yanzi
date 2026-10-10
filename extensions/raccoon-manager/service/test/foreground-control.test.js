import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

const runtime = await fs.mkdtemp(path.join(os.tmpdir(), "raccoon-foreground-"));
process.env.RACCOON_FOREGROUND_RUNTIME_DIR = runtime;
process.env.RACCOON_FOREGROUND_OVERLAY = "0";

const {
  initializeForegroundControl,
  acquireForegroundControl,
  heartbeatForegroundControl,
  pauseForegroundControl,
  releaseForegroundControl,
  foregroundControlStatus,
  assertForegroundControlReady,
  shutdownForegroundControl
} = await import("../src/foreground-control.js");

test("foreground control serializes owners and supports pause/resume/release", async () => {
  await initializeForegroundControl();
  const first = await acquireForegroundControl({ owner: "chat-a", task: "打开窗口", ttlMs: 5000 });
  assert.equal(first.active, true);
  assert.equal(first.owner, "chat-a");

  await assert.rejects(
    acquireForegroundControl({ owner: "chat-b", task: "抢占", ttlMs: 5000 }),
    /already owned/
  );

  const paused = await pauseForegroundControl({ leaseId: first.leaseId, paused: true });
  assert.equal(paused.paused, true);
  await assert.rejects(assertForegroundControlReady(first.leaseId), /paused/);

  const resumed = await pauseForegroundControl({ leaseId: first.leaseId, paused: false });
  assert.equal(resumed.paused, false);
  await assert.doesNotReject(assertForegroundControlReady(first.leaseId));

  const beat = await heartbeatForegroundControl({ leaseId: first.leaseId, task: "点击测试按钮" });
  assert.equal(beat.task, "点击测试按钮");

  const released = await releaseForegroundControl({ leaseId: first.leaseId });
  assert.equal(released.active, false);
});

test("foreground control expires without heartbeat", async () => {
  const current = await foregroundControlStatus();
  if (current.active) await releaseForegroundControl({ leaseId: current.leaseId });

  const lease = await acquireForegroundControl({ owner: "chat-a", task: "短租约", ttlMs: 40 });
  assert.equal(lease.active, true);
  await new Promise(resolve => setTimeout(resolve, 80));
  const expired = await foregroundControlStatus();
  assert.equal(expired.active, false);
});

test.after(async () => {
  await shutdownForegroundControl();
  await fs.rm(runtime, { recursive: true, force: true });
});
