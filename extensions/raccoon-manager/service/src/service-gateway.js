import http from "node:http";
import { timingSafeEqual } from "node:crypto";

export class ServiceGateway {
  constructor({ current, token, prepare, probe, release, activate, retire, persist = () => {} }) {
    Object.assign(this,{current,token,prepare,probe,release,activate,retire,persist});
    this.activeRequests=0; this.switching=false; this.updating=false; this.waiters=new Set();
    this.server=http.createServer((req,res)=>this.handle(req,res).catch(error=>{
      console.error("Gateway request failed:",error.message);
      if(!res.headersSent) {res.statusCode=502;res.end("Upstream unavailable");} else res.destroy();
    }));
    this.server.requestTimeout=0;
  }
  authorized(req) {
    const expected=Buffer.from(`Bearer ${this.token}`),received=Buffer.from(req.headers.authorization || "");
    const hostname=(req.headers.host || "").split(":")[0];
    return Boolean(this.token) && ["127.0.0.1","localhost"].includes(hostname) && received.length===expected.length && timingSafeEqual(expected,received);
  }
  async handle(req,res) {
    if(req.url.startsWith("/__raccoon/")) {
      if(!this.authorized(req)) {res.statusCode=404;res.end();return;}
      res.setHeader("Content-Type","application/json");
      if(req.method === "GET" && req.url === "/__raccoon/status") {
        res.end(JSON.stringify({current:this.current,activeRequests:this.activeRequests,updating:this.updating}));return;
      }
      if(req.method === "POST" && req.url === "/__raccoon/deploy") {
        let body="";
        for await(const chunk of req) {body+=chunk; if(Buffer.byteLength(body)>4096) throw new Error("Control body too large");}
        try {res.end(JSON.stringify(await this.deploy(JSON.parse(body))));}
        catch(error) {res.statusCode=error.busy?409:400;res.end(JSON.stringify({ok:false,error:error.message,state:error.state}));}
        return;
      }
      res.statusCode=404;res.end();return;
    }
    if(this.switching) await new Promise(resolve=>this.waiters.add(resolve));
    if(req.destroyed || res.destroyed) return;
    const target=this.current;
    this.activeRequests++;
    let done=false;
    const finish=()=>{if(!done){done=true;this.activeRequests--;}};
    res.once("close",finish);res.once("finish",finish);
    await new Promise((resolve,reject)=>{
      const upstream=http.request({hostname:"127.0.0.1",port:target.port,path:req.url,method:req.method,headers:req.headers,agent:false}, response=>{
        res.writeHead(response.statusCode,{...response.headers,"x-raccoon-gateway":"1"});
        response.pipe(res);response.once("end",resolve);response.once("error",reject);
      });
      upstream.once("error",reject);
      res.once("close",()=>upstream.destroy());
      req.pipe(upstream);
    }).finally(finish);
  }
  unlock() {this.switching=false;for(const wake of this.waiters)wake();this.waiters.clear();}
  async deploy(spec) {
    if(this.updating) throw new Error("Another deployment is already preparing");
    this.updating=true;
    let candidate,committed=false;
    try {
      candidate=await this.prepare(spec);
      // Never hold new requests waiting behind an existing long tool call.
      if(this.activeRequests>0) throw this.busy({activeRequests:this.activeRequests});
      this.switching=true;
      const state=await this.probe(this.current);
      if(state.busy || this.activeRequests>0) throw this.busy(state);
      await this.activate(candidate);
      await this.release(this.current);
      const previous=this.current;
      this.current=candidate;
      try {await this.persist(candidate);} catch(error) {this.current=previous;throw error;}
      committed=true;
      this.unlock();
      // State was released only after all calls and retained sessions were checked.
      let retirementWarning;
      try {await this.retire(previous);} catch(error) {retirementWarning=error.message;}
      return {ok:true,current:candidate,...(retirementWarning ? {retirementWarning} : {})};
    } finally {
      this.unlock();this.updating=false;
      if(candidate && !committed) await this.retire(candidate);
    }
  }
  busy(state) {const error=new Error("Deployment deferred: requests, tasks or retained sessions are still in use");error.busy=true;error.state=state;return error;}
}
