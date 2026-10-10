import { listManagedProcesses } from "./process-manager.js";
import { listSearches } from "./search-manager.js";
import { dbQuerySessions } from "./db-tools.js";
import { processWatchList } from "./resource-governor.js";
import { pipelineDeploymentState, shutdownPipelines } from "./dev-pipeline.js";

export function deploymentState(provider, activeRequests) {
  const now = Math.floor(Date.now()/1000);
  const state = {
    activeRequests,
    retainedProcessSessions: listManagedProcesses().length,
    retainedSearchSessions: listSearches().length,
    databaseSessions: dbQuerySessions().length,
    processWatches: processWatchList().length,
    pendingAuthorizations: provider ? [...provider.pending.values(),...provider.codes.values()].filter(r=>r.expiresAt>now).length : 0,
    ...pipelineDeploymentState()
  };
  return { ...state, busy: Object.values(state).some(value=>value>0) };
}

export async function releaseDeploymentState() { await shutdownPipelines(); }
