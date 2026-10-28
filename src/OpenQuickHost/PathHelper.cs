using System.IO;

namespace OpenQuickHost;

/// <summary>
/// 全局路径解析与规范化工具类
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// 解析相对路径、特殊别名（如 Desktop/、Documents/）以及环境变量
    /// </summary>
    public static string ResolveFsPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        path = path.Trim();

        // 展开环境变量如 %APPDATA%
        if (path.Contains('%'))
        {
            path = Environment.ExpandEnvironmentVariables(path);
        }

        if (string.Equals(path, "Desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        if (path.StartsWith("Desktop\\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Desktop/", StringComparison.OrdinalIgnoreCase))
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            return Path.Combine(desktopPath, path[8..]);
        }

        if (string.Equals(path, "Documents", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        if (path.StartsWith("Documents\\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Documents/", StringComparison.OrdinalIgnoreCase))
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docsPath, path[10..]);
        }

        if (string.Equals(path, "Downloads", StringComparison.OrdinalIgnoreCase))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "Downloads");
        }

        if (path.StartsWith("Downloads\\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Downloads/", StringComparison.OrdinalIgnoreCase))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "Downloads", path[10..]);
        }

        return path;
    }
}
