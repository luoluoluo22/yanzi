using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenQuickHost;

public static class AppSettingsStore
{
    public static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
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
}

public sealed record AppSettings
{
    public string LauncherHotkey { get; set; } = "Ctrl+Shift+Space";

    public bool LaunchAtStartup { get; set; } = false;

    public bool RefreshCloudOnStartup { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public List<string?> QuickPanelSlots { get; set; } = Enumerable.Repeat<string?>(null, 28).ToList();

    public string QuickPanelTrigger { get; set; } = "MiddleButtonLongPress";

    public QuickPanelMouseTriggerSettings QuickPanelMouseTriggers { get; set; } = new();

    public List<string> FavoriteExtensionIds { get; set; } = new();

    public bool EnableAgentApi { get; set; } = true;

    public int AgentApiPort { get; set; } = 53919;

    public string AgentApiToken { get; set; } = "yanzi-local-dev-token";

    public bool EnableWebDavSync { get; set; } = false;

    public string WebDavServerUrl { get; set; } = "https://dav.jianguoyun.com/dav/";

    public string WebDavRootPath { get; set; } = "/yanzi";

    public string WebDavUsername { get; set; } = string.Empty;
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

    public bool CircleGesture { get; set; } = false;

    public bool ExecuteOnButtonRelease { get; set; } = true;

    public int LongPressMilliseconds { get; set; } = 500;

    public int DragThresholdPixels { get; set; } = 26;
}
