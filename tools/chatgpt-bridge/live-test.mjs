// Explicit live regression run. Sends test prompts to the user's logged-in ChatGPT page.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { JSDOM } from 'jsdom';
const origin = 'http://127.0.0.1:53921';
const token = readFileSync(join(process.env.LOCALAPPDATA, 'OpenQuickHost/ExtensionStorage/chatgpt-bridge/api-token.txt'), 'utf8').trim();
const output = process.env.YANZI_TEST_OUTPUT || join(process.cwd(), '.artifacts/chatgpt-structured-test');
mkdirSync(output, { recursive: true });
const cases = [
  { name: 'json-plain', kind: 'json', prompt: 'Return only raw valid JSON, no Markdown fences or commentary. Use exactly this data: {"name":"燕子测试","enabled":true,"empty":null,"numbers":[0,-3,1.25],"nested":{"quote":"He said \\"hello\\"","path":"C:\\\\tests\\\\demo","newline":"first\\nsecond","emoji":"🦊"},"end":"JSON_END_739"}. Ensure JSON syntax is valid and preserve the intended escaped string values.', marker: 'JSON_END_739' },
  { name: 'json-fenced', kind: 'json', prompt: 'Return exactly one json fenced code block, no commentary, with valid nested JSON containing keys title="中文与Unicode🦊", matrix=[[1,2],[3,4]], records=[{"id":1,"tags":["a","b"]},{"id":2,"tags":[]}], text containing a newline and a quoted word, and end="FENCED_END_741".', marker: 'FENCED_END_741' },
  { name: 'python', kind: 'python', prompt: 'Return one python fenced code block and nothing else. Write a complete valid Python function summarize(records: list[dict]) -> dict with nested for and if statements, four-space indentation, a multiline docstring containing 中文, a dictionary comprehension, a string literal with escaped newline and backslash, and return a dictionary. Add a final comment # PYTHON_END_743. Do not execute the code.', marker: 'PYTHON_END_743' },
  { name: 'multi-code', kind: 'multi', prompt: 'Produce a short response starting MULTI_BEGIN_745 and ending MULTI_END_745, with exactly three distinct fenced blocks in order: python (a nested function), json (a valid object with nested array), javascript (a function using template strings). Put a brief explanatory paragraph before and after each block. Include Chinese text and an empty line inside the Python block. Do not execute code.', marker: 'MULTI_END_745' },
  { name: 'markdown-table', kind: 'markdown', prompt: 'Create a response starting TABLE_BEGIN_747 and ending TABLE_END_747. Include one heading, an ordered list with 3 items and a nested bullet under item 2, a block quote containing 中文, a Markdown table with columns Name, Value, Note and three data rows (alpha, beta, gamma), bold and italic text, a link labeled Example to https://example.com and inline code x = 1. Preserve all these components.', marker: 'TABLE_END_747' },
  { name: 'long-lines', kind: 'long', prompt: 'Return a single text fenced code block containing exactly 80 numbered lines. Line N must be formatted ROW_NNN: 燕子_🦊_VALUE_NNN where NNN is a three-digit number 001 through 080. After the code block write LONG_END_749. Do not abbreviate, summarize, use ellipses or omit any line.', marker: 'LONG_END_749' }
];
const selected = process.argv[2] ? cases.filter(c => process.argv[2].split(',').includes(c.name)) : cases;
const hash = value => createHash('sha256').update(value).digest('hex');
const normalized = value => value.replace(/\r\n/g, '\n').replace(/\n+$/, '');
async function api(path, body) {
  const response = await fetch(origin + path, { method: body ? 'POST' : 'GET', headers: { Authorization: 'Bearer ' + token, 'X-Bridge-Request': '1', 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error);
  return data;
}
async function job(task) {
  const created = await api('/api/jobs', task);
  const deadline = Date.now() + (task.timeoutSeconds || 180) * 1000 + 90000;
  while (Date.now() < deadline) {
    const current = await api('/api/jobs/' + created.id);
    if (!['running', 'queued'].includes(current.status)) return current;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error('Polling deadline exceeded; request may already be sent.');
}
function checkSyntax(kind, source) {
  if (kind === 'json') { JSON.parse(source); return 'valid JSON'; }
  if (kind === 'python') {
    const parsed = spawnSync('python', ['-c', 'import ast,sys; ast.parse(sys.stdin.read()); print("valid Python AST")'], { input: source, encoding: 'utf8', timeout: 10000 });
    if (parsed.status !== 0) throw new Error(parsed.stderr || parsed.error?.message || 'Python parser unavailable');
    return parsed.stdout.trim();
  }
}
const report = [];
for (const testCase of selected) {
  console.log('START ' + testCase.name);
  const record = { name: testCase.name, kind: testCase.kind };
  try {
    const sent = await job({ action: 'chatgpt_send', newChat: true, prompt: testCase.prompt, timeoutSeconds: 180 });
    record.jobId = sent.id;
    if (sent.status !== 'success') throw new Error(sent.message || sent.status);
    record.visibility = sent.data.visibility;
    record.tabId = sent.data.tabId;
    // Capture the page again after the API declared completion; this detects late missing text.
    await new Promise(resolve => setTimeout(resolve, 4000));
    const read = await job({ action: 'chatgpt_messages', tabId: sent.data.tabId, includePageSnapshot: true, timeoutSeconds: 30 });
    if (read.status !== 'success') throw new Error(read.message || read.status);
    const message = read.data.messages.filter(m => m.role === 'assistant').at(-1);
    const snapshot = read.data.pageSnapshots.at(-1);
    if (!snapshot) throw new Error('Page snapshot missing');
    writeFileSync(join(output, testCase.name + '.json'), JSON.stringify({ sent, read }, null, 2));
    writeFileSync(join(output, testCase.name + '.html'), snapshot.html);
    record.textLength = sent.data.text.length;
    record.responseHash = hash(sent.data.text);
    record.rereadHash = hash(message.text);
    record.stableAfterReturn = sent.data.text === message.text;
    record.markerPresent = sent.data.text.includes(testCase.marker);
    record.codeBlockCount = snapshot.codeBlocks.length;
    record.codeSourcesIncluded = snapshot.codeBlocks.map(block => sent.data.text.includes(normalized(block.text)));
    record.structuredCodeMatches = sent.data.codeBlocks?.length === snapshot.codeBlocks.length && sent.data.codeBlocks?.every((block, i) => block.text === snapshot.codeBlocks[i].text);
    const page = new JSDOM(snapshot.html).window.document;
    const content = page.querySelector('[data-markdown-text-style], .markdown') || page.body;
    const visibleProse = Array.from(content.querySelectorAll('p, h1, h2, h3, h5, h6, th, td')).filter(el => !el.closest('[data-markdown-copy="exclude"], .sr-only, .turn-action-controls, button'));
    record.missingPageText = visibleProse.map(el => el.textContent.trim()).filter(text => text && !sent.data.text.includes(text));
    record.linksPreserved = Array.from(content.querySelectorAll('a[href]')).filter(el => !el.closest('[data-markdown-copy="exclude"], .turn-action-controls')).every(el => sent.data.markdown?.includes(el.getAttribute('href')));
    const source = snapshot.codeBlocks[0]?.text || sent.data.text;
    if (['json', 'python'].includes(testCase.kind)) {
      try { record.pageSyntax = checkSyntax(testCase.kind, source); }
      catch (error) { record.pageSyntaxError = error.message; }
      try { record.returnedSyntax = checkSyntax(testCase.kind, sent.data.text); }
      catch (error) { record.returnedSyntaxError = error.message; }
      if (testCase.kind === 'json') record.jsonParsed = sent.data.json != null;
    }
    if (testCase.kind === 'long') {
      record.missingRows = Array.from({ length: 80 }, (_, i) => String(i + 1).padStart(3, '0')).filter(n => !sent.data.text.includes(`ROW_${n}: 燕子_🦊_VALUE_${n}`));
    }
    record.ok = record.stableAfterReturn && record.markerPresent && record.codeSourcesIncluded.every(Boolean) && record.structuredCodeMatches && !record.missingRows?.length && !record.missingPageText.length && record.linksPreserved;
    if (testCase.kind === 'multi') record.ok &&= record.codeBlockCount === 3;
    if (testCase.name !== 'json-plain' && ['json', 'python'].includes(testCase.kind)) record.ok &&= !!record.pageSyntax && !!record.returnedSyntax && record.structuredCodeMatches;
    if (testCase.name === 'json-plain') record.warning = record.pageSyntaxError ? '网页普通段落中的 JSON 本身不能解析；请要求 JSON 代码块，API 不猜测修补转义' : null;
  } catch (error) { record.ok = false; record.error = error.message; }
  report.push(record);
  writeFileSync(join(output, 'report.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(record));
}
console.log('REPORT ' + join(output, 'report.json'));
process.exitCode = report.every(r => r.ok) ? 0 : 1;
