import {Server} from '@modelcontextprotocol/sdk/server/index.js';
import {ListToolsRequestSchema,CallToolRequestSchema} from '@modelcontextprotocol/sdk/types.js';
import {YanziBridge} from './bridge.js';
const bridge=new YanziBridge();
export function createYanziServer(connection=bridge) {
const server=new Server({name:'yanzi-mcp',version:'0.1.0'},{capabilities:{tools:{listChanged:true}}});
const builtin=[
  {name:'yanzi_ping',description:'检查本机燕子 Agent API 是否在线。',inputSchema:{type:'object',additionalProperties:false},annotations:{readOnlyHint:true}},
  {name:'yanzi_catalog',description:'发现燕子小程序、能力和真实参数定义；只读取目录。',inputSchema:{type:'object',additionalProperties:false},annotations:{readOnlyHint:true}}
];
server.setRequestHandler(ListToolsRequestSchema,async()=> {
  try {return {tools:[...builtin,...Array.from((await connection.discover(true)).values(),x=>x.tool)]};}
  catch(e) {console.error('Yanzi discovery unavailable: '+e.message);return {tools:builtin};}
});
server.setRequestHandler(CallToolRequestSchema,async request=> {
  const started=Date.now();
  try {
    const data=await connection.call(request.params.name,request.params.arguments||{});
    const failed=data?.success===false || data?.ok===false;
    console.error(JSON.stringify({at:new Date().toISOString(),tool:request.params.name,ms:Date.now()-started,ok:!failed}));
    return {isError:failed,content:[{type:'text',text:JSON.stringify(data)}]};
  } catch(e) {
    console.error(JSON.stringify({at:new Date().toISOString(),tool:request.params.name,ms:Date.now()-started,ok:false}));
    return {isError:true,content:[{type:'text',text:e.message}]};
  }
});
return server;
}
