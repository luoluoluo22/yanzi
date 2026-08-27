using System.Diagnostics;
using System.IO;

namespace OpenQuickHost;

public static class EverythingRuntimeService
{
    private const string RuntimeDirectoryName = "EverythingRuntime";
    private const string RuntimeExecutableName = "Everything.exe";
    private static readonly Lock SyncLock = new();
    private static int? _launchedProcessId;
    private const int ConfigSchemaVersion = 2;

    private static string SchemaVersionFilePath => Path.Combine(HostAssets.EverythingRuntimeDataPath, "schema_version.txt");

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
            // 1. 如果系统上已有可用 Everything（如用户已开机运行 Everything 1.5/1.4），直接复用，无需重复拉起
            if (EverythingSearchService.IsIpcReachable())
            {
                return true;
            }

            // 2. 检查配置是否需要初始化或版本升级（只有真正的升级才返回 true）
            var needsDbRebuild = EnsureOptimizedConfig(HostAssets.EverythingRuntimeConfigPath);

            if (needsDbRebuild)
            {
                HostAssets.AppendLog("Everything config upgraded, purging database and restarting runtime to apply changes...");
                StopOwnedRuntime();
                KillAllYanziEverythingProcesses();
                try
                {
                    if (File.Exists(HostAssets.EverythingRuntimeDatabasePath))
                    {
                        File.Delete(HostAssets.EverythingRuntimeDatabasePath);
                    }
                }
                catch { }
            }
            else
            {
                StopOwnedRuntime();
                KillAllYanziEverythingProcesses();
            }

            return TryStartRuntime(isRetry: false);
        }
    }

    public static void RebuildDatabaseAndRestart()
    {
        lock (SyncLock)
        {
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
                HostAssets.AppendLog($"Failed to delete Everything database for rebuild: {ex.Message}");
            }

            _ = Task.Run(() => EnsureRunning());
        }
    }

    private static bool TryStartRuntime(bool isRetry)
    {
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

        var isReady = WaitForIpcReady(TimeSpan.FromSeconds(15));
        if (!isReady)
        {
            HostAssets.AppendLog("Everything runtime did not respond within timeout, background indexing may still be in progress.");
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
            try { process.WaitForExit(1000); } catch { }
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
                        try { process.WaitForExit(1000); } catch { }
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
            Directory.CreateDirectory(HostAssets.EverythingRuntimeDataPath);

            var currentVersion = 0;
            if (File.Exists(SchemaVersionFilePath) && int.TryParse(File.ReadAllText(SchemaVersionFilePath).Trim(), out var parsedVersion))
            {
                currentVersion = parsedVersion;
            }

            var fileExists = File.Exists(configPath);
            if (fileExists && currentVersion == ConfigSchemaVersion)
            {
                return false;
            }

            var content = fileExists ? File.ReadAllText(configPath) : string.Empty;

            content = SetOrAddIniProperty(content, "run_in_background", "1");
            content = SetOrAddIniProperty(content, "show_in_taskbar", "0");
            content = SetOrAddIniProperty(content, "show_tray_icon", "0");
            content = SetOrAddIniProperty(content, "minimize_to_tray", "0");
            content = SetOrAddIniProperty(content, "check_for_updates_on_startup", "0");
            content = SetOrAddIniProperty(content, "allow_multiple_instances", "1");

            // 启用排除列表并排除 Windows WinSxS
            content = SetOrAddIniProperty(content, "exclude_list_enabled", "1");
            if (!content.Contains("exclude_folders="))
            {
                content = SetOrAddIniProperty(content, "exclude_folders", @"""C:\Windows\WinSxS""");
            }

            File.WriteAllText(configPath, content, System.Text.Encoding.UTF8);
            File.WriteAllText(SchemaVersionFilePath, ConfigSchemaVersion.ToString(), System.Text.Encoding.UTF8);

            // 仅在已有旧版本升级时才需要重建数据库，首次初始化直接使用新配置即可
            return fileExists && currentVersion > 0 && currentVersion < ConfigSchemaVersion;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"EnsureOptimizedConfig error: {ex.Message}");
            return false;
        }
    }

    private static string SetOrAddIniProperty(string content, string key, string value)
    {
        var pattern = @"(?m)^" + System.Text.RegularExpressions.Regex.Escape(key) + @"=.*$";
        if (System.Text.RegularExpressions.Regex.IsMatch(content, pattern))
        {
            return System.Text.RegularExpressions.Regex.Replace(content, pattern, $"{key}={value}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return $"{key}={value}";
        }

        return $"{content}\r\n{key}={value}";
    }
}
