using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenQuickHost;

/// <summary>
/// Supervises the already-installed OpenCLI Browser Bridge daemon for browser
/// profiles that contain the OpenCLI extension. OpenCLI is a shared user tool:
/// never install, update, stop or uninstall someone else's CLI or store extension.
/// </summary>
internal static class OpenCliDaemonSupervisor
{
    private static System.Threading.Timer? _timer;
    private static int _checking;
    private static DateTime _lastWarningUtc = DateTime.MinValue;

    internal static bool ShouldWake(bool cliInstalled, bool browserRunning, bool extensionRegistered)
        => cliInstalled && browserRunning && extensionRegistered;

    internal static bool ShouldRestartDaemon(string status)
        => !status.Contains("Daemon: running", StringComparison.OrdinalIgnoreCase);

    internal static bool DaemonIsReady(string status)
        => status.Contains("Daemon: running", StringComparison.OrdinalIgnoreCase)
           && status.Contains("Extension: connected", StringComparison.OrdinalIgnoreCase);

    internal static void Start()
    {
        if (!BrowserOpenCliReader.IsCliInstalled || _timer != null) return;
        // Cheap timer until a supported browser launches. Do not start OpenCLI on
        // unrelated devices or create another browser extension without consent.
        _timer = new System.Threading.Timer(async _ =>
        {
            if (Interlocked.Exchange(ref _checking, 1) != 0) return;
            try { await CheckOnceAsync(); }
            finally { Interlocked.Exchange(ref _checking, 0); }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
    }

    internal static void Stop() => Interlocked.Exchange(ref _timer, null)?.Dispose();

    internal static async Task CheckOnceAsync()
    {
        if (!ShouldWake(BrowserOpenCliReader.IsCliInstalled,
                IsSupportedBrowserRunning(), HasRegisteredOpenCliExtension()))
            return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            string status;
            try { status = await BrowserOpenCliReader.RunAsync(["daemon", "status"], timeout.Token); }
            catch (InvalidOperationException) { status = ""; }
            if (DaemonIsReady(status)) return;

            // 'profile list' does not restart a daemon after an explicit stop.
            // Restart only when the daemon is down, never merely on a disconnected extension.
            if (ShouldRestartDaemon(status))
            {
                await BrowserOpenCliReader.RunAsync(["daemon", "restart"], timeout.Token);
                HostAssets.AppendLog("[OpenCLI] Stopped local daemon restart requested.");
            }
            var profiles = await BrowserOpenCliReader.RunAsync(["profile", "list"], timeout.Token);
            if (BrowserOpenCliReader.HasConnectedBridge(profiles))
            {
                HostAssets.AppendLog("[OpenCLI] Existing browser bridge connected; local daemon ready.");
                return;
            }
            LogWarning("Local daemon checked, but no OpenCLI Browser Bridge is connected. " +
                "Check the browser extension; do not overwrite store installations.");
        }
        catch (OperationCanceledException) { LogWarning("Bridge warmup timed out."); }
        catch (Exception ex) { LogWarning("Bridge warmup failed: " + ex.GetType().Name); }
    }

    private static void LogWarning(string message)
    {
        if (DateTime.UtcNow - _lastWarningUtc < TimeSpan.FromMinutes(15)) return;
        _lastWarningUtc = DateTime.UtcNow;
        HostAssets.AppendLog("[OpenCLI] " + message);
    }

    private static bool IsSupportedBrowserRunning()
        => Process.GetProcessesByName("ego").Length > 0 ||
           Process.GetProcessesByName("msedge").Length > 0 ||
           Process.GetProcessesByName("chrome").Length > 0;

    private static bool HasRegisteredOpenCliExtension()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var root in new[]
        {
            Path.Combine(local, "Citro Labs", "ego lite", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "Google", "Chrome", "User Data")
        })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root).Prepend(root))
                {
                    var file = Path.Combine(profile, "Secure Preferences");
                    if (!File.Exists(file)) continue;
                    // Chromium stores the signed extension manifest's name here.
                    // Never inspect cookies, local storage, tabs or account data.
                    if (File.ReadAllText(file).Contains("\"name\":\"OpenCLI\"",
                            StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }
}
