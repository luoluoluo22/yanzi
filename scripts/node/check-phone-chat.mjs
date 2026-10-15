import { DatabaseSync } from 'node:sqlite';
const [path, marker, mode = 'exact', expected = '0'] = process.argv.slice(2);
let database;
try {
  database = new DatabaseSync(path, { readOnly: true });
  const row = database.prepare(mode === 'contains'
    ? 'select count(*) as matches from chat_messages where instr(content, ?) > 0'
    : 'select count(*) as matches from chat_messages where content = ?').get(marker);
  process.stdout.write((Number(expected) > 0 ? row.matches === Number(expected) : row.matches > 0) ? 'true' : 'false');
} catch { process.stdout.write('false'); }
finally { database?.close(); }
