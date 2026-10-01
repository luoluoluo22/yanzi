using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

internal static class AccountExtensionDataStore
{
    public const int SchemaVersion = 1;
    private const string ObjectPrefix = "extensionData.v1.";

    public static string BuildObjectId(string extensionId, string key)
    {
        var normalizedExtensionId = ExtensionDataObjectStore.NormalizeExtensionId(extensionId);
        var normalizedKey = ExtensionDataObjectStore.NormalizeKey(key);
        var identity = normalizedExtensionId + "\0" + normalizedKey;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return ObjectPrefix + hash;
    }

    public static async Task<AccountExtensionDataReadResult> TryReadAsync(
        string extensionId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtensionId = ExtensionDataObjectStore.NormalizeExtensionId(extensionId);
        var normalizedKey = ExtensionDataObjectStore.NormalizeKey(key);
        var objectId = BuildObjectId(normalizedExtensionId, normalizedKey);
        var client = CreateClient();

        if (!CanAttemptAccountCloud(client))
        {
            return AccountExtensionDataReadResult.Unavailable(objectId);
        }

        var record = await client.GetSyncObjectAsync(objectId, cancellationToken);
        if (record == null)
        {
            return new AccountExtensionDataReadResult(true, false, objectId, 0, null, null);
        }

        var payload = ParsePayload(record, normalizedExtensionId, normalizedKey);
        if (record.Deleted)
        {
            return new AccountExtensionDataReadResult(true, false, objectId, record.Revision, null, record);
        }

        return new AccountExtensionDataReadResult(
            true,
            true,
            objectId,
            record.Revision,
            payload.Content,
            record);
    }

    public static async Task<AccountExtensionDataWriteResult> WriteAsync(
        string extensionId,
        string key,
        string content,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default,
        string? expectedAccountId = null)
    {
        var normalizedExtensionId = ExtensionDataObjectStore.NormalizeExtensionId(extensionId);
        var normalizedKey = ExtensionDataObjectStore.NormalizeKey(key);
        var normalizedContent = content ?? string.Empty;
        var objectId = BuildObjectId(normalizedExtensionId, normalizedKey);
        var client = CreateClient();

        if (expectedAccountId != null && client.CurrentUserId != expectedAccountId)
            throw new InvalidOperationException("Account changed before storage write.");

        if (!CanAttemptAccountCloud(client))
        {
            return AccountExtensionDataWriteResult.Unavailable(objectId);
        }

        var observed = expectedRevision.HasValue
            ? null
            : await client.GetSyncObjectAsync(objectId, cancellationToken);
        var revision = expectedRevision ?? observed?.Revision ?? 0;
        var payload = CreatePayloadElement(
            normalizedExtensionId,
            normalizedKey,
            normalizedContent);

        var saved = await client.PutSyncObjectAsync(
            objectId,
            SchemaVersion,
            revision,
            deleted: false,
            payload,
            DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            DeviceIdentityStore.GetDesktopDisplayName(),
            cancellationToken);

        return new AccountExtensionDataWriteResult(true, objectId, saved.Revision, saved);
    }

    public static async Task<AccountExtensionDataWriteResult> DeleteAsync(
        string extensionId,
        string key,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtensionId = ExtensionDataObjectStore.NormalizeExtensionId(extensionId);
        var normalizedKey = ExtensionDataObjectStore.NormalizeKey(key);
        var objectId = BuildObjectId(normalizedExtensionId, normalizedKey);
        var client = CreateClient();

        if (!CanAttemptAccountCloud(client))
        {
            return AccountExtensionDataWriteResult.Unavailable(objectId);
        }

        var observed = expectedRevision.HasValue
            ? null
            : await client.GetSyncObjectAsync(objectId, cancellationToken);
        var revision = expectedRevision ?? observed?.Revision ?? 0;
        var payload = CreatePayloadElement(
            normalizedExtensionId,
            normalizedKey,
            string.Empty);

        var saved = await client.PutSyncObjectAsync(
            objectId,
            SchemaVersion,
            revision,
            deleted: true,
            payload,
            DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            DeviceIdentityStore.GetDesktopDisplayName(),
            cancellationToken);

        return new AccountExtensionDataWriteResult(true, objectId, saved.Revision, saved);
    }

    private static CloudSyncClient CreateClient() => new(SyncConfigLoader.Load());

    private static bool CanAttemptAccountCloud(CloudSyncClient client) =>
        !string.IsNullOrWhiteSpace(client.CurrentUserId) || client.HasCredential;

    internal static JsonElement CreatePayloadElement(
        string extensionId,
        string key,
        string content) =>
        JsonSerializer.SerializeToElement(
            CreatePayload(extensionId, key, content),
            JsonOptions);

    private static AccountExtensionDataPayload CreatePayload(string extensionId, string key, string content) => new()
    {
        ExtensionId = extensionId,
        Key = key,
        ContentType = "text/plain; charset=utf-8",
        Content = content,
        ContentHash = ExtensionDataObjectStore.ComputeContentHash(content)
    };

    private static AccountExtensionDataPayload ParsePayload(
        CloudSyncObjectRecord record,
        string expectedExtensionId,
        string expectedKey)
    {
        AccountExtensionDataPayload? payload;
        try
        {
            payload = record.Payload.Deserialize<AccountExtensionDataPayload>(JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("账号云端的小程序数据对象格式无效。", ex);
        }

        if (payload == null ||
            !string.Equals(payload.ExtensionId, expectedExtensionId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.Key, expectedKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException("账号云端的小程序数据对象作用域不匹配。");
        }

        if (!record.Deleted &&
            !string.Equals(
                payload.ContentHash,
                ExtensionDataObjectStore.ComputeContentHash(payload.Content),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("账号云端的小程序数据对象 SHA-256 校验失败。");
        }

        return payload;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

internal sealed class AccountExtensionDataPayload
{
    public string ExtensionId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain; charset=utf-8";
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
}

internal sealed record AccountExtensionDataReadResult(
    bool Available,
    bool Exists,
    string ObjectId,
    long Revision,
    string? Content,
    CloudSyncObjectRecord? Record)
{
    public static AccountExtensionDataReadResult Unavailable(string objectId) =>
        new(false, false, objectId, 0, null, null);
}

internal sealed record AccountExtensionDataWriteResult(
    bool Available,
    string ObjectId,
    long Revision,
    CloudSyncObjectRecord? Record)
{
    public static AccountExtensionDataWriteResult Unavailable(string objectId) =>
        new(false, objectId, 0, null);
}
