using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WpfApplication = System.Windows.Application;

namespace OpenQuickHost;

/// <summary>
/// 蓝奏云网页版适配器：第一阶段复用已经登录的 Edge 浏览器助手。
/// 不读取、复制、持久化或回传浏览器 Cookie；网络协议实验独立开关。
/// </summary>
public static partial class YanziLanzouCapabilityProvider
{
    internal const string DiskUrl = "https://pc.woozooo.com/mydisk.php";
    internal const string HomeUrl = "https://www.lanzou.com/";
    internal const string LoginUrl = "https://www.lanzou.com/account.php?action=login";
    private static readonly JsonElement ObjectSchema = YanziCapabilitySchema.Parse("""{"type":"object"}""");
    private static readonly string[] AllowedHosts =
    [
        "pc.woozooo.com", "up.woozooo.com", "accounts.woozooo.com", "www.lanzou.com",
        "lanzou.com", "www.lanzoui.com", "lanzoui.com",
        "www.lanzouo.com", "lanzouo.com", "www.lanzouw.com",
        "lanzouw.com", "www.lanzoux.com", "lanzoux.com"
    ];
    private static readonly SemaphoreSlim BrowserGate = new(1, 1);

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return Define("lanzou.status", "查询蓝奏云浏览器能力和分阶段支持状态（不判定账户已登录）",
            """{"type":"object","additionalProperties":false}""",
            ["browser.read"], _ => Task.FromResult<object?>(new
            {
                provider = "lanzou", category = "cloud-drive", stage = "browser",
                browserConnected = GetServer()?.IsBrowserConnected == true,
                authenticated = (bool?)null, // 必须通过真实网页观察判断，不能把浏览器在线当成已登录
                browserOperations = new[] { "open", "inspect", "readWorkflow", "confirmedWriteWorkflow", "files.list", "folders.list", "files.upload", "files.move", "files.share", "files.rename", "files.delete", "files.download" },
                fileUpload = "confirmed-browser-multipart-small-files-max-2MiB",
                networkObserver = "opt-in-metadata-only",
                apiReplay = "selected-read-write-operations-verified;rename-membership-gated;download-not-end-to-end"
            }));

        yield return Define("lanzou.open", "用当前系统浏览器打开蓝奏云入口或登录页面",
            """{"type":"object","properties":{"page":{"type":"string","enum":["disk","home","login"]}},"additionalProperties":false}""",
            ["application.run"], payload =>
            {
                var page = OptionalString((JsonElement)payload!, "page") ?? "disk";
                var url = ResolvePage(page);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return Task.FromResult<object?>(new { opened = true, url, loginIsUserManaged = true });
            });

        yield return Define("lanzou.inspect", "在已连接的 Edge 中读取蓝奏云当前页面的可见结构；不读取表单值和凭证",
            """{"type":"object","properties":{"page":{"type":"string","enum":["disk","home","login"]}},"additionalProperties":false}""",
            ["browser.read", "network.read"], async payload =>
            {
                var page = OptionalString((JsonElement)payload!, "page") ?? "disk";
                return await RunBrowserAsync(new
                {
                    action = "scrape", url = ResolvePage(page), site = "lanzou",
                    closeOnComplete = true,
                    selectors = new Dictionary<string, string>
                    {
                        ["title"] = "title",
                        ["visibleText"] = "body",
                        ["links"] = "a | href",
                        ["buttons"] = "button, [role='button'], input[type='button'] | innerText",
                        ["frames"] = "iframe | src",
                        ["fileRows"] = "[data-file-id], [data-id], .f_tb, .f_tps, .filelist | innerText"
                    }
                }, 30);
            });

        yield return Define("lanzou.browser.read", "只读浏览器工作流：等待、滚动、提取网页内容；不执行脚本或网络重放",
            """{"type":"object","properties":{"page":{"type":"string","enum":["disk","home","login"]},"steps":{"type":"array","items":{"type":"object"}}},"required":["steps"],"additionalProperties":false}""",
            ["browser.read", "network.read"], payload => WorkflowAsync((JsonElement)payload!, false));

