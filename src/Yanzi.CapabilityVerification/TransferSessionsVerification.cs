using OpenQuickHost;
using OpenQuickHost.Sync;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class TransferSessionsVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "YanziDev", "chunks", Guid.NewGuid().ToString("N"));
        using var isolated = (IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root])!;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        int effects = 0, checks = 0;
        void Check(bool result, string label) { if (!result) throw new Exception(label); checks++; }
        LocalAgentApiServer Start() { var server = new LocalAgentApiServer($"http://127.0.0.1:{port}/", "chunk-fixture", _ => {}, onMobileMessage: _ => { effects++; return Task.FromResult((true, "saved")); }); server.Start(); return server; }
        var server = Start();
        try {
            using var http = new HttpClient {BaseAddress = new Uri($"http://127.0.0.1:{port}")};
            http.DefaultRequestHeaders.Authorization = new("Bearer", "chunk-fixture");
            http.DefaultRequestHeaders.Add("X-Yanzi-Peer-Device", "phone-chunk-test");
            var bytes = RandomNumberGenerator.GetBytes(3145729);
            var hashes = Enumerable.Range(0, 4).Select(i => Convert.ToHexString(SHA256.HashData(bytes.AsSpan(i * 1048576, Math.Min(1048576, bytes.Length - i * 1048576)))).ToLowerInvariant()).ToArray();
            var id = Guid.NewGuid().ToString("N"); var path = "/v1/lan/transfer-sessions/" + id;
            var manifest = new LanTransferManifest(id, "phone-chunk-test", "chunk.bin", "file", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hashes);
            async Task<JsonElement> Read(HttpResponseMessage response) { using (response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone(); } }
            Check((await Read(await http.PostAsJsonAsync(path, manifest))).GetProperty("missingBlocks").GetArrayLength() == 4, "all blocks initially missing");
            using (var bad = await http.PutAsync(path + "/blocks/0", new ByteArrayContent(new byte[1048576]))) Check(bad.StatusCode == HttpStatusCode.BadRequest, "wrong block hash rejected");
            await Read(await http.PutAsync(path + "/blocks/0", new ByteArrayContent(bytes[..1048576])));
            server.Dispose(); server = Start();
            var state = await Read(await http.PostAsJsonAsync(path, manifest));
            Check(state.GetProperty("missingBlocks").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual([1, 2, 3]), "restart resumes only missing blocks");
            using (var early = await http.PostAsJsonAsync(path + "/commit", new {})) Check(early.StatusCode == HttpStatusCode.Conflict && effects == 0, "incomplete file never delivered");
            http.DefaultRequestHeaders.Remove("X-Yanzi-Peer-Device"); http.DefaultRequestHeaders.Add("X-Yanzi-Peer-Device", "phone-other");
            using (var wrong = await http.GetAsync(path)) Check(wrong.StatusCode == HttpStatusCode.Forbidden, "other paired device cannot inspect session");
            http.DefaultRequestHeaders.Remove("X-Yanzi-Peer-Device"); http.DefaultRequestHeaders.Add("X-Yanzi-Peer-Device", "phone-chunk-test");
            for (int i = 1; i < 4; i++) await Read(await http.PutAsync(path + "/blocks/" + i, new ByteArrayContent(bytes.AsSpan(i * 1048576, Math.Min(1048576, bytes.Length - i * 1048576)).ToArray())));
            Check((await Read(await http.PostAsJsonAsync(path + "/commit", new {}))).GetProperty("success").GetBoolean() && effects == 1, "commit verified complete file once");
            Check(File.ReadAllBytes(YanziLanTransferStore.FilePath(id, "chunk.bin")).SequenceEqual(bytes), "whole file bytes and SHA retained");
            await Read(await http.PostAsJsonAsync(path + "/commit", new {})); Check(effects == 1, "lost commit ack never repeats effect");
            using (var late = await http.PutAsync(path + "/blocks/0", new ByteArrayContent(bytes[..1048576]))) Check(late.StatusCode == HttpStatusCode.Conflict, "terminal session cannot acquire blocks");
            var cancelled = Guid.NewGuid().ToString("N"); var cancelPath = "/v1/lan/transfer-sessions/" + cancelled;
            var cancelManifest = manifest with {Id = cancelled};
            await Read(await http.PostAsJsonAsync(cancelPath, cancelManifest)); await Read(await http.DeleteAsync(cancelPath));
            server.Dispose(); server = Start();
            using (var retry = await http.PostAsJsonAsync(cancelPath, cancelManifest)) Check(retry.StatusCode == HttpStatusCode.Conflict && effects == 1, "cancellation tombstone survives restart");
            var oversized = manifest with {Id = Guid.NewGuid().ToString("N"), Size = 31457281};
            using (var reject = await http.PostAsJsonAsync("/v1/lan/transfer-sessions/" + oversized.Id, oversized)) Check(reject.StatusCode == HttpStatusCode.BadRequest, "size limit validated before staging");
            var snapshot = Path.Combine(root, "original.bin"); File.WriteAllBytes(snapshot, bytes);
            var job = DesktopChatOutbox.Enqueue("account-a", "desktop-a", Guid.NewGuid().ToString("N"), "file", "snapshot", snapshot, "phone-a");
            File.Delete(snapshot);
            Check(File.ReadAllBytes(DesktopChatOutbox.Pending("account-a").Single().FilePath!).SequenceEqual(bytes), "outbox survives removal of original file");
            Check(DesktopChatOutbox.Pending("account-b").Count == 0 && job.TargetDeviceId == "phone-a", "outbox account and explicit target preserved");
            DesktopChatOutbox.Complete(job, "accepted"); Check(DesktopChatOutbox.Pending("account-a").Count == 0 && !File.Exists(job.FilePath), "accepted outbox releases snapshot");
            var grant = JsonSerializer.SerializeToElement(YanziLanPairing.Create("phone-account-test", "Account", ["chat"], port));
            var pairId = grant.GetProperty("pairId").GetString()!;
            YanziPeerRegistry.ObserveAuthenticated("phone-account-test", IPAddress.Parse("192.168.1.3"), 42981, "First");
            YanziPeerRegistry.ObserveAuthenticated("phone-account-test", IPAddress.Parse("192.168.1.4"), 42981, "Second");
            Check(YanziPeerRegistry.List().Single().Endpoints!.Length == 2, "stable peer retains multiple authenticated endpoints");
            SyncSessionStore.Save(new SyncSession {UserId = "another-account"});
            Check(YanziLanPairing.Find(pairId) == null && YanziPeerRegistry.List().Count == 0, "account switch cannot reuse pairing or peer routes");
            SyncSessionStore.Clear();
            SyncSessionStore.Save(new SyncSession {UserId = "auto-account"});
            var manual = JsonSerializer.SerializeToElement(YanziLanPairing.Create("phone-auto-test", "Old manual link", ["chat"], port));
            var autoKey = RandomNumberGenerator.GetBytes(32);
            var autoId = Guid.NewGuid().ToString("N");
            var autoLink = JsonSerializer.SerializeToElement(new { protocol = "yanzi.lan.aead.v1", mode = "account", ownerAccount = "auto-account",
                deviceId = "desktop-auto-test", desktopDeviceId = "phone-auto-test", desktopName = "My phone", pairId = autoId,
                key = Convert.ToBase64String(autoKey), scopes = new[] {"chat", "attachments", "account.owner"}, expiresAt = DateTimeOffset.UtcNow.AddDays(7) });
            YanziLanPairing.ImportAccountLink(autoLink, "auto-account", "desktop-auto-test");
            Check(YanziLanPairing.List().Count == 1 && YanziLanPairing.Find(manual.GetProperty("pairId").GetString()!) == null, "automatic account link replaces obsolete manual key for same peer");
            Check(YanziLanPairing.Key(YanziLanPairing.Find(autoId)!).SequenceEqual(autoKey), "automatic link key survives protected storage");
            bool mismatched = false;
            try { YanziLanPairing.ImportAccountLink(autoLink, "another-account", "desktop-auto-test"); } catch (InvalidOperationException) { mismatched = true; }
            Check(mismatched, "automatic import rejects account mismatch");
            SyncSessionStore.Clear();
            var allowed = Path.Combine(root, "allowed"); var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(allowed); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "private.txt"), "outside");
            Check(YanziDeviceResourceAuthorization.AllowsFile(Path.Combine(allowed, "new.txt"), [allowed]), "new file in approved physical directory allowed");
            Check(!YanziDeviceResourceAuthorization.AllowsFile(Path.Combine(allowed, "..", "outside", "private.txt"), [allowed]), "physical path traversal denied");
            var junction = Path.Combine(allowed, "link");
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") {CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true};
            foreach (var argument in new[] {"/d", "/c", "mklink", "/J", junction, outside}) start.ArgumentList.Add(argument);
            using (var process = System.Diagnostics.Process.Start(start)!) { await process.WaitForExitAsync(); Check(process.ExitCode == 0, "junction fixture created"); }
            Check(!YanziDeviceResourceAuthorization.AllowsFile(Path.Combine(junction, "private.txt"), [allowed]), "junction cannot widen approved resource boundary");
            Console.WriteLine($"LAN_CHUNKS_RESTART_CANCEL_HASH_INBOX_OUTBOX_AND_ACCOUNT_ISOLATION=PASSED; checks={checks}");
        } finally { server.Dispose(); }
    }
}
