// Independently compare saved browser HTML/code source with returned API fields.
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { JSDOM } from 'jsdom';
import { Script } from 'node:vm';
const extractorSource = readFileSync(new URL('../../browser-extension/chatgpt-content.js', import.meta.url), 'utf8');
const directory = resolve(process.argv[2]);
const sha = text => createHash('sha256').update(text).digest('hex');
const results = [];
for (const file of readdirSync(directory).filter(name => name.endsWith('.json') && !['report.json', 'comparison.json'].includes(name))) {
  const saved = JSON.parse(readFileSync(join(directory, file), 'utf8'));
  if (!saved.sent || !saved.read) continue;
  const reply = saved.sent.data, snapshot = saved.read.data.pageSnapshots.at(-1);
  const replay = new JSDOM('<main>' + snapshot.html + '</main>', { url: 'https://chatgpt.com/c/test', runScripts: 'outside-only' });
  const document = replay.window.document;
  Object.defineProperty(replay.window.HTMLElement.prototype, 'innerText', { get() { return this.textContent; } });
  const listeners = [];
  replay.window.chrome = { runtime: { onMessage: { addListener(fn) { listeners.push(fn); } } } };
  replay.window.eval(extractorSource);
  const replayResult = await new Promise(resolve => listeners.forEach(fn => fn({ type: 'yanzi_chatgpt_task', task: { action: 'chatgpt_messages' } }, {}, resolve)));
  const replayMessage = replayResult.data.messages.at(-1);
  const currentExtractorMatches = ['text', 'markdown', 'codeBlocks', 'json', 'jsonError'].every(key => JSON.stringify(replayMessage[key]) === JSON.stringify(reply[key]));
  const root = document.querySelector('[data-markdown-text-style], .markdown') || document.body;
  const codeNodes = Array.from(root.querySelectorAll('[data-markdown-copy="code-block"] code, pre code'));
  const blocks = codeNodes.map((code, index) => {
    const raw = code.textContent, api = reply.codeBlocks?.[index];
    const result = { index, language: api?.language, bytes: Buffer.byteLength(raw), pageHash: sha(raw), apiHash: sha(api?.text || ''), exactMatch: api?.text === raw, includedInText: reply.text.includes(raw.replace(/\n+$/, '')) };
    if (api?.language === 'json') { try { result.validJSON = JSON.parse(raw) != null; } catch (error) { result.error = error.message; } }
    if (api?.language === 'python') {
      const syntax = spawnSync('python', ['-c', 'import ast,sys; ast.parse(sys.stdin.read())'], { input: raw, encoding: 'utf8', timeout: 10000 });
      result.validPython = syntax.status === 0; if (!result.validPython) result.error = syntax.stderr || syntax.error?.message;
    }
    if (api?.language === 'javascript') { try { new Script(raw); result.validJavaScript = true; } catch (error) { result.validJavaScript = false; result.error = error.message; } }
    return result;
  });
  const prose = Array.from(root.querySelectorAll('p, h1, h2, h3, h4, h5, h6, th, td')).filter(el => !el.closest('[data-markdown-copy="exclude"], .sr-only, .turn-action-controls, button'));
  const missingText = prose.map(el => el.textContent.trim()).filter(text => text && !reply.text.includes(text));
  const links = Array.from(root.querySelectorAll('a[href]')).filter(el => !el.closest('[data-markdown-copy="exclude"], .turn-action-controls')).map(el => ({ label: el.textContent, href: el.getAttribute('href'), preserved: reply.markdown.includes(el.getAttribute('href')) }));
  const latest = saved.read.data.messages.filter(message => message.role === 'assistant').at(-1);
  const stable = latest.text === reply.text && latest.markdown === reply.markdown;
  const ok = currentExtractorMatches && stable && blocks.length === reply.codeBlocks.length && blocks.every(block => block.exactMatch && block.includedInText && block.validJSON !== false && block.validPython !== false && block.validJavaScript !== false) && missingText.length === 0 && links.every(link => link.preserved);
  const result = { name: file.slice(0, -5), visibility: reply.visibility, currentExtractorMatches, stable, blocks, missingText, links, tableCount: root.querySelectorAll('table').length, listItemCount: root.querySelectorAll('li').length, ok, jsonError: reply.jsonError };
  results.push(result); console.log(JSON.stringify(result));
  replay.window.close();
}
writeFileSync(join(directory, 'comparison.json'), JSON.stringify(results, null, 2));
process.exitCode = results.every(result => result.ok) ? 0 : 1;
