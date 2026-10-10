import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";

process.env.RACCOON_ENABLE_SHELL = "1";
const { applyFileTransaction } = await import("../src/fs-transaction.js");

test("fs_transaction validates all patches before any write", async () => {
  const dir = await fs.mkdtemp(path.join(process.cwd(), ".raccoon-tx-test-"));
  const a = path.join(dir, "a.txt"), b = path.join(dir, "b.txt");
  try {
    await fs.writeFile(a, "alpha", "utf8");
    await fs.writeFile(b, "beta", "utf8");
    await assert.rejects(() => applyFileTransaction({
      files: [
        { file: a, replacements: [{ oldText: "alpha", newText: "ALPHA", replaceAll: false }] },
        { file: b, replacements: [{ oldText: "missing", newText: "BETA", replaceAll: false }] }
      ]
    }), /oldText not found/);
    assert.equal(await fs.readFile(a, "utf8"), "alpha");
    assert.equal(await fs.readFile(b, "utf8"), "beta");
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test("fs_transaction restores a file even when its write partially fails", async () => {
  const dir = await fs.mkdtemp(path.join(process.cwd(), ".raccoon-tx-test-"));
  const file = path.join(dir, "partial.txt");
  const originalWrite = fs.writeFile;
  try {
    await originalWrite(file, "original");
    fs.writeFile = async (target, content, ...options) => {
      if (target === file && content === "updated") {
        await originalWrite(target, "up", ...options);
        throw new Error("simulated disk write failure");
      }
      return originalWrite(target, content, ...options);
    };
    const result = await applyFileTransaction({ files: [
      { file, replacements: [{ oldText: "original", newText: "updated" }] }
    ] });
    assert.equal(result.ok, false);
    assert.equal(result.rolledBack, true);
    assert.equal(await fs.readFile(file, "utf8"), "original");
  } finally {
    fs.writeFile = originalWrite;
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test("fs_transaction rolls back when validation command fails", async () => {
  const dir = await fs.mkdtemp(path.join(process.cwd(), ".raccoon-tx-test-"));
  const a = path.join(dir, "a.txt"), b = path.join(dir, "b.txt");
  try {
    await fs.writeFile(a, "alpha", "utf8");
    await fs.writeFile(b, "beta", "utf8");
    const result = await applyFileTransaction({
      files: [
        { file: a, replacements: [{ oldText: "alpha", newText: "ALPHA", replaceAll: false }] },
        { file: b, replacements: [{ oldText: "beta", newText: "BETA", replaceAll: false }] }
      ],
      validate: { command: "node -e \"process.exit(9)\"", cwd: dir, runner: "default", timeoutMs: 30000, maxOutputBytes: 1024 * 1024 }
    });
    assert.equal(result.ok, false);
    assert.equal(result.rolledBack, true);
    assert.equal(await fs.readFile(a, "utf8"), "alpha");
    assert.equal(await fs.readFile(b, "utf8"), "beta");
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
