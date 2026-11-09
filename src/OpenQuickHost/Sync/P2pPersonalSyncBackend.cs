using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenQuickHost.Sync;

internal sealed class P2pPersonalSyncBackend : PersonalSyncBackendBase
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(3)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly PersonalSyncP2pConfig _config;

    public P2pPersonalSyncBackend(PersonalSyncP2pConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public override string DisplayRoot => string.IsNullOrWhiteSpace(_config.TargetPeerIp)
        ? "P2P 局域网/组网自动发现"
        : $"p2p://{_config.TargetPeerIp}:{_config.Port}";

    private async Task<string> ResolveTargetAddressAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_config.TargetPeerIp))
        {
            return $"http://{_config.TargetPeerIp.Trim()}:{_config.Port}";
        }

        // 尝试自动发现 EasyTier 组网中的第一个在线对端
        var discoveredIp = await DiscoverPeerIpAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(discoveredIp))
        {
            return $"http://{discoveredIp}:{_config.Port}";
        }

        throw new InvalidOperationException("未找到在线的 P2P 节点，请检查目标设备是否在线或手动输入目标设备 IP。");
    }

    private void ApplyAuthHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_config.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.Token);
            request.Headers.TryAddWithoutValidation("X-Yanzi-Token", _config.Token);
        }
    }

    public override async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var baseUrl = await ResolveTargetAddressAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1/health");
        ApplyAuthHeaders(request);

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"P2P 节点响应异常: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"无法连接到 P2P 目标设备 ({baseUrl}): {ex.Message}", ex);
        }
    }

    public override async Task<byte[]?> TryReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var baseUrl = await ResolveTargetAddressAsync(cancellationToken);
        var normalized = NormalizeRelativePath(relativePath);
        var url = $"{baseUrl}/v1/sync/personal/file?path={Uri.EscapeDataString(normalized)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthHeaders(request);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public override async Task WriteBytesAsync(string relativePath, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var baseUrl = await ResolveTargetAddressAsync(cancellationToken);
        var normalized = NormalizeRelativePath(relativePath);
        var url = $"{baseUrl}/v1/sync/personal/file?path={Uri.EscapeDataString(normalized)}";

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuthHeaders(request);

        request.Content = new ByteArrayContent(content ?? Array.Empty<byte>());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public override async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        var baseUrl = await ResolveTargetAddressAsync(cancellationToken);
        var normalized = NormalizeRelativePath(relativePath);
        var url = $"{baseUrl}/v1/sync/personal/file?path={Uri.EscapeDataString(normalized)}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        ApplyAuthHeaders(request);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public static async Task<string?> DiscoverPeerIpAsync(CancellationToken cancellationToken = default)
    {
        // 1. 尝试从本地运行的 EasyTier 进程中查询对端 IP
        foreach (int port in new[] { 15992, 15994 })
        {
            try
            {
                var ip = await QueryPeersFromEasyTierCliAsync(port, cancellationToken);
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return ip;
                }
            }
            catch
            {
            }
        }

        // 2. 兜底尝试通过局域网配对列表查找
        try
        {
            var lanIp = QueryPeersFromLanPairing();
            if (!string.IsNullOrWhiteSpace(lanIp))
            {
                return lanIp;
            }
        }
        catch
        {
        }

        return null;
    }

    private static async Task<string?> QueryPeersFromEasyTierCliAsync(int port, CancellationToken cancellationToken)
    {
        string? cliPath = null;
        try
        {
            var (_, cli) = await YanziPortableEasyTierInstaller.EnsureAsync(cancellationToken);
            if (File.Exists(cli))
            {
                cliPath = cli;
            }
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(cliPath))
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost", "Runtimes", "EasyTier", "2.6.4", "easytier-windows-x86_64", "easytier-cli.exe");
            if (File.Exists(fallback))
            {
                cliPath = fallback;
            }
        }

        if (string.IsNullOrWhiteSpace(cliPath))
        {
            return null;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo(cliPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add($"127.0.0.1:{port}");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("peer");

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null) return null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(4));

        var outputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        await process.WaitForExitAsync(linkedCts.Token);
        var json = await outputTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json)) return null;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var cost = element.TryGetProperty("cost", out var c) ? c.GetString() : null;
            var ipv4 = element.TryGetProperty("ipv4", out var ip) ? ip.GetString() : null;

            // 过滤掉本机自身（cost=Local）和公共中继节点（ipv4为空）
            if (string.Equals(cost, "Local", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(ipv4))
            {
                var cleanIp = ipv4.Split('/')[0].Trim();
                if (System.Net.IPAddress.TryParse(cleanIp, out _))
                {
                    return cleanIp;
                }
            }
        }

        return null;
    }

    private static string? QueryPeersFromLanPairing()
    {
        var activePairs = YanziLanPairing.List();
        if (activePairs.Count > 0)
        {
            var firstPeer = YanziPeerRegistry.List().FirstOrDefault(x => activePairs.Any(p => p.DeviceId == x.DeviceId));
            if (firstPeer != null && !string.IsNullOrWhiteSpace(firstPeer.Address))
            {
                return firstPeer.Address;
            }
        }
        return null;
    }
}
