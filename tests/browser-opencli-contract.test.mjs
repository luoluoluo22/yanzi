import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { join, dirname } from "node:path";
import vm from "node:vm";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = readFileSync(join(root, "browser-extension", "opencli-background.js"), "utf8");
const listeners = [];
const outbound = [];
let connected = true;
const ctx = {
  crypto: { randomUUID: () => "b063c8a2-9843-4565-a91e-7fbc51253722" },
  chrome: { runtime: { id: "yanzi-test", onMessage: { addListener: fn => listeners.push(fn) } } },
  setTimeout, clearTimeout, Object,
  yanziBrowserHost: { isConnected: () => connected, send: message => outbound.push(message) }
};
ctx.globalThis = ctx;
vm.runInNewContext(source, ctx);
assert.equal(listeners.length, 1);
const handle = listeners[0];
function invoke(message, senderId = "yanzi-test") {
  return new Promise(resolve => {
    const ongoing = handle(message, { id: senderId }, resolve);
    if (ongoing !== true && message.action === "yanzi_opencli_read") {
      // Synchronous denial still resolves through sendResponse.
    }
  });
}
const invalid = await invoke({ action: "yanzi_opencli_read", site: "douyin", operation: "delete" });
assert.equal(invalid.ok, false);
assert.equal(outbound.length, 0);
const foreign = await invoke({ action: "yanzi_opencli_read", site: "bilibili", operation: "history" }, "other-extension");
assert.equal(foreign.ok, false);
connected = false;
const disconnected = await invoke({ action: "yanzi_opencli_read", site: "xiaohongshu", operation: "notes" });
assert.equal(disconnected.ok, false);
connected = true;
const response = invoke({ action: "yanzi_opencli_read", site: "bilibili", operation: "history" });
assert.equal(outbound.length, 1);
assert.equal(outbound[0].type, "opencli_request");
assert.equal(outbound[0].operation, "history");
ctx.handleYanziOpenCliResponse({ requestId: outbound[0].requestId, ok: true, data: [{ title: "safe" }] });
const result = await response;
assert.equal(result.ok, true);
assert.equal(result.data[0].title, "safe");
assert.equal(Object.keys(ctx).includes("yanziOpenCliPending"), false);
console.log("OpenCLI browser bridge contract OK");
