using System.IO;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class ExtensionRecycleBinService
{
    public static IReadOnlyList<RecycledExtensionEntry> LoadEntries()
    {
        EnsureStorage();
        var index = LoadIndex();
        var changed = false;
        var items = new List<RecycledExtensionEntry>();
        foreach (var item in index.Items)
        {
            if (!Directory.Exists(GetRecycleDirectoryPath(item.ItemId)))
            {
                changed = true;
                continue;
            }

            items.Add(item);
        }

        if (changed)
        {
            SaveIndex(new ExtensionRecycleBinIndex { Items = items });
        }

        return items
            .OrderByDescending(item => ParseTimestamp(item.DeletedAtUtc))
            .ToList();
    }

    public static RecycledExtensionEntry MoveToRecycleBin(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        EnsureStorage();
        var entry = LocalExtensionCatalog.LoadEntries()
            .FirstOrDefault(item => item.Manifest.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            throw new DirectoryNotFoundException("没有找到对应扩展目录。");
        }

        var sourceDirectory = Path.GetDirectoryName(entry.ManifestPath);
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException("没有找到对应扩展目录。");
        }

        var itemId = $"{extensionId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var targetDirectory = GetRecycleDirectoryPath(itemId);
        Directory.Move(sourceDirectory, targetDirectory);

        var recycleEntry = new RecycledExtensionEntry
        {
            ItemId = itemId,
            ExtensionId = entry.Manifest.Id,
            Title = entry.Manifest.Name,
            Category = entry.Manifest.Category ?? "扩展",
            Version = entry.Manifest.Version ?? "0.1.0",
            DeletedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            OriginalDirectoryPath = sourceDirectory
        };

        var index = LoadIndex();
        index.Items.Add(recycleEntry);
        SaveIndex(index);
        HostAssets.AppendLog($"Extension moved to recycle bin: id={recycleEntry.ExtensionId}, itemId={recycleEntry.ItemId}, title={recycleEntry.Title}");
        return recycleEntry;
    }

    public static RecycledExtensionEntry RestoreFromRecycleBin(string itemId)
    {
        var index = LoadIndex();
        var item = index.Items.FirstOrDefault(entry =>
            entry.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            throw new InvalidOperationException("没有找到对应回收站项目。");
        }

        var sourceDirectory = GetRecycleDirectoryPath(item.ItemId);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException("回收站里的扩展目录不存在。");
        }

        Directory.CreateDirectory(LocalExtensionCatalog.CatalogRootPath);
        var targetDirectory = Path.Combine(LocalExtensionCatalog.CatalogRootPath, item.ExtensionId);
        if (Directory.Exists(targetDirectory))
        {
            throw new InvalidOperationException("本地已存在同名扩展，无法恢复。");
        }

        Directory.Move(sourceDirectory, targetDirectory);
        index.Items.Remove(item);
        SaveIndex(index);
        return item;
    }

    public static RecycledExtensionEntry DeletePermanently(string itemId)
    {
        var index = LoadIndex();
        var item = index.Items.FirstOrDefault(entry =>
            entry.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            throw new InvalidOperationException("没有找到对应回收站项目。");
        }

        var sourceDirectory = GetRecycleDirectoryPath(item.ItemId);
        if (Directory.Exists(sourceDirectory))
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }

        index.Items.Remove(item);
        SaveIndex(index);
        return item;
    }

    public static int PurgeAllByExtensionId(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return 0;
        }

        var index = LoadIndex();
        var matched = index.Items
            .Where(item => item.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matched.Count == 0)
        {
            return 0;
        }

        foreach (var item in matched)
        {
            var sourceDirectory = GetRecycleDirectoryPath(item.ItemId);
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(sourceDirectory, recursive: true);
            }

            index.Items.Remove(item);
        }

        SaveIndex(index);
        return matched.Count;
    }

    private static ExtensionRecycleBinIndex LoadIndex()
    {
        try
        {
            if (!File.Exists(HostAssets.ExtensionRecycleBinIndexPath))
            {
                return new ExtensionRecycleBinIndex();
            }

            var json = File.ReadAllText(HostAssets.ExtensionRecycleBinIndexPath);
            return JsonSerializer.Deserialize<ExtensionRecycleBinIndex>(json, JsonOptions) ?? new ExtensionRecycleBinIndex();
        }
        catch
        {
            return new ExtensionRecycleBinIndex();
        }
    }

    private static void SaveIndex(ExtensionRecycleBinIndex index)
    {
        EnsureStorage();
        var json = JsonSerializer.Serialize(index, JsonOptions);
        File.WriteAllText(HostAssets.ExtensionRecycleBinIndexPath, json);
    }

    private static void EnsureStorage()
    {
        Directory.CreateDirectory(HostAssets.ExtensionRecycleBinPath);
    }

    private static string GetRecycleDirectoryPath(string itemId)
    {
        return Path.Combine(HostAssets.ExtensionRecycleBinPath, itemId);
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed class ExtensionRecycleBinIndex
{
    public List<RecycledExtensionEntry> Items { get; set; } = [];
}

public sealed class RecycledExtensionEntry
{
    public string ItemId { get; set; } = string.Empty;

    public string ExtensionId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = "扩展";

    public string Version { get; set; } = "0.1.0";

    public string DeletedAtUtc { get; set; } = string.Empty;

    public string OriginalDirectoryPath { get; set; } = string.Empty;
}
