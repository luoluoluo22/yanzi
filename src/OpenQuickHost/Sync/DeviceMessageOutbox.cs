using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

// Account-scoped payloads only: credentials are never written to the queue.
internal static class DeviceMessageOutbox
{
    private static readonly object Gate = new();
    private static string DirectoryFor(string account) => Path.Combine(Path.GetDirectoryName(HostAssets.MobileInboxPath)!,
        "device-outbox", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account))));
    public static string Save(string account, string id, string body)
    {
        if (!Guid.TryParse(id, out var logical)) throw new ArgumentException("Invalid logical message identity");
        lock (Gate)
        {
            var root = DirectoryFor(account);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, logical.ToString("N") + ".json");
            if (File.Exists(path))
            {
                if (File.ReadAllText(path) != body) throw new InvalidOperationException("message_id_reused");
                return path;
            }
            if (Directory.EnumerateFiles(root, "*.json").Take(1000).Count() >= 1000)
                throw new IOException("outbox_quota_exceeded");
            File.WriteAllText(path + ".tmp", body);
            File.Move(path + ".tmp", path, true);
            return path;
        }
    }
    public static IReadOnlyList<string> Pending(string account)
    {
        lock (Gate)
        {
            var root = DirectoryFor(account);
            return Directory.Exists(root) ? Directory.EnumerateFiles(root, "*.json")
                .OrderBy(File.GetCreationTimeUtc).Take(20).ToArray() : [];
        }
    }
    public static string? ReadSaved(string account, string id)
    {
        lock (Gate) { var path = Path.Combine(DirectoryFor(account), Guid.Parse(id).ToString("N") + ".json"); return File.Exists(path) ? File.ReadAllText(path) : null; }
    }
    public static void Fail(string path)
    {
        lock (Gate)
        {
            File.Move(path, path + ".failed", true);
            foreach (var old in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.failed"))
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1)) File.Delete(old);
        }
    }
    public static void Complete(string path) { lock (Gate) File.Delete(path); }
}
