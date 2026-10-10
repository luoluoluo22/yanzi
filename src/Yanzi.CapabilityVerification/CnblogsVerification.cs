using System.Net;
using System.Text;
using System.Text.Json;
using OpenQuickHost;

internal static class CnblogsVerification
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int PublishCount;
        public int ReviewCount;
        public string? SeenAuthType;
        public bool HasBearer;
        public string? SeenPostFormat;
        public bool? SeenAigc;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HasBearer = request.Headers.Authorization?.Scheme == "Bearer" &&
                !string.IsNullOrWhiteSpace(request.Headers.Authorization.Parameter);
            SeenAuthType = request.Headers.TryGetValues("Authorization-Type", out var values)
                ? values.FirstOrDefault() : null;
            if (request.RequestUri!.AbsolutePath.EndsWith("reviewStatus:check", StringComparison.Ordinal))
            {
                ReviewCount++;
                return Response("""{"success":true,"value":{"postId":12345,"url":"https://www.cnblogs.com/demo/p/12345","reviewStatus":1,"description":"审核通过"}}""");
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/posts", StringComparison.Ordinal))
            {
                PublishCount++;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                SeenPostFormat = json.RootElement.GetProperty("postFormat").GetString();
                SeenAigc = json.RootElement.GetProperty("isAigc").GetBoolean();
                return Response("""{"success":true,"value":{"postId":12345,"postUrl":"https://www.cnblogs.com/demo/p/12345"}}""");
            }
            throw new Exception("Unexpected endpoint");
        }
        private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    public static void ImportLocalToken()
    {
        // Explicit one-time maintenance operation: source path only under the working repository.
        var source = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (!File.Exists(source)) throw new FileNotFoundException("本地 .env 文件不存在。");
        var line = File.ReadLines(source).LastOrDefault(s =>
            s.TrimStart().StartsWith("CNBLOGS_TOKEN=", StringComparison.Ordinal));
        if (line is null) throw new InvalidOperationException("未发现 CNBLOGS_TOKEN 配置项。");
        var token = line[(line.IndexOf('=') + 1)..].Trim().Trim('"', '\'');
        if (token.Length < 12) throw new InvalidOperationException("博客园 PAT 为空或格式不符。");
        var before = AppEnvironmentVariableStore.Load().ToList();
        var variable = before.FirstOrDefault(x => x.Name.Equals("CNBLOGS_TOKEN", StringComparison.OrdinalIgnoreCase));
        if (variable == null)
            before.Add(new AppEnvironmentVariableSettings
            {
                Name = "CNBLOGS_TOKEN", Value = token,
                Description = "博客园 PAT；仅供 cnblogs.* 宿主能力使用，禁止注入普通小程序"
            });
        else variable.Value = token;
        AppEnvironmentVariableStore.Save(before);
        if (AppEnvironmentVariableStore.GetValue("CNBLOGS_TOKEN") != token)
            throw new InvalidOperationException("凭证加密持久化回读校验失败。");
        if (AppEnvironmentVariableStore.GetEnvironmentNames().Contains("CNBLOGS_TOKEN"))
            throw new InvalidOperationException("密钥仍暴露在通用脚本环境名列表中。");
        Console.WriteLine("CNBLOGS_PAT_IMPORTED_PROTECTED=true;SCOPED_ENV_EXCLUDED=true;VALUE_NOT_LOGGED=true");
    }

    public static async Task VerifyLiveReadAsync(long postId)
    {
        YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziCnblogsCapabilityProvider.Create());
        var caller = new YanziCapabilityCaller("cnblogs-verification", ["blog.cnblogs.read"]);
        var result = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.review", new { postId }, caller);
        if (!result.Success) throw new Exception("LIVE_READ_FAILED:" + result.ErrorCode);
        var value = JsonSerializer.SerializeToElement(result.Data);
        if (value.GetProperty("postId").GetInt64() != postId)
            throw new Exception("BLOG_POST_MISMATCH");
        Console.WriteLine("CNBLOGS_LIVE_READ_OK=true;POST_ID_MATCH=true;SECRET_NOT_PRINTED=true");
    }

    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        var root = Path.Combine(Path.GetTempPath(), "cnblogs-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var scope = HostAssets.UseIsolatedDataRootForVerification(root);
            AppEnvironmentVariableStore.Save([new AppEnvironmentVariableSettings
            {
                Name = "CNBLOGS_TOKEN", Value = "mock-local-pat-only-not-a-real-key",
                Description = "scoped fixture"
            }]);
            Check(AppEnvironmentVariableStore.GetValue("CNBLOGS_TOKEN") == "mock-local-pat-only-not-a-real-key", "protected_round_trip");
            var env = new Dictionary<string, string>();
            AppEnvironmentVariableStore.ApplyToEnvironment((k, v) => env[k] = v ?? "");
            Check(!env.ContainsKey("CNBLOGS_TOKEN") && !AppEnvironmentVariableStore.GetEnvironmentNames().Contains("CNBLOGS_TOKEN"),
                "never_inject_service_key_into_extension_environment");
            var fileBytes = File.ReadAllBytes(Path.Combine(root, "environment-variables.dat"));
            Check(!Encoding.UTF8.GetString(fileBytes).Contains("mock-local-pat-only-not-a-real-key"), "at_rest_protected");
            YanziCapabilityProviderSdk.RegisterProvider("yanzi-host", YanziCnblogsCapabilityProvider.Create());

            var anonymous = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.review", new { postId = 12345 });
            Check(!anonymous.Success && anonymous.ErrorCode == "permission_denied", "unauthorized_read_denied");
            var draftCaller = new YanziCapabilityCaller("draft-only", ["blog.cnblogs.draft"]);
            var denied = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.post.publish", new
            {
                title = "test", body = "test", confirmPublish = true
            }, draftCaller);
            Check(!denied.Success && denied.ErrorCode == "permission_denied", "draft_permission_does_not_grant_publish");

            var saved = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.draft.save", new
            {
                title = "Test Markdown", body = "# Fixture", isAigc = true, tags = new[] { "testing" }
            }, draftCaller);
            Check(saved.Success, "save_draft");
            var id = JsonSerializer.SerializeToElement(saved.Data).GetProperty("id").GetString()!;
            var listed = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.draft.list", new { }, draftCaller);
            Check(listed.Success && JsonSerializer.SerializeToElement(listed.Data).GetProperty("items").GetArrayLength() == 1, "list_draft");
            var read = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.draft.get", new { id }, draftCaller);
            Check(read.Success && JsonSerializer.SerializeToElement(read.Data).GetProperty("body").GetString() == "# Fixture", "get_draft");
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "cnblogs-drafts.dat"))).Contains("# Fixture"), "draft_at_rest_encrypted");

            var handler = new FakeHandler();
            YanziCnblogsCapabilityProvider.VerificationHandler = handler;
            try
            {
                var publisher = new YanziCapabilityCaller("publisher", ["blog.cnblogs.read", "blog.cnblogs.publish", "blog.cnblogs.draft"]);
                var noConfirm = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.post.publish", new
                {
                    title = "No", body = "No", confirmPublish = false, requestId = "cnblogs-no-confirm-1"
                }, publisher);
                Check(!noConfirm.Success && handler.PublishCount == 0, "explicit_confirmation_required");
                var posted = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.post.publish", new
                {
                    title = "Test", body = "# Heading", confirmPublish = true, isAigc = true, requestId = "cnblogs-publish-1"
                }, publisher);
                Check(posted.Success && handler.PublishCount == 1 && handler.SeenPostFormat == "Markdown" &&
                      handler.SeenAigc == true && handler.HasBearer && handler.SeenAuthType == "pat", "official_post_contract");
                var duplicate = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.post.publish", new
                {
                    title = "Test", body = "# Heading", confirmPublish = true, isAigc = true, requestId = "cnblogs-publish-1"
                }, publisher);
                Check(duplicate.Success && handler.PublishCount == 1, "publish_idempotent_after_success");
                var changed = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.post.publish", new
                {
                    title = "Changed", body = "# Heading", confirmPublish = true, requestId = "cnblogs-publish-1"
                }, publisher);
                Check(!changed.Success && handler.PublishCount == 1, "same_request_id_different_content_rejected");
                var review = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.review", new { postId = 12345 }, publisher);
                Check(review.Success && handler.ReviewCount == 1, "review_contract");
                var draftPublished = await YanziCapabilityInvocationService.InvokeAsync("cnblogs.draft.publish", new
                {
                    id, confirmPublish = true, requestId = "cnblogs-publish-draft-1"
                }, publisher);
                Check(draftPublished.Success && handler.PublishCount == 2, "local_draft_publish_contract");
            }
            finally { YanziCnblogsCapabilityProvider.VerificationHandler = null; }
            Check(!YanziCapabilityCallLog.List().Any(c => c.ErrorCode?.Contains("mock-local-pat", StringComparison.Ordinal) == true),
                "audit_never_contains_secret");
            Console.WriteLine($"CNBLOGS_CAPABILITIES_PERMISSIONS_DPAPI_DRAFTS_OFFICIAL_POST_REVIEW=PASSED; checks={checks}");
        }
        finally { Directory.Delete(root, true); }
    }
}
