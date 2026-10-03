namespace OpenQuickHost;

/// <summary>
/// 小程序能力提供者 SDK。
/// 扩展只需要调用 RegisterProvider，即可向能力网络暴露能力。
/// </summary>
public static class YanziCapabilityProviderSdk
{
    public static void RegisterProvider(
        string extensionId,
        IEnumerable<YanziCapabilityProviderDefinition> providers)
    {
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
                continue;

            YanziCapabilitySchema.ValidateDefinition(provider.InputSchema);
            YanziCapabilitySchema.ValidateDefinition(provider.OutputSchema);

            YanziCapabilityRuntimeBinding.Bind(
                provider.Name,
                extensionId);

            YanziCapabilityRuntimeRegistry.Register(new CapabilityRuntime(provider.Name, extensionId, provider.Description, provider.Handler));
            YanziCapabilityRegistry.Register(new YanziCapabilityDefinition
            {
                Name = provider.Name.Trim(), ProviderExtensionId = extensionId, Description = provider.Description,
                Version = provider.Version, InputSchema = provider.InputSchema.Clone(), OutputSchema = provider.OutputSchema.Clone(),
                Permissions = provider.Permissions.ToArray(), Handler = provider.Handler
            });
        }
    }
}

public sealed class YanziCapabilityProviderDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public System.Text.Json.JsonElement InputSchema { get; init; } = YanziCapabilitySchema.Any;
    public System.Text.Json.JsonElement OutputSchema { get; init; } = YanziCapabilitySchema.Any;
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public Func<object?, Task<object?>> Handler { get; init; } = _ => Task.FromResult<object?>(null);
}
