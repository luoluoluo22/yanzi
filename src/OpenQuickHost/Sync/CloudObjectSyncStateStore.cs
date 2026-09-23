using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;

namespace OpenQuickHost.Sync;

internal static class CloudObjectSyncStateStore
{
    private static readonly object IoLock = new();

    public static CloudObjectSyncState Load(string? userId) => LoadAt(HostAssets.DataRootPath, userId);
    public static void Save(CloudObjectSyncState state) => SaveAt(HostAssets.DataRootPath, state);

    internal static string AccountPath(string root, string userId) => Path.Combine(root, "SyncState",
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userId))).ToLowerInvariant() + ".json");

    internal static CloudObjectSyncState LoadAt(string root, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return NewState(userId);
        lock (IoLock)
        {
            try
            {
                var path = AccountPath(root, userId);
                CloudObjectSyncState state;
                if (!File.Exists(path))
                {
                    var legacyPath = Path.Combine(root, "cloud-object-sync-state.json");
                    if (!File.Exists(legacyPath)) return NewState(userId);
                    state = Read(legacyPath);
                    if (!string.Equals(state.UserId, userId, StringComparison.Ordinal)) return NewState(userId);
                }
                else
                {
                    try { state = Read(path); }
                    catch (Exception ex) when (ex is JsonException or IOException)
                    {
                        // Keep the damaged original until a recovered state is successfully saved.
                        state = Read(path + ".bak");
                        state.RecoveredFromBackup = true;
                    }
                    if (!string.Equals(state.UserId, userId, StringComparison.Ordinal))
                        throw new JsonException("同步记录不属于当前账号。");
                }
                if (state.SchemaVersion is < 1 or > 5) throw new JsonException("同步记录版本不受支持。");
                state.Objects ??= new(StringComparer.OrdinalIgnoreCase);
                if (!state.LocalBaselinesInitialized)
                {
                    state.LocalBaselines = state.Objects.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                    state.LocalBaselinesInitialized = true;
                }
                state.PendingObjectIds ??= [];
                state.PendingOperations ??= new(StringComparer.OrdinalIgnoreCase);
                state.KnownLocalDynamicObjectIds ??= [];
                state.Conflicts ??= new(StringComparer.OrdinalIgnoreCase);
                foreach (var objectId in state.PendingObjectIds)
                {
                    if (!state.PendingOperations.ContainsKey(objectId))
                        state.PendingOperations[objectId] = new CloudObjectPendingOperation
                        {
                            ObjectId = objectId, CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                            LastExpectedRevision = state.Objects.TryGetValue(objectId, out var cached) ? cached.Revision : 0
                        };
                }
                // Only genuinely migrated legacy entries acquire a baseline here. Existing revision 0 is intentional.
                state.PendingObjectIds = state.PendingOperations.Keys.ToList();
                var scrubbedSecrets = ScrubCachedAiSecrets(state);
                state.SchemaVersion = 5;
                if (scrubbedSecrets) SaveAt(root, state);
                return state;
            }
            catch (Exception ex)
            {
                var blocked = NewState(userId);
                blocked.PersistenceError = "本机同步记录暂时无法读取，已停止同步以保留未上传内容。";
                HostAssets.AppendLog($"Cloud object state read blocked: {ex.GetType().Name}");
                return blocked;
            }
        }
    }

    private static CloudObjectSyncState Read(string path)
    {
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("pendingObjectIds", out var pending) || pending.ValueKind != JsonValueKind.Array)
            throw new JsonException("同步记录结构不完整。");
        var state = JsonSerializer.Deserialize<CloudObjectSyncState>(json, JsonOptions)
            ?? throw new JsonException("同步记录为空。");
        if (string.IsNullOrWhiteSpace(state.UserId)) throw new JsonException("同步记录缺少账号信息。");
        if (state.Objects.Values.Any(value => value == null) || state.PendingObjectIds.Any(string.IsNullOrWhiteSpace) ||
            state.PendingOperations?.Values.Any(value => value == null) == true || state.Conflicts?.Values.Any(value => value == null) == true)
            throw new JsonException("同步记录包含无效项目。");
        return state;
    }

    internal static void SaveAt(string root, CloudObjectSyncState state)
    {
        if (!string.IsNullOrWhiteSpace(state.PersistenceError)) throw new IOException(state.PersistenceError);
        if (string.IsNullOrWhiteSpace(state.UserId)) throw new IOException("未登录，无法保存账号同步记录。");
        lock (IoLock)
        {
            var path = AccountPath(root, state.UserId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
                if (File.Exists(path))
                {
                    if (state.RecoveredFromBackup) File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    File.Replace(tempPath, path, path + ".bak");
                }
                else File.Move(tempPath, path);
                state.RecoveredFromBackup = false;
            }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        }
    }

    private static CloudObjectSyncState NewState(string? userId) => new()
    {
        LocalBaselinesInitialized = true,
        UserId = userId ?? string.Empty
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static JsonElement RemoveSensitiveAiFields(JsonElement payload, out bool changed)
    {
        changed = false;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(payload.GetRawText());
        }
        catch
        {
            return payload;
        }

        ScrubNode(node, ref changed);
        return changed && node != null
            ? JsonSerializer.SerializeToElement(node)
            : payload;
    }

    private static bool ScrubCachedAiSecrets(CloudObjectSyncState state)
    {
        var changed = false;
        if (state.Objects.TryGetValue("settings.ai", out var cached))
        {
            cached.Payload = RemoveSensitiveAiFields(cached.Payload, out var cachedChanged);
            changed |= cachedChanged;
        }
        if (state.LocalBaselines.TryGetValue("settings.ai", out var applied))
        {
            applied.Payload = RemoveSensitiveAiFields(applied.Payload, out var appliedChanged);
            changed |= appliedChanged;
        }
        if (state.Conflicts.TryGetValue("settings.ai", out var conflict))
        {
            conflict.LocalPayload = RemoveSensitiveAiFields(conflict.LocalPayload, out var conflictChanged);
            changed |= conflictChanged;
        }
        return changed;
    }

    private static void ScrubNode(JsonNode? node, ref bool changed)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(static pair => pair.Key).ToArray())
            {
                if (key.Equals("apiKey", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("aiApiKey", StringComparison.OrdinalIgnoreCase))
                {
                    var existingNode = obj[key];
                    var isAlreadyEmpty = existingNode is null ||
                                         existingNode is JsonValue value &&
                                         value.TryGetValue<string>(out var existingValue) &&
                                         existingValue.Length == 0;
                    if (!isAlreadyEmpty)
                    {
                        obj[key] = string.Empty;
                        changed = true;
                    }
                    continue;
                }
                ScrubNode(obj[key], ref changed);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                ScrubNode(child, ref changed);
            }
        }
    }
}

