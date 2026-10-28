using OpenQuickHost;
using OpenQuickHost.Sync;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class SecureLanVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "YanziDev", "secure-lan", Guid.NewGuid().ToString("N"));
        using var isolated = (IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root])!;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var effects = 0; var checks = 0; string? authority = null;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        using (var vector = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "protocol/vectors/lan-aead-v1.json"))))
        {
            var v = vector.RootElement;
            var encrypted = YanziLanPairing.Encrypt(Convert.FromBase64String(v.GetProperty("key").GetString()!), Convert.FromBase64String(v.GetProperty("nonce").GetString()!),
                Encoding.UTF8.GetBytes(v.GetProperty("plaintext").GetString()!), v.GetProperty("aad").GetString()!);
            Check(Convert.ToBase64String(encrypted) == v.GetProperty("ciphertext").GetString(), "shared cross-platform AEAD vector");
        }
        using var server = new LocalAgentApiServer($"http://127.0.0.1:{port}/", "secure-lan-fixture", _ => {},
            onMobileMessage: message => { effects++; authority = message.Payload["authorization"].GetProperty("type").GetString(); return Task.FromResult((true, "encrypted-result")); });
        server.Start();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var grant = JsonSerializer.SerializeToElement(YanziLanPairing.Create("phone-test-alpha", "Test", ["chat"], port));
        var pairId = grant.GetProperty("pairId").GetString()!;
        var key = Convert.FromBase64String(grant.GetProperty("key").GetString()!);
        var own = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        string Frame(string route, string source = "phone-test-alpha", string? target = null, long? time = null) => JsonSerializer.Serialize(new {
            source, target = target ?? own, requestId = Guid.NewGuid().ToString(), timestamp = time ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            path = route, method = "POST", contentType = "application/json" });
        byte[] Body(string kind = "text", string source = "phone-test-alpha") => JsonSerializer.SerializeToUtf8Bytes(new {
            kind, sourceDeviceId = source, clientMessageId = Guid.NewGuid().ToString("N"), text = "secure fixture" });
        async Task<HttpResponseMessage> Send(string metadata, byte[] body, bool tamper = false)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var data = YanziLanPairing.Encrypt(key, nonce, body, metadata);
            if (tamper) data[0] ^= 1;
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/lan/secure");
            request.Headers.Add("X-Yanzi-Pair", pairId); request.Headers.Add("X-Yanzi-Frame", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata)));
            request.Headers.Add("X-Yanzi-Nonce", Convert.ToBase64String(nonce)); request.Content = new ByteArrayContent(data);
            return await http.SendAsync(request);
        }
        var metadata = Frame("/v1/me/mobile/messages");
        using (var result = await Send(metadata, Body()))
        {
            Check(result.IsSuccessStatusCode && effects == 1, "paired encrypted request executes");
            Check(authority == "lan-pair", "a scoped manual pair never gains account-owner authority");
            var plain = YanziLanPairing.Decrypt(key, Convert.FromBase64String(result.Headers.GetValues("X-Yanzi-Nonce").Single()),
                await result.Content.ReadAsByteArrayAsync(), "response\n200\n" + metadata);
            Check(Encoding.UTF8.GetString(plain).Contains("encrypted-result"), "response authenticated and encrypted");
        }
        using (var result = await Send(metadata, Body())) Check(result.StatusCode == HttpStatusCode.Unauthorized && effects == 1, "replay denied");
        using (var result = await Send(Frame("/v1/me/mobile/messages"), Body(), true)) Check(result.StatusCode == HttpStatusCode.Unauthorized && effects == 1, "tamper denied before effects");
        using (var result = await Send(Frame("/v1/me/mobile/messages", "phone-test-bravo"), Body())) Check(result.StatusCode == HttpStatusCode.Unauthorized, "sender impersonation denied");
        using (var result = await Send(Frame("/v1/me/mobile/messages", target: "desktop-other"), Body())) Check(result.StatusCode == HttpStatusCode.Unauthorized, "wrong target denied");
        using (var result = await Send(Frame("/v1/me/mobile/messages", time: DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds()), Body())) Check(result.StatusCode == HttpStatusCode.Unauthorized, "expired encrypted frame denied");
        using (var result = await Send(Frame("/v1/me/mobile/messages"), Body("RUN-SHELL"))) Check(result.StatusCode == HttpStatusCode.Forbidden && effects == 1, "case variant cannot bypass terminal grant");
        using (var result = await Send(Frame("/v1/me/mobile/messages"), Body("fs-read"))) Check(result.StatusCode == HttpStatusCode.Forbidden, "chat cannot read remote files");
        using (var result = await Send(Frame("/v1/lan/transfers/../../extensions/secret/run"), Body())) Check(!result.IsSuccessStatusCode, "path traversal cannot widen route scope");
        Check(YanziLanPairing.ClaimFrame(pairId, "00000000-0000-0000-0000-000000000001"), "frame replay receipt saved");
        Check(!YanziLanPairing.ClaimFrame(pairId, "00000000-0000-0000-0000-000000000001"), "durable receipt survives independent lookup");
        YanziLanPairing.Revoke(pairId);
        using (var result = await Send(Frame("/v1/me/mobile/messages"), Body())) Check(result.StatusCode == HttpStatusCode.Unauthorized && effects == 1, "revocation immediate");
        Console.WriteLine($"SECURE_LAN_AUTH_ENCRYPTION_SCOPES_REPLAY_AND_REVOCATION=PASSED; checks={checks}");
    }
}
