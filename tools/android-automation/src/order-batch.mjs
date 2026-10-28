import {createHash} from 'node:crypto';
import {delay} from './adb.mjs';
import {extractOrderCards} from './orders.mjs';
import {collectOrderDetail,parseOrderDetailFrame} from './order-detail.mjs';

// All operations are navigation and read-only. No checkout, reorder,
// deletion, refunds, pickup confirmation or cart mutation.
const DATE=/20\d{2}[\/年.-]\d{1,2}[\/月.-]\d{1,2}/;
const fingerprint=(lines)=>createHash('sha256').update(
 lines.filter(x=>x?.box?.y>310).map(x=>String(x.text||'').trim()).join('|')
).digest('hex').slice(0,16);
function candidates(frame,{width,height}){
 const parsed=extractOrderCards(frame.lines,{height});
 if(!parsed.recognized)return {recognized:false,items:[]};
 const anchors=frame.lines.filter(l=>l?.box && DATE.test(l.text)&&l.box.x<width*.33
  && l.box.y>height*.15 && l.box.y<height*.94).sort((a,b)=>a.box.y-b.box.y);
 if(anchors.length!==parsed.orders.length)
  return {recognized:true,items:[],warning:'ambiguous_order_date_anchors'};
 return {recognized:true,items:parsed.orders.map((order,i)=>({
  order,top:anchors[i].box.y,
  // The product image row lies immediately below this date; never tap
  // near bottom action buttons (delete/reorder/confirm pickup).
  tapY:Math.round(anchors[i].box.y+Math.max(80,Math.min(135,height*.06)))
 }))};
}
export async function collectOrderDetailsFromList({
  device,vision,maxOrders=2,maxScrolls=5,waitMs=500,detailMaxPages=8
}={}){
 if(!device||!vision)throw new Error('Device and vision required');
 if(!Number.isInteger(maxOrders)||maxOrders<1||maxOrders>5)throw new Error('maxOrders must be 1..5');
 if(!Number.isInteger(maxScrolls)||maxScrolls<1||maxScrolls>12)throw new Error('maxScrolls must be 1..12');
 const size=await device.size(),{width,height}=size;
 const results=[],attempted=new Set(),knownIds=new Set();
 let previous=null,scrolled=0,stopReason='list_page_limit',sawList=false;
 for(let page=0;page<maxScrolls && results.length<maxOrders;page++){
  const frame=await vision.recognize();
  const list=candidates(frame,size);
  if(!list.recognized){
   stopReason=page===0?'not_on_order_list':'lost_order_list';break;
  }
  sawList=true;
  const key=fingerprint(frame.lines);
  if(previous===key){stopReason='list_not_moving';break;}
  previous=key;
  if(list.warning){stopReason=list.warning;break;}
  for(const entry of list.items){
   if(results.length>=maxOrders)break;
   const summary=entry.order;
   if(attempted.has(summary.identity))continue;
   // A coupon tooltip can obscure the order count or amount in the list.
   // Opening an order is safe only if its date and completed status are
   // unambiguous on THIS screen. Detail verification remains independent.
   if(!summary.date || !summary.status)continue;
   if(list.items.filter(e=>e.order.date===summary.date).length!==1)continue;
   // Read-only verification should not touch pending pickup/payment cards.
   // Their action buttons are high-stakes; prefer completed historical orders.
   if(!['已提货','已完成','待评价'].includes(summary.status))continue;
   if(entry.tapY>height*.79 || entry.tapY<height*.2)continue;
   attempted.add(summary.identity);
   const x=Math.round(width*.33),y=entry.tapY;
   // One navigation tap; never retry it when the next page is uncertain.
   await device.tap(x,y);
   // WeChat may navigate asynchronously. Poll screenshots for a bounded
   // period; NEVER tap the order again to compensate for stale frames.
   let opened=null;
   for(let check=0;check<6;check++){
    await delay(Math.max(180,waitMs));
    const frame=await vision.recognize();
    if(parseOrderDetailFrame(frame.lines,size).recognized){opened=frame;break;}
   }
   if(!opened){
    stopReason='order_detail_did_not_open';
    return {recognized:true,complete:false,orders:results,
      attempted:attempted.size,scrolled,stopReason};
   }
   let detail,readFailure=null;
   try{
    detail=await collectOrderDetail({device,vision,maxPages:detailMaxPages,waitMs});
   }catch(e){readFailure=String(e.message||e).slice(0,180);}
   // Only send a single Back once we have verified the detail page.
   let restored=false;
   try {
    await device.back();
    for(let check=0;check<6;check++){
     await delay(Math.max(180,waitMs));
     const returned=await vision.recognize();
     if(extractOrderCards(returned.lines,{height}).recognized){restored=true;break;}
    }
   } catch (error) {
    stopReason='return_navigation_unavailable';
    return {recognized:true,complete:false,orders:results,
      attempted:attempted.size,scrolled,stopReason};
   }
   if(!restored){
    stopReason='failed_to_restore_order_list';
    return {recognized:true,complete:false,orders:results,
      attempted:attempted.size,scrolled,stopReason};
   }
   if(readFailure){
    results.push({date:summary.date,status:summary.status,quantity:summary.quantity,
      amount:summary.amount,complete:false,reason:'detail_error:'+readFailure,items:[]});
   }else{
    const amountMatched=Number.isFinite(summary.amount) && detail.paidTotal!==null
      ? Math.abs(detail.paidTotal-summary.amount)<.011 : null;
    const uniqueId=Boolean(detail.orderIdHash)&&!knownIds.has(detail.orderIdHash);
    if(detail.orderIdHash)knownIds.add(detail.orderIdHash);
    results.push({
      date:summary.date,status:summary.status,quantity:summary.quantity,
      amount:summary.amount,orderIdHash:detail.orderIdHash,
      summaryVerified:summary.verified,
      detailComplete:detail.complete,
      complete:detail.complete&&uniqueId&&amountMatched!==false,
      amountMatched,items:detail.items,
      declaredCount:detail.declaredCount,paidTotal:detail.paidTotal,
      reason:!uniqueId?'duplicate_or_missing_order_identity':
        amountMatched===false?'list_detail_amount_mismatch':
        !summary.verified?'list_summary_partially_obscured':detail.warning
    });
   }
   // The page must be freshly re-read after returning, since positions
   // may have changed. Never click old coordinates in a changed list.
   break;
  }
  if(results.length>=maxOrders){stopReason='requested_order_count';break;}
  if(page===maxScrolls-1)break;
  await device.swipe(Math.round(width*.72),Math.round(height*.78),
   Math.round(width*.72),Math.round(height*.33),430);
  scrolled++;
  await delay(waitMs);
 }
 return {recognized:sawList,complete:false,orders:results,
  attempted:attempted.size,scrolled,stopReason,
  warning:'batch_is_bounded_not_a_complete_account_history'};
}
