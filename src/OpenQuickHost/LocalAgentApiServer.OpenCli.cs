using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    // The browser extension may request only these audited, read-only OpenCLI adapters.
    // Never accept a command line, executable path, URL or user-controlled arguments.
    private static readonly Dictionary<string, (string Site, string[] Args)> OpenCliReads =
        new(StringComparer.Ordinal)
        {
            ["xiaohongshu/profile"] = ("xiaohongshu", ["creator-profile"]),
            ["xiaohongshu/stats"] = ("xiaohongshu", ["creator-stats", "--period", "seven"]),
            ["xiaohongshu/notes"] = ("xiaohongshu", ["creator-notes", "--limit", "5"]),
            ["bilibili/profile"] = ("bilibili", ["whoami"]),
            ["bilibili/history"] = ("bilibili", ["history", "--limit", "5"]),
            ["bilibili/following"] = ("bilibili", ["following"]),
            ["douyin/profile"] = ("douyin", ["profile"]),
            ["douyin/videos"] = ("douyin", ["videos", "--limit", "5"])
        };
    private static readonly SemaphoreSlim OpenCliReadGate = new(1, 1);

    private async Task HandleOpenCliBrowserRequestAsync(WebSocket browserSocket, string requestId,
        string site, string operation, bool trustedExtensionOrigin)
    {
        object payload;
        var key = site + "/" + operation;
        if (!trustedExtensionOrigin)
        {
            payload = new { type = "opencli_response", requestId, ok = false, error = "extension_origin_required" };
        }
        else if (!Guid.TryParse(requestId, out _) || !OpenCliReads.TryGetValue(key, out var command))
        {
            payload = new { type = "opencli_response", requestId, ok = false, error = "unsupported_read_operation" };
        }
        else
        {
            try
            {
                // Cap outstanding work: OpenCLI shares authenticated browser tabs/sessions.
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                waitCts.CancelAfter(TimeSpan.FromSeconds(100));
                await OpenCliReadGate.WaitAsync(waitCts.Token);
                try
                {
                    var data = await ExecuteReadOnlyOpenCliAsync(command.Site, command.Args, waitCts.Token);
                    payload = new { type = "opencli_response", requestId, ok = true, site, operation, data };
                }
                finally { OpenCliReadGate.Release(); }
            }
            catch (OperationCanceledException)
            {
                payload = new { type = "opencli_response", requestId, ok = false, error = "opencli_timeout" };
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[OpenCLI] Read failed: {key}: {ex.GetType().Name}");
                payload = new { type = "opencli_response", requestId, ok = false, error = ex.Message.Length > 250 ? ex.Message[..250] : ex.Message };
            }
        }

        if (browserSocket.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        try
        {
            await _browserSendGate.WaitAsync(_cts.Token);
            try
            {
                if (browserSocket.State == WebSocketState.Open)
                    await browserSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            finally { _browserSendGate.Release(); }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Browser may disconnect while a read is in flight.
        }
    }

    private static async Task<JsonElement> ExecuteReadOnlyOpenCliAsync(string site, string[] args,
        CancellationToken cancellationToken)
    {
        // Invoke the installed npm package through Node, never through a shell.
        var script = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@jackwener", "opencli", "dist", "src", "main.js");
        if (!File.Exists(script))
            throw new FileNotFoundException("OpenCLI 未安装，请先执行 npm install -g @jackwener/opencli。");

        var node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (!File.Exists(node)) node = "node.exe";
        var start = new ProcessStartInfo(node)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(site);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.ArgumentList.Add("--format");
        start.ArgumentList.Add("json");
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动 OpenCLI。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("OpenCLI 返回失败：" +
                (string.IsNullOrWhiteSpace(error) ? output : error).Trim()[..Math.Min(250, (string.IsNullOrWhiteSpace(error) ? output : error).Trim().Length)]);
        // npm OpenCLI may append update notices after its JSON output.
        var trimmed = output.Trim();
        var updateIndex = trimmed.IndexOf("\n  Update available:", StringComparison.Ordinal);
        if (updateIndex > 0) trimmed = trimmed[..updateIndex].Trim();
        var extensionUpdateIndex = trimmed.IndexOf("\n  Extension update available:", StringComparison.Ordinal);
        if (extensionUpdateIndex > 0) trimmed = trimmed[..extensionUpdateIndex].Trim();
        if (trimmed.Length > 256_000) throw new InvalidOperationException("读取结果超出 256KB，请缩小读取范围。");
        try { using var doc = JsonDocument.Parse(trimmed); return doc.RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("OpenCLI 未返回可解析的 JSON 数据。"); }
    }
}
