using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed record YanziLanPair(string PairId, string DeviceId, string DisplayName, string ProtectedKey,
    string[] Scopes, DateTimeOffset ExpiresAt, string OwnerAccount = "local");

public static class YanziLanPairing
{
    private static readonly object Gate = new();
    private static string Root => HostAssets.ResolveDataDirectoryPath("device-network/pairs");
    private static string CurrentAccount => SyncSessionStore.Load()?.UserId ?? "local";
    private static string FileFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("invalid_pair_id");
        return Path.Combine(Root, id + ".json");
    }
    public static YanziLanPair? Find(string id)
    {
        lock (Gate)
        {
            var path = FileFor(id);
            if (!File.Exists(path)) return null;
            var pair = JsonSerializer.Deserialize<YanziLanPair>(File.ReadAllText(path));
            return pair?.ExpiresAt > DateTimeOffset.UtcNow && pair.OwnerAccount == CurrentAccount ? pair : null;
        }
    }
    public static IReadOnlyList<YanziLanPair> List()
    {
        lock (Gate) return Directory.Exists(Root) ? Directory.EnumerateFiles(Root, "*.json")
            .Select(x => JsonSerializer.Deserialize<YanziLanPair>(File.ReadAllText(x))!)
            .Where(x => x.ExpiresAt > DateTimeOffset.UtcNow && x.OwnerAccount == CurrentAccount).ToArray() : [];
    }
    public static object Create(string deviceId, string name, string[] scopes, int port)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(deviceId, "^[a-zA-Z0-9_.:-]{6,96}$") || name.Length > 120 ||
            scopes.Length > 64 || scopes.Any(x => x is not ("chat" or "attachments" or "files.remote" or "terminal" or "extensions.run") && !System.Text.RegularExpressions.Regex.IsMatch(x, "^capability:[a-zA-Z0-9_.-]{1,128}$")))
            throw new InvalidOperationException("invalid_pair_grant");
        lock (Gate)
        {
            Directory.CreateDirectory(Root);
            if (List().Count >= 128) throw new InvalidOperationException("pair_quota_exceeded");
            byte[] key = RandomNumberGenerator.GetBytes(32);
            var pair = new YanziLanPair(Guid.NewGuid().ToString("N"), deviceId, name,
                Convert.ToBase64String(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)), scopes,
                DateTimeOffset.UtcNow.AddDays(365), CurrentAccount);
            File.WriteAllText(FileFor(pair.PairId) + ".tmp", JsonSerializer.Serialize(pair));
            File.Move(FileFor(pair.PairId) + ".tmp", FileFor(pair.PairId));
            return new { protocol = "yanzi.lan.aead.v1", pairId = pair.PairId, deviceId,
                desktopDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId(), desktopName = Environment.MachineName,
                key = Convert.ToBase64String(key), scopes, expiresAt = pair.ExpiresAt, port };
        }
    }
    public static byte[] Key(YanziLanPair pair) => ProtectedData.Unprotect(Convert.FromBase64String(pair.ProtectedKey), null, DataProtectionScope.CurrentUser);
    public static void ImportAccountLink(JsonElement link, string account, string ownDeviceId)
    {
        if (account == "local" || account != CurrentAccount || link.GetProperty("ownerAccount").GetString() != account ||
            link.GetProperty("deviceId").GetString() != ownDeviceId || link.GetProperty("mode").GetString() != "account" ||
            link.GetProperty("protocol").GetString() != "yanzi.lan.aead.v1") throw new InvalidOperationException("account_link_mismatch");
        var key = Convert.FromBase64String(link.GetProperty("key").GetString()!);
        if (key.Length != 32) throw new InvalidOperationException("invalid_pair_key");
        var id = link.GetProperty("pairId").GetString()!;
        var pair = new YanziLanPair(id, link.GetProperty("desktopDeviceId").GetString()!, link.GetProperty("desktopName").GetString()!,
            Convert.ToBase64String(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)),
            link.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()!).ToArray(),
            link.GetProperty("expiresAt").GetDateTimeOffset(), account);
        lock (Gate)
        {
            var path = FileFor(id);
            Directory.CreateDirectory(Root);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(pair));
            File.Move(path + ".tmp", path, true);
            foreach (var old in List().Where(x => x.DeviceId == pair.DeviceId && x.PairId != pair.PairId)) File.Delete(FileFor(old.PairId));
        }
    }
    public static void Revoke(string id) { lock (Gate) File.Delete(FileFor(id)); }
    public static bool ClaimFrame(string pairId, string requestId)
    {
        lock (Gate)
        {
            _ = FileFor(pairId);
            var root = Path.Combine(Root, "replay", pairId);
            Directory.CreateDirectory(root);
            foreach (var old in Directory.EnumerateFiles(root, "*.nonce"))
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddMinutes(-4)) File.Delete(old);
            var path = Path.Combine(root, Guid.Parse(requestId).ToString("N") + ".nonce");
            if (File.Exists(path)) return false;
            if (Directory.EnumerateFiles(root).Take(2000).Count() >= 2000) throw new InvalidOperationException("lan_replay_quota");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Flush(true);
            return true;
        }
    }
    public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] body, string aad)
    {
        byte[] result = new byte[body.Length + 16];
        using var cipher = new AesGcm(key, 16);
        cipher.Encrypt(nonce, body, result.AsSpan(0, body.Length), result.AsSpan(body.Length), Encoding.UTF8.GetBytes(aad));
        return result;
    }
    public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] body, string aad)
    {
        if (body.Length < 16 || nonce.Length != 12) throw new CryptographicException("invalid_encrypted_frame");
        byte[] result = new byte[body.Length - 16];
        using var cipher = new AesGcm(key, 16);
        cipher.Decrypt(nonce, body.AsSpan(0, result.Length), body.AsSpan(result.Length), result, Encoding.UTF8.GetBytes(aad));
        return result;
    }
}
