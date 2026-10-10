import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import http from "node:http";

const root = await fs.mkdtemp(path.join(os.tmpdir(), "raccoon-security-"));
const outside = await fs.mkdtemp(path.join(os.tmpdir(), "raccoon-outside-"));
process.env.RACCOON_ROOT = root;
process.env.RACCOON_FILESYSTEM_UNRESTRICTED = "0";
process.env.RACCOON_READ_ONLY = "1";
process.env.RACCOON_ENABLE_SHELL = "0";
process.env.RACCOON_TRANSPORT = "http";
process.env.RACCOON_TOKEN = "test-token";
process.env.RACCOON_MAX_MESSAGE_BYTES = "1024";
process.env.RACCOON_AUDIT_LOG = path.join(root, "audit.jsonl");
const { insideRoot } = await import("../src/config.js");
const { createHttpApp } = await import("../src/http.js");
const listener = createHttpApp().listen(0, "127.0.0.1");
await new Promise(resolve => listener.once("listening", resolve));
const base = `http://127.0.0.1:${listener.address().port}`;
test.after(async () => {
  await new Promise(resolve => listener.close(resolve));
  await fs.rm(root, { recursive: true, force: true });
  await fs.rm(outside, { recursive: true, force: true });
});

test("reject traversal and absolute paths outside root", () => {
  assert.throws(() => insideRoot("../escape"), /escapes/);
  assert.throws(() => insideRoot(outside), /escapes/);
  assert.equal(insideRoot("new/file.txt"), path.join(root, "new/file.txt"));
});
test("reject directory symlinks / Windows junctions, including new writes", async () => {
  const link = path.join(root, "link");
  await fs.symlink(outside, link, process.platform === "win32" ? "junction" : "dir");
  assert.throws(() => insideRoot("link"), /escapes/);
  assert.throws(() => insideRoot("link/new/file.txt"), /escapes/);
});
test("aliases inside root resolve to the same lock key", async () => {
  await fs.mkdir(path.join(root, "real"));
  await fs.symlink(path.join(root, "real"), path.join(root, "alias"), process.platform === "win32" ? "junction" : "dir");
  assert.equal(insideRoot("real/file"), insideRoot("alias/file"));
});

test("unrestricted mode accepts outside paths, junctions, and reserved files", async () => {
  const code = `
    import assert from 'node:assert/strict';
    import path from 'node:path';
    import {insideRoot, config} from './src/config.js';
    const [root, outside] = process.argv.slice(1);
    assert.equal(config.filesystemUnrestricted, true);
    assert.equal(insideRoot(outside), outside);
    assert.equal(insideRoot(path.relative(root, outside)), outside);
    assert.equal(insideRoot('link/new/file.txt'), path.join(outside, 'new/file.txt'));
    assert.equal(insideRoot('.env'), path.join(root, '.env'));
    assert.equal(insideRoot(path.join(outside, '.env.local')), path.join(outside, '.env.local'));
    assert.equal(insideRoot('.raccoon-runtime/key.txt'), path.join(root, '.raccoon-runtime/key.txt'));
    assert.equal(insideRoot('normal.txt'), path.join(root, 'normal.txt'));
  `;
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', code, root, outside], {
    env: {...process.env, RACCOON_FILESYSTEM_UNRESTRICTED: '1'}, encoding: 'utf8'
  });
  assert.equal(result.status, 0, result.stderr);
});
test("MCP paths cannot read or overwrite service credentials", () => {
  for (const name of [".env", ".env.local", ".raccoon-runtime/openai-runtime-key.txt", ".raccoon-runtime/profiles/connection.yaml"]) {
    assert.throws(() => insideRoot(name), /reserved/);
  }
  assert.equal(insideRoot(".env.example"), path.join(root, ".env.example"));
});
test("symlink aliases cannot bypass service credential protection", async () => {
  await fs.mkdir(path.join(root, ".raccoon-runtime"));
  await fs.symlink(path.join(root, ".raccoon-runtime"), path.join(root, "credentials-alias"), process.platform === "win32" ? "junction" : "dir");
  assert.throws(() => insideRoot("credentials-alias/key.txt"), /reserved/);
});
test("invalid numeric configuration fails at startup", () => {
  for (const value of ["NaN", "0", "65536", "1.5"]) {
    const result = spawnSync(process.execPath, ["--input-type=module", "-e", "await import('./src/config.js')"], {
      env: { ...process.env, RACCOON_PORT: value }, encoding: "utf8"
    });
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /RACCOON_PORT must be/);
  }
});
test("health does not disclose workspace path", async () => {
  const response = await fetch(base + "/health");
  assert.equal(response.status, 200);
  assert.equal((await response.json()).root, undefined);
});
test("unauthenticated MCP requests receive a challenge before parsing", async () => {
  for (const method of ["POST", "GET", "DELETE"]) {
    const response = await fetch(base + "/mcp", { method });
    assert.equal(response.status, 401);
    assert.match(response.headers.get("www-authenticate"), /Bearer/);
  }
});
test("reject unknown Origin and Host headers", async () => {
  for (const headers of [{ Origin: "https://evil.example" }, { Host: "evil.example" }]) {
    const status = await new Promise((resolve, reject) => {
      const req = http.request(base + "/mcp", { method: "POST", headers: { Authorization: "Bearer test-token", ...headers } }, res => { res.resume(); resolve(res.statusCode); });
      req.on("error", reject);
      req.end();
    });
    assert.equal(status, 403);
  }
});
test("enforce configured HTTP body budget and reject malformed JSON", async () => {
  const headers = { Authorization: "Bearer test-token", "Content-Type": "application/json" };
  const large = await fetch(base + "/mcp", { method: "POST", headers, body: JSON.stringify({ data: "x".repeat(2048) }) });
  assert.equal(large.status, 413);
  const invalid = await fetch(base + "/mcp", { method: "POST", headers, body: "{" });
  assert.equal(invalid.status, 400);
});
test("read-only mode denies writes; tools describe side effects; audit excludes payloads", async () => {
  const { Client } = await import("@modelcontextprotocol/sdk/client/index.js");
  const { StreamableHTTPClientTransport } = await import("@modelcontextprotocol/sdk/client/streamableHttp.js");
  const client = new Client({ name: "security-test", version: "1" });
  await client.connect(new StreamableHTTPClientTransport(new URL(base + "/mcp"), { requestInit: { headers: { Authorization: "Bearer test-token" } } }));
  try {
    const list = await client.listTools();
    assert.equal(list.tools.find(t => t.name === "fs_read_many").annotations.readOnlyHint, true);
    assert.equal(list.tools.find(t => t.name === "fs_write_text").annotations.destructiveHint, true);
    const result = await client.callTool({ name: "fs_write_text", arguments: { file: "secret.txt", content: "private-payload" } });
    assert.equal(result.isError, true);
    await assert.rejects(fs.stat(path.join(root, "secret.txt")), { code: "ENOENT" });
    const audit = await fs.readFile(path.join(root, "audit.jsonl"), "utf8");
    assert.match(audit, /fs_write_text/);
    assert.ok(!audit.includes("private-payload"));
  } finally { await client.close(); }
});
