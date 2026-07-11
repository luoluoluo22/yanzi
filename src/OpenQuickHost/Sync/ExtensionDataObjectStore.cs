using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

internal static class ExtensionDataObjectStore
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxEmbeddedHistoryCount = 30;

    public static string BuildObjectPath(string extensionId, string key) =>
        $"state/extension-data/objects/{HashId(NormalizeExtensionId(extensionId))}/{HashId(NormalizeKey(key))}.json";

    public static string BuildHistoryPath(ExtensionDataObject value) =>
        $"state/extension-data/history/{HashId(NormalizeExtensionId(value.ExtensionId))}/{HashId(NormalizeKey(value.Key))}/{value.VersionId}.json";

    public static ExtensionDataObject CreateNext(
        string extensionId,
        string key,
        string content,
        ExtensionDataObject? observed)
    {
        var normalizedExtensionId = NormalizeExtensionId(extensionId);
        var normalizedKey = NormalizeKey(key);
        var normalizedContent = content ?? string.Empty;
        var revision = ExtensionSyncRevision.Next(observed?.Revision ?? 0);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var deviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        var contentHash = ComputeContentHash(normalizedContent);
        var value = new ExtensionDataObject
        {
            SchemaVersion = CurrentSchemaVersion,
            ExtensionId = normalizedExtensionId,
            Key = normalizedKey,
            Revision = revision,
            VersionId = Guid.NewGuid().ToString("N"),
            UpdatedAtUtc = now,
            UpdatedByDeviceId = deviceId,
            UpdatedByDeviceName = DeviceIdentityStore.GetDesktopDisplayName(),
            ContentHash = contentHash,
            Content = normalizedContent,
            Deleted = false
        };

        var history = observed?.History
            .Where(static item => !string.IsNullOrWhiteSpace(item.VersionId))
            .ToList() ?? [];
        if (observed != null && !string.IsNullOrWhiteSpace(observed.VersionId))
        {
            history.Add(ToHistoryInfo(observed));
        }

        value.History = history
            .GroupBy(static item => item.VersionId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(static item => item.Revision).First())
            .OrderByDescending(static item => item.Revision)
            .Take(MaxEmbeddedHistoryCount)
            .ToList();
        return value;
    }

    public static ExtensionDataObject CreateLegacy(string extensionId, string key, string content)
    {
        var normalizedContent = content ?? string.Empty;
        var hash = ComputeContentHash(normalizedContent);
        return new ExtensionDataObject
        {
            SchemaVersion = 0,
            ExtensionId = NormalizeExtensionId(extensionId),
            Key = NormalizeKey(key),
            Revision = 0,
            VersionId = $"legacy-{hash[..24]}",
            UpdatedAtUtc = string.Empty,
            UpdatedByDeviceId = string.Empty,
            UpdatedByDeviceName = "旧版兼容数据",
            ContentHash = hash,
            Content = normalizedContent,
            Deleted = false
        };
    }

    public static ExtensionDataObject CreateTombstone(
        string extensionId,
        string key,
        ExtensionDataObject? observed)
    {
        var value = CreateNext(extensionId, key, string.Empty, observed);
        value.Deleted = true;
        return value;
    }

    public static ExtensionDataHistoryInfo ToHistoryInfo(ExtensionDataObject value) => new()
    {
        VersionId = value.VersionId,
        Revision = value.Revision,
        UpdatedAtUtc = value.UpdatedAtUtc,
        UpdatedByDeviceId = value.UpdatedByDeviceId,
        UpdatedByDeviceName = value.UpdatedByDeviceName,
        ContentHash = value.ContentHash,
        Deleted = value.Deleted
    };

    public static byte[] Serialize(ExtensionDataObject value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    public static ExtensionDataObject? Deserialize(byte[]? bytes, string extensionId, string key)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            var value = JsonSerializer.Deserialize<ExtensionDataObject>(bytes, JsonOptions);
            if (value == null || value.SchemaVersion <= 0 || string.IsNullOrWhiteSpace(value.VersionId)) return null;
            if (!string.Equals(value.ExtensionId, NormalizeExtensionId(extensionId), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(value.Key, NormalizeKey(key), StringComparison.Ordinal))
            {
                return null;
            }

            if (!value.Deleted && !string.Equals(value.ContentHash, ComputeContentHash(value.Content), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("扩展私有数据对象的 SHA-256 校验失败。");
            }

            value.History ??= [];
            return value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string ComputeContentHash(string? content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty))).ToLowerInvariant();

    public static bool HasConcurrentChange(
        ExtensionDataObject observed,
        ExtensionDataSyncState? localState,
        string localContentHash,
        bool localDeleted = false)
    {
        if (localDeleted == observed.Deleted &&
            (localDeleted || localContentHash.Equals(observed.ContentHash, StringComparison.OrdinalIgnoreCase))) return false;
        if (observed.SchemaVersion == 0)
        {
            return !observed.ContentHash.Equals(localState?.LastRemoteContentHash, StringComparison.OrdinalIgnoreCase);
        }
        return observed.Revision > (localState?.LastRemoteRevision ?? 0);
    }

    public static string NormalizeExtensionId(string extensionId)
    {
        var normalized = (extensionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new InvalidOperationException("extensionId 不能为空。");
        return normalized;
    }

    public static string NormalizeKey(string key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)) throw new InvalidOperationException("storage key 不能为空。");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("storage key 不能包含 . 或 .. 路径段。");
        }
        return string.Join("/", segments);
    }

    private static string HashId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed class ExtensionDataObject
{
    public int SchemaVersion { get; set; } = ExtensionDataObjectStore.CurrentSchemaVersion;
    public string ExtensionId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string VersionId { get; set; } = string.Empty;
    public string UpdatedAtUtc { get; set; } = string.Empty;
    public string UpdatedByDeviceId { get; set; } = string.Empty;
    public string UpdatedByDeviceName { get; set; } = string.Empty;
    public bool Deleted { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<ExtensionDataHistoryInfo> History { get; set; } = [];
}

public sealed class ExtensionDataHistoryInfo
{
    public string VersionId { get; set; } = string.Empty;
    public long Revision { get; set; }
    public string UpdatedAtUtc { get; set; } = string.Empty;
    public string UpdatedByDeviceId { get; set; } = string.Empty;
    public string UpdatedByDeviceName { get; set; } = string.Empty;
    public bool Deleted { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

public sealed record ExtensionDataWriteResult(ExtensionDataObject Value, bool Confirmed);

public sealed record ExtensionDataReadResult(string? Content, ExtensionDataObject? Value, bool UsedLegacyValue);
