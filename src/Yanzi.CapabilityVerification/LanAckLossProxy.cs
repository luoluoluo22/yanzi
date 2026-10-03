using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal sealed class LanAckLossProxy : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly HttpClient client = new(new HttpClientHandler {UseProxy = false});
    private readonly Task loop;
    public int Port { get; }
    public bool Dropped { get; private set; }
    public int BlockZeroRequests { get; private set; }
    public LanAckLossProxy(IPAddress destination, int port)
    {
        var finder = new TcpListener(IPAddress.Loopback, 0); finder.Start(); Port = ((IPEndPoint)finder.LocalEndpoint).Port; finder.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/"); listener.Start();
        loop = Task.Run(async () => {
            while (listener.IsListening) {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); } catch (HttpListenerException) { break; }
                if (context.Request.Url!.AbsolutePath != "/v1/lan/secure" || context.Request.ContentLength64 > 2000000) { context.Response.StatusCode = 400; context.Response.Close(); continue; }
                using var metadata = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(context.Request.Headers["X-Yanzi-Frame"]!)));
                var route = metadata.RootElement.GetProperty("path").GetString()!;
                bool blockZero = route.EndsWith("/blocks/0", StringComparison.Ordinal); if (blockZero) BlockZeroRequests++;
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{destination}:{port}/v1/lan/secure");
                foreach (var name in new[] {"X-Yanzi-Pair", "X-Yanzi-Frame", "X-Yanzi-Nonce"}) request.Headers.Add(name, context.Request.Headers[name]);
                using var body = new MemoryStream(); await context.Request.InputStream.CopyToAsync(body); request.Content = new ByteArrayContent(body.ToArray());
                using var response = await client.SendAsync(request); var bytes = await response.Content.ReadAsByteArrayAsync();
                if (blockZero && !Dropped && response.IsSuccessStatusCode) { Dropped = true; context.Response.Abort(); continue; }
                context.Response.StatusCode = (int)response.StatusCode;
                foreach (var name in new[] {"X-Yanzi-Status", "X-Yanzi-Nonce"}) if (response.Headers.TryGetValues(name, out var values)) context.Response.Headers[name] = values.Single();
                context.Response.ContentLength64 = bytes.Length; await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
            }
        });
    }
    public async ValueTask DisposeAsync() { listener.Stop(); await loop; listener.Close(); client.Dispose(); }
}
