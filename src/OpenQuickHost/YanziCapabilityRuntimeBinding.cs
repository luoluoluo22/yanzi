using System.Collections.Concurrent;

namespace OpenQuickHost;

/// <summary>
/// 能力与运行中的小程序实例绑定。
/// 能力声明解决“有什么能力”，绑定解决“当前谁提供能力”。
/// </summary>
public static class YanziCapabilityRuntimeBinding
{
    private static readonly ConcurrentDictionary<string, CapabilityRuntimeBinding> Bindings = new(StringComparer.OrdinalIgnoreCase);

    public static void Bind(string capability, string extensionId, int? processId = null)
    {
        if (string.IsNullOrWhiteSpace(capability) || string.IsNullOrWhiteSpace(extensionId))
            return;

        Bindings[capability.Trim()] = new CapabilityRuntimeBinding(
            capability.Trim(),
            extensionId,
            processId,
            DateTime.UtcNow,
            true);
    }

    public static bool TryGet(string capability, out CapabilityRuntimeBinding? binding)
        => Bindings.TryGetValue(capability.Trim(), out binding);

    public static IReadOnlyList<CapabilityRuntimeBinding> List()
        => Bindings.Values.OrderBy(x => x.Capability).ToList();

    public static void UnbindByExtension(string extensionId)
    {
        foreach (var item in Bindings.Where(x =>
                     x.Value.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ((ICollection<KeyValuePair<string, CapabilityRuntimeBinding>>)Bindings).Remove(item);
        }
    }
}

public sealed record CapabilityRuntimeBinding(
    string Capability,
    string ExtensionId,
    int? ProcessId,
    DateTime BoundAt,
    bool Running);
