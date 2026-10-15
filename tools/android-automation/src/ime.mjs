import {delay} from './adb.mjs';

// ADBKeyboard is installed on the Redmi K70 but changing the active IME
// invalidates the existing WeChat input connection. Re-focus AFTER switching,
// then inspect the actual entered text BEFORE restoring the original IME.
const IME='com.android.adbkeyboard/.AdbIME';
export class AdbChineseIme {
  constructor(device,{imeId=IME}={}){this.device=device;this.imeId=imeId;}
  async type(text,{focus,verify,clearExisting=false}={}) {
    if(typeof text!=='string'||!text.trim()||text.length>120)throw new Error('Invalid input text');
    if(typeof focus!=='function'||typeof verify!=='function')throw new Error('Focus and verify callbacks are required');
    const previous=String(await this.device.shell('settings','get','secure','default_input_method')).trim();
    if(!previous||previous==='null')throw new Error('Previous input method unknown');
    const installed=String(await this.device.shell('ime','list','-s')).split(/\r?\n/).map(x=>x.trim());
    if(!installed.includes(this.imeId))throw new Error('ADBKeyboard unavailable');
    let switched=false;
    try {
      if(previous!==this.imeId){
        const status=await this.device.shell('ime','set',this.imeId);
        if(!String(status).includes('selected'))throw new Error('IME switch not acknowledged');
        switched=true;
      }
      // Switching IME drops the old input connection: never focus before this.
      await focus();
      await delay(180);
      if(clearExisting){
        const cleared=await this.device.shell('am','broadcast','-a','ADB_CLEAR_TEXT');
        if(!String(cleared).includes('Broadcast completed'))throw new Error('Clear request not acknowledged');
        await delay(220);
      }
      const result=await this.device.shell('am','broadcast','-a','ADB_INPUT_TEXT','--es','msg',text);
      if(!String(result).includes('Broadcast completed'))throw new Error('Input broadcast not acknowledged');
      await delay(250);
      const verified=await verify(text);
      if(!verified)throw new Error('Search text not visible in focused field');
      return {sent:true,verified:true};
    } finally {
      if(switched) {
        const restored=await this.device.shell('ime','set',previous);
        if(!String(restored).includes('selected'))throw new Error('Original IME could not be restored');
      }
    }
  }
}
