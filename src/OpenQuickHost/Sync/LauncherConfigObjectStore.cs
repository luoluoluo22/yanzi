using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace OpenQuickHost.Sync;

internal static class LauncherConfigObjectStore
{
    public const string DirectoryPath = "state/config-objects";
    public const string ManifestPath = "state/config-manifest.json";
    public const string ChangeDirectoryPath = "state/config-changes";

    public static readonly LauncherConfigObjectDefinition[] Definitions =
    [
        new("settings.general", "settings-general.json"),
        new("settings.ai", "settings-ai.json"),
        new("settings.hotkeys", "settings-hotkeys.json"),
        new("settings.mouseTriggers", "settings-mouse-triggers.json"),
        new("quickPanel.groups", "quick-panel-groups.json"),
        new("quickPanel.favorites", "quick-panel-favorites.json"),
        new("radialMenu.pages", "radial-menu-pages.json"),
        new("yanm.layout", "yanm-layout.json"),
        new("yanyu.rules", "yanyu-rules.json"),
        new("window.controls", "window-controls.json")
    ];

    public static IReadOnlyList<LauncherConfigObjectEnvelope> Split(CloudQuickPanelConfigSnapshot snapshot, DateTime updatedAtUtc)
    {
        var updatedAt = updatedAtUtc.ToString("O");
        var sourceDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        var sourceDeviceName = DeviceIdentityStore.GetDesktopDisplayName();
        return
        [
            Create("settings.general", updatedAt, sourceDeviceId, sourceDeviceName, new LauncherGeneralSettingsPayload
            {
                ThemeMode = snapshot.ThemeMode,
                LaunchAtStartup = snapshot.LaunchAtStartup,
                RefreshCloudOnStartup = snapshot.RefreshCloudOnStartup,
                CloseToTray = snapshot.CloseToTray,
                EnableAgentApi = snapshot.EnableAgentApi,
                AgentApiPort = snapshot.AgentApiPort
            }),
            Create("settings.ai", updatedAt, sourceDeviceId, sourceDeviceName, new LauncherAiSettingsPayload
            {
                AiBaseUrl = snapshot.AiBaseUrl,
                AiApiKey = snapshot.AiApiKey,
                AiModel = snapshot.AiModel
            }),
            Create("settings.hotkeys", updatedAt, sourceDeviceId, sourceDeviceName, new LauncherHotkeySettingsPayload
            {
                LauncherHotkey = snapshot.LauncherHotkey,
                WindowSnapAssistHotkey = snapshot.WindowSnapAssistHotkey
            }),
            Create("settings.mouseTriggers", updatedAt, sourceDeviceId, sourceDeviceName, new LauncherMouseTriggerSettingsPayload
            {
                QuickPanelTrigger = snapshot.QuickPanelTrigger,
                QuickPanelMouseTriggers = snapshot.QuickPanelMouseTriggers,
                MouseGestureTriggerMode = snapshot.MouseGestureTriggerMode,
                WindowSnapAssistMouseTriggerMode = snapshot.WindowSnapAssistMouseTriggerMode
            }),
            Create("quickPanel.groups", updatedAt, sourceDeviceId, sourceDeviceName, new QuickPanelGroupsPayload
            {
                QuickPanelSlots = snapshot.QuickPanelSlots,
                QuickPanelGlobalGroups = snapshot.QuickPanelGlobalGroups,
                QuickPanelContextGroups = snapshot.QuickPanelContextGroups,
                SelectedQuickPanelGlobalGroupId = snapshot.SelectedQuickPanelGlobalGroupId,
                SelectedQuickPanelContextGroupId = snapshot.SelectedQuickPanelContextGroupId
            }),
            Create("quickPanel.favorites", updatedAt, sourceDeviceId, sourceDeviceName, new QuickPanelFavoritesPayload
            {
                GlobalFavoriteExtensionIds = snapshot.GlobalFavoriteExtensionIds,
                ContextFavoriteExtensionIds = snapshot.ContextFavoriteExtensionIds,
                DisabledExtensionIds = snapshot.DisabledExtensionIds,
                PinnedSearchScopeCommandIds = snapshot.PinnedSearchScopeCommandIds
            }),
            Create("radialMenu.pages", updatedAt, sourceDeviceId, sourceDeviceName, new RadialMenuPayload
            {
                RadialMenu = snapshot.RadialMenu
            }),
            Create("yanm.layout", updatedAt, sourceDeviceId, sourceDeviceName, new YanmLayoutPayload
            {
                Yanm = snapshot.Yanm
            }),
            Create("yanyu.rules", updatedAt, sourceDeviceId, sourceDeviceName, new YanyuRulesPayload
            {
                YanyuRules = snapshot.YanyuRules
            }),
            Create("window.controls", updatedAt, sourceDeviceId, sourceDeviceName, new WindowControlsPayload
            {
                EnableWindowSnapAssist = snapshot.EnableWindowSnapAssist,
                WindowSnapAssistCustomLayouts = snapshot.WindowSnapAssistCustomLayouts,
                WindowBindings = snapshot.WindowBindings,
                YarnSelect = snapshot.YarnSelect
            })
        ];
    }

