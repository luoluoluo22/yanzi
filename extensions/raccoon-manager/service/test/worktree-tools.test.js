import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const exec = promisify(execFile);
const { worktreeTaskCreate, worktreeTaskList, worktreeTaskRemove } = await import("../src/worktree-tools.js");

async function git(cwd, args) {
  return exec("git", args, { cwd, windowsHide: true, encoding: "utf8" });
}

test("worktree_task isolates concurrent development directories", async () => {
  const repoDir = await fs.mkdtemp(path.join(process.cwd(), ".raccoon-worktree-repo-"));
  const taskId = "parallel-test";
  try {
    await git(repoDir, ["init"]);
    await git(repoDir, ["config", "user.name", "Raccoon Test"]);
    await git(repoDir, ["config", "user.email", "raccoon@example.invalid"]);
    await fs.writeFile(path.join(repoDir, "state.txt"), "main\n");
    await git(repoDir, ["add", "."]);
    await git(repoDir, ["commit", "-m", "initial"]);

    const created = await worktreeTaskCreate({ cwd: repoDir, taskId });
    assert.equal(created.branch, "raccoon/task/parallel-test");
    assert.notEqual(path.resolve(created.worktree), path.resolve(repoDir));
    await fs.writeFile(path.join(created.worktree, "state.txt"), "task\n");
    assert.equal(await fs.readFile(path.join(repoDir, "state.txt"), "utf8"), "main\n");

    const listed = await worktreeTaskList({ cwd: repoDir });
    const isolated = listed.worktrees.find(item => path.resolve(item.path) === path.resolve(created.worktree));
    assert.equal(isolated?.managed, true);

    const removed = await worktreeTaskRemove({ cwd: repoDir, taskId, force: true, deleteBranch: true });
    assert.equal(removed.removed, true);
    await assert.rejects(() => fs.access(created.worktree));
  } finally {
    await worktreeTaskRemove({ cwd: repoDir, taskId, force: true, deleteBranch: true }).catch(() => {});
    await fs.rm(repoDir, { recursive: true, force: true });
  }
});
