// An isolate-local optimization for legacy clients that poll every five seconds.
// Security boundary: callers must authenticate BEFORE using this cache.
// No Cache API, cross-user response sharing, database writes or persistent storage.
export function createPendingApprovalCache({ttlMs = 15_000, maxUsers = 1_000, clock = Date.now} = {}) {
  const entries = new Map();
  return {
    async load(userId, query) {
      if (!userId || typeof query !== 'function') throw new TypeError('authenticated user and query required');
      const timestamp = clock();
      const cached = entries.get(userId);
      if (cached && cached.expiresAt > timestamp) return cached.promise;
      const promise = Promise.resolve().then(query);
      entries.set(userId, {promise, expiresAt: timestamp + ttlMs});
      if (entries.size > maxUsers) entries.delete(entries.keys().next().value);
      try {
        return await promise;
      } catch (error) {
        if (entries.get(userId)?.promise === promise) entries.delete(userId);
        throw error;
      }
    },
    invalidate(userId) {
      entries.delete(userId);
    }
  };
}
