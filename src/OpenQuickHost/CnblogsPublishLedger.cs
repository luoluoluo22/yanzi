using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// 防止相同发布操作重复提交。发送请求前持久化 pending；网络结果不明时禁止盲目重试。
/// </summary>
internal static class CnblogsPublishLedger
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string PathToLedger => HostAssets.ResolveDataFilePath("cnblogs-publish-ledger.dat");

    public static async Task<object?> RunAsync(string requestId, string fingerprint, Func<Task<object?>> publish)
    {
        if (requestId.Length is < 8 or > 128 || requestId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("requestId 必须为 8-128 位字母、数字、下划线或连字符。");
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = Load();
            if (entries.TryGetValue(requestId, out var existing))
            {
                if (!existing.Fingerprint.Equals(fingerprint, StringComparison.Ordinal))
                    throw new ArgumentException("相同 requestId 不能用于不同文章内容。");
                if (existing.ResultJson != null)
                    return JsonSerializer.Deserialize<JsonElement>(existing.ResultJson);
                throw new InvalidOperationException("之前的发布请求结果尚未确认。为防止重复发文，请先到博客园核查。");
            }
            entries[requestId] = new Record(fingerprint, null);
            Save(entries); // pending must hit durable storage before any HTTP request
            try
            {
                var result = await publish().ConfigureAwait(false);
                entries[requestId] = new Record(fingerprint, JsonSerializer.Serialize(result));
                Save(entries);
                return result;
            }
            catch
            {
                // Keep pending on all network/parse failures: remote may already have committed.
                throw;
            }
        }
        finally { Gate.Release(); }
    }

    public static string Fingerprint(string title, string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + body));
        return Convert.ToHexString(bytes);
    }

    private static Dictionary<string, Record> Load()
    {
        if (!File.Exists(PathToLedger)) return new(StringComparer.Ordinal);
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(PathToLedger), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<Dictionary<string, Record>>(bytes)
                     ?? throw new InvalidDataException("发布流水账已损坏。"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void Save(Dictionary<string, Record> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathToLedger)!);
        var raw = JsonSerializer.SerializeToUtf8Bytes(entries);
        try
        {
            var encrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            var tmp = PathToLedger + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(tmp, encrypted); File.Move(tmp, PathToLedger, true); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    private sealed record Record(string Fingerprint, string? ResultJson);
}
