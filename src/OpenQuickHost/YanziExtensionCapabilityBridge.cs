namespace OpenQuickHost;

/// <summary>
/// 小程序能力桥接层。
/// 将宿主收到的能力请求转发给具体运行中的小程序实例。
/// </summary>
public sealed class YanziExtensionCapabilityBridge
{
    public async Task<object?> InvokeAsync(string extensionId, string capability, object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
            return new { error = "extension id required" };

        var command = OpenQuickHost.Sync.LocalExtensionCatalog.LoadCommands().FirstOrDefault(x =>
            string.Equals(x.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase));
        if (command == null) return YanziCapabilityInvocationResult.Failed("小程序不存在", "caller_not_found");
        return await YanziCapabilityInvocationService.InvokeAsync(capability, payload,
            new YanziCapabilityCaller(command.ExtensionId, command.Permissions));
    }
}
