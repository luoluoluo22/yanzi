using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenQuickHost;

public sealed class LanDiscoveryService : IDisposable
{
    private const int DiscoveryPort = 42980;
    private const string DiscoverRequest = "YANZI_DISCOVER_REQUEST";
    private readonly UdpClient _udpClient;
    private readonly int _agentApiPort;
    private readonly string _agentApiToken;
    private CancellationTokenSource? _cts;

    public LanDiscoveryService(int agentApiPort, string agentApiToken)
    {
        _agentApiPort = agentApiPort;
        _agentApiToken = agentApiToken;
        _udpClient = new UdpClient(DiscoveryPort);
    }

    public void Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(_cts.Token));
            HostAssets.AppendLog($"LanDiscoveryService: Started on UDP port {DiscoveryPort}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"LanDiscoveryService: Failed to start on UDP port {DiscoveryPort} - {ex.Message}");
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        if (_udpClient == null) return;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(token);
                var requestText = Encoding.UTF8.GetString(result.Buffer);
                
                if (requestText == DiscoverRequest)
                {
                    var ip = GetLocalIpForRemote(result.RemoteEndPoint.Address);
                    if (!string.IsNullOrEmpty(ip))
                    {
                        var response = new
                        {
                            device_id = Environment.MachineName,
                            ip = ip,
                            port = _agentApiPort,
                            token = _agentApiToken
                        };
                        var responseJson = JsonSerializer.Serialize(response);
                        var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                        await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                        HostAssets.AppendLog($"LanDiscoveryService: Replied to {result.RemoteEndPoint} with {responseJson}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"LanDiscoveryService: Error in ListenAsync - {ex.Message}");
            }
        }
    }

    private static string GetLocalIpForRemote(IPAddress remoteIp)
    {
        try
        {
            // By "connecting" a UDP socket to the remote IP, the OS networking stack 
            // determines the correct local interface to use for routing.
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(remoteIp, 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch
        {
            // Fallback
        }

        // Fallback to DNS resolution if the socket approach fails
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    // Avoid known VPN/virtual ranges if possible
                    var ipStr = ip.ToString();
                    if (ipStr.StartsWith("198.18.") || ipStr.StartsWith("169.254.")) continue;
                    return ipStr;
                }
            }
        }
        catch
        {
            // Ignore
        }
        return string.Empty;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpClient?.Close();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _udpClient?.Dispose();
    }
}
