// Authoritative cloud ledger. Runs are limited to 128 entries (100 tickets each).
// SQLite serializes writers across processes; the existing DeliveryLedger
// preserves DAG scheduling, reviewer segregation, lease identity and recovery.
import { DatabaseSync } from 'node:sqlite';
import { randomBytes, createHash } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { DeliveryLedger } from '../chatgpt-bridge/delivery-ledger.mjs';

const idPattern = /^[a-zA-Z0-9_.:-]{1,100}$/;
const roles = new Set(['development', 'reviewer']);
const hash = value => createHash('sha256').update(value).digest('hex');
const clone = value => structuredClone(value);

export class CloudStore extends DeliveryLedger {
  constructor(databasePath, options = {}) {
    mkdirSync(dirname(databasePath), { recursive: true });
    super(databasePath + '.not-used-json', options);
    this.db = new DatabaseSync(databasePath);
    this.db.exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
    this.db.exec("CREATE TABLE IF NOT EXISTS ledger (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);");
    this.db.exec("CREATE TABLE IF NOT EXISTS devices (id TEXT PRIMARY KEY, token_hash TEXT UNIQUE NOT NULL, roles TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1, last_seen INTEGER);");
    this.db.exec("INSERT OR IGNORE INTO ledger (id, payload) VALUES (1, '{\"version\":1,\"runs\":{}}');");
  }
  close() { this.db.close(); }
  load() {
    const row = this.db.prepare('SELECT payload FROM ledger WHERE id=1').get();
    const parsed = JSON.parse(row.payload);
    if (parsed.version !== 1 || !parsed.runs) throw new Error('cloud_state_corrupt');
    return parsed;
  }
  change(fn) {
    this.db.exec('BEGIN IMMEDIATE');
    try {
      const state = this.load();
      const result = fn(state);
      const serialized = JSON.stringify(state);
      if (Buffer.byteLength(serialized, 'utf8') > 8 * 1024 * 1024)
        throw new Error('ledger_capacity_reached');
      this.db.prepare('UPDATE ledger SET payload=? WHERE id=1').run(serialized);
      this.db.exec('COMMIT');
      return clone(result);
    } catch (error) {
      this.db.exec('ROLLBACK');
      throw error;
    }
  }
  enroll({ deviceId, grantedRoles }) {
    if (!idPattern.test(deviceId) || !Array.isArray(grantedRoles) ||
        grantedRoles.length < 1 || grantedRoles.length > 3 ||
        grantedRoles.some(role => !roles.has(role)))
      throw new Error('device_enrollment_invalid');
    const token = randomBytes(32).toString('hex');
    const digest = hash(token);
    this.db.prepare('INSERT INTO devices (id, token_hash, roles, enabled) VALUES (?, ?, ?, 1) ON CONFLICT(id) DO UPDATE SET token_hash=excluded.token_hash, roles=excluded.roles, enabled=1')
      .run(deviceId, digest, JSON.stringify([...new Set(grantedRoles)]));
    return { deviceId, token, roles: [...new Set(grantedRoles)] };
  }
  disable(deviceId) {
    if (!idPattern.test(deviceId)) throw new Error('device_id_invalid');
    const result = this.db.prepare('UPDATE devices SET enabled=0 WHERE id=?').run(deviceId);
    return { revoked: result.changes === 1 };
  }
  authenticate(token) {
    if (!/^[a-f0-9]{64}$/i.test(token || '')) return null;
    const row = this.db.prepare('SELECT id,roles FROM devices WHERE token_hash=? AND enabled=1').get(hash(token));
    if (!row) return null;
    this.db.prepare('UPDATE devices SET last_seen=? WHERE id=?').run(this.clock(), row.id);
    return { deviceId: row.id, roles: JSON.parse(row.roles) };
  }
  devices() {
    return this.db.prepare('SELECT id,roles,enabled,last_seen FROM devices ORDER BY id').all()
      .map(row => ({ id: row.id, roles: JSON.parse(row.roles),
        enabled: Boolean(row.enabled), lastSeen: row.last_seen }));
  }
  claimForDevice(input, identity) {
    if (!roles.has(input.role) || !identity.roles.includes(input.role)) throw new Error('device_role_forbidden');
    if (input.workerId !== identity.deviceId) throw new Error('device_identity_mismatch');
    return this.claim(input);
  }
  actForDevice(action, input, identity) {
    const required = {
      renew: 'any', block: 'any', development: 'development', review: 'reviewer',
      integration: 'integration',
    }[action];
    if (!required) throw new Error('unknown_device_action');
    if (input.workerId !== identity.deviceId) throw new Error('device_identity_mismatch');
    const snapshot = this.snapshot(input.runId);
    const live = snapshot?.tasks?.[input.taskId]?.lease;
    if (!live || live.id !== input.leaseId || live.workerId !== identity.deviceId ||
        !identity.roles.includes(live.role) ||
        (required !== 'any' && live.role !== required))
      throw new Error('device_lease_forbidden');
    switch (action) {
      case 'renew': return this.renewLease(input);
      case 'block': return this.blockClaim(input);
      case 'development': return this.completeDevelopment(input);
      case 'review': return this.completeReview(input);
      // No integration operation exposed. An authenticated operator must
      // supply GitHub-verified evidence through a separate future gate.
      default: throw new Error('integration_not_enabled');
    }
  }
}
