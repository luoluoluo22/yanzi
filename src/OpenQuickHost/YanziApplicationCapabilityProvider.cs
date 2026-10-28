using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// 将当前 Windows 已安装的用户可见应用统一暴露给能力网络。
/// 不为每个软件硬编码一套 Provider；新安装的软件会自动出现在 app.list/search 中。
/// </summary>
public static class YanziApplicationCapabilityProvider
{
    private static readonly JsonElement AppQuerySchema = YanziCapabilitySchema.Parse("""
    {"type":"object","properties":{"app":{"type":"string","minLength":1}},"required":["app"],"additionalProperties":false}
    """);

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "app.list",
            Description = "列出当前 Windows 可启动应用；新安装应用无需重新注册能力",
            Permissions = ["application.read"], Category = "applications",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":500,"default":200}},"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"items":{"type":"array"}},"required":["items"]}"""),
            Handler = ListAsync
        };
        yield return new()
        {
            Name = "app.search",
            Description = "按名称、别名、关键词或启动路径搜索 Windows 应用",
            Permissions = ["application.read"], Category = "applications",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"query":{"type":"string","minLength":1},"limit":{"type":"integer","minimum":1,"maximum":100,"default":20}},"required":["query"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"items":{"type":"array"}},"required":["items"]}"""),
            Handler = SearchAsync
        };
        yield return new()
        {
            Name = "app.status",
            Description = "查询 Windows 应用是否已安装以及燕子识别到的启动入口",
            Permissions = ["application.read"], Category = "applications",
            InputSchema = AppQuerySchema,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };
        yield return new()
        {
            Name = "app.open",
            Description = "打开或激活 Windows 应用；对燕子已知的缺失常用软件可先自动安装再启动",
            Permissions = ["application.run"], Category = "applications", RiskLevel = "low",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"app":{"type":"string","minLength":1},"arguments":{"type":"string"}},"required":["app"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenAsync
        };
        yield return new()
        {
            Name = "app.openFile",
            Description = "使用指定 Windows 应用打开本地文件或项目路径",
            Permissions = ["application.run", "files.read"], Category = "applications", RiskLevel = "low",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"app":{"type":"string","minLength":1},"path":{"type":"string","minLength":1},"arguments":{"type":"string"}},"required":["app","path"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = OpenFileAsync
        };
        yield return new()
        {
            Name = "app.ensure",
            Description = "确保燕子已知的常用 Windows 应用已经安装；优先 winget，失败时返回官方下载信息",
            Permissions = ["application.install"], Category = "applications", RiskLevel = "medium",
            InputSchema = AppQuerySchema,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = EnsureAsync
        };
    }

    private static IReadOnlyList<InstalledApplicationEntry> LoadInstalled() => InstalledApplicationCatalog.Load();

    private static object Describe(InstalledApplicationEntry entry) => new
    {
        id = entry.ExtensionId,
        name = entry.Title,
        installed = !KnownApplicationCatalog.IsInstallPlaceholder(entry.LaunchTarget),
        launchTarget = entry.LaunchTarget,
        displayPath = entry.DisplayPath,
        arguments = entry.Arguments ?? "",
        workingDirectory = entry.WorkingDirectory ?? "",
        aliases = entry.Keywords
    };

    private static IEnumerable<InstalledApplicationEntry> Match(IEnumerable<InstalledApplicationEntry> apps, string query)
    {
        query = query.Trim();
        var exact = apps.Where(a =>
            a.ExtensionId.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            a.Title.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            a.Keywords.Any(k => k.Equals(query, StringComparison.OrdinalIgnoreCase)) ||
            a.DisplayPath.Equals(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0) return exact;

        return apps.Where(a =>
            a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            a.ExtensionId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            a.DisplayPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            a.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static InstalledApplicationEntry? ResolveInstalled(string query)
    {
        query = query.Trim();
        var apps = LoadInstalled();

        var exactIdentity = apps.Where(a =>
            a.ExtensionId.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            a.Title.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            a.DisplayPath.Equals(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exactIdentity.Length == 1) return exactIdentity[0];
        if (exactIdentity.Length > 1)
            throw new ArgumentException("应用标识不唯一：" + query + "。请使用 app.search 后提供应用 ID。");

        var exactAlias = apps.Where(a =>
            a.Keywords.Any(k => k.Equals(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (exactAlias.Length == 1) return exactAlias[0];
        if (exactAlias.Length > 1)
            throw new ArgumentException("应用别名不唯一：" + query + "。请使用 app.search 后提供更精确名称。");

        var matches = Match(apps, query).Take(8).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new ArgumentException("应用名称不唯一：" + query + "。请使用 app.search 后提供更精确名称。")
        };
    }

    private static KnownApplicationDefinition? ResolveKnown(string query)
    {
        if (KnownApplicationCatalog.TryGetById(query, out var byId)) return byId;
        if (KnownApplicationCatalog.TryGetByExtensionId(query, out var byExtension)) return byExtension;
        return KnownApplicationCatalog.All.FirstOrDefault(d =>
            d.DisplayName.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            d.TitleAliases.Any(x => x.Equals(query, StringComparison.OrdinalIgnoreCase)) ||
            d.Keywords.Any(x => x.Equals(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static Task<object?> ListAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var query = input.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
        var limit = input.TryGetProperty("limit", out var l) ? l.GetInt32() : 200;
        var apps = LoadInstalled();
        var selected = string.IsNullOrWhiteSpace(query) ? apps : Match(apps, query);
        return Task.FromResult<object?>(new { items = selected.Take(limit).Select(Describe).ToArray() });
    }

    private static Task<object?> SearchAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var query = input.GetProperty("query").GetString()!;
        var limit = input.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        return Task.FromResult<object?>(new { items = Match(LoadInstalled(), query).Take(limit).Select(Describe).ToArray() });
    }

    private static Task<object?> StatusAsync(object? payload)
    {
        var query = ((JsonElement)payload!).GetProperty("app").GetString()!;
        var installed = ResolveInstalled(query);
        if (installed != null) return Task.FromResult<object?>(Describe(installed));
        var known = ResolveKnown(query);
        if (known == null)
            return Task.FromResult<object?>(new { app = query, installed = false, known = false });
        return Task.FromResult<object?>(new
        {
            app = known.DisplayName,
            installed = false,
            known = true,
            id = known.Id,
            downloadUrl = known.OfficialDownloadUrl,
            wingetPackageId = known.WingetPackageId
        });
    }

    private static async Task<InstalledApplicationEntry> EnsureEntryAsync(string query)
    {
        var installed = ResolveInstalled(query);
        if (installed != null && !KnownApplicationCatalog.IsInstallPlaceholder(installed.LaunchTarget)) return installed;

        var known = ResolveKnown(query);
        if (known == null) throw new KeyNotFoundException("未找到可安装的已知应用：" + query);
        var result = await KnownApplicationInstallerService.EnsureInstalledAsync(known);
        if (!result.Success || result.InstalledEntry == null)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Details) ? result.Message : result.Message + " " + result.Details);
        return result.InstalledEntry;
    }

    private static async Task<object?> OpenAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var query = input.GetProperty("app").GetString()!;
        var extra = input.TryGetProperty("arguments", out var a) ? a.GetString() : null;
        var entry = await EnsureEntryAsync(query);
        var arguments = JoinArguments(entry.Arguments, extra);
        var result = QuickWindowSwitchService.ExecuteToggleOrLaunch(
            entry.LaunchTarget, arguments,
            string.IsNullOrWhiteSpace(entry.WorkingDirectory) ? null : entry.WorkingDirectory,
            entry.Title);
        if (result.Action == WindowToggleAction.Failed) throw result.Exception ?? new InvalidOperationException(result.Message);
        return new { app = entry.Title, action = result.Action.ToString(), result.Message, launchTarget = entry.LaunchTarget };
    }

    private static async Task<object?> OpenFileAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var query = input.GetProperty("app").GetString()!;
        var rawPath = input.GetProperty("path").GetString()!;
        var path = Path.GetFullPath(rawPath);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("要打开的路径不存在。", path);
        var entry = await EnsureEntryAsync(query);
        if (entry.LaunchTarget.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("该应用入口不支持携带文件参数。");

        var extra = input.TryGetProperty("arguments", out var a) ? a.GetString() : null;
        var arguments = JoinArguments(entry.Arguments, extra, Quote(path));
        var start = QuickWindowSwitchService.CreateLaunchProcessStartInfo(
            entry.LaunchTarget, arguments,
            string.IsNullOrWhiteSpace(entry.WorkingDirectory) ? null : entry.WorkingDirectory);
        Process.Start(start);
        return new { app = entry.Title, opened = path, launchTarget = entry.LaunchTarget };
    }

    private static async Task<object?> EnsureAsync(object? payload)
    {
        var query = ((JsonElement)payload!).GetProperty("app").GetString()!;
        var entry = await EnsureEntryAsync(query);
        return new { installed = true, app = entry.Title, id = entry.ExtensionId, launchTarget = entry.LaunchTarget };
    }

    private static string JoinArguments(params string?[] values) =>
        string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
