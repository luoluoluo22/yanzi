namespace OpenQuickHost;

/// <summary>
/// 小程序侧调用能力的统一客户端。
/// 后续 JS/C#/外部 Agent 均可映射到该协议。
/// </summary>
public static class YanziCapabilityClient
{
    public static Task<YanziCapabilityInvocationResult> InvokeAsync(
        string capability,
        object? parameters = null, YanziCapabilityCaller? caller = null)
    {
        return YanziCapabilityInvocationService.InvokeAsync(capability, parameters, caller);
    }
}
