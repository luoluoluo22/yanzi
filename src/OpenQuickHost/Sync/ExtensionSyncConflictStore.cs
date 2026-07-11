using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

internal static class ExtensionSyncConflictStore
{
    private static string StatePath => HostAssets.ResolveDataFilePath("extension-sync-conflicts.json");
    private static string PackageDirectory => Path.Combine(Path.GetDirectoryName(StatePath)!, "extension-sync-conflicts");

    public static IReadOnlyList<ExtensionSyncConflictRecord> Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return [];
            return JsonSerializer.Deserialize<List<ExtensionSyncConflictRecord>>(File.ReadAllText(StatePath), JsonOptions)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Preserve(
        WebDavSyncEntry local,
        WebDavSyncEntry remote,
        byte[]? localPackageBytes)
    {
        var records = Load().ToList();
        var existing = records.FirstOrDefault(item =>
            item.ExtensionId.Equals(local.ExtensionId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new ExtensionSyncConflictRecord { ExtensionId = local.ExtensionId };
            records.Add(existing);
        }

        existing.DetectedAtUtc = DateTime.UtcNow.ToString("O");
        existing.LocalRevision = local.Revision;
        existing.LocalPackageHash = local.PackageHash;
        existing.LocalVersion = local.Version;
        existing.LocalDeleted = local.Deleted;
        existing.LocalPurged = local.Purged;
        existing.RemoteRevision = remote.Revision;
        existing.RemotePackageHash = remote.PackageHash;
        existing.RemoteVersion = remote.Version;
        existing.RemoteDeleted = remote.Deleted;
        existing.RemotePurged = remote.Purged;
        existing.RemoteDeviceId = remote.UpdatedByDeviceId;
        existing.RemoteDeviceName = remote.UpdatedByDeviceName;

        if (localPackageBytes is { Length: > 0 })
        {
            Directory.CreateDirectory(PackageDirectory);
            var fileName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(local.ExtensionId.ToLowerInvariant())))
                               .ToLowerInvariant() + ".zip";
            var path = Path.Combine(PackageDirectory, fileName);
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, localPackageBytes);
            File.Move(tempPath, path, overwrite: true);
            existing.LocalPackagePath = path;
        }

        Save(records);
    }

    public static byte[]? ReadLocalPackage(string extensionId)
    {
        var record = Load().FirstOrDefault(item =>
            item.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        return record != null && !string.IsNullOrWhiteSpace(record.LocalPackagePath) && File.Exists(record.LocalPackagePath)
            ? File.ReadAllBytes(record.LocalPackagePath)
            : null;
    }

    public static bool Remove(string extensionId)
    {
        var records = Load().ToList();
        var record = records.FirstOrDefault(item =>
            item.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (record == null) return false;
        records.Remove(record);
        if (!string.IsNullOrWhiteSpace(record.LocalPackagePath) && File.Exists(record.LocalPackagePath))
        {
            File.Delete(record.LocalPackagePath);
        }
        Save(records);
        return true;
    }

    private static void Save(List<ExtensionSyncConflictRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var tempPath = StatePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(tempPath, StatePath, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed class ExtensionSyncConflictRecord
{
    public string ExtensionId { get; set; } = string.Empty;
    public string DetectedAtUtc { get; set; } = string.Empty;
    public long LocalRevision { get; set; }
    public string LocalPackageHash { get; set; } = string.Empty;
    public string LocalVersion { get; set; } = string.Empty;
    public bool LocalDeleted { get; set; }
    public bool LocalPurged { get; set; }
    public string LocalPackagePath { get; set; } = string.Empty;
    public long RemoteRevision { get; set; }
    public string RemotePackageHash { get; set; } = string.Empty;
    public string RemoteVersion { get; set; } = string.Empty;
    public bool RemoteDeleted { get; set; }
    public bool RemotePurged { get; set; }
    public string? RemoteDeviceId { get; set; }
    public string? RemoteDeviceName { get; set; }
}
