// Trusted ADBKeyboard adapter: temporary IME switch, Unicode broadcast,
// and guaranteed restoration even if the broadcast fails.
const IME='com.android.adbkeyboard/.AdbIME';
export class AdbChineseIme {
  constructor(device,{imeId=IME}={}){this.device=device;this.imeId=imeId;}
  async type(text) {
    if(typeof text!=='string'||!text.trim()||text.length>120)throw new Error('Invalid input text');
    const current=String(await this.device.shell('settings','get','secure','default_input_method')).trim();
    if(!current || current==='null')throw new Error('Cannot determine previous input method; refusing switch');
    const installed=String(await this.device.shell('ime','list','-s')).split(/\r?\n/).map(x=>x.trim());
    if(!installed.includes(this.imeId))throw new Error('ADBKeyboard is not installed or not enabled');
    let switched=false;
    try {
      if(current!==this.imeId) {
        const set=await this.device.shell('ime','set',this.imeId);
        if(!String(set).includes('selected'))throw new Error('IME switch not acknowledged');
        switched=true;
      }
      // Never print or log user-supplied input. Broadcast status isn't proof
      // that text was actually entered; the calling flow verifies via OCR.
      const result=await this.device.shell('am','broadcast','-a','ADB_INPUT_TEXT','--es','msg',text);
      if(!String(result).includes('Broadcast completed'))throw new Error('ADBKeyboard input broadcast not acknowledged');
      return {sent:true,verified:false};
    } finally {
      if(switched) {
        const restored=await this.device.shell('ime','set',current);
        if(!String(restored).includes('selected'))throw new Error('Failed to restore original IME');
      }
    }
  }
}
