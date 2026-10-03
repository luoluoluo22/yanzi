using System.Net;
using System.Net.Sockets;
using OpenQuickHost;

internal static class LocalApiBoundaryVerification
{
    public static async Task RunAsync()
    {
        using var root = HostAssets.UseIsolatedDataRootForVerification(Path.Combine(Path.GetTempPath(), "YanziDev", "local-api", Guid.NewGuid().ToString("N")));
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        const string token = "isolated-local-api-verification-token";
        var mutations = 0;
        using var server = new LocalAgentApiServer($"http://127.0.0.1:{port}/", token, _ => mutations++);
        server.Start();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        foreach (var path in new[] { "/v1/settings", "/v1/me/devices/protocol", "/v1/extensions", "/v1/quickpanel/groups", "/v1/sync/webdav-config" })
        {
            using var denied = await client.GetAsync(path);
            Check(denied.StatusCode == HttpStatusCode.Unauthorized, "Unauthenticated route bypass: " + path);
        }
        using (var options = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/v1/settings")))
            Check(options.StatusCode == HttpStatusCode.NoContent, "Preflight authentication regression");
        var docs = await client.GetStringAsync("/docs");
        Check(!docs.Contains(token, StringComparison.Ordinal), "Public docs leaked API token");
        client.DefaultRequestHeaders.Authorization = new("Bearer", "wrong-token");
        using (var denied = await client.PostAsync("/v1/quickpanel/groups", new StringContent("{}")))
            Check(denied.StatusCode == HttpStatusCode.Unauthorized, "Wrong token dispatched a mutating route");
        Check(mutations == 0, "Denied request mutated state");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        foreach (var path in new[] { "/v1/settings", "/v1/me/devices/protocol", "/v1/quickpanel/groups", "/v1/sync/webdav-config" })
        {
            using var accepted = await client.GetAsync(path);
            Check(accepted.StatusCode == HttpStatusCode.OK, "Authenticated domain not dispatched: " + path);
        }
        using (var missing = await client.GetAsync("/v1/no-such-route"))
            Check(missing.StatusCode == HttpStatusCode.NotFound, "Unknown route status changed");
        Console.WriteLine($"LOCAL_API_DOMAIN_AUTH_BOUNDARIES=PASSED; checks={checks}");
    }
}
