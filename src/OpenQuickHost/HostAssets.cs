using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace OpenQuickHost;

public static class HostAssets
{
    private const string DevWorkspacePath = @"F:\Desktop\kaifa\OpenQuickHost";
    private const long MaxLogFileBytes = 8L * 1024 * 1024;
    private const int MaxQueuedLogLines = 4096;
    private const int MaxLogFlushLines = 256;

    private static readonly ConcurrentQueue<string> PendingLogLines = new();
    private static readonly AutoResetEvent LogFlushSignal = new(false);
    private static int _logWorkerStarted;
    private static int _queuedLogLineCount;

    public static string InstallRootPath => AppDomain.CurrentDomain.BaseDirectory;

    public static string RootPath => DataRootPath;

    public static string DataRootPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost");

    public static string ExtensionsPath => ResolveDataDirectoryPath("Extensions");

    public static string ExtensionRecycleBinPath => ResolveDataDirectoryPath("ExtensionRecycleBin");

    public static string ExtensionRecycleBinIndexPath => Path.Combine(ExtensionRecycleBinPath, "index.json");

    public static string DocsPath => ResolveDataDirectoryPath("docs");

    public static string SkillsPath => ResolveDataDirectoryPath("skills");

    public static string DocsReadmePath => Path.Combine(DocsPath, "README.txt");

    public static string LogsPath => ResolveDataDirectoryPath("logs");

    public static string HostLogPath => Path.Combine(LogsPath, "host.log");

    public static string DevDebugLogPath => Path.Combine(LogsPath, "dev-debug.log");

    public static string CloudSyncDiagnosticsLogPath => Path.Combine(LogsPath, "cloud-sync-diagnostics.log");

    public static string RecentCommandsPath => ResolveDataFilePath("recent-commands.txt");

    public static string MarketplacePath => ResolveDataFilePath("marketplace.txt");

    public static string MobileInboxPath => ResolveDataFilePath("mobile-inbox.jsonl");

    public static string LogoPath => Path.Combine(InstallRootPath, "logo.png");

    public static string WebDavSyncStatePath => ResolveDataFilePath("webdav-sync-state.json");

    public static string SearchMemoryPath => ResolveDataFilePath("search-memory.json");

    public static string EverythingRuntimeDataPath => ResolveDataDirectoryPath("EverythingRuntime");

    public static string EverythingRuntimeConfigPath => Path.Combine(EverythingRuntimeDataPath, "Everything-Yanzi.ini");

    public static string EverythingRuntimeDatabasePath => Path.Combine(EverythingRuntimeDataPath, "Everything-Yanzi.db");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ExtensionsPath);
        Directory.CreateDirectory(ExtensionRecycleBinPath);
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
        try
        {
            EnsureCreated();
            File.AppendAllText(
                RecentCommandsPath,
                $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}");
        }
        catch
        {
            // 日志文件被杀软/日志查看器占用时静默放弃，绝不打断命令启动主流程
        }
    }

    public static void AppendLog(string message)
    {
        var line = $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        if (Interlocked.Increment(ref _queuedLogLineCount) > MaxQueuedLogLines)
        {
            Interlocked.Decrement(ref _queuedLogLineCount);
            return;
        }

        PendingLogLines.Enqueue(line);
        EnsureLogWorkerStarted();
        LogFlushSignal.Set();
    }

    public static void AppendDevLog(string message)
    {
        try
        {
            if (!Directory.Exists(DevWorkspacePath))
            {
                return;
            }

            EnsureCreated();
            RotateFileIfTooLarge(DevDebugLogPath, MaxLogFileBytes);
            File.AppendAllText(
                DevDebugLogPath,
                $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }
        catch
        {
            // 多线程并发追加/轮转竞争或文件被占用时静默放弃；
            // 这里抛出的异常会顶替调用方的业务异常（如在 catch 路径中调用）
        }
    }

    public static void AppendCloudSyncDiagnosticLog(string message)
    {
        try
        {
            EnsureCreated();
            RotateFileIfTooLarge(CloudSyncDiagnosticsLogPath, MaxLogFileBytes);
            File.AppendAllText(
                CloudSyncDiagnosticsLogPath,
                $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }
        catch
        {
            // 同上：诊断日志自身失败绝不能打断云同步重试流程或顶替真实异常
        }
        AppendLog($"[CloudSyncDiag] {message}");
    }

    public static IReadOnlyList<string> ReadHostLogTailLines(int maxBytes, int maxLines)
    {
        return ReadTailLines(HostLogPath, maxBytes, maxLines);
    }

    private static IReadOnlyList<string> ReadTailLines(string path, int maxBytes, int maxLines)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= 0)
        {
            return [];
        }

        var bytesToRead = (int)Math.Min(stream.Length, maxBytes);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        if (bytesToRead < stream.Length)
        {
            _ = reader.ReadLine();
        }

        var content = reader.ReadToEnd();
        return content
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(maxLines)
            .ToArray();
    }

    private static void RotateFileIfTooLarge(string path, long maxBytes)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length <= maxBytes)
            {
                return;
            }

            var archivePath = path + ".1";
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            File.Move(path, archivePath);
        }
        catch
        {
            // Logging must never block normal app execution.
        }
    }

    private static void EnsureLogWorkerStarted()
    {
        if (Interlocked.Exchange(ref _logWorkerStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Factory.StartNew(
            LogWorkerLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void LogWorkerLoop()
    {
        while (true)
        {
            LogFlushSignal.WaitOne(TimeSpan.FromSeconds(1));
            FlushPendingLogLines();
        }
    }

    private static void FlushPendingLogLines()
    {
        if (PendingLogLines.IsEmpty)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(LogsPath);
            EnsureFile(
                HostLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Swallow Launcher initialized.{Environment.NewLine}");
            RotateFileIfTooLarge(HostLogPath, MaxLogFileBytes);

            var builder = new StringBuilder(capacity: 8192);
            var flushed = 0;
            while (flushed < MaxLogFlushLines && PendingLogLines.TryDequeue(out var line))
            {
                Interlocked.Decrement(ref _queuedLogLineCount);
                builder.Append(line);
                flushed++;
            }

            if (builder.Length > 0)
            {
                File.AppendAllText(HostLogPath, builder.ToString());
            }

            if (!PendingLogLines.IsEmpty)
            {
                LogFlushSignal.Set();
            }
        }
        catch
        {
            // Logging must never block normal app execution.
        }
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
