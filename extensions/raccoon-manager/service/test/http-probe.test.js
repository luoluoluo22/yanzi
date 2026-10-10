import test from "node:test";
import assert from "node:assert/strict";
import http from "node:http";

process.env.TEST_HTTP_SECRET = "super-secret-value";
const { httpProbe } = await import("../src/http-probe.js");

test("http_probe retries assertions and never returns resolved secret values", async () => {
  let calls = 0;
  const server = http.createServer((req, res) => {
    calls++;
    assert.equal(req.headers.authorization, "Bearer super-secret-value");
    res.setHeader("content-type", "application/json");
    if (calls === 1) {
      res.statusCode = 503;
      res.end(JSON.stringify({ ready: false }));
      return;
    }
    res.statusCode = 200;
    res.setHeader("x-echo-secret", "super-secret-value");
    res.end(JSON.stringify({ ready: true, version: "v2", echoed: "super-secret-value" }));
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  try {
    const result = await httpProbe({
      url: `http://127.0.0.1:${address.port}/health`,
      method: "GET",
      headers: {},
      authorization: { scheme: "Bearer", secretRef: "secret://env/TEST_HTTP_SECRET" },
      assertions: [
        { type: "status", equals: 200 },
        { type: "jsonPathEquals", path: "$.ready", value: true }
      ],
      requestTimeoutMs: 5000,
      intervalMs: 100,
      timeoutMs: 5000,
      maxAttempts: 3,
      maxResponseBytes: 1024 * 1024,
      redirect: "follow"
    });
    assert.equal(result.ok, true);
    assert.equal(result.attempts, 2);
    assert.equal(result.response.json.version, "v2");
    assert.equal(result.response.json.echoed, "[REDACTED]");
    assert.equal(result.response.headers["x-echo-secret"], "[REDACTED]");
    assert.doesNotMatch(JSON.stringify(result), /super-secret-value/);
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
});

test("HTTP response reads are bounded and errors redact resolved secrets", async () => {
  const originalFetch = globalThis.fetch;
  const args = {
    url: "https://example.test/", method: "GET", headers: {},
    authorization: { secretRef: "secret://env/TEST_HTTP_SECRET" },
    assertions: [{ type: "status", equals: 200 }],
    requestTimeoutMs: 5000, timeoutMs: 5000, intervalMs: 100,
    maxAttempts: 1, maxResponseBytes: 1024, redirect: "follow"
  };
  try {
    let cancelled = false;
    globalThis.fetch = async () => new Response(new ReadableStream({
      pull(controller) { controller.enqueue(new Uint8Array(2048).fill(65)); },
      cancel() { cancelled = true; }
    }));
    const bounded = await httpProbe(args);
    assert.equal(bounded.response.clipped, true);
    assert.equal(bounded.response.bytes, 1024);
    assert.equal(cancelled, true);
    globalThis.fetch = async () => { throw new Error("echo super-secret-value"); };
    const failed = await httpProbe(args);
    assert.equal(failed.ok, false);
    assert.doesNotMatch(JSON.stringify(failed), /super-secret-value/);

    globalThis.fetch = async (_url, { signal }) => new Promise((_resolve, reject) => {
      signal.addEventListener("abort", () => reject(signal.reason), { once: true });
    });
    const started = Date.now();
    const timed = await httpProbe({ ...args, timeoutMs: 150 });
    assert.equal(timed.ok, false);
    assert.ok(Date.now() - started < 1500);
  } finally { globalThis.fetch = originalFetch; }
});
