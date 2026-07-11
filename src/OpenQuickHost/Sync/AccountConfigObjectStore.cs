using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenQuickHost.Sync;

internal static class AccountConfigObjectStore
{
    public const string QuickPanelIndexObjectId = "quickPanel.groupIndex";
    public const string RadialMenuIndexObjectId = "radialMenu.pageIndex";
    public const string QuickPanelGlobalPrefix = "quickPanel.globalGroup.";
    public const string QuickPanelContextPrefix = "quickPanel.contextGroup.";
    public const string RadialMenuPagePrefix = "radialMenu.page.";

    public static IReadOnlyList<AccountConfigObjectWrite> PrepareWrites(
        CloudQuickPanelConfigSnapshot snapshot,
        DateTime updatedAtUtc,
        IEnumerable<string> existingObjectIds,
        IEnumerable<string> deletableDynamicObjectIds)
    {
        var writes = LauncherConfigObjectStore.Split(snapshot, updatedAtUtc)
            .Where(static item =>
                !item.ObjectId.Equals("quickPanel.groups", StringComparison.OrdinalIgnoreCase) &&
                !item.ObjectId.Equals("radialMenu.pages", StringComparison.OrdinalIgnoreCase))
            .Select(static item => new AccountConfigObjectWrite(item.ObjectId, item))
            .ToList();
        var currentDynamicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var globalIds = new List<string>();
        foreach (var group in snapshot.QuickPanelGlobalGroups)
        {
            var objectId = BuildObjectId(QuickPanelGlobalPrefix, group.Id);
            globalIds.Add(objectId);
            currentDynamicIds.Add(objectId);
            writes.Add(new AccountConfigObjectWrite(objectId, CreateEnvelope(
                objectId,
                updatedAtUtc,
                new QuickPanelGroupObjectPayload { Scope = "global", Group = Clone(group) })));
        }

        var contextIds = new List<string>();
        foreach (var group in snapshot.QuickPanelContextGroups)
        {
            var objectId = BuildObjectId(QuickPanelContextPrefix, group.Id);
            contextIds.Add(objectId);
            currentDynamicIds.Add(objectId);
            writes.Add(new AccountConfigObjectWrite(objectId, CreateEnvelope(
                objectId,
                updatedAtUtc,
                new QuickPanelGroupObjectPayload { Scope = "context", Group = Clone(group) })));
        }

        writes.Add(new AccountConfigObjectWrite(QuickPanelIndexObjectId, CreateEnvelope(
            QuickPanelIndexObjectId,
            updatedAtUtc,
            new QuickPanelGroupIndexPayload
            {
                QuickPanelSlots = snapshot.QuickPanelSlots.ToList(),
                GlobalObjectIds = globalIds,
                ContextObjectIds = contextIds,
                SelectedGlobalGroupId = snapshot.SelectedQuickPanelGlobalGroupId,
                SelectedContextGroupId = snapshot.SelectedQuickPanelContextGroupId
            })));

        var radial = snapshot.RadialMenu == null ? new RadialMenuSettings() : Clone(snapshot.RadialMenu);
        var pageIds = new List<string>();
        foreach (var page in radial.Pages)
        {
            var objectId = BuildObjectId(RadialMenuPagePrefix, page.Id);
            pageIds.Add(objectId);
            currentDynamicIds.Add(objectId);
            writes.Add(new AccountConfigObjectWrite(objectId, CreateEnvelope(
                objectId,
                updatedAtUtc,
                new RadialMenuPageObjectPayload { Page = Clone(page) })));
        }
        radial.Pages = [];
        writes.Add(new AccountConfigObjectWrite(RadialMenuIndexObjectId, CreateEnvelope(
            RadialMenuIndexObjectId,
            updatedAtUtc,
            new RadialMenuPageIndexPayload { Settings = radial, PageObjectIds = pageIds })));

        var deletableIds = deletableDynamicObjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existingObjectId in existingObjectIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var isDynamic = IsDynamicObjectId(existingObjectId);
            var isRetiredAggregate = existingObjectId.Equals("quickPanel.groups", StringComparison.OrdinalIgnoreCase) ||
                                     existingObjectId.Equals("radialMenu.pages", StringComparison.OrdinalIgnoreCase);
            if ((isDynamic && deletableIds.Contains(existingObjectId) && !currentDynamicIds.Contains(existingObjectId)) || isRetiredAggregate)
            {
                writes.Add(new AccountConfigObjectWrite(existingObjectId, CreateTombstone(existingObjectId, updatedAtUtc)));
            }
        }

