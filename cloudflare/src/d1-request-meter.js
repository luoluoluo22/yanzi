// Request-scoped, read-only D1 telemetry. Never stores SQL, bindings or row contents.
export function meterD1(db) {
  const usage = { queries: 0, rows_read: 0, rows_written: 0, unmetered_queries: 0 };
  const originals = new WeakMap();
  function collect(result) {
    const meta = result?.meta;
    if (!meta || !Number.isFinite(meta.rows_read) || !Number.isFinite(meta.rows_written)) {
      usage.unmetered_queries++;
      return;
    }
    usage.rows_read += Math.max(0, meta.rows_read);
    usage.rows_written += Math.max(0, meta.rows_written);
  }
  function statement(raw) {
    const proxy = new Proxy(raw, { get(target, key) {
      if (key === 'bind') return (...args) => statement(target.bind(...args));
      if (['all', 'run', 'first', 'raw'].includes(key)) return async (...args) => {
        usage.queries++;
        const result = await target[key](...args);
        collect(result);
        return result;
      };
      const value = target[key];
      return typeof value === 'function' ? value.bind(target) : value;
    }});
    originals.set(proxy, raw);
    return proxy;
  }
  const wrapped = new Proxy(db, { get(target, key) {
    if (key === 'prepare') return (...args) => statement(target.prepare(...args));
    if (key === 'batch') return async (statements) => {
      usage.queries += statements.length;
      const results = await target.batch(statements.map(s => originals.get(s) || s));
      for (const result of results) collect(result);
      return results;
    };
    const value = target[key];
    return typeof value === 'function' ? value.bind(target) : value;
  }});
  return { db: wrapped, usage };
}
