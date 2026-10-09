import fs from 'node:fs';
import crypto from 'node:crypto';

// Tokenize before removing comments; quoted comment markers are literal data.
export function sanitizeSql(sql) {
  const raw = String(sql ?? '');
  let out = '';
  let i = 0;
  while (i < raw.length) {
    const c = raw[i];
    const next = raw[i + 1];
    if (c === '-' && next === '-') {
      i += 2;
      while (i < raw.length && raw[i] !== '\n' && raw[i] !== '\r') i++;
      out += ' ';
      continue;
    }
    if (c === '/' && next === '*') {
      const end = raw.indexOf('*/', i + 2);
      i = end < 0 ? raw.length : end + 2;
      out += ' ';
      continue;
    }
    if (c === "'" || c === '"' || c === '`' || c === '[') {
      const closing = c === '[' ? ']' : c;
      i++;
      while (i < raw.length) {
        if (raw[i] === closing) {
          i++;
          if (raw[i] === closing) {
            i++;
            continue;
          }
          break;
        }
        i++;
      }
      out += '?';
      continue;
    }
    if (!/[A-Za-z0-9_$]/.test(raw[i - 1] ?? '')) {
      const numeric = /^(?:0[xX][0-9a-fA-F]+|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)/.exec(raw.slice(i));
      if (numeric) {
        out += '?';
        i += numeric[0].length;
        continue;
      }
    }
    out += c;
    i++;
  }
  return out.replace(/\s+/g, ' ').trim().slice(0, 380);
}

export function summarizeInsights(data) {
  if (!Array.isArray(data)) throw new Error('Expected a D1 insights array');
  return data
    .map((item) => {
      const pattern = sanitizeSql(item.query);
      return {
        fingerprint: crypto.createHash('sha256').update(pattern).digest('hex').slice(0, 12),
        pattern,
        rowsRead: Number(item.totalRowsRead || 0),
        rowsWritten: Number(item.totalRowsWritten || 0),
        calls: Number(item.numberOfTimesRun || 0),
        avgRowsRead: Number(item.avgRowsRead || 0),
      };
    })
    .sort((a, b) => b.rowsRead - a.rowsRead);
}

if (process.argv[1]?.endsWith('d1-insights-summary.mjs')) {
  const data = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const records = summarizeInsights(data);
  console.log('D1 SQL HOTSPOTS (literal values redacted)');
  console.log('Rank | Fingerprint | Calls | Rows read | Avg read | Rows written | SQL shape');
  records.forEach((record, index) => {
    console.log([
      index + 1,
      record.fingerprint,
      record.calls,
      record.rowsRead,
      record.avgRowsRead,
      record.rowsWritten,
      record.pattern,
    ].join(' | '));
  });
}
