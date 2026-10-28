namespace OpenQuickHost;

/// <summary>
/// 能力发现接口。
/// AI 或其他小程序可以先查询能力，再决定调用哪个节点。
/// </summary>
public static class YanziCapabilityQueryService
{
    public static IReadOnlyList<YanziCapabilityDescriptor> ListCapabilities()
        => YanziCapabilityRegistry.List();

    public static object? Describe(string name)
    {
        return YanziCapabilityRegistry.List().FirstOrDefault(x =>
            string.Equals(x.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
