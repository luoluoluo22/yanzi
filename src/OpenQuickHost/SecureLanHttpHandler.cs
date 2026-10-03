using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed class SecureLanHttpHandler : HttpMessageHandler
{
    private readonly HttpClient _transport = new(new SocketsHttpHandler { UseProxy = false,
        AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromMilliseconds(1500) }) { Timeout = TimeSpan.FromSeconds(90) };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var activePairs = YanziLanPairing.List();
        var peers = YanziPeerRegistry.List().Where(x => activePairs.Any(pair => pair.DeviceId == x.DeviceId) &&
            (x.Address == request.RequestUri!.Host && x.Port == request.RequestUri.Port ||
            x.Endpoints?.Any(e => e.Address == request.RequestUri!.Host && e.Port == request.RequestUri.Port && DateTimeOffset.UtcNow - e.VerifiedAt < TimeSpan.FromMinutes(10)) == true)).ToArray();
        if (peers.Length != 1) throw new HttpRequestException("explicit_peer_required");
        var pairs = YanziLanPairing.List().Where(x => x.DeviceId == peers[0].DeviceId).ToArray();
        if (pairs.Length != 1) throw new HttpRequestException("pair_required_or_rotation_pending");
        var pair = pairs[0]; var key = YanziLanPairing.Key(pair);
        var metadata = JsonSerializer.Serialize(new { source = DeviceIdentityStore.GetOrCreateDesktopDeviceId(), target = pair.DeviceId,
            requestId = Guid.NewGuid().ToString(), timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            path = request.RequestUri!.PathAndQuery, method = request.Method.Method,
            contentType = request.Content?.Headers.ContentType?.ToString() ?? "application/octet-stream",
            sha256 = request.Headers.TryGetValues("X-Content-Sha256", out var hashes) ? hashes.First() : "" });
        var nonce = RandomNumberGenerator.GetBytes(12);
        var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        using var encrypted = new HttpRequestMessage(HttpMethod.Post, new Uri(request.RequestUri, "/v1/lan/secure"));
        encrypted.Headers.Add("X-Yanzi-Pair", pair.PairId);
        encrypted.Headers.Add("X-Yanzi-Frame", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata)));
        encrypted.Headers.Add("X-Yanzi-Nonce", Convert.ToBase64String(nonce));
        encrypted.Content = new ByteArrayContent(YanziLanPairing.Encrypt(key, nonce, body, metadata));
        using var reply = await _transport.SendAsync(encrypted, cancellationToken);
        reply.EnsureSuccessStatusCode();
        int status = int.Parse(reply.Headers.GetValues("X-Yanzi-Status").Single());
        var output = YanziLanPairing.Decrypt(key, Convert.FromBase64String(reply.Headers.GetValues("X-Yanzi-Nonce").Single()),
            await reply.Content.ReadAsByteArrayAsync(cancellationToken), "response\n" + status + "\n" + metadata);
        return new((HttpStatusCode)status) { Content = new ByteArrayContent(output), RequestMessage = request };
    }
    protected override void Dispose(bool disposing) { if (disposing) _transport.Dispose(); base.Dispose(disposing); }
}