        yield return Define("lanzou.browser.act", "在蓝奏云网页执行确认过的填写和点击工作流（创建/重命名/分享/删除等需先确认）",
            """{"type":"object","properties":{"page":{"type":"string","enum":["disk","home","login"]},"steps":{"type":"array","items":{"type":"object"}},"confirmAction":{"type":"boolean"}},"required":["steps","confirmAction"],"additionalProperties":false}""",
            ["browser.read", "browser.write", "network.read"], payload => WorkflowAsync((JsonElement)payload!, true),
            risk: "high", confirmation: true);

        yield return Define("lanzou.folders.list", "通过已登录的 Edge 会话调用已验收的蓝奏云只读目录列表接口（task=47）",
            """{"type":"object","properties":{"folderId":{"type":"string"},"page":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}""",
            ["browser.read", "network.read"], payload => ListEntriesAsync((JsonElement)payload!, files: false));

        yield return Define("lanzou.files.list", "使用已登录 Edge 会话调用已验收的蓝奏云只读文件列表接口（task=5）",
            """{"type":"object","properties":{"folderId":{"type":"string"},"page":{"type":"integer","minimum":1,"maximum":100}},"additionalProperties":false}""",
            ["browser.read", "network.read"], payload => ListEntriesAsync((JsonElement)payload!, files: true));

        yield return Define("lanzou.files.upload", "将本机小文件通过 Edge 会话上传至蓝奏云，确认后自动移入指定目录并核对文件 ID",
            """{"type":"object","properties":{"localPath":{"type":"string","minLength":1},"folderId":{"type":"string"},"confirmAction":{"type":"boolean"}},"required":["localPath","folderId","confirmAction"],"additionalProperties":false}""",
            ["browser.write", "network.write", "file.read"], payload => UploadFileAsync((JsonElement)payload!),
            risk:"high", confirmation:true);

        yield return Define("lanzou.files.move", "移动指定文件并从目的目录核对文件 ID",
            """{"type":"object","properties":{"fileId":{"type":"string"},"sourceFolderId":{"type":"string"},"targetFolderId":{"type":"string"},"confirmAction":{"type":"boolean"}},"required":["fileId","sourceFolderId","targetFolderId","confirmAction"],"additionalProperties":false}""",
            ["browser.write", "network.write"], payload => MoveFileAsync((JsonElement)payload!),
            risk:"high", confirmation:true);

        yield return Define("lanzou.files.share", "通过已登录会话读取文件分享链接（仅回传链接和密码状态，不返回访问密码）",
            """{"type":"object","properties":{"fileId":{"type":"string"},"includeAccessCode":{"type":"boolean"},"confirmAction":{"type":"boolean"}},"required":["fileId","confirmAction"],"additionalProperties":false}""",
            ["browser.read", "network.read"], payload => ShareFileAsync((JsonElement)payload!),
            risk:"high", confirmation:true);

        yield return Define("lanzou.files.rename", "重命名蓝奏云文件；免费用户若受会员限制则返回服务端明确错误",
            """{"type":"object","properties":{"fileId":{"type":"string"},"folderId":{"type":"string"},"newBaseName":{"type":"string"},"confirmAction":{"type":"boolean"}},"required":["fileId","folderId","newBaseName","confirmAction"],"additionalProperties":false}""",
            ["browser.write", "network.write"], payload => RenameFileAsync((JsonElement)payload!),
            risk:"high", confirmation:true);

        yield return Define("lanzou.files.delete", "将指定文件移入回收站；需目录信息、确认授权和删除后校验",
            """{"type":"object","properties":{"fileId":{"type":"string"},"folderId":{"type":"string"},"expectedName":{"type":"string"},"confirmAction":{"type":"boolean"}},"required":["fileId","folderId","expectedName","confirmAction"],"additionalProperties":false}""",
            ["browser.write", "network.write"], payload => DeleteFileAsync((JsonElement)payload!),
            risk:"high", confirmation:true);

        yield return Define("lanzou.files.download", "通过官方分享下载接口在 Edge 下载并验证落地文件、可选 SHA-256",
            """{"type":"object","properties":{"fileId":{"type":"string"},"expectedName":{"type":"string"},"expectedSha256":{"type":"string"},"confirmAction":{"type":"boolean"}},"required":["fileId","expectedName","confirmAction"],"additionalProperties":false}""",
            ["browser.read", "application.run", "files.read", "network.read"], payload => DownloadFileAsync((JsonElement)payload!),
            risk: "medium", confirmation: true);

