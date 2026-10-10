import fs from "node:fs/promises";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";

const root = process.cwd();
const tempRelative = "raccoon-smoke-test.txt";
const tempAbsolute = root + "/" + tempRelative;
const nodeCommand = process.platform === "win32" ? `& '${process.execPath.replaceAll("'", "''")}'` : `'${process.execPath.replaceAll("'", "'\\''")}'`;

function data(result) {
  const item = result.content?.find(part => part.type === "text");
  if (!item) throw new Error("Tool returned no text content");
  return JSON.parse(item.text);
}

const transport = new StdioClientTransport({
  command: process.execPath,
  args: ["src/index.js"],
  env: {
    RACCOON_ROOT: root,
    RACCOON_ENABLE_SHELL: "1", RACCOON_READ_ONLY: "0", RACCOON_TRANSPORT: "stdio"
  },
  cwd: process.cwd(),
  stderr: "pipe"
});

const client = new Client({ name: "raccoon-smoke", version: "0.7.0" });
const smokeStarted = Date.now();
function mark(stage) {
  console.error(`[smoke +${Date.now() - smokeStarted}ms] ${stage}`);
}

try {
  mark("connect:start");
  await client.connect(transport);
  mark("connect:done");
  const listed = await client.listTools();
  mark("listTools:done");

  await client.callTool({
    name: "fs_write_text",
    arguments: { file: tempRelative, content: "seed\n" }
  });

  mark("concurrent-patch:start");
  const [patchA, patchB] = await Promise.all([
    client.callTool({
      name: "fs_apply_patch_many",
      arguments: {
        files: [{
          file: tempRelative,
          replacements: [{ oldText: "seed", newText: "winner-a" }]
        }]
      }
    }),
    client.callTool({
      name: "fs_apply_patch_many",
      arguments: {
        files: [{
          file: tempRelative,
          replacements: [{ oldText: "seed", newText: "winner-b" }]
        }]
      }
    })
  ]);

  mark("concurrent-patch:done");
  const patchResults = [data(patchA)[0], data(patchB)[0]];
  if (patchResults.filter(item => item.ok).length !== 1) {
    throw new Error("Concurrent patch lock test failed");
  }

  const read = data(await client.callTool({
    name: "fs_read_many",
    arguments: { paths: [tempRelative] }
  }));
  if (!/winner-(a|b)/.test(read[0]?.content || "")) {
    throw new Error("Patched file content is unexpected");
  }

  mark("workflow:start");
  const workflow = data(await client.callTool({
    name: "project_build_test",
    arguments: {
      cwd: ".",
      steps: [
        { name: "syntax", command: `${nodeCommand} --check src/index.js` },
        { name: "version", command: `${nodeCommand} -v` }
      ]
    }
  }));
  mark("workflow:done");
  if (!workflow.ok) throw new Error("Project workflow smoke failed: " + JSON.stringify(workflow));

  mark("persistent-process:start");
  const started = data(await client.callTool({
    name: "process_start",
    arguments: {
      cwd: ".",
      command: process.platform === "win32" ? "Write-Output 'process-one'; Start-Sleep -Milliseconds 200; Write-Output 'process-two'" : "printf 'process-one\n'; sleep 0.2; printf 'process-two\n'"
    }
  }));

  let cursor = 0;
  let processText = "";
  let processState = null;
  for (let i = 0; i < 30; i += 1) {
    processState = data(await client.callTool({
      name: "process_read",
      arguments: {
        sessionId: started.sessionId,
        cursor,
        waitMs: 1000
      }
    }));
    cursor = processState.nextCursor;
    processText += processState.events.map(event => event.text).join("");
    if (processState.status !== "running") break;
  }

  mark("persistent-process:done");
  if (!processText.includes("process-one") || !processText.includes("process-two")) {
    throw new Error("Persistent process output smoke failed: " + JSON.stringify({ status: processState?.status, output: processText }));
  }

  mark("interactive-process:start");
  const interactive = data(await client.callTool({
    name: "process_start",
    arguments: {
      cwd: ".",
      command: process.platform === "win32" ? "$line = [Console]::In.ReadLine(); Write-Output ('echo:' + $line)" : "read line; printf 'echo:%s\n' \"$line\""
    }
  }));

  await client.callTool({
    name: "process_write",
    arguments: {
      sessionId: interactive.sessionId,
      input: "hello-raccoon",
      appendNewline: true
    }
  });

  let interactiveCursor = 0;
  let interactiveText = "";
  for (let i = 0; i < 30; i += 1) {
    const state = data(await client.callTool({
      name: "process_read",
      arguments: {
        sessionId: interactive.sessionId,
        cursor: interactiveCursor,
        waitMs: 1000
      }
    }));
    interactiveCursor = state.nextCursor;
    interactiveText += state.events.map(event => event.text).join("");
    if (state.status !== "running") break;
  }

  mark("interactive-process:done");
  if (!interactiveText.includes("echo:hello-raccoon")) {
    throw new Error("Persistent process stdin smoke failed");
  }

  mark("kill-process:start");
  const longRunning = data(await client.callTool({
    name: "process_start",
    arguments: {
      cwd: ".",
      command: process.platform === "win32" ? "Write-Output 'kill-ready'; Start-Sleep -Seconds 30" : "printf 'kill-ready\n'; sleep 30"
    }
  }));

  await client.callTool({
    name: "process_kill",
    arguments: { sessionId: longRunning.sessionId }
  });

  let killedState = null;
  for (let i = 0; i < 5; i += 1) {
    killedState = data(await client.callTool({
      name: "process_read",
      arguments: {
        sessionId: longRunning.sessionId,
        cursor: 0,
        waitMs: 500
      }
    }));
    if (killedState.status !== "running") break;
  }

  mark("kill-process:done");
  if (killedState.status === "running") {
    throw new Error("Persistent process kill smoke failed");
  }

  mark("git-summary:start");
  const git = data(await client.callTool({
    name: "git_summary",
    arguments: { cwd: ".", includePatch: false }
  }));

  mark("git-summary:done");
  console.log(JSON.stringify({
    tools: listed.tools.map(tool => tool.name),
    fileLock: {
      patchResults,
      content: read[0].content
    },
    workflow: {
      ok: workflow.ok,
      stepsCompleted: workflow.stepsCompleted
    },
    process: {
      sessionId: started.sessionId,
      status: processState.status,
      output: processText.trim()
    },
    git: {
      ok: git.ok,
      status: git.status
    }
  }, null, 2));
} finally {
  await client.close().catch(() => {});
  await fs.rm(tempAbsolute, { force: true }).catch(() => {});
}
