import {createHash} from 'node:crypto';
import {delay} from './adb.mjs';

// Read-only parser for a PDD order-detail screen. Contact names, telephone,
// pickup address and QR codes are deliberately NEVER returned.
const BEGIN=/^(?:冷鲜|冷藏|冷冻|精选|热销|优选)?((?:\d+(?:\.\d+)?(?:kg|g|斤|枚|支|个|根)(?:[~～±+\-]\d+(?:\.\d+)?(?:kg|g|斤|枚|支|个|根)?)?(?:\/(?:份|袋|包|盒|箱|个|瓶|板)|[*×xX]\d+(?:袋|包|支)\/(?:包|袋|箱)))(?:\s+\d+(?:\.\d+)?(?:枚|支|袋|包)\/(?:份|袋|包|箱))?)\s*[|丨]?/i;
const PAID=/实付\s*[:：]?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const QUANTITY=/^[xX×]\s*(\d{1,2})$/;
const COUNT=/(展开|收起)\s*[（(]\s*共\s*(\d{1,3})\s*件\s*[）)]/;
const ORDER_ID=/(?:订单编号|订单号)\s*[:：]\s*(PO-[\w-]{8,})/i;
const TOTAL=/(?:先用后付\s*)?实付\s*[:：]?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const MONEY_LIMIT=1500;
const stable=(str)=>createHash('sha256').update(str).digest('hex').slice(0,16);
const compact=t=>String(t||'').replace(/\s+/g,'');
export function similarOrderItemName(a,b){
 const x=compact(a),y=compact(b);
 if(x===y)return true;
 if(Math.min(x.length,y.length)<8)return false;
 if((x.startsWith(y)||y.startsWith(x))&&Math.min(x.length,y.length)>=12)return true;
 const n=x.length,m=y.length;
 if(Math.abs(n-m)>Math.max(n,m)*.2)return false;
 let prev=Array.from({length:m+1},(_,i)=>i);
 for(let i=1;i<=n;i++){
   const curr=[i];
   for(let j=1;j<=m;j++)
     curr[j]=Math.min(curr[j-1]+1,prev[j]+1,prev[j-1]+(x[i-1]===y[j-1]?0:1));
   prev=curr;
 }
 return 1-prev[m]/Math.max(n,m)>=.88;
}