        yield return Define("lanzou.network.observe", "观察蓝奏云请求元数据（方法、路径、状态和非敏感字段名），不采集凭证或正文",
            """{"type":"object","properties":{"operation":{"type":"string","enum":["start","stop","status","snapshot","clear"]}},"required":["operation"],"additionalProperties":false}""",
            ["browser.read", "network.read"], payload => ObserveAsync((JsonElement)payload!),
            risk: "medium");
    }

    private static YanziCapabilityProviderDefinition Define(string name, string description, string schema,
        string[] permissions, Func<object?, Task<object?>> handler,
        string risk = "low", bool confirmation = false) => new()
        {
            Name = name, Description = description, Category = "cloud-drive", Version = "0.2.0",
            Permissions = permissions, RiskLevel = risk, RequiresConfirmation = confirmation,
            InputSchema = YanziCapabilitySchema.Parse(schema), OutputSchema = ObjectSchema, Handler = handler
        };

    private static string ResolvePage(string page) => page switch
    {
        "disk" => DiskUrl,
        "home" => HomeUrl,
        "login" => LoginUrl,
        _ => throw new ArgumentException("仅允许蓝奏云的三个固定入口。")
    };

    private static string? OptionalString(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<object?> WorkflowAsync(JsonElement input, bool mutating)
    {
        if (mutating && (!input.TryGetProperty("confirmAction", out var confirmation)
                         || confirmation.ValueKind != JsonValueKind.True))
            throw new ArgumentException("修改云端文件前必须传 confirmAction=true。");

        var steps = input.GetProperty("steps");
        if (steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() is < 1 or > 20)
            throw new ArgumentException("工作流必须包含 1–20 步。");
        foreach (var step in steps.EnumerateArray())
            ValidateStep(step, mutating);
        var page = OptionalString(input, "page") ?? "disk";
        return await RunBrowserAsync(new
        {
            action = "workflow", site = "lanzou", url = ResolvePage(page),
            steps = steps.Clone(), closeOnComplete = true
        }, 90);
    }

    internal static void ValidateStep(JsonElement step, bool mutating)
    {
        if (step.ValueKind != JsonValueKind.Object ||
            !step.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String)
            throw new ArgumentException("工作流步骤缺少 type。");
        var type = kind.GetString();
        var allowed = mutating
            ? new[] { "wait", "scroll", "scrape", "click", "fill" }
            : new[] { "wait", "scroll", "scrape" };
        if (!allowed.Contains(type, StringComparer.Ordinal))
            throw new ArgumentException("拒绝未授权的浏览器步骤（尤其是 fetch/任意 JavaScript）。");
        if (type is "click" or "fill")
        {
            var selector = step.GetProperty("selector").GetString();
            if (string.IsNullOrWhiteSpace(selector) || selector.Length > 1000)
                throw new ArgumentException("CSS 选择器无效。");
            if (type == "fill" && (!step.TryGetProperty("value", out var value) ||
                value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 4000))
                throw new ArgumentException("输入内容长度超过 4000 字符。");
        }
        if (type == "scrape")
        {
            if (!step.TryGetProperty("selectors", out var selectors) || selectors.ValueKind != JsonValueKind.Object ||
                !selectors.EnumerateObject().Any() || selectors.EnumerateObject().Count() > 30)
                throw new ArgumentException("scrape 必须指定最多 30 个选择器。");
            foreach (var property in selectors.EnumerateObject())
            {
                var raw = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (string.IsNullOrWhiteSpace(raw) || raw.Length > 1000)
                    throw new ArgumentException("选择器必须是字符串。");
                var parts = raw.Split('|', 2);
                if (parts.Length == 2 && !new[] { "innerText", "textContent", "href", "title", "aria-label", "src" }
                    .Contains(parts[1].Trim(), StringComparer.Ordinal))
                    throw new ArgumentException("禁止抓取 HTML、密码、Cookie 或任意属性。");
            }
        }
    }

    private static async Task<object?> ListEntriesAsync(JsonElement input, bool files)
    {
        var folderId = OptionalString(input, "folderId") ?? "-1";
        if (folderId.Length is < 1 or > 16 ||
            (folderId != "-1" && folderId.Any(c => c is < '0' or > '9')))
            throw new ArgumentException("目录编号必须为 -1（根目录）或最长 16 位数字。");
        var page = 1;
        if (input.TryGetProperty("page", out var pageValue))
        {
            if (pageValue.ValueKind != JsonValueKind.Number || !pageValue.TryGetInt32(out page))
                throw new ArgumentException("页码必须为整数。");
        }
        if (page is < 1 or > 100)
            throw new ArgumentException("页码必须在 1–100 之间。");

        // task=47 为文件夹列表，task=5 为文件列表；均从真实账号的只读页面抓包并复验。
        // Cookie 仅由浏览器自动携带；绝不从 Edge 中读取、序列化或持久化。
        var response = await RunBrowserAsync(new
        {
            action = "workflow", site = "lanzou", url = DiskUrl, closeOnComplete = true,
            steps = new object[] { new
            {
                type = "fetch", method = "POST", url = "/doupload.php", key = "folderListing",
                headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8"
                },
                body = $"task={(files ? 5 : 47)}&folder_id={folderId}&pg={page}"
            } }
        }, 35);
        var result = JsonSerializer.SerializeToElement(response);
        var serializedFetch = result.GetProperty("data").GetProperty("folderListing")[0].GetString();
        if (string.IsNullOrEmpty(serializedFetch))
            throw new InvalidOperationException("蓝奏云文件夹接口没有返回数据。");
        return files ? ParseFileListResponse(serializedFetch, folderId, page)
            : ParseFolderListResponse(serializedFetch, folderId, page);
    }

    internal static object ParseFolderListResponse(string serializedFetch, string folderId, int page)
    {
        using var envelope = JsonDocument.Parse(serializedFetch);
        var payload = envelope.RootElement;
        if (payload.GetProperty("status").GetInt32() != 200)
            throw new InvalidOperationException("蓝奏云目录请求 HTTP 状态异常。");
        var body = payload.GetProperty("data");
        if (!body.TryGetProperty("zt", out var status) || status.ValueKind != JsonValueKind.Number ||
            status.GetInt32() != 1)
            throw new InvalidOperationException("蓝奏云没有返回已授权的目录列表，请检查登录会话。");
        if (!body.TryGetProperty("text", out var foldersJson) || foldersJson.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("蓝奏云文件夹响应结构发生变化。");
        static string Field(JsonElement item, string key)
        {
            if (!item.TryGetProperty(key, out var value)) return string.Empty;
            return value.ValueKind switch
            {
                JsonValueKind.String => (value.GetString() ?? string.Empty)[..Math.Min(value.GetString()!.Length, 256)],
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            };
        }
        var folders = foldersJson.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new { name = Field(item, "name"), folderId = Field(item, "fol_id") })
            .ToArray();
        return new { authenticated = true, operation = "folders.list", folderId, page,
            folderCount = folders.Length, folders, verifiedProtocol = "task=47" };
    }

    internal static object ParseFileListResponse(string serializedFetch, string folderId, int page)
    {
        using var envelope = JsonDocument.Parse(serializedFetch);
        var payload = envelope.RootElement;
        if (payload.GetProperty("status").GetInt32() != 200)
            throw new InvalidOperationException("蓝奏云文件列表 HTTP 状态异常。");
        var body = payload.GetProperty("data");
        if (!body.TryGetProperty("zt", out var status) || status.ValueKind != JsonValueKind.Number ||
            status.GetInt32() != 1)
            throw new InvalidOperationException("蓝奏云未授权当前文件列表读取，请检查会话。");
        if (!body.TryGetProperty("text", out var filesJson) || filesJson.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("蓝奏云文件列表响应结构发生变化。");
        static string Field(JsonElement item, string key)
        {
            if (!item.TryGetProperty(key, out var value)) return string.Empty;
            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            };
            return text[..Math.Min(text.Length, 256)];
        }
        // 只输出文件所需的最小元数据；服务器可能附带密码和权限字段，不能透传。
        var files = filesJson.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new
            {
                name = Field(item, "name_all"), fileId = Field(item, "id"),
                size = Field(item, "size"), modifiedAt = Field(item, "time"),
                downloads = Field(item, "downs")
            }).ToArray();
        return new { authenticated = true, operation = "files.list", folderId, page,
            fileCount = files.Length, files, verifiedProtocol = "task=5" };
    }


    private static void ConfirmFileChange(JsonElement input)
    {
        if (!input.TryGetProperty("confirmAction", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new ArgumentException("需要明确确认云端文件操作（confirmAction=true）。");
    }

    private static string Required(JsonElement input, string key)
    {
        if (!input.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException("缺少必填字段：" + key);
        return value.GetString()!;
    }

    private static string FileId(JsonElement input, string key = "fileId")
    {
        var id = Required(input, key);
        if (id.Length > 18 || id.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("文件 ID 必须是最多 18 位数字。");
        return id;
    }

    private static string FolderId(JsonElement input, string key = "folderId")
    {
        var folder = Required(input, key);
        if (folder == "-1" || folder.Length is >= 1 and <= 16 &&
            folder.All(c => c is >= '0' and <= '9'))
            return folder;
        throw new ArgumentException("folderId 必须为 -1 或不超过 16 位的数字。");
    }

    private static async Task<JsonElement> PostLanzouTaskAsync(int taskCode, Dictionary<string,string> values)
    {
        var parts = new List<string> { "task=" + taskCode };
        foreach (var pair in values)
            parts.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
        var response = await RunBrowserAsync(new
        {
            action = "workflow", site = "lanzou", url = DiskUrl, closeOnComplete = true,
            steps = new object[] { new
            {
                type = "fetch", method = "POST", url = "/doupload.php", key = "apiResult",
                headers = new Dictionary<string,string> {
                    ["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8"
                },
                body = string.Join("&", parts)
            } }
        }, 40);
        var browserResult = JsonSerializer.SerializeToElement(response);
        var data = browserResult.GetProperty("data");
        var responseString = data.GetProperty("apiResult")[0].GetString();
        if (string.IsNullOrWhiteSpace(responseString))
            throw new InvalidOperationException("蓝奏云业务接口没有返回可解析的响应。");
        using var parsed = JsonDocument.Parse(responseString);
        var envelope = parsed.RootElement;
        if (!envelope.TryGetProperty("status", out var httpStatus) || httpStatus.GetInt32() != 200)
            throw new InvalidOperationException("蓝奏云业务请求 HTTP 状态异常。");
        var payload = envelope.GetProperty("data");
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("蓝奏云业务响应结构异常。");
        if (!payload.TryGetProperty("zt", out var zt) || zt.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) ||
            zt.ToString() != "1")
        {
            string detail = "服务端拒绝该操作";
            if (payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.String)
                detail = (info.GetString() ?? detail)[..Math.Min(160,(info.GetString() ?? detail).Length)];
            throw new InvalidOperationException("蓝奏云操作未通过业务验收：" + detail);
        }
        return payload.Clone();
    }

    private static async Task<bool> ContainsFileAsync(string fileId, string folderId, string? expectedName = null)
    {
        var payload = await PostLanzouTaskAsync(5, new() { ["folder_id"] = folderId, ["pg"] = "1" });
        if (!payload.TryGetProperty("text", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("无法验证云端目录内容。");
        return files.EnumerateArray().Any(file =>
            file.ValueKind == JsonValueKind.Object &&
            file.TryGetProperty("id", out var id) && id.ToString() == fileId &&
            (expectedName == null || (file.TryGetProperty("name_all", out var name) && name.GetString() == expectedName)));
    }

    private static async Task<object?> MoveFileAsync(JsonElement input)
    {
        ConfirmFileChange(input);
        var fileId = FileId(input);
        var source = FolderId(input, "sourceFolderId");
        var target = FolderId(input, "targetFolderId");
        if (source == target)
            throw new ArgumentException("原目录与目标目录不能相同。");
        if (!await ContainsFileAsync(fileId, source))
            throw new InvalidOperationException("原目录没有发现指定文件，已拒绝移动。");
        await PostLanzouTaskAsync(20, new() { ["file_id"] = fileId, ["folder_id"] = target });
        if (!await ContainsFileAsync(fileId, target) || await ContainsFileAsync(fileId, source))
            throw new InvalidOperationException("移动接口已响应，但前后目录校验未通过。");
        return new { moved = true, fileId, sourceFolderId = source, targetFolderId = target };
    }

    private static async Task<object?> ShareFileAsync(JsonElement input)
    {
        ConfirmFileChange(input);
        var fileId = FileId(input);
        var response = await PostLanzouTaskAsync(22, new() { ["file_id"] = fileId });
        var info = response.GetProperty("info");
        var baseUri = info.GetProperty("is_newd").GetString();
        var slug = info.GetProperty("f_id").GetString();
        if (string.IsNullOrWhiteSpace(baseUri) || string.IsNullOrWhiteSpace(slug) ||
            !Uri.TryCreate(baseUri + "/" + slug, UriKind.Absolute, out var link) ||
            link.Scheme != Uri.UriSchemeHttps || !link.Host.EndsWith(".lanzout.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("蓝奏云返回了不符合已验证域名规范的分享链接。");
        var protectedShare = info.TryGetProperty("onof", out var locked) && locked.ToString() == "1";
        var revealCode = input.TryGetProperty("includeAccessCode", out var requestCode) &&
            requestCode.ValueKind == JsonValueKind.True;
        var accessCode = revealCode && info.TryGetProperty("pwd", out var access) &&
            access.ValueKind == JsonValueKind.String ? access.GetString() : null;
        return new { shared = true, fileId, url = link.AbsoluteUri,
            passwordProtected = protectedShare, passwordReturned = accessCode != null,
            accessCode = protectedShare ? accessCode : null };
    }

    private static async Task<object?> RenameFileAsync(JsonElement input)
    {
        ConfirmFileChange(input);
        var fileId = FileId(input);
        var folderId = FolderId(input);
        var name = Required(input, "newBaseName");
        if (name.Length > 100 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 ||
            name.Any(char.IsControl) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请传不带扩展名的合法主文件名。");
        if (!await ContainsFileAsync(fileId, folderId))
            throw new InvalidOperationException("目录中未发现待重命名文件。");
        await PostLanzouTaskAsync(46, new() { ["file_id"] = fileId, ["file_name"] = name, ["type"] = "2" });
        if (!await ContainsFileAsync(fileId, folderId))
            throw new InvalidOperationException("改名后无法确认原文件 ID 仍在目录中。");
        return new { renamed = true, fileId, newBaseName = name, folderId };
    }

    private static async Task<object?> DeleteFileAsync(JsonElement input)
    {
        ConfirmFileChange(input);
        var fileId = FileId(input);
        var folderId = FolderId(input);
        var expectedName = Required(input, "expectedName");
        if (expectedName.Length > 200 || !await ContainsFileAsync(fileId, folderId, expectedName))
            throw new InvalidOperationException("删除前文件 ID、名称或目录不匹配；已停止。");
        await PostLanzouTaskAsync(6, new() { ["file_id"] = fileId });
        if (await ContainsFileAsync(fileId, folderId))
            throw new InvalidOperationException("服务端返回成功，但文件仍在原目录，删除未通过校验。");
        return new { deletedFromFolder = true, fileId, folderId, permanentDeletion = false };
    }

    private static async Task<HashSet<string>> FileIdsByNameAsync(string folderId, string fileName)
    {
        var body = await PostLanzouTaskAsync(5, new() { ["folder_id"] = folderId, ["pg"] = "1" });
        if (!body.TryGetProperty("text", out var files) || files.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("无法获取目录以检查是否存在重复上传。");
        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in files.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var id) ||
                !item.TryGetProperty("name_all", out var name) ||
                name.ValueKind != JsonValueKind.String || name.GetString() != fileName)
                continue;
            var value = id.ToString();
            if (value.Length is >= 1 and <= 18 && value.All(char.IsDigit))
                matches.Add(value);
        }
        return matches;
    }

    private static async Task<object?> UploadFileAsync(JsonElement input)
    {
        ConfirmFileChange(input);
        var localPath = Required(input, "localPath");
        var folderId = FolderId(input);
        if (!Path.IsPathFullyQualified(localPath) || !File.Exists(localPath))
            throw new ArgumentException("上传路径必须是存在的本机绝对文件路径。");
        var file = new FileInfo(localPath);
        if (file.Length is < 1 or > 2097152)
            throw new ArgumentException("当前浏览器传输桥仅支持 1 字节至 2 MiB 的小文件。");
        var name = file.Name;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".rar", ".7z", ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg" };
        if (!extensions.Contains(file.Extension) || name.Length > 120 ||
            name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            throw new ArgumentException("不支持上传此文件格式或文件名。");
        // 超时不代表未上传；先保存本次提交之前存在的同名文件 ID 集合。
        var existingRootIds = await FileIdsByNameAsync("-1", name);
        var existingTargetIds = folderId == "-1"
            ? existingRootIds : await FileIdsByNameAsync(folderId, name);
        var contents = await File.ReadAllBytesAsync(file.FullName);
        string? id = null;
        bool recoveredAfterTimeout = false;
        try
        {
            var result = await RunBrowserAsync(new {
                action = "lanzou_upload_file", site = "lanzou", url = DiskUrl,
                filename = name, folderId, base64 = Convert.ToBase64String(contents),
                confirmAction = true, closeOnComplete = true
            }, 75);
            var data = JsonSerializer.SerializeToElement(result).GetProperty("data");
            if (data.GetProperty("httpStatus").GetInt32() != 200 ||
                data.GetProperty("resultCode").ToString() != "1")
                throw new InvalidOperationException("云端文件上传未获得业务成功响应。");
            var items = data.GetProperty("items");
            if (items.GetArrayLength() != 1)
                throw new InvalidOperationException("上传响应没有返回唯一文件 ID；请检查根目录以免重复提交。");
            id = items[0].GetProperty("id").GetString();
        }
        catch (Exception ex) when (ex is TimeoutException ||
             (ex is InvalidOperationException && (ex.Message.Contains("超时", StringComparison.OrdinalIgnoreCase) ||
                                                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))))
        {
            // 不自动重试；真实账号实测：HTTP 层已超时但文件仍成功保存在根目录。
            await Task.Delay(1200);
            var appeared = await FileIdsByNameAsync("-1", name);
            appeared.ExceptWith(existingRootIds);
            if (folderId != "-1")
            {
                var targetAppeared = await FileIdsByNameAsync(folderId, name);
                targetAppeared.ExceptWith(existingTargetIds);
                appeared.UnionWith(targetAppeared);
            }
            if (appeared.Count != 1)
                throw new InvalidOperationException(
                    "上传通信超时，无法唯一确定是否已创建文件。请先核对网盘目录，禁止直接重试。", ex);
            id = appeared.Single();
            recoveredAfterTimeout = true;
        }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 18 ||
            id.Any(c => c is < '0' or > '9'))
            throw new InvalidOperationException("上传响应中的文件编号不可信。");
        // 蓝奏云 HTML5 上传端点忽略 folder_id；真实测试证明首先保存至根目录。
        // 必须依次完成位置核对、移动和再次核对，不能假定上传时已写入指定目录。
        var alreadyAtTarget = await ContainsFileAsync(id, folderId);
        if (!alreadyAtTarget && folderId != "-1")
        {
            if (!await ContainsFileAsync(id, "-1"))
                throw new InvalidOperationException("文件已上传，但在根目录未能定位 ID " + id + "，暂不自动移动。");
            await PostLanzouTaskAsync(20, new() { ["file_id"] = id, ["folder_id"] = folderId });
        }
        if (!await ContainsFileAsync(id, folderId))
            throw new InvalidOperationException("文件已上传但目标文件夹验收失败；上传文件 ID：" + id);
        return new { uploaded = true, fileId = id, filename = name, folderId,
            sizeBytes = file.Length, verifiedInTargetFolder = true, recoveredAfterTimeout };
    }

    private static async Task<object?> ObserveAsync(JsonElement input)
    {
        var operation = input.GetProperty("operation").GetString()!;
        return await RunBrowserAsync(new
        {
            action = "lanzou_network_control", operation, site = "lanzou"
        }, 15);
    }

    private static LocalAgentApiServer? GetServer() =>
        WpfApplication.Current is App app ? app.AgentApiServer : null;

    private static async Task<object?> RunBrowserAsync(object task, int timeoutSeconds)
    {
        var server = GetServer() ?? throw new InvalidOperationException("燕子本地服务尚未启动。");
        if (!server.IsBrowserConnected)
            throw new InvalidOperationException("Edge 浏览器助手未连接。请先启用燕子浏览器助手。");
        await BrowserGate.WaitAsync();
        try
        {
            var request = JsonSerializer.SerializeToElement(task).EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
            var result = await server.RunBrowserTaskAsync(request, timeoutSeconds);
            if (!result.success)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.message)
                    ? "蓝奏云浏览器操作失败：" + result.status : result.message);
            return new { result.success, result.status, result.taskId, data = result.data };
        }
        finally { BrowserGate.Release(); }
    }

    internal static bool IsAllowedHost(string hostname) =>
        AllowedHosts.Contains(hostname, StringComparer.OrdinalIgnoreCase);
}
