using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class ExtensionPackageService
{
    public static string ExtensionsRootPath => HostAssets.ExtensionsPath;

    public static byte[] BuildPackage(CommandItem command, string version)
    {
        if (!string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) &&
            Directory.Exists(command.ExtensionDirectoryPath))
        {
            return BuildDirectoryPackage(command.ExtensionDirectoryPath);
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
                    source = command.Source.ToString()
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
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(JsonSerializer.Serialize(data, JsonOptions));
    }

    private static byte[] BuildDirectoryPackage(string directoryPath)
    {
        using var stream = new MemoryStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                     .Where(path => ShouldIncludeInPackage(directoryPath, path)))
        {
            var relativePath = Path.GetRelativePath(directoryPath, filePath);
            archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
        }

        return stream.ToArray();
    }

    private static bool ShouldIncludeInPackage(string rootDirectory, string filePath)
    {
        var segments = Path.GetRelativePath(rootDirectory, filePath)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Any(static segment =>
                segment.Equals(".yanzi-csharp-cache", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !(segments.Length == 1 &&
                 Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
