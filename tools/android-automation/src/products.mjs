import {UnsafeTargetError} from './vision.mjs';

const SKU_SPEC=/(\d+(?:\.\d+)?)\s*(kg|g|斤)\s*\/\s*(包|袋|盒|份|板|箱|瓶|罐)/i;
const MONEY=/[¥￥]\s*(\d+(?:\.\d{1,2})?)/g;
const ACTION=/^(加入购物车|立即抢购|马上抢购|去购买)$/;
const PRICE_CONTEXT=/(券后|秒杀|限量|限时|特价|¥|￥)/;

export function normalizeText(value) {
  return String(value||'').replace(/\s+/g,'').replace(/[｜]/g,'|');
}
export function yuanPer500g(price,weightG) {
  if(!Number.isFinite(price)||price<=0||!Number.isFinite(weightG)||weightG<=0)throw new Error('Price and weight must be positive');
  return Math.round((price*500/weightG)*100)/100;
}
function weightOf(match) {
  const weight=Number(match[1]);
  return match[2].toLowerCase()==='kg'?weight*1000:match[2]==='斤'?weight*500:weight;
}
export function extractProductCards(lines,{width=1080,height=2400}={}) {
  if(!Array.isArray(lines)||!Number.isFinite(width)||!Number.isFinite(height))throw new Error('Invalid OCR frame');
  const rightStart=width*.48;
  const normalized=lines.map((line,index)=>({
    ...line,index,content:normalizeText(line.text),box:line.box,
  })).filter(l=>l.box&&l.box.width>1&&l.box.height>1&&l.box.y>height*.11&&l.box.x<width);
  const starts=normalized.filter(l=>l.box.x>=rightStart && SKU_SPEC.test(l.content) && l.content.length<=100)
    .sort((a,b)=>a.box.y-b.box.y);
  const cards=[];
  for(let i=0;i<starts.length;i++){
    const first=starts[i];
    const spec=SKU_SPEC.exec(first.content);
    const next=starts[i+1];
    const top=first.box.y;
    const bottom=next ? next.box.y : height;
    const row=normalized.filter(l=>l.box.y>=top-8 && l.box.y<bottom-8);
    const main=row.filter(l=>l.box.x>=rightStart && l.box.y<top+115 && !PRICE_CONTEXT.test(l.content));
    // The first OCR line contains both spec and product name. A second text
    // line is frequently the rest of the name (e.g. 正新原 + 味爆汁烤肠).
    const nameLines=[first,...main.filter(l=>l!==first)].sort((a,b)=>a.box.y-b.box.y);
    const rawName=nameLines.map(l=>l.content).join('');
    const firstTail=first.content.substring(spec.index+spec[0].length).replace(/^[|丨]/,'');
    const title=(firstTail+nameLines.slice(1).map(l=>l.content).join('')).trim();
    const prices=row.flatMap(l=>[...l.content.matchAll(MONEY)].map(m=>({
      value:Number(m[1]),line:l,offer:/券后|秒杀|限量|特价/.test(l.content)
    }))).filter(p=>Number.isFinite(p.value)&&p.value>0);
    const unique=new Map(prices.map(p=>[p.value,p]));
    const offers=[...unique.values()].filter(p=>p.offer);
    const chosen=unique.size===1?[...unique.values()][0]:offers.length===1?offers[0]:null;
    const actions=row.filter(l=>l.box.x>=width*.62 && ACTION.test(l.content));
    const action=actions.length===1?actions[0]:null;
    const brand=/正新|安井|三全|小牛凯西|龙大|双汇|金锣|雨润/.exec(rawName)?.[0]||null;
    const grams=weightOf(spec);
    const issues=[];
    if(!title)issues.push('missing_name');
    if(!chosen)issues.push('ambiguous_or_missing_price');
    if(!action)issues.push('ambiguous_or_missing_action');
    if(!Number.isFinite(grams)||grams<=0)issues.push('invalid_weight');
    cards.push({
      name:title,brand,weightG:grams,packageUnit:spec[3],
      price:chosen?.value??null,
      priceLabel:chosen?.line.content??null,
      unitPrice500g:chosen?yuanPer500g(chosen.value,grams):null,
      action:action?{label:action.content,x:Math.round(action.box.x+action.box.width/2),y:Math.round(action.box.y+action.box.height/2)}:null,
      bounds:{top,bottom:Math.min(bottom,height)},
      verified:issues.length===0,
      issues,
      evidence:{nameLines:nameLines.map(l=>l.text),priceText:chosen?.line.text??null,actionText:action?.text??null}
    });
  }
  return cards;
}
export function chooseProduct(cards,{brand,maxUnitPrice500g,weightG,allowPromotions=true}={}) {
  if(!Array.isArray(cards))throw new Error('Expected product candidates');
  const choices=cards.filter(c=>c.verified && (!brand||c.brand===brand)&&
    (weightG===undefined||c.weightG===weightG) &&
    (maxUnitPrice500g===undefined||c.unitPrice500g<=maxUnitPrice500g) &&
    (allowPromotions||!/券后|秒杀|限量|特价/.test(c.priceLabel||'')));
  choices.sort((a,b)=>a.unitPrice500g-b.unitPrice500g);
  return choices[0]||null;
}
