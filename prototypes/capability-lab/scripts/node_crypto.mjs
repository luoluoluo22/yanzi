import { webcrypto } from 'node:crypto';
import { performance } from 'node:perf_hooks';

const encoder = new TextEncoder();
const toHex = bytes => Buffer.from(bytes).toString('hex');

async function lane(index) {
  let value = encoder.encode(`燕子能力实验室::lane-${index}`);
  for (let round = 0; round < 64; round++) {
    value = new Uint8Array(await webcrypto.subtle.digest('SHA-256', value));
  }
  return toHex(value);
}

const started = performance.now();
const laneHashes = await Promise.all(Array.from({ length: 8 }, (_, i) => lane(i)));
const finalBytes = await webcrypto.subtle.digest(
  'SHA-256',
  encoder.encode(laneHashes.join('|'))
);
const elapsedMs = performance.now() - started;

console.log(JSON.stringify({
  ok: true,
  experiment: 'node-parallel-crypto-chain',
  lanes: laneHashes.length,
  roundsPerLane: 64,
  finalHash: toHex(finalBytes),
  elapsedMs: Number(elapsedMs.toFixed(2))
}));
