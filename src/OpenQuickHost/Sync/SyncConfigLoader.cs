using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost.Sync;

public static class SyncConfigLoader
{
    private const string FileName = "syncsettings.json";

    public static string ConfigPath =>
        HostAssets.ResolveDataFilePath(FileName);

    public static SyncOptions Load()
    {
        // Never attach a development process to the production cloud account.
        if (HostRuntimeProfile.IsDevelopment) return new SyncOptions { BaseUrl = string.Empty };
        if (!File.Exists(ConfigPath))
        {
            return new SyncOptions();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var options = JsonSerializer.Deserialize<SyncOptions>(json, JsonOptions) ?? new SyncOptions();
            if (!SyncOptions.IsRetiredHost(options.BaseUrl)) return options;
            ClearRetiredBaseUrl(json);
            return new SyncOptions { BaseUrl = string.Empty };
        }
        catch
        {
            return new SyncOptions();
        }
    }

    private static void ClearRetiredBaseUrl(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject document) return;
            document["baseUrl"] = "";
            File.WriteAllText(ConfigPath, document.ToJsonString(JsonOptions));
        }
        catch
        {
            // The in-memory result stays unconfigured even if the file cannot be rewritten.
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
            BaseUrl = SyncOptions.DefaultBaseUrl
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
