using System.IO;

namespace OpenQuickHost;

public static class HostAssets
{
    private const string DevWorkspacePath = @"F:\Desktop\kaifa\OpenQuickHost";

    public static string InstallRootPath => AppDomain.CurrentDomain.BaseDirectory;

    public static string RootPath => DataRootPath;

    public static string DataRootPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost");

    public static string ExtensionsPath => ResolveDataDirectoryPath("Extensions");

    public static string DocsPath => ResolveDataDirectoryPath("docs");

    public static string SkillsPath => ResolveDataDirectoryPath("skills");

    public static string DocsReadmePath => Path.Combine(DocsPath, "README.txt");

    public static string LogsPath => ResolveDataDirectoryPath("logs");

    public static string HostLogPath => Path.Combine(LogsPath, "host.log");

    public static string DevDebugLogPath => Path.Combine(LogsPath, "dev-debug.log");

    public static string RecentCommandsPath => ResolveDataFilePath("recent-commands.txt");

    public static string MarketplacePath => ResolveDataFilePath("marketplace.txt");

    public static string LogoPath => Path.Combine(InstallRootPath, "logo.png");

    public static string WebDavSyncStatePath => ResolveDataFilePath("webdav-sync-state.json");

    public static string SearchMemoryPath => ResolveDataFilePath("search-memory.json");

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

    public static string ResolveDataFilePath(string fileName)
    {
        Directory.CreateDirectory(DataRootPath);
        MigrateLegacyFile(fileName);
        return Path.Combine(DataRootPath, fileName);
    }

    public static string ResolveDataDirectoryPath(string directoryName)
    {
        Directory.CreateDirectory(DataRootPath);
        MigrateLegacyDirectory(directoryName);
        return Path.Combine(DataRootPath, directoryName);
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

    private static void MigrateLegacyFile(string fileName)
    {
        var legacyPath = Path.Combine(InstallRootPath, fileName);
        var targetPath = Path.Combine(DataRootPath, fileName);
        if (!File.Exists(legacyPath) || File.Exists(targetPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(legacyPath, targetPath, overwrite: false);
        }
        catch
        {
            // Ignore migration failures and continue using the new location.
        }
    }

    private static void MigrateLegacyDirectory(string directoryName)
    {
        var legacyPath = Path.Combine(InstallRootPath, directoryName);
        var targetPath = Path.Combine(DataRootPath, directoryName);
        if (!Directory.Exists(legacyPath) || Directory.Exists(targetPath))
        {
            return;
        }

        try
        {
            CopyDirectory(legacyPath, targetPath);
        }
        catch
        {
            // Ignore migration failures and continue using the new location.
        }
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        foreach (var directoryPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var destinationPath = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(filePath, destinationPath, overwrite: false);
        }
    }
}
