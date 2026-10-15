import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createCipheriv, createDecipheriv, createHash} from 'node:crypto';
test('shared UTF-8 AES-GCM vector authenticates exact metadata and rejects tampering', async () => {
  const vector = JSON.parse(await readFile(new URL('../vectors/lan-aead-v1.json', import.meta.url)));
  const key = Buffer.from(vector.key, 'base64'), nonce = Buffer.from(vector.nonce, 'base64'), plain = Buffer.from(vector.plaintext);
  const cipher = createCipheriv('aes-256-gcm', key, nonce); cipher.setAAD(Buffer.from(vector.aad));
  const encrypted = Buffer.concat([cipher.update(plain), cipher.final(), cipher.getAuthTag()]);
  assert.equal(encrypted.toString('base64'), vector.ciphertext);
  assert.equal(createHash('sha256').update(plain).digest('hex'), vector.sha256);
  const bad = createDecipheriv('aes-256-gcm', key, nonce); bad.setAAD(Buffer.from(vector.aad + ' ')); bad.setAuthTag(encrypted.subarray(-16));
  bad.update(encrypted.subarray(0, -16)); assert.throws(()=>bad.final());
  assert.equal(await readFile(new URL('../../mobile/android/app/src/main/assets/lan-aead-v1.json', import.meta.url), 'utf8'),
    await readFile(new URL('../vectors/lan-aead-v1.json', import.meta.url), 'utf8'));
});
