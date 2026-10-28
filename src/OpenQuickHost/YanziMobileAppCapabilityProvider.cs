using System.Text.Json;

namespace OpenQuickHost;

public static class YanziMobileAppCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return Provider("phone.app.list", "列出目标手机可直接启动的 App；返回真实应用名称、包名和版本", "device.read", true,
            """{"type":"object","properties":{"target":{"type":"string"},"query":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":500,"default":200}},"additionalProperties":false}""",
            async input => await Invoke(input, "mobile.apps.list", new
            {
                query = GetString(input, "query") ?? "",
                limit = GetInt(input, "limit", 200)
            }));
        yield return Provider("phone.app.info", "查询手机 App 的包名、版本和启动入口", "device.read", true,
            """{"type":"object","properties":{"target":{"type":"string"},"app":{"type":"string","minLength":1}},"required":["app"],"additionalProperties":false}""",
            async input => await Invoke(input, "mobile.apps.info", new { app = GetString(input, "app") }));
        yield return Provider("phone.app.open", "在目标手机打开指定 App；支持应用名称或包名", "device.message.send", false,
            """{"type":"object","properties":{"target":{"type":"string"},"app":{"type":"string","minLength":1}},"required":["app"],"additionalProperties":false}""",
            async input => await Invoke(input, "mobile.apps.open", new { app = GetString(input, "app") }));
        yield return Provider("phone.app.openUri", "在目标手机打开网址、地图、支付或其他 Android URI；可指定由哪个 App 处理", "device.message.send", false,
            """{"type":"object","properties":{"target":{"type":"string"},"uri":{"type":"string","minLength":1},"app":{"type":"string"}},"required":["uri"],"additionalProperties":false}""",
            async input => await Invoke(input, "mobile.apps.openUri", new { uri = GetString(input, "uri"), app = GetString(input, "app") ?? "" }));
        yield return Provider("phone.app.shareText", "把文字交给目标手机的指定 App 或系统分享面板，不自动点击最终发送按钮", "device.message.send", false,
            """{"type":"object","properties":{"target":{"type":"string"},"text":{"type":"string","minLength":1},"app":{"type":"string"}},"required":["text"],"additionalProperties":false}""",
            async input => await Invoke(input, "mobile.apps.shareText", new { text = GetString(input, "text"), app = GetString(input, "app") ?? "" }));
        yield return Provider("phone.app.settings", "打开目标手机指定 App 的系统详情设置页", "device.message.send", false,
            """{"type":"object","properties":{"target":{"type":"string"},"app":{"type":"string","minLength":1}},"required":["app"],"additionalProperties":false}""",
            async input => await Invoke(input, "mobile.apps.settings", new { app = GetString(input, "app") }));
    }

    private static YanziCapabilityProviderDefinition Provider(
        string name, string description, string permission, bool readOnly, string inputSchema,
        Func<JsonElement, Task<object?>> handler) => new()
        {
            Name = name,
            Description = description,
            Permissions = [permission],
            Category = "mobile-applications",
            RiskLevel = readOnly ? "low" : "medium",
            InputSchema = YanziCapabilitySchema.Parse(inputSchema),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = payload => handler((JsonElement)payload!)
        };

    private static string? GetString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement input, string name, int fallback) =>
        input.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static Task<object?> Invoke(JsonElement input, string capability, object args) =>
        YanziMobileCapabilityClient.InvokeAsync(GetString(input, "target"), capability, args);
}
