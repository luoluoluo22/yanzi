// Durable, fail-closed task handoff for the existing Yanzi bridge.
// Inspired by grill-engineering's ticket graph, FIFO leases, separate reviewer,
// and single integration seat. This module never runs commands or merges code.
import { randomUUID } from 'node:crypto';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, renameSync, unlinkSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';

const COMMIT = /^[0-9a-f]{40}$/i;
const ROLES = new Set(['development', 'reviewer', 'integration']);
const CHECKS = ['e2e', 'standards', 'spec'];
const clone = value => structuredClone(value);
const validId = value => typeof value === 'string' && /^[a-zA-Z0-9_.:-]{1,100}$/.test(value);

function taskGraph(input) {
  if (!Array.isArray(input) || input.length < 1 || input.length > 100)
    throw new Error('tasks_invalid');
  const ids = new Set();
  for (const task of input) {
    if (!validId(task.id) || ids.has(task.id) || !Array.isArray(task.dependsOn) ||
        (task.prompt != null && (typeof task.prompt !== 'string' || !task.prompt.trim() || task.prompt.length > 4000)))
      throw new Error('task_id_or_dependencies_invalid');
    ids.add(task.id);
  }
  const graph = new Map(input.map(t => [t.id, t.dependsOn]));
  for (const task of input) for (const dep of task.dependsOn)
    if (!ids.has(dep) || dep === task.id) throw new Error('dependency_invalid');
  const visiting = new Set(), visited = new Set();
  function walk(id) {
    if (visiting.has(id)) throw new Error('dependency_cycle');
    if (visited.has(id)) return;
    visiting.add(id);
    for (const dep of graph.get(id)) walk(dep);
    visiting.delete(id);
    visited.add(id);
  }
  for (const id of ids) walk(id);
}

function validateChecks(checks) {
  if (!checks || typeof checks !== 'object' || Array.isArray(checks)) throw new Error('checks_required');
  for (const lane of CHECKS) {
    const value = checks[lane];
    if (!value || !['pass', 'fail', 'blocked'].includes(value.status) ||
        typeof value.evidence !== 'string' || !value.evidence.trim() || value.evidence.length > 4096 ||
        !Array.isArray(value.findings) || value.findings.length > 30 || value.findings.some(x => typeof x !== 'string' || x.length > 256))
      throw new Error('checks_invalid_' + lane);
  }
}

export class DeliveryLedger {
  constructor(file, { workers = 3, reviewRounds = 3, clock = Date.now } = {}) {
    if (!Number.isInteger(workers) || workers < 1 ||
        !Number.isInteger(reviewRounds) || reviewRounds < 1)
      throw new Error('policy_invalid');
    this.file = file;
    this.workers = workers;
    this.reviewRounds = reviewRounds;
    this.clock = clock;
    mkdirSync(dirname(file), { recursive: true });
  }

  load() {
    if (!existsSync(this.file)) return { version: 1, runs: {} };
    const data = JSON.parse(readFileSync(this.file, 'utf8'));
    if (data.version !== 1 || !data.runs || typeof data.runs !== 'object')
      throw new Error('ledger_version_invalid');
    return data;
  }

  snapshot(runId) {
    const data = this.load();
    return clone(runId ? data.runs[runId] ?? null : data);
  }

  // Single-writer lock is intentional. A stale .lock is an operator intervention,
  // not permission to steal a lease and risk duplicate external side effects.
  change(fn) {
    let handle;
    const lock = this.file + '.lock';
    try { handle = openSync(lock, 'wx', 0o600); }
    catch (error) {
      if (error.code === 'EEXIST') throw new Error('ledger_writer_busy');
      throw error;
    }
    try {
      const data = this.load();
      const result = fn(data);
      const temporary = this.file + '.' + randomUUID() + '.tmp';
      try {
        writeFileSync(temporary, JSON.stringify(data, null, 2), { mode: 0o600 });
        renameSync(temporary, this.file);
      } finally {
        if (existsSync(temporary)) unlinkSync(temporary);
      }
      return clone(result);
    } finally {
      closeSync(handle);
      unlinkSync(lock);
    }
  }

