using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenQuickHost;

/// <summary>
/// 蓝奏云经过真实账号验证的受限文件传输操作。
/// 网络会话始终交给已登录的 Edge 处理；只读分享页用于兑换下载地址。
/// </summary>
public static partial class YanziLanzouCapabilityProvider
{
    private sealed record LanzouShare(Uri Url, string AccessCode, bool Protected);

    private static string RequiredId(JsonElement input, string key = "fileId")
    {
        var id = OptionalString(input, key) ?? "";
        if (!Regex.IsMatch(id, @"^[0-9]{1,18}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("蓝奏云文件 ID 必须是 1–18 位数字。");
        return id;
    }

    private static bool RequireActionConfirmation(JsonElement input)
    {
        if (!input.TryGetProperty("confirmAction", out var value) || value.ValueKind != JsonValueKind.True)
            throw new ArgumentException("此操作必须得到用户确认：confirmAction=true。");
        return true;
    }

    private static string RequiredFileName(JsonElement input, string key)
    {
        var name = OptionalString(input, key) ?? "";
        if (name.Length is < 1 or > 120 || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new ArgumentException("文件名无效或过长。");
        return name;
    }

    private static async Task<JsonElement> CallLanzouApiAsync(int taskCode, IDictionary<string, string> arguments)
    {
        if (taskCode is not (5 or 6 or 22 or 46 or 47))
            throw new ArgumentException("禁止调用未经验证的蓝奏云操作号。");
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("task", taskCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        pairs.AddRange(arguments.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));
        var body = string.Join("&", pairs.Select(pair =>
            Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        var result = await RunBrowserAsync(new
        {
            action = "workflow", site = "lanzou", url = DiskUrl, closeOnComplete = true,
            steps = new object[] { new
            {
                type = "fetch", method = "POST", url = "/doupload.php", key = "api",
                headers = new Dictionary<string, string>
                {
                    ["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8"
                },
                body
            } }
        }, 45);
        var value = JsonSerializer.SerializeToElement(result);
        var serialized = value.GetProperty("data").GetProperty("api")[0].GetString();
        if (string.IsNullOrWhiteSpace(serialized))
            throw new InvalidOperationException("蓝奏云 API 未返回数据。");
        using var document = JsonDocument.Parse(serialized);
        var envelope = document.RootElement;
        if (!envelope.TryGetProperty("status", out var http) || http.GetInt32() != 200)
            throw new InvalidOperationException("蓝奏云 API HTTP 请求失败。");
        var data = envelope.GetProperty("data");
        if (!data.TryGetProperty("zt", out var state) || state.GetRawText().Trim('"') != "1")
        {
            var detail = data.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.String
                ? (info.GetString() ?? "未知错误") : "账户或接口拒绝";
            throw new InvalidOperationException("蓝奏云操作未成功：" + detail[..Math.Min(detail.Length, 150)]);
        }
        return data.Clone();
    }

    private static async Task<LanzouShare> GetShareAsync(string fileId)
    {
        var data = await CallLanzouApiAsync(22, new Dictionary<string, string> { ["file_id"] = fileId });
        var info = data.GetProperty("info");
        var hostUrl = info.GetProperty("is_newd").GetString() ?? "";
        var shortId = info.GetProperty("f_id").GetString() ?? "";
        if (!Regex.IsMatch(shortId, @"^[A-Za-z0-9_-]{1,64}$") ||
            !Uri.TryCreate(hostUrl.TrimEnd('/') + "/" + shortId, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !IsLanzouPublicShareHost(uri.Host))
            throw new InvalidOperationException("蓝奏云返回了非受信任的分享域名。");
        var protectedFlag = info.TryGetProperty("onof", out var protectedValue) &&
            protectedValue.GetRawText().Trim('"') == "1";
        var accessCode = info.TryGetProperty("pwd", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() ?? "" : "";
        return new LanzouShare(uri, accessCode, protectedFlag);
    }

    private static bool IsLanzouPublicShareHost(string host)
    {
        var domain = host.ToLowerInvariant();
        return domain == "lanzout.com" || domain.EndsWith(".lanzout.com", StringComparison.Ordinal) ||
               domain == "lanzou.com" || domain.EndsWith(".lanzou.com", StringComparison.Ordinal) ||
               domain == "lanzoux.com" || domain.EndsWith(".lanzoux.com", StringComparison.Ordinal) ||
               domain == "lanzoui.com" || domain.EndsWith(".lanzoui.com", StringComparison.Ordinal);
    }

    private static string GetDownloadsDirectory()
    {
        const string downloadsGuid = "{374DE290-123F-4565-9164-39C4925E467B}";
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        var configured = key?.GetValue(downloadsGuid)?.ToString();
        if (!string.IsNullOrWhiteSpace(configured))
            return Environment.ExpandEnvironmentVariables(configured);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static async Task<object?> DownloadFileAsync(JsonElement input)
    {
        RequireActionConfirmation(input);
        var fileId = RequiredId(input);
        var expectedName = RequiredFileName(input, "expectedName");
        var expectedSha256 = OptionalString(input, "expectedSha256");
        if (expectedSha256 != null && !Regex.IsMatch(expectedSha256, @"^[a-fA-F0-9]{64}$"))
            throw new ArgumentException("期望 SHA-256 必须为 64 位十六进制。");

        var share = await GetShareAsync(fileId);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var html = await http.GetStringAsync(share.Url);
        var match = Regex.Match(html, @"var\s+isngis\s*=\s*'([a-zA-Z0-9_-]{10,1024})'");
        if (!match.Success) throw new InvalidOperationException("分享页中的下载凭据结构已变化。");
        var api = new Uri("https://apifile.woozooo.com/ajaxfile.php?file=" + fileId);
        using var request = new HttpRequestMessage(HttpMethod.Post, api)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["action"] = "downprocess", ["sign"] = match.Groups[1].Value,
                ["kd"] = "1", ["p"] = share.AccessCode
            })
        };
        request.Headers.Referrer = share.Url;
        request.Headers.TryAddWithoutValidation("Origin", share.Url.GetLeftPart(UriPartial.Authority));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement;
        if (data.GetProperty("zt").GetRawText().Trim('"') != "1")
            throw new InvalidOperationException("蓝奏云下载服务未授权当前文件。");
        var domain = data.GetProperty("dom").GetString() ?? "";
        var route = data.GetProperty("url").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(route) || route.Length > 2000 ||
            !Uri.TryCreate(domain.TrimEnd('/') + "/file/" + route, UriKind.Absolute, out var direct) ||
            direct.Scheme != Uri.UriSchemeHttps ||
            !(direct.Host.Equals("lanrar.com", StringComparison.OrdinalIgnoreCase) ||
              direct.Host.EndsWith(".lanrar.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("下载服务返回了未经验证的存储域名。");

        var targetFolder = GetDownloadsDirectory();
        if (!Directory.Exists(targetFolder))
            throw new InvalidOperationException("Edge 下载目录不存在，请检查 Windows 下载文件夹位置。");
        var before = Directory.GetFiles(targetFolder).ToDictionary(
            p => p, p => File.GetLastWriteTimeUtc(p), StringComparer.OrdinalIgnoreCase);
        var start = DateTime.UtcNow;
        // 直接网络下载会遇到 CDN 浏览器 JS 校验。经实测必须由真实 Edge 导航完成。
        Process.Start(new ProcessStartInfo("msedge.exe")
        {
            Arguments = "\"" + direct.AbsoluteUri + "\"", UseShellExecute = true
        });
        var ext = Path.GetExtension(expectedName);
        var stem = Path.GetFileNameWithoutExtension(expectedName);
        var filePattern = new Regex("^" + Regex.Escape(stem) + @"(?: \(\d+\))?" +
            Regex.Escape(ext) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var attempt = 0; attempt < 110; attempt++)
        {
            await Task.Delay(500);
            var candidate = Directory.EnumerateFiles(targetFolder).FirstOrDefault(path =>
            {
                if (!filePattern.IsMatch(Path.GetFileName(path))) return false;
                var last = File.GetLastWriteTimeUtc(path);
                return last >= start.AddSeconds(-2) &&
                       (!before.TryGetValue(path, out var oldTime) || last > oldTime);
            });
            if (candidate == null) continue;
            try
            {
                using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length == 0) continue;
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var prefix = new byte[2];
                    if (stream.Read(prefix) != 2 || prefix[0] != 'P' || prefix[1] != 'K')
                        throw new InvalidOperationException("下载文件不是有效 ZIP；可能是 CDN 校验页。");
                    stream.Position = 0;
                }
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                if (expectedSha256 != null && !digest.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("蓝奏云下载 SHA-256 不匹配，已禁止标记成功。");
                return new { fileId, downloaded = true, verified = true, filePath = candidate,
                    sizeBytes = stream.Length, sha256 = digest,
                    expectedHashMatched = expectedSha256 == null ? (bool?)null : true };
            }
            catch (IOException) { /* Edge 正在写入文件，继续轮询 */ }
        }
        throw new TimeoutException("Edge 尚未完成可验证下载，请检查下载面板。");
    }
}
