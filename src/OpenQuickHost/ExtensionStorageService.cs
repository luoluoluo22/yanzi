using System.Collections.Concurrent;
using System.IO;
using System.Text;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class ExtensionStorageService
{
    private static readonly TimeSpan BackgroundCloudTimeout = TimeSpan.FromSeconds(8);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CloudWriteLocks = new(StringComparer.OrdinalIgnoreCase);

    public static string StorageRootPath => HostAssets.ResolveDataDirectoryPath("ExtensionStorage");

    public static string GetExtensionStorageDirectoryPath(string extensionId)
    {
        var normalizedExtensionId = NormalizeExtensionId(extensionId);
        var directoryPath = Path.Combine(StorageRootPath, normalizedExtensionId);
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    public static async Task<ExtensionStorageReadResult> ReadTextAsync(
        string extensionId,
        string key,
        string? scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = ParseScope(scope);
        var normalizedKey = NormalizeStorageKey(key);
        var localPath = ResolveLocalFilePath(extensionId, normalizedKey);

        if (normalizedScope == ExtensionStorageScope.Both)
        {
            QueueCloudReadRefresh(extensionId, normalizedKey, localPath);
            if (File.Exists(localPath))
            {
                var localValue = await File.ReadAllTextAsync(localPath, cancellationToken);
                return new ExtensionStorageReadResult(true, localValue, "local", localPath);
            }

            var cloudValue = await TryReadCloudTextAsync(extensionId, normalizedKey, cancellationToken);
            if (cloudValue != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, cloudValue, Encoding.UTF8, cancellationToken);
                return new ExtensionStorageReadResult(true, cloudValue, "cloud", localPath);
            }

            return new ExtensionStorageReadResult(false, null, "none", localPath);
        }

        if (normalizedScope is ExtensionStorageScope.Cloud or ExtensionStorageScope.Both)
        {
            var cloudValue = await TryReadCloudTextAsync(extensionId, normalizedKey, cancellationToken);
            if (cloudValue != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, cloudValue, Encoding.UTF8, cancellationToken);
                return new ExtensionStorageReadResult(true, cloudValue, "cloud", localPath);
            }
        }

        if (File.Exists(localPath))
        {
            var localValue = await File.ReadAllTextAsync(localPath, cancellationToken);
            return new ExtensionStorageReadResult(true, localValue, "local", localPath);
        }

        return new ExtensionStorageReadResult(false, null, "none", localPath);
    }

    public static async Task<ExtensionStorageWriteResult> WriteTextAsync(
        string extensionId,
        string key,
        string content,
        string? scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedScope = ParseScope(scope);
        var normalizedKey = NormalizeStorageKey(key);
        var localPath = ResolveLocalFilePath(extensionId, normalizedKey);

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllTextAsync(localPath, content ?? string.Empty, Encoding.UTF8, cancellationToken);

        var cloudSaved = false;
        string? cloudMessage = null;
        if (normalizedScope == ExtensionStorageScope.Both)
        {
            QueueCloudWrite(extensionId, normalizedKey, content ?? string.Empty);
            cloudMessage = "cloud write queued";
        }
        else if (normalizedScope == ExtensionStorageScope.Cloud)
        {
            try
            {
                await WriteCloudTextAsync(extensionId, normalizedKey, content ?? string.Empty, cancellationToken);
                cloudSaved = true;
            }
            catch (Exception ex)
            {
                cloudMessage = ex.Message;
                if (normalizedScope == ExtensionStorageScope.Cloud)
                {
                    throw;
                }
            }
        }

        return new ExtensionStorageWriteResult(localPath, cloudSaved, normalizedScope.ToString().ToLowerInvariant(), cloudMessage);
    }

    private static void QueueCloudReadRefresh(string extensionId, string key, string localPath)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(BackgroundCloudTimeout);
                var cloudValue = await TryReadCloudTextAsync(extensionId, key, cts.Token);
                if (cloudValue == null)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                await File.WriteAllTextAsync(localPath, cloudValue, Encoding.UTF8, cts.Token);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Extension storage cloud refresh skipped: id={extensionId}, key={key}, error={ex.Message}");
            }
        });
    }

    private static void QueueCloudWrite(string extensionId, string key, string content)
    {
        _ = Task.Run(async () =>
        {
            var operationKey = $"{extensionId}\0{key}";
            var writeLock = CloudWriteLocks.GetOrAdd(operationKey, static _ => new SemaphoreSlim(1, 1));
            try
            {
                await writeLock.WaitAsync();
                using var cts = new CancellationTokenSource(BackgroundCloudTimeout);
                await WriteCloudTextAsync(extensionId, key, content, cts.Token);
                HostAssets.AppendLog($"Extension storage cloud write completed: id={extensionId}, key={key}");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Extension storage cloud write skipped: id={extensionId}, key={key}, error={ex.Message}");
            }
            finally
            {
                if (writeLock.CurrentCount == 0)
                {
                    writeLock.Release();
                }
            }
        });
    }

    private static async Task<string?> TryReadCloudTextAsync(string extensionId, string key, CancellationToken cancellationToken)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            return null;
        }

        var service = new PersonalSyncService(settings);
        return await service.TryReadExtensionDataTextAsync(extensionId, key, cancellationToken);
    }

    private static async Task WriteCloudTextAsync(string extensionId, string key, string content, CancellationToken cancellationToken)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            throw new InvalidOperationException("个人同步未完整配置，无法写入云端存储。");
        }

        var service = new PersonalSyncService(settings);
        await service.WriteExtensionDataTextAsync(extensionId, key, content, cancellationToken);
    }

    public static async Task SyncLocalDirectoryToCloudAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            return;
        }

        var dir = GetExtensionStorageDirectoryPath(extensionId);
        if (!Directory.Exists(dir))
        {
            return;
        }

        var service = new PersonalSyncService(settings);
        foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
        {
            var key = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (key.StartsWith("EBWebView/", StringComparison.OrdinalIgnoreCase) || 
                key.StartsWith(@"EBWebView\", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "EBWebView", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken);
            await service.WriteExtensionDataTextAsync(extensionId, key, content, cancellationToken);
        }
    }

    private static string ResolveLocalFilePath(string extensionId, string key)
    {
        var extensionDirectory = GetExtensionStorageDirectoryPath(extensionId);
        var relativePath = key.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(extensionDirectory, relativePath));
    }

    private static string NormalizeExtensionId(string extensionId)
    {
        var normalized = (extensionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("extensionId 不能为空。");
        }

        return normalized;
    }

    private static string NormalizeStorageKey(string key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("storage key 不能为空。");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("storage key 不能包含 . 或 .. 路径段。");
        }

        return string.Join("/", segments);
    }

    private static ExtensionStorageScope ParseScope(string? scope)
    {
        return (scope ?? "local").Trim().ToLowerInvariant() switch
        {
            "cloud" => ExtensionStorageScope.Cloud,
            "both" => ExtensionStorageScope.Both,
            _ => ExtensionStorageScope.Local
        };
    }
}

public sealed record ExtensionStorageReadResult(bool Found, string? Content, string Source, string LocalPath);

public sealed record ExtensionStorageWriteResult(string LocalPath, bool CloudSaved, string Scope, string? CloudMessage);

public enum ExtensionStorageScope
{
    Local,
    Cloud,
    Both
}
