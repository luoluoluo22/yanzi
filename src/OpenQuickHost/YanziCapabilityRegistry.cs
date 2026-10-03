using System.Collections.Concurrent;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// 燕子能力注册中心。
/// 能力不是宿主写死，而由运行中的小程序动态注册。
/// 小程序安装/启动后可以声明自己提供的能力，其他小程序和 AI 可以发现并调用。
/// </summary>
public static class YanziCapabilityRegistry
{
    private static readonly ConcurrentDictionary<string, YanziCapabilityDefinition> Capabilities = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(YanziCapabilityDefinition capability)
    {
        if (capability == null || string.IsNullOrWhiteSpace(capability.Name) || capability.Handler == null)
        {
            return;
        }

        YanziCapabilitySchema.ValidateDefinition(capability.InputSchema);
        YanziCapabilitySchema.ValidateDefinition(capability.OutputSchema);
        Capabilities[Normalize(capability.Name)] = new YanziCapabilityDefinition
        {
            Name = Normalize(capability.Name), ProviderExtensionId = capability.ProviderExtensionId,
            Description = capability.Description, Version = capability.Version,
            InputSchema = capability.InputSchema.Clone(), OutputSchema = capability.OutputSchema.Clone(),
            Permissions = Array.AsReadOnly(capability.Permissions.ToArray()), Handler = capability.Handler
        };
    }

    public static void Register(
        string name,
        Func<object?, Task<object?>> handler,
        string providerExtensionId = "host",
        string description = "")
    {
        Register(new YanziCapabilityDefinition
        {
            Name = name,
            ProviderExtensionId = providerExtensionId,
            Description = description,
            Handler = handler
        });
    }

    public static bool Unregister(string name, string? providerExtensionId = null)
    {
        if (!Capabilities.TryGetValue(Normalize(name), out var current))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(providerExtensionId) &&
            !string.Equals(current.ProviderExtensionId, providerExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ((ICollection<KeyValuePair<string, YanziCapabilityDefinition>>)Capabilities)
            .Remove(new KeyValuePair<string, YanziCapabilityDefinition>(Normalize(name), current));
    }

    public static bool Contains(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && Capabilities.ContainsKey(Normalize(name));
    }

    public static bool TryGet(string name, out YanziCapabilityDefinition? capability)
        => Capabilities.TryGetValue(Normalize(name), out capability);

    public static async Task<object?> InvokeAsync(string name, object? parameters = null,
        YanziCapabilityCaller? caller = null)
    {
        if (!Capabilities.TryGetValue(Normalize(name), out var capability))
        {
            throw new InvalidOperationException($"未知燕子能力：{name}");
        }

        caller ??= YanziCapabilityCaller.Anonymous;
        if (!caller.IsTrusted)
        {
            var missing = capability.Permissions.Where(p => !caller.Permissions.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0)
                throw new UnauthorizedAccessException($"缺少能力权限：{string.Join(", ", missing)}");
        }
        var input = parameters is JsonElement element ? element.Clone() : JsonSerializer.SerializeToElement(parameters);
        // No-argument capabilities accept an omitted payload as an empty object.
        if (input.ValueKind == JsonValueKind.Null && capability.InputSchema.TryGetProperty("type", out var type)
            && type.GetString() == "object") input = JsonSerializer.SerializeToElement(new { });
        YanziCapabilitySchema.Validate(capability.InputSchema, input);
        return await capability.Handler(input);
    }

    public static IReadOnlyList<YanziCapabilityDescriptor> List()
    {
        return Capabilities.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new YanziCapabilityDescriptor(x.Name, x.Description, x.ProviderExtensionId,
                x.Version, x.InputSchema, x.OutputSchema, x.Permissions))
            .ToArray();
    }

    private static string Normalize(string name) => (name ?? string.Empty).Trim();
}

public sealed class YanziCapabilityDefinition
{
    public required string Name { get; init; }
    public string ProviderExtensionId { get; init; } = "host";
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public JsonElement InputSchema { get; init; } = YanziCapabilitySchema.Any;
    public JsonElement OutputSchema { get; init; } = YanziCapabilitySchema.Any;
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public required Func<object?, Task<object?>> Handler { get; init; }
}

public sealed record YanziCapabilityDescriptor(string Name, string Description, string ProviderExtensionId,
    string Version, JsonElement InputSchema, JsonElement OutputSchema, IReadOnlyList<string> Permissions);

public sealed record YanziCapabilityCaller(string Id, IReadOnlyList<string> Permissions, bool IsTrusted = false)
{
    public static YanziCapabilityCaller Anonymous { get; } = new("anonymous", Array.Empty<string>());
    internal static YanziCapabilityCaller LocalAgent { get; } = new("local-agent", Array.Empty<string>(), true);
}
