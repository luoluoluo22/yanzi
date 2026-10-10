import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { z } from "zod";
import {
  withForegroundControl,
  assertForegroundControlReady,
  heartbeatForegroundControl,
  foregroundControlStatePath
} from "./foreground-control.js";

const automationScript = fileURLToPath(new URL("./desktop-automation.ps1", import.meta.url));

const actionSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("move"),
    x: z.number().int(),
    y: z.number().int(),
    durationMs: z.number().int().min(0).max(10000).default(0)
  }),
  z.object({
    type: z.literal("click"),
    button: z.enum(["left", "right", "middle"]).default("left"),
    x: z.number().int().optional(),
    y: z.number().int().optional(),
    count: z.number().int().min(1).max(10).default(1),
    intervalMs: z.number().int().min(0).max(2000).default(80)
  }).superRefine((value, ctx) => {
    if ((value.x == null) !== (value.y == null)) {
      ctx.addIssue({ code: "custom", message: "click x and y must be provided together." });
    }
  }),
  z.object({
    type: z.literal("scroll"),
    delta: z.number().int().min(-12000).max(12000),
    x: z.number().int().optional(),
    y: z.number().int().optional()
  }).superRefine((value, ctx) => {
    if ((value.x == null) !== (value.y == null)) {
      ctx.addIssue({ code: "custom", message: "scroll x and y must be provided together." });
    }
  }),
  z.object({
    type: z.literal("type"),
    text: z.string().max(200000),
    intervalMs: z.number().int().min(0).max(2000).default(0)
  }),
  z.object({
    type: z.literal("key"),
    key: z.string().min(1).max(40),
    count: z.number().int().min(1).max(100).default(1),
    intervalMs: z.number().int().min(0).max(2000).default(60)
  }),
  z.object({
    type: z.literal("hotkey"),
    keys: z.array(z.string().min(1).max(40)).min(2).max(8)
  }),
  z.object({
    type: z.literal("activateWindow"),
    titleContains: z.string().min(1).max(500)
  }),
  z.object({
    type: z.literal("wait"),
    ms: z.number().int().min(0).max(120000)
  })
]);

const desktopActionsInput = z.object({
  actions: z.array(actionSchema).min(1).max(500),
  owner: z.string().min(1).max(100).default("ChatGPT"),
  task: z.string().min(1).max(300).default("操作电脑"),
  ttlMs: z.number().int().min(3000).max(120000).default(15000),
  leaseId: z.string().uuid().optional()
});

function runPowerShell(actions, leaseId) {
  if (process.platform !== "win32") {
    throw new Error("Desktop input automation is currently implemented for Windows.");
  }

  return new Promise((resolve, reject) => {
    const child = spawn("powershell.exe", [
      "-NoLogo",
      "-NoProfile",
      "-Sta",
      "-WindowStyle", "Hidden",
      "-ExecutionPolicy", "Bypass",
      "-File", automationScript,
      "-StateFile", foregroundControlStatePath()
    ], {
      windowsHide: true,
      stdio: ["pipe", "pipe", "pipe"]
    });

    let stdout = "";
    let stderr = "";
    const maxOutput = 1024 * 1024;

    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", chunk => {
      if (Buffer.byteLength(stdout, "utf8") < maxOutput) stdout += chunk;
    });
    child.stderr.on("data", chunk => {
      if (Buffer.byteLength(stderr, "utf8") < maxOutput) stderr += chunk;
    });

    child.once("error", reject);
    child.once("exit", code => {
      if (code !== 0) {
        reject(new Error((stderr || stdout || `desktop automation exited with code ${code}`).trim()));
        return;
      }
      const trimmed = stdout.trim();
      if (!trimmed) {
        resolve({ ok: true, actionsCompleted: actions.length });
        return;
      }
      const line = trimmed.split(/\r?\n/).filter(Boolean).at(-1);
      try {
        resolve(JSON.parse(line));
      } catch {
        resolve({ ok: true, actionsCompleted: actions.length, output: trimmed });
      }
    });

    child.stdin.end(JSON.stringify({ leaseId, actions }));
  });
}

async function withExistingLease(leaseId, task, operation) {
  await assertForegroundControlReady(leaseId);
  await heartbeatForegroundControl({ leaseId, task });
  const timer = setInterval(() => {
    heartbeatForegroundControl({ leaseId, task }).catch(() => {});
  }, 3000);
  timer.unref();
  try {
    return await operation({ leaseId });
  } finally {
    clearInterval(timer);
  }
}

