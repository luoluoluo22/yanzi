using System.Net;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // Requires existing local API authentication. All commands are read-only and
    // executed by the already connected Yanzi browser extension in its user session.
    private async Task HandleMediaReadRouteAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        var body = await ReadJsonBodyAsync(request);
        var site = GetString(body, "site") ?? "";
        var operation = GetString(body, "operation") ?? "";
        var value = GetString(body, "value") ?? "";
        var profile = GetString(body, "profile") ?? "";
        var limit = body.TryGetProperty("limit", out var l) && l.TryGetInt32(out var n) ? n : 5;
        if (site == "douyin" && operation is "detail" or "comments")
        {
            if (limit is < 1 or > 20 || value.Length > 550 ||
                !Regex.IsMatch(value, @"\Ahttps://www\.douyin\.com/video/\d{12,22}/?(?:\?.*)?\z",
                    RegexOptions.CultureInvariant) || value.Any(char.IsControl))
                throw new ArgumentException("抖音作品地址不合法。");
        }
        else BrowserMediaApi.Command(site, operation, value, limit);
        if (!string.IsNullOrEmpty(profile) &&
            !Regex.IsMatch(profile, @"\A[A-Za-z0-9_-]{1,64}\z"))
            throw new ArgumentException("浏览器配置名称不合法。");

        var args = new Dictionary<string, object?>
        {
            ["action"] = "media_read", ["site"] = site,
            ["operation"] = operation, ["value"] = value, ["limit"] = limit,
            ["profile"] = profile,
            ["browser"] = "Ego"
        };
        var task = await RunBrowserTaskAsync(args, timeoutSeconds: 110);
        if (!task.success)
        {
            await WriteJsonAsync(response, task.status == "unavailable" ? 503 : 422,
                new { ok = false, error = task.message ?? task.status });
            return;
        }
        await WriteJsonAsync(response, 200, new { ok = true, data = task.data,
            schemaVersion = "1", source = "yanzi_browser_session" });
    }
}
