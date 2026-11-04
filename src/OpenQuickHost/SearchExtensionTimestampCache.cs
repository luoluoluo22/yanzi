namespace OpenQuickHost;

/// <summary>
/// Bounded, short-lived metadata cache for search ranking. FileSystemWatcher catalog
/// reloads invalidate the cache immediately; the TTL covers missed notifications.
/// Only the WPF dispatcher accesses this cache.
/// </summary>
internal sealed class SearchExtensionTimestampCache
{
    private readonly Dictionary<string, (DateTime ModifiedUtc, long CheckedAt)> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _clock;
    private readonly long _ttlMs;

    internal SearchExtensionTimestampCache(long ttlMs = 1500, Func<long>? clock = null)
    {
        if (ttlMs <= 0) throw new ArgumentOutOfRangeException(nameof(ttlMs));
        _ttlMs = ttlMs;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    internal DateTime Get(string directoryPath, string? entryPoint, Func<DateTime> resolve)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return resolve();
        var key = directoryPath + "\u001f" + (entryPoint ?? string.Empty);
        var now = _clock();

        if (_cache.TryGetValue(key, out var entry) && now - entry.CheckedAt >= 0 &&
            now - entry.CheckedAt < _ttlMs)
            return entry.ModifiedUtc;

        var value = resolve();
        if (_cache.Count >= 4096) _cache.Clear();
        _cache[key] = (value, now);
        return value;
    }

    internal void Invalidate() => _cache.Clear();
    internal int Count => _cache.Count;
}

internal static class SearchPinnedScopeLookup
{
    internal static HashSet<string> Resolve(IEnumerable<string> scopeKeys, Func<string, bool> isPinnedProvider)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in scopeKeys)
            if (isPinnedProvider(key)) result.Add(key);
        return result;
    }
}
