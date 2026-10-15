namespace OpenQuickHost.Sync;

internal static class CloudPendingChanges
{
    internal static void Capture(CloudObjectSyncState state, IEnumerable<AccountConfigObjectWrite> writes,
        IReadOnlyDictionary<string, CloudObjectSyncCacheEntry> baseline)
    {
        var local = writes.ToArray();
        // A fresh device has no observed baseline; default initialization is not an offline edit.
        if (!local.Any(write => baseline.ContainsKey(write.ObjectId))) return;
        foreach (var write in local)
        {
            if (state.Conflicts.ContainsKey(write.ObjectId) || state.PendingOperations.ContainsKey(write.ObjectId)) continue;
            baseline.TryGetValue(write.ObjectId, out var original);
            if (original == null && write.Envelope.Deleted) continue;
            if (original != null && LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope,
                new LauncherConfigObjectEnvelope { ObjectId = original.ObjectId, Deleted = original.Deleted, Payload = original.Payload })) continue;
            Enqueue(state, write.ObjectId, DateTime.UtcNow, original?.Revision ?? 0);
        }
    }

    // A fetched remote object is not necessarily applied locally (e.g. upload-only preflight).
    internal static void RecordApplied(CloudObjectSyncState state, IEnumerable<AccountConfigObjectWrite> writes)
    {
        foreach (var write in writes)
            if (state.Objects.TryGetValue(write.ObjectId, out var remote) &&
                LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope,
                    new LauncherConfigObjectEnvelope { ObjectId = remote.ObjectId, Deleted = remote.Deleted, Payload = remote.Payload }))
            {
                state.LocalBaselines[write.ObjectId] = remote;
                if (AccountConfigObjectStore.IsDynamicObjectId(write.ObjectId) || YanmObjectStore.IsDynamicObjectId(write.ObjectId))
                {
                    if (remote.Deleted) state.KnownLocalDynamicObjectIds.RemoveAll(id => id.Equals(write.ObjectId, StringComparison.OrdinalIgnoreCase));
                    else if (!state.KnownLocalDynamicObjectIds.Contains(write.ObjectId, StringComparer.OrdinalIgnoreCase)) state.KnownLocalDynamicObjectIds.Add(write.ObjectId);
                }
            }
    }

    internal static void Enqueue(CloudObjectSyncState state, string objectId, DateTime updatedAt, long expectedRevision)
    {
        if (!state.PendingObjectIds.Contains(objectId, StringComparer.OrdinalIgnoreCase)) state.PendingObjectIds.Add(objectId);
        if (!state.PendingOperations.TryGetValue(objectId, out var pending))
            state.PendingOperations[objectId] = new CloudObjectPendingOperation
            {
                ObjectId = objectId, CreatedAtUtc = updatedAt.ToString("O"), LastExpectedRevision = expectedRevision
            };
        else if (!DateTimeOffset.TryParse(pending.CreatedAtUtc, out var previous) || updatedAt > previous.UtcDateTime)
        {
            pending.CreatedAtUtc = updatedAt.ToString("O");
            pending.LastError = string.Empty;
        }
    }
}
