using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace OpenQuickHost;

public sealed class SharedRuntimeHost : IDisposable
{
    public static SharedRuntimeHost? Current { get; private set; }
    public static string RuntimeLogPath => Path.Combine(HostAssets.DataRootPath, "logs", "runtime.log");
    private readonly MainWindow _window;
    private readonly RuntimeRpcServer _server;
    private readonly ConcurrentDictionary<int, ShellLease> _clients = new();
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly DispatcherTimer _settingsTimer;
    private DateTime _settingsWriteTime;
    private string? _stableShellPath;
    private readonly SemaphoreSlim _uiRequests = new(1, 1);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _executions = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _cancelledExecutions = new();
    private sealed record ShellLease(int Pid, string Pipe, string Path, bool Development,
        DateTimeOffset SeenAt, DateTimeOffset ActiveAt);

    public SharedRuntimeHost(MainWindow window)
    {
        _window = window;
        _server = new(RuntimeRpc.PipeName, HandleAsync);
        _settingsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _settingsTimer.Tick += async (_, _) =>
        {
            foreach (var cancelled in _cancelledExecutions)
                if (DateTimeOffset.UtcNow - cancelled.Value > TimeSpan.FromMinutes(1)) _cancelledExecutions.TryRemove(cancelled.Key, out _);
            try { if (_window.CloudSyncClient != null) await _window.CloudSyncClient.ReloadPersistedSessionAsync(); }
            catch (Exception ex) { HostAssets.AppendLog("Runtime account refresh failed: " + ex.Message); }
            var written = File.GetLastWriteTimeUtc(AppSettingsStore.SettingsPath);
            if (written == _settingsWriteTime) return;
            _settingsWriteTime = written;
            _window.RefreshAppSettings();
            StartupRegistrationService.Apply(AppSettingsStore.Load().LaunchAtStartup);
        };
    }

    public void Start()
    {
        if (!HostRuntimeProfile.IsRuntime) throw new InvalidOperationException("Only Runtime may host background IPC.");
        Current = this;
        _server.Start();
        _settingsTimer.Start();
        HostAssets.AppendLog($"Shared Runtime started: pid={Environment.ProcessId}, instance={_instanceId}, protocol={RuntimeRpc.Version}");
    }

