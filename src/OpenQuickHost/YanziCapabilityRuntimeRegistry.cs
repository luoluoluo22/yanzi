using System.Collections.Concurrent;

namespace OpenQuickHost;

/// <summary>
/// 能力运行实例注册表。负责记录“哪个小程序提供哪个能力”。
/// 小程序动态加载后注册，不由宿主写死。
/// </summary>
public static class YanziCapabilityRuntimeRegistry
{
    private static readonly ConcurrentDictionary<string, CapabilityRuntime> RuntimeMap = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(CapabilityRuntime runtime)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(runtime.Name)) return;
        RuntimeMap[runtime.Name.Trim()] = runtime;
    }

    public static bool TryGet(string name, out CapabilityRuntime? runtime)
        => RuntimeMap.TryGetValue(name.Trim(), out runtime);

    public static IReadOnlyList<CapabilityRuntime> List()
        => RuntimeMap.Values.OrderBy(x => x.Name).ToList();

    public static bool Contains(string name)
        => RuntimeMap.ContainsKey(name);

    public static void RemoveByExtension(string extensionId)
    {
        foreach (var item in RuntimeMap.Where(x => x.Value.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (((ICollection<KeyValuePair<string, CapabilityRuntime>>)RuntimeMap).Remove(item))
                YanziCapabilityRegistry.Unregister(item.Key, extensionId);
        }
        YanziCapabilityRuntimeBinding.UnbindByExtension(extensionId);
    }
}

public sealed record CapabilityRuntime(
    string Name,
    string ExtensionId,
    string Description,
    Func<object?, Task<object?>> Handler);
