using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>
/// Discovers desktop peers on the LAN. UDP announcements are untrusted and never
/// grant permissions; only a successful encrypted same-account handshake does.
/// </summary>
public static class YanziDesktopLanService
{
    private const int DiscoveryPort = 42980;
    private const string DiscoveryRequest = "YANZI_DISCOVER_REQUEST";
    private static readonly HttpClient Probes = new(new SocketsHttpHandler
    {
        UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(2)
    }) { Timeout = TimeSpan.FromSeconds(4) };

    public sealed record Computer(string DeviceId, string Name, string Address, int Port,
        bool Trusted, string Status)
    {
        public string Label => $"{Name}  ·  {Address}:{Port}  ·  {Status}";
    }

    public static async Task<IReadOnlyList<Computer>> DiscoverAsync(string? manualIp, CancellationToken ct = default)
    {
        if (manualIp is { Length: > 0 } &&
            (!IPAddress.TryParse(manualIp.Trim(), out var supplied) || !IsPrivateIPv4(supplied)))
            throw new ArgumentException("请输入同一局域网的有效 IPv4 地址，例如 192.168.1.4");

        var account = SyncSessionStore.Load()?.UserId;
        if (string.IsNullOrWhiteSpace(account))
            throw new InvalidOperationException("请先在燕子登录同一云账号，以便验证局域网设备身份。");

        // Refresh the existing server-issued, account-bound symmetric pair keys.
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var cloud = new CloudSyncClient(SyncConfigLoader.Load());
            try
            {
                await cloud.ReloadPersistedSessionAsync();
                await cloud.SyncAccountLanLinksAsync(DeviceIdentityStore.GetOrCreateDesktopDeviceId(), timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // Cached account keys may still be valid when the cloud is temporarily offline.
                HostAssets.AppendLog("Desktop LAN: cloud link refresh unavailable: " + ex.GetType().Name);
            }
        }

        var own = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        var pairIds = YanziLanPairing.List().Select(p => p.DeviceId).ToHashSet(StringComparer.Ordinal);
        var seen = new Dictionary<string, (string Name, IPAddress Ip, int Port)>(StringComparer.Ordinal);

        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var bytes = Encoding.UTF8.GetBytes(DiscoveryRequest);
        var targets = GetBroadcastTargets().ToList();
        if (!string.IsNullOrWhiteSpace(manualIp))
            targets.Insert(0, IPAddress.Parse(manualIp.Trim()));
        foreach (var target in targets.Distinct())
        {
            try { await udp.SendAsync(bytes, new IPEndPoint(target, DiscoveryPort), ct); }
            catch (SocketException) { /* Unsupported adapter or firewall; other adapters remain available. */ }
        }

        using var scanTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scanTimeout.CancelAfter(TimeSpan.FromMilliseconds(1750));
        while (!scanTimeout.IsCancellationRequested && seen.Count < 32)
        {
            UdpReceiveResult reply;
            try { reply = await udp.ReceiveAsync(scanTimeout.Token); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            if (!IsPrivateIPv4(reply.RemoteEndPoint.Address) || reply.Buffer.Length is < 8 or > 2048)
                continue;
            try
            {
                using var data = JsonDocument.Parse(reply.Buffer);
                var root = data.RootElement;
                if (!root.TryGetProperty("deviceId", out var idNode) ||
                    !root.TryGetProperty("device_id", out var nameNode) ||
                    !root.TryGetProperty("port", out var portNode))
                    continue;
                var id = idNode.GetString();
                var name = nameNode.GetString();
                if (string.IsNullOrWhiteSpace(id) || id == own || id.Length > 96 ||
                    string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
                    !portNode.TryGetInt32(out var port) || port is < 1 or > 65535)
                    continue;
                seen[id] = (name, reply.RemoteEndPoint.Address, port);
            }
            catch (JsonException) { /* A malformed UDP response is not an identity. */ }
        }

        var result = new List<Computer>();
        foreach (var (id, candidate) in seen)
        {
            if (!pairIds.Contains(id))
            {
                result.Add(new Computer(id, candidate.Name, candidate.Ip.ToString(), candidate.Port, false,
                    "未授权 · 请在两台电脑登录同一燕子账号"));
                continue;
            }
            var verified = await VerifyAsync(id, candidate.Ip, candidate.Port, ct);
            if (verified)
                YanziPeerRegistry.ObserveAuthenticated(id, candidate.Ip, candidate.Port, candidate.Name,
                    "desktop", "terminal", "chat");
            result.Add(new Computer(id, candidate.Name, candidate.Ip.ToString(), candidate.Port, verified,
                verified ? "局域网加密连接已验证" : "加密握手失败 · 检查防火墙或登录状态"));
        }
        return result.OrderByDescending(x => x.Trusted).ThenBy(x => x.Name).ToArray();
    }

    private static async Task<bool> VerifyAsync(string id, IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var handler = new SecureLanHttpHandler(id, ip.ToString(), port);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(
                $"http://{ip}:{port}/v1/me/devices/protocol?notificationPort={AppSettingsStore.LoadCached().AgentApiPort}&peerPlatform=desktop", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
            TaskCanceledException or System.Security.Cryptography.CryptographicException or
            InvalidOperationException)
        {
            HostAssets.AppendLog("Desktop LAN: encrypted probe failed for " + ip + ": " + ex.GetType().Name);
            return false;
        }
    }

    public static async Task<string> ExecuteAsync(Computer target, string command, CancellationToken ct = default)
    {
        if (!target.Trusted || target.DeviceId == DeviceIdentityStore.GetOrCreateDesktopDeviceId() ||
            YanziPeerRegistry.Resolve(target.DeviceId) is not { } verified ||
            verified.Address != target.Address || verified.Port != target.Port ||
            !YanziLanPairing.List().Any(p => p.DeviceId == target.DeviceId && p.Scopes.Contains("terminal")))
            throw new UnauthorizedAccessException("远程设备未经同账号加密认证，拒绝执行。");
        if (string.IsNullOrWhiteSpace(command) || command.Length > 4096)
            throw new ArgumentException("命令不能为空且最多 4096 字符。");

        using var handler = new SecureLanHttpHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(85) };
        var body = new
        {
            sourceDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            sourceDeviceName = Environment.MachineName,
            targetDeviceId = target.DeviceId,
            targetPlatform = "desktop",
            protocolVersion = YanziDeviceMessageProtocol.Version,
            kind = "run-powershell",
            title = "燕子局域网远程命令",
            text = command,
            clientMessageId = Guid.NewGuid().ToString("N"),
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O"),
            notificationPort = 42980
        };
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(
            $"http://{target.Address}:{target.Port}/v1/me/mobile/messages", content, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("远程命令被拒绝：" + json.RootElement);
        var root = json.RootElement;
        var ok = root.TryGetProperty("success", out var flag) && flag.GetBoolean();
        var output = root.TryGetProperty("output", out var text) ? text.GetString() ?? "" : "";
        return (ok ? "执行成功" : "执行失败") + Environment.NewLine + output;
    }

    private static IEnumerable<IPAddress> GetBroadcastTargets()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            foreach (var uni in network.GetIPProperties().UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork || uni.IPv4Mask == null ||
                    !IsPrivateIPv4(uni.Address)) continue;
                var ip = uni.Address.GetAddressBytes();
                var mask = uni.IPv4Mask.GetAddressBytes();
                yield return new IPAddress(ip.Zip(mask, (a, m) => (byte)(a | ~m)).ToArray());
            }
        }
        yield return IPAddress.Broadcast;
    }

    private static bool IsPrivateIPv4(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 ||
               b[0] == 192 && b[1] == 168;
    }
}
