using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost.Sync;

internal static class SyncPackageSafety
{
    internal static string ResolveExtensionDirectory(string root, string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId) || extensionId.StartsWith('.') ||
            extensionId.EndsWith('.') || extensionId.EndsWith(' ') ||
            extensionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            extensionId.Contains('/') || extensionId.Contains('\\'))
            throw new InvalidDataException("同步扩展 ID 必须是有效的单级目录名。");

        var rootPath = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(rootPath, extensionId));
        if (!string.Equals(Path.GetDirectoryName(path), rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("同步扩展路径超出了扩展目录。");
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("同步扩展目录不能是符号链接或联接。");
        return path;
    }

    internal static WebDavSyncIndex ReadIndex(byte[]? bytes)
    {
        // Only a confirmed missing object means an empty repository. Corruption must stop writes.
        if (bytes == null) return new WebDavSyncIndex();
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.EnumerateObject().Any(p => p.Name.Equals("items", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array))
                throw new InvalidDataException("远端同步索引缺少 items 数组。");
            var index = JsonSerializer.Deserialize<WebDavSyncIndex>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (index.SchemaVersion is < 1 or > 2 || index.Items == null)
                throw new InvalidDataException("远端同步索引版本不受支持。");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in index.Items)
            {
                if (entry == null) throw new InvalidDataException("远端同步索引包含空条目。");
                ResolveExtensionDirectory(HostAssets.ExtensionsPath, entry.ExtensionId);
                if (!ids.Add(entry.ExtensionId)) throw new InvalidDataException("远端同步索引包含重复扩展 ID。");
                if (!entry.Deleted && !entry.Purged &&
                    (entry.PackageHash == null || entry.PackageHash.Length != 64 || !entry.PackageHash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(entry.PackagePath)))
                    throw new InvalidDataException("远端扩展缺少有效的 SHA-256 或包路径。");
            }
            return index;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("远端同步索引已损坏，已停止同步以避免覆盖远端数据。", ex);
        }
    }

    internal static void VerifyHash(byte[] bytes, string expectedHash)
    {
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("远端扩展包 SHA-256 与索引不一致，已停止安装。");
    }

    internal static Task ReplaceDirectoryAsync(string targetDirectory, byte[] bytes, CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(targetDirectory))!;
        targetDirectory = ResolveExtensionDirectory(root, Path.GetFileName(targetDirectory));
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, $".yanzi-sync-{Guid.NewGuid():N}");
        var backup = Path.Combine(root, $".yanzi-old-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new MemoryStream(bytes, writable: false))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                archive.ExtractToDirectory(staging);
            cancellationToken.ThrowIfCancellationRequested();
            var hadOriginal = Directory.Exists(targetDirectory);
            if (hadOriginal) Directory.Move(targetDirectory, backup);
            try { Directory.Move(staging, targetDirectory); }
            catch
            {
                if (hadOriginal) Directory.Move(backup, targetDirectory);
                throw;
            }
            if (hadOriginal) TryCleanup(backup);
        }
        finally { TryCleanup(staging); }
        return Task.CompletedTask;
    }

    private static void TryCleanup(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { HostAssets.AppendLog($"Sync staging cleanup deferred: {path}: {ex.Message}"); }
    }
}
