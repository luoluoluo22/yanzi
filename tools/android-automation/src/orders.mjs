import {createHash} from 'node:crypto';
import {delay} from './adb.mjs';

const DATE=/(20\d{2})[\/年.-](\d{1,2})[\/月.-](\d{1,2})日?/;
const COUNT=/(?:共|商品)\s*(\d{1,3})\s*件/;
const PRICE=/(?:实付|支付|合计|先用后付)?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const TOTAL=/(?:实付|先用后付|订单金额|合计).*?[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const STATUS=/(待评价|已提货|待提货|待支付|待付款|已完成|已取消|正在配货|配送中)/;
const DEFERRED=/(?:自动付款|确认提货后付款|提货后付款|付款)\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/g;
const ORDER_ID=/(?:订单编号|订单号|订单ID)\s*[:：]?\s*([a-z\d-]{8,})/i;
function digest(value){return createHash('sha256').update(value).digest('hex').slice(0,20);}
function texts(lines, height){
 return lines.filter(l=>l?.box && l.box.height>2).sort((a,b)=>a.box.y-b.box.y)
 .map(l=>({text:String(l.text||'').trim(),y:l.box.y,x:l.box.x}))
 .filter(l=>l.y>=0 && l.y<height && l.text);
}
export function extractOrderCards(lines,{height=2400}={}) {
 if(!Array.isArray(lines)||height<200)throw new Error('Invalid order OCR input');
 const seen=texts(lines,height);
 const header=seen.some(l=>/订单列表|全部订单|我的订单/.test(l.text) && l.y<height*.2);
 const tabs=seen.some(l=>/^(全部|待付款|待收货|待评价|退款\/售后)$/.test(l.text) && l.y<height*.3);
 if(!(header&&tabs))return {recognized:false,complete:false,orders:[],warning:'order_list_not_verified'};
 const endOfOrderList=seen.find(l=>l.text.includes('今日特价')&&l.y>height*.3)?.y??height;
 const anchors=seen.filter(l=>DATE.test(l.text) && l.x<height*.15 && l.y<endOfOrderList);
 const orders=[];
 for(let i=0;i<anchors.length;i++){
   const first=anchors[i],until=Math.min(anchors[i+1]?.y??endOfOrderList,endOfOrderList);
   // PDD displays order status to the right of the date at the SAME baseline.
   // OCR can put the status up to 30px above the date, so include the header line.
   const block=seen.filter(l=>l.y>=first.y-35&&l.y<until-25);
   const day=DATE.exec(first.text);
   const date=`${day[1]}-${day[2].padStart(2,'0')}-${day[3].padStart(2,'0')}`;
   const quantities=block.map(l=>COUNT.exec(l.text)?.[1]).filter(Boolean);
   const totals=block.flatMap(l=>[...l.text.matchAll(/(?:实付|订单金额|合计)\s*[:：]?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/g)].map(m=>({amount:Number(m[1]),raw:m[1]})));
   const statuses=block.filter(l=>Math.abs(l.y-first.y)<=90)
      .map(l=>STATUS.exec(l.text)?.[1]).filter(Boolean);
   const ids=block.map(l=>ORDER_ID.exec(l.text)?.[1]).filter(Boolean);
   const suspiciousLargeInteger=totals.length===1 && totals[0].amount>=1000 && !totals[0].raw.includes('.');
   const rawPaid=totals.length===1&&!suspiciousLargeInteger?totals[0].amount:null;
   // Only the current deferred payment order shows 实付 ¥0 now, plus a future
   // auto-debit amount in the line below. Never declare its order total as zero.
   const dueAmounts=block.flatMap(l=>[...l.text.matchAll(DEFERRED)].map(m=>Number(m[1])));
   const uniqueDue=[...new Set(dueAmounts.filter(n=>n>0))];
   const deferred=rawPaid===0 && block.some(l=>l.text.includes('先用后付'));
   const due=deferred&&uniqueDue.length===1?uniqueDue[0]:null;
   const amount=deferred?due:rawPaid;
   const quantity=quantities.length===1?Number(quantities[0]):null;
   const state=[...new Set(statuses)].length===1?statuses[0]:null;
   const orderId=ids.length===1?ids[0]:null;
   const issues=[];
   if(quantity===null)issues.push('missing_or_ambiguous_count');
   if(amount===null)issues.push('missing_or_ambiguous_order_amount');
   if(!state)issues.push('missing_or_ambiguous_status');
   if(suspiciousLargeInteger)issues.push('suspicious_large_integer_price_ocr');
   if(deferred&&due===null)issues.push('deferred_payment_amount_not_verified');
   // Date + quantity + total + status is a provisional identity, not a full
   // order ID. Same-totals orders may collide; report a collision flag.
   const groupKey=[date,quantity,amount,state].join('|');
   const visualHash=digest(block.map(l=>l.text.replace(/\s+/g,'')).join('|'));
   orders.push({date,quantity,amount,status:state,orderId,
      paidShown:rawPaid,deferredPaymentDue:deferred?due:null,
      identity:orderId?'id:'+orderId:'summary:'+groupKey,
      visualHash,verified:issues.length===0,issues});
 }
 // Never dedupe two same-summary orders on this page: they may be distinct.
 const seenKeys=new Set();
 for(const o of orders){
   if(seenKeys.has(o.identity))o.issues.push('identity_collision_requires_order_number');
   seenKeys.add(o.identity);
   o.verified=o.issues.length===0;
 }
 return {recognized:true,complete:false,orders,warning:'visible_page_only_not_all_orders'};
}
export async function collectVisibleOrderPages({device,vision,maxPages=5,waitMs=650,autoExpand=true}={}){
 if(!device||!vision)throw new Error('Device and OCR vision required');
 if(!Number.isInteger(maxPages)||maxPages<1||maxPages>8)throw new Error('maxPages must be 1..8');
 const {width,height}=await device.size();
 const byIdentity=new Map();
 const results=[];
 let prevScreen=null,scanned=0,endMarker=false,stopReason='page_limit';
 let duplicates=0,ambiguousCollisions=0,expanded=false;
 for(let page=0;page<maxPages;page++){
   let frame=await vision.recognize();
   let extracted=extractOrderCards(frame.lines,{height});
   if(!extracted.recognized){
     if(page===0)return {recognized:false,complete:false,pagesScanned:0,orders:[],warning:'order_list_not_verified'};
     stopReason='lost_order_list';break;
   }
   let elements=texts(frame.lines,height);
   const collapsed=elements.filter(x=>/已折叠一周前的订单/.test(x.text));
   if(collapsed.length && autoExpand){
     const expands=elements.filter(x=>x.text==='展开' && collapsed.some(c=>Math.abs(x.y-c.y)<75));
     if(collapsed.length!==1||expands.length!==1 || !frame.lines.some(l=>l.text.trim()==='展开'&&l.box?.x>width*.65)){
       stopReason='collapsed_history_expansion_uncertain';break;
     }
     const target=frame.lines.find(l=>l.text.trim()==='展开' && l.box?.x>width*.65);
     const b=target.box;
     await device.tap(Math.round(b.x+b.width/2),Math.round(b.y+b.height/2));
     await delay(Math.max(300,waitMs));
     frame=await vision.recognize();
     extracted=extractOrderCards(frame.lines,{height});
     elements=texts(frame.lines,height);
     if(!extracted.recognized||elements.some(l=>/已折叠一周前的订单/.test(l.text))){
       stopReason='history_expand_not_verified';break;
     }
     expanded=true;
   } else if(collapsed.length) {
     stopReason='collapsed_history_not_expanded';break;
   }
   const textItems=elements.map(x=>x.text.replace(/\s+/g,''));
   const fingerprint=digest(textItems.join('|'));
   if(fingerprint===prevScreen){stopReason='page_unchanged';break;}
   prevScreen=fingerprint;scanned++;
   for(const item of extracted.orders){
     const identity=item.identity;
     const group=byIdentity.get(identity)||[];
     // A real order number is authoritative across scroll positions, even
     // when only part of the card was visible on an earlier screen.
     const already=group.find(other=>item.orderId || other.visualHash===item.visualHash);
     if(already){
       duplicates++;
       if(item.orderId && !already.verified && item.verified)
         Object.assign(already,item);
       continue;
     }
     // The same date, quantity and amount may belong to two different orders.
     // Without a real order ID, keep BOTH and flag uncertainty.
     if(group.length && !item.orderId){
       ambiguousCollisions++;
       item.issues=[...new Set([...item.issues,'possible_same_order_or_same_total_collision'])];
       item.verified=false;
       for(const older of group){
         older.issues=[...new Set([...older.issues,'possible_same_order_or_same_total_collision'])];
         older.verified=false;
       }
     }
     group.push(item);byIdentity.set(identity,group);results.push(item);
   }
   const inRecommendations=elements.some(x=>x.text==='今日特价' && x.y>height*.3);
   if(textItems.some(x=>/没有更多(?:订单|内容)|已经到底了|到底啦|暂无更多订单/.test(x))){
     endMarker=true;stopReason='end_marker';break;
   }
   if(inRecommendations){stopReason='recommendations_section';break;}
   if(page===maxPages-1)break;
   await device.swipe(Math.round(width*.7),Math.round(height*.78),
     Math.round(width*.7),Math.round(height*.32),450);
   await delay(waitMs);
 }
 const complete=endMarker&&results.length>0&&results.every(x=>x.verified && x.orderId);
 return {recognized:scanned>0,complete,pagesScanned:scanned,
   orders:results,duplicatesIgnored:duplicates,ambiguousCollisions,
   expandedHistory:expanded,stopReason,warning:complete?null:'partial_order_history_please_verify'};
}
