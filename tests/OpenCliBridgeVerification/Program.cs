using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenQuickHost;

if (args.Contains("--serve"))
{
    // Isolated verification host: only this bridge is enabled, no settings/sync/hooks.
    var listener = new HttpListener();
    listener.Prefixes.Add("http://127.0.0.1:53928/");
    listener.Start();
    Console.WriteLine("OPENCLI_VERIFICATION_LISTENING 53928");
    while (true)
    {
        var context = await listener.GetContextAsync();
        if (!context.Request.IsWebSocketRequest) { context.Response.StatusCode = 404; context.Response.Close(); continue; }
        var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
        _ = new LocalAgentApiServer().ServeAsync(socket,
            LocalAgentApiServer.IsOpenCliExtensionOrigin(context.Request.Headers["Origin"]));
    }
}

void Assert(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { Console.WriteLine("PASS " + name); return; }
    catch (InvalidOperationException) { Console.WriteLine("PASS " + name); return; }
    throw new Exception("Expected rejection: " + name);
}
Assert(BrowserOpenCliReader.Command("xiaohongshu", "stats", 1).SequenceEqual(new[] { "creator-stats", "--period", "seven" }), "seven-day command");
Reject(() => BrowserOpenCliReader.Command("xiaohongshu", "publish", 1), "write command blocked");
Reject(() => BrowserOpenCliReader.Command("xiaohongshu", "notes", 2), "unsupported pagination blocked");
Reject(() => BrowserOpenCliReader.Command("unknown", "profile", 1), "unknown site blocked");
Assert(BrowserOpenCliReader.SelectProfile("\n  liveid — connected v1.0.26\n  oldid ego — not connected", "") == "liveid", "stale saved default ignored");
Reject(() => BrowserOpenCliReader.SelectProfile("  aa — connected v1\n  bb work — connected v1", ""), "ambiguous browsers rejected");
Reject(() => BrowserOpenCliReader.SelectProfile("", ""), "disconnected browser rejected");
Reject(() => BrowserOpenCliReader.SelectProfile("", "--help" + ";cmd"), "profile injection blocked");
Assert(BrowserOpenCliReader.ParseOutput("[{\"value\":287}]\n\n  Update available: v1 → v2").GetArrayLength() == 1, "update notice tolerated");
Reject(() => BrowserOpenCliReader.ParseOutput("[]\nnot JSON"), "unexpected output rejected");
Reject(() => BrowserOpenCliReader.ParseOutput("plain text"), "invalid JSON rejected");
Assert(LocalAgentApiServer.IsOpenCliExtensionOrigin("chrome-extension://abcdefghijklmnopabcdefghijklmnop"), "extension origin accepted");
Assert(!LocalAgentApiServer.IsOpenCliExtensionOrigin("https://example.com"), "website origin rejected");
Assert(!LocalAgentApiServer.IsOpenCliExtensionOrigin(null), "missing origin rejected");
if (args.Contains("--live"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    foreach (var operation in new[] { "profile", "notes", "stats" })
    {
        var data = await BrowserOpenCliReader.ReadAsync("xiaohongshu", operation, 1, null, deadline.Token);
        Console.WriteLine("LIVE " + operation + " " + JsonSerializer.Serialize(data));
    }
}

namespace OpenQuickHost
{
    internal static class HostAssets { internal static void AppendLog(string text) => Console.WriteLine(text); }
    public sealed partial class LocalAgentApiServer
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private async Task SendBrowserFrameAsync(WebSocket socket, byte[] bytes, CancellationToken token)
        {
            await _sendGate.WaitAsync(token);
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token); }
            finally { _sendGate.Release(); }
        }
        internal async Task ServeAsync(WebSocket socket, bool extensionOrigin)
        {
            var buffer = new byte[16384];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var message = await socket.ReceiveAsync(buffer, _cts.Token);
                    if (message.MessageType == WebSocketMessageType.Close) break;
                    using var json = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, message.Count));
                    if (json.RootElement.GetProperty("type").GetString() == "opencli_request")
                        _ = HandleOpenCliBrowserRequestAsync(socket, json.RootElement.Clone(), extensionOrigin);
                }
            }
            catch (WebSocketException) { }
            finally { socket.Dispose(); }
        }
    }
}
