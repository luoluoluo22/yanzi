using Microsoft.Win32;

namespace OpenQuickHost;

public static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Yanzi";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(ValueName, BuildCommandLine());
            return;
        }

        if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildCommandLine()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("无法确定当前程序路径，不能设置开机自启。");
        }

        return $"\"{processPath}\" --tray";
    }
}
