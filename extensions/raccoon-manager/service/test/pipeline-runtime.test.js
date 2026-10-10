import test from "node:test";
import assert from "node:assert/strict";
import {
  buildPipelineContext,
  captureOutputs,
  evaluateExpression,
  getPath,
  interpolateTemplate,
  matchesProbe,
  sleep
} from "../src/pipeline-runtime.js";

test("pipeline outputs interpolate across nodes", () => {
  const run = { nodes: [
    { id: "write", status: "succeeded", attempts: 1, outputs: { revision: 42, extensionId: "abc" }, result: { exitCode: 0 } },
    { id: "phone", status: "pending", attempts: 0, outputs: {} }
  ]};
  const context = buildPipelineContext(run, "phone");
  assert.equal(interpolateTemplate("test-phone --revision ${write.revision} --id ${write.extensionId}", context), "test-phone --revision 42 --id abc");
  assert.equal(evaluateExpression("${write.status} == 'succeeded' && ${write.revision} >= 40", context), true);
  assert.equal(evaluateExpression("exists ${write.extensionId} && ${write.extensionId} matches '^a'", context), true);
});

test("already cancelled waits reject immediately", async () => {
  const controller = new AbortController();
  controller.abort(new Error("cancelled before wait"));
  await assert.rejects(sleep(10000, controller.signal), /cancelled before wait/);
});

test("capture supports regex, JSON paths and result paths", () => {
  const regex = captureOutputs({
    revision: { source: "stdout", regex: "revision=(\\d+)", group: 1 },
    exit: { source: "stdout", path: "exitCode" }
  }, { stdout: "revision=123", stderr: "", exitCode: 0 });
  assert.deepEqual(regex, { revision: "123", exit: 0 });

  const json = captureOutputs({
    extensionId: { source: "stdout", jsonPath: "$.data.extensionId" }
  }, { stdout: JSON.stringify({ data: { extensionId: "ext-7" } }), stderr: "", exitCode: 0 });
  assert.equal(json.extensionId, "ext-7");
  assert.equal(getPath({ items: [{ online: true }] }, "$.items[0].online"), true);
});

test("probe conditions match structured command output", () => {
  const result = { ok: true, exitCode: 0, stdout: JSON.stringify({ serverNow: { present: true } }), stderr: "" };
  assert.equal(matchesProbe(result, { exitCode: 0, jsonPathExists: "$.serverNow.present" }), true);
  assert.equal(matchesProbe(result, { jsonPathEquals: { path: "$.serverNow.present", value: true } }), true);
  assert.equal(matchesProbe(result, { stdoutRegex: "serverNow" }), true);
  assert.equal(matchesProbe(result, { jsonPathEquals: { path: "$.serverNow.present", value: false } }), false);
});