  createRun({ runId, tasks }) {
    if (!validId(runId)) throw new Error('run_id_invalid');
    taskGraph(tasks);
    const spec = tasks.map(t => ({ id: t.id, dependsOn: [...t.dependsOn], prompt: t.prompt ?? null }));
    return this.change(data => {
      if (data.runs[runId]) {
        if (JSON.stringify(data.runs[runId].spec) !== JSON.stringify(spec))
          throw new Error('run_id_conflict');
        return data.runs[runId];
      }
      if (Object.keys(data.runs).length >= 128) throw new Error('ledger_capacity_reached');
      const run = { id: runId, spec, createdAt: this.clock(), events: [],
        tasks: Object.fromEntries(spec.map((t, index) => [t.id, {
          id: t.id, order: index, dependsOn: t.dependsOn, prompt: t.prompt, phase: 'queued',
          generation: 1, reviewFailures: 0, lease: null,
          candidate: null, checks: null, mergedCommit: null,
        }])) };
      data.runs[runId] = run;
      this.event(run, 'created', null);
      return run;
    });
  }

  event(run, type, taskId, details = {}) {
    run.events.push({ type, taskId, at: this.clock(), ...details });
    if (run.events.length > 300) run.events.splice(0, run.events.length - 300);
  }

  getRun(data, runId) {
    const run = data.runs[runId];
    if (!run) throw new Error('run_not_found');
    return run;
  }

  // Never retry an expired external invocation blind. It may have committed
  // changes while the caller lost its reply.
  expire(run) {
    for (const task of Object.values(run.tasks)) {
      if (task.lease && task.lease.expiresAt <= this.clock()) {
        this.event(run, 'lease_uncertain', task.id, { role: task.lease.role });
        task.lease = null;
        task.phase = 'blocked';
        task.blockedReason = 'external_outcome_uncertain';
      }
    }
  }

  claim({ runId, role, workerId, leaseMs = 15 * 60 * 1000 }) {
    if (!ROLES.has(role) || !validId(workerId) ||
        !Number.isInteger(leaseMs) || leaseMs < 1000 || leaseMs > 6 * 60 * 60 * 1000)
      throw new Error('claim_invalid');
    return this.change(data => {
      const run = this.getRun(data, runId);
      this.expire(run);
      const entries = Object.values(run.tasks).sort((a, b) => a.order - b.order);
      const active = entries.filter(t => t.lease);
      const integrationBusy = active.some(t => t.lease.role === 'integration');
      if (role !== 'integration' && active.filter(t => t.lease.role !== 'integration').length >= this.workers)
        return null;
      if (role === 'integration' && integrationBusy) return null;
      const phase = { development: 'queued', reviewer: 'review', integration: 'ready_for_integration' }[role];
      const candidate = entries.find(task => task.phase === phase && !task.lease &&
        (role !== 'development' || task.dependsOn.every(dep => run.tasks[dep].phase === 'completed')) &&
        (role !== 'reviewer' || task.candidate?.developer !== workerId));
      if (!candidate) return null;
      const lease = { id: randomUUID(), role, workerId, generation: candidate.generation,
        expiresAt: this.clock() + leaseMs };
      candidate.lease = lease;
      candidate.phase = { development: 'developing', reviewer: 'reviewing',
        integration: 'integrating' }[role];
      this.event(run, 'claimed', candidate.id, { role, workerId, generation: candidate.generation });
      return { runId, taskId: candidate.id, prompt: candidate.prompt, ...lease, candidate: clone(candidate.candidate) };
    });
  }

  withLease(data, input, role) {
    const run = this.getRun(data, input.runId);
    this.expire(run);
    const task = run.tasks[input.taskId];
    if (!task || !task.lease || task.lease.id !== input.leaseId ||
        task.lease.role !== role || task.lease.workerId !== input.workerId ||
        task.lease.generation !== task.generation)
      throw new Error('lease_mismatch');
    return { run, task };
  }

  renewLease({ runId, taskId, workerId, leaseId, leaseMs = 15 * 60 * 1000 }) {
    if (!Number.isInteger(leaseMs) || leaseMs < 1000 || leaseMs > 60 * 60 * 1000)
      throw new Error('lease_duration_invalid');
    return this.change(data => {
      const run = this.getRun(data, runId);
      this.expire(run);
      const task = run.tasks[taskId];
      if (!task || !task.lease || task.lease.id !== leaseId ||
          task.lease.workerId !== workerId || task.lease.generation !== task.generation)
        throw new Error('lease_mismatch');
      task.lease.expiresAt = this.clock() + leaseMs;
      this.event(run, 'lease_renewed', taskId, { role: task.lease.role });
      return task.lease;
    });
  }

