import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import express from 'express';
import {hostHeaderValidation} from '@modelcontextprotocol/sdk/server/middleware/hostHeaderValidation.js';
import {StreamableHTTPServerTransport} from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import {createYanziServer} from './server.js';
import {installOAuth} from './oauth.js';

export function createHttpApp({issuer='https://yanzi-mcp.luoluoluo.cc.cd', password, stateFile,serverFactory=createYanziServer}={}) {
 const app=express();
 app.disable('x-powered-by');
 app.use(hostHeaderValidation(['127.0.0.1','localhost',new URL(issuer).hostname]));
 app.use((_req,res,next)=>{res.setHeader('Cache-Control','no-store');res.setHeader('X-Content-Type-Options','nosniff');next();});
 app.get('/health',(_req,res)=>res.json({ok:true,name:'yanzi-mcp',version:'0.1.0'}));
 const auth=installOAuth(app,{issuer,password,stateFile});
 app.use('/mcp',(req,res,next)=>{
  if(req.headers.origin && ![issuer,'https://chatgpt.com'].includes(req.headers.origin)) return res.sendStatus(403);
  next();
 },auth,express.json({limit:'1mb'}));
 app.post('/mcp',async(req,res)=>{
  const server=serverFactory();
  const transport=new StreamableHTTPServerTransport({sessionIdGenerator:undefined,enableJsonResponse:true});
  let closed=false;
  const cleanup=async()=>{if(closed)return;closed=true;await transport.close().catch(()=>{});await server.close().catch(()=>{});};
  res.once('close',cleanup);
  try{await server.connect(transport);await transport.handleRequest(req,res,req.body);}
  catch{if(!res.headersSent)res.status(500).json({error:'MCP request failed'});await cleanup();}
 });
 app.all('/mcp',(_req,res)=>{res.setHeader('Allow','POST');res.sendStatus(405);});
 app.use((error,_req,res,_next)=>res.status(error.type==='entity.too.large'?413:error instanceof SyntaxError?400:500).json({error:'Invalid request'}));
 return app;
}

if(process.argv[1] && path.resolve(process.argv[1])===fileURLToPath(import.meta.url)) {
 const runtime=new URL('../.runtime/',import.meta.url);
 const password=fs.readFileSync(new URL('oauth-owner-password.txt',runtime),'utf8').trim();
 const app=createHttpApp({password,stateFile:fileURLToPath(new URL('oauth-state.json',runtime))});
 const listener=app.listen(3767,'127.0.0.1',()=>console.error('Yanzi MCP listening on http://127.0.0.1:3767/mcp'));
 listener.requestTimeout=180000;
}
