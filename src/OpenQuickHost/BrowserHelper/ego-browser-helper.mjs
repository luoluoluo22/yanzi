// Bundled with Yanzi Windows app. Ego only: no store bypass, no hidden install.
// Browser permission prompts are controlled by Ego; duplicate extensions are refused.
import fs from 'node:fs';
import path from 'node:path';

const options = globalThis.__yanziHelperOptions || {};
const extensionPath = options.path;
const action = options.action || 'update';
if (!extensionPath || !path.isAbsolute(extensionPath)) throw Error('INVALID_EXTENSION_PATH');
if (!['install', 'update'].includes(action)) throw Error('INVALID_ACTION');
const manifest = JSON.parse(fs.readFileSync(path.join(extensionPath, 'manifest.json'), 'utf8'));
if (manifest.manifest_version !== 3 || !/Yanzi Browser Helper/i.test(manifest.name))
  throw Error('INVALID_YANZI_MANIFEST');
await taskSpace('Yanzi extension management');
const getExtensions = async () => {
  const response = await cdp('Extensions.getExtensions', {}, null);
  return response.extensions || response.result?.extensions || [];
};
const match = entries => entries.filter(x => /Yanzi Browser Helper|燕子浏览器助手/i.test(x.name || ''));
const normalize = value => path.resolve(value).toLowerCase().replaceAll('/', '\\');
const compare = (a, b) => {
  const x = String(a || '').split('.').map(Number);
  const y = String(b || '').split('.').map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) - (y[i] || 0);
  }
  return 0;
};
const before = match(await getExtensions());
if (before.length > 1) throw Error('DUPLICATE_EXTENSIONS_PRESENT');
const existing = before[0];
const result = {status:'unknown', version:manifest.version};
if (existing) {
  result.id = existing.id;
  result.version = existing.version;
  if (!existing.enabled) throw Error('EXTENSION_DISABLED');
  if (!existing.path || normalize(existing.path) !== normalize(extensionPath)) {
    // Preserve existing Chrome extension ID and chrome.storage.local; the caller
    // must explicitly migrate a different unpacked/store installation.
    result.status = 'external';
    result.source = existing.path ? 'other-unpacked' : 'store';
  } else if (compare(existing.version, manifest.version) > 0) {
    throw Error('CANDIDATE_WOULD_DOWNGRADE_INSTALLED_VERSION');
  } else if (compare(existing.version, manifest.version) === 0) {
    result.status = 'ready';
  } else {
    const reloaded = await cdp('Extensions.loadUnpacked', {path: extensionPath}, null);
    const id = reloaded.id || reloaded.result?.id;
    const installed = match(await getExtensions()).find(x => x.id === existing.id);
    if (id !== existing.id || !installed?.enabled || installed.version !== manifest.version)
      throw Error('EGO_UPGRADE_VERIFICATION_FAILED');
    result.status = 'updated';
    result.version = installed.version;
  }
} else if (action === 'update') {
  result.status = 'missing';
} else {
  const loaded = await cdp('Extensions.loadUnpacked', {path:extensionPath}, null);
  const id = loaded.id || loaded.result?.id;
  const installed = match(await getExtensions());
  if (installed.length !== 1 || installed[0].id !== id ||
      !installed[0].enabled || installed[0].version !== manifest.version)
    throw Error('EGO_INSTALL_VERIFICATION_FAILED');
  result.status = 'installed';
  result.id = id;
}
console.log('YANZI_EGO_RESULT ' + JSON.stringify(result));
