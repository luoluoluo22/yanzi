using System.IO;

namespace OpenQuickHost;

/// <summary>
/// 统一的文件安全读写封装。
/// - 配置/索引/状态类持久化一律走 AtomicWrite*：先写临时文件再替换，进程崩溃/断电不会留下半截文件；
/// - 日志类追加走 TryAppend*：文件被日志查看器/杀软占用时静默放弃，绝不打断调用方主流程；
/// - 读取走 TryRead*：失败返回 null 并回调异常，由调用方决定兜底策略（如保留坏文件）。
/// </summary>
public static class SafeFile
{
    /// <summary>原子写文本：先写临时文件再替换。</summary>
    public static void AtomicWriteText(string path, string content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        ReplaceTemp(tempPath, path);
    }

    /// <summary>原子写字节：先写临时文件再替换。</summary>
    public static void AtomicWriteBytes(string path, byte[] content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, content);
        ReplaceTemp(tempPath, path);
    }

    private static void ReplaceTemp(string tempPath, string path)
    {
        try
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        catch (IOException)
        {
            // Replace 在目标不存在（首次写入）等场景会抛 IOException，退回移动覆盖
            File.Move(tempPath, path, overwrite: true);
        }
    }

    /// <summary>安全追加：被占用时静默放弃并返回 false。</summary>
    public static bool TryAppendAllText(string path, string content)
    {
        try
        {
            File.AppendAllText(path, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>安全读取全部文本：失败返回 null 并回调异常。</summary>
    public static string? TryReadAllText(string path, Action<Exception>? onError = null)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return null;
        }
    }
}
