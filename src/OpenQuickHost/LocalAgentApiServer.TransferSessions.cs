using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenQuickHost;

public sealed record LanTransferManifest(string Id, string SourceDeviceId, string Name, string Kind, long Size,
    string Sha256, string[] BlockHashes, int NotificationPort = 42981, string State = "active", string? Result = null);

public sealed partial class LocalAgentApiServer
{
    private static readonly SemaphoreSlim TransferSessionsGate = new(1);
    private static string TransferSessionsRoot => HostAssets.ResolveDataDirectoryPath("mobile-attachments/sessions");
    private async Task<bool> TryTransferSessionsAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        var companion=path.StartsWith("/v1/companion/transfer-sessions/",StringComparison.Ordinal);
        if (!companion && !path.StartsWith("/v1/lan/transfer-sessions/", StringComparison.Ordinal)) return false;
        var prefix=companion?"/v1/companion/transfer-sessions/":"/v1/lan/transfer-sessions/";
        var parts = path[prefix.Length..].Split('/');
        if (!Guid.TryParseExact(parts[0], "N", out _)) { await WriteJsonAsync(response, 400, new {error = "invalid_transfer_id"}); return true; }
        await TransferSessionsGate.WaitAsync(_cts.Token);
        try
        {
            var root = companion ? Path.Combine(YanziFileWorkflow.Root,"sessions") : TransferSessionsRoot;
            Directory.CreateDirectory(root);
            foreach (var expired in Directory.EnumerateDirectories(root))
            {
                var savedPath = Path.Combine(expired, "manifest.json");
                if (Directory.GetLastWriteTimeUtc(expired) < DateTime.UtcNow.AddDays(-30)) { Directory.Delete(expired, true); continue; }
                if (Directory.GetLastWriteTimeUtc(expired) < DateTime.UtcNow.AddDays(-1) && File.Exists(savedPath))
                {
                    var saved = JsonSerializer.Deserialize<LanTransferManifest>(File.ReadAllText(savedPath));
                    if (saved?.State == "active")
                    {
                        SaveManifest(savedPath, saved with { State = "cancelled" });
                        foreach (var block in Directory.EnumerateFiles(expired).Where(x => Path.GetFileName(x) != "manifest.json")) File.Delete(block);
                    }
                }
            }
            var directory = Path.Combine(root, parts[0]);
            var manifestPath = Path.Combine(directory, "manifest.json");
            LanTransferManifest? manifest = File.Exists(manifestPath) ? JsonSerializer.Deserialize<LanTransferManifest>(File.ReadAllText(manifestPath)) : null;
            var source = request.Headers["X-Yanzi-Peer-Device"];
            if (manifest != null && source != null && manifest.SourceDeviceId != source)
            { await WriteJsonAsync(response, 403, new {error = "transfer_owner_mismatch"}); return true; }
            if (parts.Length == 1 && request.HttpMethod == "POST")
            {
                if (request.ContentLength64 > 32000) { await WriteJsonAsync(response, 413, new {error = "manifest_too_large"}); return true; }
                using var manifestBytes = new MemoryStream();
                using var manifestTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token); manifestTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                var buffer = new byte[4096]; int count;
                while ((count = await request.InputStream.ReadAsync(buffer, manifestTimeout.Token)) > 0)
                {
                    if (manifestBytes.Length + count > 32000) { await WriteJsonAsync(response, 413, new {error = "manifest_too_large"}); return true; }
                    manifestBytes.Write(buffer, 0, count);
                }
                LanTransferManifest? proposed;
                try { proposed = JsonSerializer.Deserialize<LanTransferManifest>(manifestBytes.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
                catch (JsonException) { await WriteJsonAsync(response, 400, new {error = "invalid_transfer_manifest"}); return true; }
                bool ValidHash(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^[a-f0-9]{64}$");
                if (proposed == null || proposed.Id != parts[0] || proposed.Size is < 1 or > 31457280 || proposed.State != "active" || proposed.Result != null ||
                    proposed.Kind is not ("photo" or "file") || string.IsNullOrEmpty(proposed.Name) || proposed.Name != Path.GetFileName(proposed.Name.Replace('\\', '/')) ||
                    proposed.Name.Length is < 1 or > 200 || proposed.Name.Any(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c)) ||
                    !ValidHash(proposed.Sha256) || proposed.BlockHashes == null || proposed.BlockHashes.Length != (proposed.Size + 1048575) / 1048576 ||
                    proposed.BlockHashes.Any(x => !ValidHash(x)) || string.IsNullOrWhiteSpace(proposed.SourceDeviceId) ||
                    source != null && source != proposed.SourceDeviceId)
                { await WriteJsonAsync(response, 400, new {error = "invalid_transfer_manifest"}); return true; }
                if (manifest != null && JsonSerializer.Serialize(manifest with {State = "active", Result = null}) != JsonSerializer.Serialize(proposed))
                { await WriteJsonAsync(response, 409, new {error = "transfer_id_reused"}); return true; }
                if (manifest == null)
                {
                    if (Directory.EnumerateDirectories(root).Take(10000).Count() >= 10000 || Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
                        .Count(x => JsonSerializer.Deserialize<LanTransferManifest>(File.ReadAllText(x))?.State == "active") >= 128 || Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length) + proposed.Size > 300L * 1024 * 1024)
                    { await WriteJsonAsync(response, 507, new {error = "transfer_quota_exceeded"}); return true; }
                    if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < proposed.Size * 2 + 10L * 1024 * 1024)
                    { await WriteJsonAsync(response, 507, new {error = "storage_exhausted"}); return true; }
                    Directory.CreateDirectory(directory); manifest = proposed; SaveManifest(manifestPath, manifest);
                }
            }
            if (manifest == null) { await WriteJsonAsync(response, 404, new {error = "transfer_not_found"}); return true; }
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
            if (manifest.State == "cancelled") { await WriteJsonAsync(response, 409, new {error = "transfer_cancelled"}); return true; }
            if (request.HttpMethod == "DELETE" && parts.Length == 1)
            {
                if (manifest.State == "completed") { await WriteJsonAsync(response, 409, new {error = "transfer_already_completed"}); return true; }
                SaveManifest(manifestPath, manifest with {State = "cancelled"});
                foreach (var file in Directory.EnumerateFiles(directory).Where(x => Path.GetFileName(x) != "manifest.json")) File.Delete(file);
                await WriteJsonAsync(response, 200, new {cancelled = true}); return true;
            }
            var missing = new List<int>();
            for (int index = 0; index < manifest.BlockHashes.Length; index++)
            {
                var block = Path.Combine(directory, index + ".block");
                if (!File.Exists(block)) { missing.Add(index); continue; }
                using var input = File.OpenRead(block);
                if (!Convert.ToHexString(SHA256.HashData(input)).Equals(manifest.BlockHashes[index], StringComparison.OrdinalIgnoreCase)) { missing.Add(index); input.Dispose(); File.Delete(block); }
            }
            if (parts.Length == 1 && request.HttpMethod is "GET" or "POST")
            { await WriteJsonAsync(response, 200, new {id = manifest.Id, state = manifest.State, missingBlocks = manifest.State == "completed" ? [] : missing.ToArray(), result = manifest.Result}); return true; }
            if (parts.Length == 3 && parts[1] == "blocks" && request.HttpMethod == "PUT" && int.TryParse(parts[2], out var blockIndex) && blockIndex >= 0 && blockIndex < manifest.BlockHashes.Length)
            {
                if (manifest.State != "active") { await WriteJsonAsync(response, 409, new {error = "transfer_already_completed"}); return true; }
                var expected = Math.Min(1048576L, manifest.Size - blockIndex * 1048576L);
                if (request.ContentLength64 != expected) { await WriteJsonAsync(response, 400, new {error = "invalid_block_size"}); return true; }
                var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
                try
                {
                    using var bytes = new MemoryStream(); await request.InputStream.CopyToAsync(bytes, _cts.Token);
                    if (bytes.Length != expected || !Convert.ToHexString(SHA256.HashData(bytes.ToArray())).Equals(manifest.BlockHashes[blockIndex], StringComparison.OrdinalIgnoreCase))
                    { await WriteJsonAsync(response, 400, new {error = "block_checksum_mismatch"}); return true; }
                    await File.WriteAllBytesAsync(temporary, bytes.ToArray(), _cts.Token); File.Move(temporary, Path.Combine(directory, blockIndex + ".block"), true);
                    await WriteJsonAsync(response, 200, new {saved = true, index = blockIndex}); return true;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            if (parts.Length == 2 && parts[1] == "commit" && request.HttpMethod == "POST")
            {
                if (manifest.State == "completed") { await WriteJsonAsync(response, 200, JsonSerializer.Deserialize<JsonElement>(manifest.Result!)); return true; }
                if (missing.Count > 0) { await WriteJsonAsync(response, 409, new {error = "blocks_missing", missingBlocks = missing}); return true; }
                var merged = Path.Combine(directory, "merged.part");
                try
                {
                    using (var output = File.Create(merged))
                        for (int index = 0; index < manifest.BlockHashes.Length; index++) { using var input = File.OpenRead(Path.Combine(directory, index + ".block")); await input.CopyToAsync(output, _cts.Token); }
                    using (var input = File.OpenRead(merged))
                        if (input.Length != manifest.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                        { await WriteJsonAsync(response, 400, new {error = "transfer_checksum_mismatch"}); return true; }
                    using var client = new HttpClient(new HttpClientHandler {UseProxy = false}) {Timeout = TimeSpan.FromSeconds(90)};
                    using var forward = companion ? new HttpRequestMessage(HttpMethod.Put,$"http://127.0.0.1:{request.LocalEndPoint.Port}/v1/companion/files/{manifest.Id}") : new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{request.LocalEndPoint.Port}/v1/lan/transfers/{manifest.Id}?kind={manifest.Kind}&name={Uri.EscapeDataString(manifest.Name)}&sourceDeviceId={Uri.EscapeDataString(manifest.SourceDeviceId)}&notificationPort={manifest.NotificationPort}");
                    forward.Headers.Authorization = new("Bearer", _token); forward.Headers.Add("X-Content-Sha256", manifest.Sha256);
                    using var data = File.OpenRead(merged); forward.Content = new StreamContent(data); forward.Content.Headers.ContentLength = manifest.Size;
                    using var result = await client.SendAsync(forward, _cts.Token);
                    var json = await result.Content.ReadAsStringAsync();
                    if (result.IsSuccessStatusCode) {
                        SaveManifest(manifestPath, manifest with {State = "completed", Result = json});
                        foreach (var block in Directory.EnumerateFiles(directory, "*.block")) File.Delete(block);
                    }
                    await WriteJsonAsync(response, (int)result.StatusCode, JsonSerializer.Deserialize<JsonElement>(json)); return true;
                }
                finally { if (File.Exists(merged)) File.Delete(merged); }
            }
            await WriteJsonAsync(response, 405, new {error = "transfer_method_not_allowed"}); return true;
        }
        finally { TransferSessionsGate.Release(); }
    }
    private static void SaveManifest(string path, LanTransferManifest manifest) { File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(manifest)); File.Move(path + ".tmp", path, true); }
}
