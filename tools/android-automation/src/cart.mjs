import {normalizeText} from './products.mjs';
import {UnsafeTargetError} from './vision.mjs';

const WEIGHT=/(\d+(?:\.\d+)?)\s*(kg|g|斤)\s*\/\s*(包|袋|盒|份|板|箱)/i;
const PRICE=/[¥￥]\s*(\d+(?:\.\d{1,2})?)/g;
function grams(value,unit){return Number(value)*(unit.toLowerCase()==='kg'?1000:unit==='斤'?500:1);}
export function cartKey(row) {
  if(!row||!row.name||!Number.isFinite(row.weightG))throw new Error('Invalid cart row identity');
  return normalizeText(row.name).toLowerCase()+'@'+row.weightG;
}
export function extractCartRows(lines,{width=1080,height=2400}={}) {
  if(!Array.isArray(lines)||width<200||height<200)throw new Error('Invalid cart OCR data');
  const all=lines.filter(l=>l.box&&l.box.width>0&&l.box.height>0)
    .map(l=>({...l,s:normalizeText(l.text)})).sort((a,b)=>a.box.y-b.box.y);
  const anchor=all.find(l=>l.s==='购物车'||l.s==='已选');
  if(!anchor)return {visible:false,verified:false,rows:[],warnings:['cart_header_not_found']};
  const startY=anchor.box.y;
  const stops=all.filter(l=>l.box.y>startY&&/立即支付|先用后付|确认提货后付款|去结算/.test(l.s));
  const footerY=stops.length?Math.min(...stops.map(l=>l.box.y)):height;
  const starts=all.filter(l=>l.box.x>width*.26&&l.box.y>startY&&l.box.y<footerY&&WEIGHT.test(l.s)&&l.s.length<120);
  const rows=[];
  for(let i=0;i<starts.length;i++){
    const first=starts[i],match=WEIGHT.exec(first.s);
    const top=first.box.y, bottom=starts[i+1]?.box.y??footerY;
    const pieces=all.filter(l=>l.box.y>=top-2&&l.box.y<bottom-3);
    const head=pieces.filter(l=>l.box.x>width*.26&&l.box.y<=top+100);
    const joined=head.map(l=>l.s).join('');
    const suffix=first.s.slice(match.index+match[0].length).replace(/^[|丨]/,'');
    const name=suffix+head.filter(l=>l!==first).map(l=>l.s).join('');
    const parsedPrices=pieces.flatMap(l=>[...l.s.matchAll(PRICE)].map(m=>({value:Number(m[1]),line:l}))).filter(p=>p.value>0);
    // A crossed-out original and a payable price need explicit discount/context
    // verification; without it, avoid inventing which is the current price.
    const distinctPrices=[...new Set(parsedPrices.map(p=>p.value))];
    const sale=parsedPrices.filter(p=>/券后|实付|优惠价/.test(p.line.s));
    const price=distinctPrices.length===1?distinctPrices[0]:(sale.length===1?sale[0].value:null);
    const numbers=pieces.filter(l=>l.box.x>width*.66&&/^\d{1,2}$/.test(l.s)&&l.box.y>top+45);
    const uniqueQty=[...new Set(numbers.map(n=>Number(n.s)))];
    const quantity=uniqueQty.length===1&&numbers.length===1?uniqueQty[0]:null;
    const issues=[];
    if(!name)issues.push('missing_name');
    if(price===null)issues.push('ambiguous_price');
    if(quantity===null||quantity<1)issues.push('ambiguous_quantity');
    const weightG=grams(match[1],match[2]);
    rows.push({name,weightG,price,quantity,verified:issues.length===0,issues,
      evidence:{title:head.map(l=>l.text),priceLines:parsedPrices.map(p=>p.line.text),quantityLines:numbers.map(l=>l.text)},
      region:{top,bottom}});
  }
  const ids=rows.filter(r=>r.name).map(cartKey);
  const duplicates=ids.length!==new Set(ids).size;
  const explicitlyEmpty=all.some(l=>/购物车空空如也|购物车是空的|还没有商品/.test(l.s));
  const verified=(!duplicates)&&((rows.length>0&&rows.every(r=>r.verified))||(rows.length===0&&explicitlyEmpty));
  return {visible:true,verified,rows,warnings:duplicates?['duplicate_product_keys']:[]};
}
function normalized(snapshot) {
  if(!snapshot?.visible||!snapshot.verified)throw new UnsafeTargetError('Cart is not fully verified; cannot infer mutation outcome');
  const out=new Map();
  for(const row of snapshot.rows){
    const k=cartKey(row);
    if(out.has(k))throw new UnsafeTargetError('Duplicate product identity');
    out.set(k,row);
  }
  return out;
}
export function verifyCartChange(before,after,{type,target,delta=1}={}) {
  if(!['add','remove'].includes(type)||!target)throw new Error('Expected add/remove and target');
  if(!Number.isInteger(delta)||delta<=0)throw new Error('Invalid quantity delta');
  const b=normalized(before),a=normalized(after);
  const targetKey=cartKey(target),old=b.get(targetKey)?.quantity||0,newQty=a.get(targetKey)?.quantity||0;
  if(type==='add'&&newQty!==old+delta)throw new UnsafeTargetError('Target cart quantity did not increase exactly');
  if(type==='remove'&&newQty!==old-delta)throw new UnsafeTargetError('Target cart quantity did not decrease exactly');
  for(const [key,oldRow] of b){
    if(key===targetKey)continue;
    if(a.get(key)?.quantity!==oldRow.quantity)throw new UnsafeTargetError('Unrelated cart row changed');
  }
  for(const key of a.keys())if(key!==targetKey&&!b.has(key))throw new UnsafeTargetError('Unexpected cart row added');
  return {verified:true,type,key:targetKey,from:old,to:newQty};
}
