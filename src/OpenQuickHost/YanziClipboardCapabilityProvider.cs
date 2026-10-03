namespace OpenQuickHost;

/// <summary>
/// 剪贴板能力 Provider。
/// 将已有剪贴板基础服务转换为能力节点。
/// </summary>
public static class YanziClipboardCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> GetProviders()
    {
        yield return new YanziCapabilityProviderDefinition
        {
            Name = "clipboard.latest",
            Description = "获取当前剪贴板文本",
            Permissions = new[] { "clipboard" },
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"},\"time\":{\"type\":\"string\"}},\"required\":[\"text\",\"time\"]}"),
            Handler = _ => Task.FromResult<object?>(new
            {
                text = ClipboardService.GetText() ?? string.Empty,
                time = DateTime.UtcNow
            })
        };

        yield return new YanziCapabilityProviderDefinition
        {
            Name = "clipboard.set",
            Description = "写入剪贴板文本",
            Permissions = new[] { "clipboard" },
            InputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}},\"required\":[\"text\"],\"additionalProperties\":false}"),
            OutputSchema = YanziCapabilitySchema.Parse("{\"type\":\"object\",\"properties\":{\"success\":{\"type\":\"boolean\"}},\"required\":[\"success\"]}"),
            Handler = payload =>
            {
                var text = ((System.Text.Json.JsonElement)payload!).GetProperty("text").GetString()!;
                ClipboardService.SetText(text);
                return Task.FromResult<object?>(new { success = true });
            }
        };
    }
}
