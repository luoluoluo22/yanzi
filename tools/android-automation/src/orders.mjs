// Strict, read-only OCR parsing of the visible PDD order list.
// A missing header or missing date/amount/count makes a card incomplete,
// and orders from other pages must never be inferred.
const DATE=/(20\d{2})[\/年.-](\d{1,2})[\/月.-](\d{1,2})日?/;
const COUNT=/(?:共|商品)(\d{1,3})件/;
const PRICE=/(?:实付|支付|合计|先用后付)?\s*[¥￥]\s*(\d+(?:\.\d{1,2})?)/;
const STATUS=/(待评价|已提货|待提货|待支付|待付款|已完成|已取消|正在配货|配送中)/;
export function extractOrderCards(lines,{height=2400}={}){
 if(!Array.isArray(lines)||height<200)throw new Error('Invalid order OCR input');
 const seen=lines.filter(l=>l.box&&l.box.height>2).sort((a,b)=>a.box.y-b.box.y)
 .map(l=>({text:String(l.text).trim(),y:l.box.y,x:l.box.x})).filter(l=>l.y>=0&&l.y<height);
 const header=seen.some(l=>/全部订单|我的订单|订单列表/.test(l.text));
 const tabs=seen.some(l=>/(全部|待提货|已提货|待评价)/.test(l.text)&&l.y<height*.38);
 const validPage=header&&tabs;
 if(!validPage) return {recognized:false,complete:false,orders:[],warning:'order_list_not_verified'};
 const anchors=seen.filter(l=>DATE.test(l.text));
 const orders=[];
 for(let i=0;i<anchors.length;i++){
   const first=anchors[i],until=anchors[i+1]?.y??height;
   const block=seen.filter(l=>l.y>=first.y&&l.y<until);
   const day=DATE.exec(first.text);
   const date=`${day[1]}-${day[2].padStart(2,'0')}-${day[3].padStart(2,'0')}`;
   const quantities=block.map(l=>COUNT.exec(l.text)?.[1]).filter(Boolean);
   const sums=block.map(l=>PRICE.exec(l.text)?.[1]).filter(Boolean);
   const statuses=block.map(l=>STATUS.exec(l.text)?.[1]).filter(Boolean);
   // Avoid interpreting prices of multiple goods as order-total.
   // Only an explicit '实付' or '先用后付' line can be chosen when multiple prices exist.
   const totals=block.map(l=>/(?:实付|先用后付|订单金额|合计).*?[¥￥]\s*(\d+(?:\.\d{1,2})?)/.exec(l.text)?.[1]).filter(Boolean);
   const amounts=totals.length===1?totals:(sums.length===1?sums:[]);
   const quantity=quantities.length===1?Number(quantities[0]):null;
   const amount=amounts.length===1?Number(amounts[0]):null;
   const state=statuses[0]??null;
   const issues=[];
   if(quantity===null)issues.push('missing_or_ambiguous_count');
   if(amount===null)issues.push('missing_or_ambiguous_paid_amount');
   if(!state)issues.push('missing_status');
   orders.push({date,quantity,amount,status:state,verified:issues.length===0,issues});
 }
 const uniq=new Map();
 for(const o of orders){
   const key=[o.date,o.amount,o.quantity,o.status].join('|');
   if(!uniq.has(key))uniq.set(key,o);
 }
 return {recognized:true,complete:false,orders:[...uniq.values()],warning:'visible_page_only_not_all_orders'};
}
