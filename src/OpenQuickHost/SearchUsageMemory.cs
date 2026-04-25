using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

public sealed class SearchUsageMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public Dictionary<string, SearchUsageEntry> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SearchUsageMemory Load()
    {
        try
        {
            if (!File.Exists(HostAssets.SearchMemoryPath))
            {
                return new SearchUsageMemory();
            }

            var memory = JsonSerializer.Deserialize<SearchUsageMemory>(File.ReadAllText(HostAssets.SearchMemoryPath), JsonOptions)
                         ?? new SearchUsageMemory();
            memory.Items = new Dictionary<string, SearchUsageEntry>(memory.Items ?? [], StringComparer.OrdinalIgnoreCase);
            return memory;
        }
        catch
        {
            return new SearchUsageMemory();
        }
    }

    public static void Save(SearchUsageMemory memory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HostAssets.SearchMemoryPath)!);
        File.WriteAllText(HostAssets.SearchMemoryPath, JsonSerializer.Serialize(memory, JsonOptions));
    }

    public void Record(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!Items.TryGetValue(key, out var entry))
        {
            entry = new SearchUsageEntry();
            Items[key] = entry;
        }

        entry.Count++;
        entry.LastUsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public int Score(string key)
    {
        if (!Items.TryGetValue(key, out var entry))
        {
            return 0;
        }

        var countScore = Math.Min(entry.Count, 50) * 6;
        var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - entry.LastUsedAt;
        var recencyScore = ageSeconds switch
        {
            < 3600 => 60,
            < 86400 => 36,
            < 604800 => 18,
            _ => 0
        };
        return countScore + recencyScore;
    }

    public static SearchUsageMemory Merge(SearchUsageMemory left, SearchUsageMemory right)
    {
        var merged = new SearchUsageMemory();
        foreach (var pair in (left.Items ?? []).Concat(right.Items ?? []))
        {
            if (!merged.Items.TryGetValue(pair.Key, out var existing))
            {
                merged.Items[pair.Key] = new SearchUsageEntry
                {
                    Count = pair.Value.Count,
                    LastUsedAt = pair.Value.LastUsedAt
                };
                continue;
            }

            existing.Count = Math.Max(existing.Count, pair.Value.Count);
            existing.LastUsedAt = Math.Max(existing.LastUsedAt, pair.Value.LastUsedAt);
        }

        return merged;
    }
}

public sealed class SearchUsageEntry
{
    public int Count { get; set; }

    public long LastUsedAt { get; set; }
}