    private async Task<object?> HandleAsync(string operation, JsonElement payload)
    {
        if (operation == "script.cancel")
        {
            var executionId = payload.GetProperty("executionId").GetGuid();
            _cancelledExecutions[executionId] = DateTimeOffset.UtcNow;
            if (_executions.TryGetValue(executionId, out var cancellation))
                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
            return new { cancelled = true };
        }
        if (operation is "shell.attach" or "shell.touch")
        {
            var pid = payload.GetProperty("pid").GetInt32();
            var pipe = payload.GetProperty("pipe").GetString()!;
            if (!pipe.StartsWith(RuntimeRpc.PipeName + ".shell." + pid + ".", StringComparison.Ordinal))
                throw new ArgumentException("Shell 回调地址无效。");
            using var process = Process.GetProcessById(pid);
            if (process.SessionId != Process.GetCurrentProcess().SessionId) throw new UnauthorizedAccessException("Shell 会话不匹配。");
            var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("Shell 进程已退出。");
            var development = payload.GetProperty("development").GetBoolean();
            var now = DateTimeOffset.UtcNow;
            _clients.TryGetValue(pid, out var previous);
            var activeAt = payload.GetProperty("active").GetBoolean() ? now : previous?.ActiveAt ?? DateTimeOffset.MinValue;
            _clients[pid] = new(pid, pipe, executable, development, now, activeAt);
            if (!development) _stableShellPath = executable;
            return new { instanceId = _instanceId, protocolVersion = RuntimeRpc.Version, pid = Environment.ProcessId };
        }
        if (operation == "shell.detach")
        {
            _clients.TryRemove(payload.GetProperty("pid").GetInt32(), out _);
            return new { detached = true };
        }
        // All stateful service operations run on the one owning dispatcher.
        return await await _window.Dispatcher.InvokeAsync(async () =>
        {
            switch (operation)
            {
                case "health":
                case "status":
                    return (object)new
                    {
                        protocolVersion = RuntimeRpc.Version, pid = Environment.ProcessId, instanceId = _instanceId,
                        releaseVersion = typeof(App).Assembly.GetName().Version?.ToString(),
                        releaseHash = ReleaseBuildIdentity.AssemblySha256,
                        executablePath = Environment.ProcessPath,
                        startedAt = _started, backgroundServices = _window.GetRuntimeServiceStatus(),
                        clients = GetLiveClients().Select(c => new { pid = c.Pid, development = c.Development }).ToArray(),
                        running = RunningExtensionRegistry.GetSnapshot()
                    };
                case "catalog": return _window.GetAllCommands().Select(RuntimeCommandDescriptor.FromCommand).ToArray();
                case "running.list": return RunningExtensionRegistry.GetSnapshot();
                case "running.stop":
                    var ok = RunningExtensionRegistry.TryTerminate(payload.GetProperty("instanceId").GetGuid(), out var message);
                    return new { success = ok, message };
                case "command.execute":
                case "script.execute":
                    var id = payload.GetProperty("id").GetString()!;
                    CommandItem command;
                    if (payload.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.Object)
                    {
                        var descriptor = preview.Deserialize<RuntimeCommandDescriptor>(RuntimeRpc.Json)!;
                        var directory = Path.GetFullPath(descriptor.Directory ?? throw new ArgumentException("测试目录缺失。"));
                        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "yanzi-extension-test")) + Path.DirectorySeparatorChar;
                        var temporaryInstalled = Path.GetDirectoryName(directory)?.Equals(HostAssets.ExtensionsPath, StringComparison.OrdinalIgnoreCase) == true
                            && Path.GetFileName(directory).StartsWith("_temp_run_", StringComparison.Ordinal);
                        if (!directory.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && !temporaryInstalled)
                            throw new UnauthorizedAccessException("仅接受小程序编辑器和临时试运行目录。");
                        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("测试目录已移除。");
                        command = descriptor.ToCommand();
                    }
                    else command = _window.GetAllCommands().FirstOrDefault(c => c.ExtensionId.Equals(id, StringComparison.OrdinalIgnoreCase))
                        ?? throw new KeyNotFoundException("Runtime 未安装此小程序：" + id);
                    var input = payload.TryGetProperty("input", out var i) ? i.GetString() : null;
                    var source = payload.TryGetProperty("source", out var s) ? s.GetString() ?? "shell" : "shell";
                    if (operation == "script.execute")
                    {
                        var state = payload.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.Object
                            ? st.Deserialize<Dictionary<string, string>>(RuntimeRpc.Json) : null;
                        var executionId = payload.TryGetProperty("executionId", out var eid) ? eid.GetGuid() : Guid.NewGuid();
                        using var cancellation = new CancellationTokenSource();
                        if (!_executions.TryAdd(executionId, cancellation)) throw new InvalidOperationException("执行请求重复。");
                        try
                        {
                            if (_cancelledExecutions.TryRemove(executionId, out _)) cancellation.Cancel();
                            return await ScriptExtensionRunner.ExecuteAsync(command, input, source, state, cancellation.Token);
                        }
                        finally { _executions.TryRemove(executionId, out _); }
                    }
                    return await _window.ExecuteRuntimeCommandAsync(command, input, source);
                case "capabilities": return YanziCapabilityRegistry.List();
                case "auth.key":
                    var cloud = _window.CloudSyncClient ?? throw new InvalidOperationException("未启用云服务。");
                    await cloud.ReloadPersistedSessionAsync();
                    cloud.AdoptRuntimeEncryptionKey(payload.GetProperty("account").GetString()!,
                        payload.GetProperty("key").Deserialize<byte[]>(RuntimeRpc.Json)!);
                    return new { refreshed = true };
                case "capability.invoke":
                    return await YanziCapabilityRegistry.InvokeAsync(payload.GetProperty("name").GetString()!,
                        payload.TryGetProperty("payload", out var p) ? p.Clone() : null, YanziCapabilityCaller.LocalAgent);
                case "settings.refresh":
                    _window.RefreshAppSettings();
                    _window.ReloadLocalExtensionsFromExternal();
                    return new { refreshed = true };
                case "sync.queue":
                    _window.QueueBackgroundWebDavSync("shell-request", true);
                    _window.SyncLocalExtensionsToCloud(true);
                    return new { queued = true };
                case "cloud.refresh":
                    await _window.RefreshCloudStateAsync(false);
                    return new { refreshed = true };
                case "chat.send":
                {
                    var chatCloud = _window.CloudSyncClient ?? throw new InvalidOperationException("未启用云服务。");
                    await chatCloud.ReloadPersistedSessionAsync();
                    if (!chatCloud.HasCredential || string.IsNullOrWhiteSpace(chatCloud.CurrentUserId))
                        throw new InvalidOperationException("account_login_required");

                    var targetDeviceId = payload.GetProperty("targetDeviceId").GetString();
                    if (string.IsNullOrWhiteSpace(targetDeviceId))
                        throw new ArgumentException("targetDeviceId_required");

                    var kind = payload.TryGetProperty("kind", out var kindProp)
                        ? kindProp.GetString()?.Trim().ToLowerInvariant() ?? "text"
                        : "text";
                    if (kind is not ("text" or "photo" or "file"))
                        throw new ArgumentException("unsupported_chat_kind");

                    var text = payload.TryGetProperty("text", out var textProp)
                        ? textProp.GetString() ?? string.Empty
                        : string.Empty;
                    string? filePath = null;
                    if (payload.TryGetProperty("filePath", out var fileProp) &&
                        fileProp.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(fileProp.GetString()))
                    {
                        filePath = Path.GetFullPath(fileProp.GetString()!);
                        if (!File.Exists(filePath)) throw new FileNotFoundException("chat_file_not_found", filePath);
                        var info = new FileInfo(filePath);
                        if (info.Length <= 0 || info.Length > 30L * 1024 * 1024)
                            throw new IOException("chat_file_size_invalid");
                    }
                    if (kind is "photo" or "file" && filePath == null)
                        throw new ArgumentException("filePath_required");
                    if (kind == "text" && filePath != null)
                        throw new ArgumentException("text_message_cannot_have_file");

                    var clientMessageId = payload.TryGetProperty("clientMessageId", out var clientIdProp)
                        ? clientIdProp.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(clientMessageId))
                        clientMessageId = Guid.NewGuid().ToString("N");
                    if (!Guid.TryParseExact(clientMessageId, "N", out _))
                        throw new ArgumentException("clientMessageId_invalid");

                    var target = (await chatCloud.ListPeerDevicesAsync())
                        .FirstOrDefault(device => device.DeviceId == targetDeviceId);
                    if (target == null || !string.Equals(target.Platform, "android", StringComparison.OrdinalIgnoreCase))
                        throw new KeyNotFoundException("mobile_device_not_found");

                    var sourceDeviceId = Sync.DeviceIdentityStore.GetOrCreateDesktopDeviceId();
                    var inlinePhoto = payload.TryGetProperty("inlinePhoto", out var inlineProp) &&
                        inlineProp.ValueKind == JsonValueKind.True;
                    if (inlinePhoto)
                    {
                        if (kind != "photo" || filePath == null)
                            throw new ArgumentException("inlinePhoto_requires_photo");
                        var extension = Path.GetExtension(filePath).ToLowerInvariant();
                        var mime = extension switch
                        {
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".png" => "image/png",
                            _ => throw new ArgumentException("inlinePhoto_requires_jpeg_or_png")
                        };
                        var bytes = await File.ReadAllBytesAsync(filePath);
                        if (bytes.Length > 512 * 1024)
                            throw new IOException("inlinePhoto_too_large");
                        await chatCloud.RegisterDeviceAsync(
                            sourceDeviceId,
                            "desktop",
                            Sync.DeviceIdentityStore.GetDesktopDisplayName());
                        var screenshotDataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                        var messageId = await chatCloud.SendDeviceMessageAsync(
                            sourceDeviceId,
                            "android",
                            "photo",
                            "YanziChat",
                            text,
                            targetDeviceId: targetDeviceId,
                            payload: new
                            {
                                source = "desktop-chat",
                                screenshotDataUrl,
                                clientTransferId = clientMessageId,
                                clientOperationId = clientMessageId
                            },
                            clientMessageId: clientMessageId,
                            expiresAt: DateTimeOffset.UtcNow.AddDays(7));
                        return new
                        {
                            sent = true,
                            messageId,
                            clientMessageId,
                            targetDeviceId,
                            targetName = target.DisplayName,
                            kind,
                            fileName = Path.GetFileName(filePath),
                            deliveryMode = "inline-photo"
                        };
                    }

                    var job = Sync.DesktopChatOutbox.Enqueue(
                        chatCloud.CurrentUserId!,
                        sourceDeviceId,
                        clientMessageId,
                        kind,
                        text,
                        filePath,
                        targetDeviceId);
                    var queuedMessageId = await chatCloud.DeliverChatJobAsync(job);
                    return new
                    {
                        sent = true,
                        messageId = queuedMessageId,
                        clientMessageId,
                        targetDeviceId,
                        targetName = target.DisplayName,
                        kind,
                        fileName = filePath == null ? null : Path.GetFileName(filePath),
                        deliveryMode = "attachment"
                    };
                }
                case "chat.queue":
                {
                    var queueCloud = _window.CloudSyncClient ?? throw new InvalidOperationException("未启用云服务。");
                    await queueCloud.ReloadPersistedSessionAsync();
                    var queueDeviceId = payload.GetProperty("targetDeviceId").GetString();
                    if (string.IsNullOrWhiteSpace(queueDeviceId))
                        throw new ArgumentException("targetDeviceId_required");
                    var requestedLimit = payload.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var parsedLimit)
                        ? parsedLimit
                        : 100;
                    var queueLimit = Math.Clamp(requestedLimit, 1, 100);
                    var messages = await queueCloud.GetPendingDeviceMessagesAsync(queueDeviceId, queueLimit);
                    return new
                    {
                        targetDeviceId = queueDeviceId,
                        count = messages.Count,
                        items = messages.Select(item => new
                        {
                            item.MessageId,
                            item.Sequence,
                            item.Kind,
                            item.Status,
                            item.CreatedAt,
                            item.DeliveredAt,
                            item.AckedAt,
                            item.ExpiresAt,
                            item.ClientMessageId
                        }).ToArray()
                    };
                }
                case "chat.status":
                {
                    var statusCloud = _window.CloudSyncClient ?? throw new InvalidOperationException("未启用云服务。");
                    await statusCloud.ReloadPersistedSessionAsync();
                    var statusMessageId = payload.GetProperty("messageId").GetString();
                    if (string.IsNullOrWhiteSpace(statusMessageId))
                        throw new ArgumentException("messageId_required");
                    var chatMessage = await statusCloud.GetDeviceMessageAsync(statusMessageId)
                        ?? throw new KeyNotFoundException("chat_message_not_found");
                    return new
                    {
                        chatMessage.MessageId,
                        chatMessage.Status,
                        chatMessage.Kind,
                        chatMessage.SourceDeviceId,
                        chatMessage.TargetDeviceId,
                        chatMessage.CreatedAt,
                        chatMessage.DeliveredAt,
                        chatMessage.AckedAt,
                        chatMessage.ExpiresAt,
                        chatMessage.Receipts
                    };
                }
                case "listeners.pause": _window.PauseListenerServices(); return new { paused = true };
                case "listeners.resume": _window.ResumeListenerServices(); return new { paused = false };
                case "ui.approvals":
                    (System.Windows.Application.Current as App)?.ShowExternalApprovals();
                    return new { shown = true };
                case "scheduler.refresh":
                    _window.NotifyScheduleChanged(payload.GetProperty("id").GetString()!,
                        payload.TryGetProperty("schedule", out var schedule) ? schedule.GetString() : null);
                    return new { refreshed = true };
                default: throw new InvalidOperationException("未知 Runtime 操作：" + operation);
            }
        });
    }

    private IReadOnlyList<ShellLease> GetLiveClients()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var client in _clients.Values)
        {
            var alive = false;
            try { using var process = Process.GetProcessById(client.Pid); alive = !process.HasExited; } catch (ArgumentException) { }
            if (!alive || now - client.SeenAt > TimeSpan.FromSeconds(15)) _clients.TryRemove(client.Pid, out _);
        }
        return _clients.Values.OrderByDescending(c => c.ActiveAt).ThenBy(c => c.Development).ToArray();
    }

    public void RequestShell(string action, string? input = null)
    {
        _ = Task.Run(async () =>
        {
            await _uiRequests.WaitAsync();
            try
            {
            foreach (var client in GetLiveClients())
            {
                try
                {
                    // Allow the client selected by the user to bring its window to foreground.
                    NativeMethods.AllowSetForegroundWindow(client.Pid);
                    await RuntimeRpc.CallAsync(action, new { input }, pipeName: client.Pipe);
                    return;
                }
                catch { _clients.TryRemove(client.Pid, out _); }
            }
            if (_stableShellPath != null && File.Exists(_stableShellPath))
            {
                Process.Start(new ProcessStartInfo(_stableShellPath, "--show") { UseShellExecute = true });
            }
            else
            {
                HostAssets.AppendLog("No Shell is connected; Runtime background services remain active.");
            }
            }
            finally { _uiRequests.Release(); }
        });
    }

    public void Dispose()
    {
        _settingsTimer.Stop();
        foreach (var cancellation in _executions.Values) cancellation.Cancel();
        _server.Dispose();
        if (Current == this) Current = null;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool AllowSetForegroundWindow(int processId);
    }
}
