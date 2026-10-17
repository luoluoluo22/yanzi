import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const cloud = readFileSync(new URL('../../src/OpenQuickHost/MainWindow.CloudSync.cs', import.meta.url), 'utf8');
const extensions = readFileSync(new URL('../../src/OpenQuickHost/MainWindow.Extensions.cs', import.meta.url), 'utf8');
const search = readFileSync(new URL('../../src/OpenQuickHost/MainWindow.Search.cs', import.meta.url), 'utf8');
const aiChat = readFileSync(new URL('../../src/OpenQuickHost/MainWindow.AiChat.cs', import.meta.url), 'utf8');

test('ordinary account uploads never implicitly restore a deleted extension', () => {
  assert.match(cloud, /QueuePrivateExtensionUpsertToAccount\(string extensionId, bool allowRestore = false\)/);
  assert.match(cloud, /SyncPrivateExtensionToAccountAsync\(command, expectedRevision, allowRestore: allowRestore\)/);
  assert.match(cloud, /await _extensionUploadLock\.WaitAsync\(\);\s*try\s*\{[\s\S]*?LoadPersonalSyncDeletedExtensionIds\(\)\.Contains\(command\.ExtensionId\)/);
  assert.match(search, /QueuePrivateExtensionUpsertToAccount\(updated\.ExtensionId\)/);
  assert.match(extensions, /QueuePrivateExtensionUpsertToAccount\(updated\.ExtensionId\)/);
});

test('explicit restoration is limited to user recycle-bin and chosen local conflict recovery', () => {
  assert.match(extensions, /QueuePrivateExtensionUpsertToAccount\(command\.ExtensionId, allowRestore: true\);\s*QueueBackgroundWebDavSync\("extension-restore-settings"\)/);
  assert.match(cloud, /QueuePrivateExtensionUpsertToAccount\(installed\.ExtensionId, allowRestore: true\)/);
  assert.equal([...extensions.matchAll(/allowRestore:\s*true/g)].length, 1);
  assert.equal([...cloud.matchAll(/allowRestore:\s*true/g)].length, 1);
});

test('all desktop deletion paths retain the durable local deletion index', () => {
  assert.match(aiChat, /WebDavSyncService\.MarkExtensionDeletedLocally\(extIdToDelete, command\.DeclaredVersion\)/);
  assert.match(cloud, /ApplyAccountExtensionDeletions\(accountDeletedIds\)/);
  assert.match(cloud, /WebDavSyncService\.MarkExtensionDeletedLocally\(command\.ExtensionId, command\.DeclaredVersion\)/);
});
