namespace OpenQuickHost;

/// <summary>
/// Mergeable, account-scoped receipts: an observed version never becomes unread
/// again when another device publishes an older snapshot.
/// </summary>
internal static class ExtensionSeenVersions
{
    internal static bool IncludesVersion(IReadOnlyDictionary<string, string>? receipts, string id, string? current)
    {
        return receipts != null &&
               !string.IsNullOrWhiteSpace(id) &&
               receipts.TryGetValue(id, out var seen) &&
               Compare(seen, current) >= 0;
    }

    internal static int Compare(string? left, string? right)
    {
        left = (left ?? "").Trim();
        right = (right ?? "").Trim();
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return 0;
        var a = left.Split('-', '+')[0];
        var b = right.Split('-', '+')[0];
        if (Version.TryParse(a, out var v1) && Version.TryParse(b, out var v2))
            return v1.CompareTo(v2);
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string>? local,
        IReadOnlyDictionary<string, string>? remote)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { local, remote })
        {
            if (source == null) continue;
            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                if (!merged.TryGetValue(pair.Key, out var existing) || Compare(pair.Value, existing) > 0)
                    merged[pair.Key] = pair.Value;
            }
        }
        return merged;
    }

    internal static bool Equivalent(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        var a = Merge(left, null);
        var b = Merge(right, null);
        return a.Count == b.Count &&
               a.All(pair => b.TryGetValue(pair.Key, out var version) &&
                             string.Equals(pair.Value, version, StringComparison.OrdinalIgnoreCase));
    }
}
