using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// 共享 JsonSerializerOptions：避免每处 new 造成配置漂移。
/// 只合并配置完全相同的用法——转义器/缩进等差异会改变落盘格式，禁止顺手"统一"。
/// </summary>
public static class JsonDefaults
{
    /// <summary>仅反序列化用：大小写不敏感。</summary>
    public static JsonSerializerOptions CaseInsensitive { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>CamelCase + 缩进 + 大小写不敏感（默认转义器）。</summary>
    public static JsonSerializerOptions CamelCaseIndented { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
