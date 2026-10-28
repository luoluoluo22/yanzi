namespace OpenQuickHost.Sync;

internal static class CloudConflictReconciler
{
    // Compare the preserved local copy, never the UI snapshot (which may already show cloud data).
    internal static int Reconcile(CloudObjectSyncState state)
    {
        var removed = 0;
        foreach (var (id, conflict) in state.Conflicts.ToArray())
        {
            if (!state.Objects.TryGetValue(id, out var remote) || remote.Revision < conflict.RemoteRevision) continue;
            var localEnvelope = new LauncherConfigObjectEnvelope
            {
                ObjectId = id, Deleted = conflict.LocalDeleted, Payload = conflict.LocalPayload
            };
            var remoteEnvelope = new LauncherConfigObjectEnvelope
            {
                ObjectId = id, Deleted = remote.Deleted, Payload = remote.Payload
            };
            if (LauncherConfigObjectStore.HasEquivalentPayload(localEnvelope, remoteEnvelope))
            {
                state.Conflicts.Remove(id);
                removed++;
                continue;
            }
            conflict.RemoteRevision = remote.Revision;
            conflict.RemoteUpdatedAtUtc = remote.UpdatedAtUtc;
            conflict.RemoteDeviceId = remote.UpdatedByDeviceId;
            conflict.RemoteDeviceName = remote.UpdatedByDeviceName;
        }
        return removed;
    }
}