internal sealed class CloudObjectSyncState
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PersistenceError { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool RecoveredFromBackup { get; set; }

    public int SchemaVersion { get; set; } = 5;

    public string UserId { get; set; } = string.Empty;

    public long LastSyncedRevision { get; set; }

    public bool DownloadCursorValidated { get; set; }

    public int ServerProtocolVersion { get; set; }

    public bool ObjectSyncAvailable { get; set; }

    public bool ObjectHistoryAvailable { get; set; }

    public bool ObjectsAuthoritative { get; set; }

    public bool YanmObjectsInitialized { get; set; }

    public string CapabilitiesCheckedAtUtc { get; set; } = string.Empty;

    public Dictionary<string, CloudObjectSyncCacheEntry> Objects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool LocalBaselinesInitialized { get; set; }

    public Dictionary<string, CloudObjectSyncCacheEntry> LocalBaselines { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> PendingObjectIds { get; set; } = [];

    public Dictionary<string, CloudObjectPendingOperation> PendingOperations { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> KnownLocalDynamicObjectIds { get; set; } = [];

    public Dictionary<string, CloudObjectConflictRecord> Conflicts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CloudObjectConflictRecord
{
    public string ObjectId { get; set; } = string.Empty;

    public string DetectedAtUtc { get; set; } = string.Empty;

    public int LocalSchemaVersion { get; set; } = 1;

    public bool LocalDeleted { get; set; }

    public JsonElement LocalPayload { get; set; }

    public long RemoteRevision { get; set; }

    public string RemoteUpdatedAtUtc { get; set; } = string.Empty;

    public string? RemoteDeviceId { get; set; }

    public string? RemoteDeviceName { get; set; }
}

internal sealed class CloudObjectPendingOperation
{
    public string ObjectId { get; set; } = string.Empty;

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string LastAttemptAtUtc { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public long LastExpectedRevision { get; set; }

    public long LastObservedRemoteRevision { get; set; }

    public string LastError { get; set; } = string.Empty;
}

internal sealed class CloudObjectSyncCacheEntry
{
    public string ObjectId { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    public long Revision { get; set; }

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public string? UpdatedByDeviceId { get; set; }

    public string? UpdatedByDeviceName { get; set; }

    public bool Deleted { get; set; }

    public JsonElement Payload { get; set; }

    public static CloudObjectSyncCacheEntry FromRecord(CloudSyncObjectRecord record) => new()
    {
        ObjectId = record.ObjectId,
        SchemaVersion = record.SchemaVersion,
        Revision = record.Revision,
        UpdatedAtUtc = record.UpdatedAtUtc,
        UpdatedByDeviceId = record.UpdatedByDeviceId,
        UpdatedByDeviceName = record.UpdatedByDeviceName,
        Deleted = record.Deleted,
        Payload = record.Payload.Clone()
    };
}
