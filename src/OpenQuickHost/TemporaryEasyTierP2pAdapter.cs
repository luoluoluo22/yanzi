using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yanzi.Core.TemporaryTransfer;

namespace OpenQuickHost;

/// <summary>
/// Explicit opt-in EasyTier P2P adapter. It never uses a public upload API. Both
/// endpoints must run EasyTier with --p2p-only true --enable-kcp-proxy true.
/// The receiver rechecks p2p/udp before and after downloading.
/// </summary>
internal sealed class TemporaryEasyTierP2pAdapter : ITemporaryTransferAdapter
{
    private sealed record SharedFile(string Source, long Bytes, string Hash, string Secret,
        DateTimeOffset ExpiresAt);
    private static readonly ConcurrentDictionary<string, SharedFile> Shares = new(StringComparer.Ordinal);
    private static readonly object ServerLock = new();
    private static readonly SemaphoreSlim AcceptLimit = new(8, 8);
    private static TcpListener? _listener;
    private static int _listenerPort;

    private readonly Func<TransferChannel, bool> _managedSession;
    public TemporaryEasyTierP2pAdapter(Func<TransferChannel, bool> managedSession) =>
        _managedSession = managedSession;
    public string ChannelId => "easytier-p2p";

    private void Validate(TransferChannel c, bool receiver)
    {
        if (!_managedSession(c)) throw new InvalidOperationException("easytier_managed_p2p_only_session_required");
        if (c.Kind != ChannelKind.DirectP2p ||
            string.IsNullOrWhiteSpace(c.P2pCliPath) || !File.Exists(c.P2pCliPath) ||
            string.IsNullOrWhiteSpace(c.P2pPeerName) ||
            c.P2pRpcPort is < 1024 or > 65535 ||
            c.P2pListenPort is < 1024 or > 65535 ||
            c.P2pSocksPort is < 1024 or > 65535)
            throw new InvalidOperationException("easytier_p2p_not_configured");

        if (receiver && (!IPAddress.TryParse(c.P2pRemoteIp, out var ip) ||
                         ip.AddressFamily != AddressFamily.InterNetwork ||
                         !IsPrivate(ip)))
            throw new InvalidOperationException("easytier_overlay_ip_invalid");
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return IPAddress.IsLoopback(ip) || b[0] == 10 ||
               (b[0] == 172 && b[1] is >= 16 and <= 31) ||
               (b[0] == 192 && b[1] == 168);
    }

