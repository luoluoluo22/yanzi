// Platform-neutral device client. Supply an account-scoped durable store and a runtime adapter.
export class DeviceProtocolError extends Error {
  constructor(code, status = 0, details = null) { super(code); this.code = code; this.status = status; this.details = details; }
}
export const isExecution = kind => /^(run-|fs-)/.test(kind) || kind === 'capability.invoke';
export const canonicalJson = value => Array.isArray(value) ? '[' + value.map(canonicalJson).join(',') + ']' :
  value && typeof value === 'object' ? '{' + Object.keys(value).sort().map(key => JSON.stringify(key) + ':' + canonicalJson(value[key])).join(',') + '}' : JSON.stringify(value);

// A duplicate arriving while a local effect is still running is not a crashed execution.
const activeInboxes = new WeakMap();

export class DeviceClient {
  constructor({baseUrl, accountId, deviceId, platform, displayName, capabilities = {}, store, getToken,
    fetchImpl = fetch, receive, execute, now = () => Date.now()}) {
    const url = new URL(baseUrl);
    if (url.protocol !== 'https:' && !(url.protocol === 'http:' && ['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname)))
      throw new DeviceProtocolError('secure_server_required');
    if (!store || !getToken || !accountId || !deviceId) throw new DeviceProtocolError('durable_store_and_identity_required');
    if ((receive || execute) && !store.compareExchange) throw new DeviceProtocolError('transactional_inbox_store_required');
    Object.assign(this, {baseUrl:baseUrl.replace(/\/+$/, ''), accountId, deviceId, platform, displayName, capabilities,
      store, getToken, fetchImpl, receive, execute, now});
    this.prefix = accountId + '/' + deviceId + '/'; this.flushing = false; this.syncing = false;
  }
  async request(path, method = 'GET', body) {
    const timeout = new AbortController(); const timer = setTimeout(() => timeout.abort(), 10000);
    try {
      const response = await this.fetchImpl(this.baseUrl + path, {method, signal:timeout.signal,
        headers:{Authorization:'Bearer ' + await this.getToken(), 'Content-Type':'application/json'},
        body:body === undefined ? undefined : JSON.stringify(body)});
      let result;
      try { result = await response.json(); }
      catch {
        throw new DeviceProtocolError(response.ok ? 'invalid_response' : 'http_error', response.status);
      }
      if (!result || typeof result !== 'object' || Array.isArray(result))
        throw new DeviceProtocolError('invalid_response', response.status);
      if (!response.ok) throw new DeviceProtocolError(result.error || 'http_error', response.status, result.details);
      return result;
    } finally { clearTimeout(timer); }
  }
  async register() {
    this.protocol = await this.request('/v1/me/devices/protocol');
    if (!this.protocol.supportedVersions?.includes(1)) throw new DeviceProtocolError('unsupported_message_protocol', 426);
    return this.request('/v1/me/devices', 'POST', {deviceId:this.deviceId, platform:this.platform,
      displayName:this.displayName, capabilities:{...this.capabilities, deviceMessageProtocolVersions:[1]}});
  }
  async send(input) {
    await this.maintainOutbox();
    const envelope = structuredClone(input);
    envelope.protocolVersion = 1; envelope.sourceDeviceId = this.deviceId;
    envelope.clientMessageId ||= crypto.randomUUID(); envelope.operationId ||= envelope.clientMessageId;
    envelope.traceId ||= envelope.operationId;
    const key = this.prefix + 'outbox/' + envelope.clientMessageId;
    const previous = await this.store.get(key);
    if (!envelope.expiresAt && previous?.envelope.expiresAt) envelope.expiresAt = previous.envelope.expiresAt;
    if (isExecution(envelope.kind)) {
      if (!envelope.targetDeviceId) throw new DeviceProtocolError('target_device_required', 409);
      envelope.expiresAt ||= new Date(this.now() + 120000).toISOString();
      if (Date.parse(envelope.expiresAt) > this.now() + 300000) throw new DeviceProtocolError('execution_deadline_too_long', 400);
    }
    else envelope.expiresAt ||= new Date(this.now() + 30 * 86400000).toISOString();
    if (!Number.isFinite(Date.parse(envelope.expiresAt))) throw new DeviceProtocolError('invalid_expiration', 400);
    if (previous && canonicalJson(previous.envelope) !== canonicalJson(envelope)) throw new DeviceProtocolError('message_id_reused', 409);
    if (!previous) {
      if ((await this.store.list(this.prefix + 'outbox/', 10000)).filter(([, item]) => item.state === 'queued').length >= 1000) throw new DeviceProtocolError('outbox_quota_exceeded');
      await this.store.set(key, {state:'queued', envelope, attempts:0, nextAttemptAt:0, createdAt:this.now()});
    }
    await this.flush(); return this.store.get(key);
  }
  async flush() {
    if (this.flushing) return; this.flushing = true;
    try {
      let processed = 0;
      await this.maintainOutbox();
      for (const [key, item] of (await this.store.list(this.prefix + 'outbox/', 10000)).sort((a,b) => (a[1].createdAt || 0) - (b[1].createdAt || 0))) {
        if (item.state !== 'queued' || item.nextAttemptAt > this.now()) continue;
        if (processed++ >= 20) break;
        if (item.envelope.expiresAt && Date.parse(item.envelope.expiresAt) <= this.now()) {
          await this.store.set(key, {...item, state:'expired', errorCode:'message_expired'}); continue;
        }
        try {
          const result = await this.request('/v1/me/mobile/messages', 'POST', item.envelope);
          if (typeof result.messageId !== 'string' || !result.messageId)
            throw new DeviceProtocolError('invalid_response');
          await this.store.set(key, {...item, state:'accepted', messageId:result.messageId, updatedAt:this.now()});
        } catch (error) {
          const retry = !error.status || error.code === 'invalid_response' || error.status === 429 || error.status >= 500 || error.status === 401;
          const attempts = item.attempts + 1;
          await this.store.set(key, {...item, state:retry ? 'queued' : 'failed', errorCode:error.code || 'transport_unavailable', attempts,
            updatedAt:this.now(), nextAttemptAt:this.now() + Math.min(60000, 1000 * 2 ** Math.min(attempts, 6))});
          if (retry) break;
        }
      }
    } finally { this.flushing = false; }
  }
  async maintainOutbox() {
    if (!this.store.delete) return;
    const terminal = (await this.store.list(this.prefix + 'outbox/', 10000)).filter(([, item]) => item.state !== 'queued')
      .sort((a,b) => (a[1].updatedAt || a[1].createdAt || 0) - (b[1].updatedAt || b[1].createdAt || 0));
    for (let index = 0; index < terminal.length; index++) {
      const [key, item] = terminal[index];
      if (terminal.length - index > 5000 || (item.updatedAt || item.createdAt || this.now()) < this.now() - 7 * 86400000) await this.store.delete(key);
    }
  }
  async cancel(messageId) { return this.request('/v1/me/mobile/messages/' + encodeURIComponent(messageId) + '/cancel', 'POST', {deviceId:this.deviceId}); }
  async ack(message, item) {
    const body = {deviceId:this.deviceId};
    if (isExecution(message.kind) || item.state === 'failed' || item.state === 'unknown') {
      body.success = item.state === 'completed'; body.result = item.output || item.errorCode || '';
      if (item.state === 'unknown') body.resultState = 'unknown';
    }
    return this.request('/v1/me/mobile/messages/' + encodeURIComponent(message.messageId) + '/ack', 'POST', body);
  }
  async consume(message) {
    let active = activeInboxes.get(this.store);
    if (!active) { active = new Map(); activeInboxes.set(this.store, active); }
    const logical = message.operationId || message.clientMessageId || message.payload?.clientOperationId || message.payload?.clientTransferId || message.messageId;
    const key = this.prefix + 'inbox/' + message.sourceDeviceId + '/' + logical;
    if (message.targetDeviceId && message.targetDeviceId !== this.deviceId) throw new DeviceProtocolError('wrong_target');
    if (active.has(key)) return active.get(key);
    const pending = this.consumeOnce(message);
    active.set(key, pending);
    try { return await pending; } finally { active.delete(key); }
  }
  async consumeOnce(message) {
    if (message.targetDeviceId && message.targetDeviceId !== this.deviceId) throw new DeviceProtocolError('wrong_target');
    const logical = message.operationId || message.clientMessageId || message.payload?.clientOperationId || message.payload?.clientTransferId || message.messageId;
    const key = this.prefix + 'inbox/' + message.sourceDeviceId + '/' + logical;
    let saved = await this.store.get(key);
    if (!saved) {
      saved = {state:'saved', message};
      if (!await this.store.compareExchange(key, null, saved)) saved = await this.store.get(key);
    }
    if (saved.state === 'executing') {
      saved = {...saved, state:'unknown', errorCode:'execution_result_unknown'}; await this.store.set(key, saved);
    }
    if (['completed', 'failed', 'unknown', 'cancelled', 'expired'].includes(saved.state)) { await this.ack(message, saved); return saved; }
    if (isExecution(message.kind)) {
      if (!message.expiresAt || !Number.isFinite(Date.parse(message.expiresAt)) || Date.parse(message.expiresAt) <= this.now()) {
        saved = {...saved, state:'expired', errorCode:'message_expired'}; await this.store.set(key, saved); await this.ack(message, saved); return saved;
      }
      if (!this.execute) throw new DeviceProtocolError('execution_adapter_required');
      // Persist before claim: a crash or lost claim response must never rerun an effect.
      saved = {...saved, state:'executing'};
      if (!await this.store.compareExchange(key, 'saved', saved)) return {state:'unknown', errorCode:'execution_result_unknown'};
      const claim = await this.request('/v1/me/mobile/messages/' + encodeURIComponent(message.messageId) + '/claim', 'POST', {deviceId:this.deviceId});
      if (!claim.acquired) {
        saved = {...saved, state:['cancelled', 'expired'].includes(claim.status) ? claim.status : 'unknown', errorCode:'execution_result_unknown'};
        await this.store.set(key, saved); await this.ack(message, saved); return saved;
      }
      if (!Number.isFinite(Date.parse(claim.expiresAt)) || Date.parse(claim.expiresAt) <= this.now()) {
        saved = {...saved, state:'expired', errorCode:'message_expired'}; await this.store.set(key, saved); await this.ack(message, saved); return saved;
      }
    } else {
      if (!this.receive) throw new DeviceProtocolError('receive_adapter_required');
      saved = {...saved, state:'executing'};
      if (!await this.store.compareExchange(key, 'saved', saved)) return {state:'unknown', errorCode:'execution_result_unknown'};
    }
    try {
      const result = await (isExecution(message.kind) ? this.execute(message) : this.receive?.(message));
      saved = {...saved, state:result?.success === false ? 'failed' : 'completed', output:result?.output || ''};
    } catch (error) { saved = {...saved, state:error.code === 'execution_result_unknown' ? 'unknown' : 'failed', errorCode:error.code || 'execution_failed', output:error.message}; }
    await this.store.set(key, saved); await this.ack(message, saved); return saved;
  }
  async sync() {
    if (this.syncing) return; this.syncing = true;
    try {
      // Restart from pending messages, including messages below a previously saved cursor whose ACK was lost.
      let cursor = 0;
      for (let page = 0; page < 100; page++) {
        const response = await this.request('/v1/me/mobile/messages?deviceId=' + encodeURIComponent(this.deviceId) + '&limit=50&after=' + cursor);
        for (const message of response.items || []) await this.consume(message);
        await this.store.set(this.prefix + 'cursor', response.nextCursor || cursor);
        if (!response.hasMore) return;
        if (response.nextCursor <= cursor) throw new DeviceProtocolError('non_monotonic_cursor');
        cursor = response.nextCursor;
      }
      throw new DeviceProtocolError('sync_backpressure');
    } finally { this.syncing = false; }
  }
}

export class IndexedDbDeviceStore {
  constructor(databaseName = 'yanzi-device-protocol') { this.databaseName = databaseName; }
  async database() {
    if (this.db) return this.db;
    this.db = await new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore('records');
      request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
    }); return this.db;
  }
  async transact(mode, operation) {
    const db = await this.database();
    return new Promise((resolve, reject) => {
      const transaction = db.transaction('records', mode), store = transaction.objectStore('records');
      const request = operation(store); let result;
      request.onsuccess = () => result = request.result;
      transaction.oncomplete = () => resolve(result); transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }
  get(key) { return this.transact('readonly', store => store.get(key)); }
  set(key, value) { return this.transact('readwrite', store => store.put(value, key)); }
  delete(key) { return this.transact('readwrite', store => store.delete(key)); }
  async compareExchange(key, expectedState, value) {
    const db = await this.database();
    return new Promise((resolve, reject) => {
      const transaction = db.transaction('records', 'readwrite'), store = transaction.objectStore('records');
      const request = store.get(key); let changed = false;
      request.onsuccess = () => {
        if ((request.result?.state ?? null) === expectedState) { store.put(value, key); changed = true; }
      };
      transaction.oncomplete = () => resolve(changed); transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }
  async list(prefix, limit = 1000) {
    const db = await this.database();
    return new Promise((resolve, reject) => {
      const result = [], transaction = db.transaction('records', 'readonly');
      const request = transaction.objectStore('records').openCursor(IDBKeyRange.bound(prefix, prefix + '\uffff'));
      request.onsuccess = () => { const cursor = request.result; if (cursor && result.length < limit) {result.push([cursor.key, cursor.value]); cursor.continue();} };
      transaction.oncomplete = () => resolve(result); transaction.onerror = () => reject(transaction.error);
    });
  }
}
