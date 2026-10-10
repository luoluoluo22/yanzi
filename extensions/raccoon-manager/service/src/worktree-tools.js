import path from "node:path";
import fs from "node:fs/promises";
import { createHash } from "node:crypto";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { z } from "zod";
import { config, insideRoot } from "./config.js";

const execFileAsync = promisify(execFile);
const taskIdSchema = z.string().regex(/^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$/);

async function git(cwd, args, maxBuffer = 16 * 1024 * 1024) {
  const { stdout, stderr } = await execFileAsync("git", args, { cwd, windowsHide: true, maxBuffer, encoding: "utf8" });
  return { stdout: String(stdout || ""), stderr: String(stderr || "") };
}

async function repositoryRoot(cwd) {
  const workdir = insideRoot(cwd || ".");
  return insideRoot((await git(workdir, ["rev-parse", "--show-toplevel"])).stdout.trim());
}

function taskRoot(repoRoot, taskId) {
  const key = createHash("sha256").update(repoRoot).digest("hex").slice(0, 10);
  const repoName = path.basename(repoRoot).replace(/[^a-zA-Z0-9._-]/g, "_");
  return insideRoot(path.join(config.root, ".raccoon-worktrees", `${repoName}-${key}`, taskId));
}

function parseWorktrees(text) {
  const items = [];
  let item = {};
  for (const line of text.split(/\r?\n/)) {
    if (!line) {
      if (item.path) items.push(item);
      item = {};
      continue;
    }
    const space = line.indexOf(" ");
    const key = space < 0 ? line : line.slice(0, space);
    const value = space < 0 ? true : line.slice(space + 1);
    if (key === "worktree") item.path = value;
    else if (key === "HEAD") item.head = value;
    else if (key === "branch") item.branch = String(value).replace(/^refs\/heads\//, "");
    else if (key === "detached") item.detached = true;
    else if (key === "locked") item.locked = value === true ? true : value;
    else if (key === "prunable") item.prunable = value === true ? true : value;
  }
  if (item.path) items.push(item);
  return items;
}

export async function worktreeTaskCreate({ cwd = ".", taskId, baseRef = "HEAD", branch }) {
  const repoRoot = await repositoryRoot(cwd);
  const target = taskRoot(repoRoot, taskId);
  const branchName = branch || `raccoon/task/${taskId}`;
  await fs.mkdir(path.dirname(target), { recursive: true });
  try {
    await fs.access(target);
    throw new Error(`Task worktree path already exists: ${target}`);
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }

  await git(repoRoot, ["worktree", "add", "-b", branchName, target, baseRef]);
  const head = (await git(target, ["rev-parse", "HEAD"])).stdout.trim();
  return { ok: true, action: "create", taskId, repoRoot, worktree: target, branch: branchName, baseRef, head };
}

export async function worktreeTaskList({ cwd = "." } = {}) {
  const repoRoot = await repositoryRoot(cwd);
  const parsed = parseWorktrees((await git(repoRoot, ["worktree", "list", "--porcelain"])).stdout);
  const managedRoot = insideRoot(path.join(config.root, ".raccoon-worktrees"));
  return {
    ok: true,
    action: "list",
    repoRoot,
    worktrees: parsed.map(item => ({
      ...item,
      managed: item.path ? (path.relative(managedRoot, item.path) !== ".." && !path.relative(managedRoot, item.path).startsWith(".." + path.sep)) : false
    }))
  };
}

export async function worktreeTaskRemove({ cwd = ".", taskId, force = false, deleteBranch = false }) {
  const repoRoot = await repositoryRoot(cwd);
  const target = taskRoot(repoRoot, taskId);
  const before = parseWorktrees((await git(repoRoot, ["worktree", "list", "--porcelain"])).stdout);
  const item = before.find(entry => path.resolve(entry.path || "") === path.resolve(target));
  if (!item) return { ok: true, action: "remove", taskId, removed: false, reason: "Managed task worktree does not exist.", worktree: target };

  const args = ["worktree", "remove"];
  if (force) args.push("--force");
  args.push(target);
  await git(repoRoot, args);
  if (deleteBranch && item.branch) await git(repoRoot, ["branch", "-D", item.branch]);
  return { ok: true, action: "remove", taskId, removed: true, worktree: target, branch: item.branch || null, branchDeleted: Boolean(deleteBranch && item.branch) };
}

export function registerWorktreeTaskTool(server) {
  server.registerTool("worktree_task", {
    title: "Isolated Git worktree task",
    description: "Create, list or remove Raccoon-managed Git worktrees so concurrent AI chats can develop on isolated branches without sharing one working tree.",
    inputSchema: {
      action: z.enum(["create", "list", "remove"]),
      cwd: z.string().default("."),
      taskId: taskIdSchema.optional(),
      baseRef: z.string().min(1).max(500).default("HEAD"),
      branch: z.string().min(1).max(500).optional(),
      force: z.boolean().default(false),
      deleteBranch: z.boolean().default(false)
    }
  }, async args => {
    let result;
    if (args.action === "list") result = await worktreeTaskList(args);
    else {
      if (!args.taskId) throw new Error("taskId is required for create/remove.");
      result = args.action === "create" ? await worktreeTaskCreate(args) : await worktreeTaskRemove(args);
    }
    return { content: [{ type: "text", text: JSON.stringify(result, null, 2) }] };
  });
}
