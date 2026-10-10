using Microsoft.Win32;
using System.IO;

namespace OpenQuickHost;

/// <summary>
/// Only the installed, stable Shell may own startup registration.
/// Development snapshots and the Runtime process must never replace the stable
/// launch path. The Shell starts its protected background Runtime on demand.
/// </summary>
public static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string InstalledShell => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yanzi", "current", "Yanzi.exe"));

    private static bool CanRegister()
    {
        var processPath = Environment.ProcessPath;
        return !HostRuntimeProfile.IsDevelopment && !HostRuntimeProfile.IsRuntime
            && HostRuntimeProfile.RuntimeDataRoot == null
            && !string.IsNullOrWhiteSpace(processPath)
            && Path.GetFullPath(processPath).Equals(InstalledShell, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEnabled()
    {
        if (!CanRegister()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue("Yanzi") as string;
        return string.Equals(value, BuildCommandLine(), StringComparison.OrdinalIgnoreCase);
    }

    public static void Apply(bool enabled)
    {
        if (!CanRegister()) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue("Yanzi", BuildCommandLine());
        else if (key.GetValue("Yanzi") != null)
            key.DeleteValue("Yanzi", throwOnMissingValue: false);

        // Legacy installers created a second startup entry that can pin an old
        // version of Runtime indefinitely. On the official migration, use just
        // the stable Shell entry; it restores Runtime through EnsureAvailableAsync.
        if (key.GetValue("Yanzi.Runtime") != null)
            key.DeleteValue("Yanzi.Runtime", throwOnMissingValue: false);
    }

    private static string BuildCommandLine() => $"\"{InstalledShell}\" --tray";
}
