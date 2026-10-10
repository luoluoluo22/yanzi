using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// 博客园 PAT 专用能力。密钥只在宿主侧附加至固定的 HTTPS API 请求；不会包含在能力结果中。
/// 草稿采用 DPAPI 本机加密，不冒充博客园云端草稿。
/// </summary>
public static class YanziCnblogsCapabilityProvider
{
    private const string TokenName = "CNBLOGS_TOKEN";
    private const string PostsApi = "https://i.cnblogs.com/openapi/v1/posts";
    private const string ReviewApi = "https://i.cnblogs.com/openapi/v1/posts/reviewStatus:check";
    private const int MaxTitle = 200;
    private const int MaxBody = 500_000;
    private static readonly SemaphoreSlim DraftGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // Test-only transport substitutes the fixed API endpoint; never returned to untrusted callers.
    internal static HttpMessageHandler? VerificationHandler { get; set; }

    private static string DraftFile => HostAssets.ResolveDataFilePath("cnblogs-drafts.dat");

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "cnblogs.status",
            Description = "查询博客园 PAT 是否已配置，不返回密钥",
            Permissions = ["blog.cnblogs.read"], Category = "blog",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = _ => Task.FromResult<object?>(new
            {
                configured = !string.IsNullOrWhiteSpace(ReadToken()),
                provider = "cnblogs", format = "Markdown",
                draftStorage = "local-dpapi", cloudDraftSupported = false
            })
        };
        yield return new()
        {
            Name = "cnblogs.review",
            Description = "通过博客园官方 API 查询已有文章审核状态",
            Permissions = ["blog.cnblogs.read"], Category = "blog",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"postId":{"type":"integer","minimum":1}},"required":["postId"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = async payload => await ReviewAsync(((JsonElement)payload!).GetProperty("postId").GetInt64())
        };
        yield return new()
        {
            Name = "cnblogs.draft.save",
            Description = "将 Markdown 草稿加密保存到本机；不会向博客园公开发布",
            Permissions = ["blog.cnblogs.draft"], Category = "blog", RiskLevel = "low",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"id":{"type":"string"},"title":{"type":"string","minLength":1},"body":{"type":"string","minLength":1},"isAigc":{"type":"boolean"},"tags":{"type":"array","items":{"type":"string"}}},"required":["title","body"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = SaveDraftAsync
        };
        yield return new()
        {
            Name = "cnblogs.draft.list",
            Description = "列出本机加密草稿元数据，不返回草稿正文",
            Permissions = ["blog.cnblogs.draft"], Category = "blog",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = ListDraftsAsync
        };
        yield return new()
        {
            Name = "cnblogs.draft.get",
            Description = "读取指定的本机 Markdown 草稿内容",
            Permissions = ["blog.cnblogs.draft"], Category = "blog",
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"id":{"type":"string","minLength":1}},"required":["id"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = GetDraftAsync
        };
        yield return new()
        {
            Name = "cnblogs.post.publish",
            Description = "使用官方 PAT 发布 Markdown 文章，显式确认并返回文章地址",
            Permissions = ["blog.cnblogs.publish"], Category = "blog", RiskLevel = "high",
            RequiresConfirmation = true,
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"title":{"type":"string","minLength":1},"body":{"type":"string","minLength":1},"isAigc":{"type":"boolean"},"tags":{"type":"array","items":{"type":"string"}},"categories":{"type":"array","items":{"type":"string"}},"confirmPublish":{"type":"boolean"},"requestId":{"type":"string","minLength":8}},"required":["title","body","confirmPublish","requestId"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PublishAsync
        };
        yield return new()
        {
            Name = "cnblogs.draft.publish",
            Description = "读取本地加密草稿并通过博客园官方 API 发表；需要显式确认",
            Permissions = ["blog.cnblogs.draft", "blog.cnblogs.publish"],
            Category = "blog", RiskLevel = "high", RequiresConfirmation = true,
            InputSchema = YanziCapabilitySchema.Parse("""
            {"type":"object","properties":{"id":{"type":"string","minLength":1},"confirmPublish":{"type":"boolean"},"requestId":{"type":"string","minLength":8}},"required":["id","confirmPublish","requestId"],"additionalProperties":false}
            """),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PublishDraftAsync
        };
    }

    private static string ReadToken() =>
        AppEnvironmentVariableStore.GetValue(TokenName)?.Trim() ?? string.Empty;

    private static HttpClient GetClient() => VerificationHandler == null
        ? Client
        : new HttpClient(VerificationHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<JsonElement> SendApiAsync(string route, object content)
    {
        var token = ReadToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("尚未在燕子受保护环境变量中配置博客园 PAT。");
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Authorization-Type", "pat");
        request.Content = JsonContent.Create(content);
        using var verificationClient = VerificationHandler == null ? null : GetClient();
        var http = verificationClient ?? Client;
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("博客园接口发生重定向，已拒绝转发认证凭证。");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"博客园接口请求失败：HTTP {(int)response.StatusCode}。");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = json.RootElement;
        if (!root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            // 不回传远端自由文本，以免意外反射请求或认证信息。
            throw new InvalidOperationException("博客园接口未确认操作成功，请检查令牌权限或博客状态。");
        }
        return root.Clone();
    }

    private static async Task<object?> ReviewAsync(long postId)
    {
        var json = await SendApiAsync(ReviewApi, new { PostId = postId });
        var value = json.GetProperty("value");
        return new
        {
            postId = value.TryGetProperty("postId", out var p) ? p.GetInt64() : postId,
            url = value.TryGetProperty("url", out var u) ? u.GetString() : null,
            reviewStatus = value.TryGetProperty("reviewStatus", out var s) ? s.GetInt32() : -1,
            description = value.TryGetProperty("description", out var d) ? d.GetString() : null
        };
    }

    private static string Required(JsonElement input, string property, int max)
    {
        var value = input.GetProperty(property).GetString()?.Trim() ?? "";
        if (value.Length == 0 || value.Length > max)
            throw new ArgumentException($"{property} 不能为空且不能超过 {max} 字符。");
        return value;
    }

    private static string[] Strings(JsonElement input, string property)
    {
        if (!input.TryGetProperty(property, out var list) || list.ValueKind == JsonValueKind.Null)
            return [];
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > 12)
            throw new ArgumentException($"{property} 最多支持 12 项。");
        return list.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"{property} 只能包含字符串。");
            return item.GetString()?.Trim() ?? "";
        }).Where(item => item.Length > 0 && item.Length <= 80).Distinct().ToArray();
    }

    private static bool Confirmed(JsonElement input) =>
        input.TryGetProperty("confirmPublish", out var confirm) && confirm.ValueKind == JsonValueKind.True;

    private static async Task<object?> PublishAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        if (!Confirmed(input)) throw new ArgumentException("正式发布必须显式设置 confirmPublish=true。");
        var post = new LocalBlogDraft
        {
            Title = Required(input, "title", MaxTitle),
            Body = Required(input, "body", MaxBody),
            IsAigc = input.TryGetProperty("isAigc", out var aigc) && aigc.ValueKind == JsonValueKind.True,
            Tags = Strings(input, "tags"),
            Categories = Strings(input, "categories")
        };
        var requestId = Required(input, "requestId", 128);
        return await CnblogsPublishLedger.RunAsync(requestId,
            CnblogsPublishLedger.Fingerprint(post.Title, post.Body), () => PublishPostAsync(post));
    }

    private static async Task<object?> PublishPostAsync(LocalBlogDraft post)
    {
        if (post.Title.Length is < 1 or > MaxTitle || post.Body.Length is < 1 or > MaxBody)
            throw new ArgumentException("标题或正文长度不符合要求。");
        var json = await SendApiAsync(PostsApi, new
        {
            Title = post.Title, Body = post.Body, PostFormat = "Markdown",
            IsAigc = post.IsAigc, Tags = post.Tags, Categories = post.Categories
        });
        var value = json.GetProperty("value");
        var id = value.GetProperty("postId").GetInt64();
        var url = value.GetProperty("postUrl").GetString() ?? "";
        if (id <= 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("www.cnblogs.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("博客园返回了不符合预期的文章地址。");
        return new { published = true, postId = id, postUrl = url };
    }

    private static async Task<object?> SaveDraftAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var id = input.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String &&
                 !string.IsNullOrWhiteSpace(idElement.GetString())
            ? idElement.GetString()!.Trim()
            : Guid.NewGuid().ToString("N");
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("草稿 id 必须是 GUID。");
        var draft = new LocalBlogDraft
        {
            Id = id, Title = Required(input, "title", MaxTitle),
            Body = Required(input, "body", MaxBody),
            Tags = Strings(input, "tags"),
            IsAigc = input.TryGetProperty("isAigc", out var ai) && ai.ValueKind == JsonValueKind.True,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        await DraftGate.WaitAsync();
        try
        {
            var drafts = LoadDrafts();
            drafts[id] = draft;
            SaveDrafts(drafts);
        }
        finally { DraftGate.Release(); }
        return new { id, saved = true, storage = "local-dpapi", updatedUtc = draft.UpdatedUtc };
    }

    private static async Task<object?> ListDraftsAsync(object? _)
    {
        await DraftGate.WaitAsync();
        try
        {
            return new
            {
                items = LoadDrafts().Values.OrderByDescending(d => d.UpdatedUtc)
                    .Select(d => new { id = d.Id, title = d.Title, updatedUtc = d.UpdatedUtc, isAigc = d.IsAigc })
                    .ToArray()
            };
        }
        finally { DraftGate.Release(); }
    }

    private static async Task<object?> GetDraftAsync(object? payload)
    {
        var id = Required((JsonElement)payload!, "id", 64);
        await DraftGate.WaitAsync();
        try
        {
            return LoadDrafts().TryGetValue(id, out var value) ? new { id = value.Id, title = value.Title, body = value.Body, tags = value.Tags, categories = value.Categories, isAigc = value.IsAigc, updatedUtc = value.UpdatedUtc }
                : throw new KeyNotFoundException("草稿不存在。");
        }
        finally { DraftGate.Release(); }
    }

    private static async Task<object?> PublishDraftAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        if (!Confirmed(input)) throw new ArgumentException("正式发布必须显式设置 confirmPublish=true。");
        var id = Required(input, "id", 64);
        LocalBlogDraft draft;
        await DraftGate.WaitAsync();
        try
        {
            if (!LoadDrafts().TryGetValue(id, out var found))
                throw new KeyNotFoundException("草稿不存在。");
            draft = found;
        }
        finally { DraftGate.Release(); }
        // 发布后保留草稿作为本地备份；不将敏感凭证写入发布流水账。
        var requestId = Required(input, "requestId", 128);
        return await CnblogsPublishLedger.RunAsync(requestId,
            CnblogsPublishLedger.Fingerprint(draft.Title, draft.Body), () => PublishPostAsync(draft));
    }

    private static Dictionary<string, LocalBlogDraft> LoadDrafts()
    {
        if (!File.Exists(DraftFile)) return new(StringComparer.OrdinalIgnoreCase);
        // 损坏数据必须显式失败，不能把现有草稿误判为空并覆盖。
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(DraftFile), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<Dictionary<string, LocalBlogDraft>>(bytes, JsonOptions)
                         ?? throw new InvalidDataException("草稿文件为空。"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void SaveDrafts(Dictionary<string, LocalBlogDraft> drafts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DraftFile)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(drafts, JsonOptions);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temporary = DraftFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, DraftFile, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed class LocalBlogDraft
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public string[] Tags { get; set; } = [];
        public string[] Categories { get; set; } = [];
        public bool IsAigc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
