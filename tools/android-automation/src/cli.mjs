import {AndroidDevice} from './adb.mjs';
import {ScreenVision} from './vision.mjs';
import {AndroidFlows} from './flows.mjs';
import {AdbChineseIme} from './ime.mjs';

const cmd=process.argv[2]||'help';
const serial=process.env.ANDROID_SERIAL||process.argv.find(x=>x.startsWith('--serial='))?.split('=')[1];
const device=new AndroidDevice({serial});
const vision=new ScreenVision(device);
const flows=new AndroidFlows(device,vision,{typeChinese: text=>new AdbChineseIme(device).type(text)});
const main=async()=>{
  if(cmd==='help')return {commands:['status','inspect','open-wechat','locate <text>','open-mini <name>','search <Chinese query>','plan-cart <JSON>'],transactionalActions:'disabled'};
  const phone=await flows.connect();
  if(cmd==='status')return {phone,foreground:await device.foreground(),size:await device.size()};
  if(cmd==='inspect')return {phone,screen:await vision.inspect()};
  if(cmd==='open-wechat')return flows.openWechat();
  if(cmd==='locate')return vision.locate(process.argv[3]);
  if(cmd==='open-mini')return flows.openMiniProgram(process.argv[3]||'多多买菜');
  if(cmd==='search') {const r=await flows.searchProducts(process.argv[3]);return {query:r.query,verifiedInput:r.verifiedInput,recognizedLines:r.results.lines?.length||0,summary:r.results.text?.slice(0,100)||''};}
  if(cmd==='plan-cart')return flows.prepareCart({items:JSON.parse(process.argv[3]||'[]')});
  throw new Error('Unknown command: '+cmd);
};
try {console.log(JSON.stringify(await main(),null,2));}
catch(e){console.error(JSON.stringify({ok:false,error:e.message}));process.exitCode=1;}