function candidates(lines){
 return lines.filter(l=>l?.box&&l.box.height>1&&l.box.width>1)
  .map(l=>({text:String(l.text||'').trim(),box:l.box}))
  .filter(l=>l.text).sort((a,b)=>a.box.y-b.box.y);
}
export function parseOrderDetailFrame(lines,{height=2400,width=1080}={}){
 if(!Array.isArray(lines))throw new Error('Expected OCR lines');
 const all=candidates(lines);
 const heading=all.some(l=>l.text.includes('订单详情')&&l.box.y<height*.15);
 if(!heading)return {recognized:false,items:[],warning:'not_on_order_detail'};
 const declared=all.map(l=>COUNT.exec(l.text)?.[2]).find(Boolean);
 const orderId=all.map(l=>ORDER_ID.exec(l.text)?.[1]).find(Boolean)||null;
 const idHash=orderId?'sha256:'+stable(orderId):null;
 const totalLines=all.filter(l=>l.box.y>height*.35 &&
    /先用后付/.test(l.text)&&/实付/.test(l.text))
  .map(l=>TOTAL.exec(l.text)?.[1]).filter(Boolean).map(Number);
 const total=totalLines.length===1?totalLines[0]:null;
 const firstFooter=all.find(l=>l.box.y>height*.35&&
  /共优惠|商品总额|支付方式|订单编号|(?:展开|收起)\s*[（(]\s*共/.test(l.text))?.box.y??height;
 const starts=all.filter(l=>l.box.x>width*.16&&l.box.x<width*.76 &&
  l.box.y>height*.10&&l.box.y<firstFooter&&BEGIN.test(compact(l.text)));
 const items=[];
 for(let i=0;i<starts.length;i++){
  const cur=starts[i];
  const token=compact(cur.text),prefix=BEGIN.exec(token);
  const top=cur.box.y;
  const end=Math.min(starts[i+1]?.box.y??firstFooter,firstFooter);
  const region=all.filter(l=>l.box.y>=top-15 && l.box.y<end-12);
  const lead=token.slice(prefix[0].length)
     .replace(/(?:实付\s*[:：]?\s*)?[¥￥]\s*\d+(?:\.\d{1,2})?.*$/,'');
  const continued=region.filter(l=>l!==cur&&l.box.x>width*.16&&l.box.x<width*.77
    &&l.box.y>=top+10&&l.box.y<=top+95
    &&!/^(?:[¥￥]|申请退款|已卖|优惠|实付|x\d)/.test(l.text));
  const name=compact(lead+continued.map(l=>l.text).join(''));
  const explicit=region.flatMap(l=>[...l.text.matchAll(/实付\s*[:：]?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/g)].map(m=>Number(m[1])));
  // In completed orders, rows without an explicit 实付 label show a single
  // ¥ amount. Ignore ambiguous multiple prices rather than guessing.
  const unlabeled=region.flatMap(l=>[...l.text.matchAll(/[¥￥]\s*(\d+(?:\.\d{1,2})?)/g)].map(m=>Number(m[1])));
  const paid=explicit.length?explicit:unlabeled;
  const quantity=region.filter(l=>l.box.x>width*.8)
    .map(l=>QUANTITY.exec(compact(l.text))?.[1]).filter(Boolean);
  const uniqPaid=[...new Set(paid)],uniqQty=[...new Set(quantity.map(Number))];
  const issues=[];
  if(!name)issues.push('missing_name');
  if(uniqPaid.length!==1||uniqPaid[0]>MONEY_LIMIT)issues.push('missing_or_suspicious_price');
  if(uniqQty.length!==1)issues.push('missing_or_ambiguous_quantity');
  const item={name,specification:prefix[1],
    quantity:uniqQty.length===1?uniqQty[0]:null,
    paidUnitPrice:uniqPaid.length===1&&uniqPaid[0]<=MONEY_LIMIT?uniqPaid[0]:null,
    verified:issues.length===0,issues};
  item.identity=stable([item.name,item.specification,item.paidUnitPrice,item.quantity].join('|'));
  items.push(item);
 }
 return {recognized:true,declaredCount:declared?Number(declared):null,
   orderIdHash:idHash,paidTotal:total,items,
   complete:false,warning:'visible_order_detail_only'};
}
export async function collectOrderDetail({device,vision,maxPages=8,waitMs=480}={}){
 if(!device||!vision)throw new Error('Device and vision required');
 if(!Number.isInteger(maxPages)||maxPages<1||maxPages>12)throw new Error('maxPages must be 1..12');
 const {height,width}=await device.size();
 let first=await vision.recognize();
 let original=parseOrderDetailFrame(first.lines,{width,height});
 if(!original.recognized)return {recognized:false,complete:false,items:[],pagesScanned:0,warning:'not_on_order_detail'};
 let count=original.declaredCount;
 let expanded=false;
 const hasExpand=candidates(first.lines).some(l=>COUNT.exec(l.text)?.[1]==='展开');
 if(count!==null && hasExpand){
  const opts=candidates(first.lines).filter(l=>COUNT.exec(l.text)?.[1]==='展开'&&l.box.y>height*.35);
  if(opts.length!==1)throw new Error('Cannot safely identify a unique expand control');
  const b=opts[0].box;
  await device.tap(Math.round(b.x+b.width/2),Math.round(b.y+b.height/2));
  await delay(waitMs);
  first=await vision.recognize();
  const after=parseOrderDetailFrame(first.lines,{width,height});
  if(!after.recognized)throw new Error('Order page disappeared after expand');
  if(candidates(first.lines).some(l=>COUNT.exec(l.text)?.[1]==='展开'))throw new Error('Order expansion not confirmed');
  expanded=true;
 }
 const observed=[];
 let previous=null,pages=0,stopReason='page_limit',collisions=0;
 let orderIdHash=original.orderIdHash;
 let paidTotal=original.paidTotal;
 for(let i=0;i<maxPages;i++){
  const frame=i===0?first:await vision.recognize();
  const parsed=parseOrderDetailFrame(frame.lines,{height,width});
  if(!parsed.recognized){stopReason='lost_detail_page';break;}
  const all=candidates(frame.lines);
  // Exclude the status bar clock from the screen fingerprint.
  const fingerprint=stable(all.filter(l=>l.box.y>height*.1).map(l=>compact(l.text)).join('|'));
  if(fingerprint===previous){stopReason='page_unchanged';break;}
  previous=fingerprint;pages++;
  if(parsed.declaredCount!==null){
    if(count!==null && count!==parsed.declaredCount)
      throw new Error('Declared order item count changed while scanning');
    count=parsed.declaredCount;
  }
  if(parsed.paidTotal!==null){
    if(paidTotal!==null&&Math.abs(paidTotal-parsed.paidTotal)>0.01)
      throw new Error('Order total changed between OCR frames');
    paidTotal=parsed.paidTotal;
  }
  if(parsed.orderIdHash){
    if(orderIdHash && orderIdHash!==parsed.orderIdHash)
      throw new Error('Unexpected order ID changed while scanning');
    orderIdHash=parsed.orderIdHash;
  }
  for(const item of parsed.items){
    const exact=observed.find(prev=>prev.identity===item.identity);
    if(exact){collisions++;continue;}
    const partial=observed.find(prev=>prev.specification===item.specification &&
      similarOrderItemName(prev.name,item.name) &&
      (prev.quantity===null||item.quantity===null||prev.quantity===item.quantity) &&
      (prev.paidUnitPrice===null||item.paidUnitPrice===null||prev.paidUnitPrice===item.paidUnitPrice));
    if(partial){
      // Only combine fragments with matching spec and common name prefix;
      // never merge two distinct, fully verified products.
      partial.name=partial.name.length>=item.name.length?partial.name:item.name;
      partial.paidUnitPrice=partial.paidUnitPrice??item.paidUnitPrice;
      partial.quantity=partial.quantity??item.quantity;
      partial.issues=[
        ...(partial.name?'': ['missing_name']),
        ...(partial.paidUnitPrice===null?['missing_or_suspicious_price']:[]),
        ...(partial.quantity===null?['missing_or_ambiguous_quantity']:[])
      ];
      partial.verified=partial.issues.length===0;
      partial.identity=stable([partial.name,partial.specification,partial.paidUnitPrice,partial.quantity].join('|'));
      collisions++;
    }else observed.push(item);
  }
  if(count!==null && observed.length>=count && observed.every(x=>x.verified)
      && paidTotal!==null && orderIdHash){
    stopReason='declared_count_seen';break;
  }
  if(all.some(l=>/商品总额/.test(l.text)) && i>0){stopReason='order_footer';break;}
  if(i===maxPages-1)break;
  // Short overlapping scrolls keep price and quantity in view together.
  // Large scroll jumps often split a product row across two screenshots,
  // causing a false missing-price or missing-quantity diagnosis.
  await device.swipe(Math.round(width*.72),Math.round(height*.79),
    Math.round(width*.72),Math.round(height*.52),420);
  await delay(waitMs);
 }
 const items=observed;
 const computedTotal=items.every(i=>i.verified && i.paidUnitPrice!==null &&
   Number.isInteger(i.quantity)) ?
   Math.round(items.reduce((sum,i)=>sum+i.paidUnitPrice*i.quantity,0)*100)/100 : null;
 const totalMatched=paidTotal!==null&&computedTotal!==null&&Math.abs(paidTotal-computedTotal)<0.011;
 const complete=count!==null && items.length===count && items.every(i=>i.verified)
   && Boolean(orderIdHash)&&totalMatched;
 return {recognized:true,complete,pagesScanned:pages,expanded,
  orderIdHash,declaredCount:count,items,paidTotal,computedTotal,totalMatched,
  collisions,stopReason,warning:complete?null:'order_items_or_total_not_fully_verified'};
}
