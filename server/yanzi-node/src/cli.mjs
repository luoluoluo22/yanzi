import {loadConfig} from './config.mjs';
import {ServerCapabilityRuntime} from './capabilities.mjs';

const config = loadConfig();
const runtime = new ServerCapabilityRuntime({workspaceRoot: config.workspaceRoot});
const command = process.argv[2] || 'status';

if (command === 'status') {
  process.stdout.write(JSON.stringify(await runtime.invoke('server.status.get'), null, 2) + '\n');
} else if (command === 'capabilities') {
  process.stdout.write(JSON.stringify(runtime.catalog(), null, 2) + '\n');
} else {
  process.stderr.write('Usage: node src/cli.mjs [status|capabilities]\n');
  process.exitCode = 2;
}
