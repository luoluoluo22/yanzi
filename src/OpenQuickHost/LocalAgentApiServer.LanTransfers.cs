using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private static readonly SemaphoreSlim LanTransferCommitLock = new(1, 1);
    private async Task<bool> TryHandleLanTransferApiAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (request.HttpMethod != "POST" || !path.StartsWith("/v1/lan/transfers/", StringComparison.Ordinal)) return false;
        var watch = Stopwatch.StartNew();
        var id = path["/v1/lan/transfers/".Length..];
        var kind = request.QueryString["kind"];
        var name = request.QueryString["name"] ?? "file";
        var source = request.QueryString["sourceDeviceId"] ?? "";
        var sourceName = request.QueryString["sourceDeviceName"] ?? "Android";
        var hash = request.Headers["X-Content-Sha256"] ?? "";
        const long limit = 30L * 1024 * 1024;
        if (!Guid.TryParseExact(id, "N", out _) || kind is not ("photo" or "file") ||
            name.Length > 200 || name != Path.GetFileName(name.Replace('\\', '/')) || name.Any(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)) ||
            string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source) || source.Length > 200 || sourceName.Length > 200 ||
            !System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"))
        { await WriteJsonAsync(response, 400, new { error = "invalid_transfer_metadata" }); return true; }
        if (request.ContentLength64 <= 0 || request.ContentLength64 > limit)
        { await WriteJsonAsync(response, request.ContentLength64 > limit ? 413 : 400, new { error = "invalid_transfer_size" }); return true; }
        if (_onMobileMessage == null)
        { await WriteJsonAsync(response, 503, new { error = "mobile_bridge_unavailable" }); return true; }
        if (request.RemoteEndPoint is { } remote && !IPAddress.IsLoopback(remote.Address))
            YanziPeerRegistry.ObserveAuthenticated(source, remote.Address,
                int.TryParse(request.QueryString["notificationPort"], out var notificationPort) ? notificationPort : 42981, sourceName);
        Directory.CreateDirectory(YanziLanTransferStore.Root);
        YanziLanTransferStore.Cleanup();
        if (Directory.EnumerateFiles(YanziLanTransferStore.Root).Sum(x => new FileInfo(x).Length) + request.ContentLength64 > 300L * 1024 * 1024)
        { await WriteJsonAsync(response, 507, new {error = "transfer_quota_exceeded"}); return true; }
        if (new DriveInfo(Path.GetPathRoot(YanziLanTransferStore.Root)!).AvailableFreeSpace < request.ContentLength64 + 10L * 1024 * 1024)
        { await WriteJsonAsync(response, 507, new {error = "storage_exhausted"}); return true; }
        var temporary = Path.Combine(YanziLanTransferStore.Root, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            long total = 0;
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                int count;
                while ((count = await request.InputStream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count;
                    if (total > limit || total > request.ContentLength64)
                    { await WriteJsonAsync(response, 413, new { error = "transfer_too_large" }); return true; }
                    await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
            }
            using (var file = File.OpenRead(temporary))
                if (total != request.ContentLength64 || !Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                { await WriteJsonAsync(response, 400, new { error = "transfer_checksum_mismatch" }); return true; }
            var uploadedMs = watch.ElapsedMilliseconds;
            await LanTransferCommitLock.WaitAsync(timeout.Token);
            try
            {
                var receipt = YanziLanTransferStore.Load(id);
                if (receipt != null && (receipt.Hash != hash.ToLowerInvariant() || receipt.FileName != name || receipt.Source != source || receipt.Kind != kind))
                { await WriteJsonAsync(response, 409, new { error = "transfer_id_conflict" }); return true; }
                if (receipt?.Completed == true)
                { await WriteJsonAsync(response, 200, new { success = receipt.Success, messageId = id, output = receipt.Output, transport = "lan", deduplicated = true }); return true; }
                if (receipt != null)
                { await WriteJsonAsync(response, 200, new {success = false, messageId = id, output = "execution_result_unknown", transport = "lan", deduplicated = true}); return true; }
                receipt ??= new(name, hash.ToLowerInvariant(), total, source, kind!, false, false, "");
                File.Move(temporary, YanziLanTransferStore.FilePath(id, name), true);
                YanziLanTransferStore.Save(id, receipt);
                var payload = new Dictionary<string, JsonElement>
                {
                    ["lanAttachmentId"] = JsonSerializer.SerializeToElement(id),
                    ["clientTransferId"] = JsonSerializer.SerializeToElement(id),
                    ["sourceDeviceName"] = JsonSerializer.SerializeToElement(sourceName),
                    ["fileName"] = JsonSerializer.SerializeToElement(name),
                    ["size"] = JsonSerializer.SerializeToElement(total),
                    ["sha256"] = JsonSerializer.SerializeToElement(hash.ToLowerInvariant())
                };
                var message = new DeviceMessageRecord
                {
                    MessageId = id, TraceId = id, SourceDeviceId = source, TargetPlatform = "desktop", Kind = kind!,
                    Title = kind == "photo" ? "手机照片" : name, Text = kind == "photo" ? "手机照片" : "手机文件：" + name,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O"), Payload = payload
                };
                var result = await _onMobileMessage(message);
                YanziLanTransferStore.Save(id, receipt with { Completed = true, Success = result.success, Output = result.output });
                HostAssets.AppendLog($"LAN transfer completed: id={id}, kind={kind}, bytes={total}, uploadMs={uploadedMs}, processingMs={watch.ElapsedMilliseconds - uploadedMs}, totalMs={watch.ElapsedMilliseconds}, success={result.success}");
                await WriteJsonAsync(response, 200, new { success = result.success, messageId = id, output = result.output, transport = "lan", durationMs = watch.ElapsedMilliseconds });
            }
            finally { LanTransferCommitLock.Release(); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return true;
    }
}

public static class YanziLanTransferStore
{
    public static string Root => HostAssets.ResolveDataDirectoryPath("mobile-attachments/lan");
    public sealed record Receipt(string FileName, string Hash, long Size, string Source, string Kind, bool Completed, bool Success, string Output);
    public static void Cleanup()
    {
        foreach (var file in Directory.EnumerateFiles(Root))
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(file.EndsWith(".part") ? -1 : -30)) File.Delete(file);
    }
    public static Receipt? Load(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return null;
        var path = Path.Combine(Root, id + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path)) : null;
    }
    public static string FilePath(string id, string name) => Path.Combine(Root, id + "-" + name);
    public static void Save(string id, Receipt receipt)
    {
        var path = Path.Combine(Root, id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(receipt));
        File.Move(path + ".tmp", path, true);
    }
    public static string Resolve(string id)
    {
        var receipt = Load(id) ?? throw new IOException("局域网附件不存在。");
        var path = FilePath(id, receipt.FileName);
        if (!File.Exists(path) || new FileInfo(path).Length != receipt.Size) throw new IOException("局域网附件不完整。");
        return path;
    }
}
