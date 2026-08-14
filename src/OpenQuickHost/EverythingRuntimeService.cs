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
            var needsDbRebuild = EnsureOptimizedConfig(HostAssets.EverythingRuntimeConfigPath);

            if (needsDbRebuild)
            {
                HostAssets.AppendLog("Everything config updated, purging database and restarting runtime to apply new exclusions...");
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
            const string defaultExcludes = @"exclude_folders=""C:\Windows\WinSxS"";""*\.git"";""*\.vs"";""*\.vscode"";""*\.idea"";""*\.vs\"";""*node_modules"";""*\.nuget"";""*\.gradle"";""*\.cargo"";""*\.pub-cache"";""*\.cocoapods"";""*\.elm-stuff"";""*vendor\bundle"";""*\.hackage"";""*\.stack-work"";""*\.cargo\registry"";""*__pycache__"";""*\.pytest_cache"";""*\.mypy_cache"";""*\.tox"";""*build\android"";""*build\ios"";""*Pods"";""*DerivedData"";""*~\$*"";""*\.tmp\"";""*\.cache\"";""*AppData\Local\Temp"";""*\node_modules\.*"";""*\.next"";""*\.nuxt"";""*\.output"";""*\.svelte-kit"";""dist\android"";""dist\ios"";""*\.parcel-cache"";""*\.turbo"";""*\.vite\cache"";""*\.eslintcache"";""*\.sass-cache"";""*\.webpack\cache"";""*bower_components"";""*jspm_packages"";""*jspm\"";""*\.yarn\cache"";""*\.pnpm-store"";""*\.bun\cache"";""*\.cache\packages"";""*\.local\share\npm"";""*AppData\Local\npm"";""*AppData\Roaming\npm-cache"";""*\.sdkman\candidates"";""*\.rbenv"";""*\.nvm"";""*\.deno"";""*\.dartTool"";""*\.pub-cache\hosted"";""*\.pub-cache\resolved"";""*packages\terraform-provider"";""*\.terraform\providers"";""*Go\pkg\mod"";""*pkg\mod"";""*vendor\github.com"";""*vendor\golang.org"";""*vendor\gopkg.in"";""*\.gopath\src"";""*vendor\bundle\ruby"";""*vendor\cache"";""*vendor\doc"";""*vendor\paths.rb"";""*Library\Caches\com.apple"";""*Library\Developer"";""*Library\Android\sdk\build-tools"";""*Library\Android\sdk\platform-tools"";""*SDK"";""*build\tools"";""*cmake-build"";""*cmake\"";""*cmake\Debug"";""*cmake\Release"";""*CMakeFiles"";""*CMakeScripts"";""*cmake_install.cmake"";""*Makefile"";""*CMakeCache.txt"";""*.VC.db"";""*\.obj"";""*\.o"";""*\.a"";""*\.lib"";""*\.so"";""*\.dylib"";""*\.dll"";""*\.pdb"";""*\.ilk"";""*\.exp"";""*\.res"";""*\.aps"";""*\.bsc"";""*\.sdf"";""*\.opensdf"";""*\.suo"";""*\.user"";""*Debug\"";""*Release\"";""*RelWithDebInfo\"";""*MinSizeRel\"";""*x64\"";""*x86\"";""*ARM64\"";""*Win32\"";""bin\obj"";""obj\bin"";""obj\x64"";""obj\ARM64"";""*Intermediate\"";""*Generated Files\"";""*ipch\"";""*\.tlog"";""*\.lastbuildstate"";""*\$Recycle.Bin"";""*\System Volume Information""";
            var content = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;

            var currentVersion = 0;
            var versionMatch = System.Text.RegularExpressions.Regex.Match(content, @"^exclude_schema_version=(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (versionMatch.Success)
            {
                int.TryParse(versionMatch.Groups[1].Value, out currentVersion);
            }

            var needsUpdate = false;

            // Check if schema version needs update (triggers DB rebuild)
            if (currentVersion < ConfigSchemaVersion)
            {
                needsUpdate = true;
                content = System.Text.RegularExpressions.Regex.Replace(
                    content,
                    @"(?m)^exclude_schema_version=.*$",
                    $"exclude_schema_version={ConfigSchemaVersion}");
                if (!System.Text.RegularExpressions.Regex.IsMatch(content, @"^exclude_schema_version=", System.Text.RegularExpressions.RegexOptions.Multiline))
                {
                    content = $"exclude_schema_version={ConfigSchemaVersion}\r\n" + content;
                }
            }

            // Enable exclude list if not already enabled
            if (!content.Contains("exclude_list_enabled=1"))
            {
                content = System.Text.RegularExpressions.Regex.Replace(
                    content,
                    @"(?m)^exclude_list_enabled=.*$",
                    "exclude_list_enabled=1");
                if (!content.Contains("exclude_list_enabled=1"))
                {
                    content = "exclude_list_enabled=1\r\n" + content;
                }
                needsUpdate = true;
            }

            // Always update exclude_folders to the latest comprehensive list
            if (!System.Text.RegularExpressions.Regex.IsMatch(content, @"(?m)^exclude_folders=""C:\\Windows\\WinSxS"""))
            {
                content = System.Text.RegularExpressions.Regex.Replace(
                    content,
                    @"(?m)^exclude_folders=.*$",
                    defaultExcludes);
                if (!System.Text.RegularExpressions.Regex.IsMatch(content, @"(?m)^exclude_folders=""C:\\Windows\\WinSxS"""))
                {
                    content = defaultExcludes + "\r\n" + content;
                }
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory);
                File.WriteAllText(configPath, content, System.Text.Encoding.UTF8);
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
