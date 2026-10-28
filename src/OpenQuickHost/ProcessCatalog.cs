using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace OpenQuickHost;

/// <summary>
/// Shared process catalog used by the blacklist picker and Yanyu.
/// Uses window-owning processes, ProcessHelper's access-tolerant executable lookup,
/// and the same application icon providers as the existing blacklist UI.
/// </summary>
public static class ProcessCatalog
{
    private static readonly Dictionary<string, ProcessItem> IconCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static List<ProcessItem> GetRunningProcesses(string? preferredName = null)
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return []; }

        try
        {
            return processes
                .Where(static p =>
                {
                    try { return p.MainWindowHandle != IntPtr.Zero; }
                    catch { return false; }
                })
                .GroupBy(static p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    string? path = null;
                    foreach (var process in group)
                    {
                        path = ProcessHelper.GetProcessExecutablePath(process);
                        if (!string.IsNullOrWhiteSpace(path)) break;
                    }
                    return GetProcess(group.Key, path);
                })
                .OrderByDescending(item => item.ProcessName.Equals(
                    preferredName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(static item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public static ProcessItem GetProcess(string processName, string? executablePath = null)
    {
        var name = ProcessHelper.NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(name))
            return new ProcessItem { ProcessName = string.Empty };
        lock (IconCache)
        {
            if (IconCache.TryGetValue(name, out var cached))
                return cached;
        }

        var path = executablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            try
            {
                var known = AppSettingsStore.Load().ProcessExecutablePaths;
                if (known.TryGetValue(name, out var previous) && File.Exists(previous))
                    path = previous;
            }
            catch { /* Optional cache only. */ }
        }
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        path = ProcessHelper.GetProcessExecutablePath(process);
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) break;
                    }
                }
            }
            catch { /* Process may be protected or have exited. */ }
        }

        ImageSource? icon = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { icon = NativeFileIconService.GetIcon(path, isFolder: false); }
            catch { /* Unavailable executable icon. */ }
        }
        icon ??= FallbackIconResolver.GetFallbackIcon(name);
        var result = new ProcessItem { ProcessName = name, ExecutablePath = path, Icon = icon };
        lock (IconCache) IconCache[name] = result;
        return result;
    }

    public static IReadOnlyList<ProcessItem> ForBoundProcesses(string? names) =>
        (names ?? string.Empty)
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ProcessHelper.NormalizeProcessName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => GetProcess(name))
            .ToArray();
}
