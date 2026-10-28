import {
  chmodSync,
  existsSync,
  mkdirSync,
  readFileSync,
  renameSync,
  writeFileSync
} from 'node:fs';
import {join, resolve} from 'node:path';

/**
 * Single-process durable store for DeviceClient.
 *
 * Writes are serialized and committed by atomic rename. The service is
 * intentionally deployed as one systemd instance, so cross-process locking is
 * not required. The execution state is persisted before side effects, which
 * preserves DeviceClient's "unknown after crash, never blindly rerun" rule.
 */
export class AtomicJsonDeviceStore {
  constructor(directory) {
    this.directory = resolve(directory);
    mkdirSync(this.directory, {recursive: true, mode: 0o700});
    if (process.platform !== 'win32') chmodSync(this.directory, 0o700);
    this.file = join(this.directory, 'device-store.json');
    this.records = this.#load();
    this.writeQueue = Promise.resolve();
  }

  #load() {
    if (!existsSync(this.file)) return {};
    const parsed = JSON.parse(readFileSync(this.file, 'utf8'));
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      throw new Error('invalid_device_store');
    }
    return parsed;
  }

  #clone(value) {
    return value === undefined ? undefined : structuredClone(value);
  }

  #persist() {
    const temporary = this.file + '.tmp-' + process.pid;
    writeFileSync(temporary, JSON.stringify(this.records), {encoding: 'utf8', mode: 0o600});
    if (process.platform !== 'win32') chmodSync(temporary, 0o600);
    renameSync(temporary, this.file);
  }

  #write(operation) {
    const current = this.writeQueue.then(() => {
      const result = operation();
      this.#persist();
      return result;
    });
    this.writeQueue = current.catch(() => {});
    return current;
  }

  async get(key) {
    await this.writeQueue;
    return this.#clone(this.records[key]);
  }

  async set(key, value) {
    return this.#write(() => {
      this.records[key] = this.#clone(value);
    });
  }

  async delete(key) {
    return this.#write(() => {
      delete this.records[key];
    });
  }

  async list(prefix, limit = 1000) {
    await this.writeQueue;
    return Object.keys(this.records)
      .filter(key => key.startsWith(prefix))
      .sort()
      .slice(0, limit)
      .map(key => [key, this.#clone(this.records[key])]);
  }

  async compareExchange(key, expectedState, value) {
    return this.#write(() => {
      const actualState = this.records[key]?.state ?? null;
      if (actualState !== expectedState) return false;
      this.records[key] = this.#clone(value);
      return true;
    });
  }

  async close() {
    await this.writeQueue;
  }
}
