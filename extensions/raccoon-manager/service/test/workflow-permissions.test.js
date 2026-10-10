import test from "node:test";
import assert from "node:assert/strict";

process.env.RACCOON_ENABLE_SHELL = "0";
const { waitUntil } = await import("../src/dev-pipeline.js");
const { androidDevCycle } = await import("../src/android-workflow.js");
const { cloudflareDeployVerify } = await import("../src/cloudflare-workflow.js");

test("workflows cannot bypass disabled shell execution", async () => {
  await assert.rejects(waitUntil({ command: "echo forbidden", maxAttempts: 1 }), /RACCOON_ENABLE_SHELL/);
  await assert.rejects(androidDevCycle({}), /RACCOON_ENABLE_SHELL/);
  await assert.rejects(cloudflareDeployVerify({ deployCommand: "echo forbidden", cwd: "." }), /RACCOON_ENABLE_SHELL/);
});
