import {AndroidDevice} from './adb.mjs';
import {ScreenVision} from './vision.mjs';
import {AndroidFlows} from './flows.mjs';
import {AdbChineseIme} from './ime.mjs';
import {PddCartAutomation} from './pdd-cart.mjs';
import {collectOrderDetail} from './order-detail.mjs';
import {collectOrderDetailsFromList} from './order-batch.mjs';

const cmd=process.argv[2]||'help';
const serial=process.env.ANDROID_SERIAL||process.argv.find(x=>x.startsWith('--serial='))?.split('=')[1];
const device=new AndroidDevice({serial});
const vision=new ScreenVision(device);
const flows=new AndroidFlows(device,vision,{typeChinese: (text,options)=>new AdbChineseIme(device).type(text,options)});
const main=async()=>{
  if(cmd==='help')return {commands:['status','inspect','open-wechat','locate <text>','open-mini <name>','search <Chinese query>','products','compare <brand> <max¥Per500g>','plan-cart <JSON>','cart-inspect','order-detail','order-batch','cart-cycle-test --ack-test-mutation'],transactionalActions:'disabled'};
  const phone=await flows.connect();
  if(cmd==='status')return {phone,foreground:await device.foreground(),size:await device.size()};
  if(cmd==='inspect')return {phone,screen:await vision.inspect()};
  if(cmd==='open-wechat')return flows.openWechat();
  if(cmd==='locate')return vision.locate(process.argv[3]);
  if(cmd==='open-mini')return flows.openMiniProgram(process.argv[3]||'多多买菜');
  if(cmd==='search') {const r=await flows.searchProducts(process.argv[3]);return {query:r.query,verifiedInput:r.verifiedInput,loaded:r.loaded,cards:r.cards.map(({name,brand,weightG,price,unitPrice500g,verified,issues})=>({name,brand,weightG,price,unitPrice500g,verified,issues}))};}
  if(cmd==='products') return flows.findProduct('');
  if(cmd==='compare'){const brand=process.argv[3];const maxUnitPrice500g=Number(process.argv[4]);if(!brand||!Number.isFinite(maxUnitPrice500g))throw new Error('Brand and unit price required');return flows.compareProducts({brand,maxUnitPrice500g});}
  if(cmd==='cart-inspect') return (await new PddCartAutomation(device,vision).ensureCartOpen());
  if(cmd==='order-detail') return collectOrderDetail({device,vision,maxPages:8});
  if(cmd==='order-batch') return collectOrderDetailsFromList({device,vision,maxOrders:2,maxScrolls:5});
  if(cmd==='cart-cycle-test'){
    if(!process.argv.includes('--ack-test-mutation'))throw new Error('Mutation test requires explicit --ack-test-mutation');
    return new PddCartAutomation(device,vision).testCycle({target:{name:'宸欢白砂糖(一级带嘴)',weightG:468},maxPrice:6,approved:true});
  }
  if(cmd==='plan-cart')return flows.prepareCart({items:JSON.parse(process.argv[3]||'[]')});
  throw new Error('Unknown command: '+cmd);
};
try {console.log(JSON.stringify(await main(),null,2));}
catch(e){console.error(JSON.stringify({ok:false,error:e.message}));process.exitCode=1;}
