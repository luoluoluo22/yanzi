using System.IO;

namespace OpenQuickHost;

public static class DesktopShortcutService
{
    public static string CreateShortcut(string name, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("当前命令没有可创建快捷方式的目标。");
        }

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktopPath);
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "快捷方式";
        }

        return IsWebOrProtocolTarget(target)
            ? CreateInternetShortcut(desktopPath, safeName, target)
            : CreateShellShortcut(desktopPath, safeName, target);
    }

    private static string CreateInternetShortcut(string desktopPath, string safeName, string target)
    {
        var shortcutPath = Path.Combine(desktopPath, $"{safeName}.url");
        var url = target.Contains("://", StringComparison.OrdinalIgnoreCase)
            ? target
            : new Uri(target).AbsoluteUri;
        var content = $"[InternetShortcut]{Environment.NewLine}URL={url}{Environment.NewLine}";
        File.WriteAllText(shortcutPath, content);
        return shortcutPath;
    }

    private static string CreateShellShortcut(string desktopPath, string safeName, string target)
    {
        var shortcutPath = Path.Combine(desktopPath, $"{safeName}.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("当前系统无法创建 Windows 快捷方式。");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法初始化快捷方式组件。");

        try
        {
            var shortcutObject = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath]) ?? throw new InvalidOperationException("快捷方式创建失败。");
            dynamic shortcut = shortcutObject;

            shortcut.TargetPath = target;
            if (File.Exists(target))
            {
                shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            }
            else if (Directory.Exists(target))
            {
                shortcut.WorkingDirectory = target;
            }

            shortcut.Save();
            return shortcutPath;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static bool IsWebOrProtocolTarget(string target)
    {
        if (target.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return target.EndsWith(":", StringComparison.OrdinalIgnoreCase);
    }
}
