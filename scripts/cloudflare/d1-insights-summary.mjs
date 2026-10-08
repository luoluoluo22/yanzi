import fs from 'node:fs';
import crypto from 'node:crypto';

export function sanitizeSql(sql) {
  const raw = String(sql ?? '');
  // No raw SQL literals enter public GitHub Actions logs, including quoted secrets.
  return raw
    .replace(/--[^\r\n]*/g, '')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/'(?:''|[^'])*'/g, '?')
    .replace(/"(?:""|[^"])*"/g, '?')
    .replace(/\b(?:0x[0-9a-fA-F]+|\d+(?:\.\d+)?)\b/g, '?')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 380);
}

export function summarizeInsights(data) {
  if (!Array.isArray(data)) throw new Error('Expected a D1 insights array');
  return data
    .map((item) => ({
      fingerprint: crypto.createHash('sha256').update(String(item.query ?? '')).digest('hex').slice(0, 12),
      pattern: sanitizeSql(item.query),
      rowsRead: Number(item.totalRowsRead || 0),
      rowsWritten: Number(item.totalRowsWritten || 0),
      calls: Number(item.numberOfTimesRun || 0),
      avgRowsRead: Number(item.avgRowsRead || 0),
    }))
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
