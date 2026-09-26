using System.Net;
using System.Reflection;
using OpenQuickHost.Sync;

var getProxy = new StubHandler();
var getDirect = new StubHandler();
var getClient = CreateClient(getProxy, getDirect);

await ExpectTransportFailureAsync(getClient, HttpMethod.Get);
Assert(getProxy.Calls == 2 && getDirect.Calls == 1, "GET must stop after proxy, direct, and one proxy retry.");
await ExpectTransportFailureAsync(getClient, HttpMethod.Get);
Assert(getProxy.Calls == 4 && getDirect.Calls == 2, "Second failed GET must still have a bounded fallback.");
await ExpectTransportFailureAsync(getClient, HttpMethod.Get);
Assert(getProxy.Calls == 4 && getDirect.Calls == 2, "Cooldown must reject repeated requests without network traffic.");

getClient.ResumeTransportAfterNetworkChange();
getProxy.Succeed = true;
using (var response = await SendAsync(getClient, HttpMethod.Get))
{
    Assert(response.StatusCode == HttpStatusCode.OK, "Recovery request must succeed.");
}
Assert(getProxy.Calls == 5, "Network recovery must permit a fresh request.");
getProxy.Succeed = false;
await ExpectTransportFailureAsync(getClient, HttpMethod.Get);
Assert(getProxy.Calls == 7 && getDirect.Calls == 3, "Successful response must reset the failure counter.");

var postProxy = new StubHandler();
var postDirect = new StubHandler();
var postClient = CreateClient(postProxy, postDirect);
await ExpectTransportFailureAsync(postClient, HttpMethod.Post);
Assert(postProxy.Calls == 1 && postDirect.Calls == 0, "POST must not be replayed after an ambiguous transport failure.");

using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await SendAsync(postClient, HttpMethod.Get, canceled.Token);
    throw new Exception("Canceled request unexpectedly succeeded.");
}
catch (OperationCanceledException)
{
    Assert(postProxy.Calls == 1 && postDirect.Calls == 0, "Caller cancellation must not issue a request.");
}

Console.WriteLine("Network retry verification passed: bounded fallback, cooldown, recovery, non-replayed POST, cancellation.");

static CloudSyncClient CreateClient(StubHandler proxy, StubHandler direct)
{
    var client = new CloudSyncClient(new SyncOptions { BaseUrl = "http://127.0.0.1:1" });
    typeof(CloudSyncClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(client, new HttpClient(proxy));
    typeof(CloudSyncClient).GetField("_directHttpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(client, new HttpClient(direct));
    return client;
}

static async Task<HttpResponseMessage> SendAsync(CloudSyncClient client, HttpMethod method, CancellationToken cancellationToken = default)
{
    var sendMethod = typeof(CloudSyncClient).GetMethod("SendAsyncWithFallback", BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, types: [typeof(HttpRequestMessage), typeof(CancellationToken)], modifiers: null)!;
    using var request = new HttpRequestMessage(method, "http://127.0.0.1:1/retry-test");
    var task = (Task<HttpResponseMessage>)sendMethod.Invoke(client, [request, cancellationToken])!;
    return await task;
}

static async Task ExpectTransportFailureAsync(CloudSyncClient client, HttpMethod method)
{
    try
    {
        using var _ = await SendAsync(client, method);
        throw new Exception($"{method} unexpectedly succeeded.");
    }
    catch (HttpRequestException)
    {
    }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class StubHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public bool Succeed { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Succeed)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        return Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated offline connection."));
    }
}
