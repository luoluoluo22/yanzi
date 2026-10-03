using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private static readonly ConcurrentDictionary<string, long> LanReplay = new();
    private static readonly SemaphoreSlim SecureLanCapacity = new(4);
    private async Task<bool> TrySecureLanAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (path != "/v1/lan/secure") return false;
        if (request.HttpMethod != "POST" || request.ContentLength64 is < 16 or > 31458000)
        { await WriteJsonAsync(response, 400, new { error = "invalid_encrypted_frame" }); return true; }
        if (!await SecureLanCapacity.WaitAsync(0))
        { await WriteJsonAsync(response, 429, new { error = "lan_backpressure" }); return true; }
        try
        {
            var pairId = request.Headers["X-Yanzi-Pair"] ?? "";
            var pair = YanziLanPairing.Find(pairId);
            if (pair == null) { await WriteJsonAsync(response, 401, new { error = "pair_required" }); return true; }
            var metaText = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers["X-Yanzi-Frame"] ?? ""));
            using var meta = JsonDocument.Parse(metaText);
            var frame = meta.RootElement;
            var source = frame.GetProperty("source").GetString();
            var target = frame.GetProperty("target").GetString();
            var requestId = frame.GetProperty("requestId").GetString()!;
            var stamp = frame.GetProperty("timestamp").GetInt64();
            var route = frame.GetProperty("path").GetString()!;
            var method = frame.GetProperty("method").GetString()!;
            if (source != pair.DeviceId || target != DeviceIdentityStore.GetOrCreateDesktopDeviceId() ||
                !Guid.TryParse(requestId, out _) || (stamp < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 120000 || stamp > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 120000) ||
                !route.StartsWith("/v1/", StringComparison.Ordinal) || route.Contains('\r') || route.Contains('\n') ||
                !new[] {"POST", "GET", "PUT", "DELETE"}.Contains(method)) throw new CryptographicException("invalid_frame_identity");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(95));
            using var buffer = new MemoryStream();
            byte[] bytes = new byte[65536]; int count;
            while ((count = await request.InputStream.ReadAsync(bytes, timeout.Token)) > 0)
            { await buffer.WriteAsync(bytes.AsMemory(0, count), timeout.Token); if (buffer.Length > 31458000) throw new IOException("frame_too_large"); }
            var key = YanziLanPairing.Key(pair);
            var plaintext = YanziLanPairing.Decrypt(key, Convert.FromBase64String(request.Headers["X-Yanzi-Nonce"] ?? ""), buffer.ToArray(), metaText);
            var replayId = pairId + ":" + requestId;
            foreach (var old in LanReplay.Where(x => x.Value < stamp - 120000).ToArray()) LanReplay.TryRemove(old.Key, out _);
            if (!LanReplay.TryAdd(replayId, stamp) || !YanziLanPairing.ClaimFrame(pairId, requestId)) throw new CryptographicException("frame_replayed");
            var canonical = new Uri("http://127.0.0.1" + route);
            if (canonical.Host != "127.0.0.1" || canonical.AbsolutePath != route.Split('?')[0] || route.Contains('#'))
                throw new CryptographicException("invalid_frame_route");
            bool permitted = false;
            if (System.Text.RegularExpressions.Regex.IsMatch(canonical.AbsolutePath,"^/v1/companion/transfer-sessions/[a-f0-9]{32}(?:/(?:commit|blocks/[0-9]{1,2}))?$")) permitted=pair.Scopes.Contains("account.owner");
            if (System.Text.RegularExpressions.Regex.IsMatch(canonical.AbsolutePath, "^/v1/companion/files/[a-f0-9]{32}$")) permitted = pair.Scopes.Contains("account.owner");
            string? capabilityGrant = null;
            if (System.Text.RegularExpressions.Regex.IsMatch(route, "^/v1/lan/transfers/[a-f0-9]{32}(\\?|$)")) permitted = pair.Scopes.Contains("attachments");
            else if (System.Text.RegularExpressions.Regex.IsMatch(route, "^/v1/lan/transfer-sessions/[a-f0-9]{32}(?:/(?:commit|blocks/[0-9]{1,2}))?$")) permitted = pair.Scopes.Contains("attachments");
            else if (canonical.AbsolutePath == "/v1/me/devices/protocol" && method == "GET") permitted = true;
            else if (route == "/v1/me/mobile/messages" && method == "POST")
            {
                using var message = JsonDocument.Parse(plaintext);
                if (GetString(message.RootElement, "sourceDeviceId") != pair.DeviceId) throw new CryptographicException("sender_mismatch");
                var kind = (GetString(message.RootElement, "kind") ?? "text").ToLowerInvariant();
                if (kind == "capability.invoke")
                {
                    var name = message.RootElement.GetProperty("payload").GetProperty("name").GetString() ?? "";
                    if (pair.Scopes.Contains("capability:" + name) || pair.Scopes.Contains("account.owner")) capabilityGrant = name;
                }
                permitted = capabilityGrant != null || (kind.StartsWith("fs-", StringComparison.Ordinal) ? pair.Scopes.Contains("files.remote") :
                    kind is "run-shell" or "run-powershell" ? pair.Scopes.Contains("terminal") :
                    kind == "run-extension" ? pair.Scopes.Contains("extensions.run") :
                    !YanziDeviceMessageProtocol.IsExecution(kind) && pair.Scopes.Contains("chat"));
            }
            if (!permitted) { await WriteJsonAsync(response, 403, new { error = "pair_scope_denied" }); return true; }
            if (route.StartsWith("/v1/lan/transfers/", StringComparison.Ordinal))
            {
                var query = new Uri("http://localhost" + route).Query;
                if (System.Web.HttpUtility.ParseQueryString(query)["sourceDeviceId"] != pair.DeviceId)
                    throw new CryptographicException("sender_mismatch");
            }
            if (request.RemoteEndPoint is { } remote)
            {
                int port = YanziPeerRegistry.List().FirstOrDefault(x => x.DeviceId == pair.DeviceId)?.Port ?? 42981;
                if (canonical.AbsolutePath == "/v1/me/devices/protocol")
                    int.TryParse(System.Web.HttpUtility.ParseQueryString(canonical.Query)["notificationPort"], out port);
                if (route.StartsWith("/v1/lan/transfers/", StringComparison.Ordinal))
                    int.TryParse(System.Web.HttpUtility.ParseQueryString(new Uri("http://localhost" + route).Query)["notificationPort"], out port);
                else if (route == "/v1/me/mobile/messages")
                {
                    using var envelope = JsonDocument.Parse(plaintext);
                    if (envelope.RootElement.TryGetProperty("notificationPort", out var advertised)) port = advertised.GetInt32();
                }
                else if (method == "POST" && System.Text.RegularExpressions.Regex.IsMatch(route, "^/v1/lan/transfer-sessions/[a-f0-9]{32}$"))
                {
                    using var manifest = JsonDocument.Parse(plaintext);
                    if (manifest.RootElement.TryGetProperty("notificationPort", out var advertised)) port = advertised.GetInt32();
                }
                YanziPeerRegistry.ObserveAuthenticated(pair.DeviceId, remote.Address, port > 0 ? port : 42981, pair.DisplayName);
            }
            using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
            using var forward = new HttpRequestMessage(new HttpMethod(method), $"http://127.0.0.1:{request.LocalEndPoint.Port}" + route);
            forward.Headers.Authorization = new("Bearer", _token);
            forward.Headers.Add("X-Yanzi-Peer-Device", pair.DeviceId);
            forward.Headers.Add("X-Yanzi-Pair-Authority", pair.Scopes.Contains("account.owner") ? "account-owner" : "lan-pair");
            if (capabilityGrant != null) forward.Headers.Add("X-Yanzi-Capability-Grant", capabilityGrant);
            forward.Content = new ByteArrayContent(plaintext);
            forward.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(frame.GetProperty("contentType").GetString()!);
            if (frame.TryGetProperty("sha256", out var hash)) forward.Headers.TryAddWithoutValidation("X-Content-Sha256", hash.GetString());
            using var result = await client.SendAsync(forward, timeout.Token);
            var output = await result.Content.ReadAsByteArrayAsync(timeout.Token);
            var nonce = RandomNumberGenerator.GetBytes(12);
            int status = (int)result.StatusCode;
            response.Headers["X-Yanzi-Status"] = status.ToString();
            response.Headers["X-Yanzi-Nonce"] = Convert.ToBase64String(nonce);
            response.ContentType = "application/octet-stream";
            var encrypted = YanziLanPairing.Encrypt(key, nonce, output, "response\n" + status + "\n" + metaText);
            response.ContentLength64 = encrypted.Length;
            await response.OutputStream.WriteAsync(encrypted, timeout.Token);
            response.Close();
        }
        catch (Exception error) when (error is CryptographicException or FormatException or JsonException or InvalidOperationException)
        { await WriteJsonAsync(response, 401, new { error = "invalid_encrypted_frame" }); }
        finally { SecureLanCapacity.Release(); }
        return true;
    }
}
