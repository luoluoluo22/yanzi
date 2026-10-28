using System;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost.Sync;

/// <summary>
/// A policy belongs to an extension package, not to the workstation.
/// Device IDs are the stable values from device-identity.json; host names are display-only.
/// Missing policy preserves legacy all-device/shared-data behavior.
/// </summary>
public sealed record LocalExtensionDevicePolicy
{
    public string Mode { get; init; } = "all";
    public string[] DeviceIds { get; init; } = [];
    public string PackageSync { get; init; } = "all";
    public string DataSync { get; init; } = "shared";

    public bool AllowsDevice(string deviceId) =>
        (Mode ?? "invalid").Trim().ToLowerInvariant() switch
        {
            "all" => true,
            "selected" => !string.IsNullOrWhiteSpace(deviceId) &&
                Array.Exists(DeviceIds ?? [], id =>
                    string.Equals(id?.Trim(), deviceId, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

    public bool SyncsPackage =>
        (PackageSync ?? "none").Trim().ToLowerInvariant() is "all" or "targets";

    public bool SyncsData =>
        string.Equals(DataSync, "shared", StringComparison.OrdinalIgnoreCase);
}

public static class ExtensionDevicePolicy
{
    public static string CurrentDeviceId => DeviceIdentityStore.GetOrCreateDesktopDeviceId();

    public static LocalExtensionDevicePolicy FromManifestJson(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson)) return new();
        try
        {
            using var json = JsonDocument.Parse(manifestJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return new();
            foreach (var property in json.RootElement.EnumerateObject())
                if (string.Equals(property.Name, "devicePolicy", StringComparison.OrdinalIgnoreCase))
                    return property.Value.Deserialize<LocalExtensionDevicePolicy>(
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? new LocalExtensionDevicePolicy { Mode = "invalid", PackageSync = "none", DataSync = "device" };
        }
        catch (JsonException)
        {
            if (manifestJson.Contains("devicePolicy", StringComparison.OrdinalIgnoreCase))
                return new LocalExtensionDevicePolicy { Mode = "invalid", PackageSync = "none", DataSync = "device" };
        }
        return new();
    }

    public static LocalExtensionDevicePolicy ForExtension(string extensionId)
    {
        // A stale or hostile ID must not escape the extension install root.
        if (string.IsNullOrWhiteSpace(extensionId) ||
            extensionId is "." or ".." ||
            !string.Equals(Path.GetFileName(extensionId), extensionId, StringComparison.Ordinal) ||
            extensionId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            return new LocalExtensionDevicePolicy { Mode = "invalid", PackageSync = "none", DataSync = "device" };
        try
        {
            var path = Path.Combine(HostAssets.ExtensionsPath, extensionId, "manifest.json");
            return File.Exists(path) ? FromManifestJson(File.ReadAllText(path)) : new();
        }
        catch (IOException) { return new LocalExtensionDevicePolicy { Mode = "invalid", PackageSync = "none", DataSync = "device" }; }
        catch (UnauthorizedAccessException) { return new LocalExtensionDevicePolicy { Mode = "invalid", PackageSync = "none", DataSync = "device" }; }
    }

    public static bool CanRun(string extensionId) => ForExtension(extensionId).AllowsDevice(CurrentDeviceId);

    public static bool CanInstall(string? manifestJson) =>
        FromManifestJson(manifestJson).AllowsDevice(CurrentDeviceId);

    // Automatic cloud installation is additionally subject to package distribution.
    public static bool CanSyncInstall(string? manifestJson)
    {
        var policy = FromManifestJson(manifestJson);
        return policy.AllowsDevice(CurrentDeviceId) && policy.SyncsPackage;
    }

    // Some old cloud indexes have no device policy. Inspect the actual package too.
    public static bool CanSyncInstallArchive(byte[] archiveBytes)
    {
        try
        {
            using var stream = new MemoryStream(archiveBytes, writable: false);
            using var archive = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Read);
            var entry = archive.GetEntry("manifest.json");
            if (entry == null || entry.Length > 1024 * 1024) return false;
            using var reader = new StreamReader(entry.Open());
            return CanSyncInstall(reader.ReadToEnd());
        }
        catch (InvalidDataException) { return false; }
        catch (JsonException) { return false; }
    }

    public static bool ShouldSyncPackage(string extensionId) =>
        ForExtension(extensionId) is var policy &&
        policy.AllowsDevice(CurrentDeviceId) && policy.SyncsPackage;

    public static bool ShouldSyncData(string extensionId) =>
        ForExtension(extensionId) is var policy &&
        policy.AllowsDevice(CurrentDeviceId) && policy.SyncsData;
}