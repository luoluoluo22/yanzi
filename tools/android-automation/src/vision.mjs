import {LocalOcrClient} from './local-ocr.mjs';
import {delay} from './adb.mjs';

export class UnsafeTargetError extends Error {}
export function asBox(line) {
  const b=line.box ?? line.bounds;
  if(!b) return null;
  if('x' in b && 'width' in b) return b;
  if('left' in b) return {x:b.left,y:b.top,width:b.right-b.left,height:b.bottom-b.top};
  return null;
}
export function selectUnique(items, query, {exact=false,minScore=0}={}) {
  if(!query?.trim()) throw new Error('Missing target');
  const q=query.replace(/\s+/g,'').toLowerCase();
  const hits=items.filter(it=>{
    const value=String(it.text||it.description||'').replace(/\s+/g,'').toLowerCase();
    const b=asBox(it);
    return it.enabled!==false && b && b.width>2 && b.height>2 &&
      (exact ? value===q : value.includes(q)) &&
      (it.recognitionScore===undefined || Number(it.recognitionScore)>=minScore);
  });
  if(hits.length!==1)throw new UnsafeTargetError('Target '+JSON.stringify(query)+' requires one match; found '+hits.length);
  const {x,y,width,height}=asBox(hits[0]);
  return {x:Math.round(x+width/2),y:Math.round(y+height/2),matched:hits[0]};
}
export class ScreenVision {
  constructor(device,{bridge=new LocalOcrClient(),ocr}={}) {this.device=device;this.bridge=bridge;this.ocr=ocr;}
  async recognize() {
    return this.device.withScreenshotFile(async imagePath=>{
      const result=this.ocr ? await this.ocr(imagePath) :
        await this.bridge.request('/v1/capabilities/invoke',{name:'ocr.recognize',payload:{imagePath,includeLines:true}},150000);
      if(!result || result.success===false)throw new Error('OCR failed: '+(result?.error || 'service unavailable'));
      const data=result.data || result;
      return {text:data.text||'',lines:data.lines||[],image:data.image||null,engine:data.engine||'unknown'};
    });
  }
  async locate(query,{exact=false,minScore=0}={}) {
    // UI elements can be missing entirely for WeChat mini-program WebViews.
    let nodes=[];
    try{nodes=await this.device.uiTree();}catch{/* OCR below will be authoritative */}
    const viable=nodes.filter(n=>n.enabled && (n.text||n.description) && n.bounds.width>0 && n.bounds.height>0);
    if(viable.length) {
      try {return {source:'uiautomator',...selectUnique(viable,query,{exact})};}
      catch(e) {if(!(e instanceof UnsafeTargetError)||!e.message.endsWith('found 0'))throw e;}
    }
    const result=await this.recognize();
    return {source:'ocr',...selectUnique(result.lines,query,{exact,minScore})};
  }
  async tapText(query,{exact=false,minScore=0,verifyText,waitMs=650}={}) {
    const found=await this.locate(query,{exact,minScore});
    const size=await this.device.size();
    if(found.x>=size.width || found.y>=size.height) throw new UnsafeTargetError('Target outside display');
    await this.device.tap(found.x,found.y);
    if(verifyText) await this.waitFor(verifyText,{timeoutMs:7500});
    else await delay(waitMs);
    return {target:query,source:found.source,verified:!!verifyText};
  }
  async waitFor(target,{timeoutMs=9000,intervalMs=850}={}) {
    const stop=Date.now()+timeoutMs;
    let last;
    while(Date.now()<stop) {
      try {return await this.locate(target);}
      catch(e) {last=e;if(!(e instanceof UnsafeTargetError))throw e;}
      await delay(intervalMs);
    }
    throw new UnsafeTargetError('Screen did not reach state '+JSON.stringify(target)+': '+last?.message);
  }
  async inspect() {
    const [foreground,size,screen]=await Promise.all([this.device.foreground(),this.device.size(),this.recognize()]);
    return {foreground,size,engine:screen.engine,text:screen.text,lines:screen.lines.map(({text,recognitionScore,box})=>({text,recognitionScore,box}))};
  }
}
