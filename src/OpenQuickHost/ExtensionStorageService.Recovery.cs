using System.IO;
using System.Security.Cryptography;
using System.Text;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

// Import remote keys only where the local file is absent or proven unchanged.
public static partial class ExtensionStorageService
{
    public static async Task<ExtensionDataConflictResolutionResult> ResolveStaleLocalConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
            throw new InvalidOperationException("Personal sync is not configured");
        var service = new PersonalSyncService(settings);
        var resolved = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var item in ExtensionDataSyncStateStore.Load().Where(static x => x.Conflict != null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.ExtensionId is not ("yanzi-notes" or "clipboard-history") ||
                item.Conflict?.LocalDeleted != false)
            {
                skipped++;
                continue;
            }
            var conflict = item.Conflict;
            var dir = GetExtensionStorageDirectoryPath(item.ExtensionId);
            var file = Path.GetFullPath(Path.Combine(dir, item.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            {
                skipped++;
                continue;
            }
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            // A conflict snapshot may be older than the on-disk file. Only trust the latest
            // local file if this is the same source device and its edit is newer than remote.
            if (!conflict.Remote.UpdatedByDeviceName.Contains(Environment.MachineName,
                    StringComparison.OrdinalIgnoreCase) ||
                !DateTimeOffset.TryParse(conflict.Remote.UpdatedAtUtc, out var remoteTime) ||
                File.GetLastWriteTimeUtc(file) <= remoteTime.UtcDateTime.AddMinutes(1))
            {
                skipped++;
                continue;
            }
            try
            {
                var latest = (await service.TryReadExtensionDataAsync(item.ExtensionId, item.Key, cancellationToken)).Value;
                if (latest == null || !string.Equals(latest.VersionId, conflict.Remote.VersionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }
                ExtensionDataSyncStateStore.ClearConflict(item.ExtensionId, item.Key);
                try
                {
                    var saved = await service.WriteExtensionDataTextAsync(item.ExtensionId, item.Key, content, cancellationToken);
                    if (!saved.Confirmed) throw new IOException("Cloud write was not confirmed");
                    ExtensionDataSyncStateStore.MarkSynced(saved.Value);
                    resolved++;
                }
                catch
                {
                    ExtensionDataSyncStateStore.PreserveConflict(item.ExtensionId, item.Key, content, latest);
                    throw;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                HostAssets.AppendLog($"Extension data conflict resolution failed: id={item.ExtensionId}, key={item.Key}, error={ex.Message}");
            }
        }
        return new ExtensionDataConflictResolutionResult(resolved, skipped, failed);
    }

    public static async Task<ExtensionDataRecoveryResult> ReconcileCloudDataAsync(
        CancellationToken cancellationToken = default, IReadOnlyCollection<string>? onlyExtensionIds = null)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
            return new ExtensionDataRecoveryResult(0, 0, 0);

        var states = ExtensionDataSyncStateStore.Load();
        var known = states.Select(static s => s.ExtensionId)
            .Concat(["yanzi-notes", "clipboard-history", "inspiration-board", "yanzi-album",
                "ext_e9e37068c8284b1e808f5454181fb418"])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (onlyExtensionIds != null)
            known = known.Where(id => onlyExtensionIds.Contains(id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var service = new PersonalSyncService(settings);
        var restored = 0;
        var published = 0;
        var skipped = 0;
        foreach (var extensionId in known)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = GetExtensionStorageDirectoryPath(extensionId);
            var localFiles = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(path => IsPortableDataForExtension(extensionId, Path.GetRelativePath(dir, path).Replace('\\', '/')))
                .ToDictionary(path => Path.GetRelativePath(dir, path).Replace('\\', '/'),
                    path => path, StringComparer.Ordinal);
            var indexKeys = states.Where(state => string.Equals(state.ExtensionId, extensionId,
                    StringComparison.OrdinalIgnoreCase)).Select(static state => state.Key)
                .Concat(localFiles.Keys).Distinct(StringComparer.Ordinal).ToArray();

            try
            {
                // Backfill old installation keys without publishing conflicted contents.
                if (indexKeys.Length != 0)
                    await service.PublishExtensionDataKeysAsync(extensionId, indexKeys, cancellationToken);
                foreach (var file in localFiles)
                {
                    if (ExtensionDataSyncStateStore.Get(extensionId, file.Key)?.Conflict != null)
                        continue;
                    if (IsPortableBinaryAsset(file.Key))
                    {
                        if (await service.PublishExtensionBinaryAssetIfAbsentAsync(extensionId,
                                file.Key, await File.ReadAllBytesAsync(file.Value, cancellationToken), cancellationToken))
                            published++;
                        else skipped++;
                    }
                    else
                    {
                        var old = await service.TryReadExtensionDataAsync(extensionId, file.Key, cancellationToken);
                        var localValue = await File.ReadAllTextAsync(file.Value, cancellationToken);
                        var localHash = ExtensionDataObjectStore.ComputeContentHash(localValue);
                        var baseline = ExtensionDataSyncStateStore.Get(extensionId, file.Key);
                        if (old.Value == null)
                        {
                            var written = await service.WriteExtensionDataTextAsync(extensionId, file.Key, localValue, cancellationToken);
                            ExtensionDataSyncStateStore.MarkSynced(written.Value);
                            published++;
                        }
                        else if (!old.Value.Deleted && old.Value.ContentHash.Equals(localHash, StringComparison.OrdinalIgnoreCase))
                        {
                            ExtensionDataSyncStateStore.MarkSynced(old.Value);
                        }
                        else if (baseline is { Pending: false, Conflict: null, LastRemoteRevision: > 0 } &&
                                 baseline.LastRemoteRevision == old.Value.Revision &&
                                 !string.Equals(baseline.LocalContentHash, localHash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Upload only when the source still matches the last observed cloud revision.
                            var written = await service.WriteExtensionDataTextAsync(extensionId, file.Key, localValue, cancellationToken);
                            ExtensionDataSyncStateStore.MarkSynced(written.Value);
                            published++;
                        }
                    }
                }

                // Index is separate from extension package zip and works on a fresh machine.
                var remoteKeys = await service.GetExtensionDataKeysAsync(extensionId, cancellationToken);
                foreach (var key in remoteKeys)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsPortableDataForExtension(extensionId, key)) continue;
                    var localPath = Path.GetFullPath(Path.Combine(dir, key.Replace('/', Path.DirectorySeparatorChar)));
                    var prefix = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!localPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsPortableBinaryAsset(key))
                    {
                        var raw = await service.TryReadExtensionBinaryAssetAsync(extensionId, key, cancellationToken);
                        if (raw == null) continue;
                        if (File.Exists(localPath))
                        {
                            if (!PersonalSyncService.BinaryAssetContentMatches(
                                    await File.ReadAllBytesAsync(localPath, cancellationToken), raw)) skipped++;
                            continue;
                        }
                        await AtomicRestoreBytesAsync(localPath, raw, cancellationToken);
                        restored++;
                        continue;
                    }
                    var remote = (await service.TryReadExtensionDataAsync(extensionId, key, cancellationToken)).Value;
                    if (remote == null) continue;
                    var state = ExtensionDataSyncStateStore.Get(extensionId, key);
                    if (state?.Conflict != null || state?.Pending == true)
                    {
                        skipped++;
                        continue;
                    }
                    if (!File.Exists(localPath))
                    {
                        // An existing sync baseline plus a missing file could mean a local deletion.
                        if (state?.PendingDeleted == true || state?.LastRemoteRevision > 0)
                        {
                            skipped++;
                            continue;
                        }
                        if (!remote.Deleted)
                        {
                            await AtomicRestoreBytesAsync(localPath, Encoding.UTF8.GetBytes(remote.Content), cancellationToken);
                            restored++;
                        }
                        ExtensionDataSyncStateStore.MarkSynced(remote);
                        continue;
                    }
                    var localContent = await File.ReadAllTextAsync(localPath, cancellationToken);
                    var localHash = ExtensionDataObjectStore.ComputeContentHash(localContent);
                    if (!remote.Deleted && string.Equals(localHash, remote.ContentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        ExtensionDataSyncStateStore.MarkSynced(remote);
                        continue;
                    }
                    if (state != null && !string.IsNullOrWhiteSpace(state.LocalContentHash) &&
                        string.Equals(localHash, state.LocalContentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        if (remote.Deleted) File.Delete(localPath);
                        else await AtomicRestoreBytesAsync(localPath, Encoding.UTF8.GetBytes(remote.Content), cancellationToken);
                        ExtensionDataSyncStateStore.MarkSynced(remote);
                        restored++;
                        continue;
                    }
                    ExtensionDataSyncStateStore.PreserveConflict(extensionId, key, localContent, remote);
                    skipped++;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                skipped++;
                HostAssets.AppendLog($"Extension data recovery failed: id={extensionId}, error={ex.Message}");
            }
        }
        HostAssets.AppendLog($"Extension data recovery completed: restored={restored}, published={published}, skipped={skipped}");
        return new ExtensionDataRecoveryResult(restored, published, skipped);
    }

    // Bulk account recovery uses an explicit allowlist. Never upload unknown extension
    // folders: some extensions persist bearer tokens, cookies and browser session data.
    internal static bool IsPortableDataForExtension(string extensionId, string key)
    {
        if (!IsPortableExtensionFile(key)) return false;
        var relative = key.Replace('\\', '/');
        if (string.Equals(extensionId, "yanzi-notes", StringComparison.OrdinalIgnoreCase))
            return relative.StartsWith("notes/article/", StringComparison.OrdinalIgnoreCase) &&
                   (relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                    IsPortableBinaryAsset(relative));
        if (string.Equals(extensionId, "clipboard-history", StringComparison.OrdinalIgnoreCase))
            return string.Equals(relative, "favorites-v2.json", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(extensionId, "inspiration-board", StringComparison.OrdinalIgnoreCase))
            return string.Equals(relative, "board-data.json", StringComparison.OrdinalIgnoreCase) ||
                   (relative.StartsWith("boards/", StringComparison.OrdinalIgnoreCase) &&
                    relative.EndsWith(".yzboard", StringComparison.OrdinalIgnoreCase));
        if (string.Equals(extensionId, "yanzi-album", StringComparison.OrdinalIgnoreCase))
            return IsPortableBinaryAsset(relative) &&
                   (relative.StartsWith("作品/", StringComparison.Ordinal) ||
                    relative.StartsWith("桌面钉图/", StringComparison.Ordinal));
        if (string.Equals(extensionId, "ext_e9e37068c8284b1e808f5454181fb418", StringComparison.OrdinalIgnoreCase))
            return relative is "settings.json" or "resume.json" ||
                   (relative.StartsWith("daily/", StringComparison.OrdinalIgnoreCase) &&
                    relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                   (relative.StartsWith("zhaopin-resume-history/", StringComparison.OrdinalIgnoreCase) &&
                    relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        return false;
    }

    internal static bool IsPortableBinaryAsset(string key) =>
        key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPortableExtensionFile(string key)
    {
        var parts = key.Replace('\\', '/').Split('/');
        if (parts.Any(static p => p is "sync-backups" or "EBWebView" or ".git" or "node_modules" ||
                p.StartsWith(".", StringComparison.Ordinal) ||
                p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
            return false;
        return IsPortableBinaryAsset(key) || key.EndsWith(".yzboard", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AtomicRestoreBytesAsync(string path, byte[] content, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".syncrestore-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, content, token);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record ExtensionDataConflictResolutionResult(int ResolvedCount, int SkippedCount, int FailedCount);

public sealed record ExtensionDataRecoveryResult(int RestoredCount, int PublishedCount, int SkippedCount);
