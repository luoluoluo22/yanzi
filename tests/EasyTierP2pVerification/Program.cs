using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Yanzi.Core.TemporaryTransfer;
using OpenQuickHost;

if (args.Contains("--network-name"))
{
    await Task.Delay(TimeSpan.FromMinutes(5));
    return;
}
if (args.Contains("-p"))
{
    var down = Environment.GetEnvironmentVariable("YANZI_P2P_TEST_RELAY") == "1";
    Console.WriteLine(down ? "[{\"hostname\":\"test-office\",\"cost\":\"relay(2)\",\"tunnel_proto\":\"\"}]"
        : "[{\"hostname\":\"test-office\",\"cost\":\"p2p\",\"tunnel_proto\":\"udp\"}]");
    return;
}

OpenQuickHost.HostAssets.TestRoot = Path.Combine(Path.GetTempPath(), "yanzi-p2p-test-config-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(OpenQuickHost.HostAssets.TestRoot);
YanziEasyTierSessionManager.SkipFirewallInIsolatedTests = true;
var failures = new List<string>();
void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (!ok) failures.Add(name);
}
static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop(); return port;
}
var temp = Path.Combine(Path.GetTempPath(), "yanzi-easytier-adapter-verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
using var proxy = new SocksTestProxy();
try
{
    var bytes = RandomNumberGenerator.GetBytes(1024 * 1024);
    var source = Path.Combine(temp, "p2p-test.bin");
    await File.WriteAllBytesAsync(source, bytes);
    var port = FreePort();
    var exe = Path.Combine(AppContext.BaseDirectory, "EasyTierP2pVerification.exe");
    var profile = TransferPolicy.BuiltIns.Single(x => x.Id == "easytier-p2p") with
    {
        Enabled = true, P2pCliPath = exe, P2pPeerName = "test-office",
        P2pRemoteIp = "127.0.0.1", P2pRpcPort = 15992,
        P2pSocksPort = proxy.Port, P2pListenPort = port
    };
    var plan = new TransferRequest(TransferEndpoint.Home, TransferEndpoint.Office,
        bytes.Length, PreferredChannel: "easytier-p2p");
    var secretPath = Path.Combine(OpenQuickHost.HostAssets.TestRoot, "session-secret.txt");
    await File.WriteAllTextAsync(secretPath, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)));
    var configPath = OpenQuickHost.HostAssets.ResolveDataDirectoryPath("temporary-transfer/easytier.json");
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    await File.WriteAllTextAsync(configPath, System.Text.Json.JsonSerializer.Serialize(new {
        corePath = exe, networkName = "yanzi-test-p2p", networkSecretFile = secretPath,
        localIpv4 = "10.233.10.1", coordinationPeers = new [] { "tcp://node.example.net:11010" },
        listenerPort = 23332, rpcPort = 15992, socksPort = proxy.Port }));
    YanziEasyTierSessionManager.Start(profile);
    Check("managed p2p-only session owns child", YanziEasyTierSessionManager.IsOwned(profile));
    var core = new TemporaryTransferCoordinator([profile],
        [new TemporaryEasyTierP2pAdapter(YanziEasyTierSessionManager.IsOwned)]);
    Check("opt-in direct is eligible", core.Plan(plan).Single().Eligible);
    Check("disabled by default", !TransferPolicy.BuiltIns.Single(x => x.Id == "easytier-p2p").Enabled);
    Check("requires configured peer", !TransferPolicy.Rank(plan, [profile with { P2pPeerName = null }]).Single().Eligible);

    var published = await core.PublishAsync(source, plan, CancellationToken.None);
    Check("issued short-lived opaque receipt", published.Receipt.Location.Length == 97
          && !published.Receipt.Location.Contains("127.0.0.1", StringComparison.Ordinal));
    using var unauthenticated = new HttpClient();
    using var denied = await unauthenticated.GetAsync("http://127.0.0.1:" + port + "/p2p/" +
        published.Receipt.Location[..32]);
    Check("missing secret rejected", denied.StatusCode == HttpStatusCode.NotFound);
    var inbox = Path.Combine(temp, "received");
    var result = await core.ReceiveAsync(published.Receipt, inbox, CancellationToken.None);
    Check("p2p SOCKS5 receive verified", result.Bytes == bytes.Length &&
        File.ReadAllBytes(result.FilePath).SequenceEqual(bytes));
    Check("sha256 matches", result.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)));
    var tampered = published.Receipt with { Location = published.Receipt.Location[..33] + new string('0', 64) };
    try
    {
        await core.ReceiveAsync(tampered, Path.Combine(temp, "tampered"), CancellationToken.None);
        Check("wrong transfer token rejected", false);
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException) { Check("wrong transfer token rejected", true); }
    Environment.SetEnvironmentVariable("YANZI_P2P_TEST_RELAY", "1");
    try
    {
        await core.PublishAsync(source, plan, CancellationToken.None);
        Check("relay path refused", false);
    }
    catch (IOException) { Check("relay path refused", true); }
    var fakeFallback = new FakeFallbackAdapter();
    var fakeSftp = TransferPolicy.BuiltIns.Single(x => x.Id == "sg-sftp") with
    {
        Enabled = true, Host = "example.net", Account = "tester",
        RemoteDirectory = "/transfer", KnownHostsFile = "pinned"
    };
    var options = new TemporaryTransferCoordinator([profile, fakeSftp],
        [new TemporaryEasyTierP2pAdapter(YanziEasyTierSessionManager.IsOwned), fakeFallback]);
    try
    {
        await options.PublishAsync(source, new TransferRequest(TransferEndpoint.Home,
            TransferEndpoint.Office, bytes.Length), CancellationToken.None);
        Check("direct failure never silently falls back", false);
    }
    catch (IOException) { Check("direct failure never silently falls back", !fakeFallback.Used); }
    finally { Environment.SetEnvironmentVariable("YANZI_P2P_TEST_RELAY", null); }
    YanziEasyTierSessionManager.Stop();
    Check("managed session stopped", !YanziEasyTierSessionManager.IsOwned(profile));
    try { await core.PublishAsync(source, plan, CancellationToken.None);
          Check("stopped session refuses transfer", false); }
    catch (IOException) { Check("stopped session refuses transfer", true); }
}
finally
{
    YanziEasyTierSessionManager.Stop();
    try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
    try { Directory.Delete(OpenQuickHost.HostAssets.TestRoot, recursive: true); } catch (IOException) { }
}
if (failures.Count > 0) throw new Exception("failed: " + string.Join(", ", failures));

