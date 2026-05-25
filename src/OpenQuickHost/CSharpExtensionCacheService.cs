using System.IO;
using System.Threading;

namespace OpenQuickHost;

public static class CSharpExtensionCacheService
{
    private const string CacheDirectoryName = ".yanzi-csharp-cache";
    private const int MaxBuildsPerExtension = 3;
    private const long MaxTotalCacheBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan MinimumCleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly object CleanupGate = new();
    private static DateTimeOffset _lastCleanup = DateTimeOffset.MinValue;
    private static int _cleanupRunning;

    public static string GetBuildRoot(string extensionDirectoryPath, string sourceHash)
    {
        return Path.Combine(extensionDirectoryPath, CacheDirectoryName, sourceHash);
    }

    public static void TouchBuildRoot(string buildRoot)
    {
        try
        {
            if (!Directory.Exists(buildRoot))
            {
                return;
            }

            Directory.SetLastWriteTimeUtc(buildRoot, DateTime.UtcNow);
        }
        catch
        {
            // Cache metadata should never block extension execution.
        }
    }

    public static void QueueCleanup(string? activeBuildRoot)
    {
        if (Interlocked.CompareExchange(ref _cleanupRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                if (!ShouldRunCleanup())
                {
                    return;
                }

                Cleanup(activeBuildRoot);
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
            }
        });
    }

    private static bool ShouldRunCleanup()
    {
        lock (CleanupGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastCleanup < MinimumCleanupInterval)
            {
                return false;
            }

            _lastCleanup = now;
            return true;
        }
    }

    private static void Cleanup(string? activeBuildRoot)
    {
        var activePath = NormalizePath(activeBuildRoot);
        var deletedCount = 0;
        long deletedBytes = 0;

        foreach (var cacheRoot in EnumerateExtensionCacheRoots())
        {
            var entries = EnumerateCacheEntries(cacheRoot)
                .OrderByDescending(static entry => entry.LastUsedUtc)
                .ToArray();

            foreach (var entry in entries.Skip(MaxBuildsPerExtension))
            {
                if (IsActive(entry.Path, activePath))
                {
                    continue;
                }

                if (TryDeleteCacheEntry(entry, out var bytes))
                {
                    deletedCount++;
                    deletedBytes += bytes;
                }
            }
        }

        var remainingEntries = EnumerateExtensionCacheRoots()
            .SelectMany(static root => EnumerateCacheEntries(root))
            .OrderBy(static entry => entry.LastUsedUtc)
            .ToArray();
        var totalBytes = remainingEntries.Sum(static entry => entry.SizeBytes);

        foreach (var entry in remainingEntries)
        {
            if (totalBytes <= MaxTotalCacheBytes)
            {
                break;
            }

            if (IsActive(entry.Path, activePath))
            {
                continue;
            }

            if (TryDeleteCacheEntry(entry, out var bytes))
            {
                deletedCount++;
                deletedBytes += bytes;
                totalBytes -= bytes;
            }
        }

        if (deletedCount > 0)
        {
            HostAssets.AppendLog(
                $"CSharp cache cleanup: deleted={deletedCount}, bytes={deletedBytes}, maxPerExtension={MaxBuildsPerExtension}, maxTotalBytes={MaxTotalCacheBytes}");
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateExtensionCacheRoots()
    {
        if (!Directory.Exists(HostAssets.ExtensionsPath))
        {
            yield break;
        }

        foreach (var extensionDirectory in Directory.EnumerateDirectories(HostAssets.ExtensionsPath))
        {
            var cacheRoot = Path.Combine(extensionDirectory, CacheDirectoryName);
            if (Directory.Exists(cacheRoot))
            {
                yield return new DirectoryInfo(cacheRoot);
            }
        }
    }

    private static IEnumerable<CacheEntry> EnumerateCacheEntries(DirectoryInfo cacheRoot)
    {
        DirectoryInfo[] directories;
        try
        {
            directories = cacheRoot.GetDirectories();
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            var path = NormalizePath(directory.FullName);
            if (path == null || !IsSafeCacheEntryPath(path))
            {
                continue;
            }

            yield return new CacheEntry(
                path,
                GetDirectorySize(directory),
                GetLastUsedUtc(directory));
        }
    }

    private static bool TryDeleteCacheEntry(CacheEntry entry, out long bytes)
    {
        bytes = entry.SizeBytes;
        try
        {
            if (!IsSafeCacheEntryPath(entry.Path))
            {
                return false;
            }

            Directory.Delete(entry.Path, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"CSharp cache cleanup skipped: path={entry.Path}, error={ex.Message}");
            return false;
        }
    }

    private static long GetDirectorySize(DirectoryInfo directory)
    {
        try
        {
            return directory
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(static file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime GetLastUsedUtc(DirectoryInfo directory)
    {
        try
        {
            return directory.LastWriteTimeUtc;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool IsActive(string path, string? activePath)
    {
        return activePath != null &&
               string.Equals(path, activePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeCacheEntryPath(string path)
    {
        var extensionsRoot = NormalizePath(HostAssets.ExtensionsPath);
        if (extensionsRoot == null ||
            !path.StartsWith(extensionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parent = Directory.GetParent(path);
        return parent != null &&
               string.Equals(parent.Name, CacheDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private sealed record CacheEntry(string Path, long SizeBytes, DateTime LastUsedUtc);
}
