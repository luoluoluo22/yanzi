using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private static readonly SemaphoreSlim OpenCliReadGate = new(1, 1);

    private async Task HandleOpenCliBrowserRequestAsync(WebSocket socket, JsonElement request, bool extensionOrigin)
    {
        var requestId = request.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() : null;
        object response;
        try
        {
            if (!extensionOrigin || !Guid.TryParse(requestId, out _))
                throw new ArgumentException("仅允许浏览器扩展发起读取请求。");
            var site = request.GetProperty("site").GetString() ?? "";
            var operation = request.GetProperty("operation").GetString() ?? "";
            if (site == "xiaohongshu" && operation == "capabilities")
                response = new { type = "opencli_response", requestId, ok = true, data = new { available = true } };
            else
            {
                var page = request.TryGetProperty("page", out var pageValue) ? pageValue.GetInt32() : 1;
                BrowserOpenCliReader.Command(site, operation, page);
                var profile = request.TryGetProperty("profile", out var profileValue) && profileValue.ValueKind == JsonValueKind.String
                    ? profileValue.GetString() : null;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                if (!await OpenCliReadGate.WaitAsync(0, deadline.Token))
                    throw new InvalidOperationException("已有数据读取任务正在运行，请稍后重试。");
                try
                {
                    var result = await BrowserOpenCliReader.ReadAsync(site, operation, page, profile, deadline.Token);
                    response = new { type = "opencli_response", requestId, ok = true, result };
                }
                finally { OpenCliReadGate.Release(); }
            }
        }
        catch (OperationCanceledException)
        {
            response = new { type = "opencli_response", requestId, ok = false, error = "OpenCLI 读取超时，请检查网站登录与浏览器连接。" };
        }
        catch (Exception ex)
        {
            // Do not log account data or CLI output.
            HostAssets.AppendLog($"[OpenCLI] Read failed: {ex.GetType().Name}");
            response = new { type = "opencli_response", requestId, ok = false, error = ex.Message };
        }
        try { await SendBrowserFrameAsync(socket, JsonSerializer.SerializeToUtf8Bytes(response), _cts.Token); }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidOperationException or ObjectDisposedException) { }
    }

    internal static bool IsOpenCliExtensionOrigin(string? origin) =>
        origin != null && Regex.IsMatch(origin, @"\Achrome-extension://[a-p]{32}/?\z", RegexOptions.IgnoreCase);
}
