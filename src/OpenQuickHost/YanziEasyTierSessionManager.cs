using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yanzi.Core.TemporaryTransfer;

namespace OpenQuickHost;

/// <summary>
/// Owns only the EasyTier process explicitly started through this opt-in feature.
/// No system routes, other EasyTier instances, or proxy settings are modified.
/// </summary>
internal static class YanziEasyTierSessionManager
{
    private sealed record Options(
        string CorePath, string NetworkName, string NetworkSecretFile,
        string LocalIpv4, string[] CoordinationPeers, int ListenerPort = 23332,
        int RpcPort = 15992, int SocksPort = 19089);
    private static readonly object Gate = new();
    private static Process? _owned;
    private static int _rpcPort;
    private static int _socksPort;
    private static string? _firewallRule;
    internal static bool SkipFirewallInIsolatedTests { get; set; }

    static YanziEasyTierSessionManager()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    private static void RunFirewall(string verb, string rule, int port = 0)
    {
        if (SkipFirewallInIsolatedTests) return;
        var psi = new ProcessStartInfo("netsh.exe") { UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var item in verb == "add" ? new[] { "advfirewall", "firewall", "add", "rule",
            "name=" + rule, "dir=in", "action=block", "protocol=TCP",
            "localport=" + port, "profile=any" }
            : new[] { "advfirewall", "firewall", "delete", "rule", "name=" + rule })
            psi.ArgumentList.Add(item);
        using var process = Process.Start(psi) ?? throw new IOException("firewall_process_failed");
        _ = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5000) || process.ExitCode != 0)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("p2p_socks5_inbound_firewall_required");
        }
    }

    internal static bool IsOwned(TransferChannel profile)
    {
        lock (Gate)
            return _owned is { HasExited: false } &&
                   _rpcPort == profile.P2pRpcPort &&
                   _socksPort == profile.P2pSocksPort;
    }

    internal static object Start(TransferChannel profile)
    {
        if (!profile.Enabled || profile.Kind != ChannelKind.DirectP2p)
            throw new InvalidOperationException("p2p_channel_not_enabled");
        var path = HostAssets.ResolveDataDirectoryPath("temporary-transfer/easytier.json");
        if (!File.Exists(path)) throw new InvalidOperationException("p2p_session_config_missing");
        var o = JsonSerializer.Deserialize<Options>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("p2p_session_config_invalid");
        if (o.RpcPort != profile.P2pRpcPort || o.SocksPort != profile.P2pSocksPort ||
            !File.Exists(o.CorePath) || !File.Exists(o.NetworkSecretFile) ||
            !File.Exists(profile.P2pCliPath) ||
            !Regex.IsMatch(o.NetworkName, @"^[a-zA-Z0-9_-]{8,60}$") ||
            !IPAddress.TryParse(o.LocalIpv4, out var local) ||
            local.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            o.ListenerPort is < 1024 or > 65535 ||
            o.CoordinationPeers is null or { Length: < 1 or > 5 } ||
            o.CoordinationPeers.Any(p => !Regex.IsMatch(p,
                @"^(?:tcp|udp)://[a-zA-Z0-9.-]{1,253}:[0-9]{2,5}$")))
            throw new InvalidDataException("p2p_session_config_invalid");
        var secret = File.ReadAllText(o.NetworkSecretFile).Trim();
        if (secret.Length is < 24 or > 128 || secret.Any(char.IsWhiteSpace))
            throw new InvalidDataException("p2p_network_secret_invalid");
        lock (Gate)
        {
            if (_owned is { HasExited: false })
            {
                if (_rpcPort != o.RpcPort || _socksPort != o.SocksPort)
                    throw new InvalidOperationException("p2p_session_already_running_different_ports");
                return new { started = false, alreadyRunning = true,
                    direct = false, note = "Use files.p2p.status to confirm the peer route." };
            }
            var psi = new ProcessStartInfo(o.CorePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = false, RedirectStandardError = false
            };
            // The secret is not passed on the process command line or written to logs.
            psi.Environment["ET_NETWORK_SECRET"] = secret;
            string[] args =
            [
                "--network-name", o.NetworkName,
                "--hostname", Environment.MachineName,
                "-i", o.LocalIpv4,
                "--no-tun", "true",
                "--p2p-only", "true",
                "--need-p2p", "true",
                "--enable-kcp-proxy", "true",
                "--bind-device", "true",
                "--disable-ipv6", "true",
                "--rpc-portal", "127.0.0.1:" + o.RpcPort,
                "--socks5", o.SocksPort.ToString(),
                "--listeners", "udp://0.0.0.0:" + o.ListenerPort,
                "tcp://0.0.0.0:" + o.ListenerPort,
                "--peers"
            ];
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            foreach (var peer in o.CoordinationPeers) psi.ArgumentList.Add(peer);
            var rule = "Yanzi-EasyTier-Direct-LocalSocks-" + Guid.NewGuid().ToString("N");
            RunFirewall("add", rule, o.SocksPort);
            try { _owned = Process.Start(psi) ?? throw new IOException("easytier_start_failed"); }
            catch { RunFirewall("delete", rule); throw; }
            _firewallRule = rule;
            _rpcPort = o.RpcPort;
            _socksPort = o.SocksPort;
            return new { started = true, alreadyRunning = false,
                direct = false, processId = _owned.Id,
                note = "Waiting for p2p/udp; relay file transmission is disabled." };
        }
    }

    internal static object Status(TransferChannel profile)
    {
        lock (Gate)
            return new { managed = IsOwned(profile), pid = IsOwned(profile) ? _owned?.Id : null,
                rpcPort = _rpcPort, socksPort = _socksPort,
                note = "The transfer adapter additionally checks EasyTier peer cost=p2p and protocol=udp." };
    }

    internal static object Stop()
    {
        lock (Gate)
        {
            bool stopped = false;
            if (_owned is { HasExited: false } p)
            {
                try { p.Kill(entireProcessTree: true); stopped = true; }
                catch (InvalidOperationException) { }
            }
            _owned?.Dispose();
            if (_firewallRule is { } firewall)
            {
                try { RunFirewall("delete", firewall); }
                catch (InvalidOperationException) { /* Best-effort cleanup; report during independent QA. */ }
                _firewallRule = null;
            }
            _owned = null;
            _rpcPort = 0;
            _socksPort = 0;
            TemporaryEasyTierP2pAdapter.StopServer();
            return new { stopped };
        }
    }
}
