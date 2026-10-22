using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private WebSocket? _activeBrowserSocket;
    public string ConnectedBrowserName { get; private set; } = "";
    public event Action<bool>? BrowserConnectionChanged;
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>
        _pendingBrowserTasks = new(StringComparer.OrdinalIgnoreCase);

    private static void Check(bool actual, string message)
    {
        if (!actual) throw new InvalidOperationException("FAILED: " + message);
    }

    public static async Task RunTests()
    {
        using var edge = new FakeSocket();
        using var ego = new FakeSocket();
        var app = new LocalAgentApiServer();
        var eventCount = 0;
        app.BrowserConnectionChanged += _ => eventCount++;
        app.RegisterBrowser(edge, 11, "Edge");
        app.RegisterBrowser(ego, 12, "Ego");
        Check(app.GetConnectedBrowserDetails().Length == 2, "Both connections registered");
        Check(app.ConnectedBrowserName == "Ego", "Ego primary");
        using (var egoAgain = new FakeSocket())
        {
            app.RegisterBrowser(egoAgain, 13, "Ego");
            Check(ReferenceEquals(app.SelectBrowser()?.Socket, egoAgain),
                "Reconnected Ego becomes preferred, not stale earlier socket");
            app.DisconnectBrowser(egoAgain);
        }
        Check(ReferenceEquals(app.SelectBrowser("browser-11")?.Socket, edge), "Select exact connection");
        Check(ReferenceEquals(app.SelectBrowser(browserName:"Edge")?.Socket, edge), "Select Edge by name");
        Check(app.SelectBrowser("browser-missing") == null, "Unknown explicit destination does not fall back");
        Check(ReferenceEquals(app.SelectBrowser(preferEdge:true)?.Socket, edge), "Legacy signed-in Edge preferred");

        var a = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.RegisterPendingBrowserTask("task-ego", ego, a);
        using (var doc = JsonDocument.Parse("{\"type\":\"task_response\",\"status\":\"success\"}"))
        {
            Check(!app.CompleteBrowserTask(edge, "task-ego", doc.RootElement), "Reject response from wrong browser");
            Check(!a.Task.IsCompleted, "Wrong browser has no effect");
            Check(app.CompleteBrowserTask(ego, "task-ego", doc.RootElement), "Accept response from correct browser");
            Check((await a.Task).GetProperty("status").GetString()=="success", "Response delivered");
        }
        var edgePending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var egoPending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.RegisterPendingBrowserTask("edge-job", edge, edgePending);
        app.RegisterPendingBrowserTask("ego-job", ego, egoPending);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            app.SendBrowserFrameAsync(edge, Encoding.UTF8.GetBytes("hi"), CancellationToken.None)));
        Check(edge.MaxInFlight == 1, "Parallel writes on one socket are serialized");
        await Task.WhenAll(
            app.SendBrowserFrameAsync(edge, Encoding.UTF8.GetBytes("one"), CancellationToken.None),
            app.SendBrowserFrameAsync(ego, Encoding.UTF8.GetBytes("two"), CancellationToken.None));
        ego.Disconnect();
        app.DisconnectBrowser(ego);
        Check((await egoPending.Task).GetProperty("message").GetString()=="browser_connection_lost",
            "Disconnected browser only fails its own task");
        Check(!edgePending.Task.IsCompleted, "Edge task unaffected by Ego disconnect");
        Check(app.ConnectedBrowserName=="Edge", "Primary falls back to Edge");
        using (var doc = JsonDocument.Parse("{\"type\":\"task_response\",\"status\":\"success\"}"))
            Check(app.CompleteBrowserTask(edge, "edge-job", doc.RootElement), "Edge task completes after Ego lost");
        Check(eventCount>=3, "Connection changes emitted");
        // Catch unsupported capability schema keywords before a packaged GUI build
        // can crash during App.OnStartup registration.
        const string schemaOpen = "YanziCapabilitySchema.Parse(\"\"\"";
        var providerFile = Path.Combine(Directory.GetCurrentDirectory(), "src", "OpenQuickHost",
            "YanziBrowserCapabilityProvider.cs");
        var providerSource = File.ReadAllText(providerFile);
        var offset = 0;
        var schemaCount = 0;
        while (true)
        {
            var begin = providerSource.IndexOf(schemaOpen, offset, StringComparison.Ordinal);
            if (begin < 0) break;
            begin += schemaOpen.Length;
            var end = providerSource.IndexOf("\"\"\"", begin, StringComparison.Ordinal);
            Check(end > begin, "Capability raw schema must terminate");
            var definition = YanziCapabilitySchema.Parse(providerSource.Substring(begin, end - begin));
            YanziCapabilitySchema.ValidateDefinition(definition);
            schemaCount++;
            offset = end + 3;
        }
        Check(schemaCount >= 6, "All browser capability schemas must be covered");

        var bounded = YanziCapabilitySchema.Parse("{\"type\":\"string\",\"minLength\":1,\"maxLength\":8}");
        YanziCapabilitySchema.ValidateDefinition(bounded);
        YanziCapabilitySchema.Validate(bounded, JsonDocument.Parse("\"hello\"").RootElement);
        var rejected = false;
        try { YanziCapabilitySchema.Validate(bounded, JsonDocument.Parse("\"excessivelylong\"").RootElement); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "maxLength must reject oversized input");
        Console.WriteLine($"PASS: startup capability schemas ({schemaCount}) and maxLength validation");
        Console.WriteLine("PASS: two browsers coexist, targeted routing, response isolation, per-socket send locks, disconnect and fallback");
    }
}

public sealed class FakeSocket : WebSocket
{
    private WebSocketState _state = WebSocketState.Open;
    private int _inFlight;
    public int MaxInFlight { get; private set; }
    public void Disconnect() => _state=WebSocketState.Closed;
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;
    public override void Abort() => Disconnect();
    public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
    { Disconnect();return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
    { Disconnect();return Task.CompletedTask; }
    public override void Dispose() => Disconnect();
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        => throw new NotSupportedException();
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
        bool endOfMessage, CancellationToken ct)
    {
        int active=Interlocked.Increment(ref _inFlight);
        MaxInFlight=Math.Max(MaxInFlight, active);
        await Task.Delay(12,ct);
        Interlocked.Decrement(ref _inFlight);
    }
}
public class Program
{
    public static async Task Main() => await LocalAgentApiServer.RunTests();
}