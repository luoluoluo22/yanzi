import {delay} from './adb.mjs';
import {UnsafeTargetError} from './vision.mjs';
import {extractProductCards,chooseProduct} from './products.mjs';
import {extractCartRows} from './cart.mjs';
import {extractOrderCards} from './orders.mjs';

const MINI='多多买菜';
export class AndroidFlows {
  constructor(device,vision,{typeChinese=null}={}) {this.device=device;this.vision=vision;this.typeChinese=typeChinese;}
  async connect() {return this.device.ensureConnected();}
  async openWechat() {
    await this.connect();
    const focus=await this.device.foreground();
    if(!focus.includes('com.tencent.mm'))await this.device.openApp('com.tencent.mm');
    await delay(950);
    return {opened:true,foreground:await this.device.foreground()};
  }
  async openMiniProgram(name=MINI,{maxScroll=4}={}) {
    await this.openWechat();
    try {await this.vision.waitFor(name,{timeoutMs:1700});}
    catch {
      // Only attempt pull-down from a verified main WeChat state. If another mini-app
      // is foreground, caller must return there explicitly.
      const home=await this.vision.recognize();
      if(!(/微信|通讯录|发现|我/.test(home.text)))throw new UnsafeTargetError('WeChat main page not verified. Do not swipe blindly');
      const {width,height}=await this.device.size();
      await this.device.swipe(width*.52,height*.21,width*.52,height*.68,510);
      await delay(500);
    }
    for(let i=0;i<=maxScroll;i++){
      try {
        await this.vision.tapText(name,{exact:false});
        await this.vision.waitFor('多多买菜',{timeoutMs:15000});
        return {opened:true,name,scrolls:i};
      } catch(e) {
        if(!(e instanceof UnsafeTargetError)) throw e;
        if(i===maxScroll)throw new UnsafeTargetError('Mini program not located: '+name);
        const {width,height}=await this.device.size();
        await this.device.swipe(width*.5,height*.75,width*.5,height*.32,360);
      }
    }
  }
  // A search button on the home page may jump straight to cached results.
  // Navigate by the actual screen rather than assuming a fixed sequence.
  async ensureSearchEntry() {
    for(let step=0;step<4;step++) {
      const screen=await this.vision.recognize();
      const header=screen.lines.filter(l=>l.box && l.box.y>=95 && l.box.y<215);
      const searchButton=header.some(l=>l.text.trim()==='搜索' && l.box.x>450);
      const field=header.find(l=>l.box.x>=75 && l.box.x<540 && l.text.trim()!=='搜索' && l.text.trim());
      if(searchButton && field) return {screen,field};
      if(searchButton){
        // Search button exists, but no OCR-visible field: refuse to guess.
        throw new UnsafeTargetError('Search input field not identified');
      }
      if(field) {
        const box=field.box;
        await this.device.tap(Math.round(box.x+box.width/2),Math.round(box.y+box.height/2));
      } else {
        throw new UnsafeTargetError('Cannot locate search entry safely');
      }
      await delay(400);
    }
    throw new UnsafeTargetError('Search entry state not reached');
  }
  async searchProducts(query,{openSearch=true}={}) {
    if(typeof query!=='string'||!query.trim())throw new Error('Empty query');
    if(!this.typeChinese)throw new Error('Chinese input adapter is not configured; cannot safely search products');
    // Safe alternative for callers starting from the home page.
    if(openSearch) {
      const first=await this.vision.recognize();
      const top=first.lines.filter(l=>l.box && l.box.y>=95 && l.box.y<215);
      const button=top.find(l=>l.text.trim()==='搜索' && l.box.x>450);
      const field=top.find(l=>l.box.x>=75 && l.box.x<540 && l.text.trim()!=='搜索');
      if(button && !field) await this.vision.tapText('搜索',{exact:true,waitMs:400});
    }
    await this.ensureSearchEntry();
    const focus=async()=>{
      const {field}=await this.ensureSearchEntry();
      const box=field.box;
      await this.device.tap(Math.round(box.x+box.width/2),Math.round(box.y+box.height/2));
    };
    const verify=async wanted=>{
      const typed=await this.vision.recognize();
      return typed.lines.some(l=>{
        if(!l.box || l.box.y<95 || l.box.y>=215 || l.box.x<75 || l.box.x>=540)return false;
        const found=l.text.replace(/\s+/g,'');
        const target=wanted.replace(/\s+/g,'');
        // PP-OCR can include the search magnifying-glass glyph as "Q".
        return found===target || (found.endsWith(target)&&found.length-target.length<=2);
      });
    };
    await this.typeChinese(query,{focus,verify,clearExisting:true});
    await this.vision.tapText('搜索',{exact:true,waitMs:850});
    let results=await this.vision.recognize();
    if(!results.text.includes('综合') && !results.text.includes('销量'))throw new UnsafeTargetError('Search results page not verified');
    let cards=[];
    for(let i=0;i<5;i++){
      const size=await this.device.size();
      cards=extractProductCards(results.lines,size);
      if(cards.length)break;
      await delay(550);
      results=await this.vision.recognize();
    }
    return {query,verifiedInput:true,results,cards,loaded:cards.length>0};
  }
  async findProduct(query) {
    const scan=await this.vision.recognize();
    const size=await this.device.size();
    const cards=extractProductCards(scan.lines,size);
    return {query,cards:cards.filter(c=>!query||c.name.includes(query)||c.brand===query),scanned:cards.length};
  }
  async compareProducts(constraints={}) {
    const scan=await this.vision.recognize();
    const cards=extractProductCards(scan.lines,await this.device.size());
    return {candidates:cards,choice:chooseProduct(cards,constraints)};
  }
  async openOrderHistory({attemptOnce=true}={}) {
    const size=await this.device.size();
    const current=await this.vision.recognize();
    if(extractOrderCards(current.lines,{height:size.height}).recognized)
      return {recognized:true,navigated:false};
    if(!attemptOnce)throw new UnsafeTargetError('Not on a verified order list');
    const top=current.lines.filter(line=>line?.box && line.box.y>size.height*.065
      && line.box.y<size.height*.15
      && /订单/.test(line.text)
      && line.box.x>size.width*.5);
    if(top.length!==1)throw new UnsafeTargetError('Order navigation shortcut is not uniquely identifiable');
    const box=top[0].box;
    await this.device.tap(Math.round(box.x+box.width/2),Math.round(box.y+box.height/2));
    for(let i=0;i<5;i++){
      await delay(500);
      const next=await this.vision.recognize();
      if(extractOrderCards(next.lines,{height:size.height}).recognized)
        return {recognized:true,navigated:true};
    }
    throw new UnsafeTargetError('Order shortcut tapped but order list did not open');
  }
  async openCart() {
    await this.vision.tapText('购物车');
    await this.vision.waitFor('购物车');
    return this.readCart();
  }
  async readCart() {
    const screen=await this.vision.recognize();
    const snapshot=extractCartRows(screen.lines,await this.device.size());
    return {text:screen.text,snapshot,verified:snapshot.verified};
  }
  // Only executed on an exact unique product label. Final cart state is re-read
  // and MUST be separately verified; tapping '+' is not proof of success.
  async addProduct() {
    throw new UnsafeTargetError('Live addProduct is disabled pending verified product-row and quantity matching');
  }
  async removeProduct({name}={}) {
    // Fail closed: currently the UI's minus/delete controls are unlabeled
    // and position-based deletion risks removing the wrong product.
    throw new UnsafeTargetError('Safe removal requires a cart row identity + before/after quantity assertion: '+(name||'unknown'));
  }
  async prepareCart({items=[],dryRun=true}={}) {
    if(!Array.isArray(items)||items.some(i=>!i.name))throw new Error('Invalid item list');
    const history=[];
    for(const item of items) {
      if(dryRun){history.push({item:item.name,action:'plan',executed:false});continue;}
      throw new UnsafeTargetError('Batch cart mutation disabled pending visual price/variant and quantity guard');
    }
    return {dryRun,history};
  }
  async submitOrder(){throw new UnsafeTargetError('Ordering requires an independent verified approval transaction; disabled in this P0/P1 release');}
}
