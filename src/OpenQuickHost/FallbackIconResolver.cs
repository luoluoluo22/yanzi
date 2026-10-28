using System;
using System.IO;
using Microsoft.Win32;
using System.Windows.Media;

namespace OpenQuickHost
{
    public static class FallbackIconResolver
    {
        public static string? TryGetExecutablePath(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;

            if (processName.Equals("desktop", StringComparison.OrdinalIgnoreCase))
            {
                var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                if (File.Exists(explorerPath)) return explorerPath;
            }

            string exeName = processName;
            if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exeName += ".exe";
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                if (key != null)
                {
                    var path = key.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
                if (key != null)
                {
                    var path = key.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }

            return null;
        }

        public static ImageSource? GetFallbackIcon(string processName)
        {
            var path = TryGetExecutablePath(processName);
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    return NativeFileIconService.GetIcon(path, false);
                }
                catch { }
            }
            return null;
        }
    }
}