        return writes;
    }

    public static CloudQuickPanelConfigSnapshot? Apply(
        CloudQuickPanelConfigSnapshot? baseSnapshot,
        IEnumerable<LauncherConfigObjectEnvelope> objects)
    {
        var snapshot = baseSnapshot == null ? new CloudQuickPanelConfigSnapshot() : Clone(baseSnapshot);
        var objectMap = objects
            .Where(static item => !item.Deleted)
            .GroupBy(static item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var applied = false;
        var latestUpdatedAtUtc = TryParseUtc(snapshot.UpdatedAtUtc) ?? DateTime.MinValue;

        if (objectMap.TryGetValue(QuickPanelIndexObjectId, out var quickIndexEnvelope))
        {
            var index = quickIndexEnvelope.Payload.Deserialize<QuickPanelGroupIndexPayload>(JsonOptions);
            if (index != null)
            {
                snapshot.QuickPanelSlots = index.QuickPanelSlots ?? snapshot.QuickPanelSlots;
                snapshot.QuickPanelGlobalGroups = ReadGroups(index.GlobalObjectIds, objectMap, "global");
                snapshot.QuickPanelContextGroups = ReadGroups(index.ContextObjectIds, objectMap, "context");
                snapshot.SelectedQuickPanelGlobalGroupId = index.SelectedGlobalGroupId ?? snapshot.SelectedQuickPanelGlobalGroupId;
                snapshot.SelectedQuickPanelContextGroupId = index.SelectedContextGroupId ?? snapshot.SelectedQuickPanelContextGroupId;
                applied = true;
                latestUpdatedAtUtc = Max(latestUpdatedAtUtc, TryParseUtc(quickIndexEnvelope.UpdatedAtUtc));
            }
        }

        if (objectMap.TryGetValue(RadialMenuIndexObjectId, out var radialIndexEnvelope))
        {
            var index = radialIndexEnvelope.Payload.Deserialize<RadialMenuPageIndexPayload>(JsonOptions);
            if (index?.Settings != null)
            {
                var radial = Clone(index.Settings);
                radial.Pages = ReadRadialPages(index.PageObjectIds, objectMap);
                snapshot.RadialMenu = radial;
                applied = true;
                latestUpdatedAtUtc = Max(latestUpdatedAtUtc, TryParseUtc(radialIndexEnvelope.UpdatedAtUtc));
            }
        }

        if (!applied)
        {
            return baseSnapshot;
        }

        foreach (var envelope in objectMap.Values)
        {
            latestUpdatedAtUtc = Max(latestUpdatedAtUtc, TryParseUtc(envelope.UpdatedAtUtc));
        }

        snapshot.UpdatedAtUtc = latestUpdatedAtUtc == DateTime.MinValue
            ? snapshot.UpdatedAtUtc
            : latestUpdatedAtUtc.ToString("O");
        snapshot.HasUserContent = CloudQuickPanelConfigSnapshot.HasMeaningfulUserContent(snapshot);
        snapshot.IsInitialDefaultConfig = !snapshot.HasUserContent;
        return snapshot;
    }

    public static bool IsDynamicObjectId(string objectId) =>
        objectId.StartsWith(QuickPanelGlobalPrefix, StringComparison.OrdinalIgnoreCase) ||
        objectId.StartsWith(QuickPanelContextPrefix, StringComparison.OrdinalIgnoreCase) ||
        objectId.StartsWith(RadialMenuPagePrefix, StringComparison.OrdinalIgnoreCase);

    private static List<QuickPanelGroupSettings> ReadGroups(
        IEnumerable<string>? objectIds,
        IReadOnlyDictionary<string, LauncherConfigObjectEnvelope> objectMap,
        string scope)
    {
        var groups = new List<QuickPanelGroupSettings>();
        foreach (var objectId in objectIds ?? [])
        {
            if (!objectMap.TryGetValue(objectId, out var envelope)) continue;
            var payload = envelope.Payload.Deserialize<QuickPanelGroupObjectPayload>(JsonOptions);
            if (payload?.Group != null && string.Equals(payload.Scope, scope, StringComparison.OrdinalIgnoreCase))
            {
                groups.Add(Clone(payload.Group));
            }
        }
        return groups;
    }

    private static List<RadialMenuPageSettings> ReadRadialPages(
        IEnumerable<string>? objectIds,
        IReadOnlyDictionary<string, LauncherConfigObjectEnvelope> objectMap)
    {
        var pages = new List<RadialMenuPageSettings>();
        foreach (var objectId in objectIds ?? [])
        {
            if (!objectMap.TryGetValue(objectId, out var envelope)) continue;
            var payload = envelope.Payload.Deserialize<RadialMenuPageObjectPayload>(JsonOptions);
            if (payload?.Page != null)
            {
                pages.Add(Clone(payload.Page));
            }
        }
        return pages;
    }

    private static LauncherConfigObjectEnvelope CreateEnvelope<T>(string objectId, DateTime updatedAtUtc, T payload) => new()
    {
        ObjectId = objectId,
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime().ToString("O"),
        UpdatedByDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
        UpdatedByDeviceName = DeviceIdentityStore.GetDesktopDisplayName(),
        Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
    };

    private static LauncherConfigObjectEnvelope CreateTombstone(string objectId, DateTime updatedAtUtc)
    {
        var envelope = CreateEnvelope(objectId, updatedAtUtc, new { });
        envelope.Deleted = true;
        return envelope;
    }

    private static string BuildObjectId(string prefix, string sourceId)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceId) ? "missing" : sourceId.Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return prefix + hash;
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? value;
    }

    private static DateTime? TryParseUtc(string? value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static DateTime Max(DateTime left, DateTime? right) => right.HasValue && right.Value > left ? right.Value : left;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record AccountConfigObjectWrite(string ObjectId, LauncherConfigObjectEnvelope Envelope);

internal sealed class QuickPanelGroupIndexPayload
{
    public List<string?>? QuickPanelSlots { get; set; }
    public List<string> GlobalObjectIds { get; set; } = [];
    public List<string> ContextObjectIds { get; set; } = [];
    public string? SelectedGlobalGroupId { get; set; }
    public string? SelectedContextGroupId { get; set; }
}

internal sealed class QuickPanelGroupObjectPayload
{
    public string Scope { get; set; } = string.Empty;
    public QuickPanelGroupSettings? Group { get; set; }
}

internal sealed class RadialMenuPageIndexPayload
{
    public RadialMenuSettings? Settings { get; set; }
    public List<string> PageObjectIds { get; set; } = [];
}

internal sealed class RadialMenuPageObjectPayload
{
    public RadialMenuPageSettings? Page { get; set; }
}
