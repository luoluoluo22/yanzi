import {normalizeText} from './products.mjs';
import {UnsafeTargetError} from './vision.mjs';

const WEIGHT=/(\d+(?:\.\d+)?)\s*(kg|g|斤)\s*\/\s*(包|袋|盒|份|板|箱)/i;
const PRICE=/[¥￥]\s*(\d+(?:\.\d{1,2})?)/g;
const STOP_NAME=/^(?:已卖|已抢|好评|疯抢|刚刚|配送|已售|仅剩|券后|满\d|品牌|用户|[¥￥]|精选|正品|细腻|口感|口味|分量|售出|库存|已卖|已抢)/;
function grams(value,unit){return Number(value)*(unit.toLowerCase()==='kg'?1000:unit==='斤'?500:1);}
export function cartKey(row){
  if(!row||!row.name||!Number.isFinite(row.weightG)||row.weightG<=0)throw new Error('Invalid cart row identity');
  return normalizeText(row.name).toLowerCase()+'@'+row.weightG;
}
const blank=(warning)=>({visible:false,verified:false,complete:false,rows:[],outOfStockCount:null,warnings:[warning]});

export function extractCartRows(lines,{width=1080,height=2400}={}){
  if(!Array.isArray(lines)||width<200||height<200)throw new Error('Invalid cart OCR data');
  const all=lines.filter(l=>l.box&&l.box.width>0&&l.box.height>0)
    .map(l=>({...l,s:normalizeText(l.text)})).sort((a,b)=>a.box.y-b.box.y);
  const header=all.find(l=>l.s==='购物车');
  const unavailable=all.find(l=>/^下架商品[（(]?共\d+件/.test(l.s));
  const outOfStockCount=unavailable?Number(/共(\d+)件/.exec(unavailable.s)?.[1]):0;
  const pay=all.find(l=>l.s==='立即支付' && l.box.y>height*.72);
  const bottomSelect=all.some(l=>l.s==='全选'||/^已选\d+$/.test(l.s));
  const full=Boolean(pay&&bottomSelect);
  if(!header&&!unavailable)return blank('cart_panel_header_not_found');
  if(!full)return blank('cart_footer_not_visible');
  const startY=header?.box.y ?? unavailable.box.y;
  const footerY=unavailable?.box.y ?? pay.box.y;
  const starts=all.filter(l=>l.box.x>width*.26&&l.box.y>startY&&l.box.y<footerY
    &&WEIGHT.test(l.s)&&l.s.length<120);
  const rows=[];
  for(let i=0;i<starts.length;i++){
    const first=starts[i],match=WEIGHT.exec(first.s),top=first.box.y,bottom=starts[i+1]?.box.y??footerY;
    const pieces=all.filter(l=>l.box.y>=top-3 && l.box.y<bottom-2);
    const continuation=pieces.filter(l=>l!==first && l.box.x>width*.26&&l.box.x<width*.85 &&
      l.box.y>top+3&&l.box.y<=top+110&&l.s.length<=90&&!STOP_NAME.test(l.s)
      &&!/^[（(]?已卖|优惠|\d+[.万千]/.test(l.s));
    const title=first.s.slice(match.index+match[0].length).replace(/^[|丨]/,'');
    const name=title+continuation.map(l=>l.s).join('');
    const prices=pieces.flatMap(l=>[...l.s.matchAll(PRICE)].map(m=>({value:Number(m[1]),line:l})))
      .filter(p=>p.value>0&&p.line.box.x>width*.26);
    const distinct=[...new Set(prices.map(p=>p.value))];
    const offers=prices.filter(p=>/券后|实付|优惠价/.test(p.line.s));
    const price=distinct.length===1?distinct[0]:(offers.length===1?offers[0].value:null);
    const qtyCandidates=pieces.filter(l=>l.box.x>width*.73 && /^\d{1,2}$/.test(l.s)
      && l.box.y>top+40 && l.box.y<Math.min(bottom-2,top+320));
    const qty=qtyCandidates.length===1?Number(qtyCandidates[0].s):null;
    const issues=[];
    if(name.length<2)issues.push('missing_name');
    if(price===null)issues.push('ambiguous_price');
    if(!Number.isInteger(qty)||qty<1)issues.push('ambiguous_quantity');
    const weightG=grams(match[1],match[2]);
    if(!weightG)issues.push('weight_unrecognized');
    rows.push({name,weightG,price,quantity:qty,verified:issues.length===0,issues,
      quantityBox:qtyCandidates.length===1?qtyCandidates[0].box:null,
      evidence:{title:[first.text,...continuation.map(l=>l.text)],priceLines:prices.map(p=>p.line.text),quantityLines:qtyCandidates.map(l=>l.text)},
      region:{top,bottom}});
  }
  const ids=rows.filter(r=>r.name&&r.weightG>0).map(cartKey);
  const duplicates=ids.length!==new Set(ids).size;
  const safeEmpty=rows.length===0 && Boolean(unavailable)&&!header;
  const complete=Boolean(unavailable)||safeEmpty;
  const warnings=[];
  if(duplicates)warnings.push('duplicate_product_keys');
  if(!complete)warnings.push('missing_in_stock_section_end');
  if(rows.length===0&&!safeEmpty)warnings.push('empty_state_not_proven');
  const verified=Boolean(full&&complete&&!duplicates&&(safeEmpty||rows.every(r=>r.verified)));
  return {visible:true,verified,complete,rows,outOfStockCount,warnings};
}
function checked(snapshot){
  if(!snapshot?.visible||!snapshot.verified||!snapshot.complete)
    throw new UnsafeTargetError('Cart is not fully verified; cannot infer mutation outcome');
  const out=new Map();
  for(const row of snapshot.rows){
    const k=cartKey(row);
    if(out.has(k))throw new UnsafeTargetError('Duplicate product identity');
    out.set(k,row);
  }
  return out;
}
export function verifyCartChange(before,after,{type,target,delta=1}={}){
  if(!['add','remove'].includes(type)||!target)throw new Error('Expected add/remove and target');
  if(!Number.isInteger(delta)||delta<=0)throw new Error('Invalid quantity delta');
  const b=checked(before),a=checked(after);
  if(before.outOfStockCount!==after.outOfStockCount)
    throw new UnsafeTargetError('Unavailable cart items changed');
  const key=cartKey(target),old=b.get(key)?.quantity||0,newQty=a.get(key)?.quantity||0;
  if(type==='add'&&newQty!==old+delta)throw new UnsafeTargetError('Target cart quantity did not increase exactly');
  if(type==='remove'&&newQty!==old-delta)throw new UnsafeTargetError('Target cart quantity did not decrease exactly');
  for(const [k,row] of b){
    if(k===key)continue;
    const current=a.get(k);
    if(!current||current.quantity!==row.quantity||current.price!==row.price)
      throw new UnsafeTargetError('Unrelated cart row changed');
  }
  for(const k of a.keys())if(k!==key&&!b.has(k))throw new UnsafeTargetError('Unexpected cart row added');
  return {verified:true,type,key,from:old,to:newQty,unavailablePreserved:true};
}
