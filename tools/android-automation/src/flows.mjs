import {delay} from './adb.mjs';
import {UnsafeTargetError} from './vision.mjs';

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
  async searchProducts(query,{openSearch=true}={}) {
    if(!query?.trim())throw new Error('Empty query');
    if(openSearch)await this.vision.tapText('搜索',{exact:true,waitMs:500});
    // The search CTA leads to a page whose input is not automatically focused.
    // Focus the placeholder explicitly; never broadcast text to an unknown field.
    await this.vision.tapText('搜索您要的商品',{exact:false,waitMs:250});
    // No implicit pinyin: the actual visible Chinese query must be verified.
    if(!this.typeChinese)throw new Error('Chinese input adapter is not configured; cannot safely search products');
    await this.typeChinese(query,{device:this.device,vision:this.vision});
    const typed=await this.vision.recognize();
    const normalized=(s)=>String(s).replace(/\\s+/g,'');
    const located=typed.lines.some(line=>line.box && line.box.y<240 && normalized(line.text).includes(normalized(query)));
    if(!located)throw new UnsafeTargetError('Chinese search field was not verified: refusing to submit');
    await this.vision.tapText('搜索',{exact:true,waitMs:800});
    return {query,verifiedInput:!!located,results:await this.vision.recognize()};
  }
  async findProduct(query) {
    const scan=await this.vision.recognize();
    return {query,matches:scan.lines.filter(l=>l.text.includes(query)).map(l=>({text:l.text,box:l.box})),snapshotText:scan.text};
  }
  async openCart() {
    await this.vision.tapText('购物车');
    await this.vision.waitFor('购物车');
    return this.readCart();
  }
  async readCart() {
    const screen=await this.vision.recognize();
    return {text:screen.text,lines:screen.lines,verified:screen.text.includes('购物车')||screen.text.includes('已选')};
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
