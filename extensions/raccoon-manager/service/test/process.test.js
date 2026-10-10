import test from "node:test";
import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { shellCommand, startManagedProcess, readManagedProcess, shutdownManagedProcesses } from "../src/process-manager.js";

const execFileAsync = promisify(execFile);

test.after(shutdownManagedProcesses);

test("Windows shell runs without a visible console", { skip: process.platform !== "win32" }, async () => {
  const command = `Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class RaccoonConsoleProbe { [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow(); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle); }'; [RaccoonConsoleProbe]::IsWindowVisible([RaccoonConsoleProbe]::GetConsoleWindow())`;
  const shell = shellCommand(command);
  const { stdout, stderr } = await execFileAsync(shell.executable, shell.args, { windowsHide: true });
  assert.equal(stderr, "");
  assert.equal(stdout.trim(), "False");
});

test("Windows shell output is normalized to UTF-8", { skip: process.platform !== "win32" }, async () => {
  const shell = shellCommand("Write-Output '中文输出测试'; chcp");
  const { stdout, stderr } = await execFileAsync(shell.executable, shell.args, {
    encoding: "utf8",
    windowsHide: true
  });
  assert.equal(stderr, "");
  assert.match(stdout, /中文输出测试/);
  assert.match(stdout, /65001/);
});

test("large Unicode output survives chunks and final process close", async () => {
  const exe = `'${process.execPath.replaceAll("'", process.platform === "win32" ? "''" : "'\\''")}'`;
  const command = `${process.platform === "win32" ? "& " : ""}${exe} -e 'process.stdout.write(String.fromCodePoint(30028).repeat(50000))'`;
  const session = startManagedProcess({ command, cwd: process.cwd(), maxLogBytes: 1024 * 1024 });
  let cursor = 0, text = "", state;
  for (let i = 0; i < 20; i++) {
    state = await readManagedProcess({ sessionId: session.sessionId, cursor, maxBytes: 64 * 1024, waitMs: 1000 });
    cursor = state.nextCursor;
    text += state.events.map(e => e.text).join("");
    if (state.status !== "running" && cursor >= state.nextCursor && !state.events.length) break;
  }
  assert.equal(state.status, "exited");
  assert.equal(text, "界".repeat(50000));
});
