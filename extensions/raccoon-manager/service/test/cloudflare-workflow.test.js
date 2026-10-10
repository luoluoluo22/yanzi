import test from "node:test";
import assert from "node:assert/strict";
import http from "node:http";

process.env.RACCOON_ENABLE_SHELL = "1";
const { cloudflareDeployVerify } = await import("../src/cloudflare-workflow.js");

test("cloudflare_deploy_verify composes deploy, version capture, activation and HTTP assertions", async () => {
  const server = http.createServer((_req, res) => {
    res.setHeader("content-type", "application/json");
    res.end(JSON.stringify({ ok: true, serverNow: { present: true } }));
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  try {
    const result = await cloudflareDeployVerify({
      cwd: ".",
      deployCommand: "node -e \"console.log('deploy-ok')\"",
      runner: "default",
      deployTimeoutMs: 5000,
      maxOutputBytes: 1024 * 1024,
      versionProbe: {
        command: "node -e \"console.log(JSON.stringify({version:'v-123'}))\"",
        cwd: ".",
        runner: "default",
        condition: { jsonPathExists: "$.version" },
        capture: { version: { source: "stdout", jsonPath: "$.version", flags: "", group: 1 } },
        intervalMs: 100,
        timeoutMs: 5000,
        commandTimeoutMs: 5000,
        maxAttempts: 2
      },
      activateCommand: "node -e \"console.log('activate-${deploy.version}')\"",
      activateRunner: "default",
      activateTimeoutMs: 5000,
      probes: [{
        name: "health",
        url: `http://127.0.0.1:${address.port}/health`,
        method: "GET",
        headers: {},
        assertions: [
          { type: "status", equals: 200 },
          { type: "jsonPathExists", path: "$.serverNow.present" }
        ],
        requestTimeoutMs: 5000,
        intervalMs: 100,
        timeoutMs: 5000,
        maxAttempts: 2,
        maxResponseBytes: 1024 * 1024,
        redirect: "follow"
      }]
    });
    assert.equal(result.ok, true);
    assert.equal(result.outputs.version, "v-123");
    assert.match(result.stages.activate.last.stdout, /activate-v-123/);
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
});
