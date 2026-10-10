import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';
import { config } from './config.js';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);

const bom = Buffer.from([0xef, 0xbb, 0xbf]);
export const isProtectedSource = target => path.basename(target).toLowerCase() === 'main.cs';

export function assertOrdinaryWriteAllowed(target) {
  if (isProtectedSource(target)) throw new Error('main.cs requires fs_transaction with a compile validation command; ordinary writes, patches and byte chunks are disabled for this source file.');
}

export function decodeSource(bytes) {
  try { return new TextDecoder('utf-8', {fatal:true}).decode(bytes); }
  catch { throw new Error('main.cs is not valid UTF-8. Original bytes were preserved; repair encoding explicitly before editing.'); }
}

export function sourceMetrics(bytes) {
  const content = decodeSource(bytes);
  return {utf8:true, bom:bytes.subarray(0,3).equals(bom), bytes:bytes.length,
    chineseCharacters:(content.match(/\p{Script=Han}/gu) || []).length,
    doubleQuotes:(content.match(/"/g) || []).length,
    singleQuotes:(content.match(/'/g) || []).length,
    sha256:createHash('sha256').update(bytes).digest('hex')};
}

export function encodeSource(content) {
  const text = content.replace(/^\uFEFF/, '');
  if (/[\u0000\uFFFD]/u.test(text) || !text.isWellFormed()) {
    throw new Error('Source integrity scan rejected NUL, replacement characters or unpaired Unicode surrogates.');
  }
  const bytes = Buffer.concat([bom, Buffer.from(text, 'utf8')]);
  if (decodeSource(bytes) !== text) throw new Error('Source UTF-8 round-trip validation failed.');
  return bytes;
}

export async function backupSource(target, original) {
  const directory = path.join(config.sourceBackupDir, randomUUID());
  await fs.mkdir(directory, {recursive:true});
  const backup = path.join(directory, 'original.bin');
  await fs.writeFile(backup, original, {flag:'wx'});
  await fs.writeFile(path.join(directory, 'manifest.json'), JSON.stringify({target, createdAt:new Date().toISOString(), sha256:createHash('sha256').update(original).digest('hex'), bytes:original.length}, null, 2));
  return backup;
}

export async function verifySourceReadBack(target, expected) {
  if (process.platform === 'win32') {
    const literal = target.replace(/'/g, "''");
    const command = `$ErrorActionPreference='Stop'; Get-Content -LiteralPath '${literal}' -Encoding utf8 | Out-Null`;
    await execFileAsync('powershell.exe', ['-NoProfile', '-WindowStyle', 'Hidden', '-EncodedCommand', Buffer.from(command, 'utf16le').toString('base64')], {windowsHide:true, timeout:30000, maxBuffer:1024*1024});
  }
  const actual = await fs.readFile(target);
  if (!actual.equals(expected)) throw new Error('Source read-back bytes differ from the prepared file.');
  return sourceMetrics(actual);
}

// Write the complete file to a sibling first, then replace it. Readers never
// observe a partially truncated source. Keep original bytes for exact rollback.
export async function replaceSourceBytes(target, bytes) {
  const temporary = path.join(path.dirname(target), `.${path.basename(target)}.${randomUUID()}.tmp`);
  try {
    const handle = await fs.open(temporary, 'wx', (await fs.stat(target)).mode);
    try { await handle.writeFile(bytes); await handle.sync(); } finally { await handle.close(); }
    await fs.rename(temporary, target);
    const actual = await fs.readFile(target);
    if (!actual.equals(bytes)) throw new Error('Source read-back bytes differ from the prepared file.');
  } finally { await fs.rm(temporary, {force:true}); }
}
