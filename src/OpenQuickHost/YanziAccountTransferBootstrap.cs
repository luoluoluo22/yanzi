using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>
/// Auto-bootstrap a per-pair P2P network from the existing account-issued LAN authorization.
/// There is no separate device registration, pairing UI, or shared global network secret.
/// Never rewrites a live worker config: active transfers survive host restarts.
/// </summary>
internal static class YanziAccountTransferBootstrap
{
    internal sealed record PairPlan(string LocalDeviceId, string PeerDeviceId, string NetworkName,
        string NetworkSecret, string LocalIp, string RemoteIp);

    internal static string NetworkHostname(string deviceId) =>
        "yanzi-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)))
            .ToLowerInvariant()[..22];

    internal static PairPlan Derive(string account, string own, string peer, byte[] accountLinkKey)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(own) ||
            string.IsNullOrWhiteSpace(peer) || own == peer || accountLinkKey.Length != 32)
            throw new InvalidDataException("p2p_account_identity_invalid");
        var ids = new[] { own, peer };
        Array.Sort(ids, StringComparer.Ordinal);
        var context = "yanzi.p2p.account.v1\n" + account + "\n" + ids[0] + "\n" + ids[1];
        var namespaceKey = HMACSHA256.HashData(accountLinkKey, Encoding.UTF8.GetBytes(context + "\nnetwork"));
        var secretBytes = HMACSHA256.HashData(accountLinkKey, Encoding.UTF8.GetBytes(context + "\nsecret"));
        var name = "yanzi_" + Convert.ToHexString(namespaceKey).ToLowerInvariant()[..22];
        // These are virtual SOCKS-only addresses; never modify the OS routing table.
        var third = 40 + namespaceKey[0] % 180;
        var first = ids[0] == own;
        return new PairPlan(own, peer, name, Convert.ToHexString(secretBytes).ToLowerInvariant(),
            $"10.244.{third}.{(first ? 1 : 2)}", $"10.244.{third}.{(first ? 2 : 1)}");
    }

    internal static async Task<object> PrepareAsync(string? peerQuery = null, CancellationToken ct = default)
    {
        var account = SyncSessionStore.Load()?.UserId;
        if (string.IsNullOrWhiteSpace(account))
            throw new UnauthorizedAccessException("p2p_account_login_required");
        var own = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        var (cloud, devices) = await YanziDeviceCapabilityProvider.LoadDevicesAsync();
        if (!string.Equals(cloud.CurrentUserId, account, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("p2p_account_session_mismatch");

        var candidates = devices.Where(x => x.DeviceId != own &&
            x.Platform.Equals("desktop", StringComparison.OrdinalIgnoreCase)).ToArray();
        PeerDeviceInfo peer;
        if (string.IsNullOrWhiteSpace(peerQuery))
        {
            var online = candidates.Where(x => x.Online).ToArray();
            if (online.Length != 1)
                throw new InvalidOperationException("p2p_target_required_when_multiple_or_no_online_desktops");
            peer = online[0];
        }
        else peer = YanziDeviceCapabilityProvider.Resolve(candidates, peerQuery);
        // Server-side ownership check is performed by /v1/me/devices and /lan-links.
        if (!peer.Online) throw new IOException("p2p_target_offline");
        await cloud.SyncAccountLanLinksAsync(own, ct);
        var link = YanziLanPairing.List().SingleOrDefault(x =>
            x.OwnerAccount == account && x.DeviceId == peer.DeviceId &&
            x.Scopes.Contains("account.owner") && x.ExpiresAt > DateTimeOffset.UtcNow);
        if (link is null) throw new UnauthorizedAccessException("p2p_account_link_not_ready");
        var key = YanziLanPairing.Key(link);
        PairPlan plan;
        try { plan = Derive(account, own, peer.DeviceId, key); }
        finally { CryptographicOperations.ZeroMemory(key); }

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YanziTransferService");
        Directory.CreateDirectory(root);
        // The independent service remains the owner of its live configuration and active jobs.
        // A new peer requires a new session/worker in the future; never silently replace it.
        if (File.Exists(Path.Combine(root, "config.json")))
        {
            // A process can only serve its existing peer. Never claim it silently
            // switched overlay networks while downloads are still active.
            var currentPairing = Path.Combine(root, "pairing.json");
            if (File.Exists(currentPairing))
            {
                using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(currentPairing, ct));
                if (previous.RootElement.GetProperty("networkName").GetString() != plan.NetworkName ||
                    previous.RootElement.GetProperty("hostname").GetString() != NetworkHostname(own) ||
                    previous.RootElement.GetProperty("peerName").GetString() != NetworkHostname(peer.DeviceId))
                    throw new IOException("p2p_peer_switch_requires_independent_session");
            }
            else
            {
                // Only for installed 1.0.83 legacy workers, match the existing
                // hostname to the server-owned peer record before reusing it.
                using var previous = JsonDocument.Parse(
                    await File.ReadAllTextAsync(Path.Combine(root, "config.json"), ct));
                var configured = previous.RootElement.GetProperty("tunnel")
                    .GetProperty("peerName").GetString() ?? "";
                var label = peer.DisplayName ?? "";
                var model = peer.ModelName ?? "";
                if (configured.Length < 3 ||
                    (!label.Contains(configured, StringComparison.OrdinalIgnoreCase) &&
                     !model.Contains(configured, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("p2p_legacy_worker_peer_mismatch");
            }
            return new { existingWorker = true, peerDeviceId = peer.DeviceId,
                autoPaired = File.Exists(currentPairing), note = "existing_worker_preserved" };
        }

        // Reuse an existing trusted runtime when possible. On a clean account device,
        // fetch the pinned official EasyTier archive and verify its SHA-256.
        var (corePath, cliPath) = await YanziPortableEasyTierInstaller.EnsureAsync(ct);
        var moduleManifest = Path.Combine(AppContext.BaseDirectory, "ServiceModules",
            "yanzi-transfer-service.json");
        if (!File.Exists(moduleManifest)) throw new IOException("p2p_module_manifest_unavailable");
        using var module = JsonDocument.Parse(await File.ReadAllTextAsync(moduleManifest, ct));
        var peers = module.RootElement.GetProperty("coordinationPeers").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray();
        if (peers.Length is < 1 or > 5 ||
            peers.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x,
                @"^(tcp|udp)://[a-zA-Z0-9.-]{1,253}:[0-9]{2,5}$")))
            throw new InvalidDataException("p2p_module_coordination_invalid");
        var legacy = HostAssets.ResolveDataDirectoryPath("temporary-transfer/easytier.json");
        if (File.Exists(legacy))
        {
            using var old = JsonDocument.Parse(await File.ReadAllTextAsync(legacy, ct));
            if (old.RootElement.TryGetProperty("coordinationPeers", out var saved) &&
                saved.ValueKind == JsonValueKind.Array)
            {
                var current = saved.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                if (current.Length is > 0 and <= 5 &&
                    current.All(x => System.Text.RegularExpressions.Regex.IsMatch(x,
                        @"^(tcp|udp)://[a-zA-Z0-9.-]{1,253}:[0-9]{2,5}$")))
                    peers = current;
            }
        }

        var secretFile = Path.Combine(root, "private-network.secret");
        var pairing = Path.Combine(root, "pairing.json");
        if (File.Exists(pairing) || File.Exists(secretFile))
        {
            if (!File.Exists(pairing) || !File.Exists(secretFile) ||
                !string.Equals((await File.ReadAllTextAsync(secretFile, ct)).Trim(),
                    plan.NetworkSecret, StringComparison.Ordinal))
                throw new IOException("p2p_existing_pairing_requires_migration");
            using var prior = JsonDocument.Parse(await File.ReadAllTextAsync(pairing, ct));
            if (prior.RootElement.GetProperty("networkName").GetString() != plan.NetworkName ||
                prior.RootElement.GetProperty("peerName").GetString() != NetworkHostname(peer.DeviceId))
                throw new IOException("p2p_existing_pairing_requires_migration");
            return new { existingWorker = false, autoPaired = true,
                peerDeviceId = peer.DeviceId, networkName = plan.NetworkName };
        }
        await File.WriteAllTextAsync(secretFile, plan.NetworkSecret, ct);
        try
        {
            var payload = new
            {
                corePath, cliPath, networkSecretFile = secretFile,
                networkName = plan.NetworkName,
                localIp = plan.LocalIp, remoteIp = plan.RemoteIp,
                peerName = NetworkHostname(plan.PeerDeviceId),
                hostname = NetworkHostname(plan.LocalDeviceId),
                coordinationPeers = peers
            };
            await File.WriteAllTextAsync(pairing, JsonSerializer.Serialize(payload), ct);
            // Restrict both files to the current Windows user.
            RestrictToCurrentUser(secretFile);
            RestrictToCurrentUser(pairing);
        }
        catch
        {
            File.Delete(secretFile);
            if (File.Exists(pairing)) File.Delete(pairing);
            throw;
        }
        return new { existingWorker = false, autoPaired = true,
            peerDeviceId = peer.DeviceId, networkName = plan.NetworkName };
    }

    private static void RestrictToCurrentUser(string file)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls.exe")
        {
            ArgumentList = { file, "/inheritance:r", "/grant:r",
                "*" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value + ":R" },
            UseShellExecute = false, CreateNoWindow = true
        }) ?? throw new IOException("p2p_acl_process_failed");
        if (!process.WaitForExit(10000) || process.ExitCode != 0)
            throw new IOException("p2p_acl_restriction_failed");
    }

    internal static async Task NotifyPeerAsync(string peerDeviceId, CancellationToken ct = default)
    {
        var (cloud, devices) = await YanziDeviceCapabilityProvider.LoadDevicesAsync();
        if (!devices.Any(x => x.DeviceId == peerDeviceId && x.Online &&
            x.Platform.Equals("desktop", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("p2p_peer_no_longer_online");
        await cloud.SendDeviceMessageAsync(
            DeviceIdentityStore.GetOrCreateDesktopDeviceId(), "desktop", "capability.invoke",
            "P2P 自动组网", "", peerDeviceId,
            new { name = "files.p2p.connect",
                payload = new { targetDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
                    suppressPeerNotification = true } },
            ct, expiresAt: DateTimeOffset.UtcNow.AddMinutes(2));
    }
}
