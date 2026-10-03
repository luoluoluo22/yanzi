import { DatabaseSync } from 'node:sqlite';
const [path, marker] = process.argv.slice(2);
let database;
try {
  database = new DatabaseSync(path, { readOnly: true });
  const row = database.prepare('select count(*) as matches from chat_messages where content = ?').get(marker);
  process.stdout.write(row.matches > 0 ? 'true' : 'false');
} catch { process.stdout.write('false'); }
finally { database?.close(); }
