import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Browser action popup has a content-sized viewport. Deriving its CSS width
// from 100vw creates a circular constraint and can collapse the main column.
// This regression test protects the office-verified 760 x 600 layout.
const css = readFileSync(
  new URL('../../../browser-extension/popup/popup.css', import.meta.url),
  'utf8'
);
const html = readFileSync(
  new URL('../../../browser-extension/popup/popup.html', import.meta.url),
  'utf8'
);

test('browser helper popup has explicit fixed action dimensions', () => {
  const redesign = css.slice(css.indexOf('/* Browser helper redesign:'));
  assert.ok(redesign.startsWith('/* Browser helper redesign:'), 'redesign styles must be present');
  const dimensions = redesign.match(/(?:^|\n)body\s*\{([^}]+)\}/);
  assert.ok(dimensions, 'redesign body dimensions are defined');
  assert.match(dimensions[1], /\bwidth:\s*760px\s*;/);
  assert.match(dimensions[1], /\bheight:\s*600px\s*;/);
  assert.doesNotMatch(redesign, /body\s*\{[^}]*\bwidth:\s*(?:100vw|min\([^;]*100vw)/);
  assert.doesNotMatch(redesign, /body\s*\{[^}]*\bheight:\s*(?:100vh|min\([^;]*100vh)/);
});

test('sidebar and main workspace have independently constrained layout', () => {
  assert.match(css, /\.app-workspace\s*\{[^}]*grid-template-columns:\s*166px\s+minmax\(0,\s*1fr\)/);
  assert.match(css, /\.app-main\s*\{[^}]*min-width:\s*0/);
  assert.match(html, /class="app-workspace"/);
  assert.match(html, /class="app-sidebar"/);
  assert.match(html, /class="app-main"/);
});
