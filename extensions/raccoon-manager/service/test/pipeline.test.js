import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawn, execFile } from "node:child_process";
import { promisify } from "node:util";

const root = fs.mkdtempSync(path.join(os.tmpdir(), "raccoon-pipeline-test-"));
process.env.RACCOON_ROOT = root;
process.env.RACCOON_FILESYSTEM_UNRESTRICTED = "0";
const { PipelineEngine, validatePipeline } = await import("../src/dev-pipeline.js");
const { configureGovernor, withHeavyPermit } = await import("../src/resource-governor.js");
const quote = text => "'" + text.replaceAll("'", process.platform === "win32" ? "''" : "'\\''") + "'";
const command = code => `${process.platform === "win32" ? "& " : ""}${quote(process.execPath)} -e ${quote(code)}`;
const node = (id, code = "console.log('ok')", rest = {}) => ({ id, command: command(code), ...rest });
const spec = nodes => ({ name: "test", nodes });
let engine;
test.beforeEach(() => { engine = new PipelineEngine(path.join(root, `store-${Math.random()}`)); });
test.afterEach(async () => { await engine.close(); });
test.after(() => fs.rmSync(root, { recursive:true, force:true }));
async function finished(runId) {
  for (let i = 0; i < 150; i++) {
    const report = engine.report(runId);
    if (report.status !== "running" && !report.nodes.some(n => n.status === "running")) return report;
    await engine.wait(runId, report.revision, 100);
  }
  throw new Error("Pipeline did not settle");
}

test("reject invalid DAGs, unsafe paths and non-idempotent retries", () => {
  assert.throws(() => validatePipeline(spec([node("a", "", {dependsOn:["missing"]})])), /Unknown dependency/);
  assert.throws(() => validatePipeline(spec([node("a", "", {dependsOn:["b"]}),node("b", "", {dependsOn:["a"]})])), /cycle/);
  assert.throws(() => validatePipeline(spec([node("a"),node("a")])), /Duplicate/);
  assert.throws(() => validatePipeline(spec([node("a", "", {retries:1})])), /idempotent/);
  assert.throws(() => validatePipeline({...spec([node("a")]),cwd:".."}), /escapes/);
  assert.throws(() => validatePipeline(spec([node("a", "", {expect:{artifacts:["../escape"]}})])), /escapes/);
});

test("parallel branches join after both complete; persisted logs and artifacts", async () => {
  const run = engine.start(spec([
    node("a", "setTimeout(()=>{require('fs').writeFileSync('a.txt','a');console.log('中文完成')},350)", {expect:{stdoutIncludes:"中文完成",artifacts:["a.txt"]}}),
    node("b", "setTimeout(()=>console.log('b'),350)"),
    node("join", "console.log('joined')", {dependsOn:["a","b"]})
  ]));
  const result = await finished(run.runId);
  assert.equal(result.status,"succeeded");
  const [a,b,join] = result.nodes;
  assert.ok(Date.parse(b.startedAt) < Date.parse(a.endedAt));
  assert.ok(Date.parse(join.startedAt) >= Date.parse(a.endedAt));
  assert.ok(Date.parse(join.startedAt) >= Date.parse(b.endedAt));
  assert.match(engine.logs(run.runId,"a").text,/中文完成/);
  assert.equal(a.result.artifacts[0].bytes,1);
});

test("resource locks serialize different pipelines and normalize Windows case", async () => {
  const first = engine.start(spec([node("a","setTimeout(()=>console.log('a'),350)",{locks:["device:phone"]})]));
  const second = engine.start(spec([node("b","console.log('b')",{locks:[process.platform === "win32" ? "DEVICE:PHONE" : "device:phone"]})]));
  assert.equal(second.nodes[0].status,"pending");
  const a = await finished(first.runId), b = await finished(second.runId);
  assert.ok(Date.parse(b.nodes[0].startedAt) >= Date.parse(a.nodes[0].endedAt));
});

