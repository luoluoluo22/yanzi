using System.Collections.Concurrent;
using System.IO;
using System.Text;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class ExtensionStorageService
{
    private static readonly TimeSpan BackgroundCloudTimeout = TimeSpan.FromSeconds(8);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CloudWriteLocks = new(StringComparer.OrdinalIgnoreCase);

    public static string StorageRootPath => HostAssets.ResolveDataDirectoryPath("ExtensionStorage");

    public static string GetExtensionStorageDirectoryPath(string extensionId)
    {
        var normalizedExtensionId = NormalizeExtensionId(extensionId);
        var directoryPath = Path.Combine(StorageRootPath, normalizedExtensionId);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public static async Task<ExtensionStorageReadResult> ReadTextAsync(
        string extensionId,
        string key,
        string? scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = ParseScope(scope);
        var normalizedKey = NormalizeStorageKey(key);
        var localPath = ResolveLocalFilePath(extensionId, normalizedKey);

        if (normalizedScope == ExtensionStorageScope.Both)
        {
            QueueCloudReadRefresh(extensionId, normalizedKey, localPath);
            if (File.Exists(localPath))
            {
                var localValue = await File.ReadAllTextAsync(localPath, cancellationToken);
                return new ExtensionStorageReadResult(true, localValue, "local", localPath);
            }

            var cloudResult = await TryReadCloudDataAsync(extensionId, normalizedKey, cancellationToken);
            if (cloudResult.Value?.Deleted == true)
            {
                ExtensionDataSyncStateStore.MarkSynced(cloudResult.Value);
                return new ExtensionStorageReadResult(false, null, "cloud-tombstone", localPath);
            }
            if (cloudResult.Content != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, cloudResult.Content, Encoding.UTF8, cancellationToken);
                if (cloudResult.Value != null) ExtensionDataSyncStateStore.MarkSynced(cloudResult.Value);
                return new ExtensionStorageReadResult(true, cloudResult.Content, "cloud", localPath);
            }

            return new ExtensionStorageReadResult(false, null, "none", localPath);
        }

        if (normalizedScope is ExtensionStorageScope.Cloud or ExtensionStorageScope.Both)
        {
            var cloudResult = await TryReadCloudDataAsync(extensionId, normalizedKey, cancellationToken);
            if (cloudResult.Value != null)
            {
                var state = ExtensionDataSyncStateStore.Get(extensionId, normalizedKey);
                if (File.Exists(localPath))
                {
                    var localContent = await File.ReadAllTextAsync(localPath, cancellationToken);
                    var localHash = ExtensionDataObjectStore.ComputeContentHash(localContent);
                    var localDiverged = state == null || state.Pending || state.Conflict != null ||
                                        (!string.IsNullOrWhiteSpace(state?.LocalContentHash) &&
                                         !localHash.Equals(state.LocalContentHash, StringComparison.OrdinalIgnoreCase));
                    if (localDiverged &&
                        (cloudResult.Value.Deleted ||
                         !localHash.Equals(cloudResult.Value.ContentHash, StringComparison.OrdinalIgnoreCase)))
                    {
                        ExtensionDataSyncStateStore.PreserveConflict(
                            extensionId,
                            normalizedKey,
                            localContent,
                            cloudResult.Value);
                        return new ExtensionStorageReadResult(
                            !cloudResult.Value.Deleted,
                            cloudResult.Content,
                            "cloud-conflict-preserved",
                            localPath);
                    }
                }
                if (cloudResult.Value.Deleted)
                {
                    if (File.Exists(localPath)) File.Delete(localPath);
                    ExtensionDataSyncStateStore.MarkSynced(cloudResult.Value);
                    return new ExtensionStorageReadResult(false, null, "cloud-tombstone", localPath);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, cloudResult.Content, Encoding.UTF8, cancellationToken);
                if (cloudResult.Value != null) ExtensionDataSyncStateStore.MarkSynced(cloudResult.Value);
                return new ExtensionStorageReadResult(true, cloudResult.Content, "cloud", localPath);
            }
        }

        if (File.Exists(localPath))
        {
            var localValue = await File.ReadAllTextAsync(localPath, cancellationToken);
            return new ExtensionStorageReadResult(true, localValue, "local", localPath);
        }

        return new ExtensionStorageReadResult(false, null, "none", localPath);
    }

    public static async Task<ExtensionStorageWriteResult> WriteTextAsync(
        string extensionId,
        string key,
        string content,
        string? scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = ParseScope(scope);
        var normalizedKey = NormalizeStorageKey(key);
        var localPath = ResolveLocalFilePath(extensionId, normalizedKey);

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, content ?? string.Empty, Encoding.UTF8, cancellationToken);

        var cloudSaved = false;
        string? cloudMessage = null;
        if (normalizedScope == ExtensionStorageScope.Both)
        {
            ExtensionDataSyncStateStore.MarkPending(
                extensionId,
                normalizedKey,
                ExtensionDataObjectStore.ComputeContentHash(content));
            QueueCloudWrite(extensionId, normalizedKey, content ?? string.Empty);
            cloudMessage = "cloud write queued";
        }
        else if (normalizedScope == ExtensionStorageScope.Cloud)
        {
            ExtensionDataSyncStateStore.MarkPending(
                extensionId,
                normalizedKey,
                ExtensionDataObjectStore.ComputeContentHash(content));
            try
            {
                var result = await WriteCloudTextAsync(extensionId, normalizedKey, content ?? string.Empty, cancellationToken);
                ExtensionDataSyncStateStore.MarkSynced(result.Value);
                cloudSaved = true;
            }
            catch (Exception ex)
            {
                ExtensionDataSyncStateStore.MarkFailed(extensionId, normalizedKey, ex.Message);
                cloudMessage = ex.Message;
                if (normalizedScope == ExtensionStorageScope.Cloud)
                {
                    throw;
                }
            }
        }

        return new ExtensionStorageWriteResult(localPath, cloudSaved, normalizedScope.ToString().ToLowerInvariant(), cloudMessage);
    }

    public static async Task<ExtensionStorageWriteResult> DeleteTextAsync(
        string extensionId,
        string key,
        string? scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = ParseScope(scope);
        var normalizedKey = NormalizeStorageKey(key);
        var localPath = ResolveLocalFilePath(extensionId, normalizedKey);
        if (File.Exists(localPath)) File.Delete(localPath);

        var cloudSaved = false;
        string? cloudMessage = null;
        if (normalizedScope == ExtensionStorageScope.Both)
        {
            ExtensionDataSyncStateStore.MarkPending(
                extensionId,
                normalizedKey,
                ExtensionDataObjectStore.ComputeContentHash(string.Empty),
                deleted: true);
            QueueCloudWrite(extensionId, normalizedKey, string.Empty, deleted: true);
            cloudMessage = "cloud delete queued";
        }
        else if (normalizedScope == ExtensionStorageScope.Cloud)
        {
            ExtensionDataSyncStateStore.MarkPending(
                extensionId,
                normalizedKey,
                ExtensionDataObjectStore.ComputeContentHash(string.Empty),
                deleted: true);
            try
            {
                var settings = AppSettingsStore.Load();
                if (!PersonalSyncBackendFactory.IsConfigured(settings))
                {
                    throw new InvalidOperationException("个人同步未完整配置，无法删除云端存储。");
                }
                var result = await new PersonalSyncService(settings)
                    .DeleteExtensionDataAsync(extensionId, normalizedKey, cancellationToken);
                ExtensionDataSyncStateStore.MarkSynced(result.Value);
                cloudSaved = true;
            }
            catch (Exception ex)
            {
                ExtensionDataSyncStateStore.MarkFailed(extensionId, normalizedKey, ex.Message);
                throw;
            }
        }

        return new ExtensionStorageWriteResult(
            localPath,
            cloudSaved,
            normalizedScope.ToString().ToLowerInvariant(),
            cloudMessage);
    }

    private static void QueueCloudReadRefresh(string extensionId, string key, string localPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(BackgroundCloudTimeout);
                var cloudResult = await TryReadCloudDataAsync(extensionId, key, cts.Token);
                if (cloudResult.Value == null && cloudResult.Content == null)
                {
                    return;
                }

                var state = ExtensionDataSyncStateStore.Get(extensionId, key);
                var localContent = File.Exists(localPath)
                    ? await File.ReadAllTextAsync(localPath, cts.Token)
                    : null;
                var localHash = localContent == null ? string.Empty : ExtensionDataObjectStore.ComputeContentHash(localContent);
                if (state?.Conflict != null)
                {
                    return;
                }

                // An earlier write from this device is not a competing editor while a newer
                // local snapshot is still queued. Let the write queue publish that snapshot.
                if (ShouldSkipCloudRefresh(state, cloudResult.Value,
                    DeviceIdentityStore.GetOrCreateDesktopDeviceId()))
                {
                    return;
                }

                if (localContent != null &&
                    (state == null || state.Pending || state.Conflict != null ||
                     (!string.IsNullOrWhiteSpace(state.LocalContentHash) &&
                      !localHash.Equals(state.LocalContentHash, StringComparison.OrdinalIgnoreCase))))
                {
                    if (cloudResult.Value != null &&
                        (cloudResult.Value.Deleted ||
                         !localHash.Equals(cloudResult.Value.ContentHash, StringComparison.OrdinalIgnoreCase)))
                    {
                        ExtensionDataSyncStateStore.PreserveConflict(extensionId, key, localContent ?? string.Empty, cloudResult.Value);
                    }
                    return;
                }

                if (cloudResult.Value?.Deleted == true)
                {
                    if (File.Exists(localPath)) File.Delete(localPath);
                    ExtensionDataSyncStateStore.MarkSynced(cloudResult.Value);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, cloudResult.Content ?? string.Empty, Encoding.UTF8, cts.Token);
                if (cloudResult.Value != null) ExtensionDataSyncStateStore.MarkSynced(cloudResult.Value);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Extension storage cloud refresh skipped: id={extensionId}, key={key}, error={ex.Message}");
            }
        });
    }

    internal static bool ShouldSkipCloudRefresh(
        ExtensionDataSyncState? state,
        ExtensionDataObject? remote,
        string localDeviceId) =>
        state != null && remote != null &&
        (remote.Revision < state.LastRemoteRevision ||
         (state.Pending && remote.UpdatedByDeviceId.Equals(
             localDeviceId, StringComparison.OrdinalIgnoreCase)));

    private static void QueueCloudWrite(string extensionId, string key, string content, bool deleted = false)
    {
        _ = Task.Run(async () =>
        {
            var operationKey = $"{extensionId}\0{key}";
            var writeLock = CloudWriteLocks.GetOrAdd(operationKey, static _ => new SemaphoreSlim(1, 1));
            try
            {
                await writeLock.WaitAsync();
                using var cts = new CancellationTokenSource(BackgroundCloudTimeout);
                var state = ExtensionDataSyncStateStore.Get(extensionId, key);
                if (state?.Pending != true || state.Conflict != null)
                {
                    return;
                }

                deleted = state.PendingDeleted;
                if (!deleted)
                {
                    var localPath = ResolveLocalFilePath(extensionId, key);
                    content = await File.ReadAllTextAsync(localPath, cts.Token);
                }
                ExtensionDataWriteResult result;
                if (deleted)
                {
                    var settings = AppSettingsStore.Load();
                    if (!PersonalSyncBackendFactory.IsConfigured(settings))
                    {
                        throw new InvalidOperationException("个人同步未完整配置，无法删除云端存储。");
                    }
                    result = await new PersonalSyncService(settings).DeleteExtensionDataAsync(extensionId, key, cts.Token);
                }
                else
                {
                    result = await WriteCloudTextAsync(extensionId, key, content, cts.Token);
                }
                ExtensionDataSyncStateStore.MarkSynced(result.Value);
                HostAssets.AppendLog($"Extension storage cloud write completed: id={extensionId}, key={key}");
            }
            catch (Exception ex)
            {
                ExtensionDataSyncStateStore.MarkFailed(extensionId, key, ex.Message);
                HostAssets.AppendLog($"Extension storage cloud write skipped: id={extensionId}, key={key}, error={ex.Message}");
            }
            finally
            {
                if (writeLock.CurrentCount == 0)
                {
                    writeLock.Release();
                }
            }
        });
    }

    private static async Task<string?> TryReadCloudTextAsync(string extensionId, string key, CancellationToken cancellationToken)
    {
        var result = await TryReadCloudDataAsync(extensionId, key, cancellationToken);
        return result.Content;
    }

    private static async Task<ExtensionDataReadResult> TryReadCloudDataAsync(
        string extensionId,
        string key,
        CancellationToken cancellationToken)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            return new ExtensionDataReadResult(null, null, false);
        }

        var service = new PersonalSyncService(settings);
        return await service.TryReadExtensionDataAsync(extensionId, key, cancellationToken);
    }

    private static async Task<ExtensionDataWriteResult> WriteCloudTextAsync(
        string extensionId,
        string key,
        string content,
        CancellationToken cancellationToken)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            throw new InvalidOperationException("个人同步未完整配置，无法写入云端存储。");
        }

        var service = new PersonalSyncService(settings);
        return await service.WriteExtensionDataTextAsync(extensionId, key, content, cancellationToken);
    }

    public static async Task SyncLocalDirectoryToCloudAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            return;
        }

        var dir = GetExtensionStorageDirectoryPath(extensionId);
        if (!Directory.Exists(dir))
        {
            return;
        }

        var service = new PersonalSyncService(settings);
        foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            var key = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (key.StartsWith("EBWebView/", StringComparison.OrdinalIgnoreCase) || 
                key.StartsWith(@"EBWebView\", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "EBWebView", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken);
            ExtensionDataSyncStateStore.MarkPending(
                extensionId,
                key,
                ExtensionDataObjectStore.ComputeContentHash(content));
            try
            {
                var result = await service.WriteExtensionDataTextAsync(extensionId, key, content, cancellationToken);
                ExtensionDataSyncStateStore.MarkSynced(result.Value);
            }
            catch (Exception ex)
            {
                ExtensionDataSyncStateStore.MarkFailed(extensionId, key, ex.Message);
                throw;
            }
        }
    }

    public static async Task<ExtensionDataPendingSyncResult> SyncPendingCloudWritesAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            return new ExtensionDataPendingSyncResult(0, 0);
        }

        var pending = ExtensionDataSyncStateStore.Load()
            .Where(static item => item.Pending && item.Conflict == null)
            .ToArray();
        if (pending.Length == 0)
        {
            return new ExtensionDataPendingSyncResult(0, 0);
        }

        var service = new PersonalSyncService(settings);
        var uploaded = 0;
        var failed = 0;
        foreach (var state in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationKey = $"{state.ExtensionId}\0{state.Key}";
            var writeLock = CloudWriteLocks.GetOrAdd(operationKey, static _ => new SemaphoreSlim(1, 1));
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                var current = ExtensionDataSyncStateStore.Get(state.ExtensionId, state.Key);
                if (current?.Pending != true || current.Conflict != null) continue;

                var path = ResolveLocalFilePath(current.ExtensionId, current.Key);
                if (!current.PendingDeleted && !File.Exists(path))
                {
                    ExtensionDataSyncStateStore.MarkFailed(current.ExtensionId, current.Key, "本地扩展数据文件已不存在。");
                    failed++;
                    continue;
                }
                var content = current.PendingDeleted
                    ? string.Empty
                    : await File.ReadAllTextAsync(path, cancellationToken);
                ExtensionDataSyncStateStore.MarkPending(
                    current.ExtensionId,
                    current.Key,
                    ExtensionDataObjectStore.ComputeContentHash(content),
                    current.PendingDeleted);
                var result = current.PendingDeleted
                    ? await service.DeleteExtensionDataAsync(current.ExtensionId, current.Key, cancellationToken)
                    : await service.WriteExtensionDataTextAsync(current.ExtensionId, current.Key, content, cancellationToken);
                ExtensionDataSyncStateStore.MarkSynced(result.Value);
                uploaded++;
            }
            catch (Exception ex)
            {
                ExtensionDataSyncStateStore.MarkFailed(state.ExtensionId, state.Key, ex.Message);
                failed++;
            }
            finally
            {
                writeLock.Release();
            }
        }
        return new ExtensionDataPendingSyncResult(uploaded, failed);
    }

    private static string ResolveLocalFilePath(string extensionId, string key)
    {
        var extensionDirectory = GetExtensionStorageDirectoryPath(extensionId);
        var relativePath = key.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(extensionDirectory, relativePath));
    }

    private static string NormalizeExtensionId(string extensionId)
    {
        var normalized = (extensionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("extensionId 不能为空。");
        }

        return normalized;
    }

    private static string NormalizeStorageKey(string key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("storage key 不能为空。");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("storage key 不能包含 . 或 .. 路径段。");
        }

        return string.Join("/", segments);
    }

    private static ExtensionStorageScope ParseScope(string? scope)
    {
        return (scope ?? "local").Trim().ToLowerInvariant() switch
        {
            "cloud" => ExtensionStorageScope.Cloud,
            "both" => ExtensionStorageScope.Both,
            _ => ExtensionStorageScope.Local
        };
    }
}

public sealed record ExtensionStorageReadResult(bool Found, string? Content, string Source, string LocalPath);

public sealed record ExtensionStorageWriteResult(string LocalPath, bool CloudSaved, string Scope, string? CloudMessage);

public sealed record ExtensionDataPendingSyncResult(int UploadedCount, int FailedCount);

public enum ExtensionStorageScope
{
    Local,
    Cloud,
    Both
}
