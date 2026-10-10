using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace OpenQuickHost;

/// <summary>Shell attachment; disconnecting never shuts down the shared Runtime.</summary>
public static class RuntimeConnection
{
    private static RuntimeRpcServer? _callback;
    private static DispatcherTimer? _heartbeat;
    private static MainWindow? _window;
    private static bool _busy;
    private static string? _catalogJson;
    private static int _catalogPoll;
    private static readonly string CallbackPipe = RuntimeRpc.PipeName + ".shell." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
    private static IReadOnlyList<RunningExtensionInfo> _running = [];
    public static IReadOnlyList<RunningExtensionInfo> Running => _running;
    public static bool IsConnected { get; private set; }
    public static Guid InstanceId { get; private set; }
    public static int RuntimePid { get; private set; }
    // An old background snapshot may remain alive during a Shell upgrade. Keep
    // serving existing jobs, but do not report a mismatched version as verified.
    public static bool IsRuntimeVersionCurrent { get; private set; }
    public static string InstallationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YanziRuntime", "runtime.json");

    public static async Task EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        try { await ReadHealthAsync(cancellationToken); return; } catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { }
        var executable = FindExecutable();
        if (executable == null) throw new InvalidOperationException("共享 Runtime 尚未安装。请运行 scripts/install-shared-runtime.ps1。");
        Process.Start(new ProcessStartInfo(executable, "--runtime --tray")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        });
        var until = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < until)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { await ReadHealthAsync(cancellationToken); return; }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) { }
            await Task.Delay(400, cancellationToken);
        }
        throw new InvalidOperationException("Runtime 未能启动。请确认旧版燕子已退出，再查看 Runtime 日志。");
    }

    public static string? FindExecutable()
    {
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var selfContainedHost = Path.Combine(baseDirectory, "Yanzi.exe");
        var independentRuntime = Path.Combine(baseDirectory, "Yanzi.Runtime.exe");
        var installedHost = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yanzi", "current", "Yanzi.exe");
        var officialInstall = Path.GetFullPath(selfContainedHost).Equals(
            Path.GetFullPath(installedHost), StringComparison.OrdinalIgnoreCase);
        var parent = Directory.GetParent(baseDirectory)?.FullName;
        var portableRelease = File.Exists(Path.Combine(baseDirectory, ".portable"))
            || (parent != null && File.Exists(Path.Combine(parent, ".portable")));
        // Never promote a development Shell or its isolated snapshot into the production Runtime.
        var useSharedHost = (officialInstall || portableRelease)
            && !HostRuntimeProfile.IsDevelopment && File.Exists(independentRuntime)
            && File.Exists(Path.Combine(baseDirectory, "coreclr.dll"));
        var legacyRuntime = Path.Combine(baseDirectory, "Runtime", "Yanzi.Runtime.exe");
        var sourceExecutable = useSharedHost ? independentRuntime
            : File.Exists(legacyRuntime) ? legacyRuntime : null;
        if (sourceExecutable != null)
        {
            // Copy to a content-addressed, standalone snapshot before starting. The runtime
            // survives replacing the official installer directory, while the installer
            // distributes the shared native/framework binaries only once.
            var bundleDirectory = Path.GetDirectoryName(sourceExecutable)!;
            using var fingerprint = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (var file in Directory.EnumerateFiles(bundleDirectory, "*", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                fingerprint.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(bundleDirectory, file)));
                using var stream = File.OpenRead(file);
                fingerprint.AppendData(System.Security.Cryptography.SHA256.HashData(stream));
            }
            var hash = Convert.ToHexString(fingerprint.GetHashAndReset());
            var snapshot = Path.Combine(Path.GetDirectoryName(InstallationPath)!, "bundled", hash);
            var executable = Path.Combine(snapshot, Path.GetFileName(sourceExecutable));
            if (!File.Exists(executable))
            {
                var staging = snapshot + "." + Guid.NewGuid().ToString("N");
                CopyDirectory(bundleDirectory, staging);
                try { Directory.Move(staging, snapshot); }
                catch (IOException) when (File.Exists(executable)) { Directory.Delete(staging, true); }
            }
            return executable;
        }
        if (!File.Exists(InstallationPath)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(InstallationPath));
        var path = document.RootElement.GetProperty("executable").GetString();
        return path != null && Path.IsPathFullyQualified(path) && File.Exists(path)
            && (Path.GetFileName(path).Equals("Yanzi.Runtime.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Equals("Yanzi.exe", StringComparison.OrdinalIgnoreCase)) ? path : null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static async Task ReadHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = await RuntimeRpc.CallAsync("health", cancellationToken: cancellationToken);
        RuntimePid = health.GetProperty("pid").GetInt32();
        InstanceId = health.GetProperty("instanceId").GetGuid();
        var actualVersion = health.TryGetProperty("releaseVersion", out var publishedVersion)
            ? publishedVersion.GetString() : null;
        var actualHash = health.TryGetProperty("releaseHash", out var hashProperty)
            ? hashProperty.GetString() : null;
        IsRuntimeVersionCurrent = string.Equals(actualVersion,
            typeof(App).Assembly.GetName().Version?.ToString(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(actualHash, ReleaseBuildIdentity.AssemblySha256, StringComparison.OrdinalIgnoreCase);
        if (!IsRuntimeVersionCurrent)
            HostAssets.AppendLog($"Runtime build mismatch: Shell={typeof(App).Assembly.GetName().Version}/{ReleaseBuildIdentity.AssemblySha256[..12]}, Runtime={actualVersion ?? "legacy/unknown"}/{actualHash?[..Math.Min(12, actualHash.Length)] ?? "unknown"}; existing background jobs preserved.");
        _running = health.GetProperty("running").Deserialize<RunningExtensionInfo[]>(RuntimeRpc.Json) ?? [];
        IsConnected = true;
    }

    public static async Task AttachAsync(MainWindow window)
    {
        _window = window;
        _callback = new(CallbackPipe, async (operation, payload) =>
        {
            return await window.Dispatcher.InvokeAsync<object?>(() =>
            {
                switch (operation)
                {
                    case "launcher.show": window.ShowPanel(); break;
                    case "launcher.toggle": window.TogglePanelVisibility(); break;
                    case "mouse.show": window.ShowMousePanel(); break;
                    case "mouse.hide": window.HideMousePanel(); break;
                    case "command.ui":
                        var id = payload.GetProperty("input").GetString();
                        var command = window.GetAllCommands().FirstOrDefault(c => c.ExtensionId == id);
                        if (command == null) throw new InvalidOperationException("界面尚未加载此小程序。");
                        window.ShowPanel();
                        _ = window.ExecuteRuntimeCommandAsync(command, null, "runtime-ui");
                        break;
                    default: throw new InvalidOperationException("未知界面操作：" + operation);
                }
                return new { handled = true };
            });
        });
        _callback.Start();
        await TouchAsync("shell.attach");
        await RefreshCatalogAsync();
        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _heartbeat.Tick += async (_, _) =>
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var previous = InstanceId;
                await ReadHealthAsync();
                await TouchAsync("shell.touch");
                if (previous != InstanceId || ++_catalogPoll % 5 == 0) await RefreshCatalogAsync();
                window.RefreshRuntimeRunningState();
            }
            catch (Exception ex)
            {
                IsConnected = false;
                window.SetRuntimeConnectionStatus("后台连接已断开：" + ex.Message);
                // Do not take over hooks, APIs, or extension execution after a disconnect.
            }
            finally { _busy = false; }
        };
        _heartbeat.Start();
        window.SetRuntimeConnectionStatus(IsRuntimeVersionCurrent
            ? $"已连接常驻 Runtime · PID {RuntimePid}"
            : $"Runtime 版本与宿主不一致 · PID {RuntimePid} · 请在维护窗口升级后台服务");
    }

    private static Task<JsonElement> TouchAsync(string operation) => RuntimeRpc.CallAsync(operation,
        new { pid = Environment.ProcessId, pipe = CallbackPipe, development = HostRuntimeProfile.IsDevelopment, active = _window?.IsActive == true });

    public static async Task RefreshCatalogAsync()
    {
        var catalog = await RuntimeRpc.CallAsync("catalog");
        var descriptors = catalog.Deserialize<RuntimeCommandDescriptor[]>(RuntimeRpc.Json) ?? [];
        var json = catalog.GetRawText();
        if (_window != null && _catalogJson != json) await _window.Dispatcher.InvokeAsync(() => _window.ApplyRuntimeCatalog(descriptors));
        _catalogJson = json;
        var capabilities = (await RuntimeRpc.CallAsync("capabilities")).Deserialize<YanziCapabilityDescriptor[]>(RuntimeRpc.Json) ?? [];
        foreach (var capability in capabilities)
        {
            YanziCapabilityRegistry.Register(new YanziCapabilityDefinition
            {
                Name = capability.Name, Description = capability.Description, ProviderExtensionId = capability.ProviderExtensionId,
                Version = capability.Version, Permissions = capability.Permissions, InputSchema = capability.InputSchema,
                OutputSchema = capability.OutputSchema,
                Handler = async payload => await RuntimeRpc.CallAsync("capability.invoke", new { name = capability.Name, payload })
            });
        }
    }

    public static void Queue(string operation, object? payload = null)
    {
        if (!HostRuntimeProfile.IsShell || !IsConnected) return;
        _ = Task.Run(async () =>
        {
            try { await RuntimeRpc.CallAsync(operation, payload); }
            catch (Exception ex) { HostAssets.AppendLog("Runtime request failed: " + operation + ": " + ex.Message); }
        });
    }

    public static void Detach()
    {
        _heartbeat?.Stop();
        Queue("shell.detach", new { pid = Environment.ProcessId });
        _callback?.Dispose();
        _callback = null;
        IsConnected = false;
        _window = null;
    }
}
