using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace OpenQuickHost;

public sealed class SearchUsageMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly object SaveLock = new();
    private static SearchUsageMemory? _pendingMemory;
    private static DispatcherTimer? _saveDebounceTimer;

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
        if (memory == null)
        {
            return;
        }

        lock (SaveLock)
        {
            _pendingMemory = memory;
            if (_saveDebounceTimer == null)
            {
                _saveDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _saveDebounceTimer.Tick += (_, _) =>
                {
                    _saveDebounceTimer!.Stop();
                    SearchUsageMemory? toWrite;
                    lock (SaveLock)
                    {
                        toWrite = _pendingMemory;
                        _pendingMemory = null;
                    }
                    if (toWrite != null)
                    {
                        WriteToDisk(toWrite);
                    }
                };
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }
    }

    public static void FlushPendingSaves()
    {
        SearchUsageMemory? toWrite;
        lock (SaveLock)
        {
            toWrite = _pendingMemory;
            _pendingMemory = null;
            _saveDebounceTimer?.Stop();
        }
        if (toWrite != null)
        {
            WriteToDisk(toWrite);
        }
    }

    private static void WriteToDisk(SearchUsageMemory memory)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HostAssets.SearchMemoryPath)!);
            File.WriteAllText(HostAssets.SearchMemoryPath, JsonSerializer.Serialize(memory, JsonOptions));
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SearchUsageMemory.Save failed: {ex.Message}");
        }
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

    public int GetUsageCount(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        return Items.TryGetValue(key, out var entry) ? entry.Count : 0;
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
