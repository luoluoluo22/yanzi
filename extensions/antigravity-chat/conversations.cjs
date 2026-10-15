'use strict';
const path = require('node:path');
const os = require('node:os');
const fs = require('node:fs');
const { DatabaseSync } = require('node:sqlite');

function getConversations(limit = 30) {
  const userProfile = process.env.USERPROFILE || process.env.HOME || os.homedir();
  const dbPaths = [
    path.join(userProfile, '.gemini', 'antigravity-cli', 'conversation_summaries.db')
  ];
  const list = [];
  const seen = new Set();

  for (const dbPath of dbPaths) {
    if (!fs.existsSync(dbPath)) continue;
    try {
      const db = new DatabaseSync(dbPath, { readOnly: true });
      const query = db.prepare(`
        SELECT conversation_id, title, preview, last_modified_time, step_count
        FROM conversation_summaries
        WHERE conversation_id IS NOT NULL AND conversation_id != ''
        ORDER BY last_modified_time DESC
        LIMIT ?
      `);
      const rows = query.all(limit);
      for (const r of rows) {
        const id = r.conversation_id;
        if (!id || seen.has(id)) continue;
        seen.add(id);
        let title = (r.title || '').trim();
        if (!title) title = (r.preview || '').trim();
        if (!title) title = '未命名会话';
        if (title.length > 25) title = title.slice(0, 24) + '…';
        let ms = 0;
        try { ms = new Date(r.last_modified_time).getTime(); } catch {}
        if (!ms || isNaN(ms)) ms = Date.now();
        list.push({
          id,
          title,
          preview: (r.preview || '').slice(0, 80),
          updatedAt: r.last_modified_time,
          updatedAtMs: ms,
          stepCount: r.step_count || 0
        });
      }
      db.close();
    } catch (e) {
      // ignore
    }
  }

  list.sort((a, b) => String(b.updatedAt).localeCompare(String(a.updatedAt)));
  return list.slice(0, limit);
}

if (require.main === module) {
  const result = getConversations(30);
  process.stdout.write(JSON.stringify(result));
}

module.exports = { getConversations };
