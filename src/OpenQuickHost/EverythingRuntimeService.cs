using System.Diagnostics;
using System.IO;

namespace OpenQuickHost;

public static class EverythingRuntimeService
{
    private const string RuntimeDirectoryName = "EverythingRuntime";
    private const string RuntimeExecutableName = "Everything.exe";
    private static readonly Lock SyncLock = new();
    private static int? _launchedProcessId;

    public static void EnsureStartedInBackground()
    {
        _ = Task.Run(() => EnsureRunning());
    }

    public static bool IsProcessRunning()
    {
        return EverythingProcessExists();
    }

    public static bool HasBundledRuntime()
    {
        return File.Exists(GetBundledRuntimeExecutablePath());
    }

    public static bool ShowInteractiveSetup()
    {
        var runtimeExecutablePath = GetBundledRuntimeExecutablePath();
        if (!File.Exists(runtimeExecutablePath))
        {
            HostAssets.AppendLog($"Everything interactive setup skipped: bundled executable not found at {runtimeExecutablePath}.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = runtimeExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(runtimeExecutablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            HostAssets.AppendLog("Everything interactive setup launched.");
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Everything interactive setup launch failed: {ex.Message}");
            return false;
        }
    }

    public static bool EnsureRunning()
    {
        lock (SyncLock)
        {
            var configUpdated = EnsureOptimizedConfig(HostAssets.EverythingRuntimeConfigPath);

            var configPath = HostAssets.EverythingRuntimeConfigPath;
            var dbPath = HostAssets.EverythingRuntimeDatabasePath;
            var isDbStale = File.Exists(configPath) && File.Exists(dbPath) && File.GetLastWriteTimeUtc(configPath) > File.GetLastWriteTimeUtc(dbPath);

            if (configUpdated || isDbStale)
            {
                HostAssets.AppendLog("Everything config updated or database stale with old index, purging stale DB and restarting runtime to apply exclusions...");
                StopOwnedRuntime();
                KillAllYanziEverythingProcesses();
                try
                {
                    if (File.Exists(dbPath))
                    {
                        File.Delete(dbPath);
                    }
                }
                catch { }
            }

            if (EverythingSearchService.IsIpcReachable())
            {
                return true;
            }

            return TryStartRuntime(isRetry: false);
        }
    }

    private static bool TryStartRuntime(bool isRetry)
    {
        if (EverythingProcessExists())
        {
            HostAssets.AppendLog("Everything runtime skipped: existing Everything process detected.");
            return false;
        }

        var runtimeExecutablePath = GetBundledRuntimeExecutablePath();
        if (!File.Exists(runtimeExecutablePath))
        {
            HostAssets.AppendLog($"Everything runtime skipped: bundled executable not found at {runtimeExecutablePath}.");
            return false;
        }

        Directory.CreateDirectory(HostAssets.EverythingRuntimeDataPath);
        EnsureOptimizedConfig(HostAssets.EverythingRuntimeConfigPath);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = runtimeExecutablePath,
                Arguments = BuildArguments(),
                WorkingDirectory = Path.GetDirectoryName(runtimeExecutablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(startInfo);
            if (process != null)
            {
                _launchedProcessId = process.Id;
                ChildProcessTracker.AddProcess(process);
            }
            HostAssets.AppendLog($"Everything runtime launch requested: path={runtimeExecutablePath}, args={startInfo.Arguments}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Everything runtime launch failed: {ex.Message}");
            return false;
        }

        var isReady = WaitForIpcReady(TimeSpan.FromSeconds(5));
        if (!isReady && !isRetry)
        {
            HostAssets.AppendLog("Everything runtime failed to respond, attempting automatic recovery by resetting database...");
            StopOwnedRuntime();
            KillAllYanziEverythingProcesses();
            try
            {
                if (File.Exists(HostAssets.EverythingRuntimeDatabasePath))
                {
                    File.Delete(HostAssets.EverythingRuntimeDatabasePath);
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Failed to delete corrupted Everything database: {ex.Message}");
            }

            return TryStartRuntime(isRetry: true);
        }

        return isReady;
    }

    public static void StopOwnedRuntime()
    {
        int? processId;
        lock (SyncLock)
        {
            processId = _launchedProcessId;
            _launchedProcessId = null;
        }

        if (processId == null)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            HostAssets.AppendLog($"Everything runtime stopped: pid={processId.Value}");
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Everything runtime stop failed: pid={processId.Value}, error={ex.Message}");
        }
    }

    public static void KillAllYanziEverythingProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("Everything");
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path != null && (path.Contains("Yanzi", StringComparison.OrdinalIgnoreCase) || 
                                         path.Contains("OpenQuickHost", StringComparison.OrdinalIgnoreCase) || 
                                         path.Contains("EverythingRuntime", StringComparison.OrdinalIgnoreCase)))
                    {
                        process.Kill();
                        HostAssets.AppendLog($"Killed lingering Everything process: pid={process.Id}");
                    }
                }
                catch
                {
                    // Ignore access denied on processes we don't own
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to kill lingering Everything processes: {ex.Message}");
        }
    }

    private static string GetBundledRuntimeExecutablePath()
    {
        return Path.Combine(AppContext.BaseDirectory, RuntimeDirectoryName, RuntimeExecutableName);
    }

    private static bool EverythingProcessExists()
    {
        try
        {
            return Process.GetProcessesByName("Everything").Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForIpcReady(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (EverythingSearchService.IsIpcReachable())
            {
                HostAssets.AppendLog("Everything runtime is reachable over IPC.");
                return true;
            }

            Thread.Sleep(200);
        }

        HostAssets.AppendLog("Everything runtime did not become reachable before timeout.");
        return false;
    }

    private static string BuildArguments()
    {
        return string.Join(
            ' ',
            [
                "-startup",
                "-config", Quote(HostAssets.EverythingRuntimeConfigPath),
                "-db", Quote(HostAssets.EverythingRuntimeDatabasePath)
            ]);
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static bool EnsureOptimizedConfig(string configPath)
    {
        try
        {
            const string defaultExcludes = @"exclude_folders=""C:\Windows\WinSxS"";""*\node_modules"";""*\.git"";""*\.vs"";""*\AppData\Local\Temp"";""*\$Recycle.Bin"";""*\System Volume Information""";
            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, $"exclude_list_enabled=1\r\n{defaultExcludes}\r\n", System.Text.Encoding.UTF8);
                return true;
            }

            var content = File.ReadAllText(configPath);
            if (!content.Contains("exclude_folders=") || content.Contains("exclude_folders=\r\n") || content.Contains("exclude_folders=\n") || content.Contains("exclude_folders=\"\""))
            {
                var updated = System.Text.RegularExpressions.Regex.Replace(
                    content,
                    @"(?m)^exclude_folders=.*$",
                    defaultExcludes);

                if (!updated.Contains("exclude_list_enabled=1"))
                {
                    updated = System.Text.RegularExpressions.Regex.Replace(
                        updated,
                        @"(?m)^exclude_list_enabled=.*$",
                        "exclude_list_enabled=1");
                }

                File.WriteAllText(configPath, updated, System.Text.Encoding.UTF8);
                return true;
            }
        }
        catch
        {
            // Ignore failure to ensure config
        }

        return false;
    }
}
