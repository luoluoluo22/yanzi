using System.IO;
using System.Net;
using System.Text.Json;

namespace OpenQuickHost;

public sealed record YanziPeerEndpoint(string Address, int Port, DateTimeOffset VerifiedAt);
public sealed record YanziPeer(string DeviceId, string DisplayName, string Address, int Port, string Platform,
    DateTimeOffset VerifiedAt, string[] Capabilities, YanziPeerEndpoint[]? Endpoints = null);

public static class YanziPeerRegistry
{
    private static readonly object Gate = new();
    private static string Store => Path.Combine(HostAssets.ResolveDataDirectoryPath("device-network"), "peers-" +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Sync.SyncSessionStore.Load()?.UserId ?? "local"))) + ".json");
    private static Dictionary<string, YanziPeer> Read()
    {
        if (!File.Exists(Store)) return new(StringComparer.Ordinal);
        return JsonSerializer.Deserialize<Dictionary<string, YanziPeer>>(File.ReadAllText(Store)) ?? new(StringComparer.Ordinal);
    }
    public static IReadOnlyList<YanziPeer> List()
    {
        lock (Gate) return Read().Values.OrderBy(x => x.DeviceId, StringComparer.Ordinal).ToArray();
    }
    public static void ObserveAuthenticated(string deviceId, IPAddress address, int port, string displayName,
        string platform = "android", params string[] capabilities)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId == "lan" || deviceId.Length > 96 || port is < 1 or > 65535) return;
        lock (Gate)
        {
            var peers = Read();
            if (!peers.ContainsKey(deviceId) && peers.Count >= 128) throw new InvalidOperationException("peer_quota_exceeded");
            peers.TryGetValue(deviceId, out var old);
            var endpoints = (old?.Endpoints ?? (old == null ? [] : [new YanziPeerEndpoint(old.Address, old.Port, old.VerifiedAt)]))
                .Where(x => (x.Address != address.ToString() || x.Port != port) && DateTimeOffset.UtcNow - x.VerifiedAt < TimeSpan.FromDays(1))
                .Prepend(new YanziPeerEndpoint(address.ToString(), port, DateTimeOffset.UtcNow)).Take(8).ToArray();
            peers[deviceId] = new(deviceId, displayName, address.ToString(), port, platform, DateTimeOffset.UtcNow, capabilities, endpoints);
            Directory.CreateDirectory(Path.GetDirectoryName(Store)!);
            File.WriteAllText(Store + ".tmp", JsonSerializer.Serialize(peers));
            File.Move(Store + ".tmp", Store, true);
        }
    }
    public static YanziPeer? Resolve(string? deviceId)
    {
        var paired = YanziLanPairing.List().Select(x => x.DeviceId).ToHashSet();
        var peers = List().Where(x => paired.Contains(x.DeviceId) && DateTimeOffset.UtcNow - x.VerifiedAt < TimeSpan.FromMinutes(10)).ToArray();
        // No implicit 'last phone wins': ambiguous targets go through account routing.
        return string.IsNullOrEmpty(deviceId) ? peers.Length == 1 ? peers[0] : null : peers.SingleOrDefault(x => x.DeviceId == deviceId);
    }
    public static void Remove(string deviceId)
    {
        lock (Gate)
        {
            var peers = Read();
            if (!peers.Remove(deviceId)) return;
            File.WriteAllText(Store + ".tmp", JsonSerializer.Serialize(peers));
            File.Move(Store + ".tmp", Store, true);
        }
    }
}
