import {delay} from './adb.mjs';
import {UnsafeTargetError} from './vision.mjs';
import {extractCartRows,cartKey,verifyCartChange} from './cart.mjs';
import {extractProductCards} from './products.mjs';
import {runVerifiedCartAction} from './transaction.mjs';

const TARGET_APP='com.tencent.mm';
function clean(value){return String(value||'').replace(/\s+/g,'');}
function goodResult(frame){
  return frame.lines.some(l=>l.box && l.box.y<frame.image?.height*.2 && /综合|销量/.test(l.text));
}
export class PddCartAutomation {
  constructor(device,vision,{maxCartReads=3}={}){
    this.device=device;this.vision=vision;this.maxCartReads=maxCartReads;
  }
  async appGuard(){
    const focus=await this.device.foreground();
    if(!focus.includes(TARGET_APP)||!focus.includes('AppBrand'))throw new UnsafeTargetError('Wechat mini-program is not foreground');
  }
  async cartSnapshot(){
    await this.appGuard();
    const frame=await this.vision.recognize();
    const snapshot=extractCartRows(frame.lines,await this.device.size());
    return {snapshot,frame};
  }
  async ensureCartOpen(){
    const first=await this.cartSnapshot();
    if(first.snapshot.verified)return first.snapshot;
    if(first.snapshot.visible)throw new UnsafeTargetError('Cart panel is visible but not fully verified');
    const size=await this.device.size();
    // The payment strip shows both a main CTA and a second line about
    // deferred payment. Only the main CTA identifies the cart bar.
    const cta=first.frame.lines.filter(l=>l.box&&l.box.y>size.height*.88&&
      /^\d+(?:\.\d+)?元下单$/.test(clean(l.text)));
    if(cta.length!==1)throw new UnsafeTargetError('Shopping cart main bottom CTA is not unique');
    const y=Math.round(cta[0].box.y+cta[0].box.height/2);
    // This is the empty/filled cart icon to the LEFT of the verified bottom CTA.
    await this.device.tap(Math.round(size.width*.073),y);
    await delay(450);
    for(let i=0;i<this.maxCartReads;i++){
      const read=await this.cartSnapshot();
      if(read.snapshot.verified)return read.snapshot;
      await delay(350);
    }
    throw new UnsafeTargetError('Cart open but no complete verified inventory');
  }
  async closeCart(){
    const initial=await this.cartSnapshot();
    if(!initial.snapshot.verified)throw new UnsafeTargetError('Cart is not verified before closing');
    await this.device.back();
    await delay(400);
    const after=await this.cartSnapshot();
    if(after.snapshot.visible)throw new UnsafeTargetError('Cart did not close');
  }
  async readCards(){
    await this.appGuard();
    const frame=await this.vision.recognize();
    const size=await this.device.size();
    const cards=extractProductCards(frame.lines,size);
    if(!frame.text.includes('综合')||cards.length===0)throw new UnsafeTargetError('Product results page not ready');
    return cards;
  }
  async add({target,maxPrice=Infinity}={}){
    if(!target?.name||!Number.isFinite(target.weightG)||!(maxPrice>0))throw new Error('Specific product identity and max price required');
    const before=await this.ensureCartOpen();
    const key=cartKey(target);
    if(before.rows.some(r=>cartKey(r)===key))throw new UnsafeTargetError('Target already in cart; refusing duplicate-test add');
    await this.closeCart();
    const cards=await this.readCards();
    const choices=cards.filter(c=>c.verified&&c.weightG===target.weightG&&c.name===target.name&&
      c.price<=maxPrice&&c.action?.label==='加入购物车');
    if(choices.length!==1)throw new UnsafeTargetError('Cannot identify exactly one safe add button');
    const chosen=choices[0];
    const action=async()=>{
      await this.appGuard();
      const fresh=await this.readCards();
      const renewed=fresh.filter(c=>c.verified&&c.name===chosen.name&&c.weightG===chosen.weightG&&c.price===chosen.price&&c.action?.label==='加入购物车');
      if(renewed.length!==1)throw new UnsafeTargetError('Product card changed before action');
      await this.device.tap(renewed[0].action.x,renewed[0].action.y);
      await delay(450);
      // No action is repeated even if opening the cart fails.
      await this.ensureCartOpen();
    };
    let reads=0;
    const readSnapshot=async()=>reads++===0?before:await this.ensureCartOpen();
    return runVerifiedCartAction({type:'add',target,readSnapshot,perform:action});
  }
  async remove({target}={}){
    if(!target?.name||!Number.isFinite(target.weightG))throw new Error('Specific target required');
    const before=await this.ensureCartOpen();
    const row=before.rows.filter(r=>cartKey(r)===cartKey(target));
    if(row.length!==1||row[0].quantity!==1||!row[0].quantityBox)
      throw new UnsafeTargetError('Only one exactly identified quantity-one row may be removed');
    const act=async()=>{
      await this.appGuard();
      // Derive the minus control from the verified quantity control in its own row.
      const size=await this.device.size();
      const refreshed=await this.ensureCartOpen();
      const matches=refreshed.rows.filter(r=>cartKey(r)===cartKey(target));
      if(matches.length!==1||matches[0].quantity!==1)throw new UnsafeTargetError('Cart row changed before remove');
      const q=matches[0].quantityBox;
      const cx=q.x+q.width/2;
      const x=Math.round(cx-size.width*.077);
      const y=Math.round(q.y+q.height/2);
      if(x<size.width*.6||x>=cx-35||y<=matches[0].region.top||y>=matches[0].region.bottom)
        throw new UnsafeTargetError('Minus control not confidently inside the target row');
      await this.device.tap(x,y);
      await delay(250);
      const modal=await this.vision.recognize();
      const prompt=modal.lines.some(l=>clean(l.text).includes('确认删除该商品'));
      const buttons=modal.lines.filter(l=>clean(l.text)==='确认删除');
      if(!prompt||buttons.length!==1)throw new UnsafeTargetError('Delete confirmation dialogue not verified');
      // Only confirm this specific modal; never tap the cart's "全部删除".
      const b=buttons[0].box;
      await this.device.tap(Math.round(b.x+b.width/2),Math.round(b.y+b.height/2));
      await delay(450);
    };
    let reads=0;
    const readSnapshot=async()=>reads++===0?before:await this.ensureCartOpen();
    return runVerifiedCartAction({type:'remove',target,readSnapshot,perform:act});
  }
  // Non-atomic real-UI transaction: add new first, verify it, then remove old.
  // If the second stage fails, keep both and report partial; never roll back
  // by guessing, retrying or placing an order.
  async replaceCartItem({from,to,maxPrice,approved=false}={}){
    if(approved!==true)throw new UnsafeTargetError('Explicit replacement approval is required');
    if(!Number.isFinite(maxPrice)||maxPrice<=0)throw new Error('Positive maximum item price required');
    const oldKey=cartKey(from),newKey=cartKey(to);
    if(oldKey===newKey)throw new UnsafeTargetError('Replacement must be a different product identity');
    const before=await this.ensureCartOpen();
    const prior=before.rows.filter(r=>cartKey(r)===oldKey);
    if(prior.length!==1||prior[0].quantity!==1)
      throw new UnsafeTargetError('Original item not uniquely identified with quantity one');
    if(before.rows.some(r=>cartKey(r)===newKey))
      throw new UnsafeTargetError('Replacement item already exists in cart');
    const added=await this.add({target:to,maxPrice});
    if(added.status!=='confirmed')return {status:'uncertain',stage:'add',added,policy:'no_retry'};
    const removed=await this.remove({target:from});
    if(removed.status!=='confirmed')return {status:'partial',stage:'remove',added,removed,policy:'manual_reconcile'};
    const after=await this.ensureCartOpen();
    const wanted=after.rows.find(r=>cartKey(r)===newKey);
    const obsolete=after.rows.find(r=>cartKey(r)===oldKey);
    if(!wanted||wanted.quantity!==1||obsolete||before.outOfStockCount!==after.outOfStockCount)
      return {status:'uncertain',stage:'final_verification',policy:'manual_reconcile'};
    const protectedRows=before.rows.filter(r=>cartKey(r)!==oldKey);
    if(after.rows.length!==protectedRows.length+1||
      protectedRows.some(row=>{
        const curr=after.rows.find(r=>cartKey(r)===cartKey(row));
        return !curr||curr.quantity!==row.quantity||curr.price!==row.price;
      }))return {status:'uncertain',stage:'unrelated_cart_change',policy:'manual_reconcile'};
    return {status:'confirmed',from:oldKey,to:newKey,added:added.verification,removed:removed.verification,unavailablePreserved:after.outOfStockCount};
  }
  async testCycle({target,maxPrice,approved=false}={}){
    if(approved!==true)throw new UnsafeTargetError('Explicit test-cycle authorization required');
    const baseline=await this.ensureCartOpen();
    if(baseline.rows.some(r=>cartKey(r)===cartKey(target)))throw new UnsafeTargetError('Target already existed; will not interfere');
    const added=await this.add({target,maxPrice});
    if(added.status!=='confirmed')return {status:'uncertain',step:'add',added};
    const removed=await this.remove({target});
    if(removed.status!=='confirmed')return {status:'uncertain',step:'remove',added,removed};
    const final=await this.ensureCartOpen();
    // Verify all original rows and unavailable products are restored.
    if(final.outOfStockCount!==baseline.outOfStockCount||final.rows.length!==baseline.rows.length)
      throw new UnsafeTargetError('Original cart not restored after test');
    for(const old of baseline.rows){
      const row=final.rows.find(r=>cartKey(r)===cartKey(old));
      if(!row||row.quantity!==old.quantity||row.price!==old.price)
        throw new UnsafeTargetError('Original cart contents changed');
    }
    return {status:'confirmed',added:added.verification,removed:removed.verification,restored:true,unavailablePreserved:final.outOfStockCount};
  }
}
