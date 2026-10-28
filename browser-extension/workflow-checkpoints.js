/* Yanzi workflow checkpoint journal.
 * Persists bounded, non-sensitive execution metadata only. Do not store
 * selectors, form values, page content, full URLs, or raw error messages.
 * A restarted service worker reports unfinished jobs as interrupted.
 * It intentionally DOES NOT replay write steps.
 */
(() => {
  "use strict";
  const KEY = "yanzi.workflow.checkpoints.v1";
  const MAX_ENTRIES = 40;
  const storeFor = api => {
    let queue = Promise.resolve();
    const supported = !!api?.storage?.local;
    async function read() {
      if (!supported) return [];
      const obj = await api.storage.local.get(KEY);
      return Array.isArray(obj?.[KEY]) ? obj[KEY] : [];
    }
    function mutate(change) {
      if (!supported) return Promise.resolve(null);
      const operation = queue.then(async () => {
        const list = await read();
        const value = change(list);
        await api.storage.local.set({ [KEY]: list.slice(-MAX_ENTRIES) });
        return value;
      });
      queue = operation.catch(() => {});
      return operation;
    }
    const iso = () => new Date().toISOString();
    const safeId = id => String(id || "").slice(0, 128);
    async function begin(id, info) {
      if (!id || !supported) return;
      return mutate(rows => {
        const name = safeId(id);
        const oldIndex = rows.findIndex(row => row.taskId === name);
        if (oldIndex >= 0) rows.splice(oldIndex, 1);
        rows.push({ taskId: name, tabId: info.tabId, origin: info.origin || "",
          totalSteps: Math.max(0, Number(info.totalSteps) || 0),
          completedSteps: 0, currentStep: null, state: "running",
          startedAt: iso(), updatedAt: iso() });
      });
    }
    async function progress(id, completed, step) {
      if (!id || !supported) return;
      return mutate(rows => {
        const row = rows.find(x => x.taskId === safeId(id));
        if (!row || row.state !== "running") return;
        row.completedSteps = Math.max(0, Number(completed) || 0);
        row.currentStep = step ? {
          index: Math.max(1, Number(step.index) || 1),
          type: String(step.type || "").slice(0, 30)
        } : null;
        row.updatedAt = iso();
      });
    }
    async function finish(id, state, completed) {
      if (!id || !supported) return;
      return mutate(rows => {
        const row = rows.find(x => x.taskId === safeId(id));
        if (!row || row.state !== "running") return;
        row.state = state === "success" ? "success" : "error";
        row.completedSteps = Math.max(0, Number(completed) || 0);
        row.currentStep = null;
        row.updatedAt = iso();
      });
    }
    async function interruptUnfinished() {
      return mutate(rows => {
        let count = 0;
        for (const row of rows) {
          if (row.state !== "running") continue;
          row.state = "interrupted";
          row.updatedAt = iso();
          row.note = "Extension worker restarted. Writes were not replayed.";
          count++;
        }
        return count;
      });
    }
    async function status(id) {
      // Wait for writes queued in this service worker before reading.
      await queue;
      const list = await read();
      const selected = id ? list.filter(x => x.taskId === safeId(id)) : list.slice(-20);
      return selected.map(x => ({ ...x }));
    }
    return { begin, progress, finish, interruptUnfinished, status, supported };
  };
  globalThis.yanziWorkflowCheckpointsFactory = storeFor;
  if (typeof module !== "undefined" && module.exports) module.exports = { storeFor };
})();
