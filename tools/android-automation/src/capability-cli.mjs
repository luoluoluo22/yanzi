import {AndroidDevice} from './adb.mjs';
import {ScreenVision} from './vision.mjs';
import {AndroidFlows} from './flows.mjs';
import {PddCartAutomation} from './pdd-cart.mjs';
import {AdbChineseIme} from './ime.mjs';
import {extractOrderCards,collectVisibleOrderPages} from './orders.mjs';
import {UnsafeTargetError} from './vision.mjs';
import {collectOrderDetail} from './order-detail.mjs';

const allowed=new Set(['android.device.status','pdd.product.search','pdd.cart.inspect','pdd.orders.preview','pdd.orders.collect','pdd.orders.detail']);
const operation=process.argv[2];
const encoded=process.argv[3]||'e30=';
async function main(){
 if(!allowed.has(operation))throw new Error('Capability not allowed: '+String(operation));
 if(encoded.length>8192||!/^[A-Za-z0-9+/]*={0,2}$/.test(encoded))throw new Error('Invalid payload encoding');
 const input=JSON.parse(Buffer.from(encoded,'base64').toString('utf8'));
 if(!input||typeof input!=='object'||Array.isArray(input))throw new Error('Expected object payload');
 const device=new AndroidDevice({serial:process.env.ANDROID_SERIAL||undefined});
 const phone=await device.ensureConnected();
 const {serial,state,model}=phone;
 if(operation==='android.device.status')return {ok:true,device:{serial,state,model},foreground:await device.foreground(),size:await device.size()};
 const focus=await device.foreground();
 if(!focus.includes('com.tencent.mm/')||!focus.includes('AppBrand'))throw new Error('微信小程序当前未在前台；请先打开多多买菜');
 const vision=new ScreenVision(device);
 if(operation==='pdd.product.search'){
  if(Object.keys(input).join()!=='query'||typeof input.query!=='string'||!input.query.trim()||input.query.length>80)throw new Error('query required (1-80 characters)');
  const flows=new AndroidFlows(device,vision,{typeChinese:(text,opts)=>new AdbChineseIme(device).type(text,opts)});
  const result=await flows.searchProducts(input.query.trim());
  return {ok:true,query:result.query,loaded:result.loaded,products:result.cards.map(({name,brand,weightG,price,unitPrice500g,verified,issues})=>({name,brand,weightG,price,unitPrice500g,verified,issues}))};
 }
 if(operation==='pdd.orders.collect') {
    const keys=Object.keys(input);
    if(keys.some(key=>!['maxPages','openIfNeeded'].includes(key)))throw new Error('Unknown order collection options');
    if(input.openIfNeeded!==undefined&&typeof input.openIfNeeded!=='boolean')throw new Error('openIfNeeded must be boolean');
    const maxPages=input.maxPages===undefined?5:input.maxPages;
    if(!Number.isInteger(maxPages)||maxPages<1||maxPages>8)throw new Error('maxPages must be 1..8');
    if(input.openIfNeeded===true) {
      const flows=new AndroidFlows(device,vision);
      await flows.openOrderHistory({attemptOnce:true});
    }
    const collected=await collectVisibleOrderPages({device,vision,maxPages});
    return {ok:true,...collected};
 }
 if(operation==='pdd.orders.detail'){
    if(Object.keys(input).some(k=>k!=='maxPages'))throw new Error('Unsupported detail options');
    const maxPages=input.maxPages===undefined?8:input.maxPages;
    if(!Number.isInteger(maxPages)||maxPages<1||maxPages>12)throw new Error('maxPages must be 1..12');
    const detail=await collectOrderDetail({device,vision,maxPages});
    return {ok:true,...detail};
 }
 if(Object.keys(input).length)throw new Error('Unexpected arguments for read-only capability');
 if(operation==='pdd.cart.inspect'){
  const pdd=new PddCartAutomation(device,vision);
  let snapshot;
  try {snapshot=await pdd.ensureCartOpen();}
  catch (error){
    if(error instanceof UnsafeTargetError)return {ok:true,recognized:false,verified:false,products:[],reason:'cart_not_identifiable_on_current_page'};
    throw error;
  }
  return {ok:true,recognized:true,verified:snapshot.verified,outOfStockCount:snapshot.outOfStockCount,
    products:snapshot.rows.map(({name,weightG,price,quantity,verified})=>({name,weightG,price,quantity,verified}))};
 }
 if(operation==='pdd.orders.preview'){
  const recognized=await vision.recognize();
  const parsed=extractOrderCards(recognized.lines,{height:(await device.size()).height});
  return {ok:true,...parsed};
 }
}
function printJson(value) {
  // ASCII JSON survives hosts configured with a legacy Windows code page.
  // console.log writes one real newline (rather than a literal backslash-n).
  const ascii = JSON.stringify(value).replace(/[\u007f-\uffff]/g, c => '\\u'+c.charCodeAt(0).toString(16).padStart(4,'0'));
  console.log(ascii);
}
try {
  printJson(await main());
} catch (error) {
  printJson({ok:false,error:String(error?.message||error).slice(0,350)});
  process.exitCode=1;
}
