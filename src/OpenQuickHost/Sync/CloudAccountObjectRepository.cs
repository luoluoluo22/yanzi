using Yanzi.Core;

namespace OpenQuickHost.Sync;

internal sealed class CloudAccountObjectRepository(CloudSyncClient client) : IAccountObjectRepository<CloudSyncObjectListResponse>
{
    public Task<CloudSyncObjectListResponse> ReadSnapshotAsync(CancellationToken cancellationToken = default) =>
        client.GetSyncObjectsAsync(cancellationToken);
    public Task<CloudSyncObjectListResponse> ReadChangesAsync(long cursor, CancellationToken cancellationToken = default) =>
        client.GetSyncChangesAsync(cursor, cancellationToken: cancellationToken);
}
