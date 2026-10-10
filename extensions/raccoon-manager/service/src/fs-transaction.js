import fs from "node:fs/promises";
import path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { z } from "zod";
import { config, insideRoot } from "./config.js";
import { withFileLock } from "./state.js";
import { shellCommand } from "./process-manager.js";
import { withHeavyPermit } from "./resource-governor.js";
import { runnerExecutable } from "./pipeline-runtime.js";
import { isProtectedSource, decodeSource, encodeSource, sourceMetrics, backupSource, replaceSourceBytes, verifySourceReadBack } from './source-integrity.js';

const execFileAsync = promisify(execFile);

function countOccurrences(content, needle) {
  if (!needle) return 0;
  let count = 0, offset = 0;
  while (true) {
    const index = content.indexOf(needle, offset);
    if (index < 0) return count;
    count++;
    offset = index + needle.length;
  }
}

function applyReplacements(original, replacements) {
  let updated = original;
  let applied = 0;
  for (const replacement of replacements) {
    const matches = countOccurrences(updated, replacement.oldText);
    if (matches === 0) throw new Error("oldText not found");
    if (!replacement.replaceAll && matches > 1) {
      throw new Error(`oldText is ambiguous (${matches} matches); use a larger oldText or replaceAll=true`);
    }
    if (replacement.replaceAll) {
      updated = updated.split(replacement.oldText).join(replacement.newText);
      applied += matches;
    } else {
      const index = updated.indexOf(replacement.oldText);
      updated = updated.slice(0, index) + replacement.newText + updated.slice(index + replacement.oldText.length);
      applied++;
    }
  }
  return { updated, applied };
}

async function withOrderedLocks(targets, fn, index = 0) {
  if (index >= targets.length) return fn();
  return withFileLock(targets[index], () => withOrderedLocks(targets, fn, index + 1));
}

async function runValidation(validate) {
  if (!validate) return null;
  if (!config.shellEnabled) throw new Error("Transaction validation command requires RACCOON_ENABLE_SHELL=1.");
  const cwd = insideRoot(validate.cwd || ".");
  const shell = shellCommand(validate.command, runnerExecutable(validate.runner || "default"));
  if (process.platform === "win32" && /(?:powershell|pwsh)(?:\.exe)?$/i.test(shell.executable)) {
    shell.args[shell.args.length - 1] = "$ErrorActionPreference = 'Stop'; " + shell.args.at(-1) + "; $_raccoonTransactionLastSuccess = $?; if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; if (-not $_raccoonTransactionLastSuccess) { exit 1 }";
  }
  const started = Date.now();
  try {
    const { stdout, stderr } = await withHeavyPermit(() => execFileAsync(shell.executable, shell.args, {
      cwd,
      timeout: validate.timeoutMs,
      windowsHide: true,
      maxBuffer: validate.maxOutputBytes
    }));
    const ok = !validate.stdoutIncludes || String(stdout || "").includes(validate.stdoutIncludes);
    return {
      ok,
      exitCode: 0,
      durationMs: Date.now() - started,
      stdout: String(stdout || ""),
      stderr: String(stderr || ""),
      error: ok ? null : "Expected validation text was not present."
    };
  } catch (error) {
    return {
      ok: false,
      exitCode: typeof error?.code === "number" ? error.code : null,
      signal: error?.signal ?? null,
      durationMs: Date.now() - started,
      stdout: String(error?.stdout || ""),
      stderr: String(error?.stderr || error?.message || error),
      error: String(error?.message || error)
    };
  }
}

