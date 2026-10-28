import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const source = fs.readFileSync(new URL("../../../browser-extension/chatgpt-background.js", import.meta.url), "utf8");
const start = source.indexOf("// Only inspect the original reserved tab");
const end = source.indexOf("async function runChatGptTask(task) {", start);
assert.ok(start >= 0 && end > start, "recovery helper exists");
const functionSource = source.slice(start, end);

function buildRecovery({ lastUserText, replyText, settled = true, active = false, url = "https://chatgpt.com/c/safe-id" }) {
  let sends = 0;
  let now = 0;
  const chrome = {
    tabs: { get: async () => ({ id: 42, url, active, pinned: false, discarded: false, status: "complete" }) },
    scripting: { executeScript: async () => [{ result: { url, lastUserText, replyText, settled } }] }
  };
  const scope = {
    chrome,
    Date: { now: () => (now += 2000) },
    setTimeout: callback => { sends++; callback(); },
    withChatGptPageScriptTimeout: operation => operation(),
    URL
  };
  vm.createContext(scope);
  vm.runInContext(functionSource + "\nthis.recover = recoverChatGptPageAfterChannelDrop;", scope);
  return { recover: scope.recover, counts: () => ({ waits: sends }) };
}

test("read-only recovery confirms original prompt and settled assistant response", async () => {
  const agent = buildRecovery({ lastUserText: "original  prompt", replyText: "verified answer" });
  const result = await agent.recover(42, "original prompt", { maxWaitMs: 10000 });
  assert.equal(result.status, "success");
  assert.equal(result.data.text, "verified answer");
  assert.equal(result.data.tabId, 42);
  assert.equal(result.data.recoveredFromChannelDrop, true);
  assert.ok(agent.counts().waits >= 1);
});

test("read-only recovery refuses mismatched user turn", async () => {
  const agent = buildRecovery({ lastUserText: "another task", replyText: "unrelated answer" });
  assert.equal(await agent.recover(42, "original prompt", { maxWaitMs: 6000 }), null);
});

test("read-only recovery refuses unfinished assistant response", async () => {
  const agent = buildRecovery({ lastUserText: "original prompt", replyText: "partial answer", settled: false });
  assert.equal(await agent.recover(42, "original prompt", { maxWaitMs: 6000 }), null);
});

test("read-only recovery refuses foreground or user-controlled tab", async () => {
  const agent = buildRecovery({ lastUserText: "original prompt", replyText: "answer", active: true });
  assert.equal(await agent.recover(42, "original prompt", { maxWaitMs: 6000 }), null);
});
