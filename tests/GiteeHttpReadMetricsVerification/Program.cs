using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics;
using OpenQuickHost.Sync;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

static async Task WaitForAsync(Func<bool> ready, string name)
{
    var timer = Stopwatch.StartNew();
    while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(5))
        await Task.Delay(10);
    Check(ready(), name);
}

var now = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
var snapshots = new ConcurrentQueue<GiteeHttpReadMetricsSnapshot>();
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
    snapshots.Enqueue, TimeSpan.FromMinutes(1), () => now));

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
await WaitForAsync(() => snapshots.Count == 1, "one aggregate log emitted after elapsed minute");
var m = snapshots.ElementAt(0);
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
await WaitForAsync(() => snapshots.Count == 2, "second aggregate log emitted");
Check(snapshots.ElementAt(1).Sent == 1 &&
      snapshots.ElementAt(1).SentTotal == 7 && snapshots.ElementAt(1).CompletedTotal == 7,
    "per-window count resets while cumulative transport count stays monotonic");
using var failReportClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
    _ => throw new InvalidOperationException("logging unavailable"),
    TimeSpan.FromSeconds(1), () => now));
using (var response = await failReportClient.GetAsync("https://example.test/first")) { }
now += TimeSpan.FromSeconds(2);
using (var response = await failReportClient.GetAsync("https://example.test/second")) { }
// Completion after a window closes must not be lost when that later window
// contains no new dispatches. A GET in a subsequent window triggers the flush.
var crossNow = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
var crossSnapshots = new ConcurrentQueue<GiteeHttpReadMetricsSnapshot>();
var inFlight = new TaskCompletionSource<HttpResponseMessage>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var crossRequests = 0;
using (var crossClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) => Interlocked.Increment(ref crossRequests) == 1
        ? inFlight.Task
        : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
    crossSnapshots.Enqueue, TimeSpan.FromMinutes(1), () => crossNow)))
{
    var first = crossClient.GetAsync("https://example.test/inflight");
    crossNow += TimeSpan.FromSeconds(61);
    using (var response = await crossClient.GetAsync("https://example.test/trigger")) { }
    await WaitForAsync(() => crossSnapshots.Count == 1, "in-flight window logged");
    Check(crossSnapshots.ElementAt(0).Sent == 1 &&
          crossSnapshots.ElementAt(0).Completed == 0, "in-flight request is not marked completed");
    inFlight.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
    using (var response = await first) { }
    crossNow += TimeSpan.FromSeconds(61);
    using (var response = await crossClient.GetAsync("https://example.test/later")) { }
    await WaitForAsync(() => crossSnapshots.Count == 2, "cross-window completion logged");
    Check(crossSnapshots.ElementAt(1).Sent == 1 &&
          crossSnapshots.ElementAt(1).Completed == 2 && crossSnapshots.ElementAt(1).Success2xx == 2,
        "cross-window completions are retained and fully classified");
}
// A short sync must be counted even when no later GET arrives. The handler is
// process-wide in production, so its low-frequency timer can flush an idle burst.
var periodicReports = 0;
var periodicFlush = new TaskCompletionSource<GiteeHttpReadMetricsSnapshot>(
    TaskCreationOptions.RunContinuationsAsynchronously);
using (var periodicClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
    m => { Interlocked.Increment(ref periodicReports); periodicFlush.TrySetResult(m); },
    TimeSpan.FromMilliseconds(100))))
{
    using (var response = await periodicClient.GetAsync("https://example.test/short-cycle")) { }
    var flushed = await periodicFlush.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(flushed.Sent == 1 && flushed.Completed == 1 &&
          flushed.SentTotal == 1 && flushed.Success2xx == 1,
        "timer flushes short sync without requiring another GET");
    await Task.Delay(250);
    Check(Volatile.Read(ref periodicReports) == 1,
        "idle timer does not emit empty windows");
}
// A GET may remain in flight beyond its dispatch window. With no subsequent
// requests, the timer must still report its completion in the next window.
var lateSnapshots = new ConcurrentQueue<GiteeHttpReadMetricsSnapshot>();
var lateResponse = new TaskCompletionSource<HttpResponseMessage>(
    TaskCreationOptions.RunContinuationsAsynchronously);
