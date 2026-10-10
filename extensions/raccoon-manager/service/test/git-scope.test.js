import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const exec = promisify(execFile);
const { gitCommitScope } = await import("../src/git-scope.js");

async function git(cwd, args) {
  return exec("git", args, { cwd, windowsHide: true, encoding: "utf8" });
}

test("git_commit_scope commits only requested paths", async () => {
  const dir = await fs.mkdtemp(path.join(process.cwd(), ".raccoon-git-test-"));
  try {
    await git(dir, ["init"]);
    await git(dir, ["config", "user.name", "Raccoon Test"]);
    await git(dir, ["config", "user.email", "raccoon@example.invalid"]);
    await fs.writeFile(path.join(dir, "a.txt"), "a1\n");
    await fs.writeFile(path.join(dir, "b.txt"), "b1\n");
    await git(dir, ["add", "."]);
    await git(dir, ["commit", "-m", "initial"]);

    await fs.writeFile(path.join(dir, "a.txt"), "a2\n");
    await fs.writeFile(path.join(dir, "b.txt"), "b2\n");
    const result = await gitCommitScope({ cwd: dir, paths: ["a.txt"], message: "scope a" });
    assert.equal(result.committed, true);
    assert.deepEqual(result.files, ["a.txt"]);
    const status = (await git(dir, ["status", "--porcelain=v1"])).stdout;
    assert.match(status, /b\.txt/);
    assert.doesNotMatch(status, /a\.txt/);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test("git_commit_scope refuses an already dirty index", async () => {
  const dir = await fs.mkdtemp(path.join(process.cwd(), ".raccoon-git-test-"));
  try {
    await git(dir, ["init"]);
    await git(dir, ["config", "user.name", "Raccoon Test"]);
    await git(dir, ["config", "user.email", "raccoon@example.invalid"]);
    await fs.writeFile(path.join(dir, "a.txt"), "a1\n");
    await fs.writeFile(path.join(dir, "b.txt"), "b1\n");
    await git(dir, ["add", "."]);
    await git(dir, ["commit", "-m", "initial"]);
    await fs.writeFile(path.join(dir, "a.txt"), "a2\n");
    await fs.writeFile(path.join(dir, "b.txt"), "b2\n");
    await git(dir, ["add", "b.txt"]);
    await assert.rejects(() => gitCommitScope({ cwd: dir, paths: ["a.txt"], message: "scope a" }), /already contains staged changes/);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
