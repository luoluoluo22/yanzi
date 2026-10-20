// Opt-in API router. Caller must perform Bridge authorization, Host/Origin
// enforcement, and X-Bridge-Request verification BEFORE invoking this router.
export async function handleDeliveryApi({ ledger, path, method, body, reply }) {
  if (path === '/api/delivery-runs') {
    if (method === 'GET') return reply(200, Object.values(ledger.snapshot().runs));
    if (method === 'POST') return reply(201, ledger.createRun(await body()));
    return reply(405, { error: 'method_not_allowed' });
  }
  const match = /^\/api\/delivery-runs\/([a-zA-Z0-9_.:-]{1,100})(?:\/(claim|development|review|integration|retry))?$/.exec(path);
  if (!match) return reply(404, { error: 'delivery_route_not_found' });
  const [, runId, action] = match;
  if (method === 'GET' && !action) {
    const state = ledger.snapshot(runId);
    return reply(state ? 200 : 404, state || { error: 'run_not_found' });
  }
  if (method !== 'POST' || !action) return reply(405, { error: 'method_not_allowed' });
  const input = { ...await body(), runId };
  switch (action) {
    case 'claim': return reply(200, { lease: ledger.claim(input) });
    case 'development': return reply(200, ledger.completeDevelopment(input));
    case 'review': return reply(200, ledger.completeReview(input));
    case 'integration': return reply(200, ledger.completeIntegration(input));
    case 'retry': return reply(200, ledger.retryBlocked(input));
    default: return reply(404, { error: 'delivery_route_not_found' });
  }
}
