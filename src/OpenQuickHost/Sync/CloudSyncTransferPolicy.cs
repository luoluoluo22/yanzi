namespace OpenQuickHost.Sync;

internal static class CloudSyncTransferPolicy
{
    // Conditional PUT is safe in compatibility mode too; authority selects the read source.
    internal static bool SupportsUpload(CloudSyncCapabilitiesResponse? capabilities) =>
        capabilities is { Ok: true, ObjectSyncAvailable: true };

    internal static bool SupportsDownload(CloudSyncCapabilitiesResponse? capabilities) =>
        capabilities is { Ok: true, ObjectSyncAvailable: true, ObjectsAuthoritative: true };

    internal static long UploadRevision(CloudObjectPendingOperation pending, CloudObjectSyncCacheEntry? remote,
        CloudObjectSyncCacheEntry? localBaseline, string deviceId)
    {
        // Repair a historical same-device retry only when that version is already the local baseline.
        // Revision zero is an intentional create, and another device's update still requires a choice.
        if (pending.LastExpectedRevision > 0 && remote != null && localBaseline != null &&
            remote.Revision > pending.LastExpectedRevision && remote.Revision == localBaseline.Revision &&
            !string.IsNullOrWhiteSpace(deviceId) && string.Equals(remote.UpdatedByDeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
            LauncherConfigObjectStore.HasEquivalentPayload(
                new LauncherConfigObjectEnvelope { ObjectId = remote.ObjectId, Deleted = remote.Deleted, Payload = remote.Payload },
                new LauncherConfigObjectEnvelope { ObjectId = localBaseline.ObjectId, Deleted = localBaseline.Deleted, Payload = localBaseline.Payload }))
            return remote.Revision;
        return pending.LastExpectedRevision;
    }
}
