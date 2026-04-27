using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenQuickHost;

public static class AppSettingsStore
{
    public static string SettingsPath =>
        HostAssets.ResolveDataFilePath("appsettings.local.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return Normalize(new AppSettings());
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
        }
        catch
        {
            return Normalize(new AppSettings());
        }
    }

    public static void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.QuickPanelGlobalGroups ??= [];
        if (settings.QuickPanelGlobalGroups.Count == 0)
        {
            settings.QuickPanelGlobalGroups.Add(new QuickPanelGroupSettings
            {
                Id = "global-default",
                Name = "默认",
                Slots = settings.QuickPanelSlots.Take(12).ToList()
            });
        }

        settings.QuickPanelContextGroups ??= [];
        if (settings.QuickPanelContextGroups.Count == 0)
        {
            settings.QuickPanelContextGroups.Add(new QuickPanelGroupSettings
            {
                Id = "context-default",
                Name = "默认"
            });
        }

        foreach (var group in settings.QuickPanelGlobalGroups.Concat(settings.QuickPanelContextGroups))
        {
            group.Id = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("N") : group.Id;
            group.Name = string.IsNullOrWhiteSpace(group.Name) ? "未命名" : group.Name.Trim();
            group.Slots ??= [];
            while (group.Slots.Count < 12)
            {
                group.Slots.Add(null);
            }
            if (group.Slots.Count > 12)
            {
                group.Slots = group.Slots.Take(12).ToList();
            }
        }

        settings.GlobalFavoriteExtensionIds ??= settings.FavoriteExtensionIds?.ToList() ?? [];
        settings.ContextFavoriteExtensionIds ??= [];
        settings.DisabledExtensionIds ??= [];

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelGlobalGroupId) ||
            settings.QuickPanelGlobalGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelGlobalGroupId = settings.QuickPanelGlobalGroups[0].Id;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelContextGroupId) ||
            settings.QuickPanelContextGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelContextGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelContextGroupId = settings.QuickPanelContextGroups[0].Id;
        }

        if (!settings.WebDavSyncManuallyDisabled &&
            HasWebDavConfigValues(settings.WebDavServerUrl, settings.WebDavRootPath, settings.WebDavUsername))
        {
            settings.EnableWebDavSync = true;
        }

        return settings;
    }

    private static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username);
    }
}

public sealed record AppSettings
{
    public string LauncherHotkey { get; set; } = "Ctrl+Shift+Space";

    public bool LaunchAtStartup { get; set; } = false;

    public bool RefreshCloudOnStartup { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public List<string?> QuickPanelSlots { get; set; } = Enumerable.Repeat<string?>(null, 28).ToList();

    public List<QuickPanelGroupSettings> QuickPanelGlobalGroups { get; set; } = [];

    public List<QuickPanelGroupSettings> QuickPanelContextGroups { get; set; } = [];

    public string SelectedQuickPanelGlobalGroupId { get; set; } = "global-default";

    public string SelectedQuickPanelContextGroupId { get; set; } = "context-default";

    public string QuickPanelTrigger { get; set; } = "MiddleButtonLongPress";

    public QuickPanelMouseTriggerSettings QuickPanelMouseTriggers { get; set; } = new();

    public List<string> FavoriteExtensionIds { get; set; } = new();

    public List<string> GlobalFavoriteExtensionIds { get; set; } = new();

    public List<string> ContextFavoriteExtensionIds { get; set; } = new();

    public List<string> DisabledExtensionIds { get; set; } = new();

    public bool EnableAgentApi { get; set; } = true;

    public int AgentApiPort { get; set; } = 53919;

    public string AgentApiToken { get; set; } = "yanzi-local-dev-token";

    public bool EnableWebDavSync { get; set; } = false;

    public bool WebDavSyncManuallyDisabled { get; set; } = false;

    public string WebDavServerUrl { get; set; } = "https://dav.jianguoyun.com/dav/";

    public string WebDavRootPath { get; set; } = "/yanzi";

    public string WebDavUsername { get; set; } = string.Empty;

    public bool PreferManualExtensionEditor { get; set; } = false;
}

public sealed class QuickPanelGroupSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名";

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, 12).ToList();
}

public sealed record QuickPanelMouseTriggerSettings
{
    public bool MiddleButtonDown { get; set; } = false;

    public bool X1ButtonDown { get; set; } = false;

    public bool X2ButtonDown { get; set; } = false;

    public bool CtrlLeftClick { get; set; } = false;

    public bool CtrlRightClick { get; set; } = false;

    public bool MiddleButtonLongPress { get; set; } = true;

    public bool RightButtonLongPress { get; set; } = false;

    public bool RightButtonDrag { get; set; } = false;

    public bool HorizontalWheel { get; set; } = false;



    public bool ExecuteOnButtonRelease { get; set; } = true;

    public int LongPressMilliseconds { get; set; } = 500;

    public int DragThresholdPixels { get; set; } = 26;
}
