using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// Read-only, allowlisted unified media search/detail/comments contract.
/// Runs the user's existing OpenCLI installation; never executes arbitrary commands
/// nor logs account content. Null is NOT treated as zero.
/// </summary>
internal static class BrowserMediaApi
{
    private static readonly string[] MetricNames =
        ["plays", "likes", "comments", "collects", "shares", "danmaku", "coins"];

    internal static string[] Command(string site, string operation, string value, int limit)
    {
        if (site is not ("xiaohongshu" or "douyin" or "bilibili"))
            throw new ArgumentException("不支持的平台。");
        if (operation is not ("search" or "detail" or "comments"))
            throw new ArgumentException("仅允许搜索、详情和评论只读操作。");
        if (limit is < 1 or > 20) throw new ArgumentException("数量必须为 1～20。");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 550 || value.Any(char.IsControl))
            throw new ArgumentException("关键词或作品地址不合法。");
        if (operation == "search")
        {
            if (value.Length > 80 || value.StartsWith('-') || !value.Any(c => !char.IsWhiteSpace(c)))
                throw new ArgumentException("关键词必须为 1～80 字，不能包含控制字符。");
            return site switch
            {
                "bilibili" => [site, "search", value, "--type", "video", "--page", "1", "--limit", limit.ToString(CultureInfo.InvariantCulture)],
                _ => [site, "search", value, "--limit", limit.ToString(CultureInfo.InvariantCulture)]
            };
        }
        if (site == "douyin")
            throw new ArgumentException("抖音公开视频详情与评论由已登录浏览器直读，不调用创作者后台数据接口。");
        var id = NormalizeTarget(site, value);
        return site switch
        {
            "xiaohongshu" => [site, operation == "detail" ? "note" : "comments", id,
                .. (operation == "comments" ? new[] { "--limit", limit.ToString(CultureInfo.InvariantCulture) } : Array.Empty<string>())],
            "bilibili" => [site, operation == "detail" ? "video" : "comments", id,
                .. (operation == "comments" ? new[] { "--limit", limit.ToString(CultureInfo.InvariantCulture) } : Array.Empty<string>())],
            _ => throw new ArgumentException("不支持的平台。")
        };
    }

    internal static string NormalizeTarget(string site, string raw)
    {
        var value = raw.Trim();
        if (site == "bilibili")
        {
            var id = Regex.Match(value, @"\ABV[A-Za-z0-9]{10}\z");
            if (id.Success) return id.Value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != "https" ||
                url.Host is not ("www.bilibili.com" or "bilibili.com"))
                throw new ArgumentException("请输入 B 站官方视频地址或 BV 号。");
            var match = Regex.Match(url.AbsolutePath, @"\A/video/(BV[A-Za-z0-9]{10})/?\z");
            if (!match.Success) throw new ArgumentException("B 站视频 BV 号无效。");
            return match.Groups[1].Value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var note) ||
            note.Scheme != "https" || note.Host != "www.xiaohongshu.com" ||
            !Regex.IsMatch(note.AbsolutePath, @"\A/(explore|search_result)/[A-Za-z0-9]{12,40}/?\z") ||
            note.UserInfo.Length > 0)
            throw new ArgumentException("请输入完整的小红书官方笔记 URL（包含必要的 xsec_token）。");
        if (note.Query.Length > 0 && !note.Query.Contains("xsec_token=", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("小红书笔记 URL 缺少 xsec_token。");
        return value;
    }

    internal static async Task<object> ReadAsync(string site, string operation, string value, int limit,
        string? requestedProfile, CancellationToken cancellation)
    {
        var command = Command(site, operation, value, limit); // validate before launching CLI
        var listing = await BrowserOpenCliReader.RunAsync(["profile", "list"], cancellation);
        var profile = BrowserOpenCliReader.SelectProfile(listing, requestedProfile);
        var output = await BrowserOpenCliReader.RunAsync(
            ["--profile", profile, .. command, "--window", "background",
             "--site-session", "ephemeral", "--keep-tab", "false", "--format", "json"], cancellation);
        var normalized = Normalize(site, operation, value, BrowserOpenCliReader.ParseOutput(output));
        return new
        {
            data = normalized,
            source = "opencli-browser-bridge",
            profile,
            collectedAt = DateTimeOffset.UtcNow
        };
    }

    private static object? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var n) ? n : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? StringField(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            var value = Property(e, n);
            if (value != null && !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static object? NumberField(JsonElement e, params string[] names)
    {
        foreach (var n in names)
        {
            var v = Property(e, n);
            if (v is long or int or double or float or decimal) return v;
            if (v is string str && long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x))
                return x;
        }
        return null;
    }

    private static Dictionary<string, object?> Metrics(string source)
        => MetricNames.ToDictionary(s => s, s => (object?)new { value = (object?)null, status = "not_provided", source });

    private static void SetMetric(Dictionary<string, object?> metrics, string key, object? value,
        string source, bool untrustedZero = false)
    {
        if (value == null) return;
        var unverified = untrustedZero && (value.Equals(0L) || value.Equals(0) || value.Equals(0.0));
        metrics[key] = new { value = unverified ? null : value, status = unverified ? "unverified" : "available", source };
    }

    private static List<JsonElement> Rows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToList();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "items", "list", "data" })
                if (root.TryGetProperty(key, out var rows) && rows.ValueKind == JsonValueKind.Array)
                    return rows.EnumerateArray().ToList();
        }
        throw new InvalidOperationException("平台未返回预期的列表，不能伪造空结果。");
    }

    internal static object Normalize(string site, string operation, string value, JsonElement root)
    {
        if (site is not ("xiaohongshu" or "douyin" or "bilibili") ||
            operation is not ("search" or "detail" or "comments"))
            throw new ArgumentException("无效的媒体操作。");
        var source = operation == "search" ? "opencli_search" :
                     operation == "detail" ? "opencli_detail" : "opencli_comments";
        if (operation == "search")
        {
            var items = Rows(root).Select(row =>
            {
                var url = StringField(row, "url", "link");
                var title = StringField(row, "title", "desc", "description");
                if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                    parsed.Scheme != "https" || !HostMatches(site, parsed.Host))
                    return null;
                var metrics = Metrics(source);
                SetMetric(metrics, "likes", NumberField(row, "likes"), source);
                // OpenCLI's Douyin search adapter has repeatedly returned fabricated zeros
                // for plays/comments/shares. Never present those as verified measurements.
                SetMetric(metrics, "plays", NumberField(row, "plays"), source, site == "douyin");
                SetMetric(metrics, "comments", NumberField(row, "comments"), source, site == "douyin");
                SetMetric(metrics, "shares", NumberField(row, "shares"), source, site == "douyin");
                return (object)new
                {
                    id = site == "bilibili"
                        ? Regex.Match(parsed.AbsolutePath, @"BV[A-Za-z0-9]{10}").Value
                        : site == "douyin"
                            ? Regex.Match(parsed.AbsolutePath, @"\d{12,22}").Value
                            : parsed.AbsolutePath.Trim('/').Split('/').Last(),
                    url, title, author = StringField(row, "author", "username"),
                    metrics, source
                };
            }).Where(x => x != null).ToArray();
            return new { schemaVersion = "1", site, operation, query = value, items,
                returned = items.Length, source, collectedAt = DateTimeOffset.UtcNow };
        }
        if (operation == "detail")
        {
            if (root.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("作品详情结构异常。");
            var fields = root.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => new { field = StringField(e, "field"), value = Property(e, "value") })
                .Where(e => e.field != null)
                .GroupBy(e => e.field!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().value, StringComparer.OrdinalIgnoreCase);
            object? Field(string name) => fields.GetValueOrDefault(name);
            object? Num(string name)
            {
                var v = Field(name);
                return v switch
                {
                    long or int or double => v,
                    string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
                    _ => null
                };
            }
            var metrics = Metrics(source);
            foreach (var (key, field) in new[] {
                ("plays", "view"), ("likes", "like"), ("likes", "likes"),
                ("comments", "reply"), ("comments", "comments"),
                ("collects", "favorite"), ("collects", "collects"),
                ("shares", "share"), ("danmaku", "danmaku"), ("coins", "coin")
            })
                SetMetric(metrics, key, Num(field), source);
            if (Field("title") == null || Field("author") == null)
                throw new InvalidOperationException("作品详情缺少标题或作者，拒绝标记为成功。");
            return new
            {
                schemaVersion = "1", site, operation,
                item = new { id = Field("bvid") ?? Field("id") ?? value,
                    url = site == "bilibili" ? "https://www.bilibili.com/video/" + value : value,
                    title = Field("title"), author = Field("author"),
                    description = Field("description") ?? Field("content"),
                    publishedAt = Field("publish_time"), duration = Field("duration"), metrics,
                    source },
                source, collectedAt = DateTimeOffset.UtcNow
            };
        }
        var comments = Rows(root).Select(row => new {
            id = StringField(row, "rpid", "comment_id"),
            author = StringField(row, "author", "username"),
            text = StringField(row, "text", "content"),
            likes = NumberField(row, "likes"),
            replyCount = NumberField(row, "replies"),
            publishedAt = StringField(row, "time"),
            source
        }).Where(row => !string.IsNullOrWhiteSpace(row.text)).ToArray();
        return new { schemaVersion = "1", site, operation,
            target = value, items = comments, returned = comments.Length,
            source, collectedAt = DateTimeOffset.UtcNow };
    }

    private static bool HostMatches(string site, string host)
        => site switch
        {
            "xiaohongshu" => host is "www.xiaohongshu.com" or "xiaohongshu.com",
            "douyin" => host is "www.douyin.com" or "douyin.com",
            "bilibili" => host is "www.bilibili.com" or "bilibili.com",
            _ => false
        };
}
