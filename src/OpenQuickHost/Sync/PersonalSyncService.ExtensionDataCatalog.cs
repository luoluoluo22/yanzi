using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

// Versioned object records remain authoritative; this file only lists discoverable keys.
public sealed partial class PersonalSyncService
{
    private static string ExtensionKeyIndexPath(string extensionId)
    {
        var id = ExtensionDataObjectStore.NormalizeExtensionId(extensionId).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return $"state/extension-data/key-index/{hash}.json";
    }

    public async Task<IReadOnlyList<string>> GetExtensionDataKeysAsync(
        string extensionId, CancellationToken cancellationToken = default)
    {
        var bytes = await _backend.TryReadBytesAsync(ExtensionKeyIndexPath(extensionId), cancellationToken);
        if (bytes == null || bytes.Length == 0) return [];
        var keys = JsonSerializer.Deserialize<List<string>>(bytes) ?? [];
        return keys.Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(ExtensionDataObjectStore.NormalizeKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task PublishExtensionDataKeysAsync(
        string extensionId, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var oldKeys = await GetExtensionDataKeysAsync(extensionId, cancellationToken);
        var merged = oldKeys.Concat(keys.Select(ExtensionDataObjectStore.NormalizeKey))
            .Distinct(StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        if (merged.SequenceEqual(oldKeys)) return;
        var path = ExtensionKeyIndexPath(extensionId);
        await _backend.WriteBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(merged),
            "application/json; charset=utf-8", cancellationToken);
        var verified = await GetExtensionDataKeysAsync(extensionId, cancellationToken);
        if (!merged.All(verified.Contains))
            throw new IOException($"Extension data catalog confirmation failed for {extensionId}");
    }

    public async Task<bool> PublishExtensionBinaryAssetIfAbsentAsync(
        string extensionId, string key, byte[] data, CancellationToken cancellationToken = default)
    {
        var path = BuildExtensionDataPath(extensionId, key);
        var existing = await _backend.TryReadBytesAsync(path, cancellationToken);
        if (existing != null)
        {
            if (!BinaryAssetContentMatches(existing, data))
                return false; // never overwrite a divergent remote binary asset
        }
        else await _backend.WriteBytesAsync(path, data, "application/octet-stream", cancellationToken);
        await PublishExtensionDataKeysAsync(extensionId, [key], cancellationToken);
        return true;
    }

    // Some image editors store "data:image/..." as UTF-8 text with a BOM.
    // File.ReadAllTextAsync removes that BOM when the same asset is uploaded as text.
    // Ignore only that byte-order mark, never differences in actual image content.
    internal static bool BinaryAssetContentMatches(byte[] left, byte[] right) =>
        SHA256.HashData(PortableBytes(left)).SequenceEqual(SHA256.HashData(PortableBytes(right)));

    private static ReadOnlySpan<byte> PortableBytes(byte[] bytes) =>
        bytes.Length >= 8 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF &&
        bytes.AsSpan(3, 5).SequenceEqual("data:"u8)
            ? bytes.AsSpan(3)
            : bytes.AsSpan();

    public Task<byte[]?> TryReadExtensionBinaryAssetAsync(
        string extensionId, string key, CancellationToken cancellationToken = default) =>
        _backend.TryReadBytesAsync(BuildExtensionDataPath(extensionId, key), cancellationToken);
}