test("native exit failures skip dependents, preserve independent work and run cleanup", async () => {
  const run = engine.start(spec([
    node("bad","process.exit(7)"),
    node("blocked","console.log('must not run')",{dependsOn:["bad"]}),
    node("independent"),
    node("cleanup","console.log('clean')",{dependsOn:["bad","blocked"],when:"always"})
  ]));
  const result = await finished(run.runId);
  assert.equal(result.status,"failed");
  assert.equal(result.nodes[0].result.exitCode,7);
  assert.equal(result.nodes[1].status,"skipped");
  assert.equal(result.nodes[2].status,"succeeded");
  assert.equal(result.nodes[3].status,"succeeded");
});

test("zero exit is insufficient when output or artifact verification fails", async () => {
  const run = engine.start(spec([
    node("text", "console.log('wrong')", {expect:{stdoutIncludes:"required-marker"}}),
    node("artifact", "console.log('ok')", {expect:{artifacts:["missing-output.apk"]}}),
    node("compensate", "console.log('compensated')", {dependsOn:["artifact"],when:"failure"})
  ]));
  const result = await finished(run.runId);
  assert.equal(result.status,"failed");
  assert.equal(result.nodes[0].result.exitCode,0);
  assert.match(result.nodes[0].result.error,/Expected text/);
  assert.match(result.nodes[1].result.error,/Expected artifact/);
  assert.equal(result.nodes[2].status,"succeeded");
});

test("unknown verification variables fail the node without crashing the service", async () => {
  const run = engine.start(spec([node("bad", "console.log('ok')", {expect:{stdoutIncludes:"${missing.output}"}})]));
  const result = await finished(run.runId);
  assert.equal(result.status, "failed");
  assert.match(result.nodes[0].result.error, /Unknown pipeline variable/);
  const next = await finished(engine.start(spec([node("healthy")])).runId);
  assert.equal(next.status, "succeeded");
});

test("retryUntil overall deadline also bounds a single command", async () => {
  const started = Date.now();
  const run = engine.start(spec([node("slow", "setTimeout(()=>console.log('done'),10000)", {
    idempotent:true, timeoutMs:15000,
    retryUntil:{timeoutMs:1000,intervalMs:100,maxAttempts:2,condition:{exitCode:0}}
  })]));
  const result = await finished(run.runId);
  assert.equal(result.status, "failed");
  assert.equal(result.nodes[0].result.timedOut, true);
  assert.ok(Date.now() - started < 7000);
});

test("retry idempotent work; submission deduplication rejects changed graphs", async () => {
  const graph = spec([node("retry","const f=require('fs');const p='retry.txt';const n=f.existsSync(p)?Number(f.readFileSync(p)):0;f.writeFileSync(p,String(n+1));process.exit(n===0?1:0)",{retries:1,idempotent:true,retryDelayMs:10})]);
  const run = engine.start(graph,"submission-1");
  assert.equal(engine.start(graph,"submission-1").runId,run.runId);
  assert.throws(()=>engine.start(spec([node("changed")]),"submission-1"),/different task graph/);
  const result = await finished(run.runId);
  assert.equal(result.status,"succeeded");
  assert.equal(result.nodes[0].attempts,2);
});

test("resume failed branch without redoing successful independent work", async () => {
  const run = engine.start(spec([
    node("gate","process.exit(require('fs').existsSync('resume-marker')?0:1)"),
    node("independent","console.log('done')"),
    node("after","console.log('after')",{dependsOn:["gate"]})
  ]));
  assert.equal((await finished(run.runId)).status,"failed");
  fs.writeFileSync(path.join(root,"resume-marker"),"");
  engine.resume(run.runId);
  const result = await finished(run.runId);
  assert.equal(result.status,"succeeded");
  assert.equal(result.nodes[0].attempts,2);
  assert.equal(result.nodes[1].attempts,1);
  assert.equal(result.nodes[2].attempts,1);
});

test("timeouts kill trees; bounded log file preserves final output tail", async () => {
  const run = engine.start(spec([
    node("timeout","setTimeout(()=>{},10000)",{timeoutMs:250}),
    node("noisy","process.stdout.write('x'.repeat(20000)+'THE_END')",{maxLogBytes:1024})
  ]));
  const result = await finished(run.runId);
  assert.equal(result.nodes[0].result.timedOut,true);
  assert.equal(result.nodes[1].result.logTruncated,true);
  assert.ok(result.nodes[1].result.stdout.endsWith("THE_END"));
  assert.ok(fs.statSync(path.join(engine.directory,result.nodes[1].logFile)).size <= 1024);
});

