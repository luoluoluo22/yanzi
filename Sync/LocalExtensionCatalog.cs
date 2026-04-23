using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class LocalExtensionCatalog
{
    public static string CatalogRootPath => ExtensionPackageService.ExtensionsRootPath;

    public static void EnsureSampleExtension()
    {
        Directory.CreateDirectory(CatalogRootPath);

        var extensionDirectory = Path.Combine(CatalogRootPath, "sample-notes");
        Directory.CreateDirectory(extensionDirectory);

        var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            return;
        }

        var manifest = new LocalExtensionManifest
        {
            Id = "sample-notes",
            Name = "快速便签",
            Version = "0.1.0",
            Category = "扩展",
            Description = "示例扩展：打开桌面扩展目录里的便签说明文件。",
            Keywords = ["note", "memo", "sample", "extension"],
            OpenTarget = Path.Combine(extensionDirectory, "README.txt")
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions));
        File.WriteAllText(
            Path.Combine(extensionDirectory, "README.txt"),
            "这是一个本地示例扩展。把 manifest.json 改掉后，宿主会在下次启动时重新扫描并自动尝试云同步。");
    }

    public static IReadOnlyList<CommandItem> LoadCommands()
    {
        if (!Directory.Exists(CatalogRootPath))
        {
            return [];
        }

        var commands = new List<CommandItem>();
        foreach (var manifestPath in Directory.EnumerateFiles(CatalogRootPath, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(json, JsonOptions);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
                {
                    continue;
                }

                commands.Add(new CommandItem(
                    glyph: "E",
                    title: manifest.Name,
                    subtitle: manifest.Description ?? $"来自本地扩展目录：{Path.GetDirectoryName(manifestPath)}",
                    category: manifest.Category ?? "扩展",
                    accentHex: "#FF38BDF8",
                    openTarget: manifest.OpenTarget,
                    keywords: manifest.Keywords ?? [],
                    source: CommandSource.LocalExtension,
                    extensionId: manifest.Id,
                    declaredVersion: manifest.Version ?? "0.1.0",
                    extensionDirectoryPath: Path.GetDirectoryName(manifestPath)));
            }
            catch
            {
                // Skip invalid manifests so one broken extension does not block the host.
            }
        }

        return commands;
    }

    public static CommandItem SaveJsonExtension(string json)
    {
        Directory.CreateDirectory(CatalogRootPath);

        var manifest = ParseManifest(json);

        var extensionDirectory = Path.Combine(CatalogRootPath, manifest.Id);
        Directory.CreateDirectory(extensionDirectory);
        var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new CommandItem(
            glyph: "J",
            title: manifest.Name,
            subtitle: manifest.Description ?? $"来自本地扩展目录：{extensionDirectory}",
            category: manifest.Category ?? "扩展",
            accentHex: "#FF22C55E",
            openTarget: manifest.OpenTarget,
            keywords: manifest.Keywords ?? [],
            source: CommandSource.LocalExtension,
            extensionId: manifest.Id,
            declaredVersion: manifest.Version ?? "0.1.0",
            extensionDirectoryPath: extensionDirectory);
    }

    public static string LoadManifestJson(string extensionId)
    {
        var manifestPath = GetManifestPath(extensionId);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("没有找到对应扩展的 manifest.json。", manifestPath);
        }

        return File.ReadAllText(manifestPath);
    }

    public static void DeleteExtension(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        var extensionDirectory = Path.Combine(CatalogRootPath, extensionId);
        if (!Directory.Exists(extensionDirectory))
        {
            throw new DirectoryNotFoundException("没有找到对应扩展目录。");
        }

        Directory.Delete(extensionDirectory, true);
    }

    public static CommandItem RenameExtension(string extensionId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("扩展名称不能为空。");
        }

        var manifestPath = GetManifestPath(extensionId);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("没有找到对应扩展的 manifest.json。", manifestPath);
        }

        var manifest = ParseManifest(File.ReadAllText(manifestPath));
        var renamed = new LocalExtensionManifest
        {
            Id = manifest.Id,
            Name = newName.Trim(),
            Version = manifest.Version,
            Category = manifest.Category,
            Description = manifest.Description,
            Keywords = manifest.Keywords,
            OpenTarget = manifest.OpenTarget
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(renamed, JsonOptions));
        return SaveJsonExtension(JsonSerializer.Serialize(renamed, JsonOptions));
    }

    public static string CreateTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = "my-json-extension",
            Name = "我的 JSON 扩展",
            Version = "0.1.0",
            Category = "扩展",
            Description = "示例：打开本地文档或目录。",
            Keywords = ["json", "extension"],
            OpenTarget = HostAssets.DocsReadmePath
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private static LocalExtensionManifest ParseManifest(string json)
    {
        var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(json, JsonOptions);
        if (manifest == null)
        {
            throw new InvalidOperationException("JSON 解析失败。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidOperationException("扩展必须包含 id。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException("扩展必须包含 name。");
        }

        return manifest;
    }

    private static string GetManifestPath(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        return Path.Combine(CatalogRootPath, extensionId, "manifest.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

public sealed class LocalExtensionManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = "0.1.0";

    public string? Category { get; init; }

    public string? Description { get; init; }

    public string[]? Keywords { get; init; }

    public string? OpenTarget { get; init; }
}