    private async Task EnsureDirectAsync(TransferChannel c, CancellationToken token)
    {
        Validate(c, receiver: false);
        var start = new ProcessStartInfo(c.P2pCliPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add("127.0.0.1:" + c.P2pRpcPort);
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("json");
        start.ArgumentList.Add("peer");
        using var p = Process.Start(start) ?? throw new IOException("easytier_cli_start_failed");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var output = p.StandardOutput.ReadToEndAsync(deadline.Token);
            var errors = p.StandardError.ReadToEndAsync(deadline.Token);
            await p.WaitForExitAsync(deadline.Token);
            var json = await output;
            _ = await errors;
            if (p.ExitCode != 0) throw new IOException("easytier_peer_query_failed");
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                throw new IOException("easytier_peer_list_invalid");
            foreach (var peer in parsed.RootElement.EnumerateArray())
            {
                if (peer.ValueKind != JsonValueKind.Object) continue;
                static string? Read(JsonElement node, string key) =>
                    node.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() : null;
                if (Read(peer, "hostname") == c.P2pPeerName &&
                    Read(peer, "cost") == "p2p" &&
                    Read(peer, "tunnel_proto") == "udp")
                    return;
            }
            throw new IOException("easytier_direct_udp_unavailable");
        }
        catch
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            throw;
        }
    }

    internal static void StopServer()
    {
        lock (ServerLock)
        {
            _listener?.Stop();
            _listener = null;
            _listenerPort = 0;
            Shares.Clear();
        }
    }

    internal async Task<bool> IsDirectAsync(TransferChannel profile, CancellationToken token)
    {
        try { await EnsureDirectAsync(profile, token); return true; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                   OperationCanceledException or JsonException or System.ComponentModel.Win32Exception)
        { return false; }
    }
    private static void StartServer(int port)
    {
        lock (ServerLock)
        {
            if (_listener != null)
            {
                if (_listenerPort != port) throw new InvalidOperationException("p2p_listener_port_conflict");
                return;
            }
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start(16);
            _listener = listener;
            _listenerPort = port;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(); }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }
                    if (!await AcceptLimit.WaitAsync(0)) { client.Dispose(); continue; }
                    _ = Task.Run(async () =>
                    {
                        try { await ServeAsync(client); }
                        finally { AcceptLimit.Release(); }
                    });
                }
            });
        }
    }

    private static async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = 7000;
            client.SendTimeout = 30000;
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var first = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(7));
                var match = Regex.Match(first ?? "", @"^GET /p2p/([a-f0-9]{32}) HTTP/1\.[01]$");
                string? token = null;
                string? rangeHeader = null;
                var headerBytes = (first?.Length ?? 0) + 2;
                for (int i = 0; i < 48; i++)
                {
                    var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(7));
                    if (line is null) return;
                    headerBytes += line.Length + 2;
                    if (headerBytes > 8192) return;
                    if (line.Length == 0) break;
                    if (line.StartsWith("x-yanzi-p2p-token:", StringComparison.OrdinalIgnoreCase))
                        token = line["x-yanzi-p2p-token:".Length..].Trim();
                    if (line.StartsWith("range:", StringComparison.OrdinalIgnoreCase))
                        rangeHeader = line["range:".Length..].Trim();
                }
                if (!match.Success || !Shares.TryGetValue(match.Groups[1].Value, out var share) ||
                    share.ExpiresAt <= DateTimeOffset.UtcNow || token is null ||
                    !Regex.IsMatch(token, "^[a-f0-9]{64}$") ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(share.Secret)))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                    return;
                }
                await using var file = new FileStream(share.Source, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (file.Length != share.Bytes) return;
                long offset = 0;
                if (rangeHeader != null)
                {
                    var range = Regex.Match(rangeHeader, @"^bytes=(0|[1-9][0-9]*)-$");
                    if (!range.Success || !long.TryParse(range.Groups[1].Value, out offset) ||
                        offset >= share.Bytes)
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                            "HTTP/1.1 416 Range Not Satisfiable\r\nContent-Length: 0\r\n" +
                            "Content-Range: bytes */" + share.Bytes + "\r\nConnection: close\r\n\r\n"));
                        return;
                    }
                }
                var partial = rangeHeader != null;
                var length = share.Bytes - offset;
                var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " +
                    (partial ? "206 Partial Content" : "200 OK") +
                    "\r\nContent-Type: application/octet-stream\r\n" +
                    "Content-Length: " + length + "\r\n" +
                    (partial ? "Content-Range: bytes " + offset + "-" +
                        (share.Bytes - 1) + "/" + share.Bytes + "\r\n" : "") +
                    "Accept-Ranges: bytes\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                file.Seek(offset, SeekOrigin.Begin);
                await stream.WriteAsync(headers);
                await file.CopyToAsync(stream, 131072);
            }
            catch (Exception ex) when (ex is IOException or SocketException or TimeoutException
                                       or ObjectDisposedException or InvalidOperationException)
            { /* A disconnected peer must not affect the host process. */ }
        }
    }

    public async Task<TransferReceipt> PublishAsync(TransferChannel channel, string path,
        string sha256, CancellationToken token)
    {
        Validate(channel, receiver: false);
        await EnsureDirectAsync(channel, token);
        StartServer(channel.P2pListenPort);
        var id = Guid.NewGuid().ToString("N");
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var expiry = DateTimeOffset.UtcNow.AddMinutes(90);
        foreach (var (key, value) in Shares)
            if (value.ExpiresAt <= DateTimeOffset.UtcNow) Shares.TryRemove(key, out _);
        var size = new FileInfo(path).Length;
        Shares[id] = new SharedFile(path, size, sha256, secret, expiry);
        return new TransferReceipt(ChannelId, Path.GetFileName(path), size, sha256,
            id + ":" + secret, DateTimeOffset.UtcNow, expiry, "easytier_p2p_udp_kcp_no_relay");
    }

    public async Task DownloadAsync(TransferChannel channel, TransferReceipt receipt,
        string temporaryPath, CancellationToken token)
    {
        Validate(channel, receiver: true);
        var match = Regex.Match(receipt.Location, @"^([a-f0-9]{32}):([a-f0-9]{64})$");
        if (!match.Success) throw new InvalidDataException("p2p_receipt_invalid");
        await EnsureDirectAsync(channel, token);
        using var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("socks5://127.0.0.1:" + channel.P2pSocksPort),
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var url = "http://" + channel.P2pRemoteIp + ":" + channel.P2pListenPort +
                  "/p2p/" + match.Groups[1].Value;
        var existing = File.Exists(temporaryPath) ? new FileInfo(temporaryPath).Length : 0;
        if (existing > receipt.Bytes) throw new InvalidDataException("p2p_part_exceeds_expected_size");
        if (existing < receipt.Bytes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("x-yanzi-p2p-token", match.Groups[2].Value);
            if (existing > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (existing == 0 && response.StatusCode != HttpStatusCode.OK ||
                existing > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                throw new InvalidDataException("p2p_range_response_status_invalid");
            if (existing > 0)
            {
                var contentRange = response.Content.Headers.ContentRange;
                if (contentRange?.From != existing || contentRange.To != receipt.Bytes - 1 ||
                    contentRange.Length != receipt.Bytes)
                    throw new InvalidDataException("p2p_range_response_mismatch");
            }
            if (response.Content.Headers.ContentLength != receipt.Bytes - existing)
                throw new InvalidDataException("p2p_content_length_mismatch");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(temporaryPath,
                existing > 0 ? FileMode.Open : FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
            if (output.Length != existing) throw new IOException("p2p_part_changed_concurrently");
            output.Position = existing;
            var buffer = new byte[131072];
            long received = existing;
            int count;
            while ((count = await input.ReadAsync(buffer, token)) > 0)
            {
                received += count;
                if (received > receipt.Bytes) throw new InvalidDataException("p2p_exceeds_expected_size");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            if (received != receipt.Bytes) throw new IOException("p2p_short_read");
        }
                await EnsureDirectAsync(channel, token);
    }
}
