import test from 'node:test';
import assert from 'node:assert/strict';
import { isAccountWakeEvent } from './device-relay-events.js';

test('authorization and sync events wake account peers without requiring a message envelope', () => {
  assert.equal(isAccountWakeEvent({type:'external-access-ready'}), true);
  assert.equal(isAccountWakeEvent({type:'sync-ready'}), true);
  assert.equal(isAccountWakeEvent({type:'receipt'}), false);
  assert.equal(isAccountWakeEvent({type:'message'}), false);
  assert.equal(isAccountWakeEvent({type:'unknown'}), false);
});
