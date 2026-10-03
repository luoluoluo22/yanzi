namespace OpenQuickHost;

/// <summary>
/// 宿主内置能力注册入口。
/// 用于验证能力网络链路，后续可替换为独立小程序 Provider。
/// </summary>
public static class YanziBuiltinCapabilityRegistration
{
    public static void Register()
    {
        YanziCapabilityProviderSdk.RegisterProvider(
            "yanzi-host",
            new[]
            {
                new YanziCapabilityProviderDefinition
                {
                    Name = "system.info",
                    Description = "获取燕子运行信息",
                    InputSchema = YanziCapabilitySchema.EmptyObject,
                    OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"time\":{\"type\":\"string\"}}}"),
                    Handler = _ => Task.FromResult<object?>(new
                    {
                        name = "OpenQuickHost",
                        time = DateTime.UtcNow
                    })
                },
                new YanziCapabilityProviderDefinition
                {
                    Name = "capability.list", Description = "列出当前可调用能力及输入输出契约",
                    InputSchema = YanziCapabilitySchema.EmptyObject,
                    OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"array\",\"items\":{\"type\":\"object\"}}"),
                    Handler = _ => Task.FromResult<object?>(YanziCapabilityQueryService.ListCapabilities())
                },
                new YanziCapabilityProviderDefinition
                {
                    Name = "capability.describe", Description = "查询指定能力的版本、Schema 和所需权限",
                    InputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"name\"],\"additionalProperties\":false}"),
                    OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\"}"),
                    Handler = payload => Task.FromResult<object?>(YanziCapabilityQueryService.Describe(
                        ((System.Text.Json.JsonElement)payload!).GetProperty("name").GetString()!)
                        ?? throw new KeyNotFoundException("能力不存在"))
                }
            });

        YanziCapabilityProviderSdk.RegisterProvider(
            "yanzi-host",
            YanziSystemCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziFileWorkflow.Providers());

        YanziCapabilityProviderSdk.RegisterProvider(
            "yanzi-host",
            YanziClipboardCapabilityProvider.GetProviders());
    }
}
