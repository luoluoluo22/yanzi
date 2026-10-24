import test from 'node:test';
import assert from 'node:assert/strict';
import { siteAdapters } from '../site-adapters.mjs';
import { buildSiteDetailsLines } from '../site-details.mjs';

const baseUrl = 'http://127.0.0.1:43820';
const expectedSites = ['chatgpt', 'xiaohongshu', 'douyin', 'bilibili'];

test('all website adapters expose a stable action catalog', () => {
  assert.deepEqual(Object.keys(siteAdapters), expectedSites);
  for (const id of expectedSites) {
    const adapter = siteAdapters[id];
    assert.ok(adapter.homeUrl, id + ' has no home URL');
    assert.ok(adapter.allowedHosts.length > 0, id + ' has no host allowlist');
    assert.ok(adapter.actions.length >= 6, id + ' has too few actions');
    for (const action of adapter.actions) {
      assert.ok(action.id && action.label, id + ' has an incomplete action');
    }
  }
  for (const id of ['xiaohongshu', 'douyin', 'bilibili']) {
    assert.ok(siteAdapters[id].actions.some(action => action.id === 'extract_details'));
    assert.ok(siteAdapters[id].detailSelectors);
  }
  assert.equal(siteAdapters.chatgpt.actions.find(action => action.id === 'send_prompt').requiresConfirm, true);
});

test('generated extraction code parses for all content platforms', () => {
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
  for (const id of ['xiaohongshu', 'douyin', 'bilibili']) {
    const lines = buildSiteDetailsLines(siteAdapters[id], id);
    assert.equal(lines.length, 2);
    assert.doesNotThrow(() => new AsyncFunction('page', lines.join('\n')));
    assert.ok(lines.join('\n').includes('contentAccessible'));
  }
});

test('live API health, adapter discovery, OpenAPI, and safety guards', async () => {
  const healthResponse = await fetch(baseUrl + '/health');
  assert.equal(healthResponse.status, 200);
  const health = await healthResponse.json();
  assert.equal(health.version, '0.4.4');
  assert.equal(health.status, 'online');

  const configResponse = await fetch(baseUrl + '/api/ui-config');
  assert.equal(configResponse.status, 200);
  const config = await configResponse.json();
  const headers = { Authorization: 'Bearer ' + config.token, 'Content-Type': 'application/json' };

  const sitesResponse = await fetch(baseUrl + '/api/sites', { headers });
  assert.equal(sitesResponse.status, 200);
  const catalog = await sitesResponse.json();
  assert.equal(catalog.count, 4);
  for (const id of expectedSites) assert.ok(catalog.sites.some(site => site.id === id));
  for (const id of ['xiaohongshu', 'douyin', 'bilibili']) {
    const site = catalog.sites.find(item => item.id === id);
    assert.ok(site.actions.some(action => action.id === 'extract_details'));
  }

  const openApiResponse = await fetch(baseUrl + '/api/openapi.json', { headers });
  assert.equal(openApiResponse.status, 200);
  const openApi = await openApiResponse.json();
  assert.ok(openApi.paths['/api/sites']);
  assert.ok(openApi.paths['/api/sites/{site}/actions']);

  const unauthorized = await fetch(baseUrl + '/api/sites');
  assert.equal(unauthorized.status, 401);

  const unknownSite = await fetch(baseUrl + '/api/sites/not-a-site/actions', {
    method: 'POST', headers, body: JSON.stringify({ action: 'open' })
  });
  assert.equal(unknownSite.status, 404);

  const sendWithoutConfirmation = await fetch(baseUrl + '/api/sites/chatgpt/actions', {
    method: 'POST', headers, body: JSON.stringify({ action: 'send_prompt', text: 'contract test' })
  });
  assert.equal(sendWithoutConfirmation.status, 409);

  const searchWithoutQuery = await fetch(baseUrl + '/api/sites/bilibili/actions', {
    method: 'POST', headers, body: JSON.stringify({ action: 'search' })
  });
  assert.equal(searchWithoutQuery.status, 400);
});