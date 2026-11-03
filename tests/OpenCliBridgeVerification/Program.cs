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
// OpenCLI store onboarding: use synthetic browser preferences, never remove
// or disable the user's actually-installed bridge during testing.
var prefsFile = Path.Combine(Path.GetTempPath(), "opencli-onboarding-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var missing = """{"extensions":{"settings":{}}}""";
    File.WriteAllText(prefsFile, missing);
    var absent = OpenCliExtensionOnboarding.InspectFiles([prefsFile]);
    Assert(absent.Status == "missing" && absent.ProfilesChecked == 1,
        "missing extension detected from browser registration, not daemon state");
    Assert(absent.StoreUrl == "https://chromewebstore.google.com/detail/opencli/ildkmabpimmkaediidaifkhjpohdnifk",
        "official OpenCLI Chrome Web Store link is exact");

    var installed = """{"extensions":{"settings":{"ildkmabpimmkaediidaifkhjpohdnifk":{"state":1}}}}""";
    File.WriteAllText(prefsFile, installed);
    Assert(OpenCliExtensionOnboarding.InspectFiles([prefsFile]).Status == "installed",
        "installed extension does not get reinstall prompt");
    var disabled = """{"extensions":{"settings":{"ildkmabpimmkaediidaifkhjpohdnifk":{"state":0,"disable_reasons":1}}}}""";
    File.WriteAllText(prefsFile, disabled);
    Assert(OpenCliExtensionOnboarding.InspectFiles([prefsFile]).Status == "disabled",
        "disabled extension prompts enable instead of reinstall");
    File.WriteAllText(prefsFile, "{malformed");
    Assert(OpenCliExtensionOnboarding.InspectFiles([prefsFile]).Status == "unknown",
        "unreadable preferences never misdiagnosed as missing");

    var tempRoot = Path.Combine(Path.GetTempPath(), "opencli-scoped-" + Guid.NewGuid().ToString("N"));
    var egoDir = Path.Combine(tempRoot, "Ego", "Profile 4");
    var chromeDir = Path.Combine(tempRoot, "Chrome", "Default");
    Directory.CreateDirectory(egoDir);
    Directory.CreateDirectory(chromeDir);
    var egoPrefs = Path.Combine(egoDir, "Secure Preferences");
    var chromePrefs = Path.Combine(chromeDir, "Secure Preferences");
    const string caller = "hbbaogocjdkfbffpcoiaacdjioamjfef";
    File.WriteAllText(egoPrefs, """{"extensions":{"settings":{"hbbaogocjdkfbffpcoiaacdjioamjfef":{}}}}""");
    File.WriteAllText(chromePrefs, """{"extensions":{"settings":{"ildkmabpimmkaediidaifkhjpohdnifk":{"state":1}}}}""");
    Assert(OpenCliExtensionOnboarding.InspectForCaller([egoPrefs, chromePrefs], caller).Status == "missing",
        "OpenCLI installed in another browser profile cannot conceal missing Ego extension");
    File.WriteAllText(egoPrefs, """{"extensions":{"settings":{"hbbaogocjdkfbffpcoiaacdjioamjfef":{},"ildkmabpimmkaediidaifkhjpohdnifk":{"state":1}}}}""");
    Assert(OpenCliExtensionOnboarding.InspectForCaller([egoPrefs, chromePrefs], caller).Status == "installed",
        "same-profile OpenCLI installation is recognized");
    Assert(OpenCliExtensionOnboarding.InspectForCaller([chromePrefs], caller).Status == "unknown",
        "no invoking extension profile yields unknown, not missing");
    Directory.Delete(tempRoot, recursive: true);
}
finally { try { File.Delete(prefsFile); } catch (IOException) { } }

Assert(BrowserMediaApi.Command("douyin", "search", "人工智能", 5)[1] == "search", "Douyin search read-only command");
Assert(BrowserMediaApi.Command("bilibili", "detail", "BV1atCRYsE7x", 5)[1] == "video", "Bilibili video detail command");
Assert(BrowserMediaApi.Command("xiaohongshu", "comments",
    "https://www.xiaohongshu.com/search_result/6ae890890000000032022adf?xsec_token=testtoken", 5)[1] == "comments",
    "Xiaohongshu comments read-only command");
Reject(() => BrowserMediaApi.Command("douyin", "publish", "123", 5), "media write operation blocked");
Reject(() => BrowserMediaApi.Command("unknown", "search", "AI", 5), "unknown media platform blocked");
Reject(() => BrowserMediaApi.Command("bilibili", "search", "--help", 5), "flag-shaped media keyword blocked");
Reject(() => BrowserMediaApi.Command("bilibili", "search", "AI", 21), "unbounded search blocked");
Reject(() => BrowserMediaApi.Command("bilibili", "detail", "https://attacker.example/video/BV1atCRYsE7x", 5), "unsafe target host blocked");
Reject(() => BrowserMediaApi.Command("xiaohongshu", "detail", "file:///etc/passwd", 5), "local file URL blocked");
Reject(() => BrowserMediaApi.Command("douyin", "detail", "7427381362337778980", 5), "Douyin creator-only details blocked");
using (var fixture = JsonDocument.Parse("""
    [{"rank":1,"desc":"Video title","author":"Test author",
      "url":"https://www.douyin.com/video/7427381362337778980",
      "likes":68000,"plays":0,"comments":0,"shares":0}]
    """))
{
    var normalized = JsonSerializer.Serialize(BrowserMediaApi.Normalize("douyin", "search", "AI", fixture.RootElement));
    using var parsed = JsonDocument.Parse(normalized);
    var first = parsed.RootElement.GetProperty("items")[0].GetProperty("metrics");
    Assert(first.GetProperty("plays").GetProperty("status").GetString() == "unverified" &&
           first.GetProperty("plays").GetProperty("value").ValueKind == JsonValueKind.Null,
           "Douyin search zero metrics are not fabricated");
    Assert(first.GetProperty("likes").GetProperty("value").GetInt64() == 68000,
           "real reported likes retained");
    Assert(first.GetProperty("collects").GetProperty("status").GetString() == "not_provided",
           "absent metric explicitly marked not provided");
}
using (var fixture = JsonDocument.Parse("""
    [{"field":"title","value":"Test Bili"},{"field":"author","value":"Test creator"},
     {"field":"view","value":"1477391"},{"field":"reply","value":"2293"}]
    """))
{
    var normalized = JsonSerializer.Serialize(BrowserMediaApi.Normalize("bilibili", "detail", "BV1atCRYsE7x", fixture.RootElement));
    using var parsed = JsonDocument.Parse(normalized);
    var first = parsed.RootElement.GetProperty("item").GetProperty("metrics");
    Assert(first.GetProperty("plays").GetProperty("value").GetInt64() == 1477391 &&
           first.GetProperty("comments").GetProperty("value").GetInt64() == 2293,
           "Bilibili detail play/comment metrics normalized");
    Assert(first.GetProperty("shares").GetProperty("status").GetString() == "not_provided",
           "missing Bilibili shares never become zero");
}
using (var fixture = JsonDocument.Parse("""
    [{"rank":1,"author":"Tester","text":"Interesting detail","likes":3,"replies":2}]
    """))
{
    var normalized = JsonSerializer.Serialize(BrowserMediaApi.Normalize("bilibili", "comments", "BV1atCRYsE7x", fixture.RootElement));
    using var parsed = JsonDocument.Parse(normalized);
    Assert(parsed.RootElement.GetProperty("returned").GetInt32() == 1 &&
           parsed.RootElement.GetProperty("items")[0].GetProperty("text").GetString() == "Interesting detail",
           "Bilibili comment content normalized");
}

Assert(BrowserOpenCliReader.Command("xiaohongshu", "stats", 1).SequenceEqual(new[] { "creator-stats", "--period", "seven" }), "seven-day command");
Reject(() => BrowserOpenCliReader.Command("xiaohongshu", "publish", 1), "write command blocked");
Reject(() => BrowserOpenCliReader.Command("xiaohongshu", "notes", 2), "unsupported pagination blocked");
Reject(() => BrowserOpenCliReader.Command("unknown", "profile", 1), "unknown site blocked");
Assert(BrowserOpenCliReader.SelectProfile("\n  liveid — connected v1.0.26\n  oldid ego — not connected", "") == "liveid", "stale saved default ignored");
Assert(BrowserOpenCliReader.HasConnectedBridge("\n  liveid — connected v1.0.26"), "connected bridge detected");
Assert(!BrowserOpenCliReader.HasConnectedBridge("\n  oldid ego — not connected"), "stale bridge not considered live");
Assert(OpenCliDaemonSupervisor.ShouldWake(true, true, true), "existing CLI/browser/extension eligible for warmup");
Assert(!OpenCliDaemonSupervisor.ShouldWake(true, true, false), "no extension means no daemon warmup");
Assert(!OpenCliDaemonSupervisor.ShouldWake(false, true, true), "absent CLI means no daemon warmup");
Assert(OpenCliDaemonSupervisor.DaemonIsReady("Daemon: running\nExtension: connected"), "healthy daemon recognized");
Assert(OpenCliDaemonSupervisor.ShouldRestartDaemon("Daemon: not running"), "explicitly stopped daemon requires restart");
Assert(!OpenCliDaemonSupervisor.ShouldRestartDaemon("Daemon: running\nExtension: disconnected"), "reconnecting extension does not restart daemon");
Assert(!OpenCliDaemonSupervisor.DaemonIsReady("Daemon: running\nExtension: disconnected"), "disconnected bridge not considered ready");
Reject(() => BrowserOpenCliReader.SelectProfile("  aa — connected v1\n  bb work — connected v1", ""), "ambiguous browsers rejected");
Reject(() => BrowserOpenCliReader.SelectProfile("", ""), "disconnected browser rejected");
Reject(() => BrowserOpenCliReader.SelectProfile("", "--help" + ";cmd"), "profile injection blocked");
Assert(BrowserOpenCliReader.ParseOutput("[{\"value\":287}]\n\n  Update available: v1 → v2").GetArrayLength() == 1, "update notice tolerated");
Reject(() => BrowserOpenCliReader.ParseOutput("[]\nnot JSON"), "unexpected output rejected");
Reject(() => BrowserOpenCliReader.ParseOutput("plain text"), "invalid JSON rejected");
Assert(LocalAgentApiServer.IsOpenCliExtensionOrigin("chrome-extension://abcdefghijklmnopabcdefghijklmnop"), "extension origin accepted");
Assert(!LocalAgentApiServer.IsOpenCliExtensionOrigin("https://example.com"), "website origin rejected");
Assert(!LocalAgentApiServer.IsOpenCliExtensionOrigin(null), "missing origin rejected");
if (args.Contains("--live-safe"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
    var result = await BrowserOpenCliReader.ReadAsync("xiaohongshu", "profile", 1, null, deadline.Token);
    Console.WriteLine("OPENCLI_LIVE_SAFE_OK (personal data redacted)");
}
if (args.Contains("--media-live-safe"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var examples = new (string site, string operation, string value, int limit)[]
    {
        ("xiaohongshu", "search", "人工智能", 2),
        ("douyin", "search", "人工智能", 2),
        ("bilibili", "search", "人工智能", 2),
        ("bilibili", "detail", "BV1atCRYsE7x", 5),
        ("bilibili", "comments", "BV1atCRYsE7x", 3),
        ("xiaohongshu", "detail", "https://www.xiaohongshu.com/search_result/6ae890890000000032022adf?xsec_token=ABS3kWIaE_p5ZL9E-_LC9aEo4SBgP64YsHhnD7zzyN2Rc=&xsec_source=", 5)
    };
    foreach (var example in examples)
    {
        try
        {
            var reply = await BrowserMediaApi.ReadAsync(example.site, example.operation,
                example.value, example.limit, null, deadline.Token);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(reply));
            var data = json.RootElement.GetProperty("data");
            var returned = data.TryGetProperty("returned", out var length) ? length.GetInt32()
                : data.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object ? 1 : -1;
            var titleAvailable = example.operation != "detail"
                || data.GetProperty("item").GetProperty("title").ValueKind == JsonValueKind.String;
            Console.WriteLine("MEDIA_LIVE_SAFE_OK " + example.site + "/" + example.operation
                + " returned=" + returned + " titleAvailable=" + titleAvailable);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MEDIA_LIVE_SAFE_FAILED " + example.site + "/" + example.operation
                + " reason=" + ex.GetType().Name);
        }
    }
}

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
