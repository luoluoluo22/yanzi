using System.IO;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static partial class ExtensionStorageService
{
    // Only reconcile changes proven to be device-local or a monotonic, same-device
    // refresh of a daily snapshot. Ambiguous user edits must remain conflicts.
    public static async Task<ExtensionDataConflictResolutionResult> ResolveSafeConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        var conflicts = ExtensionDataSyncStateStore.Load()
            .Where(static state => state.Conflict != null).ToArray();
        if (conflicts.Length == 0) return new(0, 0, 0);

        var settings = AppSettingsStore.Load();
        PersonalSyncService? service = null;
        var resolved = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var item in conflicts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var conflict = item.Conflict!;
            try
            {
                if (!ExtensionDevicePolicy.ShouldSyncData(item.ExtensionId, item.Key))
                {
                    // Local-only runtime metadata must never overwrite the cloud.
                    await ArchiveConflictAsync(item, cancellationToken);
                    ExtensionDataSyncStateStore.ClearConflict(item.ExtensionId, item.Key);
                    resolved++;
                    continue;
                }

                var localPath = Path.GetFullPath(Path.Combine(
                    GetExtensionStorageDirectoryPath(item.ExtensionId),
                    item.Key.Replace('/', Path.DirectorySeparatorChar)));
                var root = Path.GetFullPath(GetExtensionStorageDirectoryPath(item.ExtensionId))
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!localPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(localPath) || conflict.LocalDeleted)
                {
                    skipped++;
                    continue;
                }

                var localContent = await File.ReadAllTextAsync(localPath, cancellationToken);
                if (!string.Equals(ExtensionDataObjectStore.ComputeContentHash(localContent),
                        conflict.LocalContentHash, StringComparison.OrdinalIgnoreCase) ||
                    !CanAutoResolveDailySnapshot(item, localContent))
                {
                    skipped++;
                    continue;
                }

                if (!PersonalSyncBackendFactory.IsConfigured(settings))
                {
                    skipped++;
                    continue;
                }
                service ??= new PersonalSyncService(settings);
                var latest = (await service.TryReadExtensionDataAsync(
                    item.ExtensionId, item.Key, cancellationToken)).Value;
                if (latest == null ||
                    !string.Equals(latest.VersionId, conflict.Remote.VersionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                await ArchiveConflictAsync(item, cancellationToken);
                ExtensionDataSyncStateStore.ClearConflict(item.ExtensionId, item.Key);
                try
                {
                    if (string.Equals(latest.ContentHash, conflict.LocalContentHash,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        ExtensionDataSyncStateStore.MarkSynced(latest);
                    }
                    else
                    {
                        var saved = await service.WriteExtensionDataTextAsync(
                            item.ExtensionId, item.Key, localContent, cancellationToken);
                        if (!saved.Confirmed) throw new IOException("Cloud write was not confirmed");
                        ExtensionDataSyncStateStore.MarkSynced(saved.Value);
                    }
                    resolved++;
                }
                catch
                {
                    ExtensionDataSyncStateStore.PreserveConflict(
                        item.ExtensionId, item.Key, localContent, latest);
                    throw;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                HostAssets.AppendLog(
                    $"Safe extension conflict recovery failed: id={item.ExtensionId}, key={item.Key}, error={ex.Message}");
            }
        }
        HostAssets.AppendLog($"Safe extension conflicts: resolved={resolved}, skipped={skipped}, failed={failed}");
        return new(resolved, skipped, failed);
    }

    private static async Task ArchiveConflictAsync(
        ExtensionDataSyncState item, CancellationToken cancellationToken)
    {
        var directory = HostAssets.ResolveDataDirectoryPath("SyncConflictBackups");
        Directory.CreateDirectory(directory);
        var filename = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") +
            "-" + item.Id + "-" + Guid.NewGuid().ToString("N") + ".json";
        await File.WriteAllTextAsync(
            Path.Combine(directory, filename),
            JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    internal static bool CanAutoResolveDailySnapshot(
        ExtensionDataSyncState item, string localContent)
    {
        const string job = "ext_e9e37068c8284b1e808f5454181fb418";
        var conflict = item.Conflict;
        if (!string.Equals(item.ExtensionId, job, StringComparison.OrdinalIgnoreCase) ||
            !item.Key.StartsWith("daily/", StringComparison.OrdinalIgnoreCase) ||
            !item.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            conflict == null || conflict.Remote.Deleted ||
            !string.Equals(conflict.Remote.UpdatedByDeviceId,
                ExtensionDevicePolicy.CurrentDeviceId, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var local = JsonDocument.Parse(localContent);
            using var remote = JsonDocument.Parse(conflict.Remote.Content);
            if (local.RootElement.ValueKind != JsonValueKind.Object ||
                remote.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var localRoot = local.RootElement;
            var remoteRoot = remote.RootElement;
            if (!localRoot.TryGetProperty("Date", out var localDate) ||
                !remoteRoot.TryGetProperty("Date", out var remoteDate) ||
                localDate.ValueKind != JsonValueKind.String ||
                localDate.GetString() != remoteDate.GetString() ||
                !Path.GetFileName(item.Key).StartsWith(localDate.GetString() + ".",
                    StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(item.Key).StartsWith(localDate.GetString() + "--",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            if (!localRoot.TryGetProperty("CapturedAt", out var capturedLocal) ||
                !remoteRoot.TryGetProperty("CapturedAt", out var capturedRemote) ||
                !DateTimeOffset.TryParse(capturedLocal.GetString(), out var localTime) ||
                !DateTimeOffset.TryParse(capturedRemote.GetString(), out var remoteTime) ||
                localTime <= remoteTime)
                return false;

            // Only timestamp and status prose may differ. Statistics and job lists
            // must be identical; never silently merge different user records.
            var localFields = localRoot.EnumerateObject().ToArray();
            var remoteFields = remoteRoot.EnumerateObject().ToArray();
            if (localFields.Length != remoteFields.Length) return false;
            foreach (var field in localFields)
            {
                if (!remoteRoot.TryGetProperty(field.Name, out var other)) return false;
                if (field.Name is "CapturedAt" or "Reason") continue;
                if (!JsonElement.DeepEquals(field.Value, other)) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
