import {StdioServerTransport} from '@modelcontextprotocol/sdk/server/stdio.js';
import {createYanziServer} from './server.js';
await createYanziServer().connect(new StdioServerTransport());
