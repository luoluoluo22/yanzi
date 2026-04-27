using System.IO;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class SyncConfigLoader
{
    private const string FileName = "syncsettings.json";

    public static string ConfigPath =>
        HostAssets.ResolveDataFilePath(FileName);

    public static SyncOptions Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new SyncOptions();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var options = JsonSerializer.Deserialize<SyncOptions>(json, JsonOptions);
            return options ?? new SyncOptions();
        }
        catch
        {
            return new SyncOptions();
        }
    }

    public static void EnsureExampleFile()
    {
        var examplePath = HostAssets.ResolveDataFilePath("syncsettings.example.json");
        if (File.Exists(examplePath))
        {
            return;
        }

        var example = new SyncOptions
        {
            BaseUrl = "https://openquickhost-sync.a1137583371.workers.dev"
        };

        var json = JsonSerializer.Serialize(example, JsonOptions);
        File.WriteAllText(examplePath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
