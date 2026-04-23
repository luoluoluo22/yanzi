using System.IO;
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
    public bool RefreshCloudOnStartup { get; init; } = true;

    public bool CloseToTray { get; init; } = true;
}
