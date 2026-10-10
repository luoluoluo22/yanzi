import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import {spawnSync} from 'node:child_process';

process.env.RACCOON_ENABLE_SHELL = '1';
const {applyFileTransaction} = await import('../src/fs-transaction.js');
const {decodeSource, encodeSource, assertOrdinaryWriteAllowed} = await import('../src/source-integrity.js');
const {writeRichFile, editRichBlock} = await import('../src/file-tools.js');
const {Client} = await import('@modelcontextprotocol/sdk/client/index.js');
const {InMemoryTransport} = await import('@modelcontextprotocol/sdk/inMemory.js');
const {createRaccoonServer} = await import('../src/server.js');

test('source protection rejects invalid encodings and unsafe Unicode without guessing', () => {
  assert.throws(()=>decodeSource(Buffer.from([0xff, 0xfe, 0x41, 0x00])), /not valid UTF-8/);
  for (const text of ['bad\uFFFD', 'bad\u0000', 'bad\ud800']) assert.throws(()=>encodeSource(text), /integrity scan/);
  assert.equal(decodeSource(encodeSource('中文')), '中文');
  assert.throws(()=>assertOrdinaryWriteAllowed('MAIN.CS'), /fs_transaction/);
});

test('main.cs ordinary MCP writes cannot bypass the transaction and compile requirement', async () => {
  const directory = await fs.mkdtemp(path.join(process.cwd(), '.raccoon-source-test-'));
  const file = path.join(directory, 'main.cs');
  const bytes = Buffer.from('class Program {}');
  const server = createRaccoonServer(), client = new Client({name:'source-test',version:'1'});
  const [a,b] = InMemoryTransport.createLinkedPair();
  try {
    await fs.writeFile(file, bytes);
    await server.connect(a); await client.connect(b);
    for (const [name, args] of [
      ['fs_write_text',{file,content:'broken'}],
      ['fs_write_chunk',{file,content:'broken',mode:'rewrite'}],
      ['fs_apply_patch_many',{files:[{file,replacements:[{oldText:'Program',newText:'Broken'}]}]}],
      ['fs_write_many',{files:[{file,content:'broken'}]}]
    ]) {
      const r = await client.callTool({name,arguments:args});
      assert.match(r.content[0].text, /fs_transaction/);
      assert.deepEqual(await fs.readFile(file), bytes);
    }
    await assert.rejects(()=>writeRichFile({path:file,content:'bad'}), /fs_transaction/);
    await assert.rejects(()=>editRichBlock({file_path:file,old_string:'Program',new_string:'Broken'}), /fs_transaction/);
    await assert.rejects(()=>applyFileTransaction({files:[{file,replacements:[{oldText:'Program',newText:'Broken'}]}]}), /compile validation/);
  } finally {await client.close(); await server.close(); await fs.rm(directory,{recursive:true,force:true});}
});

test('real C# compilation: BOM, read-back, backup metrics, and exact rollback on syntax failure', {skip:!/^9\.|^[1-9]\d\./m.test(spawnSync('dotnet',['--list-sdks'],{encoding:'utf8',windowsHide:true}).stdout || '')}, async () => {
  const directory = await fs.mkdtemp(path.join(process.cwd(), '.raccoon-source-test-'));
  const file = path.join(directory, 'main.cs');
  const original = Buffer.from('class Program { static void Main() { System.Console.WriteLine("中文原始"); } }\r\n');
  const validate = {command:'dotnet build --nologo -v:q',cwd:directory,timeoutMs:60000,maxOutputBytes:1024*1024};
  try {
    await fs.writeFile(path.join(directory, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>');
    await fs.writeFile(path.join(directory, 'Source.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>');
    await fs.writeFile(file, original);
    const success = await applyFileTransaction({files:[{file,replacements:[{oldText:'中文原始',newText:'中文修改'}]}],validate});
    assert.equal(success.ok, true, JSON.stringify(success));
    const committed = await fs.readFile(file);
    assert.deepEqual(committed.subarray(0,3), Buffer.from([0xef,0xbb,0xbf]));
    assert.equal(success.files[0].integrity.before.bom, false);
    assert.equal(success.files[0].integrity.after.bom, true);
    assert.equal(success.files[0].integrity.readBackVerified, true);
    assert.equal(success.files[0].integrity.after.chineseCharacters, 4);
    assert.deepEqual(await fs.readFile(success.files[0].integrity.backup), original);
    const failed = await applyFileTransaction({files:[{file,replacements:[{oldText:'WriteLine("中文修改")',newText:'WriteLine("中文修改"'}]}],validate});
    assert.equal(failed.ok,false);
    assert.equal(failed.rolledBack,true);
    assert.notEqual(failed.validation.exitCode,0);
    assert.deepEqual(await fs.readFile(file),committed);
    await fs.writeFile(file, original);
    const noBomFailure = await applyFileTransaction({files:[{file,replacements:[{oldText:'WriteLine("中文原始")',newText:'WriteLine("中文原始"'}]}],validate});
    assert.equal(noBomFailure.rolledBack,true);
    assert.deepEqual(await fs.readFile(file),original);
    const invalid = Buffer.from([0xc3,0x28]);
    await fs.writeFile(file,invalid);
    await assert.rejects(()=>applyFileTransaction({files:[{file,replacements:[{oldText:'x',newText:'y'}]}],validate}), /valid UTF-8/);
    assert.deepEqual(await fs.readFile(file),invalid);
  } finally {await fs.rm(directory,{recursive:true,force:true});}
});
