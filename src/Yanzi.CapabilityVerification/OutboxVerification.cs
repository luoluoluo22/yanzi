using OpenQuickHost;
using OpenQuickHost.Sync;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class OutboxVerification
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "YanziDev", "outbox", Guid.NewGuid().ToString("N"));
        using var isolated = (IDisposable)typeof(HostAssets).GetMethod("UseIsolatedDataRootForVerification", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root])!;
        var portFinder = new TcpListener(IPAddress.Loopback, 0); portFinder.Start(); var port = ((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        using var http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{port}/"); http.Start();
        int uploads = 0, posts = 0, checks = 0; bool online = false; string? acceptedBody = null;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using var lifetime = new CancellationTokenSource();
        var fixture = Task.Run(async () => {
            while (!lifetime.IsCancellationRequested) {
                HttpListenerContext context;
                try { context = await http.GetContextAsync(); } catch (HttpListenerException) { break; }
                var request = context.Request; var response = context.Response;
                object result = new {ok = true};
                if (request.Url!.AbsolutePath == "/v1/me/mobile/attachments") {
                    using var bytes = new MemoryStream(); await request.InputStream.CopyToAsync(bytes); uploads++;
                    result = new {attachmentId = "att_" + new string('1', 32), fileName = request.QueryString["name"], contentType = "application/octet-stream", size = bytes.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O")};
                } else if (request.Url.AbsolutePath == "/v1/me/mobile/messages") {
                    using var reader = new StreamReader(request.InputStream); var body = await reader.ReadToEndAsync(); posts++;
                    if (acceptedBody != null && acceptedBody != body) throw new Exception("retry_changed_persisted_envelope");
                    acceptedBody = body;
                    response.StatusCode = online ? 200 : 503;
                    result = online ? new {messageId = "msg_fixture_stable"} : (object)new {error = "simulated_lost_acceptance"};
                }
                var output = JsonSerializer.SerializeToUtf8Bytes(result); response.ContentType = "application/json"; response.ContentLength64 = output.Length;
                await response.OutputStream.WriteAsync(output); response.Close();
            }
        });
        try {
            SyncSessionStore.Save(new SyncSession {UserId = "outbox-account", Username = "Fixture", AccessToken = "isolated-fixture", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()});
            var original = Path.Combine(root, "original.bin"); var bytes = RandomNumberGenerator.GetBytes(12345); File.WriteAllBytes(original, bytes);
            var job = DesktopChatOutbox.Enqueue("outbox-account", "desktop-outbox", Guid.NewGuid().ToString("N"), "file", "offline attachment", original, "phone-target");
            File.Delete(original);
            var options = new SyncOptions {BaseUrl = $"http://127.0.0.1:{port}"};
            try { await new CloudSyncClient(options).DeliverChatJobAsync(job); throw new Exception("offline_send_unexpected_success"); }
            catch (InvalidOperationException) { }
            Check(uploads == 1 && posts >= 1 && DesktopChatOutbox.Pending("outbox-account").Count == 1, "unconfirmed cloud acceptance remains durable");
            Check(DesktopChatOutbox.Find(job)!.Attachment != null && File.Exists(job.FilePath), "accepted upload metadata and snapshot survive process recreation");
            online = true;
            var restarted = new CloudSyncClient(options); await restarted.ReplayDeviceOutboxAsync();
            Check(uploads == 1 && posts >= 2, "restart retries same envelope without reupload");
            Check(DesktopChatOutbox.Find(job)!.CompletedMessageId == "msg_fixture_stable" && !File.Exists(job.FilePath), "accepted message releases immutable snapshot");
            using var accepted = JsonDocument.Parse(acceptedBody!);
            Check(accepted.RootElement.GetProperty("targetDeviceId").GetString() == "phone-target" && accepted.RootElement.GetProperty("clientMessageId").GetString() == job.Id, "target and logical identity preserved across restart");
            Check(DesktopChatOutbox.Pending("other-account").Count == 0, "another account cannot replay job");
            Console.WriteLine($"DESKTOP_OUTBOX_OFFLINE_RESTART_LOST_ACCEPTANCE_AND_ATTACHMENT_REUSE=PASSED; checks={checks}");
        } finally { lifetime.Cancel(); http.Stop(); await fixture; }
    }
}
