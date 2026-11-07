using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OpenQuickHost;

/// <summary>
/// Account-local authenticated model adapter for the browser Cookie Suite.
/// Models are discovered from the local backend and all published model IDs
/// may be selected, including reverse, Work, image and research routes.
/// Invalid or temporarily unavailable models return upstream failures.
/// No Cookie/Token endpoint is exposed and credentials are never synchronized.
/// </summary>
public static class YanziCookieApiBridge
{
    public const string ProviderType = "Cookie API";
    public const string BaseUrl = "http://127.0.0.1:18765/v1";
    private const string Prefix = "http://127.0.0.1:18765/";
    private const string Backend = "http://127.0.0.1:18800";
    private const string Hub = "http://127.0.0.1:18888/status";
    private static readonly object Gate = new();
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(110)
    };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static HttpListener? _listener;
    private static CancellationTokenSource? _stop;
    private static string? _key;
    private static bool _ownsBackend;

    private static string Root => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "ManagedDependencies", "yanzi-cookie-api");
    private static string RuntimeFile => Path.Combine(Root, "backend-root.txt");
    public static bool IsRunning => _listener?.IsListening == true;

    public static string GetLocalKey()
    {
        lock (Gate)
        {
            if (_key != null) return _key;
            Directory.CreateDirectory(Root);
            var file = Path.Combine(Root, "api-token");
            if (!File.Exists(file))
            {
                var bytes = Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                try
                {
                    using var s = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    s.Write(bytes);
                }
                catch (IOException) when (File.Exists(file)) { }
            }
            _key = File.ReadAllText(file, Encoding.UTF8).Trim();
            if (_key.Length != 64 || !_key.All(Uri.IsHexDigit))
                throw new IOException("Invalid local Cookie API key.");
            return _key;
        }
    }

    /// <summary>The user explicitly selects an existing installation. Never clone, execute, or overwrite it automatically.</summary>
    public static void ConfigureRuntimeFolder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("请选择后台目录。");
        var full = Path.GetFullPath(directory.Trim().Trim('"'));
        if (!Directory.Exists(full) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0 ||
            !File.Exists(Path.Combine(full, "start_cookie_service.ps1")) ||
            !File.Exists(Path.Combine(full, "extension", "server.js")) ||
            !File.Exists(Path.Combine(full, ".venv", "Scripts", "python.exe")))
            throw new IOException("目录不是已安装完成的 Cookie Suite 后台。");
        Directory.CreateDirectory(Root);
        File.WriteAllText(RuntimeFile, full, new UTF8Encoding(false));
    }

    public static string? GetRuntimeFolder()
    {
        if (!File.Exists(RuntimeFile)) return null;
        try
        {
            var path = File.ReadAllText(RuntimeFile, Encoding.UTF8).Trim();
            return Directory.Exists(path) && File.Exists(Path.Combine(path, "start_cookie_service.ps1"))
                ? path : null;
        }
        catch (IOException) { return null; }
    }

    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_listener?.IsListening == true) return;
            GetLocalKey();
            var listener = new HttpListener();
            listener.Prefixes.Add(Prefix);
            listener.Start();
            _listener = listener;
            _stop = new CancellationTokenSource();
            _ = AcceptAsync(listener, _stop.Token);
            _ = YanziCookieModelCatalog.RunSyncLoopAsync(_stop.Token);
        }
    }

    public static void Stop(bool stopOwnedBackend = false)
    {
        lock (Gate)
        {
            _stop?.Cancel();
            _listener?.Close();
            _listener = null;
            _stop?.Dispose();
            _stop = null;
            if (stopOwnedBackend && _ownsBackend)
            {
                var path = GetRuntimeFolder();
                if (path != null) RunScript(path, "stop_cookie_service.ps1");
                _ownsBackend = false;
            }
        }
    }

    private static void RunScript(string root, string filename)
    {
        var script = Path.Combine(root, filename);
        if (!File.Exists(script)) return;
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new IOException("Unable to launch Cookie Suite.");
        if (!process.WaitForExit(15000)) throw new TimeoutException("Cookie Suite start script timed out.");
        if (process.ExitCode != 0) throw new IOException("Cookie Suite start script failed.");
    }

    private static async Task<bool> IsOnlineAsync(string url, CancellationToken stop)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await Client.SendAsync(req, stop);
            return res.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return false;
        }
    }

    public static async Task<(bool Backend, bool Extension)> GetStatusAsync(CancellationToken stop = default)
    {
        var backend = await IsOnlineAsync(Backend + "/api/hub/status", stop);
        if (!backend) return (false, false);
        try
        {
            using var res = await Client.GetAsync(Hub, stop);
            using var document = JsonDocument.Parse(await res.Content.ReadAsStreamAsync(stop));
            return (true, document.RootElement.TryGetProperty("extensionConnected", out var connected)
                && connected.ValueKind == JsonValueKind.True);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (true, false);
        }
    }

    public static async Task<bool> EnsureBackendStartedAsync(CancellationToken stop = default)
    {
        var status = await GetStatusAsync(stop);
        if (status.Backend) return true;
        var root = GetRuntimeFolder();
        if (root == null) return false;
        await Task.Run(() => RunScript(root, "start_cookie_service.ps1"), stop);
        _ownsBackend = true;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500, stop);
            if ((await GetStatusAsync(stop)).Backend) return true;
        }
        return false;
    }

    private static async Task AcceptAsync(HttpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(ctx, stop));
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                break;
            }
        }
    }

    private static async Task RespondAsync(HttpListenerResponse res, int status, object data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.Headers["Cache-Control"] = "no-store";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }
    private static object Error(string code) => new { error = new { message = code, type = "cookie_api_error" } };

    private static readonly Regex Elicitations = new(
        @"<ElicitationsGroup\b[^>]*>.*?</ElicitationsGroup>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(200));

    private static bool LooksLikeUpstreamFailure(string answer)
    {
        var value = answer.Trim();
        return value.StartsWith("Sorry, something went wrong", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("I encountered an error doing what you asked", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("Something went wrong. Please", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task HandleAsync(HttpListenerContext ctx, CancellationToken stop)
    {
        try
        {
            var request = ctx.Request;
            if (request.Headers["Origin"] is { Length: > 0 })
            {
                await RespondAsync(ctx.Response, 403, Error("browser_origin_not_allowed"));
                return;
            }
            var auth = request.Headers["Authorization"] ?? "";
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(auth), Encoding.UTF8.GetBytes("Bearer " + GetLocalKey())))
            {
                await RespondAsync(ctx.Response, 401, Error("unauthorized"));
                return;
            }

            var path = request.Url?.AbsolutePath;
            if (request.HttpMethod == "GET" && path == "/v1/models")
            {
                var modelsCatalog = await YanziCookieModelCatalog.SyncRegisteredModelsAsync(
                    cancellationToken: stop);
                await RespondAsync(ctx.Response, 200, new
                {
                    @object = "list",
                    data = modelsCatalog.SelectableModels.Select(m => new
                    {
                        id = m, @object = "model", owned_by = "cookie-api"
                    }),
                    catalog_stale = modelsCatalog.IsStale
                });
                return;
            }
            if (request.HttpMethod == "GET" && path == "/v1/catalog")
            {
                var directory = await YanziCookieModelCatalog.RefreshAsync(
                    cancellationToken: stop);
                await RespondAsync(ctx.Response, 200, new
                {
                    backend_online = directory.BackendOnline,
                    reverse_online = directory.ReverseOnline,
                    stale = directory.IsStale,
                    checked_at = directory.CheckedAt,
                    discovered = directory.Models.Count,
                    selectable = directory.SelectableModels.Count,
                    models = directory.Models
                });
                return;
            }
            if (request.HttpMethod == "GET" && path == "/health")
            {
                var status = await GetStatusAsync(stop);
                await RespondAsync(ctx.Response, status.Backend ? 200 : 503,
                    new { backend = status.Backend, extension = status.Extension });
                return;
            }
            if (request.HttpMethod != "POST" || path is not ("/v1/chat/completions" or "/v1/responses"))
            {
                await RespondAsync(ctx.Response, 404, Error("not_found"));
                return;
            }
            if (request.ContentLength64 is <= 0 or > 131072)
            {
                await RespondAsync(ctx.Response, 413, Error("body_too_large"));
                return;
            }
            using var doc = await JsonDocument.ParseAsync(request.InputStream,
                new JsonDocumentOptions { MaxDepth = 24 }, stop);
            var input = doc.RootElement;
            if (input.ValueKind != JsonValueKind.Object ||
                !input.TryGetProperty("model", out var specified) || specified.ValueKind != JsonValueKind.String)
            {
                await RespondAsync(ctx.Response, 400, Error("model_required"));
                return;
            }
            var model = specified.GetString() ?? "";
            var streaming = input.TryGetProperty("stream", out var streamProp) &&
                            streamProp.ValueKind == JsonValueKind.True;
            if (!await EnsureBackendStartedAsync(stop))
            {
                await RespondAsync(ctx.Response, 503, Error("backend_not_installed_or_offline"));
                return;
            }
            var catalog = await YanziCookieModelCatalog.RefreshAsync(
                cancellationToken: stop);
            if (!catalog.SelectableModels.Contains(model, StringComparer.Ordinal))
            {
                await RespondAsync(ctx.Response, 404, Error("model_not_in_backend_catalog"));
                return;
            }
            if (path == "/v1/responses")
            {
                // Preserve function calls, call IDs, reasoning items, errors and SSE events.
                // Text-only backends must fail explicitly rather than simulate native tools.
                using var nativeRequest = new HttpRequestMessage(HttpMethod.Post, Backend + "/v1/responses")
                {
                    Content = new StringContent(input.GetRawText(), Encoding.UTF8, "application/json")
                };
                using var nativeResponse = await Client.SendAsync(nativeRequest, HttpCompletionOption.ResponseHeadersRead, stop);
                if (nativeResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                {
                    await RespondAsync(ctx.Response, 501, Error("responses_native_tools_not_supported_by_backend"));
                    return;
                }
                ctx.Response.StatusCode = (int)nativeResponse.StatusCode;
                ctx.Response.ContentType = nativeResponse.Content.Headers.ContentType?.ToString() ?? "application/json";
                ctx.Response.SendChunked = true;
                ctx.Response.Headers["Cache-Control"] = "no-store";
                await nativeResponse.Content.CopyToAsync(ctx.Response.OutputStream, stop);
                return;
            }
            // Only the DOM-based ChatGPT models need an active Ego extension.
            // Reverse and Gemini models must not be gated by the browser connection.
            var requiresEgo = catalog.Models.Any(m =>
                string.Equals(m.Id, model, StringComparison.Ordinal) &&
                m.Category == "browser");
            if (requiresEgo)
            {
                var online = await GetStatusAsync(stop);
                if (!online.Extension)
                {
                    await RespondAsync(ctx.Response, 503, Error("ego_extension_not_connected"));
                    return;
                }
            }
            if (requiresEgo)
            {
                // The browser-backed ChatGPT transport requires an actual tab.
                // Restore it on demand rather than silently turning every call into HTTP 502.
                try
                {
                    using var tabsResponse = await Client.GetAsync("http://127.0.0.1:18888/tabs", stop);
                    using var tabsJson = JsonDocument.Parse(await tabsResponse.Content.ReadAsStreamAsync(stop));
                    var tabs = tabsJson.RootElement.TryGetProperty("tabs", out var items) &&
                        items.ValueKind == JsonValueKind.Array ? items : default;
                    var hasTab = tabs.ValueKind == JsonValueKind.Array &&
                        tabs.EnumerateArray().Any(t => t.TryGetProperty("url", out var url) &&
                            url.ValueKind == JsonValueKind.String &&
                            Uri.TryCreate(url.GetString(), UriKind.Absolute, out var u) &&
                            (u.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
                             u.Host.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase)));
                    if (!hasTab)
                    {
                        using var opened = await Client.GetAsync("http://127.0.0.1:18888/chatgpt/open", stop);
                        if (!opened.IsSuccessStatusCode)
                        {
                            await RespondAsync(ctx.Response, 503, Error("chatgpt_tab_not_available"));
                            return;
                        }
                        await Task.Delay(1500, stop);
                    }
                }
                catch (Exception e) when (e is HttpRequestException or JsonException)
                {
                    await RespondAsync(ctx.Response, 503, Error("chatgpt_tab_check_failed"));
                    return;
                }
            }
            var payload = JsonNode.Parse(input.GetRawText())!.AsObject();
            payload["stream"] = false; // Convert complete upstream response to SSE below if requested.
            using var forward = new HttpRequestMessage(HttpMethod.Post, Backend + "/v1/chat/completions")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            using var result = await Client.SendAsync(forward, stop);
            if (!result.IsSuccessStatusCode)
            {
                await RespondAsync(ctx.Response, 502, Error("upstream_http_" + (int)result.StatusCode));
                return;
            }
            var raw = await result.Content.ReadAsStringAsync(stop);
            var parsed = JsonNode.Parse(raw)?.AsObject();
            if (parsed?["choices"] is not JsonArray choices || choices.Count == 0 ||
                choices[0]?["message"]?["content"] is not JsonValue original)
            {
                await RespondAsync(ctx.Response, 502, Error("invalid_upstream_response"));
                return;
            }
            var answer = original.GetValue<string>();
            if (LooksLikeUpstreamFailure(answer))
            {
                await RespondAsync(ctx.Response, 502, Error("gemini_upstream_generation_failed"));
                return;
            }
            var normalized = Elicitations.Replace(answer, "").Trim();
            if (normalized.Length == 0)
            {
                await RespondAsync(ctx.Response, 502, Error("empty_upstream_response"));
                return;
            }
            choices[0]!["message"]!["content"] = normalized;
            if (!streaming)
            {
                await RespondAsync(ctx.Response, 200, parsed);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            var chunk = new
            {
                id = parsed["id"]?.GetValue<string>() ?? "chatcmpl-cookie",
                @object = "chat.completion.chunk",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model,
                choices = new[] { new { index = 0, delta = new { content = normalized }, finish_reason = "stop" } }
            };
            var eventBytes = Encoding.UTF8.GetBytes("data: " + JsonSerializer.Serialize(chunk) + "\n\n" +
                                                    "data: [DONE]\n\n");
            await ctx.Response.OutputStream.WriteAsync(eventBytes, stop);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or
                                        TimeoutException or IOException or InvalidOperationException or
                                        RegexMatchTimeoutException)
        {
            try { await RespondAsync(ctx.Response, 502, Error(e.GetType().Name)); } catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }
}
