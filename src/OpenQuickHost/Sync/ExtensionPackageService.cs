using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class ExtensionPackageService
{
    public static string ExtensionsRootPath => HostAssets.ExtensionsPath;

    public static byte[] BuildPackage(CommandItem command, string version, string? iconOverride = null, bool includeUserShortcut = true)
    {
        var extensionsRoot = Path.GetFullPath(ExtensionsRootPath);
        var appDataRoot = Path.GetDirectoryName(extensionsRoot);

        var dirPath = !string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath)
            ? Path.GetFullPath(command.ExtensionDirectoryPath)
            : null;

        if (dirPath != null &&
            Directory.Exists(dirPath) &&
            !string.Equals(dirPath, extensionsRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dirPath, appDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return BuildDirectoryPackage(dirPath, iconOverride, includeUserShortcut);
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteJsonEntry(
                archive,
                "manifest.json",
                new
                {
                    id = command.ExtensionId,
                    name = command.Title,
                    version,
                    category = command.Category,
                    description = command.Subtitle,
                    keywords = command.Keywords,
                    source = command.Source.ToString(),
                    icon = string.IsNullOrWhiteSpace(iconOverride) ? command.IconReference : iconOverride
                });

            WriteJsonEntry(
                archive,
                "command.json",
                new
                {
                    command.Title,
                    command.Subtitle,
                    command.Category,
                    command.OpenTarget,
                    command.Keywords
                });
        }

        return stream.ToArray();
    }

    public static async Task<string> SavePackageAsync(string extensionId, string version, byte[] packageBytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ExtensionsRootPath);
        var targetDirectory = Path.Combine(ExtensionsRootPath, extensionId);
        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, $"{version}.zip");
        await File.WriteAllBytesAsync(targetPath, packageBytes, cancellationToken);
        return targetPath;
    }

    private static void WriteJsonEntry(ZipArchive archive, string entryName, object data)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        // 使用固定的时间戳，确保相同内容生成相同的hash
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(data, JsonOptions));
    }

    private static byte[] BuildDirectoryPackage(string directoryPath, string? iconOverride, bool includeUserShortcut)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteManifestEntry(archive, directoryPath, iconOverride, includeUserShortcut);
            
            // 使用固定的时间戳（2020-01-01），确保相同内容生成相同的hash
            var fixedTimestamp = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                         .Where(path => ShouldIncludeInPackage(directoryPath, path))
                         .OrderBy(path => Path.GetRelativePath(directoryPath, path), StringComparer.Ordinal))
            {
                var relativePath = Path.GetRelativePath(directoryPath, filePath);
                if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 手动创建entry并设置固定时间戳，而不是使用CreateEntryFromFile
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                entry.LastWriteTime = fixedTimestamp;
                
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(filePath);
                fileStream.CopyTo(entryStream);
            }
        }

        var dependencyDlls = Directory
            .EnumerateFiles(directoryPath, "*.dll", SearchOption.AllDirectories)
            .Where(path => ShouldIncludeInPackage(directoryPath, path))
            .Select(path => Path.GetRelativePath(directoryPath, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (dependencyDlls.Length > 0)
        {
            HostAssets.AppendLog(
                $"Extension package built: id={Path.GetFileName(directoryPath)}, zipBytes={stream.Length}, includeUserShortcut={includeUserShortcut}, dependencyDlls={string.Join(",", dependencyDlls)}");
        }

        return stream.ToArray();
    }

    private static void WriteManifestEntry(ZipArchive archive, string directoryPath, string? iconOverride, bool includeUserShortcut)
    {
        var manifestPath = Path.Combine(directoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("扩展目录里缺少 manifest.json。", manifestPath);
        }

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidOperationException("扩展目录中的 manifest.json 无效。");
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException("扩展目录中的 manifest.json 缺少 id 或 name。");
        }

        var packagedManifest = string.IsNullOrWhiteSpace(iconOverride)
            ? manifest
            : manifest with { Icon = iconOverride };

        if (!includeUserShortcut)
        {
            packagedManifest = packagedManifest with { GlobalShortcut = null };
        }

        WriteJsonEntry(
            archive,
            "manifest.json",
            packagedManifest);
    }

    internal static bool ShouldIncludeInPackage(string rootDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, filePath);
        var normalizedRelativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var segments = normalizedRelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        // 内联脚本执行时生成的临时文件不属于小程序内容。将其打包会让每次运行都产生新哈希并重复上传。
        if (segments.Length == 1 &&
            (segments[0].StartsWith(".yanzi-inline-", StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals("debug.log", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (normalizedRelativePath.Equals(".yanzi-csharp-cache", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelativePath.StartsWith(".yanzi-csharp-cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (segments.Any(static segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
                segment.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains(".bak-", StringComparison.OrdinalIgnoreCase) ||
                segment.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains(".backup-", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !(segments.Length == 1 &&
                 Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
