// Use HTTPS across devices. A loopback URL is accepted only for
// localhost or an already authenticated local SSH/Cloudflare tunnel.
export function cloudWorkerClient(origin, deviceToken, { fetchImpl = fetch } = {}) {
  if (typeof origin !== 'string' || typeof deviceToken !== 'string' ||
      !/^[a-f0-9]{64}$/i.test(deviceToken)) throw new Error('cloud_client_invalid');
  const url = new URL(origin);
  if (url.username || url.password || url.search || url.hash || url.pathname !== '/')
    throw new Error('cloud_client_url_invalid');
  const local = url.protocol === 'http:' && ['127.0.0.1', '[::1]'].includes(url.hostname);
  if (!local && url.protocol !== 'https:')
    throw new Error('cloud_client_requires_tls');
  return {
    async send(runId, action, input) {
      if (!/^[a-zA-Z0-9_.:-]{1,100}$/.test(runId) ||
          !['claim','renew','block','development','review'].includes(action))
        throw new Error('cloud_client_action_invalid');
      const response = await fetchImpl(url.origin + '/v1/runs/' + encodeURIComponent(runId) + '/' + action, {
        method: 'POST', headers: { Authorization: 'Bearer ' + deviceToken,
          'X-Yanzi-Agent-Request': '1', 'Content-Type': 'application/json' },
        body: JSON.stringify(input), signal: AbortSignal.timeout(20000),
      });
      const value = await response.json();
      if (!response.ok) throw new Error('coordinator_response_' + response.status + ':' +
        String(value.error || '').slice(0, 120));
      return value;
    },
  };
}
