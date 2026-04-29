using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OpenQuickHost;

public static class AppVersionInfo
{
    public static string Version
    {
        get
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    var fileVersion = FileVersionInfo.GetVersionInfo(processPath);
                    if (!string.IsNullOrWhiteSpace(fileVersion.ProductVersion))
                    {
                        return fileVersion.ProductVersion!;
                    }

                    if (!string.IsNullOrWhiteSpace(fileVersion.FileVersion))
                    {
                        return fileVersion.FileVersion!;
                    }
                }
            }
            catch
            {
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
    }

    public static string BuildStamp
    {
        get
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    return File.GetLastWriteTime(processPath).ToString("yyyy-MM-dd HH:mm:ss");
                }
            }
            catch
            {
            }

            return "unknown";
        }
    }

    public static string DisplayText => $"燕子 · v{Version} · build {BuildStamp}";
}