  blockClaim({ runId, taskId, workerId, leaseId, reason }) {
    if (typeof reason !== 'string' || !reason.trim() || reason.length > 1000)
      throw new Error('block_reason_invalid');
    return this.change(data => {
      const run = this.getRun(data, runId);
      this.expire(run);
      const task = run.tasks[taskId];
      if (!task || !task.lease || task.lease.id !== leaseId ||
          task.lease.workerId !== workerId || task.lease.generation !== task.generation)
        throw new Error('lease_mismatch');
      const role = task.lease.role;
      task.lease = null;
      task.phase = 'blocked';
      task.blockedReason = reason;
      this.event(run, 'claim_blocked', taskId, { role, reason });
      return task;
    });
  }

  completeDevelopment(input) {
    if (!COMMIT.test(input.commitSha) || typeof input.evidence !== 'string' || !input.evidence.trim() || input.evidence.length > 4096)
      throw new Error('development_evidence_invalid');
    return this.change(data => {
      const { run, task } = this.withLease(data, input, 'development');
      task.candidate = { commitSha: input.commitSha, evidence: input.evidence,
        developer: input.workerId, generation: task.generation };
      task.lease = null;
      task.phase = 'review';
      this.event(run, 'development_submitted', task.id, { commitSha: input.commitSha });
      return task;
    });
  }

  completeReview(input) {
    validateChecks(input.checks);
    return this.change(data => {
      const { run, task } = this.withLease(data, input, 'reviewer');
      if (task.candidate?.commitSha !== input.reviewedCommitSha)
        throw new Error('review_target_stale');
      task.checks = clone(input.checks);
      task.lease = null;
      const statuses = CHECKS.map(key => input.checks[key].status);
      if (statuses.includes('blocked')) {
        task.phase = 'blocked';
        task.blockedReason = 'review_blocked';
      } else if (statuses.includes('fail')) {
        task.reviewFailures++;
        task.phase = task.reviewFailures < this.reviewRounds ? 'queued' : 'blocked';
        if (task.phase === 'blocked') task.blockedReason = 'review_budget_exhausted';
      } else {
        task.phase = 'ready_for_integration';
      }
      this.event(run, 'review_finished', task.id, { verdict: task.phase,
        reviewer: input.workerId, commitSha: input.reviewedCommitSha });
      return task;
    });
  }

  completeIntegration(input) {
    if (!COMMIT.test(input.mergeCommitSha) || input.ciPassed !== true ||
        typeof input.proof !== 'string' || !input.proof.trim() || input.proof.length > 4096)
      throw new Error('merge_evidence_invalid');
    return this.change(data => {
      const { run, task } = this.withLease(data, input, 'integration');
      if (task.candidate?.commitSha !== input.reviewedCommitSha ||
          CHECKS.some(key => task.checks?.[key]?.status !== 'pass'))
        throw new Error('integration_gate_failed');
      task.phase = 'completed';
      task.lease = null;
      task.mergedCommit = input.mergeCommitSha;
      this.event(run, 'integration_recorded', task.id, { mergeCommitSha: input.mergeCommitSha });
      return task;
    });
  }

  // Explicit reconciliation creates a new attempt. Only after the operator
  // establishes that previous side effects are safe to abandon.
  retryBlocked({ runId, taskId, reason }) {
    if (typeof reason !== 'string' || !reason.trim() || reason.length > 1000) throw new Error('recovery_reason_required');
    return this.change(data => {
      const run = this.getRun(data, runId);
      this.expire(run);
      const task = run.tasks[taskId];
      if (!task || task.phase !== 'blocked') throw new Error('task_not_blocked');
      task.generation++;
      task.phase = 'queued';
      task.candidate = null;
      task.checks = null;
      task.blockedReason = null;
      task.reviewFailures = 0;
      this.event(run, 'explicit_requeue', task.id, { reason });
      return task;
    });
  }
}
