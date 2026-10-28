// Install or check the unpacked Chromium build in ego(lite), without a store install.
// Run: ego-browser nodejs -e "await import('file:///F:/Desktop/kaifa/yanzi-release-main-1052/scripts/ego-browser-helper-install.mjs')"
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const extensionPath = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'browser-extension');
const manifest = JSON.parse(fs.readFileSync(path.join(extensionPath, 'manifest.json'), 'utf8'));
if (manifest.manifest_version !== 3) throw new Error('Only Chromium Manifest V3 is supported.');
if (!/Yanzi Browser Helper/i.test(manifest.name)) throw new Error('Unexpected extension identity.');

await taskSpace('Yanzi extension development');
let info = {}; try { info = await cdp('Browser.getVersion', {}, null); } catch { /* Ego CDP exposes a restricted subset. */ }
const actualVersion = String(info.product || info.result?.product || '');
const chromium = Number((actualVersion.match(/(?:Chrome|Chromium)\/(\d+)/i) || [])[1]);
const required = Number(manifest.minimum_chrome_version || 0);
if (Number.isFinite(chromium) && chromium > 0 && chromium < required) {
  throw new Error(`EGO_CHROMIUM_TOO_OLD: ${chromium} < ${required}; update ego(lite) first.`);
}

const getInstalled = async () => {
  const response = await cdp('Extensions.getExtensions', {}, null);
  return response.extensions || response.result?.extensions || [];
};
const matches = entries => entries.filter(x => /Yanzi Browser Helper|燕子浏览器助手/i.test(x.name || ''));
const before = matches(await getInstalled());
if (before.length > 1) {
  throw new Error('MULTIPLE_YANZI_EXTENSIONS: remove duplicate builds manually before continuing.');
}

if (before.length === 1) {
  const existing = before[0];
  // Never overwrite, unload or replace a browser extension installed from a store
  // or a different worktree: this can change its ID and orphan local settings.
  const samePath = existing.path && path.resolve(existing.path).toLowerCase() === extensionPath.toLowerCase();
  if (!samePath) {
    console.log('EGO_YANZI_ALREADY_INSTALLED ' + JSON.stringify({
      id: existing.id, version: existing.version, enabled: existing.enabled,
      source: existing.path || 'store-managed', availableSourceVersion: manifest.version,
      note: 'No duplicate installed. Existing extension managed independently.'
    }));
  } else {
    if (!existing.enabled) throw new Error('YANZI_DISABLED: enable the extension in ego settings first.');
    const result = await cdp('Extensions.loadUnpacked', {path:extensionPath}, null);
    const id = result.id || result.result?.id;
    if (id !== existing.id) throw new Error('EGO_EXTENSION_ID_CHANGED; review extensions for duplicates.');
    const updated = matches(await getInstalled()).find(x => x.id === id);
    if (!updated?.enabled || updated.version !== manifest.version) {
      throw new Error('EGO_EXTENSION_VERIFY_FAILED: inspect extension errors in ego.');
    }
    console.log('EGO_YANZI_UPDATED ' + JSON.stringify({id, version:updated.version, chromium, source:extensionPath}));
  }
} else {
  const result = await cdp('Extensions.loadUnpacked', {path:extensionPath}, null);
  const id = result.id || result.result?.id;
  const installed = matches(await getInstalled());
  if (installed.length !== 1 || installed[0].id !== id || !installed[0].enabled ||
      installed[0].version !== manifest.version) {
    throw new Error('EGO_INSTALL_VERIFY_FAILED: inspect ego extension management.');
  }
  console.log('EGO_YANZI_INSTALLED ' + JSON.stringify({id, version:manifest.version, chromium, source:extensionPath}));
}
