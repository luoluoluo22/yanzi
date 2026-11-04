using System.IO;
using System.Text.Json;
using System.Threading;

namespace OpenQuickHost;

public sealed class SearchUsageMemory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly object SaveLock = new();
    private static readonly object ReadCacheLock = new();
    private static SearchUsageMemory? _pendingMemory;
    private static System.Threading.Timer? _saveDebounceTimer;
    private static readonly LatestSnapshotWriter<SearchUsageMemory> DiskWriter = new(
        WriteToDisk, ex => HostAssets.AppendLog($"SearchUsageMemory.Save failed: {ex.Message}"));

    // CommandItem and quick-panel bindings may request the same count thousands of times.
    // Resolve the migrated path once per data root, not on every binding evaluation.
    private static string? _cachedDataRoot;
    private static string? _cachedPath;
    private static SearchUsageMemory? _cachedMemory;
    private static DateTime _cachedWriteUtc;
    private static long _nextCacheCheckAt;

    public Dictionary<string, SearchUsageEntry> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SearchUsageMemory Load()
    {
        try
        {
            var path = HostAssets.SearchMemoryPath;
            if (!File.Exists(path))
            {
                return new SearchUsageMemory();
            }

            var memory = JsonSerializer.Deserialize<SearchUsageMemory>(File.ReadAllText(path), JsonOptions)
                         ?? new SearchUsageMemory();
            memory.Items = new Dictionary<string, SearchUsageEntry>(memory.Items ?? [], StringComparer.OrdinalIgnoreCase);
            return memory;
        }
        catch
        {
            return new SearchUsageMemory();
        }
    }

    /// <summary>
    /// Fast, read-only lookup for UI bindings. Recheck the file once per second so an
    /// external sync or settings import remains visible without a full parse per item.
    /// The authoritative Load() method retains its uncached semantics for sync merges.
    /// </summary>
    public static int GetCachedUsageCount(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;

        var root = HostAssets.DataRootPath;
        lock (ReadCacheLock)
        {
            var now = Environment.TickCount64;
            if (_cachedMemory == null || !string.Equals(_cachedDataRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                _cachedPath = HostAssets.SearchMemoryPath;
                _cachedDataRoot = root;
                _cachedMemory = Load();
                _cachedWriteUtc = File.GetLastWriteTimeUtc(_cachedPath);
                _nextCacheCheckAt = now + 1000;
            }
            else if (now >= _nextCacheCheckAt)
            {
                var modified = File.GetLastWriteTimeUtc(_cachedPath!);
                if (modified != _cachedWriteUtc)
                {
                    _cachedMemory = Load();
                    _cachedWriteUtc = modified;
                }
                _nextCacheCheckAt = now + 1000;
            }

            return _cachedMemory.GetUsageCount(key);
        }
    }

    public static void Save(SearchUsageMemory memory)
    {
        if (memory == null) return;

        // Capture before returning: Record() can mutate the live UI dictionary while
        // a background worker serializes the previous snapshot.
        var snapshot = Snapshot(memory);
        lock (SaveLock)
        {
            _pendingMemory = snapshot;
            _saveDebounceTimer ??= new System.Threading.Timer(static _ => QueuePendingSave(), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _saveDebounceTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);

            // Make newly recorded usage visible to later-created UI items immediately.
            lock (ReadCacheLock)
            {
                _cachedDataRoot = HostAssets.DataRootPath;
                _cachedPath = HostAssets.SearchMemoryPath;
                _cachedMemory = snapshot;
                _cachedWriteUtc = File.GetLastWriteTimeUtc(_cachedPath);
                _nextCacheCheckAt = Environment.TickCount64 + 1000;
            }
        }
    }

    private static void QueuePendingSave()
    {
        lock (SaveLock)
        {
            var toWrite = _pendingMemory;
            _pendingMemory = null;
            // Keep dequeue/enqueue ordered against a concurrent Save or Flush.
            if (toWrite != null) DiskWriter.Enqueue("search-memory", toWrite);
        }
    }

    /// <summary>Force the most recent snapshot to disk before normal application exit.</summary>
    public static void FlushPendingSaves()
    {
        lock (SaveLock)
        {
            _saveDebounceTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        QueuePendingSave();
        DiskWriter.WhenIdleAsync().GetAwaiter().GetResult();
    }

    private static SearchUsageMemory Snapshot(SearchUsageMemory source)
    {
        var snapshot = new SearchUsageMemory();
        foreach (var pair in source.Items ?? [])
        {
            if (pair.Value == null) continue;
            snapshot.Items[pair.Key] = new SearchUsageEntry
            {
                Count = pair.Value.Count,
                LastUsedAt = pair.Value.LastUsedAt
            };
        }
        return snapshot;
    }

    private static void WriteToDisk(SearchUsageMemory memory)
    {
        try
        {
            var path = HostAssets.SearchMemoryPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFile.AtomicWriteText(path, JsonSerializer.Serialize(memory, JsonOptions));
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SearchUsageMemory.Save failed: {ex.Message}");
        }
    }

    public void Record(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
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
        if (string.IsNullOrWhiteSpace(key)) return 0;
        return Items.TryGetValue(key, out var entry) ? entry.Count : 0;
    }

    public int Score(string key)
    {
        if (!Items.TryGetValue(key, out var entry)) return 0;
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
