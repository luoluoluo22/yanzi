using System.IO;
using Yanzi.Core;

namespace OpenQuickHost.Sync;

internal sealed class AccountObjectSyncService(IAccountObjectRepository<CloudSyncObjectListResponse> repository, AccountSyncCoordinator coordinator)
{
    private readonly IAccountObjectRepository<CloudSyncObjectListResponse> _repository = repository;
    private readonly AccountSyncCoordinator _coordinator = coordinator;
    public async Task RefreshAsync(CloudObjectSyncState state)
    {
        if (_repository == null)
        {
            return;
        }

        var generation = _coordinator.Generation;
        state.Objects.Remove(AccountEnvironmentSecretVault.ObjectId);
        state.PendingOperations.Remove(AccountEnvironmentSecretVault.ObjectId);
        state.PendingObjectIds.Remove(AccountEnvironmentSecretVault.ObjectId);
        state.Conflicts.Remove(AccountEnvironmentSecretVault.ObjectId);
        // Older clients advanced the download cursor after PUT, potentially skipping other devices' edits.
        var cursor = state.DownloadCursorValidated ? state.LastSyncedRevision : 0;
        var replaceSnapshot = cursor == 0 || state.Objects.Count == 0;
        CloudSyncObjectListResponse page;
        if (replaceSnapshot)
        {
            page = await _repository.ReadSnapshotAsync();
        }
        else
        {
            page = await _repository.ReadChangesAsync(cursor);
        }

        while (true)
        {
            _coordinator.RequireCurrent(generation);
            if (!page.Ok || !string.Equals(page.UserId, state.UserId, StringComparison.Ordinal))
                throw new InvalidDataException("云端同步响应无效或账号已变化。");
            _coordinator.Commit(generation, () =>
            {
                var nextCursor = CloudSyncProgress.DownloadCursor(cursor, page);
                if (page.HasMore && nextCursor <= cursor)
                    throw new InvalidDataException("云端分页没有前进，请稍后重试。");
                if (replaceSnapshot) state.Objects.Clear();
                replaceSnapshot = false;
                foreach (var item in page.Objects)
                {
                    if (AccountEnvironmentSecretVault.IsManagedObjectId(item.ObjectId)) continue;
                    if (!state.Objects.TryGetValue(item.ObjectId, out var known) || item.Revision >= known.Revision)
                        state.Objects[item.ObjectId] = CloudObjectSyncCacheEntry.FromRecord(item);
                }
                cursor = state.LastSyncedRevision = nextCursor;
                if (!page.HasMore)
                {
                    state.DownloadCursorValidated = true;
                    CloudConflictReconciler.Reconcile(state);
                }
            });
            if (!page.HasMore) return;

            page = await _repository.ReadChangesAsync(state.LastSyncedRevision);
        }
    }

}
