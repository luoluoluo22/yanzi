import {readFile} from 'node:fs/promises';
import path from 'node:path';

// Connects ONLY to the installed Yanzi Agent API on loopback. Never uploads
// screenshots to the internet, and never logs or persists its local token.
export class LocalOcrClient {
  constructor({settingsFile=process.env.YANZI_SETTINGS_FILE || path.join(process.env.LOCALAPPDATA||'', 'OpenQuickHost','appsettings.local.json'),fetchImpl=globalThis.fetch}={}) {
    this.settingsFile=settingsFile;this.fetchImpl=fetchImpl;
  }
  async request(route,payload,timeout=150000) {
    const settings=JSON.parse((await readFile(this.settingsFile,'utf8')).replace(/^\uFEFF/,''));
    const port=settings.agentApiPort, token=settings.agentApiToken;
    if(!Number.isSafeInteger(port)||port<1||port>65535||typeof token!=='string'||!token)throw new Error('Yanzi local OCR settings unavailable');
    const url='http://127.0.0.1:'+port+route;
    const response=await this.fetchImpl(url,{method:'POST',headers:{'X-Yanzi-Token':token,'Content-Type':'application/json; charset=utf-8'},body:JSON.stringify(payload),signal:AbortSignal.timeout(timeout)});
    if(!response.ok)throw new Error('Yanzi OCR HTTP '+response.status);
    const data=await response.json();
    if(data?.success===false)throw new Error('Yanzi OCR returned failure');
    return data;
  }
}
