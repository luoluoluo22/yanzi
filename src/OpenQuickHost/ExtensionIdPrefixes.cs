namespace OpenQuickHost;

/// <summary>
/// 扩展 ID 的合成前缀约定：这些前缀出现在轮盘槽位/命令的 ExtensionId 字段中，
/// 用于把"非真实扩展"的动作编码成字符串。改动任何前缀都会使用户已有配置失效。
/// </summary>
public static class ExtensionIdPrefixes
{
    /// <summary>模拟按键槽位：后跟格式化后的组合键文本（如 Ctrl+G）。</summary>
    public const string SimulatedKey = "keysim::";

    /// <summary>搜索/文件系统结果直开槽位：后跟 OpenTarget 路径或结果 Id。</summary>
    public const string SearchResult = "result::";
}
