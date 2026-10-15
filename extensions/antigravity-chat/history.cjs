'use strict';
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

function getHistory(convId) {
  if (!convId) return [];
  const userProfile = process.env.USERPROFILE || process.env.HOME || os.homedir();
  const searchDirs = [
    path.join(userProfile, '.gemini', 'antigravity-cli', 'brain', convId),
    path.join(userProfile, '.gemini', 'antigravity', 'brain', convId)
  ];

  let transcriptPath = null;
  for (const d of searchDirs) {
    const tp = path.join(d, '.system_generated', 'logs', 'transcript.jsonl');
    if (fs.existsSync(tp)) {
      transcriptPath = tp;
      break;
    }
  }

  if (!transcriptPath) return [];

  const msgs = [];
  try {
    const content = fs.readFileSync(transcriptPath, 'utf8');
    const lines = content.trim().split(/\r?\n/);
    for (const l of lines) {
      if (!l.trim()) continue;
      try {
        const j = JSON.parse(l);
        if (j.type === 'USER_INPUT') {
          let txt = j.content || '';
          const m = txt.match(/<USER_REQUEST>([\s\S]*?)<\/USER_REQUEST>/);
          if (m) txt = m[1].trim();
          if (!txt || txt.startsWith('!status') || txt.startsWith('/')) continue;
          const t = j.created_at ? new Date(j.created_at).getTime() : Date.now();
          msgs.push({
            role: 'self',
            kind: 'text',
            content: txt,
            time: t
          });
        } else if (j.type === 'PLANNER_RESPONSE') {
          let txt = (j.content || '').trim();
          if (!txt) continue;
          const t = j.created_at ? new Date(j.created_at).getTime() : Date.now();
          msgs.push({
            role: 'peer',
            kind: 'text',
            content: txt,
            time: t
          });
        }
      } catch {}
    }
  } catch (err) {
    // ignore
  }

  return msgs;
}

if (require.main === module) {
  const convId = process.argv[2] || '';
  const result = getHistory(convId);
  process.stdout.write(JSON.stringify(result));
}

module.exports = { getHistory };
