using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class MainWindow
{
    // Direction applies to user settings, not protocol reads needed for safe conditional uploads.
    public async Task<(bool ok, string message)> TransferAccountSettingsAsync(bool upload)
    {
        if (_cloudSyncClient?.HasCredential != true) return (false, "请先登录，再同步账号设置。");
        await _accountObjectSyncLock.WaitAsync();
        try
        {
            await _cloudSyncClient.EnsureAuthenticatedAsync();
            var capabilities = await TryGetCloudSyncCapabilitiesAsync();
            if (capabilities?.ObjectsAuthoritative != true)
                return (false, "当前服务端尚未启用安全的单向同步，请升级服务端后重试。");

            var state = CloudObjectSyncStateStore.Load(_cloudSyncClient.CurrentUserId);
            var settings = AppSettingsStore.Load();
            var localWriteVersion = AppSettingsStore.WriteVersion;
            var snapshot = CloudQuickPanelConfigSnapshot.FromSettings(settings);
            var writes = AccountConfigObjectStore.PrepareWrites(snapshot, DateTime.UtcNow,
                    state.Objects.Keys, state.KnownLocalDynamicObjectIds)
                .Where(_ => state.Objects.Keys.Any(id => !YanmObjectStore.IsObjectId(id)) || !CloudQuickPanelConfigSnapshot.IsInitialDefaultSnapshot(snapshot))
                .Concat(YanmObjectStore.PrepareWrites(settings.Yanm ?? new YanmSettings(), DateTime.UtcNow,
                    state.Objects.Keys, state.KnownLocalDynamicObjectIds)
                    .Where(_ => state.Objects.Keys.Any(YanmObjectStore.IsObjectId) || HasYanmLayoutUserContent(settings.Yanm) || settings.Yanm?.ComponentState?.Count > 0))
                .ToDictionary(w => w.ObjectId, StringComparer.OrdinalIgnoreCase);
            var previouslyKnown = state.Objects.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Persist offline edits before a GET changes their observed baseline.
            foreach (var write in writes.Values)
            {
                if (state.Conflicts.ContainsKey(write.ObjectId)) continue;
                var known = state.Objects.TryGetValue(write.ObjectId, out var baseline);
                if ((known || upload) && (!known || !LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope, ToEnvelope(baseline!))))
                    AddPendingCloudObject(state, write.ObjectId, DateTime.UtcNow, baseline?.Revision ?? 0);
            }
            CloudObjectSyncStateStore.Save(state);
            await RefreshCloudObjectCacheAsync(state);
            if (!string.Equals(state.UserId, _cloudSyncClient.CurrentUserId, StringComparison.Ordinal))
                return (false, "账号已变化，本次同步已停止，请重新操作。");

            var transferred = 0;
            if (upload)
            {
                foreach (var write in writes.Values)
                {
                    if (state.Conflicts.ContainsKey(write.ObjectId) || !state.PendingOperations.TryGetValue(write.ObjectId, out var pending)) continue;
                    if (state.Objects.TryGetValue(write.ObjectId, out var remote) &&
                        LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope, ToEnvelope(remote)))
                    {
                        UpdateKnownDynamicObjectAfterWrite(state, write);
                        RemovePendingCloudObject(state, write.ObjectId);
                        continue;
                    }
                    pending.AttemptCount++;
                    pending.LastAttemptAtUtc = DateTime.UtcNow.ToString("O");
                    try
                    {
                        var saved = await PutCloudObjectWriteAsync(write, pending.LastExpectedRevision);
                        state.Objects[write.ObjectId] = CloudObjectSyncCacheEntry.FromRecord(saved);
                        UpdateKnownDynamicObjectAfterWrite(state, write);
                        RemovePendingCloudObject(state, write.ObjectId);
                        transferred++;
                    }
                    catch (CloudSyncRevisionConflictException)
                    {
                        await RefreshCloudObjectCacheAsync(state);
                        if (state.Objects.TryGetValue(write.ObjectId, out remote))
                        {
                            if (!LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope, ToEnvelope(remote)))
                                PreserveManualSyncConflict(state, write, remote);
                            RemovePendingCloudObject(state, write.ObjectId);
                        }
                    }
                    catch (Exception ex)
                    {
                        pending.LastError = FormatExceptionMessage(ex);
                    }
                    CloudObjectSyncStateStore.Save(state);
                }
            }
            else
            {
                if (AppSettingsStore.WriteVersion != localWriteVersion)
                {
                    CloudObjectSyncStateStore.Save(state);
                    return (false, "下载期间本机设置有新修改，已保留当前内容，请再次点击下载。");
                }
                // First download on a populated device preserves divergent local content for explicit choice.
                foreach (var write in writes.Values.Where(w => !previouslyKnown.Contains(w.ObjectId)))
                    if (!state.Conflicts.ContainsKey(write.ObjectId) && state.Objects.TryGetValue(write.ObjectId, out var remote) &&
                        !LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope, ToEnvelope(remote)))
                        PreserveManualSyncConflict(state, write, remote);
                CloudObjectSyncStateStore.Save(state);
                ApplyDownloadedAccountSettings(state);
                transferred = state.Objects.Count;
            }
            CloudObjectSyncStateStore.Save(state);
            if (upload && AppSettingsStore.WriteVersion != localWriteVersion)
                return (false, $"已上传 {transferred} 项；同步期间本机又有新修改，请再次上传。");
            var pendingCount = state.PendingOperations.Count;
            var conflictCount = state.Conflicts.Count;
            var summary = upload ? $"已上传 {transferred} 项账号设置" : $"已读取 {transferred} 项云端设置";
            if (pendingCount > 0) summary += $"；{pendingCount} 项本机修改保留待上传";
            if (conflictCount > 0) summary += $"；{conflictCount} 项不同内容需要选择";
            return (pendingCount == 0 && conflictCount == 0, summary + "。");
        }
        catch (Exception ex) { return (false, $"本次同步未完成：{FormatExceptionMessage(ex)}"); }
        finally { _accountObjectSyncLock.Release(); }
    }

    private static void PreserveManualSyncConflict(CloudObjectSyncState state, AccountConfigObjectWrite write, CloudObjectSyncCacheEntry remote)
    {
        RemovePendingCloudObject(state, write.ObjectId);
        state.Conflicts[write.ObjectId] = new CloudObjectConflictRecord
        {
            ObjectId = write.ObjectId, LocalSchemaVersion = write.Envelope.SchemaVersion,
            LocalDeleted = write.Envelope.Deleted, LocalPayload = write.Envelope.Payload.Clone(),
            DetectedAtUtc = DateTime.UtcNow.ToString("O"), RemoteRevision = remote.Revision,
            RemoteUpdatedAtUtc = remote.UpdatedAtUtc, RemoteDeviceId = remote.UpdatedByDeviceId,
            RemoteDeviceName = remote.UpdatedByDeviceName
        };
    }

    private void ApplyDownloadedAccountSettings(CloudObjectSyncState state)
    {
        var settings = AppSettingsStore.Load();
        var snapshot = CloudQuickPanelConfigSnapshot.FromSettings(settings);
        var envelopes = state.Objects.Values.Select(ToEnvelope).ToDictionary(e => e.ObjectId, StringComparer.OrdinalIgnoreCase);
        foreach (var write in AccountConfigObjectStore.PrepareWrites(snapshot, DateTime.UtcNow, state.Objects.Keys, state.KnownLocalDynamicObjectIds))
            if (state.PendingOperations.ContainsKey(write.ObjectId)) envelopes[write.ObjectId] = write.Envelope;
        var resolved = LauncherConfigObjectStore.Compose(snapshot, envelopes.Values.Where(e => !e.Deleted), out _, preferObjectsOverBase: true);
        resolved = AccountConfigObjectStore.Apply(resolved, envelopes.Values);
        if (resolved != null)
        {
            ApplyCloudLauncherSettings(settings, resolved.ToAppSettings(), resolved);
            settings.LauncherConfigUpdatedAtUtc = resolved.UpdatedAtUtc ?? settings.LauncherConfigUpdatedAtUtc;
            AppSettingsStore.Save(settings);
            RefreshRuntimeAfterCloudLauncherPull(settings, resolved);
        }
        ApplyYanmObjectsFromCache(settings, state);
    }
}
