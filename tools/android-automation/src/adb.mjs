import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {mkdtemp, readFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';

const run = promisify(execFile);
export const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));
export function decodeXml(s) {
  return String(s).replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&').replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(Number(n)));
}
export function parseUiDump(xml) {
  const nodes = [];
  for (const match of xml.matchAll(/<node\s+([^>]*?)\/?\s*>/g)) {
    const attrs = {};
    for (const item of match[1].matchAll(/([\w-]+)="([^"]*)"/g)) attrs[item[1]] = decodeXml(item[2]);
    const m = /\[(\d+),(\d+)\]\[(\d+),(\d+)\]/.exec(attrs.bounds ?? '');
    if (!m) continue;
    const [left, top, right, bottom] = m.slice(1).map(Number);
    nodes.push({text: attrs.text || '', description: attrs['content-desc'] || '', resourceId: attrs['resource-id'] || '', packageName: attrs.package || '', clickable: attrs.clickable === 'true', enabled: attrs.enabled !== 'false', bounds: {x:left,y:top,width:right-left,height:bottom-top}});
  }
  return nodes;
}
export class AndroidDevice {
  constructor({serial, adbPath=process.env.ADB_PATH||'adb', execute=run}={}) {
    this.serial = serial; this.adbPath = adbPath; this.execute = execute;
  }
  async call(args, {timeout=15000, binary=false}={}) {
    const params = this.serial ? ['-s', this.serial, ...args] : args;
    const {stdout, stderr} = await this.execute(this.adbPath, params, {timeout, maxBuffer:24*1024*1024, encoding:binary?'buffer':'utf8', windowsHide:true});
    if (String(stderr||'').includes('more than one device')) throw new Error('Multiple ADB devices: choose a serial');
    return stdout;
  }
  async devices() {
    const stdout = await this.execute(this.adbPath, ['devices','-l'], {timeout:10000, windowsHide:true});
    return String(stdout.stdout).split(/\r?\n/).slice(1).map(s=>s.trim()).filter(s=>s && !s.startsWith('*')).map(s=>{
      const [serial,state] = s.split(/\s+/);
      return {serial,state, model:/model:(\S+)/.exec(s)?.[1]||''};
    });
  }
  async ensureConnected() {
    const online=(await this.devices()).filter(d=>d.state==='device');
    const matches=online.filter(d=>!this.serial||d.serial===this.serial);
    if(matches.length!==1) throw new Error(matches.length===0?'Android device unavailable':'Multiple phones connected: select serial explicitly');
    this.serial=matches[0].serial; return matches[0];
  }
  async shell(...args) {return this.call(['shell',...args]);}
  async foreground() {
    const out=await this.shell('dumpsys','window');
    return /mCurrentFocus=([^\r\n]+)/.exec(out)?.[1] || /mFocusedApp=([^\r\n]+)/.exec(out)?.[1] || '';
  }
  async size() {
    const out=await this.shell('wm','size');
    const m=/(?:Override size|Physical size):\s*(\d+)x(\d+)/g;
    const all=[...out.matchAll(m)];
    const chosen=all.find(x=>x[0].startsWith('Override'))||all[0];
    if(!chosen) throw new Error('Cannot detect screen dimensions');
    return {width:+chosen[1],height:+chosen[2]};
  }
  async tap(x,y) {
    if(!Number.isFinite(x)||!Number.isFinite(y)||x<0||y<0) throw new Error('Invalid tap coordinates');
    return this.shell('input','tap',String(Math.round(x)),String(Math.round(y)));
  }
  async swipe(x,y,x2,y2,ms=350) {
    if([x,y,x2,y2].some(n=>!Number.isFinite(n)||n<0)) throw new Error('Invalid swipe');
    return this.shell('input','swipe',...([x,y,x2,y2,ms].map(n=>String(Math.round(n)))));
  }
  async back(){return this.shell('input','keyevent','KEYCODE_BACK');}
  async key(key){if(!/^(?:KEYCODE_[A-Z0-9_]+|\d+)$/.test(String(key)))throw new Error('Invalid keycode');return this.shell('input','keyevent',String(key));}
  async openApp(pkg='com.tencent.mm'){if(!/^[a-zA-Z][a-zA-Z0-9_.]+$/.test(pkg))throw new Error('Invalid package');return this.shell('monkey','-p',pkg,'-c','android.intent.category.LAUNCHER','1');}
  async typeAscii(text) {
    if(!/^[\x20-\x7e]+$/.test(text))throw new Error('ADB ASCII input only; Chinese requires an IME text adapter');
    // No shell invocation, so meta characters are argv rather than host shell commands.
    return this.shell('input','text',text.replace(/ /g,'%s'));
  }
  async screenshot() {return this.call(['exec-out','screencap','-p'],{timeout:18000,binary:true});}
  async withScreenshotFile(fn) {
    const dir=await mkdtemp(path.join(tmpdir(),'yanzi-adb-'));
    const file=path.join(dir,'screen.png');
    try {
      const {writeFile}=await import('node:fs/promises');
      await writeFile(file,await this.screenshot());
      return await fn(file);
    } finally {await rm(dir,{recursive:true,force:true});}
  }
  async uiTree() {
    const remote='/sdcard/window.xml';
    await this.shell('uiautomator','dump',remote);
    const xml=await this.shell('cat',remote);
    return parseUiDump(xml);
  }
}
