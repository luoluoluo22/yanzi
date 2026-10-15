using System.Text.Json;
using System.IO;

namespace OpenQuickHost;

/// <summary>
/// 从小程序 manifest 自动发现能力。
/// 新增小程序无需修改宿主代码。
/// </summary>
public static class YanziCapabilityDiscoveryService
{
    public static void Discover(string extensionId, string manifestPath, Func<string, string, object?, Task<object?>>? invoker = null)
    {
        if (!File.Exists(manifestPath)) return;

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = JsonSerializer.Deserialize<YanziCapabilityManifest>(doc.RootElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (manifest == null) return;
        foreach (var item in manifest.Provides)
        {
            var name = item.Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            // A declaration without an executable binding must not advertise a working capability.
            if (invoker == null) continue;
            YanziCapabilityProviderSdk.RegisterProvider(extensionId, new[]
            {
                new YanziCapabilityProviderDefinition
                {
                    Name = name, Description = item.Description, Version = item.Version,
                    InputSchema = item.InputSchema, OutputSchema = item.OutputSchema, Permissions = item.Permissions,
                    Handler = payload => invoker(extensionId, name, payload)
                }
            });
        }
    }
}
