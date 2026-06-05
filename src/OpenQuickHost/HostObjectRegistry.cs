using System.Collections.Concurrent;

namespace OpenQuickHost;

public static class HostObjectRegistry
{
    private static readonly ConcurrentDictionary<string, object> _registry = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string id, object obj)
    {
        if (string.IsNullOrWhiteSpace(id) || obj == null)
        {
            return;
        }
        _registry[id] = obj;
    }

    public static bool TryGetObject(string id, out object? obj)
    {
        return _registry.TryGetValue(id, out obj);
    }

    public static void Remove(string id)
    {
        _registry.TryRemove(id, out _);
    }
}
