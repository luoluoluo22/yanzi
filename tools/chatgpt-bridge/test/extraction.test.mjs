import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { JSDOM } from 'jsdom';
const source = readFileSync(new URL('../../../browser-extension/chatgpt-content.js', import.meta.url), 'utf8');
async function extract(html) {
  const dom = new JSDOM('<main><div data-chatgpt-search-unit-key="t:assistant"><h4 class="sr-only">ChatGPT 说：</h4><div data-markdown-text-style="assistant-message">' + html + '</div></div></main>', { url: 'https://chatgpt.com/c/test', runScripts: 'outside-only' });
  const win = dom.window;
  Object.defineProperty(win.HTMLElement.prototype, 'innerText', { get() { return this.textContent; } });
  const listeners = [];
  win.chrome = { runtime: { onMessage: { addListener(fn) { listeners.push(fn); } } } };
  win.eval(source);
  const result = await new Promise(resolve => listeners.forEach(fn => fn({ type: 'yanzi_chatgpt_task', task: { action: 'chatgpt_messages' } }, {}, resolve)));
  dom.window.close(); return result.data.messages[0];
}
const escape = text => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
const block = (language, code) => '<div data-markdown-copy="code-block"><div data-markdown-copy="exclude">' + language + '<button>复制代码</button></div><code>' + escape(code) + '</code></div>';
test('JSON block preserves quotes, backslashes, newline escapes, Unicode and parses', async () => {
  const expected = { quote: 'He said "hi"', path: 'C:\\test', newline: 'a\nb', nested: [null, false, { 中文: '🦊' }] };
  const code = JSON.stringify(expected, null, 2);
  const result = await extract(block('JSON', code));
  assert.equal(result.text, code); assert.equal(result.codeBlocks[0].text, code);
  assert.deepEqual(JSON.parse(JSON.stringify(result.json)), expected);
  assert.match(result.markdown, /^```json\n/); assert.equal(result.jsonError, null);
});
test('Python block preserves indentation, blank lines and trailing newline exactly', async () => {
  const code = 'def example():\n    """中文 <tag> & 🦊"""\n\n    for i in range(2):\n        if i:\n            return "a\\nb"\n';
  const result = await extract(block('Python', code));
  assert.equal(result.codeBlocks[0].text, code); assert.equal(result.codeBlocks[0].language, 'python');
  assert.equal(result.text, code.trimEnd()); assert.ok(!result.text.startsWith('Python'));
});
test('multiple blocks retain order, explanations and code containing triple backticks', async () => {
  const result = await extract('<p>before</p>' + block('python', 'print("```")') + '<p>middle</p>' + block('json', '{"a":1}') + '<p>after</p>');
  assert.equal(result.codeBlocks.length, 2); assert.match(result.markdown, /````python\nprint\("```"\)\n````/);
  assert.ok(result.text.indexOf('before') < result.text.indexOf('middle')); assert.ok(result.text.endsWith('after'));
});
test('table, nested lists, headings, links and inline formatting survive Markdown reconstruction', async () => {
  const result = await extract('<h2>Title</h2><ol start="3"><li>first</li><li>second<ul><li>nested</li></ul></li></ol><table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>A|B</td><td>2</td></tr></tbody></table><p><strong>bold</strong> <em>italic</em> <a href="https://example.com">Example</a> <code>x = 1</code></p>');
  assert.match(result.markdown, /## Title/); assert.match(result.markdown, /3\. first/); assert.match(result.markdown, /- nested/);
  assert.match(result.markdown, /A\\\|B/); assert.match(result.markdown, /\[Example\]\(https:\/\/example.com\)/);
  assert.match(result.markdown, /\*\*bold\*\*/); assert.match(result.markdown, /`x = 1`/);
  assert.match(result.text, /Name\tValue/); assert.equal(result.markdownSource, 'reconstructed-from-dom');
});
test('invalid displayed JSON is reported, never silently repaired', async () => {
  const result = await extract('<p>{"quote":"say "hi""}</p>');
  assert.equal(result.json, null); assert.ok(result.jsonError); assert.equal(result.text, '{"quote":"say "hi""}');
});
test('legacy pre/code is supported with exact whitespace', async () => {
  const result = await extract('<pre><code class="language-python">def f():\n\treturn 1\n</code></pre>');
  assert.equal(result.codeBlocks[0].language, 'python'); assert.equal(result.codeBlocks[0].text, 'def f():\n\treturn 1\n');
});
