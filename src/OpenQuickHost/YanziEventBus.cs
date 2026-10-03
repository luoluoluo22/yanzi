using System.Collections.Concurrent;

namespace OpenQuickHost;

/// <summary>
/// 燕子事件总线。用于未来设备、定时任务、用户行为触发自动化。
/// </summary>
public static class YanziEventBus
{
    private static readonly ConcurrentDictionary<string, List<Func<object?, Task>>> Subscribers = new(StringComparer.OrdinalIgnoreCase);

    public static void Subscribe(string eventName, Func<object?, Task> handler)
    {
        if (string.IsNullOrWhiteSpace(eventName) || handler == null) return;
        var list = Subscribers.GetOrAdd(eventName, _ => new List<Func<object?, Task>>());
        lock (list) list.Add(handler);
    }

    public static async Task PublishAsync(string eventName, object? payload = null)
    {
        if (!Subscribers.TryGetValue(eventName, out var list)) return;
        Func<object?, Task>[] handlers;
        lock (list) handlers = list.ToArray();
        foreach (var handler in handlers) await handler(payload);
    }
}
