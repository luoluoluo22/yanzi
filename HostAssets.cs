using System.IO;

namespace OpenQuickHost;

public static class HostAssets
{
    private const string DevWorkspacePath = @"F:\Desktop\kaifa\OpenQuickHost";

    public static string RootPath => AppDomain.CurrentDomain.BaseDirectory;

    public static string ExtensionsPath => Path.Combine(RootPath, "Extensions");

    public static string DocsPath => Path.Combine(RootPath, "docs");

    public static string SkillsPath => Path.Combine(RootPath, "skills");

    public static string DocsReadmePath => Path.Combine(DocsPath, "README.txt");

    public static string LogsPath => Path.Combine(RootPath, "logs");

    public static string HostLogPath => Path.Combine(LogsPath, "host.log");

    public static string DevDebugLogPath => Path.Combine(LogsPath, "dev-debug.log");

    public static string RecentCommandsPath => Path.Combine(RootPath, "recent-commands.txt");

    public static string MarketplacePath => Path.Combine(RootPath, "marketplace.txt");

    public static string LogoPath => Path.Combine(RootPath, "logo.png");

    public static string WebDavSyncStatePath => Path.Combine(RootPath, "webdav-sync-state.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ExtensionsPath);
        Directory.CreateDirectory(DocsPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(SkillsPath);

        EnsureFile(
            DocsReadmePath,
            """
            燕子 (Swallow Launcher) 文档中心

            当前宿主已支持：
            - 本地扩展目录扫描
            - 云端扩展同步
            - 扩展包上传与下载

            本地扩展目录：
            Extensions
            """);
        EnsureFile(
            MarketplacePath,
            """
            燕子 插件市场占位页

            当前阶段：
            - 云端同步后端已部署
            - 扩展元数据和扩展包已可上传
            - 下一步可以把这里接成真实市场页
            """);
        EnsureFile(
            HostLogPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Swallow Launcher initialized.{Environment.NewLine}");
        EnsureFile(
            RecentCommandsPath,
            "最近执行命令会追加在这里。");
    }

    public static void AppendRecent(string title)
    {
        EnsureCreated();
        File.AppendAllText(
            RecentCommandsPath,
            $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}");
    }

    public static void AppendLog(string message)
    {
        EnsureCreated();
        File.AppendAllText(
            HostLogPath,
            $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }

    public static void AppendDevLog(string message)
    {
        if (!Directory.Exists(DevWorkspacePath))
        {
            return;
        }

        EnsureCreated();
        File.AppendAllText(
            DevDebugLogPath,
            $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }

    private static void EnsureFile(string path, string content)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
        }
    }
}
