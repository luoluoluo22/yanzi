import {createHash} from 'node:crypto';
import {delay} from './adb.mjs';

const DATE=/(20\d{2})[\/年.-](\d{1,2})[\/月.-](\d{1,2})日?/;
const COUNT=/(?:共|商品)\s*(\d{1,3})\s*件/;
const PRICE=/(?:实付|支付|合计|先用后付)?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const TOTAL=/(?:实付|先用后付|订单金额|合计).*?[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const STATUS=/(待评价|已提货|待提货|待支付|待付款|已完成|已取消|正在配货|配送中)/;
const ORDER_ID=/(?:订单编号|订单号|订单ID)\s*[:：]?\s*([a-z\d-]{8,})/i;
function digest(value){return createHash('sha256').update(value).digest('hex').slice(0,20);}
function texts(lines, height){
 return lines.filter(l=>l?.box && l.box.height>2).sort((a,b)=>a.box.y-b.box.y)
 .map(l=>({text:String(l.text||'').trim(),y:l.box.y,x:l.box.x}))
 .filter(l=>l.y>=0 && l.y<height && l.text);
}
export function extractOrderCards(lines,{height=2400}={}){
 if(!Array.isArray(lines)||height<200)throw new Error('Invalid order OCR input');
 const seen=texts(lines,height);
 const header=seen.some(l=>/全部订单|我的订单|订单列表/.test(l.text) && l.y < height*.3);
 const tabs=seen.some(l=>/(全部|待提货|已提货|待评价)/.test(l.text) && l.y<height*.38);
 if(!(header&&tabs))return {recognized:false,complete:false,orders:[],warning:'order_list_not_verified'};
 const anchors=seen.filter(l=>DATE.test(l.text));
 const orders=[];
 for(let i=0;i<anchors.length;i++){
   const first=anchors[i],until=anchors[i+1]?.y??height;
   const block=seen.filter(l=>l.y>=first.y && l.y<until);
   const day=DATE.exec(first.text);
   const date=`${day[1]}-${day[2].padStart(2,'0')}-${day[3].padStart(2,'0')}`;
   const quantities=block.map(l=>COUNT.exec(l.text)?.[1]).filter(Boolean);
   const sums=block.map(l=>PRICE.exec(l.text)?.[1]).filter(Boolean);
   const statuses=block.map(l=>STATUS.exec(l.text)?.[1]).filter(Boolean);
   const totals=block.map(l=>TOTAL.exec(l.text)?.[1]).filter(Boolean);
   const amounts=totals.length===1?totals:(sums.length===1?sums:[]);
   const quantity=quantities.length===1?Number(quantities[0]):null;
   const amount=amounts.length===1?Number(amounts[0]):null;
   const state=statuses.length===1?statuses[0]:null;
   const ids=block.map(l=>ORDER_ID.exec(l.text)?.[1]).filter(Boolean);
   const orderId=ids.length===1?ids[0]:null;
   const issues=[];
   if(quantity===null)issues.push('missing_or_ambiguous_count');
   if(amount===null)issues.push('missing_or_ambiguous_paid_amount');
   if(!state)issues.push('missing_or_ambiguous_status');
   // Distinct orders may share exactly the same date, amount and quantity.
   // A visual hash uses ALL visible text; never dedupe just on purchase totals.
   const visualHash=digest(block.map(l=>l.text.replace(/\s+/g,'')).join('|'));
   orders.push({date,quantity,amount,status:state,orderId,
      identity:orderId?'id:'+orderId:'visual:'+visualHash,
      verified:issues.length===0,issues});
 }
 const uniq=new Map();
 for(const item of orders)if(!uniq.has(item.identity))uniq.set(item.identity,item);
 return {recognized:true,complete:false,orders:[...uniq.values()],warning:'visible_page_only_not_all_orders'};
}
export async function collectVisibleOrderPages({device,vision,maxPages=5,waitMs=650}={}){
 if(!device||!vision)throw new Error('Device and OCR vision required');
 if(!Number.isInteger(maxPages)||maxPages<1||maxPages>8)throw new Error('maxPages must be 1..8');
 const screen=await device.size();
 const width=screen.width,height=screen.height;
 const unique=new Map();
 let prevScreen=null,scanned=0,endMarker=false,stopReason='page_limit',duplicates=0;
 for(let page=0;page<maxPages;page++){
   const frame=await vision.recognize();
   const extracted=extractOrderCards(frame.lines,{height});
   if(!extracted.recognized){
     if(page===0)return {recognized:false,complete:false,pagesScanned:0,orders:[],warning:'order_list_not_verified'};
     stopReason='lost_order_list';break;
   }
   const textItems=texts(frame.lines,height).map(x=>x.text.replace(/\s+/g,''));
   const fingerprint=digest(textItems.join('|'));
   if(fingerprint===prevScreen){stopReason='page_unchanged';break;}
   prevScreen=fingerprint;scanned++;
   for(const item of extracted.orders){
     if(unique.has(item.identity)){duplicates++;continue;}
     unique.set(item.identity,item);
   }
   if(textItems.some(x=>/没有更多(?:订单|内容)|已经到底了|到底啦|暂无更多订单/.test(x))){
     endMarker=true;stopReason='end_marker';break;
   }
   if(page===maxPages-1)break;
   await device.swipe(Math.round(width*.7),Math.round(height*.78),
     Math.round(width*.7),Math.round(height*.32),450);
   await delay(waitMs);
 }
 const orders=[...unique.values()];
 const allIdentified=orders.length>0&&orders.every(x=>x.verified);
 // A list end marker is necessary but not sufficient: orders lacking unique
 // IDs may still be indistinguishable, and we cannot assert total coverage.
 const complete=endMarker&&allIdentified&&orders.every(x=>x.orderId);
 return {recognized:scanned>0,complete,pagesScanned:scanned,
   orders,duplicatesIgnored:duplicates,stopReason,
   warning:complete?null:'partial_order_history_please_verify'};
}
