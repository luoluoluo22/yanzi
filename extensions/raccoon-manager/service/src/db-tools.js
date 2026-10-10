import { randomUUID } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import { insideRoot } from "./config.js";

const sessions = new Map();
const MAX_SESSIONS = 256;
const RETAIN_MS = 15 * 60 * 1000;

function normalizeValue(value) {
  if (typeof value === "bigint") return value.toString();
  if (Buffer.isBuffer(value) || value instanceof Uint8Array) {
    return { type: "blob", base64: Buffer.from(value).toString("base64"), bytes: value.length };
  }
  return value;
}

function normalizeRow(row) {
  return Object.fromEntries(Object.entries(row).map(([key, value]) => [key, normalizeValue(value)]));
}

function cleanup() {
  const cutoff = Date.now() - RETAIN_MS;
  for (const [id, session] of sessions) {
    if (session.lastAccess < cutoff) closeSession(id);
  }
}

function bindIterator(statement, params) {
  if (params == null) return statement.iterate();
  if (Array.isArray(params)) return statement.iterate(...params);
  if (typeof params === "object") return statement.iterate(params);
  return statement.iterate(params);
}

function bindRun(statement, params) {
  if (params == null) return statement.run();
  if (Array.isArray(params)) return statement.run(...params);
  if (typeof params === "object") return statement.run(params);
  return statement.run(params);
}

function closeSession(id) {
  const session = sessions.get(id);
  if (!session) return false;
  sessions.delete(id);
  try { session.db.close(); } catch {}
  return true;
}

function readBatch(session, maxRows) {
  const rows = [];
  let done = false;
  for (let i = 0; i < maxRows; i++) {
    const next = session.iterator.next();
    if (next.done) {
      done = true;
      break;
    }
    rows.push(normalizeRow(next.value));
    session.rowOffset += 1;
  }
  session.lastAccess = Date.now();
  return { rows, done };
}

export function dbInfo({ file }) {
  const target = insideRoot(file);
  const db = new DatabaseSync(target, { readOnly: true });
  try {
    const objects = db.prepare(
      "SELECT type, name, tbl_name AS tableName, sql " +
      "FROM sqlite_master " +
      "WHERE type IN ('table','view','index','trigger') " +
      "ORDER BY type, name"
    ).all().map(normalizeRow);
    const userVersion = normalizeValue(db.prepare("PRAGMA user_version").get()?.user_version ?? 0);
    const journalMode = normalizeValue(db.prepare("PRAGMA journal_mode").get()?.journal_mode ?? null);
    const pageCount = normalizeValue(db.prepare("PRAGMA page_count").get()?.page_count ?? null);
    const pageSize = normalizeValue(db.prepare("PRAGMA page_size").get()?.page_size ?? null);
    return {
      file,
      userVersion,
      journalMode,
      pageCount,
      pageSize,
      estimatedBytes: pageCount != null && pageSize != null ? Number(pageCount) * Number(pageSize) : null,
      objects
    };
  } finally {
    db.close();
  }
}

export function dbQueryStart({ file, sql, params, batchRows = 500, readOnly = true }) {
  cleanup();
  if (sessions.size >= MAX_SESSIONS) {
    throw new Error("Database query session limit reached (" + MAX_SESSIONS + ").");
  }
  const target = insideRoot(file);
  const db = new DatabaseSync(target, { readOnly });
  try {
    const statement = db.prepare(sql);
    const iterator = bindIterator(statement, params);
    const id = randomUUID();
    const session = {
      id,
      db,
      statement,
      iterator,
      file,
      sql,
      readOnly,
      rowOffset: 0,
      createdAt: Date.now(),
      lastAccess: Date.now()
    };
    sessions.set(id, session);
    const first = readBatch(session, batchRows);
    const result = {
      sessionId: id,
      file,
      readOnly,
      offset: 0,
      rows: first.rows,
      nextOffset: session.rowOffset,
      done: first.done
    };
    if (first.done) closeSession(id);
    return result;
  } catch (error) {
    db.close();
    throw error;
  }
}

export function dbQueryNext({ sessionId, maxRows = 500 }) {
  cleanup();
  const session = sessions.get(sessionId);
  if (!session) throw new Error("Unknown database query session: " + sessionId);
  const offset = session.rowOffset;
  try {
    const batch = readBatch(session, maxRows);
    const result = {
      sessionId,
      file: session.file,
      offset,
      rows: batch.rows,
      nextOffset: session.rowOffset,
      done: batch.done
    };
    if (batch.done) closeSession(sessionId);
    return result;
  } catch (error) {
    closeSession(sessionId);
    throw error;
  }
}

export function dbQueryClose({ sessionId }) {
  return { ok: closeSession(sessionId), sessionId };
}

export function dbQuerySessions() {
  cleanup();
  return [...sessions.values()].map(session => ({
    sessionId: session.id,
    file: session.file,
    readOnly: session.readOnly,
    rowOffset: session.rowOffset,
    createdAt: new Date(session.createdAt).toISOString(),
    lastAccess: new Date(session.lastAccess).toISOString(),
    sqlPreview: session.sql.slice(0, 500)
  }));
}

export function dbExecute({ file, sql, params }) {
  const target = insideRoot(file);
  const db = new DatabaseSync(target);
  try {
    if (params == null) {
      db.exec(sql);
      return { ok: true, file, changes: null, lastInsertRowid: null };
    }
    const statement = db.prepare(sql);
    const result = bindRun(statement, params);
    return {
      ok: true,
      file,
      changes: normalizeValue(result.changes),
      lastInsertRowid: normalizeValue(result.lastInsertRowid)
    };
  } finally {
    db.close();
  }
}
