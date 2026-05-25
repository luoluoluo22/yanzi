using System.IO;
using System.Net;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class DeviceIdentityStore
{
    private const string FileName = "device-identity.json";

    private static string PathName => HostAssets.ResolveDataFilePath(FileName);

    public static string GetOrCreateDesktopDeviceId()
    {
        var saved = Load();
        if (!string.IsNullOrWhiteSpace(saved?.DeviceId))
        {
            return saved.DeviceId;
        }

        var identity = new DeviceIdentity
        {
            DeviceId = $"desktop-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow.ToString("O")
        };
        Save(identity);
        return identity.DeviceId;
    }

    public static string GetDesktopDisplayName()
    {
        var machineName = Dns.GetHostName();
        return string.IsNullOrWhiteSpace(machineName) ? "Windows 桌面端" : $"Windows · {machineName}";
    }

    private static DeviceIdentity? Load()
    {
        try
        {
            if (!File.Exists(PathName))
            {
                return null;
            }

            var json = File.ReadAllText(PathName);
            return JsonSerializer.Deserialize<DeviceIdentity>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void Save(DeviceIdentity identity)
    {
        var directory = Path.GetDirectoryName(PathName);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(PathName, JsonSerializer.Serialize(identity, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed class DeviceIdentity
    {
        public string DeviceId { get; set; } = string.Empty;

        public string CreatedAtUtc { get; set; } = string.Empty;
    }
}