using (var lateClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) => lateResponse.Task),
    lateSnapshots.Enqueue, TimeSpan.FromMilliseconds(150))))
{
    var pending = lateClient.GetAsync("https://example.test/late");
    await WaitForAsync(() => lateSnapshots.Count >= 1,
        "timer reports an in-flight GET without a later dispatch");
    var firstWindow = lateSnapshots.ElementAt(0);
    Check(firstWindow.Sent == 1 && firstWindow.Completed == 0 &&
          firstWindow.SentTotal == 1 && firstWindow.CompletedTotal == 0,
        "dispatch-only window retains an outstanding request");
    lateResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
    using (var response = await pending) { }
    await WaitForAsync(() => lateSnapshots.Count >= 2,
        "timer reports a late completion without a later GET");
    var secondWindow = lateSnapshots.ElementAt(1);
    Check(secondWindow.Sent == 0 && secondWindow.Completed == 1 &&
          secondWindow.Success2xx == 1 && secondWindow.SentTotal == 1 &&
          secondWindow.CompletedTotal == 1,
        "completion-only window preserves counters and status classification");
}
// Timer and GET can close consecutive windows concurrently. A slow logger must
// not cause later cumulative totals to appear before earlier ones in the log.
long orderedTicks = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero).Ticks;
var orderedStep = TimeSpan.FromHours(1).Ticks + TimeSpan.FromSeconds(1).Ticks;
var orderedReports = new List<long>();
var reportStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
using var releaseReport = new ManualResetEventSlim(false);
var reportCalls = 0;
using (var orderedClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
    snapshot =>
    {
        if (Interlocked.Increment(ref reportCalls) == 1)
        {
            reportStarted.TrySetResult(true);
            releaseReport.Wait(TimeSpan.FromSeconds(5));
        }
        lock (orderedReports) orderedReports.Add(snapshot.SentTotal);
    }, TimeSpan.FromHours(1), () => new DateTimeOffset(Interlocked.Read(ref orderedTicks), TimeSpan.Zero))))
{
    using (var response = await orderedClient.GetAsync("https://example.test/first")) { }
    Interlocked.Add(ref orderedTicks, orderedStep);
    var firstFlush = Task.Run(async () =>
    {
        using var response = await orderedClient.GetAsync("https://example.test/second");
    });
    try
    {
        await reportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The GET that closed the first window must finish even while logging is blocked.
        await firstFlush.WaitAsync(TimeSpan.FromSeconds(2));
        Check(true, "slow metrics sink does not delay the triggering HTTP GET");
        Interlocked.Add(ref orderedTicks, orderedStep);
        // This GET must not wait for the first logger, and must not overtake it.
        using (var response = await orderedClient.GetAsync("https://example.test/third")) { }
    }
    finally { releaseReport.Set(); }
    await firstFlush.WaitAsync(TimeSpan.FromSeconds(5));
    await WaitForAsync(() => { lock (orderedReports) return orderedReports.Count == 2; },
        "both ordered windows logged");
    lock (orderedReports)
        Check(orderedReports.SequenceEqual(new long[] { 1, 2 }),
            "concurrent window closures preserve monotonic log ordering");
}
// Integration regression: 64 logical reads share one actual HTTP handler dispatch.
// The metrics window records both physical transport and deduplication totals.
var integratedReports = new ConcurrentQueue<GiteeHttpReadMetricsSnapshot>();
var integratedPending = new TaskCompletionSource<HttpResponseMessage>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var integratedPhysical = 0;
using (var integratedClient = new HttpClient(new GiteeHttpReadMetricsHandler(
    new FakeHandler((_, _) =>
    {
        var call = Interlocked.Increment(ref integratedPhysical);
        return call == 1 ? integratedPending.Task : Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[] { 2 }) });
    }), integratedReports.Enqueue, TimeSpan.FromMilliseconds(150))))
{
    var integratedKey = GiteeReadFlightCoordinator.CreateKey(
        "qa-owner", "qa-repo", "main", "qa-object.json", "test-only-token");
    Func<Task<byte[]?>> integratedFetch = async () =>
        await integratedClient.GetByteArrayAsync("https://example.test/object");
    var logicalReads = Enumerable.Range(0, 64)
        .Select(_ => GiteeReadFlightCoordinator.ReadAsync(
            integratedKey, integratedFetch, CancellationToken.None))
        .ToArray();
    Check(Volatile.Read(ref integratedPhysical) == 1,
        "64 concurrent logical reads dispatch exactly one physical GET");
    integratedPending.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new ByteArrayContent(new byte[] { 1 }) });
    var logicalResults = await Task.WhenAll(logicalReads);
    Check(logicalResults.All(x => x is { Length: 1 } && x[0] == 1),
        "every coalesced waiter receives the first HTTP response");
    await WaitForAsync(() => integratedReports.Count >= 1,
        "singleflight integration produces a physical GET metrics window");
    var integratedFirst = integratedReports.ElementAt(0);
    Check(integratedFirst.Sent == 1 && integratedFirst.Completed == 1 &&
          integratedFirst.SingleflightLeadersTotal == 1 &&
          integratedFirst.SingleflightSharedWaitersTotal == 63 &&
          integratedFirst.SingleflightActiveFlights == 0,
        "metrics distinguish physical GET, singleflight leader and 63 shared waiters");
    var sequential = await GiteeReadFlightCoordinator.ReadAsync(
        integratedKey, integratedFetch, CancellationToken.None);
    Check(sequential is { Length: 1 } && sequential[0] == 2 &&
          Volatile.Read(ref integratedPhysical) == 2,
        "sequential read performs a fresh physical GET, never a stale cache hit");
    await WaitForAsync(() => integratedReports.Count >= 2,
        "second physical GET metrics window is flushed");
    var integratedSecond = integratedReports.ElementAt(1);
    Check(integratedSecond.SentTotal == 2 &&
          integratedSecond.CompletedTotal == 2 &&
          integratedSecond.SingleflightLeadersTotal == 2 &&
          integratedSecond.SingleflightSharedWaitersTotal == 63,
        "physical and singleflight totals remain monotonic across windows");
}
// Optional read-only public Gitee transport probe. This is deliberately not a
// CI prerequisite and never reads local credentials or writes remote objects.
// It proves the metrics handler counts actual HTTPS GET dispatches (not merely
// simulated handlers), but does not measure production account sync reduction.
if (Environment.GetEnvironmentVariable("YANZI_GITEE_PUBLIC_PROBE") == "1")
{
    var publicReports = new ConcurrentQueue<GiteeHttpReadMetricsSnapshot>();
    using var publicClient = new HttpClient(new GiteeHttpReadMetricsHandler(
        new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false },
        publicReports.Enqueue, TimeSpan.FromMilliseconds(250)));
    publicClient.Timeout = TimeSpan.FromSeconds(20);
    for (var i = 0; i < 2; i++)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://gitee.com/api/v5/repos/openharmony/docs");
        using var response = await publicClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);
        Check(response.StatusCode == HttpStatusCode.OK,
            $"public Gitee HTTPS GET {i + 1} succeeds without credentials");
    }
    await WaitForAsync(() => publicReports.Sum(s => s.Completed) == 2,
        "public Gitee transport windows report both completed GETs");
    Check(publicReports.Sum(s => s.Sent) == 2 &&
          publicReports.Sum(s => s.Completed) == 2 &&
          publicReports.Sum(s => s.Success2xx) == 2 &&
          publicReports.Sum(s => s.OtherHttp + s.Retryable429Or5xx +
              s.TransportErrors + s.Canceled) == 0,
        "actual public Gitee HTTPS transport is counted exactly twice");
    Console.WriteLine("GITEE_PUBLIC_TRANSPORT_PASS sent=2 completed=2 status2xx=2");
}
Console.WriteLine("GITEE_HTTP_METRICS_PASS sentTotal=8 firstWindow=6 summaries=2 integratedPhysical=2 sharedWaiters=63");
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}
