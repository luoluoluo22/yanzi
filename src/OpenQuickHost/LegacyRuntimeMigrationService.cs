using System.Diagnostics;
using System.IO;

namespace OpenQuickHost;

/// <summary>
/// One-time migration from the split Shell/Runtime releases.
/// Stops only Yanzi-owned Runtime snapshots in the current user's session;
/// never deletes user settings, cached credentials or extension data.
/// </summary>
public static class LegacyRuntimeMigrationService
{
    public static bool IsManagedRuntimeExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return false;
        try
        {
            var path = Path.GetFullPath(executable);
            if (!Path.GetFileName(path).Equals("Yanzi.Runtime.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            var root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YanziRuntime"));
            return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsOfficialDistribution()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)) return false;
        var official = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Yanzi", "current", "Yanzi.exe");
        var path = Path.GetFullPath(processPath);
        if (!Path.GetFileName(path).Equals("Yanzi.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Equals(Path.GetFullPath(official), StringComparison.OrdinalIgnoreCase)) return true;
        var directory = Path.GetDirectoryName(path);
        return directory != null && (File.Exists(Path.Combine(directory, ".portable")) ||
            File.Exists(Path.Combine(Directory.GetParent(directory)?.FullName ?? directory, ".portable")));
    }

    public static int StopManagedProcesses()
    {
        if (HostRuntimeProfile.IsDevelopment || HostRuntimeProfile.IsRuntime || !IsOfficialDistribution())
            return 0;
        int stopped = 0;
        int failed = 0;
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("Yanzi.Runtime"))
        {
            using (process)
            {
                try
                {
                    if (process.Id == current.Id || process.SessionId != current.SessionId ||
                        !IsManagedRuntimeExecutable(process.MainModule?.FileName)) continue;

                    // Request graceful close first, then release old hooks/ports before
                    // the new in-process background services start.
                    try { process.CloseMainWindow(); } catch (InvalidOperationException) { }
                    if (!process.WaitForExit(1200))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                    if (process.HasExited) stopped++;
                    else failed++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or
                    System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    failed++;
                    HostAssets.AppendLog("Legacy Runtime migration process inspection failed: " +
                        ex.GetType().Name);
                }
            }
        }
        HostAssets.AppendLog($"Legacy Runtime migration: stopped={stopped}, failed={failed}");
        if (failed != 0)
            throw new InvalidOperationException("Legacy Yanzi.Runtime processes could not be stopped. Restart Yanzi after closing the old Runtime.");
        return stopped;
    }
}
