import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createAgentClient } from '../agent-sdk.mjs';

const id='aa111111-bb22-cc33-dd44-eeeeeeeeeeee';

test('explicit self ID and local-only transport are mandatory',()=>{
  assert.throws(()=>createAgentClient({agentId:'bad'}),/agent_self_id_required/);
  assert.throws(()=>createAgentClient({agentId:id,base:'https://evil.example'}),/agent_bridge_must_be_local/);
  assert.throws(()=>createAgentClient({agentId:id,base:'http://127.0.0.1:53921/other'}),/agent_bridge_must_be_local/);
});

test('local SDK self and spawn use registered identity and never export bearer in payload',async()=>{
  const calls=[];
  const fetchImpl=async(path,init)=>{
    calls.push({path,init});
    return {ok:true,status:200,json:async()=>path.endsWith('/context')
      ? {agentId:id,role:'supervisor',conversationId:'the-verified-conversation'}
      : {id:'child-generated'}};
  };
  const client=createAgentClient({agentId:id,token:'local-only-secret',fetchImpl});
  const self=await client.self();
  assert.equal(self.agentId,id);
  await client.spawn({requestKey:'child:test:01',title:'child',prompt:'Short task'});
  assert.equal(calls.length,3);
  assert.equal(calls[1].path.endsWith('/context'),true);
  const input=JSON.parse(calls[2].init.body);
  assert.equal(input.parentManagedId,id);
  assert.equal(input.bootstrap,true);
  assert.equal(input.prompt,'Short task');
  assert.equal(calls[2].init.headers.Authorization,'Bearer local-only-secret');
  assert.equal(calls[2].init.body.includes('local-only-secret'),false);
});
