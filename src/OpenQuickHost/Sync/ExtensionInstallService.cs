using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class ExtensionInstallService
{
    public static async Task<ExtensionInstallResult> InstallPackageAsync(
        byte[] packageBytes,
        string? requestedExtensionId = null,
        CancellationToken cancellationToken = default)
    {
        if (packageBytes == null || packageBytes.Length == 0)
        {
            throw new InvalidOperationException("扩展包为空。");
        }

        Directory.CreateDirectory(HostAssets.ExtensionsPath);
        var tempDirectory = Path.Combine(HostAssets.ExtensionsPath, $".yanzi-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            await using var stream = new MemoryStream(packageBytes, writable: false);
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(tempDirectory, overwriteFiles: true);
            }

            var manifestPath = Path.Combine(tempDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("扩展包里缺少 manifest.json。");
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            LocalExtensionManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(manifestJson, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"扩展包中的 manifest.json 不是有效 JSON：{ex.Message}", ex);
            }

            if (manifest == null)
            {
                throw new InvalidOperationException("扩展包中的 manifest.json 无效。");
            }

            if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new InvalidOperationException("扩展包中的 manifest.json 缺少 id 或 name。");
            }

            if (!string.IsNullOrWhiteSpace(requestedExtensionId) &&
                !string.Equals(requestedExtensionId, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"扩展 ID 不匹配。协议里是 {requestedExtensionId}，扩展包里是 {manifest.Id}。");
            }

            manifest = await LocalizeRemoteIconAsync(tempDirectory, manifest, cancellationToken);

            var targetDirectory = Path.Combine(HostAssets.ExtensionsPath, manifest.Id);
            // 升级采用“旧目录先挪走 → 新目录就位 → 删旧”的顺序：直接 Delete 后 Move 之间
            // 一旦崩溃/断电，扩展会凭空消失且无任何恢复手段。
            // 备份目录用点前缀命名，扩展目录扫描会跳过（见 LocalExtensionCatalog）。
            string? backupDirectory = null;
            if (Directory.Exists(targetDirectory))
            {
                backupDirectory = Path.Combine(
                    HostAssets.ExtensionsPath,
                    $".yanzi-old-{manifest.Id:N}-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.Move(targetDirectory, backupDirectory);
            }

            try
            {
                Directory.Move(tempDirectory, targetDirectory);
            }
            catch
            {
                // 新目录就位失败时把旧目录挪回来，保持升级前的状态
                if (backupDirectory != null && !Directory.Exists(targetDirectory))
                {
                    try
                    {
                        Directory.Move(backupDirectory, targetDirectory);
                    }
                    catch
                    {
                        // 回滚失败时保留备份目录，避免数据被误删
                    }
                }
                throw;
            }

            if (backupDirectory != null && Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
                catch
                {
                    // 清理失败只留下垃圾目录，不影响升级结果
                }
            }

            return new ExtensionInstallResult(
                manifest.Id,
                manifest.Name,
                manifest.Version ?? "0.1.0",
                targetDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<LocalExtensionManifest> LocalizeRemoteIconAsync(
        string extensionDirectory,
        LocalExtensionManifest manifest,
        CancellationToken cancellationToken)
    {
        var iconReference = manifest.Icon?.Trim();
        if (string.IsNullOrWhiteSpace(iconReference) ||
            !Uri.TryCreate(iconReference, UriKind.Absolute, out var iconUri) ||
            (iconUri.Scheme != Uri.UriSchemeHttp && iconUri.Scheme != Uri.UriSchemeHttps))
        {
            return manifest;
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        using var response = await httpClient.GetAsync(iconUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            return manifest;
        }

        var extension = ResolveIconExtension(iconUri, response.Content.Headers.ContentType?.MediaType);
        var fileName = "icon" + extension;
        var filePath = Path.Combine(extensionDirectory, fileName);
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

        var localizedManifest = manifest with { Icon = fileName };
        var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(localizedManifest, JsonOptions), cancellationToken);
        return localizedManifest;
    }

    private static string ResolveIconExtension(Uri iconUri, string? contentType)
    {
        var path = iconUri.AbsolutePath.ToLowerInvariant();
        var mediaType = (contentType ?? string.Empty).ToLowerInvariant();

        if (path.EndsWith(".png") || mediaType.Contains("image/png"))
        {
            return ".png";
        }

        if (path.EndsWith(".jpg") || path.EndsWith(".jpeg") || mediaType.Contains("image/jpeg"))
        {
            return ".jpg";
        }

        if (path.EndsWith(".gif") || mediaType.Contains("image/gif"))
        {
            return ".gif";
        }

        if (path.EndsWith(".webp") || mediaType.Contains("image/webp"))
        {
            return ".webp";
        }

        if (path.EndsWith(".bmp") || mediaType.Contains("image/bmp"))
        {
            return ".bmp";
        }

        if (path.EndsWith(".ico") || mediaType.Contains("image/x-icon") || mediaType.Contains("image/vnd.microsoft.icon"))
        {
            return ".ico";
        }

        if (path.EndsWith(".svg") || mediaType.Contains("image/svg+xml"))
        {
            return ".svg";
        }

        return ".img";
    }
}

public sealed record ExtensionInstallResult(
    string ExtensionId,
    string Name,
    string Version,
    string DirectoryPath);