sealed class SocksTestProxy : IDisposable
{
    private readonly TcpListener _server = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancel = new();
    public int Port { get; }
    public SocksTestProxy()
    {
        _server.Start();
        Port = ((IPEndPoint)_server.LocalEndpoint).Port;
        _ = Task.Run(Loop);
    }
    private async Task Loop()
    {
        while (!_cancel.IsCancellationRequested)
        {
            TcpClient connection;
            try { connection = await _server.AcceptTcpClientAsync(_cancel.Token); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            _ = Task.Run(() => Serve(connection));
        }
    }
    private async Task Serve(TcpClient socket)
    {
        using (socket)
        {
            try
            {
                var client = socket.GetStream();
                var header = new byte[2];
                await client.ReadExactlyAsync(header, _cancel.Token);
                var methods = new byte[header[1]];
                await client.ReadExactlyAsync(methods, _cancel.Token);
                await client.WriteAsync(new byte[] { 5, 0 }, _cancel.Token);
                var request = new byte[4];
                await client.ReadExactlyAsync(request, _cancel.Token);
                if (request[0] != 5 || request[1] != 1) return;
                string dest;
                if (request[3] == 1)
                {
                    var ip = new byte[4]; await client.ReadExactlyAsync(ip, _cancel.Token);
                    dest = new IPAddress(ip).ToString();
                }
                else if (request[3] == 3)
                {
                    var length = new byte[1]; await client.ReadExactlyAsync(length, _cancel.Token);
                    var host = new byte[length[0]];
                    await client.ReadExactlyAsync(host, _cancel.Token);
                    dest = System.Text.Encoding.ASCII.GetString(host);
                }
                else return;
                var portBytes = new byte[2]; await client.ReadExactlyAsync(portBytes, _cancel.Token);
                int port = (portBytes[0] << 8) | portBytes[1];
                using var target = new TcpClient();
                await target.ConnectAsync(dest, port, _cancel.Token);
                await client.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 }, _cancel.Token);
                var remote = target.GetStream();
                await Task.WhenAny(client.CopyToAsync(remote, _cancel.Token),
                                   remote.CopyToAsync(client, _cancel.Token));
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
            { }
        }
    }
    public void Dispose() { _cancel.Cancel(); _server.Stop(); _cancel.Dispose(); }
}

namespace OpenQuickHost
{
    internal static class HostAssets
    {
        internal static string TestRoot = "";
        public static string ResolveDataDirectoryPath(string relative) =>
            Path.Combine(TestRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}

sealed class FakeFallbackAdapter : ITemporaryTransferAdapter
{
    public string ChannelId => "sg-sftp";
    public bool Used { get; private set; }
    public Task<TransferReceipt> PublishAsync(TransferChannel channel, string source, string hash, CancellationToken token)
    {
        Used = true;
        return Task.FromResult(new TransferReceipt(channel.Id, Path.GetFileName(source),
            new FileInfo(source).Length, hash, "cloud", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(20)));
    }
    public Task DownloadAsync(TransferChannel channel, TransferReceipt receipt, string path, CancellationToken token)
        => throw new NotSupportedException();
}