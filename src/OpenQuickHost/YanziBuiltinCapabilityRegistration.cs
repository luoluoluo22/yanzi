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
                    Name = "capability.list", Description = "列出当前可调用能力及输入输出契约", Audience = "system", Category = "capability",
                    InputSchema = YanziCapabilitySchema.EmptyObject,
                    OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"array\",\"items\":{\"type\":\"object\"}}"),
                    Handler = _ => Task.FromResult<object?>(YanziCapabilityQueryService.ListCapabilities())
                },
                new YanziCapabilityProviderDefinition
                {
                    Name = "capability.describe", Description = "查询指定能力的版本、Schema 和所需权限", Audience = "system", Category = "capability",
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
        YanziCapabilityProviderSdk.RegisterProvider(
            "yanzi-host",
            YanziSystemDependencyCapabilities.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziFileWorkflow.Providers());

        YanziCapabilityProviderSdk.RegisterProvider(
            "yanzi-host",
            YanziClipboardCapabilityProvider.GetProviders());

        YanziCapabilityProviderSdk.RegisterProvider(
            "yanzi-host",
            YanziChatCapabilityProvider.GetProviders());

        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziDeviceCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziScreenshotCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziOcrCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziExtensionCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziNotesCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziFilesCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziBoardCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziCalendarCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziShutdownCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziHomeCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziConvenienceCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziApplicationCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziMobileAppCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziWeChatCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziQqCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziVsCodeCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziWpsCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziNeteaseMusicCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziBaiduNetdiskCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziBaiduTransferCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziQuarkCloudDriveCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziQuarkTransferCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziBlenderCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziUnityCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziJianyingCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziVisualStudioCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziBrowserCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziGitCapabilityProvider.Create());
        HostAssets.AppendLog("[Capabilities] Git registered: " + string.Join(",",
            YanziCapabilityRegistry.List()
                .Where(item => item.Name.StartsWith("git.", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Name)));
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziEverythingCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziBandizipCapabilityProvider.Create());
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziThunderCapabilityProvider.Create());
    }
}
