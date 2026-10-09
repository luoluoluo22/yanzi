using System.Net;
using System.Reflection;
using System.Text.Json;
using OpenQuickHost;
using OpenQuickHost.Sync;

internal static class LoginRecoveryVerification
{
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "YanziLoginVerification-" + Guid.NewGuid().ToString("N"));
        using var scope = (IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root])!;
        var handler = new LoginHandler();
        var client = new CloudSyncClient(new SyncOptions { BaseUrl = "http://127.0.0.1:1" });
        typeof(CloudSyncClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1") });
        client.SetCredential("test@example.invalid", "verification-only-password", remember: false);
        // Restore re-enters EnsureAuthenticatedAsync while fetching the vault key.
        await client.EnsureAuthenticatedAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Require(handler.Logins == 1 && handler.VaultRequests == 1, "Login must finish and restore the vault without re-authenticating.");
        await client.RegisterDeviceAsync("verification-device", "desktop", "test");
        await client.RegisterDeviceAsync("verification-device", "desktop", "test", reactivateRemovedDevice: true);
        Require(handler.Registrations.SequenceEqual(new[] { false, true }), "Only explicit reconnect may reactivate a removed device.");
        await client.EnsureAuthenticatedAsync();
        Require(handler.Logins == 1, "An existing valid session must not log in again.");
        await client.LoginAsync("test@example.invalid", "verification-only-password").WaitAsync(TimeSpan.FromSeconds(8));
        Require(handler.Logins == 2, "Direct login must remain supported.");
        client.ClearSessionOnly();
        handler.DelayVault = true;
        // A different account forces a fresh recovery-key fetch rather than using the cache.
        handler.AccountId = "verification-timeout-account";
        await client.EnsureAuthenticatedAsync().WaitAsync(TimeSpan.FromSeconds(18));
        Require(client.CurrentUserId == handler.AccountId, "Vault timeout must preserve the successful login.");
        await client.EnsureAuthenticatedAsync().WaitAsync(TimeSpan.FromSeconds(2));
        client.ClearSessionOnly();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await client.EnsureAuthenticatedAsync(cancellation.Token);
            throw new Exception("Caller cancellation was swallowed.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Console.WriteLine("Login recovery verification passed: vault re-entry and timeout, caller cancellation, direct login, explicit device reactivation, bounded existing-session authentication.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class LoginHandler : HttpMessageHandler
    {
        internal int Logins;
        internal int VaultRequests;
        internal bool DelayVault;
        internal string AccountId = "verification-account";
        internal List<bool> Registrations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (path == "/v1/auth/login")
            {
                Logins++;
                body = new { accessToken = "verification-token", expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), userId = AccountId, username = "test", email = "test@example.invalid" };
            }
            else if (path == "/v1/sync/vault-key")
            {
                VaultRequests++;
                if (DelayVault) await Task.Delay(Timeout.Infinite, cancellationToken);
                body = new { ok = true, version = 1, key = Convert.ToBase64String(new byte[32]) };
            }
            else if (path.StartsWith("/v1/sync/objects/")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            else if (path == "/v1/me/devices")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Require(json.RootElement.GetProperty("deviceId").GetString() == "verification-device", "Reconnect must preserve device identity.");
                Registrations.Add(json.RootElement.GetProperty("reactivateRemovedDevice").GetBoolean());
                body = new { ok = true };
            }
            else throw new Exception("Unexpected verification request: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
