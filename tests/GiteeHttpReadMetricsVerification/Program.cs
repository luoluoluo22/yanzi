using System.Net;
using OpenQuickHost.Sync;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

var now = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
var snapshots = new List<GiteeHttpReadMetricsSnapshot>();
var physical = 0;
using var client = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((request, ct) =>
    {
        Interlocked.Increment(ref physical);
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/transport") throw new HttpRequestException("test-only");
        if (path == "/cancel") throw new OperationCanceledException(ct);
        var code = path switch
        {
            "/missing" => HttpStatusCode.NotFound,
            "/retry" => HttpStatusCode.ServiceUnavailable,
            "/unauthorized" => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.OK
        };
        return Task.FromResult(new HttpResponseMessage(code));
    }),
    snapshots.Add, TimeSpan.FromMinutes(1), () => now));

async Task Get(string path)
{
    using var response = await client.GetAsync("https://example.test" + path);
}
await Get("/ok");
await Get("/missing");
await Get("/retry");
await Get("/unauthorized");
try { await Get("/transport"); throw new Exception("transport failure suppressed"); }
catch (HttpRequestException) { }
try { await Get("/cancel"); throw new Exception("cancellation suppressed"); }
catch (OperationCanceledException) { }
using (var response = await client.PostAsync("https://example.test/write", new StringContent("x"))) { }
Check(physical == 7, "six GETs and one POST reach underlying transport");
Check(snapshots.Count == 0, "no per-request log storm before window expires");

now += TimeSpan.FromSeconds(61);
await Get("/ok");
Check(snapshots.Count == 1, "one aggregate log emitted after elapsed minute");
var m = snapshots[0];
Check(m.Sent == 6 && m.Completed == 6 && m.SentTotal == 6 && m.CompletedTotal == 6,
    "counts real GET dispatches including retry/error attempts, excludes POST");
Check(m.Success2xx == 1 && m.NotFound404 == 1 && m.Retryable429Or5xx == 1 &&
      m.OtherHttp == 1 && m.TransportErrors == 1 && m.Canceled == 1,
    "classifies every HTTP status, transport failure and cancellation");
Check(m.DurationMsTotal >= 0 && m.DurationMsMax >= 0, "latency is nonnegative");
Check(m.ToString()!.IndexOf("example.test", StringComparison.Ordinal) < 0 &&
      m.ToString()!.IndexOf("token", StringComparison.Ordinal) < 0,
    "snapshot cannot leak request URLs or credentials");

now += TimeSpan.FromSeconds(61);
await Get("/ok");
Check(snapshots.Count == 2 && snapshots[1].Sent == 1 &&
      snapshots[1].SentTotal == 7 && snapshots[1].CompletedTotal == 7,
    "per-window count resets while cumulative transport count stays monotonic");
using var failReportClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
    _ => throw new InvalidOperationException("logging unavailable"),
    TimeSpan.FromSeconds(1), () => now));
using (var response = await failReportClient.GetAsync("https://example.test/first")) { }
now += TimeSpan.FromSeconds(2);
using (var response = await failReportClient.GetAsync("https://example.test/second")) { }
Console.WriteLine("GITEE_HTTP_METRICS_PASS sentTotal=8 firstWindow=6 summaries=2");
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}