test("cancellation does not launch queued dependents and releases resource locks", async () => {
  const run = engine.start(spec([node("long","setTimeout(()=>{},10000)",{locks:["cancel-lock"]}),node("after","",{dependsOn:["long"]})]));
  await engine.cancel(run.runId);
  const result = await finished(run.runId);
  assert.equal(result.status,"cancelled");
  assert.equal(result.nodes[1].attempts,0);
  const next = engine.start(spec([node("next","console.log('next')",{locks:["cancel-lock"]})]));
  assert.equal((await finished(next.runId)).status,"succeeded");
});

test("persistent recovery requires reconciliation; store excludes concurrent owners", async () => {
  assert.throws(()=>new PipelineEngine(engine.directory),/owned by process/);
  const run = engine.start(spec([node("long","setTimeout(()=>{},10000)",{locks:["recovery"]})]));
  const directory = engine.directory;
  await engine.close();
  engine = new PipelineEngine(directory);
  assert.equal(engine.report(run.runId).status,"interrupted");
  assert.throws(()=>engine.resume(run.runId),/Reconcile/);
  await engine.cancel(run.runId,true);
  assert.equal(engine.report(run.runId).status,"cancelled");
});

test("hard process termination preserves checkpoints and quarantines uncertain locks", async () => {
  const directory = path.join(root,"crash-store");
  const moduleUrl = new URL("../src/dev-pipeline.js",import.meta.url).href;
  const code = `import {PipelineEngine} from ${JSON.stringify(moduleUrl)};
    const engine = new PipelineEngine(process.argv[1]);
    const report = engine.start(${JSON.stringify(spec([node("crash","setTimeout(()=>{},10000)",{locks:["crash-device"]})]))});
    const timer=setInterval(()=>{if(engine.report(report.runId).nodes[0].pid){clearInterval(timer);console.log(report.runId);}},20);`;
  const child = spawn(process.execPath,["--input-type=module","-e",code,directory],{windowsHide:true,env:{...process.env,RACCOON_ROOT:root},stdio:["ignore","pipe","ignore"],detached:process.platform !== "win32"});
  const closed = new Promise(resolve=>child.once("close",resolve));
  const runId = await new Promise((resolve,reject)=>{
    const timer=setTimeout(()=>reject(new Error("Crash fixture did not checkpoint")),5000);
    child.stdout.once("data",data=>{clearTimeout(timer);resolve(data.toString().trim());});
    child.once("error",reject);
  });
  if(process.platform === "win32") await promisify(execFile)("taskkill.exe",["/PID",String(child.pid),"/T","/F"],{windowsHide:true});
  else process.kill(-child.pid,"SIGKILL");
  await closed;
  const recovered = new PipelineEngine(directory);
  try {
    assert.equal(recovered.report(runId).status,"interrupted");
    assert.equal(recovered.report(runId).nodes[0].status,"interrupted");
    assert.throws(()=>recovered.resume(runId),/Reconcile/);
    const queued = recovered.start(spec([node("blocked","console.log('ok')",{locks:["crash-device"]})]));
    assert.equal(queued.nodes[0].status,"pending");
    await recovered.cancel(queued.runId);
    await recovered.cancel(runId,true);
  } finally { await recovered.close(); }
});

test("cancellation while waiting for resource governor settles promptly", async () => {
  configureGovernor({enabled:true,maxConcurrentHeavyOps:1,minFreeMemoryBytes:0,maxSystemCpuPercent:100});
  let release;
  const held = withHeavyPermit(()=>new Promise(resolve=>{release=resolve;}));
  await new Promise(resolve=>setImmediate(resolve));
  try {
    const run = engine.start(spec([node("queued")]));
    await engine.cancel(run.runId);
    assert.equal((await finished(run.runId)).nodes[0].status,"cancelled");
  } finally { release(); await held; configureGovernor({enabled:false}); }
});
