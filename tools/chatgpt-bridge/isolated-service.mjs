// Second bridge instance for an independently authenticated Edge user-data-dir.
// Shares bridge implementation, but never main bridge socket, token or state.
import {join} from 'node:path';
import {createBridge} from './server.mjs';
const directory=join(process.env.LOCALAPPDATA||process.env.HOME,
  'OpenQuickHost','ExtensionStorage','chatgpt-agent-bridge');
const port=53922;
const bridge=await createBridge({port,directory});
console.log('YANZI_AGENT_BRIDGE_STARTED='+bridge.origin);
for(const signal of ['SIGINT','SIGTERM']){
  process.on(signal,async()=>{await bridge.close();process.exit(0);});
}
