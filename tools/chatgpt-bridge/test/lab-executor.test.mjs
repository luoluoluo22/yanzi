import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {copyFileSync,mkdirSync,mkdtempSync,readFileSync,rmSync,writeFileSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {applyProposal} from '../lab-patch-executor.mjs';
const root=resolve(import.meta.dirname,'..','..','..','.tmp','yanzi-iteration-lab');
const sha=s=>createHash('sha256').update(s).digest('hex').toUpperCase();
function sample(t){
  mkdirSync(root,{recursive:true});
  const dir=mkdtempSync(join(root,'unit-'));
  copyFileSync(join(root,'title.mjs'),join(dir,'title.mjs'));
  writeFileSync(join(dir,'title.mjs'),'export function normalizeTitle(value) { return value; }\n');
  copyFileSync(join(root,'title.test.mjs'),join(dir,'title.test.mjs'));
  t.after(()=>rmSync(dir,{recursive:true,force:true}));
  const original=readFileSync(join(dir,'title.mjs'));
  return {dir,original,expectedSha256:sha(original)};
}
test('invalid source file path, global ambient and stale hash are rejected',t=>{
  const s=sample(t);
  const valid='export function normalizeTitle(value) { return typeof value === "string" ? value.trim().replace(/\\s+/g, " ") : ""; }';
  assert.throws(()=>applyProposal({task:'../../src/core',expectedSha256:s.expectedSha256,source:valid},{sandbox:s.dir}),/task_not_allowlisted/);
  assert.throws(()=>applyProposal({task:'title',expectedSha256:'A'.repeat(64),source:valid},{sandbox:s.dir}),/source_version_conflict/);
  assert.throws(()=>applyProposal({task:'title',expectedSha256:s.expectedSha256,source:'export function normalizeTitle(value) { process.exit(0); }'},{sandbox:s.dir}),/disallowed_capability/);
  assert.throws(()=>applyProposal({task:'title',expectedSha256:s.expectedSha256,source:valid},{sandbox:resolve(root,'..','..')}),/sandbox_outside/);
  assert.equal(sha(readFileSync(join(s.dir,'title.mjs'))),s.expectedSha256);
});
test('bad patch genuinely fails pinned tests and rolls back',t=>{
  const s=sample(t);
  const result=applyProposal({
    task:'title',expectedSha256:s.expectedSha256,
    source:'export function normalizeTitle(value) { return "WRONG"; }'
  },{sandbox:s.dir,log:false});
  assert.equal(result.status,'failed_rolled_back');
  assert.equal(result.rollback,true);
  assert.equal(result.testPassed,false);
  assert.equal(sha(readFileSync(join(s.dir,'title.mjs'))),s.expectedSha256);
});
test('good patch changes isolated code and passes immutable tests',t=>{
  const s=sample(t);
  const testHash=sha(readFileSync(join(s.dir,'title.test.mjs')));
  const result=applyProposal({
    task:'title',expectedSha256:s.expectedSha256,
    source:'export function normalizeTitle(value) { return typeof value === "string" ? value.trim().replace(/\\s+/g, " ") : ""; }'
  },{sandbox:s.dir,log:false});
  assert.equal(result.status,'passed');
  assert.equal(result.testPassed,true);
  assert.equal(result.testsUnchanged,true);
  assert.notEqual(sha(readFileSync(join(s.dir,'title.mjs'))),s.expectedSha256);
  assert.equal(sha(readFileSync(join(s.dir,'title.test.mjs'))),testHash);
});
