import {AndroidDevice} from './adb.mjs';
import {ScreenVision} from './vision.mjs';
import {AndroidFlows} from './flows.mjs';
import {PddCartAutomation} from './pdd-cart.mjs';
import {AdbChineseIme} from './ime.mjs';
import {extractOrderCards} from './orders.mjs';
import {UnsafeTargetError} from './vision.mjs';

const allowed=new Set(['android.device.status','pdd.product.search','pdd.cart.inspect','pdd.orders.preview']);
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
try{
 const result=await main();
 process.stdout.write(JSON.stringify(result)+'\n');
}catch(error){process.stdout.write(JSON.stringify({ok:false,error:String(error?.message||error).slice(0,350)})+'\n');process.exitCode=1;}