    public static IReadOnlyList<LauncherConfigObjectWrite> PrepareWrites(CloudQuickPanelConfigSnapshot snapshot, DateTime updatedAtUtc)
    {
        return Split(snapshot, updatedAtUtc)
            .Select(envelope =>
            {
                var bytes = Serialize(envelope);
                return new LauncherConfigObjectWrite(
                    envelope.ObjectId,
                    GetPath(envelope.ObjectId),
                    envelope,
                    bytes,
                    ComputeSha256(bytes));
            })
            .ToList();
    }

    public static LauncherConfigManifest CreateManifest(IEnumerable<LauncherConfigObjectWrite> writes, DateTime updatedAtUtc)
    {
        var sourceDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        var sourceDeviceName = DeviceIdentityStore.GetDesktopDisplayName();
        var writeList = writes.ToList();
        return new LauncherConfigManifest
        {
            Revision = ToRevision(updatedAtUtc),
            UpdatedAtUtc = updatedAtUtc.ToString("O"),
            UpdatedByDeviceId = sourceDeviceId,
            UpdatedByDeviceName = sourceDeviceName,
            Objects = writeList
                .Select(write => new LauncherConfigManifestObject
                {
                    ObjectId = write.ObjectId,
                    Path = write.Path,
                    UpdatedAtUtc = write.Envelope.UpdatedAtUtc,
                    UpdatedByDeviceId = write.Envelope.UpdatedByDeviceId,
                    Sha256 = write.Sha256,
                    SizeBytes = write.Bytes.Length,
                    Deleted = write.Envelope.Deleted
                })
                .OrderBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public static LauncherConfigChangeSet CreateChangeSet(
        IEnumerable<LauncherConfigObjectWrite> writes,
        DateTime updatedAtUtc,
        string reason)
    {
        var sourceDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        var sourceDeviceName = DeviceIdentityStore.GetDesktopDisplayName();
        return new LauncherConfigChangeSet
        {
            ChangeId = $"{ToRevision(updatedAtUtc)}-{sourceDeviceId}",
            Revision = ToRevision(updatedAtUtc),
            CreatedAtUtc = updatedAtUtc.ToString("O"),
            SourceDeviceId = sourceDeviceId,
            SourceDeviceName = sourceDeviceName,
            Reason = reason,
            Changes = writes
                .Select(write => new LauncherConfigObjectChange
                {
                    ObjectId = write.ObjectId,
                    Operation = write.Envelope.Deleted ? "delete" : "upsert",
                    Path = write.Path,
                    Sha256 = write.Sha256,
                    SizeBytes = write.Bytes.Length
                })
                .OrderBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public static string GetChangePath(DateTime updatedAtUtc)
    {
        var sourceDeviceId = SanitizePathSegment(DeviceIdentityStore.GetOrCreateDesktopDeviceId());
        return $"{ChangeDirectoryPath}/{updatedAtUtc:yyyyMMddHHmmssfff}-{sourceDeviceId}.json";
    }

    public static string GetPath(string objectId)
    {
        var definition = Definitions.FirstOrDefault(item => item.ObjectId.Equals(objectId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未知配置对象：{objectId}");
        return $"{DirectoryPath}/{definition.FileName}";
    }

    public static CloudQuickPanelConfigSnapshot? Compose(
        CloudQuickPanelConfigSnapshot? baseSnapshot,
        IEnumerable<LauncherConfigObjectEnvelope> objects,
        out DateTime updatedAtUtc)
    {
        var snapshot = baseSnapshot == null ? new CloudQuickPanelConfigSnapshot() : CloneByJson(baseSnapshot);
        var applied = false;
        updatedAtUtc = DateTime.MinValue;
        foreach (var envelope in objects)
        {
            if (!TryApply(snapshot, envelope))
            {
                continue;
            }

            applied = true;
            var objectUpdatedAtUtc = TryParseUtc(envelope.UpdatedAtUtc) ?? DateTime.MinValue;
            if (objectUpdatedAtUtc > updatedAtUtc)
            {
                updatedAtUtc = objectUpdatedAtUtc;
            }
        }

        if (!applied)
        {
            return baseSnapshot;
        }

        snapshot.UpdatedAtUtc = updatedAtUtc == DateTime.MinValue ? baseSnapshot?.UpdatedAtUtc : updatedAtUtc.ToString("O");
        snapshot.HasUserContent = CloudQuickPanelConfigSnapshot.HasMeaningfulUserContent(snapshot);
        snapshot.IsInitialDefaultConfig = !snapshot.HasUserContent;
        return snapshot;
    }

    public static LauncherConfigObjectEnvelope? Deserialize(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        return JsonSerializer.Deserialize<LauncherConfigObjectEnvelope>(Encoding.UTF8.GetString(bytes), JsonOptions);
    }

    public static byte[] Serialize(LauncherConfigObjectEnvelope envelope)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    public static byte[] SerializeManifest(LauncherConfigManifest manifest)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public static byte[] SerializeChangeSet(LauncherConfigChangeSet changeSet)
    {
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(changeSet, JsonOptions));
    }

    private static LauncherConfigObjectEnvelope Create<T>(string objectId, string updatedAtUtc, string sourceDeviceId, string sourceDeviceName, T payload)
    {
        return new LauncherConfigObjectEnvelope
        {
            ObjectId = objectId,
            UpdatedAtUtc = updatedAtUtc,
            UpdatedByDeviceId = sourceDeviceId,
            UpdatedByDeviceName = sourceDeviceName,
            Payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
        };
    }

    private static bool TryApply(CloudQuickPanelConfigSnapshot snapshot, LauncherConfigObjectEnvelope envelope)
    {
        try
        {
            switch (envelope.ObjectId)
            {
                case "settings.general":
                    var general = envelope.Payload.Deserialize<LauncherGeneralSettingsPayload>(JsonOptions);
                    if (general == null) return false;
                    snapshot.ThemeMode = general.ThemeMode ?? snapshot.ThemeMode;
                    snapshot.LaunchAtStartup = general.LaunchAtStartup;
                    snapshot.RefreshCloudOnStartup = general.RefreshCloudOnStartup;
                    snapshot.CloseToTray = general.CloseToTray;
                    snapshot.EnableAgentApi = general.EnableAgentApi;
                    snapshot.AgentApiPort = general.AgentApiPort;
                    return true;
                case "settings.ai":
                    var ai = envelope.Payload.Deserialize<LauncherAiSettingsPayload>(JsonOptions);
                    if (ai == null) return false;
                    snapshot.AiBaseUrl = ai.AiBaseUrl;
                    snapshot.AiApiKey = ai.AiApiKey;
                    snapshot.AiModel = ai.AiModel;
                    return true;
                case "settings.hotkeys":
                    var hotkeys = envelope.Payload.Deserialize<LauncherHotkeySettingsPayload>(JsonOptions);
                    if (hotkeys == null) return false;
                    snapshot.LauncherHotkey = hotkeys.LauncherHotkey ?? snapshot.LauncherHotkey;
                    snapshot.WindowSnapAssistHotkey = hotkeys.WindowSnapAssistHotkey ?? snapshot.WindowSnapAssistHotkey;
                    return true;
                case "settings.mouseTriggers":
                    var mouse = envelope.Payload.Deserialize<LauncherMouseTriggerSettingsPayload>(JsonOptions);
                    if (mouse == null) return false;
                    snapshot.QuickPanelTrigger = mouse.QuickPanelTrigger ?? snapshot.QuickPanelTrigger;
                    snapshot.QuickPanelMouseTriggers = mouse.QuickPanelMouseTriggers ?? snapshot.QuickPanelMouseTriggers;
                    snapshot.MouseGestureTriggerMode = mouse.MouseGestureTriggerMode ?? snapshot.MouseGestureTriggerMode;
                    snapshot.WindowSnapAssistMouseTriggerMode = mouse.WindowSnapAssistMouseTriggerMode ?? snapshot.WindowSnapAssistMouseTriggerMode;
                    return true;
                case "quickPanel.groups":
                    var groups = envelope.Payload.Deserialize<QuickPanelGroupsPayload>(JsonOptions);
                    if (groups == null) return false;
                    snapshot.QuickPanelSlots = groups.QuickPanelSlots ?? snapshot.QuickPanelSlots;
                    snapshot.QuickPanelGlobalGroups = groups.QuickPanelGlobalGroups ?? snapshot.QuickPanelGlobalGroups;
                    snapshot.QuickPanelContextGroups = groups.QuickPanelContextGroups ?? snapshot.QuickPanelContextGroups;
                    snapshot.SelectedQuickPanelGlobalGroupId = groups.SelectedQuickPanelGlobalGroupId ?? snapshot.SelectedQuickPanelGlobalGroupId;
                    snapshot.SelectedQuickPanelContextGroupId = groups.SelectedQuickPanelContextGroupId ?? snapshot.SelectedQuickPanelContextGroupId;
                    return true;
                case "quickPanel.favorites":
                    var favorites = envelope.Payload.Deserialize<QuickPanelFavoritesPayload>(JsonOptions);
                    if (favorites == null) return false;
                    snapshot.GlobalFavoriteExtensionIds = favorites.GlobalFavoriteExtensionIds ?? snapshot.GlobalFavoriteExtensionIds;
                    snapshot.ContextFavoriteExtensionIds = favorites.ContextFavoriteExtensionIds ?? snapshot.ContextFavoriteExtensionIds;
                    snapshot.DisabledExtensionIds = favorites.DisabledExtensionIds ?? snapshot.DisabledExtensionIds;
                    snapshot.PinnedSearchScopeCommandIds = favorites.PinnedSearchScopeCommandIds ?? snapshot.PinnedSearchScopeCommandIds;
                    return true;
                case "radialMenu.pages":
                    var radial = envelope.Payload.Deserialize<RadialMenuPayload>(JsonOptions);
                    if (radial == null) return false;
                    snapshot.RadialMenu = radial.RadialMenu;
                    return true;
                case "yanm.layout":
                    var yanm = envelope.Payload.Deserialize<YanmLayoutPayload>(JsonOptions);
                    if (yanm == null) return false;
                    snapshot.Yanm = yanm.Yanm;
                    return true;
                case "yanyu.rules":
                    var yanyu = envelope.Payload.Deserialize<YanyuRulesPayload>(JsonOptions);
                    if (yanyu == null) return false;
                    snapshot.YanyuRules = yanyu.YanyuRules;
                    return true;
                case "window.controls":
                    var window = envelope.Payload.Deserialize<WindowControlsPayload>(JsonOptions);
                    if (window == null) return false;
                    snapshot.EnableWindowSnapAssist = window.EnableWindowSnapAssist;
                    snapshot.WindowSnapAssistCustomLayouts = window.WindowSnapAssistCustomLayouts ?? snapshot.WindowSnapAssistCustomLayouts;
                    snapshot.WindowBindings = window.WindowBindings ?? snapshot.WindowBindings;
                    snapshot.YarnSelect = window.YarnSelect;
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static T CloneByJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? value;
    }

    private static DateTime? TryParseUtc(string? value)
    {
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static long ToRevision(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string SanitizePathSegment(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var chars = normalized.Select(static ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        return string.Concat(chars);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}

internal sealed record LauncherConfigObjectDefinition(string ObjectId, string FileName);

internal sealed record LauncherConfigObjectWrite(
    string ObjectId,
    string Path,
    LauncherConfigObjectEnvelope Envelope,
    byte[] Bytes,
    string Sha256);

internal sealed class LauncherConfigObjectEnvelope
{
    public int SchemaVersion { get; set; } = 1;

    public string ObjectId { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public string? UpdatedByDeviceId { get; set; }

    public string? UpdatedByDeviceName { get; set; }

    public bool Deleted { get; set; }

    public JsonElement Payload { get; set; }
}

internal sealed class LauncherConfigManifest
{
    public int SchemaVersion { get; set; } = 1;

    public long Revision { get; set; }

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public string? UpdatedByDeviceId { get; set; }

    public string? UpdatedByDeviceName { get; set; }

    public List<LauncherConfigManifestObject> Objects { get; set; } = [];
}

internal sealed class LauncherConfigManifestObject
{
    public string ObjectId { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public string? UpdatedByDeviceId { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    public bool Deleted { get; set; }
}

internal sealed class LauncherConfigChangeSet
{
    public int SchemaVersion { get; set; } = 1;

    public string ChangeId { get; set; } = string.Empty;

    public long Revision { get; set; }

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string? SourceDeviceId { get; set; }

    public string? SourceDeviceName { get; set; }

    public string Reason { get; set; } = string.Empty;

    public List<LauncherConfigObjectChange> Changes { get; set; } = [];
}

internal sealed class LauncherConfigObjectChange
{
    public string ObjectId { get; set; } = string.Empty;

    public string Operation { get; set; } = "upsert";

    public string Path { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int SizeBytes { get; set; }
}

internal sealed class LauncherGeneralSettingsPayload
{
    public string? ThemeMode { get; set; }
    public bool LaunchAtStartup { get; set; }
    public bool RefreshCloudOnStartup { get; set; }
    public bool CloseToTray { get; set; }
    public bool EnableAgentApi { get; set; }
    public int AgentApiPort { get; set; }
}

internal sealed class LauncherAiSettingsPayload
{
    public string? AiBaseUrl { get; set; }
    public string? AiApiKey { get; set; }
    public string? AiModel { get; set; }
}

internal sealed class LauncherHotkeySettingsPayload
{
    public string? LauncherHotkey { get; set; }
    public string? WindowSnapAssistHotkey { get; set; }
}

internal sealed class LauncherMouseTriggerSettingsPayload
{
    public string? QuickPanelTrigger { get; set; }
    public QuickPanelMouseTriggerSettings? QuickPanelMouseTriggers { get; set; }
    public string? MouseGestureTriggerMode { get; set; }
    public string? WindowSnapAssistMouseTriggerMode { get; set; }
}

internal sealed class QuickPanelGroupsPayload
{
    public List<string?>? QuickPanelSlots { get; set; }
    public List<QuickPanelGroupSettings>? QuickPanelGlobalGroups { get; set; }
    public List<QuickPanelGroupSettings>? QuickPanelContextGroups { get; set; }
    public string? SelectedQuickPanelGlobalGroupId { get; set; }
    public string? SelectedQuickPanelContextGroupId { get; set; }
}

internal sealed class QuickPanelFavoritesPayload
{
    public List<string>? GlobalFavoriteExtensionIds { get; set; }
    public List<string>? ContextFavoriteExtensionIds { get; set; }
    public List<string>? DisabledExtensionIds { get; set; }
    public List<string>? PinnedSearchScopeCommandIds { get; set; }
}

internal sealed class RadialMenuPayload
{
    public RadialMenuSettings? RadialMenu { get; set; }
}

internal sealed class YanmLayoutPayload
{
    public YanmSettings? Yanm { get; set; }
}

internal sealed class YanyuRulesPayload
{
    public List<YanyuRuleSettings>? YanyuRules { get; set; }
}

internal sealed class WindowControlsPayload
{
    public bool EnableWindowSnapAssist { get; set; }
    public List<WindowSnapAssistCustomLayoutSettings>? WindowSnapAssistCustomLayouts { get; set; }
    public WindowBindingSettings? WindowBindings { get; set; }
    public YarnSelectSettings? YarnSelect { get; set; }
}
