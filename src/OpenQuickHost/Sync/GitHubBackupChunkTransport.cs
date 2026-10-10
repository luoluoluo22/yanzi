using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

/// <summary>Append-only, read-after-write verified GitHub backup parts.</summary>
internal static class GitHubBackupChunkTransport
{
    internal const int ChunkSize = 192 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private sealed record Manifest(int Version, int Parts, int TotalBytes, string Sha256);

    internal static async Task<string> UploadVerifiedChunksAsync(
        IPersonalSyncBackend backend, string basePath, byte[] payload, CancellationToken token)
    {
        var partCount = (payload.Length + ChunkSize - 1) / ChunkSize;
        for (var i = 0; i < partCount; i++)
        {
            var size = Math.Min(ChunkSize, payload.Length - i * ChunkSize);
            var part = payload.AsSpan(i * ChunkSize, size).ToArray();
            var path = $"{basePath}/part-{i:D4}.bin";
            await backend.WriteBytesAsync(path, part, "application/octet-stream", token);
            var downloaded = await backend.TryReadBytesAsync(path, token)
                ?? throw new IOException("GitHub did not return the uploaded backup part.");
            if (!part.AsSpan().SequenceEqual(downloaded))
                throw new CryptographicException("Remote backup chunk readback differs.");
        }

        // This manifest is the only discoverable completed snapshot marker.
        var manifestPath = basePath + "/manifest.json";
        var document = JsonSerializer.SerializeToUtf8Bytes(new Manifest(
            1, partCount, payload.Length, Convert.ToHexString(SHA256.HashData(payload))), Options);
        await backend.WriteBytesAsync(manifestPath, document, "application/json", token);
        var verify = await backend.TryReadBytesAsync(manifestPath, token)
            ?? throw new IOException("Committed backup manifest is missing.");
        if (!document.AsSpan().SequenceEqual(verify))
            throw new CryptographicException("Backup manifest readback differs.");
        return manifestPath;
    }

    internal static async Task<byte[]> DownloadVerifiedChunksAsync(
        IPersonalSyncBackend backend, string manifestPath, CancellationToken token)
    {
        var manifestBytes = await backend.TryReadBytesAsync(manifestPath, token)
            ?? throw new FileNotFoundException("Backup manifest missing.");
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes, Options)
            ?? throw new InvalidDataException("Backup manifest cannot be parsed.");
        if (manifest.Version != 1 || manifest.TotalBytes < 1 ||
            manifest.TotalBytes > 60 * 1024 * 1024 || manifest.Parts < 1 ||
            manifest.Parts > 320 ||
            manifest.Parts != (manifest.TotalBytes + ChunkSize - 1) / ChunkSize)
            throw new InvalidDataException("Backup manifest dimensions are invalid.");
        var prefix = manifestPath[..^"manifest.json".Length];
        using var stream = new MemoryStream(manifest.TotalBytes);
        for (var i = 0; i < manifest.Parts; i++)
        {
            var part = await backend.TryReadBytesAsync(prefix + $"part-{i:D4}.bin", token)
                ?? throw new FileNotFoundException("Backup part missing.");
            var length = Math.Min(ChunkSize, manifest.TotalBytes - i * ChunkSize);
            if (part.Length != length) throw new InvalidDataException("Backup part size mismatch.");
            stream.Write(part);
        }
        var bytes = stream.ToArray();
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), manifest.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Downloaded encrypted backup checksum mismatch.");
        return bytes;
    }
}
