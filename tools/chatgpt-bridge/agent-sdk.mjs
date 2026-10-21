/**
 * Explicit-identity local SDK for trusted scripts running on the Yanzi host.
 * Agent identity must come from the bootstrap context (or scoped environment).
 * No browser tab guessing, cookie scraping or remote token distribution.
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

export function createAgentClient({
  agentId = process.env.YANZI_AGENT_ID,
  base = 'http://127.0.0.1:53921',
  token = null,
  fetchImpl = fetch
} = {}) {
  if (typeof agentId !== 'string' || !/^[0-9a-f-]{36}$/i.test(agentId))
    throw new Error('agent_self_id_required');
  const u = new URL(base);
  if (u.protocol !== 'http:' || u.hostname !== '127.0.0.1' || u.port !== '53921' ||
      u.pathname !== '/') throw new Error('agent_bridge_must_be_local');
  // Resolve the file only inside the authorized local process, never the chat.
  function localToken() {
    const root=process.env.LOCALAPPDATA;
    if (token) return token;
    if (!root) throw new Error('agent_local_data_missing');
    return readFileSync(join(root,'OpenQuickHost','ExtensionStorage',
      'chatgpt-bridge','api-token.txt'),'utf8').trim();
  }
  async function api(path,{method='GET',payload}={}) {
    const headers={Authorization:'Bearer '+localToken(),'X-Bridge-Request':'1'};
    if(payload!==undefined)headers['Content-Type']='application/json';
    const response=await fetchImpl(base+path,{
      method,headers,body:payload===undefined?undefined:JSON.stringify(payload)
    });
    const data=await response.json();
    if(!response.ok)throw new Error('agent_api_error_'+response.status+':'+JSON.stringify(data));
    return data;
  }
  return Object.freeze({
    self:()=>api('/api/agents/'+agentId+'/context'),
    status:()=>api('/api/managed-parents/'+agentId),
    spawn:async ({requestKey,title,prompt,timeoutSeconds}={})=>{
      const context=await api('/api/agents/'+agentId+'/context');
      if(context.role!=='supervisor')throw new Error('agent_spawn_requires_supervisor');
      return api('/api/subagents',{method:'POST',payload:{
        requestKey,title,prompt,timeoutSeconds,bootstrap:true,
        parentManagedId:agentId
      }});
    },
    children:()=>api('/api/subagents')
  });
}