export async function applyFileTransaction({ files, dryRun = false, validate }) {
  const targets = files.map(item => insideRoot(item.file));
  if (targets.some(isProtectedSource) && !dryRun && !validate?.command?.trim()) {
    throw new Error('main.cs requires a compile validation command in fs_transaction.validate.');
  }
  if (validate && !config.shellEnabled) throw new Error('Transaction validation command requires RACCOON_ENABLE_SHELL=1.');
  if (new Set(targets).size !== targets.length) throw new Error("Each transaction file may appear only once.");
  const orderedTargets = [...targets].sort((a, b) => a.localeCompare(b));

  return withOrderedLocks(orderedTargets, async () => {
    const originals = new Map();
    const prepared = [];
    for (let i = 0; i < files.length; i++) {
      const item = files[i], target = targets[i];
      const raw = await fs.readFile(target);
      const protectedSource = isProtectedSource(target);
      const backup = protectedSource && !dryRun ? await backupSource(target, raw) : undefined;
      const original = protectedSource ? decodeSource(raw) : raw.toString('utf8');
      const { updated, applied } = applyReplacements(original, item.replacements);
      const encoded = protectedSource ? encodeSource(updated) : Buffer.from(updated, 'utf8');
      originals.set(target, raw);
      prepared.push({
        file: item.file,
        target,
        updated,
        encoded,
        protectedSource,
        integrity: protectedSource ? {backup, before:sourceMetrics(raw), after:sourceMetrics(encoded), quickScan:'Unicode round-trip passed; syntax checked by compile command', syntaxValidation:'pending compile command', sizeDelta:encoded.length - raw.length} : undefined,
        replacementsApplied: applied,
        bytesBefore: raw.length,
        bytesAfter: encoded.length,
        changed: !encoded.equals(raw)
      });
    }

    if (dryRun) {
      return { ok: true, dryRun: true, rolledBack: false, files: prepared.map(({ target, updated, encoded, protectedSource, ...item }) => item), validation: null };
    }

    const written = [];
    try {
      for (const item of prepared) {
        if (!item.changed) {
          if (item.protectedSource) written.push(item.target);
          continue;
        }
        written.push(item.target);
        if (item.protectedSource) {
          await replaceSourceBytes(item.target, item.encoded);
          item.integrity.readBack = await verifySourceReadBack(item.target, item.encoded);
          item.integrity.readBackVerified = true;
        } else await fs.writeFile(item.target, item.updated, "utf8");
      }
      const validation = await runValidation(validate);
      if (validation && !validation.ok) throw Object.assign(new Error("Transaction validation failed."), { validation });
      // A build command may itself modify files; recheck before committing.
      for (const item of prepared.filter(item => item.protectedSource)) {
        if (!(await fs.readFile(item.target)).equals(item.encoded)) throw new Error('Compile validation modified main.cs; rolling back.');
        item.integrity.syntaxValidation = 'validation command passed';
      }
      return {
        ok: true,
        dryRun: false,
        rolledBack: false,
        files: prepared.map(({ target, updated, encoded, protectedSource, ...item }) => item),
        validation
      };
    } catch (error) {
      const rollbackErrors = [];
      for (const target of written.reverse()) {
        try {
          if (isProtectedSource(target)) await replaceSourceBytes(target, originals.get(target));
          else await fs.writeFile(target, originals.get(target));
          if (!(await fs.readFile(target)).equals(originals.get(target))) throw new Error('Rollback byte verification failed.');
        }
        catch (rollbackError) { rollbackErrors.push({ file: target, error: String(rollbackError?.message || rollbackError) }); }
      }
      return {
        ok: false,
        dryRun: false,
        rolledBack: rollbackErrors.length === 0,
        error: String(error?.message || error),
        validation: error.validation || null,
        rollbackErrors,
        files: prepared.map(({ target, updated, encoded, protectedSource, ...item }) => item)
      };
    }
  });
}

export function registerFsTransactionTool(server) {
  server.registerTool("fs_transaction", {
    title: "Transactional multi-file patch",
    description: "Transactional source edits: main.cs requires validate.command to compile its project. Strict UTF-8 input, persistent byte backup, forced UTF-8 BOM, integrity statistics, atomic replacement and read-back verification; compile failure restores exact original bytes. Other files support optional validation.",
    inputSchema: {
      files: z.array(z.object({
        file: z.string().min(1),
        replacements: z.array(z.object({
          oldText: z.string().min(1),
          newText: z.string(),
          replaceAll: z.boolean().default(false)
        })).min(1).max(config.maxReplacementsPerFile)
      })).min(1).max(config.maxPatchFiles),
      dryRun: z.boolean().default(false),
      validate: z.object({
        command: z.string().min(1).max(65536),
        cwd: z.string().default("."),
        runner: z.enum(["default", "powershell5", "powershell7", "cmd", "bash"]).default("default"),
        timeoutMs: z.number().int().min(100).max(24 * 60 * 60 * 1000).default(300000),
        maxOutputBytes: z.number().int().min(1024).max(config.maxBuildOutputBytes).default(Math.min(4 * 1024 * 1024, config.maxBuildOutputBytes)),
        stdoutIncludes: z.string().max(16384).optional()
      }).optional()
    }
  }, async args => {
    const result = await applyFileTransaction(args);
    return {isError:!result.ok, content:[{type:'text', text:JSON.stringify(result, null, 2)}]};
  });
}