export async function runDesktopActions(input, executor = runPowerShell) {
  const args = desktopActionsInput.parse(input);
  const started = Date.now();

  const execute = async lease => {
    await assertForegroundControlReady(lease.leaseId);
    const result = await executor(args.actions, lease.leaseId);
    await assertForegroundControlReady(lease.leaseId);
    return {
      ...result,
      leaseId: lease.leaseId,
      autoLease: !args.leaseId,
      durationMs: Date.now() - started
    };
  };

  if (args.leaseId) {
    return withExistingLease(args.leaseId, args.task, execute);
  }

  return withForegroundControl({
    owner: args.owner,
    task: args.task,
    ttlMs: args.ttlMs
  }, execute);
}

export async function listDesktopWindows() {
  if (process.platform !== "win32") {
    throw new Error("Desktop window inspection is currently implemented for Windows.");
  }

  const script = [
    "$ErrorActionPreference='Stop'",
    "Add-Type @'",
    "using System;",
    "using System.Text;",
    "using System.Runtime.InteropServices;",
    "using System.Collections.Generic;",
    "public class RaccoonWindowList {",
    "  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);",
    "  [DllImport(\"user32.dll\")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);",
    "  [DllImport(\"user32.dll\")] static extern bool IsWindowVisible(IntPtr hWnd);",
    "  [DllImport(\"user32.dll\")] static extern int GetWindowTextLength(IntPtr hWnd);",
    "  [DllImport(\"user32.dll\", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int n);",
    "  [DllImport(\"user32.dll\")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);",
    "  public static string GetJson() {",
    "    var rows = new List<string>();",
    "    EnumWindows((h, l) => {",
    "      if (!IsWindowVisible(h)) return true;",
    "      int len = GetWindowTextLength(h); if (len <= 0) return true;",
    "      var sb = new StringBuilder(len + 1); GetWindowText(h, sb, sb.Capacity);",
    "      uint pid; GetWindowThreadProcessId(h, out pid);",
    "      string t = sb.ToString().Replace(\"\\\\\",\"\\\\\\\\\").Replace(\"\\\"\",\"\\\\\\\"\");",
    "      rows.Add(\"{\\\"handle\\\":\\\"\" + h.ToInt64().ToString(\"X\") + \"\\\",\\\"pid\\\":\" + pid + \",\\\"title\\\":\\\"\" + t + \"\\\"}\");",
    "      return true;",
    "    }, IntPtr.Zero);",
    "    return \"[\" + string.Join(\",\", rows) + \"]\";",
    "  }",
    "}",
    "'@",
    "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)",
    "[RaccoonWindowList]::GetJson()"
  ].join("\r\n");

  return new Promise((resolve, reject) => {
    const child = spawn("powershell.exe", [
      "-NoLogo", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script
    ], { windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });

    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", chunk => { stdout += chunk; });
    child.stderr.on("data", chunk => { stderr += chunk; });
    child.once("error", reject);
    child.once("exit", code => {
      if (code !== 0) return reject(new Error(stderr.trim() || "Unable to enumerate desktop windows."));
      try { resolve(JSON.parse(stdout.trim() || "[]")); }
      catch { reject(new Error("Unable to parse desktop window list.")); }
    });
  });
}

export function registerDesktopAutomationTools(server) {
  server.registerTool("desktop_actions", {
    title: "Run Windows desktop actions",
    description: "Run a batch of Windows mouse, keyboard, scroll, wait and window-activation actions. Foreground control is acquired automatically, a visible AI-control overlay is shown for the whole batch, heartbeat is maintained, and the lease is released when complete. Pass leaseId only when intentionally joining a manually acquired foreground-control session.",
    inputSchema: {
      actions: z.array(actionSchema).min(1).max(500),
      owner: z.string().min(1).max(100).default("ChatGPT"),
      task: z.string().min(1).max(300).default("操作电脑"),
      ttlMs: z.number().int().min(3000).max(120000).default(15000),
      leaseId: z.string().uuid().optional()
    }
  }, async args => ({
    content: [{ type: "text", text: JSON.stringify(await runDesktopActions(args), null, 2) }]
  }));

  server.registerTool("desktop_windows", {
    title: "List Windows desktop windows",
    description: "List visible top-level Windows windows with title, process ID and native handle. This is read-only and does not claim foreground control.",
    inputSchema: {}
  }, async () => ({
    content: [{ type: "text", text: JSON.stringify(await listDesktopWindows(), null, 2) }]
  }));
}
