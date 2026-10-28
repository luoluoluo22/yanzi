/** Browser/Node SDK. Pass a user-approved, scoped token; never embed account credentials. */
export class YanziDataClient {
  constructor({baseUrl, extensionId, token, fetch: fetchImpl = globalThis.fetch}) {
    this.baseUrl = baseUrl.replace(/\/$/, ''); this.extensionId = extensionId;
    this.token = token; this.fetch = fetchImpl;
  }
  async request(key, input) {
    const token = typeof this.token === 'function' ? await this.token() : this.token;
    const response = await this.fetch(`${this.baseUrl}/v1/extension-data/${encodeURIComponent(this.extensionId)}?key=${encodeURIComponent(key)}`, {
      method:input ? 'PUT':'GET', headers:{Authorization:`Bearer ${token}`, 'Content-Type':'application/json'},
      ...(input ? {body:JSON.stringify(input)} : {})
    });
    const result = await response.json();
    if (!response.ok) { const error = new Error(result.message || result.error); error.status=response.status; error.details=result.details; throw error; }
    return result;
  }
  read(key) { return this.request(key); }
  write(key, content, expectedRevision, accountId) { return this.request(key,{content,expectedRevision,accountId}); }
}

/** Persistent per-account outbox. Caller supplies storage (localStorage or an equivalent adapter).
 * Conflicting snapshots remain local until the caller explicitly resolves them.
 */
export class YanziDocument {
  constructor({client, key, accountId, storage}) {
    this.client=client; this.key=key; this.accountId=accountId; this.storage=storage;
    this.cacheKey=`yanzi.document.v1.${JSON.stringify([accountId,client.extensionId,key])}`;
    this.state=JSON.parse(storage.getItem(this.cacheKey)||'{"revision":0,"content":"","dirty":false,"generation":0}');
    this.running=null;
  }
  persist() { this.storage.setItem(this.cacheKey,JSON.stringify(this.state)); }
  save(content) {
    if (typeof content !== 'string') throw new TypeError('Content must be text');
    this.state.content=content; this.state.dirty=true; this.state.generation++; this.persist();
  }
  sync() {
    if (this.running) return this.running;
    this.running=this.performSync().finally(()=>{this.running=null;}); return this.running;
  }
  async performSync() {
    const remote=await this.client.read(this.key);
    if (remote.accountId !== this.accountId) throw new Error('Account changed');
    if (!this.state.dirty) { this.state.revision=remote.revision; this.state.content=remote.content; this.persist(); return {status:'synced'}; }
    // A previous upload may have succeeded before its acknowledgement was lost.
    if (remote.content === this.state.content) {
      this.state.revision=remote.revision; this.state.dirty=false; delete this.state.conflict; this.persist(); return {status:'synced'};
    }
    if (remote.revision !== this.state.revision) {
      this.state.conflict=remote; this.persist(); return {status:'conflict',remote};
    }
    const generation=this.state.generation, content=this.state.content;
    let result;
    try { result=await this.client.write(this.key,content,remote.revision,this.accountId); }
    catch(error) {
      if (error.status === 409) return {status:'retry'};
      throw error; // Local outbox is already durable. Call sync again when online.
    }
    if (result.accountId !== this.accountId) throw new Error('Account changed');
    this.state.revision=result.revision;
    this.state.dirty=this.state.generation!==generation;
    delete this.state.conflict; this.persist(); return {status:this.state.dirty?'pending':'synced'};
  }
  resolve(choice) {
    const remote=this.state.conflict;
    if (!remote) throw new Error('No conflict');
    if (!['cloud','local'].includes(choice)) throw new Error('Choose cloud or local');
    this.state.revision=remote.revision;
    if (choice==='cloud') {this.state.content=remote.content;this.state.dirty=false;}
    this.state.generation++; delete this.state.conflict; this.persist();
  }
}
