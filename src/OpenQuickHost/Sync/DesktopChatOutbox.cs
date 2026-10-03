using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed record DesktopChatJob(string Id, string Account, string Source, string Kind, string Text, string? FilePath,
    DateTimeOffset CreatedAt, MobileAttachment? Attachment = null, string? CompletedMessageId = null, string? TargetDeviceId = null);

public static class DesktopChatOutbox
{
    private static readonly object Gate = new();
    private static string Root(string account) => Path.Combine(HostAssets.ResolveDataDirectoryPath("device-chat-outbox"),
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account))));
    private static string RecordPath(DesktopChatJob job) => Path.Combine(Root(job.Account), job.Id, "job.json");
    public static DesktopChatJob Enqueue(string account, string source, string id, string kind, string text, string? file, string? targetDeviceId = null)
    {
        lock (Gate)
        {
            var root = Root(account);
            Directory.CreateDirectory(root);
            Cleanup(root);
            if (!Guid.TryParseExact(id, "N", out _) || Directory.EnumerateFiles(root, "job.json", SearchOption.AllDirectories)
                .Count(x => JsonSerializer.Deserialize<DesktopChatJob>(File.ReadAllText(x))?.CompletedMessageId == null) >= 1000)
                throw new IOException("outbox_quota_exceeded");
            var directory = Path.Combine(root, id);
            Directory.CreateDirectory(directory);
            string? saved = null;
            if (file != null)
            {
                var info = new FileInfo(file);
                var used = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
                if (info.Length > 30L * 1024 * 1024 || used + info.Length > 300L * 1024 * 1024 ||
                    new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < info.Length + 10L * 1024 * 1024)
                    throw new IOException("outbox_storage_exhausted");
                saved = Path.Combine(directory, info.Name);
                if (info.Name == "job.json") saved = Path.Combine(directory, "attachment-job.json");
                File.Copy(file, saved);
            }
            var job = new DesktopChatJob(id, account, source, kind, text, saved, DateTimeOffset.UtcNow, TargetDeviceId: targetDeviceId);
            Save(job);
            return job;
        }
    }
    public static void Save(DesktopChatJob job)
    {
        lock (Gate)
        {
            var path = RecordPath(job);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(job)); File.Move(path + ".tmp", path, true);
        }
    }
    public static DesktopChatJob? Find(DesktopChatJob job)
    {
        lock (Gate) return File.Exists(RecordPath(job)) ? JsonSerializer.Deserialize<DesktopChatJob>(File.ReadAllText(RecordPath(job))) : null;
    }
    public static IReadOnlyList<DesktopChatJob> Pending(string account)
    {
        lock (Gate)
        {
            var root = Root(account);
            if (Directory.Exists(root)) Cleanup(root);
            return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Select(x => Path.Combine(x, "job.json"))
                .Where(File.Exists).Select(x => JsonSerializer.Deserialize<DesktopChatJob>(File.ReadAllText(x))!)
                .Where(x => x.CompletedMessageId == null).OrderBy(x => x.CreatedAt).Take(20).ToArray() : [];
        }
    }
    public static void Complete(DesktopChatJob job, string completedMessageId = "expired")
    {
        lock (Gate)
        {
            var directory = Path.GetDirectoryName(RecordPath(job))!;
            if (!directory.StartsWith(Root(job.Account) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("invalid_outbox_directory");
            Save(job with { CompletedMessageId = completedMessageId });
            if (job.FilePath != null) try { File.Delete(job.FilePath); } catch (IOException) { /* Active LAN stream holds the snapshot; cleanup retries later. */ }
        }
    }
    private static void Cleanup(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var record = Path.Combine(directory, "job.json");
            if (!File.Exists(record)) { if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1)) Directory.Delete(directory, true); continue; }
            var job = JsonSerializer.Deserialize<DesktopChatJob>(File.ReadAllText(record));
            if (job == null) continue;
            if (job.CompletedMessageId != null && job.FilePath != null && Path.GetDirectoryName(job.FilePath) == directory)
                try { File.Delete(job.FilePath); } catch (IOException) { continue; }
            if (job.CompletedMessageId != null && File.GetLastWriteTimeUtc(record) < DateTime.UtcNow.AddDays(-1) || job.CreatedAt < DateTimeOffset.UtcNow.AddDays(-30))
                try { Directory.Delete(directory, true); } catch (IOException) { /* Retry after an active reader releases the snapshot. */ }
        }
    }
}
