using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// AGY CLI compatibility transport. Starts only when the AGY provider is used.
/// Runs on loopback; the CLI and authentication belong to the current Windows user.
/// </summary>
public static class YanziAgyModelBridge
{
    public static Task<string> EnsureAgyInstalledAsync(CancellationToken cancellation = default) =>
        YanziAgyManagedInstaller.EnsureInstalledAsync(cancellation);

    public static bool IsRunning => _listener?.IsListening == true;
    public const string ProviderType = "AGY CLI";
    public const string BaseUrl = "http://127.0.0.1:18764/v1";
    private const string ListenerPrefix = "http://127.0.0.1:18764/";
    private static readonly object Gate = new();
    private static readonly string[] ModelIds =
    [
        "agy-default", "gemini-3.8-flash-high", "gemini-3.8-flash-medium",
        "gemini-3.8-flash-low", "claude-sonnet-4-6",
        "claude-opus-4-6-thinking", "gpt-oss-120b-medium"
    ];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static HttpListener? _listener;
    private static CancellationTokenSource? _stop;
    private static string? _key;

    private static string LocalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "ManagedDependencies", "yanzi-agy-api");

    internal static string GetLocalKey()
    {
        lock (Gate)
        {
            if (_key != null) return _key;
            Directory.CreateDirectory(LocalDirectory);
            var file = Path.Combine(LocalDirectory, "api-token");
            if (!File.Exists(file))
            {
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                try
                {
                    using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var bytes = Encoding.UTF8.GetBytes(token);
                    output.Write(bytes);
                }
                catch (IOException) when (File.Exists(file)) { }
            }
            _key = File.ReadAllText(file, Encoding.UTF8).Trim();
            if (_key.Length < 32) throw new InvalidOperationException("AGY local key is invalid.");
            return _key;
        }
    }

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_listener?.IsListening == true) return;
            GetLocalKey();
            var listener = new HttpListener();
            listener.Prefixes.Add(ListenerPrefix);
            listener.Start();
            _listener = listener;
            _stop = new CancellationTokenSource();
            _ = AcceptAsync(listener, _stop.Token);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _stop?.Cancel();
            _listener?.Close();
            _listener = null;
            _stop?.Dispose();
            _stop = null;
        }
    }

    public static string? FindExistingBinary()
    {
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "agy", "bin", "agy.exe");
        return File.Exists(user) ? user : YanziAgyManagedInstaller.FindOwned();
    }

    private static async Task AcceptAsync(HttpListener listener, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(context, cancellation), cancellation);
        }
    }

    private static async Task SendJsonAsync(HttpListenerResponse response, int status, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private static object Error(string message) => new { error = new { message } };

    private static async Task HandleAsync(HttpListenerContext ctx, CancellationToken stop)
    {
        try
        {
            var auth = ctx.Request.Headers["Authorization"] ?? "";
            var expected = "Bearer " + GetLocalKey();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth),
                    Encoding.UTF8.GetBytes(expected)))
            {
                await SendJsonAsync(ctx.Response, 401, Error("unauthorized"));
                return;
            }
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (ctx.Request.HttpMethod == "GET" && path == "/health")
            {
                await SendJsonAsync(ctx.Response, 200,
                    new { status = FindExistingBinary() == null ? "missing_cli" : "ok" });
                return;
            }
            if (ctx.Request.HttpMethod == "GET" && path == "/v1/models")
            {
                await SendJsonAsync(ctx.Response, 200, new
                {
                    @object = "list",
                    data = ModelIds.Select(id => new { id, @object = "model", owned_by = "agy" }).ToArray()
                });
                return;
            }
            if (ctx.Request.HttpMethod != "POST" || path != "/v1/chat/completions")
            {
                await SendJsonAsync(ctx.Response, 404, Error("not_found"));
                return;
            }
            if (ctx.Request.ContentLength64 is <= 0 or > 524288)
            {
                await SendJsonAsync(ctx.Response, 413, Error("invalid_body_size"));
                return;
            }
            using var body = await JsonDocument.ParseAsync(ctx.Request.InputStream, cancellationToken: stop);
            var root = body.RootElement;
            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() ?? "agy-default" : "agy-default";
            if (!ModelIds.Contains(model, StringComparer.Ordinal))
            {
                await SendJsonAsync(ctx.Response, 400, Error("unsupported_model"));
                return;
            }
            if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array
                || messages.GetArrayLength() == 0)
            {
                await SendJsonAsync(ctx.Response, 400, Error("messages_required"));
                return;
            }
            var prompt = new StringBuilder();
            foreach (var message in messages.EnumerateArray())
            {
                var role = message.GetProperty("role").GetString() ?? "";
                if (role is not ("system" or "developer" or "user" or "assistant") ||
                    !message.TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.String)
                {
                    await SendJsonAsync(ctx.Response, 400, Error("unsupported_message"));
                    return;
                }
                prompt.Append('[').Append(role).AppendLine("]").AppendLine(content.GetString()).AppendLine();
            }
            var binary = FindExistingBinary() ?? await YanziAgyManagedInstaller.EnsureInstalledAsync(stop);
            if (binary == null)
            {
                await SendJsonAsync(ctx.Response, 503, Error("agy_not_installed"));
                return;
            }
            var answer = await RunAgyAsync(binary, model, prompt.ToString(), stop);
            var streaming = root.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True;
            var id = "chatcmpl-" + Guid.NewGuid().ToString("N");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (streaming)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                ctx.Response.Headers["Cache-Control"] = "no-cache";
                var first = new
                {
                    id, @object = "chat.completion.chunk", created = timestamp, model,
                    choices = new[] { new { index = 0, delta = new { role = "assistant", content = answer },
                        finish_reason = (string?)null } }
                };
                var last = new
                {
                    id, @object = "chat.completion.chunk", created = timestamp, model,
                    choices = new[] { new { index = 0, delta = new { },
                        finish_reason = "stop" } }
                };
                foreach (var payload in new[] { JsonSerializer.Serialize(first, Json), JsonSerializer.Serialize(last, Json) })
                {
                    var b = Encoding.UTF8.GetBytes("data: " + payload + "\n\n");
                    await ctx.Response.OutputStream.WriteAsync(b, stop);
                }
                await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n"), stop);
            }
            else
            {
                await SendJsonAsync(ctx.Response, 200, new
                {
                    id, @object = "chat.completion", created = timestamp, model,
                    choices = new[] { new { index = 0, finish_reason = "stop",
                        message = new { role = "assistant", content = answer } } }
                });
            }
        }
        catch (OperationCanceledException)
        {
            try { await SendJsonAsync(ctx.Response, 504, Error("agy_timeout")); } catch { }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("[AGY] Bridge error: " + ex.GetType().Name);
            try { await SendJsonAsync(ctx.Response, 502, Error("agy_execution_failed")); } catch { }
        }
        finally { try { ctx.Response.Close(); } catch { } }
    }

    private static async Task<string> RunAgyAsync(string binary, string model, string prompt, CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var start = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = LocalDirectory
        };
        foreach (var arg in new[] { "-p", prompt, "--output-format", "json",
                     "--print-timeout", "120s", "--mode", "plan", "--disable-slash-commands" })
            start.ArgumentList.Add(arg);
        if (model != "agy-default")
        {
            start.ArgumentList.Add("--model");
            start.ArgumentList.Add(model);
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("agy_start_failed");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr; // Drain diagnostics but never expose token or prompts in logs.
            if (process.ExitCode != 0) throw new InvalidOperationException("agy_nonzero_exit");
            using var result = JsonDocument.Parse(output);
            var obj = result.RootElement;
            if (!obj.TryGetProperty("status", out var status) || status.GetString() != "SUCCESS")
                throw new InvalidOperationException("agy_result_error");
            return obj.GetProperty("response").GetString() ?? "";
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }
    }
}
