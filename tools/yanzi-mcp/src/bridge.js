import fs from 'node:fs/promises';
import path from 'node:path';
import Ajv from 'ajv';
import {randomUUID} from 'node:crypto';

export class YanziBridge {
  constructor({settingsFile = process.env.YANZI_SETTINGS_FILE || path.join(process.env.LOCALAPPDATA || '', 'OpenQuickHost', 'appsettings.local.json'), fetchImpl = fetch} = {}) {
    this.settingsFile = settingsFile;
    this.fetch = fetchImpl;
    this.ajv = new Ajv({strict:false, allErrors:true});
    this.tools = new Map();
    this.loadedAt = 0;
  }
  async settings() {
    const data = JSON.parse((await fs.readFile(this.settingsFile,'utf8')).replace(/^\uFEFF/,''));
    if (!Number.isInteger(data.agentApiPort) || data.agentApiPort < 1 || data.agentApiPort > 65535 || !data.agentApiToken) throw new Error('Yanzi Agent API settings are incomplete.');
    return {port:data.agentApiPort, token:data.agentApiToken};
  }
  async request(route, body, timeoutMs=15000) {
    const {port,token} = await this.settings();
    try {
      const response = await this.fetch(`http://127.0.0.1:${port}${route}`, {
        method:body===undefined?'GET':'POST',
        headers:{'X-Yanzi-Token':token,'Content-Type':'application/json; charset=utf-8'},
        body:body===undefined?undefined:JSON.stringify(body), signal:AbortSignal.timeout(timeoutMs)
      });
      const raw = await response.text();
      let data; try {data=JSON.parse(raw);} catch {data={message:raw};}
      // Never retry a mutating call: it may already have completed in Yanzi.
      if (!response.ok) throw new Error(`Yanzi API ${response.status}: ${JSON.stringify(data)}`);
      return JSON.parse(JSON.stringify(data).replaceAll(token,'[redacted]'));
    } catch(error) {
      throw new Error(String(error.message).replaceAll(token,'[redacted]'));
    }
  }
  async discover(force=false) {
    if (!force && this.loadedAt && Date.now()-this.loadedAt < 60000) return this.tools;
    // The registered-only endpoint belongs to the API process. Extension leases
    // can live in another runtime or disappear while a provider is stopped.
    // The agent catalog is the public contract for installed providers as well
    // as host capabilities; invocation resolves/starts the provider in Yanzi.
    const catalog = await this.request('/v1/agent/catalog');
    if (!Array.isArray(catalog.hostCapabilities) || !Array.isArray(catalog.extensions)) throw new Error('Yanzi returned an invalid capability catalog.');
    const capabilities = [...catalog.hostCapabilities, ...catalog.extensions.flatMap(extension => extension.capabilities || [])];
    const next = new Map();
    for (const cap of capabilities) {
      if (cap.audience !== 'user' || typeof cap.name !== 'string') continue;
      const name = 'yanzi_'+cap.name.replace(/[^a-zA-Z0-9_-]/g,'_');
      if (next.has(name)) throw new Error(`Capability name collision: ${name}`);
      // MCP requires an object root; Yanzi's parameterless music actions use {}.
      const schema = {type:'object',...(cap.inputSchema || {})};
      const readonly = cap.permissions?.length>0 && cap.permissions.every(p=>p.endsWith('.read'));
      const tool = {name, description:cap.description+'\n燕子能力：'+cap.name+(cap.requiresConfirmation?'。此能力要求用户明确确认。':''),inputSchema:schema,
        annotations:{title:cap.name,readOnlyHint:!!readonly,destructiveHint:!readonly,idempotentHint:!!readonly,openWorldHint:true}};
      next.set(name,{cap, tool, validate:this.ajv.compile(schema)});
    }
    this.tools=next; this.loadedAt=Date.now(); return next;
  }
  async call(name,args={}) {
    if (name==='yanzi_ping') return this.request('/health');
    if (name==='yanzi_catalog') return this.request('/v1/agent/catalog');
    await this.discover();
    let item=this.tools.get(name);
    // A provider may have been installed after this connection's last list.
    // Refresh discovery once; never retry the invocation itself.
    if (!item) {await this.discover(true); item=this.tools.get(name);}
    if (!item) throw new Error('Unknown Yanzi tool: '+name);
    if (!item.validate(args)) throw new Error('Invalid arguments: '+this.ajv.errorsText(item.validate.errors));
    const payload={...args};
    if (['chat.send','wechat.fileTransfer.sendText'].includes(item.cap.name) && item.tool.inputSchema.properties?.requestId && !payload.requestId) payload.requestId=randomUUID();
    try {return await this.request('/v1/capabilities/invoke',{name:item.cap.name,payload},150000);}
    catch(error) {throw new Error(error.message+(payload.requestId?'；requestId='+payload.requestId+'。先用 task.status 查询原任务，勿重新发送。':''));}
  }
}
