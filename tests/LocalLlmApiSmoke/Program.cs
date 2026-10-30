using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenQuickHost;

internal static class Program
{
    private static int Port()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static async Task Main()
    {
        var proxyPort = Port();
        var upstreamPort = Port();
        var upstream = new HttpListener();
        upstream.Prefixes.Add($"http://127.0.0.1:{upstreamPort}/");
        upstream.Start();
        var mockCount = 0;
        using var mockCts = new CancellationTokenSource();
        var mockTask = Task.Run(async () =>
        {
            while (!mockCts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await upstream.GetContextAsync(); }
                catch (HttpListenerException) { break; }
                using var read = new StreamReader(ctx.Request.InputStream);
                var body = await read.ReadToEndAsync();
                var json = JsonNode.Parse(body)!.AsObject();
                if (json["model"]?.GetValue<string>() != "demo")
                    throw new Exception("Provider model was not correctly resolved");
                if (ctx.Request.Headers["Authorization"] != "Bearer provider-secret")
                    throw new Exception("Provider credential not injected");
                Interlocked.Increment(ref mockCount);
                var isStream = json["stream"]?.GetValue<bool>() == true;
                var response = isStream
                    ? "data: {\"id\":\"sample\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hello\"},\"finish_reason\":null}]}\n\ndata: [DONE]\n\n"
                    : "{\"id\":\"sample\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}";
                ctx.Response.ContentType = isStream ? "text/event-stream" : "application/json";
                var data = Encoding.UTF8.GetBytes(response);
                ctx.Response.ContentLength64 = data.Length;
                await ctx.Response.OutputStream.WriteAsync(data);
                ctx.Response.Close();
            }
        });

        AppSettingsStore.Current = new AppSettings
        {
            ActiveServiceProviderId = "openai",
            AiModel = "demo",
            AiServiceProviders = new()
            {
                new() { Id = "openai", Name = "Demo OpenAI", ProviderType = "OpenAI",
                    BaseUrl = $"http://127.0.0.1:{upstreamPort}/v1", ApiKey = "provider-secret",
                    IsEnabled = true, Models = ["demo"], SelectedModel = "demo" },
                new() { Id = "second", Name = "Duplicate", ProviderType = "OpenAI",
                    BaseUrl = $"http://127.0.0.1:{upstreamPort}/v1", ApiKey = "provider-secret",
                    IsEnabled = true, Models = ["demo"] },
                new() { Id = "off", Name = "Disabled", ProviderType = "OpenAI",
                    BaseUrl = $"http://127.0.0.1:{upstreamPort}/v1", ApiKey = "provider-secret",
                    IsEnabled = false, Models = ["off-model"] }
            }
        };

        var key = LocalLlmApiTokenStore.LoadOrCreate();
        Assert(key.Length == 64 && key == LocalLlmApiTokenStore.LoadOrCreate(), "DPAPI token persists");
        var server = new LocalAgentApiServer("yanzi-local-dev-token");
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{proxyPort}/");
        listener.Start();
        using var gatewayCts = new CancellationTokenSource();
        var gatewayTask = Task.Run(async () =>
        {
            while (!gatewayCts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch (HttpListenerException) { break; }
                if (!LocalLlmApiTokenStore.IsAuthorized(ctx.Request))
                {
                    await LocalAgentApiServer.WriteTestJsonAsync(ctx.Response, 401, new { error = "unauthorized" });
                    continue;
                }
                await server.HandleAsync(ctx.Request, ctx.Response, ctx.Request.Url!.AbsolutePath);
            }
        });
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{proxyPort}/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "yanzi-local-dev-token");
        using (var denied = await http.GetAsync("v1/models"))
            Assert((int)denied.StatusCode == 401, "default Agent token cannot invoke LLM");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var models = await http.GetStringAsync("v1/models");
        Assert(models.Contains("openai/demo") && models.Contains("second/demo") &&
            !models.Contains("off-model") && !models.Contains("provider-secret"), "model listing and secret shielding");

        async Task<HttpResponseMessage> Chat(string model, bool stream = false)
        {
            var body = JsonSerializer.Serialize(new { model, stream,
                messages = new[] { new { role = "user", content = "hi" } } });
            return await http.PostAsync("v1/chat/completions",
                new StringContent(body, Encoding.UTF8, "application/json"));
        }

        using (var ambiguous = await Chat("demo"))
            Assert((int)ambiguous.StatusCode == 400, "duplicate bare model rejected");
        using (var normal = await Chat("openai/demo"))
        {
            var json = await normal.Content.ReadAsStringAsync();
            Assert(normal.IsSuccessStatusCode && json.Contains("hello") && !json.Contains("provider-secret"),
                "OpenAI completion forward and secret shielding");
        }
        using (var defaultResponse = await Chat("default"))
            Assert(defaultResponse.IsSuccessStatusCode, "active provider default alias");
        using (var sse = await Chat("openai/demo", true))
        {
            var text = await sse.Content.ReadAsStringAsync();
            Assert(sse.IsSuccessStatusCode && sse.Content.Headers.ContentType?.MediaType == "text/event-stream"
                && text.Contains("data: [DONE]"), "upstream SSE forward");
        }
        Assert(mockCount == 3, "upstream called for valid requests only");
        gatewayCts.Cancel(); mockCts.Cancel(); listener.Stop(); upstream.Stop();
        await Task.WhenAll(gatewayTask, mockTask);
        Console.WriteLine("LLM API smoke: dedicated auth, DPAPI, model routing, HTTP and SSE verified.");
    }

    private static void Assert(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
}

public static class HostAssets
{
    public static string ResolveDataFilePath(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "yanzi-llm-smoke-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }
}
public sealed class AppSettings
{
    public List<AiServiceProviderSettings> AiServiceProviders { get; set; } = new();
    public string ActiveServiceProviderId { get; set; } = "";
    public string AiModel { get; set; } = "";
}
public sealed class AiServiceProviderSettings
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProviderType { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool IsEnabled { get; set; }
    public List<string> Models { get; set; } = new();
    public string SelectedModel { get; set; } = "";
}
public static class AppSettingsStore
{
    public static AppSettings Current { get; set; } = new();
    public static AppSettings Load() => Current;
}
public sealed partial class LocalAgentApiServer
{
    private readonly CancellationTokenSource _cts = new();
    private readonly string _token;
    public LocalAgentApiServer(string token) => _token = token;
    public Task<bool> HandleAsync(HttpListenerRequest req, HttpListenerResponse res, string path) =>
        TryHandleLlmApiAsync(req, res, path);
    public static Task WriteTestJsonAsync(HttpListenerResponse response, int status, object body) =>
        WriteJsonAsync(response, status, body);
    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object body)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
