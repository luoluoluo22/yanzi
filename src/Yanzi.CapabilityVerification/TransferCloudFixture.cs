using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class TransferCloudFixture
{
    public static async Task RunAsync()
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:53929/");
        listener.Start();
        Console.WriteLine("Disposable transfer cloud fixture ready on 53929. Press Enter to stop.");
        var stop = Task.Run(() => { Console.ReadLine(); listener.Stop(); });
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) { break; }
            var request = context.Request;
            object body = new { error = "unknown_endpoint" };
            var status = 404;
            if (request.Headers["Authorization"] != "Bearer disposable-cloud-transfer-test") status = 401;
            else if (request.Url!.AbsolutePath == "/v1/me/devices") { status = 200; body = new { ok = true }; }
            else if (request.Url!.AbsolutePath == "/v1/me/mobile/attachments")
            {
                using var data = new MemoryStream();
                await request.InputStream.CopyToAsync(data);
                var hash = Convert.ToHexString(SHA256.HashData(data.ToArray())).ToLowerInvariant();
                status = hash == request.Headers["X-Content-Sha256"] ? 200 : 400;
                body = new { attachmentId = "att_" + Guid.NewGuid().ToString("N"), size = data.Length, sha256 = hash,
                    fileName = request.QueryString["name"], contentType = request.ContentType, expiresAt = DateTimeOffset.UtcNow.AddDays(1) };
                Console.WriteLine("FALLBACK_ATTACHMENT_BYTES=" + data.Length + ";CHECKSUM=" + (status == 200));
            }
            else if (request.Url!.AbsolutePath == "/v1/me/mobile/messages")
            {
                using var document = await JsonDocument.ParseAsync(request.InputStream);
                var message = document.RootElement;
                var id = message.GetProperty("payload").GetProperty("clientTransferId").GetString();
                status = Guid.TryParseExact(id, "N", out _) && message.GetProperty("clientMessageId").GetString() == id ? 200 : 400;
                body = new { messageId = "msg_fixture_" + id };
                Console.WriteLine("FALLBACK_MESSAGE=" + message.GetProperty("kind").GetString() + ";STABLE_ID=" + (status == 200));
            }
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        await stop;
    }
}
