using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

/// <summary>
/// 将燕幕拆为一个布局对象、一个状态索引和每个状态 key 的独立对象。
/// 布局与运行内容互不拥有对方的数据，避免移动组件时覆盖便签内容。
/// </summary>
internal static class YanmObjectStore
{
    public const string LayoutObjectId = "yanm.layout";
    public const string ComponentStateIndexObjectId = "yanm.componentStateIndex";
    public const string ComponentStatePrefix = "yanm.componentState.";

    public static IReadOnlyList<AccountConfigObjectWrite> PrepareWrites(
        YanmSettings yanm,
        DateTime updatedAtUtc,
        IEnumerable<string> existingObjectIds,
        IEnumerable<string> deletableDynamicObjectIds)
    {
        var normalized = Clone(yanm);
        normalized.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var componentState = normalized.ComponentState
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(static pair => new KeyValuePair<string, string>(pair.Key.Trim(), pair.Value ?? string.Empty))
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        normalized.ComponentState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var writes = new List<AccountConfigObjectWrite>
        {
            CreateWrite(LayoutObjectId, updatedAtUtc, new YanmLayoutObjectPayload { Settings = normalized })
        };

        var stateObjectIds = new List<string>();
        var currentDynamicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in componentState)
        {
            var objectId = BuildComponentStateObjectId(pair.Key);
            stateObjectIds.Add(objectId);
            currentDynamicIds.Add(objectId);
            writes.Add(CreateWrite(objectId, updatedAtUtc, new YanmComponentStateObjectPayload
            {
                StateKey = pair.Key,
                Value = pair.Value
            }));
        }

        writes.Add(CreateWrite(ComponentStateIndexObjectId, updatedAtUtc, new YanmComponentStateIndexPayload
        {
            StateObjectIds = stateObjectIds
        }));

        var deletableIds = deletableDynamicObjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var objectId in existingObjectIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsDynamicObjectId(objectId) &&
                deletableIds.Contains(objectId) &&
                !currentDynamicIds.Contains(objectId))
            {
                writes.Add(CreateTombstone(objectId, updatedAtUtc));
            }
        }

        return writes;
    }

    public static YanmSettings Apply(
        YanmSettings? baseSettings,
        IEnumerable<LauncherConfigObjectEnvelope> objects,
        out bool applied,
        out DateTime? latestUpdatedAtUtc)
    {
        var result = baseSettings == null ? new YanmSettings() : Clone(baseSettings);
        result.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var objectMap = objects
            .GroupBy(static item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
        applied = false;
        latestUpdatedAtUtc = null;

        if (objectMap.TryGetValue(LayoutObjectId, out var layoutEnvelope) && !layoutEnvelope.Deleted)
        {
            var layout = layoutEnvelope.Payload.Deserialize<YanmLayoutObjectPayload>(JsonOptions);
            if (layout?.Settings != null)
            {
                var retainedState = result.ComponentState;
                result = Clone(layout.Settings);
                result.ComponentState = retainedState;
                applied = true;
                latestUpdatedAtUtc = Max(latestUpdatedAtUtc, ParseUtc(layoutEnvelope.UpdatedAtUtc));
            }
        }

        if (objectMap.TryGetValue(ComponentStateIndexObjectId, out var indexEnvelope) && !indexEnvelope.Deleted)
        {
            var index = indexEnvelope.Payload.Deserialize<YanmComponentStateIndexPayload>(JsonOptions);
            if (index != null)
            {
                var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var objectId in index.StateObjectIds ?? [])
                {
                    if (!objectMap.TryGetValue(objectId, out var stateEnvelope) || stateEnvelope.Deleted)
                    {
                        continue;
                    }
                    var payload = stateEnvelope.Payload.Deserialize<YanmComponentStateObjectPayload>(JsonOptions);
                    if (payload == null || string.IsNullOrWhiteSpace(payload.StateKey))
                    {
                        continue;
                    }
                    state[payload.StateKey.Trim()] = payload.Value ?? string.Empty;
                    latestUpdatedAtUtc = Max(latestUpdatedAtUtc, ParseUtc(stateEnvelope.UpdatedAtUtc));
                }
                result.ComponentState = state;
                applied = true;
                latestUpdatedAtUtc = Max(latestUpdatedAtUtc, ParseUtc(indexEnvelope.UpdatedAtUtc));
            }
        }

        return result;
    }

    public static bool IsObjectId(string objectId) =>
        objectId.Equals(LayoutObjectId, StringComparison.OrdinalIgnoreCase) ||
        objectId.Equals(ComponentStateIndexObjectId, StringComparison.OrdinalIgnoreCase) ||
        IsDynamicObjectId(objectId);

    public static bool IsDynamicObjectId(string objectId) =>
        objectId.StartsWith(ComponentStatePrefix, StringComparison.OrdinalIgnoreCase);

    public static string BuildComponentStateObjectId(string stateKey)
    {
        var normalized = (stateKey ?? string.Empty).Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return ComponentStatePrefix + hash;
    }

    private static AccountConfigObjectWrite CreateWrite<T>(string objectId, DateTime updatedAtUtc, T payload) =>
        new(objectId, new LauncherConfigObjectEnvelope
        {
            ObjectId = objectId,
            UpdatedAtUtc = updatedAtUtc.ToUniversalTime().ToString("O"),
            UpdatedByDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            UpdatedByDeviceName = DeviceIdentityStore.GetDesktopDisplayName(),
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
        });

    private static AccountConfigObjectWrite CreateTombstone(string objectId, DateTime updatedAtUtc)
    {
        var write = CreateWrite(objectId, updatedAtUtc, new { });
        write.Envelope.Deleted = true;
        return write;
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? value;
    }

    private static DateTime? ParseUtc(string? value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static DateTime? Max(DateTime? left, DateTime? right) =>
        !left.HasValue ? right : !right.HasValue || left.Value >= right.Value ? left : right;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

internal sealed class YanmLayoutObjectPayload
{
    public YanmSettings? Settings { get; set; }
}

internal sealed class YanmComponentStateIndexPayload
{
    public List<string> StateObjectIds { get; set; } = [];
}

internal sealed class YanmComponentStateObjectPayload
{
    public string StateKey { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
