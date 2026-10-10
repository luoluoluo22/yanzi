import {Client} from '@modelcontextprotocol/sdk/client/index.js';
import {StdioClientTransport} from '@modelcontextprotocol/sdk/client/stdio.js';
import {fileURLToPath} from 'node:url';
const client=new Client({name:'yanzi-smoke',version:'1.0.0'});
try {
  await client.connect(new StdioClientTransport({command:process.execPath,args:[fileURLToPath(new URL('../src/index.js',import.meta.url))],stderr:'pipe'}));
  const list=await client.listTools();
  for(const name of ['yanzi_extension_open','yanzi_wechat_fileTransfer_sendText'])if(!list.tools.some(t=>t.name===name))throw new Error('Missing tool '+name);
  const results={};
  for(const name of ['yanzi_ping','yanzi_wechat_status','yanzi_extension_list']) {
    const result=await client.callTool({name,arguments:{}});
    if(result.isError)throw new Error(name+': '+result.content[0].text);
    const data=JSON.parse(result.content[0].text);
    results[name]={ok:!result.isError,success:data.success};
  }
  const invalid=await client.callTool({name:'yanzi_wechat_fileTransfer_sendText',arguments:{}});
  if(!invalid.isError)throw new Error('Missing text was accepted');
  console.log(JSON.stringify({tools:list.tools.length,results,invalidInputRejected:true}));
} finally {await client.close();}
