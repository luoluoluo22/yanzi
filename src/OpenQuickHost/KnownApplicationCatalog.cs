using System.IO;

namespace OpenQuickHost;

internal sealed record KnownApplicationDefinition(
    string Id,
    string DisplayName,
    string WingetPackageId,
    string OfficialDownloadUrl,
    IReadOnlyList<string> AlternateWingetPackageIds,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<string> TitleAliases,
    IReadOnlyList<string> Keywords)
{
    public string ExtensionId => $"app-known-{Id}";
    public string InstallTarget => $"{KnownApplicationCatalog.InstallTargetPrefix}{Id}";
}

internal static class KnownApplicationCatalog
{
    public const string InstallTargetPrefix = "yanzi-app:";

    private static readonly KnownApplicationDefinition[] Definitions =
    [
        new(
            "wechat",
            "微信",
            "Tencent.WeChat.Universal",
            "https://windows.weixin.qq.com/?lang=zh_CN",
            ["Tencent.WeChat"],
            ["Weixin.exe", "WeChat.exe"],
            ["微信", "WeChat"],
            ["微信", "wechat", "weixin", "聊天", "腾讯"]),
        new(
            "qq",
            "QQ",
            "Tencent.QQ.NT",
            "https://im.qq.com/pcqq/index.shtml",
            ["Tencent.QQ"],
            ["QQ.exe"],
            ["QQ", "腾讯QQ"],
            ["qq", "腾讯qq", "聊天", "腾讯"]),
        new(
            "vscode",
            "Visual Studio Code",
            "Microsoft.VisualStudioCode",
            "https://code.visualstudio.com/",
            [],
            ["Code.exe"],
            ["Visual Studio Code", "VS Code"],
            ["vscode", "vs code", "visual studio code", "代码", "编辑器"]),
        new(
            "7zip",
            "7-Zip",
            "7zip.7zip",
            "https://www.7-zip.org/",
            [],
            ["7zFM.exe", "7zG.exe"],
            ["7-Zip", "7-Zip File Manager"],
            ["7zip", "7-zip", "压缩", "解压"]),
        new(
            "everything",
            "Everything",
            "voidtools.Everything",
            "https://www.voidtools.com/",
            [],
            ["Everything.exe"],
            ["Everything"],
            ["everything", "文件搜索", "搜索"]),
        new(
            "chrome",
            "Google Chrome",
            "Google.Chrome",
            "https://www.google.com/chrome/",
            [],
            ["chrome.exe"],
            ["Google Chrome", "Chrome"],
            ["chrome", "google chrome", "浏览器", "谷歌浏览器"])
    ];

    public static IReadOnlyList<KnownApplicationDefinition> All => Definitions;

    public static KnownApplicationDefinition? Match(string? title, string? displayPath)
    {
        var normalizedTitle = (title ?? string.Empty).Trim();
        var executableName = string.IsNullOrWhiteSpace(displayPath)
            ? string.Empty
            : Path.GetFileName((displayPath ?? string.Empty).Trim().Trim('"'));

        foreach (var definition in Definitions)
        {
            if (!string.IsNullOrWhiteSpace(executableName) &&
                definition.ExecutableNames.Any(name =>
                    executableName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return definition;
            }

            if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
                definition.TitleAliases.Any(alias =>
                    normalizedTitle.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            {
                return definition;
            }
        }

        return null;
    }

    public static bool TryGetByExtensionId(string? extensionId, out KnownApplicationDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            item.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase))!;
        return definition != null;
    }

    public static bool TryGetById(string? id, out KnownApplicationDefinition definition)
    {
        definition = Definitions.FirstOrDefault(item =>
            item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))!;
        return definition != null;
    }

    public static bool TryGetByTarget(string? target, out KnownApplicationDefinition definition)
    {
        definition = null!;
        if (string.IsNullOrWhiteSpace(target) ||
            !target.StartsWith(InstallTargetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryGetById(target[InstallTargetPrefix.Length..], out definition);
    }

    public static bool IsInstallPlaceholder(string? target) =>
        !string.IsNullOrWhiteSpace(target) &&
        target.StartsWith(InstallTargetPrefix, StringComparison.OrdinalIgnoreCase);

    public static InstalledApplicationEntry CreatePlaceholder(KnownApplicationDefinition definition)
    {
        var host = TryGetHost(definition.OfficialDownloadUrl);
        var subtitle = string.IsNullOrWhiteSpace(host)
            ? "未安装 · 回车后自动安装"
            : $"未安装 · 回车后自动安装 · 官网：{host}";

        return new InstalledApplicationEntry(
            definition.ExtensionId,
            definition.DisplayName,
            subtitle,
            definition.InstallTarget,
            definition.InstallTarget,
            null,
            definition.Keywords.Concat(definition.TitleAliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            null,
            null);
    }

    private static string TryGetHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : string.Empty;
    }
}
