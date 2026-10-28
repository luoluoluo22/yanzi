using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>Shell attachment; disconnecting never shuts down the shared Runtime.</summary>
public static class RuntimeConnection
{
    private static RuntimeRpcServer? _callback;
    private static MainWindow? _window;
    private static string? _catalogJson;
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

    // Retired: the installed Yanzi.exe owns all background services in-process.
    public static Task EnsureAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Standalone Yanzi.Runtime is retired."));

    public static string? FindExecutable() => null; // No standalone Runtime may be discovered or started.

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

    public static Task AttachAsync(MainWindow window) =>
        Task.FromException(new NotSupportedException("Runtime IPC attachment is retired in the single-process release."));

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
        Queue("shell.detach", new { pid = Environment.ProcessId });
        _callback?.Dispose();
        _callback = null;
        IsConnected = false;
        _window = null;
    }
}
