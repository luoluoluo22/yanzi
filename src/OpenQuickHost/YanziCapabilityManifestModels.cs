namespace OpenQuickHost;

/// <summary>
/// 小程序能力声明模型。
/// manifest 中 provides 节点对应此结构。
/// </summary>
public sealed class YanziCapabilityManifest
{
    public List<string> Requires { get; set; } = [];
    public List<YanziCapabilityDeclaration> Provides { get; set; } = [];
}

public sealed class YanziCapabilityDeclaration
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public List<string> Permissions { get; set; } = [];
    public string Audience { get; set; } = "user";
    public string Category { get; set; } = "general";
    public string RiskLevel { get; set; } = "low";
    public bool RequiresConfirmation { get; set; }
    public System.Text.Json.JsonElement InputSchema { get; set; } = YanziCapabilitySchema.Any;
    public System.Text.Json.JsonElement OutputSchema { get; set; } = YanziCapabilitySchema.Any;
}
