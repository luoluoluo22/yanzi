using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;

namespace OpenQuickHost.Sync;

internal static class CloudObjectSyncStateStore
{
    private static string StatePath => HostAssets.ResolveDataFilePath("cloud-object-sync-state.json");

    public static CloudObjectSyncState Load(string? userId)
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return NewState(userId);
            }

            var state = JsonSerializer.Deserialize<CloudObjectSyncState>(File.ReadAllText(StatePath), JsonOptions)
                ?? NewState(userId);
            if (!string.Equals(state.UserId, userId, StringComparison.Ordinal))
            {
                return NewState(userId);
            }

            state.Objects ??= new(StringComparer.OrdinalIgnoreCase);
            state.PendingObjectIds ??= [];
            state.PendingOperations ??= new(StringComparer.OrdinalIgnoreCase);
            state.KnownLocalDynamicObjectIds ??= [];
            state.Conflicts ??= new(StringComparer.OrdinalIgnoreCase);
            foreach (var objectId in state.PendingObjectIds)
            {
                if (!state.PendingOperations.ContainsKey(objectId))
                {
                    state.PendingOperations[objectId] = new CloudObjectPendingOperation
                    {
                        ObjectId = objectId,
                        CreatedAtUtc = DateTime.UtcNow.ToString("O")
                    };
                }
                var pending = state.PendingOperations[objectId];
                if (pending.AttemptCount == 0 && pending.LastExpectedRevision == 0 &&
                    state.Objects.TryGetValue(objectId, out var cached) && cached.Revision > 0)
                {
                    pending.LastExpectedRevision = cached.Revision;
                }
            }
            var scrubbedSecrets = ScrubCachedAiSecrets(state);
            state.SchemaVersion = 5;
            if (scrubbedSecrets)
            {
                Save(state);
            }
            return state;
        }
        catch
        {
            return NewState(userId);
        }
    }

    public static void Save(CloudObjectSyncState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var tempPath = StatePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tempPath, StatePath, overwrite: true);
    }

    private static CloudObjectSyncState NewState(string? userId) => new()
    {
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
    public int SchemaVersion { get; set; } = 5;

    public string UserId { get; set; } = string.Empty;

    public long LastSyncedRevision { get; set; }

    public int ServerProtocolVersion { get; set; }

    public bool ObjectSyncAvailable { get; set; }

    public bool ObjectHistoryAvailable { get; set; }

    public bool ObjectsAuthoritative { get; set; }

    public bool YanmObjectsInitialized { get; set; }

    public string CapabilitiesCheckedAtUtc { get; set; } = string.Empty;

    public Dictionary<string, CloudObjectSyncCacheEntry> Objects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
