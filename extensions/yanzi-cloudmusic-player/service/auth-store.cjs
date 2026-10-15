'use strict';
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const SECRET_PATH = path.join(process.env.LOCALAPPDATA || path.join(process.env.USERPROFILE, 'AppData', 'Local'),
  'OpenQuickHost', 'ExtensionStorage', 'yanzi-cloudmusic-player', 'private-auth.dpapi');

const dpapiProgram = [
  'Add-Type -AssemblyName System.Security;',
  '$raw = [Console]::In.ReadToEnd().Trim();',
  "if ($env:YANZI_DPAPI_OP -eq 'protect') {",
  '  $bytes = [Text.Encoding]::UTF8.GetBytes($raw);',
  '  $out = [Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);',
  '  [Console]::Out.Write([Convert]::ToBase64String($out));',
  '} else {',
  '  $bytes = [Convert]::FromBase64String($raw);',
  '  $out = [Security.Cryptography.ProtectedData]::Unprotect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);',
  '  [Console]::Out.Write([Text.Encoding]::UTF8.GetString($out));',
  '}',
].join('\n');

function dpapi(op, value) {
  if (process.platform !== 'win32') throw Error('Windows user-scoped DPAPI is required');
  const result = spawnSync('powershell.exe',
    ['-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(dpapiProgram,'utf16le').toString('base64')],
    { input: value, encoding: 'utf8', timeout: 12000, maxBuffer: 1024 * 1024,
      windowsHide: true, env: {...process.env,YANZI_DPAPI_OP:op } });
  if (result.status !== 0 || result.error) throw Error('Local Windows credential encryption failed');
  return result.stdout.trim();
}

function isolateCookie(raw) {
  // Keep session credentials out of URLs, logs, miniapp localStorage, source control.
  const found = String(raw || '').match(/(?:^|[;\s])MUSIC_U=([^;\s]+)/);
  const csrf = String(raw || '').match(/(?:^|[;\s])__csrf=([^;\s]+)/);
  if (!found) throw Error('QR login did not return the expected account authorization token');
  return 'MUSIC_U=' + found[1] + (csrf ? '; __csrf=' + csrf[1] : '');
}
function save(raw) {
  const token = isolateCookie(raw);
  fs.mkdirSync(path.dirname(SECRET_PATH),{ recursive:true });
  const tmp=SECRET_PATH+'.tmp-'+process.pid;
  fs.writeFileSync(tmp,dpapi('protect',token),{mode:0o600});
  fs.renameSync(tmp,SECRET_PATH);
  return true;
}
function load() {
  try { return isolateCookie(dpapi('unprotect',fs.readFileSync(SECRET_PATH,'utf8'))); }
  catch (_) { return ''; }
}
function clear() { try { fs.unlinkSync(SECRET_PATH); } catch (_) {} }
module.exports = { load, save, clear, isolateCookie };
