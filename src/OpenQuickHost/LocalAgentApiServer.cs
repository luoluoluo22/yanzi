using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer : IDisposable
{
    private WebSocket? _activeBrowserSocket;
    private static int _nextWsId;
    public event Action<bool>? BrowserConnectionChanged;
    public bool IsBrowserConnected => _activeBrowserSocket != null && _activeBrowserSocket.State == WebSocketState.Open;
    public string ConnectedBrowserName { get; private set; } = "";

    private static readonly (string ProcessName, string DisplayName)[] SupportedBrowserProcesses =
    [
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi")
    ];
    private static readonly object BrowserProcessCacheLock = new();
    private static string[] _cachedRunningBrowserNames = [];
    private static long _browserProcessCacheExpiresAt;

    public static IReadOnlyList<string> GetRunningBrowserNames()
    {
        var now = Environment.TickCount64;
        lock (BrowserProcessCacheLock)
        {
            if (now < _browserProcessCacheExpiresAt)
                return _cachedRunningBrowserNames;

            var result = new List<string>();
            foreach (var (processName, displayName) in SupportedBrowserProcesses)
            {
                System.Diagnostics.Process[] processes = [];
                try
                {
                    processes = System.Diagnostics.Process.GetProcessesByName(processName);
                    if (processes.Length > 0)
                        result.Add(displayName);
                }
                catch
                {
                    // Process enumeration can fail transiently; treat that browser as unknown/not detected.
                }
                finally
                {
                    foreach (var process in processes)
                    {
                        try { process.Dispose(); } catch { }
                    }
                }
            }

            _cachedRunningBrowserNames = result.ToArray();
            _browserProcessCacheExpiresAt = now + 2000;
            return _cachedRunningBrowserNames;
        }
    }

    public bool IsSupportedBrowserRunning => GetRunningBrowserNames().Count > 0;

    private object BuildBrowserUnavailableResponse()
    {
        var runningBrowsers = GetRunningBrowserNames();
        var browserRunning = runningBrowsers.Count > 0;
        HostAssets.AppendLog(browserRunning
            ? $"[LocalAgentApi] Browser unavailable: browser_extension_not_connected, detected={string.Join(",", runningBrowsers)}"
            : "[LocalAgentApi] Browser unavailable: browser_not_running, no supported browser process detected.");

        return new
        {
            error = browserRunning
                ? "browser_extension_not_connected"
                : "browser_not_running",
            message = browserRunning
                ? $"检测到 {string.Join("、", runningBrowsers)} 正在运行，但燕子浏览器助手未连接。"
                : "未检测到已运行的受支持浏览器，请先打开 Edge、Chrome 或其他已安装燕子浏览器助手的浏览器。",
            browserRunning,
            extensionConnected = false,
            detectedBrowsers = runningBrowsers
        };
    }
    public static string LastKnownMobileDeviceModel { get; set; } = "";
    public static event Action<string>? MobileDeviceConnected;

    private static readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingBrowserTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _browserSendGate = new(1, 1);

    public async Task<(bool success, string taskId, string status, string? message, JsonElement? data)> RunBrowserTaskAsync(
        IReadOnlyDictionary<string, object?> payload,
        int timeoutSeconds = 30)
    {
        var browserSocket = _activeBrowserSocket;
        if (browserSocket == null || browserSocket.State != WebSocketState.Open)
        {
            var runningBrowsers = GetRunningBrowserNames();
            var message = runningBrowsers.Count > 0
                ? $"检测到 {string.Join("、", runningBrowsers)} 正在运行，但燕子浏览器助手未连接。"
                : "未检测到已运行的受支持浏览器，请先打开浏览器。";
            return (false, "", "unavailable", message, null);
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 120);
        var taskId = Guid.NewGuid().ToString("N");
        var taskPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in payload)
            taskPayload[pair.Key] = pair.Value;

        taskPayload["type"] = "task_request";
        taskPayload["taskId"] = taskId;
        if (!taskPayload.ContainsKey("action"))
            taskPayload["action"] = "workflow";

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingBrowserTasks[taskId] = tcs;

        try
        {
            var sendBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(taskPayload));
            await _browserSendGate.WaitAsync(_cts.Token);
            try
            {
                browserSocket = _activeBrowserSocket;
                if (browserSocket == null || browserSocket.State != WebSocketState.Open)
                    return (false, taskId, "unavailable", "燕子浏览器助手连接已断开。", null);

                await browserSocket.SendAsync(
                    new ArraySegment<byte>(sendBuffer),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token);
            }
            finally
            {
                _browserSendGate.Release();
            }

            using var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var completed = await Task.WhenAny(
                tcs.Task,
                Task.Delay(Timeout.Infinite, delayCts.Token));

            if (completed != tcs.Task)
                return (false, taskId, "timeout", $"浏览器任务超过 {timeoutSeconds} 秒未返回。", null);

            var result = await tcs.Task;
            var status = result.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? ""
                : "";
            var message = result.TryGetProperty("message", out var messageProp)
                ? messageProp.GetString()
                : null;
            JsonElement? data = result.TryGetProperty("data", out var dataProp)
                ? dataProp.Clone()
                : null;

            return (
                string.Equals(status, "success", StringComparison.OrdinalIgnoreCase),
                taskId,
                status,
                message,
                data);
        }
        catch (Exception ex)
        {
            return (false, taskId, "error", ex.Message, null);
        }
        finally
        {
            _pendingBrowserTasks.TryRemove(taskId, out _);
        }
    }

    public async Task<(bool success, string? jsonResult, string? error)> RunBrowserAiPromptTransferAsync(
        string prompt,
        string aiSite = "deepseek",
        int timeoutSeconds = 120,
        bool isNewSession = false)
    {
        if (_activeBrowserSocket == null || _activeBrowserSocket.State != WebSocketState.Open)
        {
            return (false, null, "燕子浏览器助手未连接。请先打开浏览器并确保已启用“燕子浏览器助手”插件。");
        }

        var taskId = Guid.NewGuid().ToString("N");
        var taskPayload = new Dictionary<string, object>
        {
            ["type"] = "task_request",
            ["taskId"] = taskId,
            ["action"] = "ai_prompt_transfer",
            ["aiSite"] = aiSite,
            ["prompt"] = prompt,
            ["timeoutSeconds"] = timeoutSeconds,
            ["isNewSession"] = isNewSession
        };

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingBrowserTasks[taskId] = tcs;

        try
        {
            var sendBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(taskPayload));
            // The same browser socket is shared with other tasks: serialize writes.
            // Bound a stalled send independently of the AI generation timeout.
            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            sendCts.CancelAfter(TimeSpan.FromSeconds(15));
            await _browserSendGate.WaitAsync(sendCts.Token);
            try
            {
                var socket = _activeBrowserSocket;
                if (socket == null || socket.State != WebSocketState.Open)
                {
                    _pendingBrowserTasks.TryRemove(taskId, out _);
                    return (false, null, "浏览器助手连接已断开，请重新连接后重试。");
                }

                await socket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, sendCts.Token);
            }
            finally
            {
                _browserSendGate.Release();
            }

            HostAssets.AppendLog($"[LocalAgentApi] Dispatched AI prompt transfer task to browser extension: taskId={taskId}, site={aiSite}, promptLength={prompt.Length}");
        }
        catch (Exception ex)
        {
            _pendingBrowserTasks.TryRemove(taskId, out _);
            return (false, null, $"向浏览器助手发送指令失败: {ex.Message}");
        }

        using (var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 10)))
        {
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, delayCts.Token));
            if (completedTask == tcs.Task)
            {
                _pendingBrowserTasks.TryRemove(taskId, out _);
                var resultDoc = await tcs.Task;
                string? status = resultDoc.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
                string? message = resultDoc.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : null;

                if (status == "success")
                {
                    if (resultDoc.TryGetProperty("data", out var dataProp) &&
                        dataProp.TryGetProperty("rawJson", out var jsonProp))
                    {
                        var rawJson = jsonProp.GetString();
                        return (true, rawJson, null);
                    }
                    return (false, null, "已从 AI 页面完成生成，但未提取到有效的 JSON 代码块。");
                }
                else
                {
                    return (false, null, string.IsNullOrWhiteSpace(message) ? "DeepSeek 网页端生成异常或超时" : message);
                }
            }
            else
            {
                _pendingBrowserTasks.TryRemove(taskId, out _);
                return (false, null, $"AI 响应超时（等待超过 {timeoutSeconds} 秒未返回）");
            }
        }
    }

    private readonly string _prefix;
    private readonly string _token;
    // Exposed only inside the desktop application for its local Settings window.
    // Never serialize this into /docs or any unauthenticated API response.
    internal string TokenForSettings => _token;
    internal bool IsListening => _listener.IsListening;
    private readonly Action<string?> _onMutated;
    private readonly Action? _onTriggerSync;
    private readonly Func<string, Task<(bool ok, string message)>>? _onPublishExtension;
    private readonly Func<string, Task<(bool ok, string message)>>? _onUnpublishExtension;
    private readonly Func<string, Task<(bool ok, string message)>>? _onInstallExtension;
    private readonly Func<string, Task<(bool ok, string message)>>? _onDeleteExtension;
    private readonly Func<Task<AuthMeResponse?>>? _onGetMe;
    private readonly Func<string, string, Task>? _onShowNotification;
    private readonly Func<string, string, Task>? _onPushToMobile;
    private readonly Func<DeviceMessageRecord, Task<(bool success, string output)>>? _onMobileMessage;
    private readonly Action<string, bool>? _onSettingsChanged;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new(StringComparer.OrdinalIgnoreCase);
    // Prevent concurrent deployment, execution, and stop calls from racing with a reload.
    private static readonly ConcurrentDictionary<string, byte> _deployingExtensions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, LocalMobileMessageDetail> _localMobileMessages = new(StringComparer.OrdinalIgnoreCase);

    public LocalAgentApiServer(
        string prefix,
        string token,
        Action<string?> onMutated,
        Action? onTriggerSync = null,
        Func<string, Task<(bool ok, string message)>>? onPublishExtension = null,
        Func<string, Task<(bool ok, string message)>>? onUnpublishExtension = null,
        Func<string, Task<(bool ok, string message)>>? onInstallExtension = null,
        Func<Task<AuthMeResponse?>>? onGetMe = null,
        Func<string, string, Task>? onShowNotification = null,
        Func<string, string, Task>? onPushToMobile = null,
        Func<DeviceMessageRecord, Task<(bool success, string output)>>? onMobileMessage = null,
        Action<string, bool>? onSettingsChanged = null,
        Func<string, Task<(bool ok, string message)>>? onDeleteExtension = null)
    {
        _prefix = prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
        _token = token;
        _onMutated = onMutated;
        _onTriggerSync = onTriggerSync;
        _onPublishExtension = onPublishExtension;
        _onUnpublishExtension = onUnpublishExtension;
        _onInstallExtension = onInstallExtension;
        _onDeleteExtension = onDeleteExtension;
        _onGetMe = onGetMe;
        _onShowNotification = onShowNotification;
        _onPushToMobile = onPushToMobile;
        _onMobileMessage = onMobileMessage;
        _onSettingsChanged = onSettingsChanged;
        _listener.Prefixes.Add(_prefix);
    }

    public void Start()
    {
        YanziBuiltinCapabilityRegistration.Register();
        _listener.Start();
        _loopTask = Task.Run(ListenLoopAsync);
        HostAssets.AppendLog($"Local Agent API started at {_prefix}");
    }

    public void Stop()
    {
        EndAllUiSessions("agent_api_stopped");
        _cts.Cancel();
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore graceful shutdown errors.
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
            }
            catch (HttpListenerException)
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Local Agent API listen error: {ex.Message}");
            }
        }
    }

    private static string ResolveFsPath(string? path)
    {
        return PathHelper.ResolveFsPath(path);
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Yanzi-Token";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, PATCH, DELETE, OPTIONS";

        try
        {
            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            if (await TrySecureLanAsync(request, response, path)) return;
            if (request.RemoteEndPoint is { } remotePeer && !System.Net.IPAddress.IsLoopback(remotePeer.Address))
            {
                await WriteJsonAsync(response, 426, new { error = "encrypted_lan_required", protocol = "yanzi.lan.aead.v1" });
                return;
            }
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (path == "/v1/browser/ws")
            {
                var settings = AppSettingsStore.Load();
                if (!settings.EnableBrowserHelper)
                {
                    await WriteJsonAsync(response, 403, new { error = "browser_helper_disabled_by_settings" });
                    return;
                }

                if (context.Request.IsWebSocketRequest)
                {
                    var socketId = Interlocked.Increment(ref _nextWsId);
                    try
                    {
                        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                        var webSocket = wsContext.WebSocket;

                        var userAgent = context.Request.Headers["User-Agent"] ?? "";
                        var browserName = "浏览器";
                        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
                        {
                            browserName = "Edge";
                        }
                        else if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
                        {
                            browserName = "Chrome";
                        }
                        else if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
                        {
                            browserName = "Firefox";
                        }
                        else if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase))
                        {
                            browserName = "Safari";
                        }
                        ConnectedBrowserName = browserName;

                        var oldSocket = Interlocked.Exchange(ref _activeBrowserSocket, webSocket);
                        if (oldSocket != null && oldSocket != webSocket)
                        {
                            HostAssets.AppendLog($"[BrowserWS #{socketId}] Replaced existing WebSocket session.");
                            try { await oldSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "New connection established", CancellationToken.None); } catch {}
                            oldSocket.Dispose();
                        }

                        BrowserConnectionChanged?.Invoke(true);
                        HostAssets.AppendLog($"[BrowserWS #{socketId}] Connected from {browserName}.");

                        await HandleBrowserWebSocketLoopAsync(webSocket, socketId, browserName);
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"[BrowserWS #{socketId}] Accept error: {ex.Message}");
                        try { response.StatusCode = 500; response.Close(); } catch {}
                    }
                }
                else
                {
                    await WriteJsonAsync(response, 400, new { error = "websocket_required" });
                }
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/browser/status")
            {
                if (!IsAuthorized(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                    return;
                }

                var extensionConnected = IsBrowserConnected;
                var runningBrowsers = extensionConnected
                    ? (IReadOnlyList<string>)(string.IsNullOrWhiteSpace(ConnectedBrowserName)
                        ? Array.Empty<string>()
                        : new[] { ConnectedBrowserName })
                    : GetRunningBrowserNames();
                var browserRunning = extensionConnected || runningBrowsers.Count > 0;
                var state = extensionConnected
                    ? "connected"
                    : browserRunning
                        ? "browser_extension_not_connected"
                        : "browser_not_running";

                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    state,
                    browserRunning,
                    extensionConnected,
                    connectedBrowser = extensionConnected ? ConnectedBrowserName : "",
                    detectedBrowsers = runningBrowsers
                });
                return;
            }
            if (request.HttpMethod == "POST" && path == "/v1/browser/reload")
            {
                if (!IsAuthorized(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                    return;
                }

                var browserSocket = _activeBrowserSocket;
                if (browserSocket == null || browserSocket.State != WebSocketState.Open)
                {
                    await WriteJsonAsync(response, 503, BuildBrowserUnavailableResponse());
                    return;
                }

                var requestId = Guid.NewGuid().ToString("N");
                var payload = JsonSerializer.Serialize(new
                {
                    type = "browser_extension_reload",
                    requestId
                });

                try
                {
                    var sendBuffer = Encoding.UTF8.GetBytes(payload);
                    await browserSocket.SendAsync(
                        new ArraySegment<byte>(sendBuffer),
                        WebSocketMessageType.Text,
                        true,
                        _cts.Token);
                    HostAssets.AppendLog($"[LocalAgentApi] Browser extension reload requested: requestId={requestId}, browser={ConnectedBrowserName}");
                    await WriteJsonAsync(response, 202, new
                    {
                        ok = true,
                        status = "accepted",
                        requestId,
                        browser = ConnectedBrowserName
                    });
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"[LocalAgentApi] Browser extension reload dispatch failed: {ex.Message}");
                    await WriteJsonAsync(response, 500, new { error = "failed_to_send_reload: " + ex.Message });
                }
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/browser/execute")
            {
                if (!IsAuthorized(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                    return;
                }

                if (_activeBrowserSocket == null || _activeBrowserSocket.State != WebSocketState.Open)
                {
                    await WriteJsonAsync(response, 503, BuildBrowserUnavailableResponse());
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                var taskId = Guid.NewGuid().ToString("N");
                var taskPayload = new Dictionary<string, object>();
                var executionTimeoutSeconds = 30;
                try
                {
                    var jsonDoc = JsonDocument.Parse(body);
                    foreach (var prop in jsonDoc.RootElement.EnumerateObject())
                    {
                        taskPayload[prop.Name] = prop.Value;
                        if (
                            prop.NameEquals("timeoutSeconds") &&
                            prop.Value.ValueKind == JsonValueKind.Number &&
                            prop.Value.TryGetInt32(out var requestedTimeoutSeconds)
                        )
                        {
                            executionTimeoutSeconds = Math.Clamp(requestedTimeoutSeconds, 5, 600);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await WriteJsonAsync(response, 400, new { error = "invalid_json_body: " + ex.Message });
                    return;
                }

                taskPayload["type"] = "task_request";
                taskPayload["taskId"] = taskId;
                if (!taskPayload.ContainsKey("action"))
                {
                    taskPayload["action"] = "workflow";
                }

                var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingBrowserTasks[taskId] = tcs;

                try
                {
                    var sendBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(taskPayload));
                    await _activeBrowserSocket.SendAsync(new ArraySegment<byte>(sendBuffer), WebSocketMessageType.Text, true, _cts.Token);
                }
                catch (Exception ex)
                {
                    _pendingBrowserTasks.TryRemove(taskId, out _);
                    await WriteJsonAsync(response, 500, new { error = "failed_to_send_to_extension: " + ex.Message });
                    return;
                }

                using (var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(executionTimeoutSeconds)))
                {
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, delayCts.Token));
                    if (completedTask == tcs.Task)
                    {
                        var resultDoc = await tcs.Task;
                        var responseData = new Dictionary<string, object?>();
                        responseData["taskId"] = taskId;

                        if (resultDoc.TryGetProperty("status", out var statusProp))
                            responseData["status"] = statusProp.GetString();
                        if (resultDoc.TryGetProperty("message", out var msgProp))
                            responseData["message"] = msgProp.GetString();
                        if (resultDoc.TryGetProperty("data", out var dataProp))
                            responseData["data"] = dataProp;

                        int httpStatus = 200;
                        if (responseData.TryGetValue("status", out var st) && st?.ToString() == "error")
                        {
                            httpStatus = 500;
                        }

                        await WriteJsonAsync(response, httpStatus, responseData);
                    }
                    else
                    {
                        _pendingBrowserTasks.TryRemove(taskId, out _);
                        await WriteJsonAsync(response, 504, new
                        {
                            error = "browser_execution_timeout",
                            timeoutSeconds = executionTimeoutSeconds
                        });
                    }
                }
                return;
            }

            if (request.HttpMethod == "GET" && (path == "/docs" || path == "/" || path == "/index.html"))
            {
                // The console holds privileged credentials; never execute third-party scripts.
                response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
                    "connect-src 'self'; img-src 'self' blob: data:; " +
                    "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
                response.Headers["Cache-Control"] = "no-store";
                response.Headers["X-Content-Type-Options"] = "nosniff";
                response.ContentType = "text/html; charset=utf-8";
                response.StatusCode = 200;
                var html = GetDocsHtml();
                var buffer = Encoding.UTF8.GetBytes(html);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, _cts.Token);
                response.Close();
                return;
            }

            if (request.HttpMethod == "POST" && path == "/v1/notify")
            {
                if (!IsAuthorized(request))
                {
                    await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                string title = "Yanzi 通知";
                string text = "";

                try
                {
                    var json = JsonDocument.Parse(body).RootElement;
                    if (json.TryGetProperty("title", out var t)) title = t.GetString() ?? title;
                    if (json.TryGetProperty("body", out var b)) text = b.GetString() ?? text;
                } catch {}

                if (_onPushToMobile != null && !string.IsNullOrEmpty(text))
                {
                    _ = Task.Run(() => _onPushToMobile(title, text));
                }

                await WriteJsonAsync(response, 200, new { success = true });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/store/extensions/status")
            {
                var ids = ParseIds(GetQueryString(request, "ids"));
                var commands = LocalExtensionCatalog.LoadCommands()
                    .Where(command => ids.Count == 0 || ids.Contains(command.ExtensionId))
                    .Select(command => new
                    {
                        extensionId = command.ExtensionId,
                        installed = true,
                        version = command.DeclaredVersion,
                        title = command.Title
                    })
                    .ToList();
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    items = commands
                });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonAsync(response, 200, new { ok = true, service = "yanzi-local-agent-api" });
                return;
            }

            if (await TryHandleConsoleLogoutAsync(request, response, path))
                return;
            if (!IsAuthorized(request))
            {
                await WriteJsonAsync(response, 401, new { error = "unauthorized" });
                return;
            }
            if (await GetAuthenticatedRouter().TryHandleAsync(request, response, path)) return;

            await WriteJsonAsync(response, 404, new { error = "not_found" });
        }
        catch (FileNotFoundException ex)
        {
            await WriteJsonAsync(response, 404, new { error = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            await WriteJsonAsync(response, 404, new { error = ex.Message });
        }
        catch (UiApiException ex)
        {
            await WriteJsonAsync(response, ex.Status, new { ok = false, error = ex.Code });
        }
        catch (InvalidOperationException ex)
        {
            await WriteJsonAsync(response, 400, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Local Agent API request error: {ex.Message}");
            await WriteJsonAsync(response, 500, new { error = "internal_error", detail = ex.Message });
        }
    }

    private LocalApiRouter? _authenticatedRouter;
    private LocalApiRouter GetAuthenticatedRouter()
    {
        var existing = Volatile.Read(ref _authenticatedRouter);
        if (existing != null) return existing;
        var router = new LocalApiRouter();
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/me/devices/peers", HandleAccountRoute0Async);
        router.AddHandled(TryHandleMobileDeviceApiAsync);
        router.AddHandled(TryCompanionFiles);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/me/devices/protocol", HandleAccountRoute3Async);
        router.AddHandled(TryHandleConsoleSessionApiAsync);
        router.AddHandled(TryPairingApiAsync);
        router.AddHandled(TryHandleCapabilityApiAsync);
        router.AddHandled(TryTransferSessionsAsync);
        router.AddHandled(TryHandleLanTransferApiAsync);
        router.AddHandled(TryHandleUiApiAsync);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/clipboard/sync", HandleAccountRoute10Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/shell/run", HandleFilesRoute11Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/fs/list", HandleFilesRoute12Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/fs/read", HandleFilesRoute13Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/fs/write", HandleFilesRoute14Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/me/devices", HandleAccountRoute15Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/logs", HandleAccountRoute16Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/extensions/template", HandleExtensionsRoute17Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/extensions", HandleExtensionsRoute18Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/me/extensions", HandleExtensionsRoute19Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/quickpanel/groups", HandleSettingsRoute20Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/quickpanel/groups", HandleSettingsRoute21Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/quickpanel/add", HandleSettingsRoute22Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/app/notify", HandleAccountRoute23Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/me/mobile/messages", HandleAccountRoute24Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path.StartsWith("/v1/me/mobile/messages/", StringComparison.Ordinal), HandleAccountRoute25Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/build", StringComparison.Ordinal), HandleExtensionsRoute26Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/reload", StringComparison.Ordinal), HandleExtensionsRoute27Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.EndsWith("/run", StringComparison.Ordinal) && path.StartsWith("/v1/extensions/", StringComparison.Ordinal), HandleExtensionsRoute28Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/logs", StringComparison.Ordinal), HandleExtensionsRoute29Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/status", StringComparison.Ordinal), HandleExtensionsRoute30Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/stop", StringComparison.Ordinal), HandleExtensionsRoute31Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/webview/", StringComparison.Ordinal) && path.EndsWith("/execute", StringComparison.Ordinal), HandleExtensionsRoute32Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/sync/webdav-config", HandleAccountRoute33Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/sync/personal-config", HandleAccountRoute34Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/sync/trigger", HandleAccountRoute35Async);
        router.Add((request, path) => path.StartsWith("/v1/account-storage/", StringComparison.Ordinal), HandleExtensionsRoute36Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path.StartsWith("/v1/storage/", StringComparison.Ordinal), HandleExtensionsRoute37Async);
        router.Add((request, path) => request.HttpMethod == "DELETE" && path.StartsWith("/v1/storage/", StringComparison.Ordinal), HandleExtensionsRoute38Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && !path.StartsWith("/v1/extensions/recycle-bin", StringComparison.Ordinal), HandleExtensionsRoute39Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path == "/v1/extensions", HandleExtensionsRoute40Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/storage/", StringComparison.Ordinal) && path.EndsWith("/sync", StringComparison.Ordinal), HandleExtensionsRoute41Async);
        router.Add((request, path) => request.HttpMethod == "PUT" && path.StartsWith("/v1/storage/", StringComparison.Ordinal), HandleExtensionsRoute42Async);
        router.Add((request, path) => request.HttpMethod == "PUT" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal), HandleExtensionsRoute43Async);
        router.Add((request, path) => request.HttpMethod == "PATCH" && path.EndsWith("/rename", StringComparison.Ordinal), HandleAccountRoute44Async);
        router.Add((request, path) => request.HttpMethod == "PATCH" && path.EndsWith("/shortcut", StringComparison.Ordinal), HandleAccountRoute45Async);
        router.Add((request, path) => request.HttpMethod == "DELETE" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && !path.StartsWith("/v1/extensions/recycle-bin", StringComparison.Ordinal), HandleExtensionsRoute46Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/publish", StringComparison.Ordinal), HandleExtensionsRoute47Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/", StringComparison.Ordinal) && path.EndsWith("/unpublish", StringComparison.Ordinal), HandleExtensionsRoute48Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/extensions/recycle-bin", HandleExtensionsRoute49Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/extensions/recycle-bin/", StringComparison.Ordinal) && path.EndsWith("/restore", StringComparison.Ordinal), HandleExtensionsRoute50Async);
        router.Add((request, path) => request.HttpMethod == "DELETE" && path.StartsWith("/v1/extensions/recycle-bin/", StringComparison.Ordinal), HandleExtensionsRoute51Async);
        router.Add((request, path) => request.HttpMethod == "POST" && path.StartsWith("/v1/store/extensions/", StringComparison.Ordinal) && path.EndsWith("/install", StringComparison.Ordinal), HandleExtensionsRoute52Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/user/me", HandleAccountRoute53Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/settings", HandleSettingsRoute54Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/me/yanm-state", HandleSettingsRoute55Async);
        router.Add((request, path) => request.HttpMethod == "PUT" && path == "/v1/me/yanm-state", HandleSettingsRoute56Async);
        router.Add((request, path) => request.HttpMethod == "PUT" && path == "/v1/me/yanm-state/component-state", HandleSettingsRoute57Async);
        router.Add((request, path) => request.HttpMethod == "GET" && path == "/v1/me/mobile/extensions", HandleSettingsRoute58Async);
        router.Add((request, path) => request.HttpMethod == "PUT" && path == "/v1/me/mobile/extensions", HandleSettingsRoute59Async);
        router.Add((request, path) => request.HttpMethod == "PATCH" && path == "/v1/settings", HandleSettingsRoute60Async);
        router.Add((request, path) => request.HttpMethod == "DELETE" && path == "/v1/quickpanel/remove", HandleSettingsRoute61Async);
        router.Add((request, path) => request.HttpMethod == "PUT" && path == "/v1/quickpanel/reorder", HandleSettingsRoute62Async);
        return Interlocked.CompareExchange(ref _authenticatedRouter, router, null) ?? router;
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            return true;
        }

        var bearer = request.Headers["Authorization"];
        if (!string.IsNullOrWhiteSpace(bearer) &&
            bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(bearer["Bearer ".Length..].Trim(), _token, StringComparison.Ordinal))
        {
            return true;
        }

        var incoming = request.Headers["X-Yanzi-Token"];
        return string.Equals(incoming, _token, StringComparison.Ordinal) ||
               IsValidConsoleCookie(request);
    }

    private static Task WriteYanmStateAsync(HttpListenerResponse response, AppSettings settings)
    {
        settings.Yanm ??= new YanmSettings();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var updatedAtUtc = string.IsNullOrWhiteSpace(settings.YanmStateUpdatedAtUtc)
            ? settings.LauncherConfigUpdatedAtUtc
            : settings.YanmStateUpdatedAtUtc;
        updatedAtUtc = string.IsNullOrWhiteSpace(updatedAtUtc)
            ? DateTime.UtcNow.ToString("O")
            : updatedAtUtc;
        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(settings.Yanm, JsonOptions));
        return WriteJsonAsync(response, 200, new
        {
            ok = true,
            source = "local-agent-api",
            updatedAtUtc,
            yanm = settings.Yanm,
            bytes
        });
    }

    private static WebDavConfigDto GetWebDavConfigDto()
    {
        var settings = AppSettingsStore.Load();
        var credential = WebDavCredentialStore.Load();

        bool hasCredentials = !string.IsNullOrWhiteSpace(settings.WebDavServerUrl) &&
                             !string.IsNullOrWhiteSpace(settings.WebDavUsername) &&
                             !string.IsNullOrWhiteSpace(credential?.Password);

        return new WebDavConfigDto
        {
            Enabled = settings.EnableWebDavSync || hasCredentials,
            ServerUrl = settings.WebDavServerUrl,
            RootPath = settings.WebDavRootPath,
            Username = settings.WebDavUsername,
            Password = string.IsNullOrWhiteSpace(credential?.Password) ? null : credential.Password
        };
    }

    private static object ToDto(CommandItem x)
    {
        return new
        {
            id = x.ExtensionId,
            title = x.Title,
            subtitle = x.Subtitle,
            category = x.Category,
            source = x.Source.ToString(),
            version = x.DeclaredVersion,
            globalShortcut = x.GlobalShortcut,
            runtime = x.Runtime,
            entry = x.EntryPoint,
            permissions = x.Permissions,
            capabilities = YanziAgentCapabilityCatalog.ForExtension(x)
        };
    }

    private static async Task<JsonElement> ReadJsonBodyAsync(HttpListenerRequest request)
    {
        var encoding = Encoding.UTF8;
        var contentType = request.ContentType;
        if (!string.IsNullOrEmpty(contentType) && contentType.Contains("charset=", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                encoding = request.ContentEncoding ?? Encoding.UTF8;
            }
            catch
            {
                encoding = Encoding.UTF8;
            }
        }

        using var reader = new StreamReader(request.InputStream, encoding);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static Dictionary<string, string> ReadYanmComponentStatePatch(JsonElement payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.TryGetProperty("componentState", out var stateElement) &&
            stateElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in stateElement.EnumerateObject())
            {
                var key = item.Name.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = JsonElementToStateString(item.Value);
                }
            }
        }

        var explicitKey = GetString(payload, "stateKey") ?? GetString(payload, "key");
        if (!string.IsNullOrWhiteSpace(explicitKey))
        {
            payload.TryGetProperty("value", out var valueElement);
            result[explicitKey.Trim()] = JsonElementToStateString(valueElement);
        }

        return result;
    }

    private static string JsonElementToStateString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }

    private static LocalMobileMessageDetail CreateLocalMobileMessageDetail(DeviceMessageRecord message, bool success, string output)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in message.Payload)
        {
            payload[item.Key] = item.Value.Clone();
        }

        payload["executionResult"] = new
        {
            success,
            output,
            error = success ? string.Empty : output
        };

        return new LocalMobileMessageDetail
        {
            Ok = true,
            MessageId = message.MessageId,
            SourceDeviceId = message.SourceDeviceId,
            TargetPlatform = message.TargetPlatform,
            Kind = message.Kind,
            Title = message.Title,
            Text = message.Text,
            Payload = payload,
            Status = success ? "completed" : "failed",
            CreatedAt = message.CreatedAt,
            DeliveredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            AckedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
        };
    }

    private static void TrimLocalMobileMessages()
    {
        if (_localMobileMessages.Count <= 100)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds();
        foreach (var item in _localMobileMessages)
        {
            if (long.TryParse(item.Value.CreatedAt, out var createdAt) && createdAt < cutoff)
            {
                _localMobileMessages.TryRemove(item.Key, out _);
            }
        }
    }

    private static string? GetQueryString(HttpListenerRequest request, string key)
    {
        return request.QueryString[key];
    }

    private static HashSet<string> ParseIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        response.StatusCode = statusCode;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed class LocalMobileMessageDetail
    {
        public bool Ok { get; set; }

        public string MessageId { get; set; } = string.Empty;

        public string? SourceDeviceId { get; set; }

        public string? TargetPlatform { get; set; }

        public string Kind { get; set; } = "text";

        public string Title { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public Dictionary<string, object?> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string Status { get; set; } = string.Empty;

        public string CreatedAt { get; set; } = string.Empty;

        public string? DeliveredAt { get; set; }

        public string? AckedAt { get; set; }
    }

    private string GetDocsHtml()
    {
        var html = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>燕子 · 本地 Agent API 控制台</title>
    <style>
        :root {
            --bg-color: #0b0f19;
            --card-bg: rgba(22, 28, 45, 0.4);
            --border-color: #1e293b;
            --text-main: #f8fafc;
            --text-muted: #94a3b8;
            --accent-color: #38bdf8;
            --accent-hover: #0ea5e9;
            --accent-light: rgba(56, 189, 248, 0.1);
            --success-color: #10b981;
            --danger-color: #f43f5e;
        }
        body {
            background-color: var(--bg-color);
            color: var(--text-main);
            font-family: 'Inter', system-ui, -apple-system, sans-serif;
            margin: 0;
            padding: 0;
            min-height: 100vh;
        }
        .header {
            background: rgba(13, 20, 35, 0.7);
            backdrop-filter: blur(12px);
            border-bottom: 1px solid var(--border-color);
            padding: 16px 24px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            position: sticky;
            top: 0;
            z-index: 100;
        }
        .header h1 {
            margin: 0;
            font-size: 20px;
            font-weight: 600;
            background: linear-gradient(135deg, #38bdf8, #818cf8);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
        }
        .badge-service {
            background: rgba(16, 185, 129, 0.1);
            color: var(--success-color);
            border: 1px solid rgba(16, 185, 129, 0.2);
            padding: 4px 12px;
            border-radius: 99px;
            font-size: 12px;
            font-weight: 500;
        }
        .container {
            max-width: 1400px;
            margin: 30px auto;
            padding: 0 20px;
            display: grid;
            grid-template-columns: 1fr 480px;
            gap: 30px;
        }
        .api-list {
            display: flex;
            flex-direction: column;
            gap: 24px;
        }
        .api-section-title {
            font-size: 14px;
            font-weight: 600;
            color: var(--text-muted);
            margin: 0 0 12px 0;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }
        .card {
            background: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            margin-bottom: 12px;
            overflow: hidden;
            transition: all 0.2s ease;
        }
        .card:hover {
            border-color: #334155;
            box-shadow: 0 4px 20px rgba(0,0,0,0.3);
        }
        .card-header {
            padding: 16px;
            display: flex;
            align-items: center;
            cursor: pointer;
            user-select: none;
        }
        .method {
            font-size: 11px;
            font-weight: 700;
            padding: 4px 8px;
            border-radius: 6px;
            width: 65px;
            text-align: center;
            margin-right: 14px;
            letter-spacing: 0.02em;
        }
        .method.get { background: rgba(56, 189, 248, 0.15); color: #38bdf8; border: 1px solid rgba(56, 189, 248, 0.3); }
        .method.post { background: rgba(245, 158, 11, 0.15); color: #f59e0b; border: 1px solid rgba(245, 158, 11, 0.3); }
        .method.put { background: rgba(168, 85, 247, 0.15); color: #a855f7; border: 1px solid rgba(168, 85, 247, 0.3); }
        .method.delete { background: rgba(239, 68, 68, 0.15); color: #ef4444; border: 1px solid rgba(239, 68, 68, 0.3); }
        
        .path {
            font-family: 'Consolas', monospace;
            font-size: 14px;
            color: #f1f5f9;
            font-weight: 500;
            flex-grow: 1;
        }
        .desc {
            font-size: 13px;
            color: var(--text-muted);
            margin-right: 8px;
        }
        .card-body {
            padding: 0 16px 16px 16px;
            border-top: 1px solid var(--border-color);
            background: rgba(10, 15, 26, 0.4);
            display: none;
        }
        .card.expanded .card-body {
            display: block;
        }
        .form-group {
            margin-top: 14px;
        }
        .form-group label {
            display: block;
            font-size: 12px;
            color: var(--text-muted);
            margin-bottom: 6px;
            font-weight: 500;
        }
        .form-control {
            background: #0f172a;
            border: 1px solid var(--border-color);
            border-radius: 6px;
            color: #f1f5f9;
            padding: 8px 12px;
            width: 100%;
            box-sizing: border-box;
            font-family: 'Consolas', monospace;
            font-size: 13px;
            transition: border-color 0.2s;
        }
        .form-control:focus {
            outline: none;
            border-color: var(--accent-color);
        }
        textarea.form-control {
            min-height: 80px;
            resize: vertical;
        }
        .btn-send {
            background: linear-gradient(135deg, #38bdf8, #6366f1);
            color: #ffffff;
            border: none;
            border-radius: 6px;
            padding: 8px 16px;
            font-size: 13px;
            font-weight: 600;
            cursor: pointer;
            margin-top: 16px;
            transition: opacity 0.2s;
        }
        .btn-send:hover {
            opacity: 0.95;
        }
        .sidebar {
            position: sticky;
            top: 95px;
            height: calc(100vh - 140px);
            display: flex;
            flex-direction: column;
            gap: 20px;
        }
        .panel {
            background: var(--card-bg);
            border: 1px solid var(--border-color);
            border-radius: 12px;
            padding: 20px;
            display: flex;
            flex-direction: column;
        }
        .panel-credentials {
            flex-shrink: 0;
        }
        .panel-response {
            flex-grow: 1;
            min-height: 0;
        }
        .panel-title {
            font-size: 14px;
            font-weight: 600;
            color: #e2e8f0;
            margin: 0 0 16px 0;
            border-bottom: 1px solid var(--border-color);
            padding-bottom: 10px;
            text-transform: uppercase;
            letter-spacing: 0.05em;
        }
        .token-container {
            display: flex;
            gap: 8px;
            margin-top: 6px;
        }
        .btn-action {
            background: #1e293b;
            border: 1px solid #334155;
            color: #cbd5e1;
            border-radius: 6px;
            padding: 8px 12px;
            cursor: pointer;
            font-size: 12px;
            font-weight: 500;
            transition: all 0.2s;
        }
        .btn-action:hover {
            background: #334155;
            color: #f8fafc;
        }
        .res-meta {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 12px;
            font-size: 13px;
        }
        .status-badge {
            padding: 3px 10px;
            border-radius: 4px;
            font-weight: 600;
            font-size: 12px;
        }
        .status-ok { background: rgba(16, 185, 129, 0.15); color: var(--success-color); border: 1px solid rgba(16, 185, 129, 0.3); }
        .status-error { background: rgba(239, 68, 68, 0.15); color: var(--danger-color); border: 1px solid rgba(239, 68, 68, 0.3); }
        .res-container {
            flex-grow: 1;
            overflow: auto;
            background: #050811;
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 12px;
            margin: 0;
            box-sizing: border-box;
        }
        .res-container pre {
            margin: 0;
            white-space: pre-wrap;
            word-break: break-all;
        }
        .res-container code {
            font-family: 'Consolas', monospace;
            font-size: 12px;
        }
        .capability-contract { max-height: 280px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 12px; }
        .catalog-table { width: 100%; border-collapse: collapse; font-size: 12px; }
        .catalog-table th, .catalog-table td { padding: 9px; border-bottom: 1px solid var(--border-color); text-align: left; overflow-wrap: anywhere; }
        .catalog-scroll { max-height: 260px; overflow: auto; margin-top: 12px; }
        .capability-actions { display: flex; flex-wrap: wrap; gap: 8px; }
        button:disabled { opacity: .45; cursor: not-allowed; }
        @media (max-width: 1000px) {
            .container { grid-template-columns: minmax(0, 1fr); }
            .sidebar { position: static; height: auto; }
            .panel-response { min-height: 220px; }
        }
    </style>
</head>
<body>
    <div class="header">
        <h1>燕子 · 本地 Agent API 控制台</h1>
        <div class="badge-service">API 服务正常</div>
    </div>
    <div class="container">
        <datalist id="available-program-ids"></datalist>
        <div class="api-list">
            <!-- Diagnostics -->
            <div class="api-section">
                <h3 class="api-section-title">诊断与健康检查</h3>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method get">GET</span>
                        <span class="path">/health</span>
                        <span class="desc">健康检查</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">检查 API 监听器是否运行正常。</p>
                        <button class="btn-send" onclick="testHealth()">发送请求</button>
                    </div>
                </div>
            </div>

            <div class="api-section" id="capability-console">
                <h3 class="api-section-title">AI 能力目录与调用</h3>
                <p><a href="/pair" target="_blank" rel="noopener">同账号设备连接状态</a>（请先完成下方控制台鉴权）</p>
                <div class="card expanded">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method get">目录</span>
                        <span class="path">/v1/agent/catalog</span>
                        <span class="desc">小程序及对应能力</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px;color:var(--text-muted)">读取本机实际安装的小程序、能力契约和可用状态。选择能力，填写参数后可直接调用。</p>
                        <div class="capability-actions">
                            <button class="btn-send" onclick="refreshCapabilityCatalog(true)">刷新能力目录</button>
                            <button class="btn-send" onclick="sendRequest('GET', '/v1/agent/openapi.json')">读取 AI 接口规范</button>
                            <button class="btn-send" id="copy-capability-catalog" onclick="copyCapabilityCatalog()" disabled>复制 AI 能力目录</button>
                            <button class="btn-send" onclick="sendRequest('GET', '/v1/capabilities/calls')">查看调用记录</button>
                        </div>
                        <p id="capability-load-state" role="status" aria-live="polite" style="font-size:12px;color:var(--text-muted)">请先验证本地 API 身份</p>
                        <details>
                            <summary>已安装小程序及对应能力</summary>
                            <div class="catalog-scroll">
                                <table class="catalog-table"><thead><tr><th>小程序</th><th>运行状态</th><th>可用 / 声明能力</th></tr></thead><tbody id="capability-programs"></tbody></table>
                            </div>
                        </details>
                        <div class="form-group">
                            <label for="capability-provider">能力提供者</label>
                            <select class="form-control" id="capability-provider" onchange="filterCapabilityOptions()" disabled><option value="">请先读取能力目录</option></select>
                        </div>
                        <div class="form-group">
                            <label for="capability-name">对应能力</label>
                            <select class="form-control" id="capability-name" onchange="onSelectedCapabilityChanged()" disabled><option value="">请选择能力</option></select>
                        </div>
                        <p id="capability-availability" role="status" aria-live="polite" style="font-size:12px;color:var(--text-muted)">尚未选择能力</p>
                        <details open><summary>能力说明、输入输出 Schema 与权限</summary><pre class="capability-contract" id="capability-contract">选择能力后显示契约。</pre></details>
                        <div class="form-group">
                            <label for="capability-payload">调用参数（JSON，按照 inputSchema 填写）</label>
                            <textarea class="form-control" id="capability-payload" rows="5" spellcheck="false">{}</textarea>
                        </div>
                        <div class="capability-actions">
                            <button class="btn-send" id="invoke-capability" onclick="testInvokeCapability()" disabled>调用所选能力</button>
                            <button class="btn-send" id="start-capability-provider" onclick="startCapabilityProvider()" disabled>启动提供小程序</button>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Extensions -->
            <div class="api-section">
                <h3 class="api-section-title">小程序管理</h3>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method get">GET</span>
                        <span class="path">/v1/extensions</span>
                        <span class="desc">读取已安装小程序</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">读取当前燕子中实际安装的小程序，不依赖示例 ID。</p>
                        <button class="btn-send" onclick="testListExtensions()">发送请求</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method post">接口</span>
                        <span class="path">/v1/extensions/{id}/status · build · reload</span>
                        <span class="desc">开发与重载</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted);">GET 查询状态；POST 编译只构建不停止；POST 重载先编译再启动。重载不保证运行时失败后的自动回滚。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="deploy-ext-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <button class="btn-send" onclick="testDeployment('status')">查看状态</button>
                        <button class="btn-send" onclick="testDeployment('build')">编译</button>
                        <button class="btn-send" onclick="testDeployment('reload')">重载</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method post">POST</span>
                        <span class="path">/v1/extensions/{id}/run</span>
                        <span class="desc">运行小程序</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">运行指定小程序脚本或可执行文件。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="run-ext-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <div class="form-group">
                            <label>运行参数（文本）</label>
                            <textarea class="form-control" id="run-input" placeholder="作为参数传给所选小程序…"></textarea>
                        </div>
                        <button class="btn-send" onclick="testRunExtension()">发送请求</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method post">POST</span>
                        <span class="path">/v1/extensions/{id}/stop</span>
                        <span class="desc">停止小程序</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">中止或终止指定 ID 的运行中进程或后台任务。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="stop-ext-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <button class="btn-send" onclick="testStopExtension()">发送请求</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method delete">DELETE</span>
                        <span class="path">/v1/extensions/{id}</span>
                        <span class="desc">将小程序移至回收站</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">将指定小程序移入回收站（软删除）。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="delete-ext-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <button class="btn-send" onclick="testDeleteExtension()">发送请求</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method delete">DELETE</span>
                        <span class="path">/v1/extensions/recycle-bin/{id}</span>
                        <span class="desc">彻底删除小程序</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">从回收站中永久删除指定小程序，此操作不可逆。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="purge-ext-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <button class="btn-send" onclick="testPurgeExtension()">发送请求</button>
                    </div>
                </div>
            </div>

            <!-- Scoped extension UI capture and automation -->
            <div class="api-section">
                <h3 class="api-section-title">小程序界面测试</h3>
                <div class="card expanded">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method post">界面</span>
                        <span class="path">/v1/extensions/{id}/ui/*</span>
                        <span class="desc">窗口截图与 UI 自动化</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted);">只访问指定小程序窗口，不会捕获整个桌面。日历失去焦点后可在后台截图，无需重新弹出；自动化点击则会主动显示日历。须先填写本地 API Token。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <select class="form-control" id="ui-ext-id" onchange="onSelectedProgramChanged()" disabled><option value="">请先登录并读取已安装小程序</option></select>
                        </div>
                        <button class="btn-send" onclick="refreshInstalledPrograms(true)">刷新小程序列表</button><span id="program-load-state" aria-live="polite" style="margin-left:10px;font-size:12px;color:var(--text-muted)">尚未读取</span><br><button class="btn-send" onclick="testUi('windows')">窗口列表</button>
                        <button class="btn-send" onclick="testUi('open')">打开窗口</button>
                        <button class="btn-send" onclick="testUi('elements')">查看控件</button>
                        <button class="btn-send" onclick="testUi('capture')">截图</button>
                        <div style="margin-top:14px;padding:12px;border:1px solid #3777CE;border-radius:9px;background:rgba(45,113,210,.09)">
                            <strong style="color:#93C5FD">AI 自动化会话 · 蓝色屏幕边框与操作提示</strong>
                            <div id="ui-session-state" style="font-size:12px;color:#AAA;margin:7px 0">尚未建立会话。普通自动化操作会显示短暂的测试提示。</div>
                            <button class="btn-send" onclick="testUiSession('start')">开始窗口测试</button>
                            <button class="btn-send" onclick="testUiSession('foreground')">前台测试（需确认）</button>
                            <button class="btn-send" onclick="testUiSession('pause')">暂停</button>
                            <button class="btn-send" onclick="testUiSession('resume')">继续</button>
                            <button class="btn-send" onclick="testUiSession('stop')">结束</button>
                            <button class="btn-send" onclick="testUiSession('status')">状态</button>
                            <p style="font-size:11px;color:#A8BFEA;margin:9px 0 0">检测到用户操作会自动暂停；此时需在桌面蓝色工具条上手动点击继续。紧急停止：Ctrl+Alt+Shift+F12。前台模式暂不注入系统鼠标输入。</p>
                        </div>
                        <div class="form-group" style="margin-top:12px">
                            <label>界面操作步骤（JSON；先读取控件 ID）</label>
                            <textarea class="form-control" id="ui-action-json" rows="5" placeholder="先选择小程序，点击“查看控件”，再填写控件 ID 与操作步骤。"></textarea>
                        </div>
                        <button class="btn-send" onclick="testUi('actions')">执行操作并截图</button>
                        <div style="margin-top:12px">
                            <img id="ui-preview" alt="小程序界面截图" style="display:none;max-width:100%;border-radius:8px;border:1px solid #444" />
                        </div>
                    </div>
                </div>
            </div>

            <!-- Storage -->
            <div class="api-section">
                <h3 class="api-section-title">小程序数据存储</h3>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method get">GET</span>
                        <span class="path">/v1/storage/{id}</span>
                        <span class="desc">读取小程序存储数据</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">读取某个小程序存放在沙盒里的配置、笔记或其他文本数据。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="get-store-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <div class="form-group">
                            <label>存储键（相对路径）</label>
                            <input type="text" class="form-control" id="get-store-key" placeholder="例如: notes/index.json">
                        </div>
                        <button class="btn-send" onclick="testGetStorage()">发送请求</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method put">PUT</span>
                        <span class="path">/v1/storage/{id}</span>
                        <span class="desc">修改/写入小程序数据</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">覆写或创建某个小程序沙盒中的键值文件，支持本地和云端的自动更新。</p>
                        <div class="form-group">
                            <label>小程序 ID</label>
                            <input type="text" class="form-control" id="put-store-id" list="available-program-ids" placeholder="先读取已安装小程序">
                        </div>
                        <div class="form-group">
                            <label>存储键</label>
                            <input type="text" class="form-control" id="put-store-key" placeholder="例如: notes/index.json">
                        </div>
                        <div class="form-group">
                            <label>写入内容</label>
                            <textarea class="form-control" id="put-store-content" placeholder="写入的 JSON 或文本..."></textarea>
                        </div>
                        <button class="btn-send" onclick="testPutStorage()">发送请求</button>
                    </div>
                </div>
            </div>

            <!-- Yanm Status -->
            <div class="api-section">
                <h3 class="api-section-title">燕幕状态管理</h3>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method get">GET</span>
                        <span class="path">/v1/me/yanm-state</span>
                        <span class="desc">获取燕幕状态与内容</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">获取当前燕幕的组件状态、便签内容和组件列表。</p>
                        <button class="btn-send" onclick="testGetYanmState()">发送请求</button>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header" onclick="toggleCard(this)">
                        <span class="method put">PUT</span>
                        <span class="path">/v1/me/yanm-state</span>
                        <span class="desc">更新燕幕状态与内容</span>
                    </div>
                    <div class="card-body">
                        <p style="font-size:13px; color:var(--text-muted); margin: 0 0 10px 0;">修改燕幕便签组件的内容或组件布局状态，立即反映在屏幕上。</p>
                        <div class="form-group">
                            <label>燕幕配置数据（JSON）</label>
                            <textarea class="form-control" id="put-yanm-content" placeholder='例如: {"componentState": {"note": "新便签内容"}}'></textarea>
                        </div>
                        <button class="btn-send" onclick="testPutYanmState()">发送请求</button>
                    </div>
                </div>
            </div>
        </div>

        <div class="sidebar">
            <!-- Credentials Panel -->
            <div class="panel panel-credentials">
                <h4 class="panel-title">身份验证</h4>
                <div class="form-group" style="margin-top:0;">
                    <label>服务地址</label>
                    <input type="text" class="form-control" id="base-url" readonly>
                </div>
                <div class="form-group">
                    <label>X-Yanzi-Token</label>
                    <div class="token-container">
                        <input type="password" class="form-control" id="api-token" value=""
                            autocomplete="off" spellcheck="false" placeholder="首次使用：从燕子设置中粘贴 Token">
                        <button id="token-visibility-button" class="btn-action" onclick="toggleTokenVisibility()">显示</button>
                        <button class="btn-action" onclick="copyToken()">复制</button>
                    </div>
                    <p id="console-auth-state" aria-live="polite" style="font-size:12px;color:var(--text-muted);margin:10px 0">正在检测本机登录状态…</p>
                    <div style="display:flex;gap:8px;flex-wrap:wrap;margin-top:9px">
                        <button class="btn-action" onclick="rememberConsoleLogin()">验证并记住此浏览器</button>
                        <button class="btn-action" onclick="signOutConsole()">退出并清除此浏览器的登录</button>
                    </div>
                    <p style="font-size:12px;color:var(--text-muted);margin:10px 0 0">
                        获取方式：燕子 → 设置 → 常规 → 本地 Agent API → 复制 Token。首次验证后使用 HttpOnly 登录 Cookie，
                        无需每次粘贴，也不会将原始 Token 保存到网页存储。使用公用电脑时请退出登录；
                        清除浏览器 Cookie 或修改燕子 Token 后需要重新验证。
                    </p>
                </div>
            </div>

            <!-- Response Panel -->
            <div class="panel panel-response">
                <h4 class="panel-title">响应监视器</h4>
                <div class="res-meta">
                    <div>状态：<span id="res-status" class="status-badge" style="display:inline-block; min-width:40px; text-align:center;">-</span></div>
                    <div>耗时：<span id="res-time">-</span></div>
                </div>
                <div class="res-container">
                    <pre><code id="res-body" class="language-json">尚未发送请求。</code></pre>
                </div>
            </div>
        </div>
    </div>

    <script>
        // Console login uses an HttpOnly, SameSite=Strict cookie. No raw token
        // is persisted in JS-accessible localStorage or injected in public HTML.
        document.getElementById('base-url').value = window.location.origin;
        const calendarExample = JSON.stringify({
            captureEach: true,
            actions: [
                {type: 'invoke', automationId: 'calendar.add',
                 waitFor: 'calendar.editor.title'},
                {type: 'invoke', automationId: 'calendar.editor.cancel'}
            ]
        }, null, 2);
        let activePrograms = [];
        let capabilityCatalog = null;
        let filteredCapabilities = [];
        let capabilityCatalogRequest = 0;

        function resetCapabilityCatalog(message) {
            ++capabilityCatalogRequest;
            capabilityCatalog = null;
            filteredCapabilities = [];
            document.getElementById('capability-provider').replaceChildren(new Option('请先读取能力目录', ''));
            document.getElementById('capability-provider').disabled = true;
            document.getElementById('capability-name').replaceChildren(new Option('请选择能力', ''));
            document.getElementById('capability-name').disabled = true;
            document.getElementById('capability-programs').replaceChildren();
            document.getElementById('capability-contract').textContent = '选择能力后显示契约。';
            document.getElementById('capability-availability').textContent = '尚未选择能力';
            document.getElementById('capability-load-state').textContent = message;
            document.getElementById('capability-payload').value = '{}';
            for (const id of ['invoke-capability', 'start-capability-provider', 'copy-capability-catalog'])
                document.getElementById(id).disabled = true;
        }

        async function refreshCapabilityCatalog(showInMonitor = false) {
            const requestId = ++capabilityCatalogRequest;
            const token = document.getElementById('api-token').value.trim();
            document.getElementById('capability-load-state').textContent = '正在读取能力目录…';
            try {
                const response = await fetch('/v1/agent/catalog', {
                    headers: token ? {'X-Yanzi-Token': token} : {}, credentials: 'same-origin', cache: 'no-store'
                });
                const data = await response.json();
                if (requestId !== capabilityCatalogRequest) return;
                if (!response.ok) {
                    if (showInMonitor) showResponse(response.status, data);
                    throw new Error(response.status === 401 ? '请先验证本地 API 身份' : 'HTTP ' + response.status);
                }
                capabilityCatalog = data;
                const provider = document.getElementById('capability-provider');
                const oldProvider = provider.value;
                provider.replaceChildren(new Option('全部小程序与宿主能力', ''), new Option('宿主内置能力', 'host:'));
                const rows = document.getElementById('capability-programs');
                rows.replaceChildren();
                for (const program of data.extensions) {
                    const capabilities = program.capabilities || [];
                    provider.add(new Option((program.title || program.id) + ' · ' + capabilities.length + ' 项能力', 'extension:' + program.id));
                    const row = document.createElement('tr');
                    const title = document.createElement('td');
                    const button = document.createElement('button');
                    button.className = 'btn-action';
                    button.textContent = program.title || program.id;
                    button.title = program.id;
                    button.addEventListener('click', () => { provider.value = 'extension:' + program.id; filterCapabilityOptions(); });
                    title.appendChild(button);
                    const state = document.createElement('td');
                    state.textContent = program.isRunning ? '运行中' : '未运行';
                    const count = document.createElement('td');
                    count.textContent = capabilities.filter(x => x.available).length + ' / ' + capabilities.length;
                    row.append(title, state, count);
                    rows.appendChild(row);
                }
                provider.disabled = false;
                if (Array.from(provider.options).some(x => x.value === oldProvider)) provider.value = oldProvider;
                document.getElementById('copy-capability-catalog').disabled = false;
                const total = data.extensions.flatMap(x => x.capabilities || []).concat(data.hostCapabilities || []);
                document.getElementById('capability-load-state').textContent =
                    data.extensions.length + ' 个小程序 · ' + total.filter(x => x.available).length + ' 项当前可调用能力';
                filterCapabilityOptions();
                if (showInMonitor) showResponse(200, data);
            } catch (error) {
                if (requestId === capabilityCatalogRequest) resetCapabilityCatalog('读取失败：' + error.message);
            }
        }

        function filterCapabilityOptions() {
            const selector = document.getElementById('capability-name');
            const oldName = selector.value;
            const provider = document.getElementById('capability-provider').value;
            const catalog = capabilityCatalog;
            filteredCapabilities = catalog ?
                (provider === 'host:' ? catalog.hostCapabilities :
                 provider.startsWith('extension:') ? catalog.extensions.find(x => x.id === provider.slice(10))?.capabilities || [] :
                 catalog.extensions.flatMap(x => x.capabilities || []).concat(catalog.hostCapabilities || [])) : [];
            selector.replaceChildren(new Option(filteredCapabilities.length ? '请选择能力' : '该提供者尚未声明能力', ''));
            for (const capability of filteredCapabilities) {
                selector.add(new Option(capability.name + ' · ' + (capability.available ? '可调用' : '尚未注册'), capabilitySelectionKey(capability)));
            }
            selector.disabled = filteredCapabilities.length === 0;
            if (filteredCapabilities.some(x => capabilitySelectionKey(x) === oldName)) selector.value = oldName;
            onSelectedCapabilityChanged();
        }

        function capabilitySelectionKey(capability) {
            return JSON.stringify([capability.providerExtensionId, capability.name]);
        }

        function selectedCapability() {
            return filteredCapabilities.find(x => capabilitySelectionKey(x) === document.getElementById('capability-name').value);
        }

        function capabilitySample(schema, depth = 0) {
            if (depth > 5) return null;
            if (schema.default !== undefined) return schema.default;
            if (schema.examples?.length) return schema.examples[0];
            if (schema.enum?.length) return schema.enum[0];
            if (schema.type === 'object') {
                const value = {};
                for (const key of schema.required || []) value[key] = capabilitySample(schema.properties?.[key] || {}, depth + 1);
                return value;
            }
            if (schema.type === 'array') return [];
            if (schema.type === 'boolean') return false;
            if (schema.type === 'integer' || schema.type === 'number') return 0;
            if (schema.type === 'string') return '';
            return {};
        }

        function onSelectedCapabilityChanged() {
            const capability = selectedCapability();
            const provider = capabilityCatalog?.extensions.find(x => x.id === capability?.providerExtensionId);
            document.getElementById('capability-contract').textContent = capability ? JSON.stringify(capability, null, 2) : '选择能力后显示契约。';
            document.getElementById('capability-availability').textContent = !capability ? '尚未选择能力' :
                capability.available ? '当前可调用 · 提供者：' + capability.providerExtensionId :
                '尚未注册。可启动提供小程序后刷新目录；运行中的提供者需要实际注册 Handler。';
            document.getElementById('capability-payload').value = JSON.stringify(capabilitySample(capability?.inputSchema || {}), null, 2);
            document.getElementById('invoke-capability').disabled = !capability?.available;
            document.getElementById('start-capability-provider').disabled = !provider || provider.isRunning || capability.available;
        }

        async function testInvokeCapability() {
            const capability = selectedCapability();
            if (!capability?.available) return;
            let payload;
            try { payload = JSON.parse(document.getElementById('capability-payload').value); }
            catch { return showResponse(400, {error: '调用参数必须是有效 JSON'}); }
            await sendRequest('POST', '/v1/capabilities/invoke', {name: capability.name, payload});
        }

        async function startCapabilityProvider() {
            const capability = selectedCapability();
            const provider = capabilityCatalog?.extensions.find(x => x.id === capability?.providerExtensionId);
            if (!provider || provider.isRunning) return;
            const result = await sendRequest('POST', provider.runEndpoint, {input: '', launchSource: 'app-startup'});
            if (result?.success) await refreshCapabilityCatalog();
        }

        async function copyCapabilityCatalog() {
            if (!capabilityCatalog) return;
            try {
                await navigator.clipboard.writeText(JSON.stringify(capabilityCatalog, null, 2));
                document.getElementById('capability-load-state').textContent = '已复制 AI 能力目录（不含 Token）';
            } catch { document.getElementById('capability-load-state').textContent = '复制失败，请使用刷新能力目录后的响应内容。'; }
        }

        function setAuthState(message, success = false) {
            const status = document.getElementById('console-auth-state');
            status.textContent = message;
            status.style.color = success ? '#6ee7b7' : 'var(--text-muted)';
        }

        function toggleCard(header) {
            header.parentElement.classList.toggle('expanded');
        }

        function toggleTokenVisibility() {
            const input = document.getElementById('api-token');
            const button = document.getElementById('token-visibility-button');
            input.type = input.type === 'password' ? 'text' : 'password';
            button.textContent = input.type === 'password' ? '显示' : '隐藏';
        }

        async function copyToken() {
            const value = document.getElementById('api-token').value;
            if (!value) return alert('登录后的原始 Token 不在网页中保存；如需复制，请到燕子设置界面获取。');
            try {
                await navigator.clipboard.writeText(value);
                setAuthState('已复制到剪贴板。请妥善保管。', true);
            } catch (error) {
                setAuthState('复制失败，请检查浏览器剪贴板权限。');
            }
        }

        async function rememberConsoleLogin() {
            const input = document.getElementById('api-token');
            const token = input.value.trim();
            if (!token) {
                return setAuthState('请先从燕子设置中复制 Token 并粘贴。');
            }
            setAuthState('正在验证并创建本机登录…');
            try {
                const result = await fetch('/v1/console/login', {
                    method: 'POST',
                    credentials: 'same-origin',
                    cache: 'no-store',
                    headers: {'Content-Type': 'application/json', 'X-Yanzi-Token': token},
                    body: '{}'
                });
                if (!result.ok) throw new Error('Token 验证失败（HTTP ' + result.status + '）');
                input.value = '';
                input.type = 'password';
                document.getElementById('token-visibility-button').textContent = '显示';
                setAuthState('已记住此浏览器：下次打开控制台可直接使用。', true);
                await refreshInstalledPrograms();
            } catch (error) {
                setAuthState(error.message + '；请检查设置中的当前 Token。');
            }
        }

        async function signOutConsole() {
            try {
                const result = await fetch('/v1/console/logout', {
                    method: 'POST', credentials: 'same-origin',
                    headers: {'Content-Type': 'application/json'}, body: '{}'
                });
                if (!result.ok) throw new Error('退出失败（HTTP ' + result.status + '）');
                document.getElementById('api-token').value = '';
                resetInstalledPrograms('已退出，请重新验证后读取小程序');
                setAuthState('已清除当前浏览器的登录 Cookie。');
            } catch (error) {
                setAuthState('无法退出登录：' + error.message);
            }
        }

        function resetInstalledPrograms(message) {
            resetCapabilityCatalog(message);
            activePrograms = [];
            const selector = document.getElementById('ui-ext-id');
            selector.replaceChildren(new Option('请先读取已安装小程序', ''));
            selector.disabled = true;
            document.getElementById('available-program-ids').replaceChildren();
            document.getElementById('program-load-state').textContent = message;
            document.getElementById('ui-action-json').value = '';
        }

        function onSelectedProgramChanged() {
            const selected = document.getElementById('ui-ext-id').value;
            const editor = document.getElementById('ui-action-json');
            if (selected === 'taskbar-calendar' && !editor.value.trim())
                editor.value = calendarExample;
            else if (selected !== 'taskbar-calendar' && editor.value.trim() === calendarExample)
                editor.value = '';
        }

        async function refreshInstalledPrograms(showInMonitor = false) {
            const manuallyEnteredToken = document.getElementById('api-token').value.trim();
            const headers = manuallyEnteredToken ? {'X-Yanzi-Token': manuallyEnteredToken} : {};
            const status = document.getElementById('program-load-state');
            status.textContent = '正在读取…';
            try {
                const result = await fetch('/v1/extensions', {
                    method: 'GET', headers, cache: 'no-store', credentials: 'same-origin'
                });
                if (result.status === 401) {
                    resetInstalledPrograms('身份验证失效');
                    setAuthState('登录失效。请到燕子设置中复制当前 Token，重新验证。');
                    return;
                }
                if (!result.ok) throw new Error('HTTP ' + result.status);
                const data = await result.json();
                activePrograms = Array.isArray(data.items)
                    ? data.items.filter(item => item && typeof item.id === 'string' && item.id.trim())
                        .sort((a, b) => (a.title || a.id).localeCompare(b.title || b.id, 'zh-CN'))
                    : [];
                const selector = document.getElementById('ui-ext-id');
                const oldSelection = selector.value;
                selector.replaceChildren(new Option('请选择已安装小程序', ''));
                const suggestions = document.getElementById('available-program-ids');
                suggestions.replaceChildren();
                for (const program of activePrograms) {
                    selector.add(new Option((program.title || program.id) + ' · ' + program.id, program.id));
                    suggestions.appendChild(new Option(program.id));
                }
                selector.disabled = activePrograms.length === 0;
                if (activePrograms.some(program => program.id === oldSelection))
                    selector.value = oldSelection;
                status.textContent = '已读取 ' + activePrograms.length + ' 个小程序';
                setAuthState('本机 API 已登录，可以直接操作已安装小程序。', true);
                await refreshCapabilityCatalog();
                if (showInMonitor) showResponse(200, {ok: true, items: activePrograms});
            } catch (error) {
                resetInstalledPrograms('读取失败');
                setAuthState('获取小程序列表失败：' + error.message);
            }
        }

        const statusTranslations = {
            200: '成功', 201: '已创建', 202: '已接受', 204: '已完成',
            400: '请求错误', 401: '未授权', 403: '禁止访问', 404: '未找到',
            405: '不支持该操作', 409: '状态冲突', 413: '请求过大',
            422: '无法处理', 429: '请求过多', 500: '服务器错误',
            503: '服务不可用'
        };

        function showResponse(status, value, duration = '-') {
            const statusEl = document.getElementById('res-status');
            statusEl.textContent = status + ' ' + (statusTranslations[status] || '请求完成');
            statusEl.className = 'status-badge ' +
                (status >= 200 && status < 300 ? 'status-ok' : 'status-error');
            document.getElementById('res-time').textContent =
                duration === '-' ? '-' : duration + ' 毫秒';
            const code = document.getElementById('res-body');
            code.textContent = typeof value === 'string'
                ? value : JSON.stringify(value, null, 2);
        }

        async function sendRequest(method, path, body = null) {
            const token = document.getElementById('api-token').value.trim();
            const headers = {'Content-Type': 'application/json'};
            if (token) headers['X-Yanzi-Token'] = token;
            const options = {method, headers, credentials: 'same-origin'};
            if (method === 'POST' || method === 'PUT')
                options.body = typeof body === 'string' ? body : JSON.stringify(body ?? {});
            showResponse(0, '正在发送…');
            const start = performance.now();
            try {
                const response = await fetch(window.location.origin + path, options);
                const duration = (performance.now() - start).toFixed(1);
                const raw = await response.text();
                let data;
                try { data = JSON.parse(raw); }
                catch { data = raw; }
                showResponse(response.status, data, duration);
                if (response.status === 401) {
                    resetInstalledPrograms('身份验证失效');
                    setAuthState('尚未登录或 Token 已失效；请到燕子设置中获取当前 Token。');
                }
                return response.ok ? data : null;
            } catch (error) {
                showResponse(0, '请求失败：' + error.message,
                    (performance.now() - start).toFixed(1));
                return null;
            }
        }

        async function bootstrapConsole() {
            resetInstalledPrograms('请先登录并读取已安装小程序');
            try {
                const response = await fetch('/v1/console/session', {
                    credentials: 'same-origin', cache: 'no-store'
                });
                if (response.ok) {
                    setAuthState('已恢复此浏览器的本机登录状态。', true);
                    await refreshInstalledPrograms();
                } else {
                    setAuthState('首次使用：在燕子设置中复制 Token，点击“验证并记住此浏览器”。');
                }
            } catch {
                setAuthState('暂时无法连接本地 API 服务，请检查燕子是否已启动。');
            }
        }

        function testHealth() {
            sendRequest('GET', '/health');
        }

        function testListExtensions() {
            refreshInstalledPrograms(true);
        }

        function testDeployment(action) {
            const id = document.getElementById('deploy-ext-id').value.trim();
            if (!id) return alert('请先选择或填写已安装的小程序 ID');
            if (!['status', 'build', 'reload'].includes(action)) return;
            if (action === 'reload' && !confirm('重新加载会关闭当前实例；运行时失败时不会自动回滚。是否继续？')) return;
            const endpoint = `/v1/extensions/${encodeURIComponent(id)}/${action}`;
            sendRequest(action === 'status' ? 'GET' : 'POST', endpoint);
        }

        async function testUiSession(action) {
            const id = document.getElementById('ui-ext-id').value.trim();
            const indicator = document.getElementById('ui-session-state');
            if (!id) return alert('请先读取并选择小程序');
            let method = 'GET', path = '', body = null;
            if (action === 'start' || action === 'foreground') {
                if (window.yanziCurrentUiSessionId) {
                    return alert('请先结束已有会话');
                }
                if (action === 'foreground' &&
                    !confirm('前台测试将显示蓝色屏幕边缘和 AI 光标提示。真实用户输入会自动暂停。继续吗？'))
                    return;
                method = 'POST';
                path = '/v1/extensions/' + encodeURIComponent(id) + '/ui/sessions';
                body = {
                    mode: action === 'foreground' ? 'foreground' : 'scoped',
                    userConsent: action === 'foreground',
                    launchIfNeeded: true
                };
            } else {
                const sessionId = window.yanziCurrentUiSessionId;
                if (!sessionId) return alert('尚未建立 UI 会话');
                path = '/v1/ui/sessions/' + sessionId;
                if (action !== 'status') {
                    method = action === 'stop' ? 'DELETE' : 'POST';
                    if (action !== 'stop') path += '/' + action;
                }
            }
            const response = await sendRequest(method, path, body);
            if (!response) return;
            if (response.session?.sessionId)
                window.yanziCurrentUiSessionId = response.session.sessionId;
            if (response.ended) window.yanziCurrentUiSessionId = null;
            indicator.textContent = response.session
                ? ({active:'测试中',paused:'已暂停',ended:'已结束'}[response.session.state] || response.session.state) + ' · ' + response.session.stage +
                  ' · ' + ({scoped:'小程序窗口',foreground:'前台提示'}[response.session.mode] || response.session.mode) + ' · 会话 ' +
                  response.session.sessionId.slice(0, 8)
                : response.ended ? '会话已结束，边框与光标提示已释放'
                : '无法获取会话状态';
        }

        async function testUi(action) {
            const id = document.getElementById('ui-ext-id').value.trim();
            if (!id) return alert('请先读取并选择小程序');
            const prefix = '/v1/extensions/' + encodeURIComponent(id) + '/ui/';
            let method = 'GET', route = action, body = null;
            if (action === 'open') {
                method = 'POST'; body = {launchIfNeeded: true};
            } else if (action === 'capture') {
                method = 'POST'; body = {mode: 'auto', launchIfNeeded: false};
            } else if (action === 'actions') {
                method = 'POST';
                try { body = JSON.parse(document.getElementById('ui-action-json').value); }
                catch { return alert('操作步骤必须是有效的 JSON'); }
                if (!confirm('即将操作所选扩展的真实窗口。请勿在测试脚本中触发真实数据保存或删除。')) return;
            }
            if (body && window.yanziCurrentUiSessionId && (action === 'capture' || action === 'actions'))
                body.sessionId = window.yanziCurrentUiSessionId;
            const payload = await sendRequest(method, prefix + route, body);
            if (action !== 'capture' && action !== 'actions') return;
            try {
                const snapshots = action === 'capture' ? [payload.capture] : payload.captures;
                const last = snapshots?.[snapshots.length - 1];
                if (!last?.imageUrl) return;
                const token = document.getElementById('api-token').value.trim();
                const headers = token ? {'X-Yanzi-Token': token} : {};
                const result = await fetch(last.imageUrl, {headers, cache: 'no-store', credentials: 'same-origin'});
                if (!result.ok) throw new Error('截图请求失败（HTTP ' + result.status + '）');
                const preview = document.getElementById('ui-preview');
                if (preview.dataset.objectUrl) URL.revokeObjectURL(preview.dataset.objectUrl);
                preview.dataset.objectUrl = URL.createObjectURL(await result.blob());
                preview.src = preview.dataset.objectUrl;
                preview.style.display = 'block';
            } catch (err) {
                document.getElementById('res-body').textContent += '\n图片预览失败：' + err.message;
            }
        }

        function testRunExtension() {
            const id = document.getElementById('run-ext-id').value.trim();
            const input = document.getElementById('run-input').value;
            if (!id) return alert('请先选择或填写已安装的小程序 ID');
            sendRequest('POST', `/v1/extensions/${encodeURIComponent(id)}/run`, { input });
        }

        function testStopExtension() {
            const id = document.getElementById('stop-ext-id').value.trim();
            if (!id) return alert('请先选择或填写已安装的小程序 ID');
            sendRequest('POST', `/v1/extensions/${encodeURIComponent(id)}/stop`);
        }

        function testDeleteExtension() {
            const id = document.getElementById('delete-ext-id').value.trim();
            if (!id) return alert('请先选择或填写已安装的小程序 ID');
            sendRequest('DELETE', `/v1/extensions/${encodeURIComponent(id)}`);
        }

        function testPurgeExtension() {
            const id = document.getElementById('purge-ext-id').value.trim();
            if (!id) return alert('请先选择或填写已安装的小程序 ID');
            sendRequest('DELETE', `/v1/extensions/recycle-bin/${encodeURIComponent(id)}`);
        }

        function testGetStorage() {
            const id = document.getElementById('get-store-id').value.trim();
            const key = document.getElementById('get-store-key').value.trim();
            if (!id || !key) return alert('请输入小程序 ID 和存储键');
            sendRequest('GET', `/v1/storage/${encodeURIComponent(id)}?key=${encodeURIComponent(key)}`);
        }

        function testPutStorage() {
            const id = document.getElementById('put-store-id').value.trim();
            const key = document.getElementById('put-store-key').value.trim();
            const content = document.getElementById('put-store-content').value;
            if (!id || !key) return alert('请输入小程序 ID 和存储键');
            sendRequest('PUT', `/v1/storage/${encodeURIComponent(id)}`, { key, content });
        }

        function testGetYanmState() {
            sendRequest('GET', '/v1/me/yanm-state');
        }

        function testPutYanmState() {
            const content = document.getElementById('put-yanm-content').value.trim();
            if (!content) return alert('请输入燕幕配置 JSON');
            try {
                const parsed = JSON.parse(content);
                sendRequest('PUT', '/v1/me/yanm-state', parsed);
            } catch (e) {
                alert('JSON 格式错误: ' + e.message);
            }
        }
        bootstrapConsole();
    </script>
</body>
</html>
""";
        // /docs is public on loopback: never embed an API credential in HTML.
        return html.Replace("__YANZI_TOKEN__", string.Empty);
    }

    private async Task SendBrowserWebAppStorageResponseAsync(WebSocket webSocket, object payload)
    {
        if (webSocket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
    }

    private async Task HandleBrowserWebAppStorageMessageAsync(
        WebSocket webSocket,
        JsonElement json,
        string messageType,
        int socketId)
    {
        var requestId = json.TryGetProperty("requestId", out var requestIdProp)
            ? requestIdProp.GetString() ?? string.Empty
            : string.Empty;
        var appId = json.TryGetProperty("appId", out var appIdProp)
            ? appIdProp.GetString() ?? string.Empty
            : string.Empty;
        var key = json.TryGetProperty("key", out var keyProp)
            ? keyProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(requestId) ||
            string.IsNullOrWhiteSpace(appId) ||
            string.IsNullOrWhiteSpace(key))
        {
            await SendBrowserWebAppStorageResponseAsync(webSocket, new
            {
                type = "webapp_storage_response",
                requestId,
                operation = messageType,
                ok = false,
                available = false,
                error = "requestId/appId/key is required"
            });
            return;
        }

        var extensionId = "browser.webapp." + appId.Trim();

        try
        {
            if (messageType == "webapp_storage_get")
            {
                var result = await AccountExtensionDataStore.TryReadAsync(extensionId, key, _cts.Token);
                await SendBrowserWebAppStorageResponseAsync(webSocket, new
                {
                    type = "webapp_storage_response",
                    requestId,
                    operation = "get",
                    ok = true,
                    available = result.Available,
                    exists = result.Exists,
                    revision = result.Revision,
                    content = result.Content
                });
                return;
            }

            if (messageType == "webapp_storage_put")
            {
                var content = json.TryGetProperty("content", out var contentProp)
                    ? contentProp.GetString() ?? string.Empty
                    : string.Empty;
                if (Encoding.UTF8.GetByteCount(content) > 2 * 1024 * 1024)
                {
                    throw new InvalidOperationException("网页小程序单项同步数据不能超过 2MB。");
                }

                var result = await AccountExtensionDataStore.WriteAsync(
                    extensionId,
                    key,
                    content,
                    cancellationToken: _cts.Token);
                await SendBrowserWebAppStorageResponseAsync(webSocket, new
                {
                    type = "webapp_storage_response",
                    requestId,
                    operation = "put",
                    ok = true,
                    available = result.Available,
                    revision = result.Revision
                });
                HostAssets.AppendLog($"[BrowserWS #{socketId}] Web app cloud data saved: app={appId}, key={key}, revision={result.Revision}");
                return;
            }

            if (messageType == "webapp_storage_delete")
            {
                var result = await AccountExtensionDataStore.DeleteAsync(
                    extensionId,
                    key,
                    cancellationToken: _cts.Token);
                await SendBrowserWebAppStorageResponseAsync(webSocket, new
                {
                    type = "webapp_storage_response",
                    requestId,
                    operation = "delete",
                    ok = true,
                    available = result.Available,
                    revision = result.Revision
                });
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[BrowserWS #{socketId}] Web app storage error: {ex.Message}");
            await SendBrowserWebAppStorageResponseAsync(webSocket, new
            {
                type = "webapp_storage_response",
                requestId,
                operation = messageType,
                ok = false,
                available = true,
                error = ex.Message
            });
        }
    }

    private async Task HandleBrowserWebSocketLoopAsync(WebSocket webSocket, int socketId, string browserName)
    {
        var buffer = new byte[1024 * 64];
        try
        {
            while (webSocket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    HostAssets.AppendLog($"[BrowserWS #{socketId}] Client requested Close ({result.CloseStatus}: {result.CloseStatusDescription}).");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var rawJson = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    if (!result.EndOfMessage)
                    {
                        using var ms = new MemoryStream();
                        await ms.WriteAsync(buffer, 0, result.Count);
                        while (!result.EndOfMessage)
                        {
                            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                            await ms.WriteAsync(buffer, 0, result.Count);
                        }
                        rawJson = Encoding.UTF8.GetString(ms.ToArray());
                    }

                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(rawJson);
                        var json = jsonDoc.RootElement;
                        if (json.TryGetProperty("type", out var typeProp))
                        {
                            var msgType = typeProp.GetString();
                            if (msgType == "ping")
                            {
                                var pongBytes = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                                await webSocket.SendAsync(new ArraySegment<byte>(pongBytes), WebSocketMessageType.Text, true, _cts.Token);
                            }
                            else if (msgType == "register")
                            {
                                HostAssets.AppendLog($"[BrowserWS #{socketId}] Registered successfully from {browserName}.");
                            }
                            else if (msgType is "webapp_storage_get" or "webapp_storage_put" or "webapp_storage_delete")
                            {
                                await HandleBrowserWebAppStorageMessageAsync(webSocket, json, msgType, socketId);
                            }
                            else if (msgType == "browser_extension_reload_ack")
                            {
                                var requestId = json.TryGetProperty("requestId", out var reloadRequestId)
                                    ? reloadRequestId.GetString() ?? string.Empty
                                    : string.Empty;
                                var version = json.TryGetProperty("version", out var reloadVersion)
                                    ? reloadVersion.GetString() ?? string.Empty
                                    : string.Empty;
                                HostAssets.AppendLog($"[BrowserWS #{socketId}] Browser extension reload ACK: requestId={requestId}, version={version}");
                            }
                            else if (msgType == "browser_extension_runtime_ready")
                            {
                                var version = json.TryGetProperty("version", out var runtimeVersion)
                                    ? runtimeVersion.GetString() ?? string.Empty
                                    : string.Empty;
                                var refreshedTabs = json.TryGetProperty("refreshedWebAppTabs", out var refreshedTabsProp) &&
                                                    refreshedTabsProp.TryGetInt32(out var refreshedTabCount)
                                    ? refreshedTabCount
                                    : 0;
                                HostAssets.AppendLog($"[BrowserWS #{socketId}] Browser extension runtime ready: version={version}, refreshedWebAppTabs={refreshedTabs}");
                            }
                            else if (msgType == "task_response")
                            {
                                if (json.TryGetProperty("taskId", out var taskIdProp))
                                {
                                    var taskId = taskIdProp.GetString();
                                    HostAssets.AppendLog($"[BrowserWS #{socketId}] Received task response: taskId={taskId}");
                                    if (!string.IsNullOrEmpty(taskId) && _pendingBrowserTasks.TryRemove(taskId, out var tcs))
                                    {
                                        tcs.SetResult(json.Clone());
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"[BrowserWS #{socketId}] Error parsing message: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[BrowserWS #{socketId}] Disconnected with exception: {ex.Message}");
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _activeBrowserSocket, null, webSocket) == webSocket)
            {
                ConnectedBrowserName = "";
                BrowserConnectionChanged?.Invoke(false);
            }
            try
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                }
            }
            catch { /* ignore */ }
            webSocket.Dispose();
            HostAssets.AppendLog($"[BrowserWS #{socketId}] Connection session ended.");
        }
    }
}
