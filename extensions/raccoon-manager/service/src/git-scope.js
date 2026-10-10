import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { z } from "zod";
import { insideRoot } from "./config.js";

const execFileAsync = promisify(execFile);

async function git(cwd, args, maxBuffer = 16 * 1024 * 1024) {
  const { stdout, stderr } = await execFileAsync("git", args, { cwd, windowsHide: true, maxBuffer, encoding: "utf8" });
  return { stdout: String(stdout || ""), stderr: String(stderr || "") };
}

function splitZero(value) {
  return value.split("\0").filter(Boolean);
}

function relativeInside(repoRoot, input) {
  const absolute = insideRoot(path.resolve(repoRoot, input));
  const relative = path.relative(repoRoot, absolute);
  if (relative === ".." || relative.startsWith(".." + path.sep) || path.isAbsolute(relative)) {
    throw new Error(`Path is outside repository: ${input}`);
  }
  return relative || ".";
}

function coveredBy(pathName, requested) {
  const normalized = pathName.split(path.sep).join("/");
  return requested.some(scope => {
    const root = scope.split(path.sep).join("/").replace(/^\.\//, "").replace(/\/$/, "");
    return root === "." || normalized === root || normalized.startsWith(root + "/");
  });
}

export async function gitCommitScope({ cwd = ".", paths, message, allowEmpty = false }) {
  const workdir = insideRoot(cwd);
  const repoRootRaw = (await git(workdir, ["rev-parse", "--show-toplevel"])).stdout.trim();
  const repoRoot = insideRoot(repoRootRaw);
  const scopes = paths.map(item => relativeInside(repoRoot, item));

  const beforeStaged = splitZero((await git(repoRoot, ["diff", "--cached", "--name-only", "-z"])).stdout);
  if (beforeStaged.length) {
    throw new Error(`Refusing scoped commit because the index already contains staged changes: ${beforeStaged.join(", ")}`);
  }

  let staged = [];
  try {
    await git(repoRoot, ["add", "--", ...scopes]);
    staged = splitZero((await git(repoRoot, ["diff", "--cached", "--name-only", "-z"])).stdout);
    const unexpected = staged.filter(name => !coveredBy(name, scopes));
    if (unexpected.length) throw new Error(`Scoped staging captured unexpected paths: ${unexpected.join(", ")}`);
    if (!staged.length && !allowEmpty) {
      return { ok: true, committed: false, reason: "No changes in requested scope.", repoRoot, paths: scopes, staged: [] };
    }

    const args = ["commit", "-m", message];
    if (allowEmpty && !staged.length) args.push("--allow-empty");
    await git(repoRoot, args);
    const commit = (await git(repoRoot, ["rev-parse", "HEAD"])).stdout.trim();
    const summary = (await git(repoRoot, ["show", "--stat", "--oneline", "--no-renames", "-1", commit])).stdout;
    return { ok: true, committed: true, commit, repoRoot, paths: scopes, files: staged, summary };
  } catch (error) {
    if (staged.length) {
      await git(repoRoot, ["reset", "--", ...scopes]).catch(() => {});
    }
    throw error;
  }
}

export function registerGitScopeTool(server) {
  server.registerTool("git_commit_scope", {
    title: "Scoped Git commit",
    description: "Stage and commit only explicit files/directories. Refuses to run when unrelated changes are already staged and verifies the staged set before commit.",
    inputSchema: {
      cwd: z.string().default("."),
      paths: z.array(z.string().min(1)).min(1).max(10000),
      message: z.string().min(1).max(10000),
      allowEmpty: z.boolean().default(false)
    }
  }, async args => ({
    content: [{ type: "text", text: JSON.stringify(await gitCommitScope(args), null, 2) }]
  }));
}
