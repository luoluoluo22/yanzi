using System.IO;
using System.Text.Json;

namespace OpenQuickHost.Sync;

internal static class ExtensionDataSyncStateStore
{
    private static readonly object Gate = new();
    private static string StatePath => HostAssets.ResolveDataFilePath("extension-data-sync-state.json");

    public static ExtensionDataSyncState? Get(string extensionId, string key)
    {
        lock (Gate)
        {
            var id = BuildId(extensionId, key);
            return LoadCore().FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static IReadOnlyList<ExtensionDataSyncState> Load()
    {
        lock (Gate) return LoadCore();
    }

    public static void MarkPending(string extensionId, string key, string localHash, bool deleted = false)
    {
        Mutate(extensionId, key, state =>
        {
            state.Pending = true;
            state.PendingDeleted = deleted;
            state.LocalContentHash = localHash;
            state.LastError = null;
            state.LastAttemptAtUtc = DateTimeOffset.UtcNow.ToString("O");
        });
    }

    public static void MarkSynced(ExtensionDataObject remote)
    {
        Mutate(remote.ExtensionId, remote.Key, state =>
        {
            state.Pending = false;
            state.PendingDeleted = false;
            state.LocalContentHash = remote.ContentHash;
            state.LastRemoteRevision = remote.Revision;
            state.LastRemoteContentHash = remote.ContentHash;
            state.LastRemoteVersionId = remote.VersionId;
            state.LastRemoteDeviceId = remote.UpdatedByDeviceId;
            state.LastRemoteDeviceName = remote.UpdatedByDeviceName;
            state.LastSyncedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            state.LastError = null;
            if (state.Conflict != null &&
                state.Conflict.LocalDeleted == remote.Deleted &&
                (remote.Deleted || state.Conflict.LocalContentHash.Equals(remote.ContentHash, StringComparison.OrdinalIgnoreCase)))
            {
                state.Conflict = null;
            }
        });
    }

    public static void MarkFailed(string extensionId, string key, string error)
    {
        Mutate(extensionId, key, state =>
        {
            state.Pending = state.Conflict == null;
            state.LastAttemptAtUtc = DateTimeOffset.UtcNow.ToString("O");
            state.LastError = error;
        });
    }

    public static void PreserveConflict(
        string extensionId,
        string key,
        string localContent,
        ExtensionDataObject remote,
        bool localDeleted = false)
    {
        Mutate(extensionId, key, state =>
        {
            state.Pending = false;
            state.Conflict = new ExtensionDataConflict
            {
                DetectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                LocalContent = localContent,
                LocalContentHash = ExtensionDataObjectStore.ComputeContentHash(localContent),
                LocalDeleted = localDeleted,
                LocalBaseRevision = state.LastRemoteRevision,
                Remote = remote
            };
            state.LastRemoteRevision = remote.Revision;
            state.LastRemoteContentHash = remote.ContentHash;
            state.LastRemoteVersionId = remote.VersionId;
            state.LastRemoteDeviceId = remote.UpdatedByDeviceId;
            state.LastRemoteDeviceName = remote.UpdatedByDeviceName;
            state.LastError = "检测到扩展私有数据并发修改，已保留本地副本。";
        });
    }

    public static void ClearConflict(string extensionId, string key)
    {
        Mutate(extensionId, key, state =>
        {
            state.Conflict = null;
            state.LastError = null;
        });
    }

    private static void Mutate(string extensionId, string key, Action<ExtensionDataSyncState> mutation)
    {
        lock (Gate)
        {
            var values = LoadCore().ToList();
            var id = BuildId(extensionId, key);
            var state = values.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (state == null)
            {
                state = new ExtensionDataSyncState
                {
                    Id = id,
                    ExtensionId = ExtensionDataObjectStore.NormalizeExtensionId(extensionId),
                    Key = ExtensionDataObjectStore.NormalizeKey(key)
                };
                values.Add(state);
            }
            mutation(state);
            SaveCore(values);
        }
    }

    private static IReadOnlyList<ExtensionDataSyncState> LoadCore()
    {
        try
        {
            if (!File.Exists(StatePath)) return [];
            return JsonSerializer.Deserialize<List<ExtensionDataSyncState>>(File.ReadAllText(StatePath), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void SaveCore(List<ExtensionDataSyncState> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var tempPath = StatePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(values, JsonOptions));
        File.Move(tempPath, StatePath, overwrite: true);
    }

    private static string BuildId(string extensionId, string key) =>
        ExtensionDataObjectStore.ComputeContentHash(
            ExtensionDataObjectStore.NormalizeExtensionId(extensionId).ToLowerInvariant() + "\0" +
            ExtensionDataObjectStore.NormalizeKey(key));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed class ExtensionDataSyncState
{
    public string Id { get; set; } = string.Empty;
    public string ExtensionId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool Pending { get; set; }
    public bool PendingDeleted { get; set; }
    public string LocalContentHash { get; set; } = string.Empty;
    public long LastRemoteRevision { get; set; }
    public string LastRemoteContentHash { get; set; } = string.Empty;
    public string LastRemoteVersionId { get; set; } = string.Empty;
    public string LastRemoteDeviceId { get; set; } = string.Empty;
    public string LastRemoteDeviceName { get; set; } = string.Empty;
    public string? LastSyncedAtUtc { get; set; }
    public string? LastAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public ExtensionDataConflict? Conflict { get; set; }
}

public sealed class ExtensionDataConflict
{
    public string DetectedAtUtc { get; set; } = string.Empty;
    public string LocalContent { get; set; } = string.Empty;
    public string LocalContentHash { get; set; } = string.Empty;
    public bool LocalDeleted { get; set; }
    public long LocalBaseRevision { get; set; }
    public ExtensionDataObject Remote { get; set; } = new();
}
