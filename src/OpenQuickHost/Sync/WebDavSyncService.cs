using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed class WebDavSyncService
{
    private const string RemoteIndexPath = "index.json";
    private readonly AppSettings _settings;
    private readonly SavedWebDavCredential? _credential;
    private readonly HttpClient _httpClient;

    public WebDavSyncService(AppSettings settings)
    {
        _settings = settings;
        _credential = WebDavCredentialStore.Load();
        if (string.IsNullOrWhiteSpace(_settings.WebDavServerUrl))
        {
            throw new InvalidOperationException("WebDAV 服务地址未配置。");
        }

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(EnsureTrailingSlash(_settings.WebDavServerUrl), UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        if (_credential != null && !string.IsNullOrWhiteSpace(_credential.Password))
        {
            var raw = $"{_settings.WebDavUsername}:{_credential.Password}";
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
    }

    public bool IsConfigured =>
        _settings.EnableWebDavSync &&
        Uri.TryCreate(_settings.WebDavServerUrl, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(_settings.WebDavUsername) &&
        !string.IsNullOrWhiteSpace(_credential?.Password);

    public string SyncRootDisplay => $"{_settings.WebDavServerUrl.TrimEnd('/')}{NormalizeRootPath(_settings.WebDavRootPath)}";

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureCollectionTreeAsync(cancellationToken, NormalizeRootPath(_settings.WebDavRootPath).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));
        await EnsureCollectionAsync("packages", cancellationToken);
    }

    public async Task<WebDavSyncResult> SyncExtensionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await ProbeAsync(cancellationToken);

        var remoteIndex = await LoadRemoteIndexAsync(cancellationToken);
        var localState = LoadLocalIndex();
        var snapshot = BuildLocalSnapshot(localState);
        HostAssets.AppendLog(
            $"WebDAV sync snapshot: remote={remoteIndex.Items.Count}, local={snapshot.Items.Count}, " +
            $"localState={localState.Items.Count}, pendingPackages={snapshot.PackageBytesByExtensionId.Count}");
        var remoteMap = remoteIndex.Items.ToDictionary(item => item.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var localMap = snapshot.Items.ToDictionary(item => item.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var mergedMap = new Dictionary<string, WebDavSyncEntry>(StringComparer.OrdinalIgnoreCase);
        var uploaded = 0;
        var pulled = 0;
        var remoteIndexChanged = false;

        foreach (var extensionId in remoteMap.Keys.Union(localMap.Keys, StringComparer.OrdinalIgnoreCase))
        {
            localMap.TryGetValue(extensionId, out var localEntry);
            remoteMap.TryGetValue(extensionId, out var remoteEntry);

            if (localEntry == null)
            {
                if (remoteEntry == null)
                {
                    continue;
                }

                LogDecision(extensionId, "remote-only", null, remoteEntry);
                try
                {
                    if (await ApplyRemoteEntryAsync(remoteEntry, cancellationToken))
                    {
                        pulled++;
                    }
                }
                catch (InvalidDataException ex)
                {
                    HostAssets.AppendLog($"WebDAV skipped invalid remote package for {remoteEntry.ExtensionId}: {ex.Message}");
                    remoteIndexChanged = true;
                    continue;
                }

                mergedMap[extensionId] = remoteEntry;
                continue;
            }

            if (remoteEntry == null)
            {
                LogDecision(extensionId, "local-only", localEntry, null);
                if (!localEntry.Deleted)
                {
                    await UploadPackageIfNeededAsync(localEntry, snapshot.PackageBytesByExtensionId, cancellationToken);
                    uploaded++;
                }

                mergedMap[extensionId] = localEntry;
                remoteIndexChanged = true;
                continue;
            }

            var winner = CompareEntries(localEntry, remoteEntry) >= 0 ? localEntry : remoteEntry;
            var loser = ReferenceEquals(winner, localEntry) ? remoteEntry : localEntry;
            LogDecision(
                extensionId,
                ReferenceEquals(winner, localEntry) ? "local-wins" : "remote-wins",
                localEntry,
                remoteEntry);

            if (ReferenceEquals(winner, localEntry))
            {
                if (!winner.Deleted &&
                    !string.Equals(remoteEntry.PackageHash, winner.PackageHash, StringComparison.OrdinalIgnoreCase))
                {
                    await UploadPackageIfNeededAsync(winner, snapshot.PackageBytesByExtensionId, cancellationToken);
                    uploaded++;
                }
                else if (winner.Deleted != loser.Deleted ||
                         !string.Equals(winner.UpdatedAtUtc, loser.UpdatedAtUtc, StringComparison.Ordinal))
                {
                    remoteIndexChanged = true;
                }
            }
            else
            {
                try
                {
                    if (await ApplyRemoteEntryAsync(winner, cancellationToken))
                    {
                        pulled++;
                    }
                }
                catch (InvalidDataException ex)
                {
                    HostAssets.AppendLog($"WebDAV ignored invalid newer remote package for {winner.ExtensionId}: {ex.Message}");
                    if (!localEntry.Deleted)
                    {
                        await UploadPackageIfNeededAsync(localEntry, snapshot.PackageBytesByExtensionId, cancellationToken);
                        mergedMap[extensionId] = localEntry;
                    }

                    remoteIndexChanged = true;
                    continue;
                }
            }

            if (!EntriesEquivalent(winner, remoteEntry))
            {
                remoteIndexChanged = true;
            }

            mergedMap[extensionId] = winner;
        }

        var mergedIndex = new WebDavSyncIndex
        {
            Items = mergedMap.Values
                .OrderBy(item => item.ExtensionId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        if (remoteIndexChanged || !IndexesEquivalent(remoteIndex, mergedIndex))
        {
            await SaveRemoteIndexAsync(mergedIndex, cancellationToken);
        }

        SaveLocalIndex(ClearLocalPendingFlags(mergedIndex));
        return new WebDavSyncResult(uploaded, pulled, SyncRootDisplay);
    }

    private static WebDavSyncIndex LoadLocalIndex()
    {
        try
        {
            if (!File.Exists(HostAssets.WebDavSyncStatePath))
            {
                return new WebDavSyncIndex();
            }

            var json = File.ReadAllText(HostAssets.WebDavSyncStatePath);
            return JsonSerializer.Deserialize<WebDavSyncIndex>(json, JsonOptions) ?? new WebDavSyncIndex();
        }
        catch
        {
            return new WebDavSyncIndex();
        }
    }

    private static void SaveLocalIndex(WebDavSyncIndex index)
    {
        var json = JsonSerializer.Serialize(index, JsonOptions);
        File.WriteAllText(HostAssets.WebDavSyncStatePath, json);
    }

    public static void MarkExtensionDeletedLocally(string extensionId, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var index = LoadLocalIndex();
        var existing = index.Items.FirstOrDefault(item =>
            item.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new WebDavSyncEntry
            {
                ExtensionId = extensionId
            };
            index.Items.Add(existing);
        }

        existing.Version = string.IsNullOrWhiteSpace(version) ? existing.Version : version!;
        existing.Deleted = true;
        existing.LocalDeletionPending = true;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        SaveLocalIndex(index);
    }

    private LocalSnapshot BuildLocalSnapshot(WebDavSyncIndex localState)
    {
        var stateMap = localState.Items.ToDictionary(item => item.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var items = new List<WebDavSyncEntry>();
        var packageBytesByExtensionId = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in LocalExtensionCatalog.LoadCommands())
        {
            if (string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) ||
                !Directory.Exists(command.ExtensionDirectoryPath))
            {
                continue;
            }

            var packageBytes = ExtensionPackageService.BuildPackage(command, command.DeclaredVersion);
            var packageHash = ComputeSha256(packageBytes);
            existingIds.Add(command.ExtensionId);
            stateMap.TryGetValue(command.ExtensionId, out var previous);
            var updatedAtUtc = previous == null
                ? GetDirectoryLastWriteUtc(command.ExtensionDirectoryPath).ToString("O")
                : !previous.Deleted && string.Equals(previous.PackageHash, packageHash, StringComparison.OrdinalIgnoreCase)
                    ? previous.UpdatedAtUtc
                    : DateTimeOffset.UtcNow.ToString("O");
            var packagePath = BuildRemotePackagePath(command.ExtensionId, packageHash);
            var entry = new WebDavSyncEntry
            {
                ExtensionId = command.ExtensionId,
                Version = command.DeclaredVersion,
                PackageHash = packageHash,
                PackagePath = packagePath,
                UpdatedAtUtc = updatedAtUtc,
                Deleted = false
            };
            items.Add(entry);
            if (previous == null ||
                previous.Deleted ||
                !string.Equals(previous.PackageHash, packageHash, StringComparison.OrdinalIgnoreCase))
            {
                packageBytesByExtensionId[command.ExtensionId] = packageBytes;
            }
        }

        foreach (var stateEntry in stateMap.Values)
        {
            if (existingIds.Contains(stateEntry.ExtensionId))
            {
                continue;
            }

            if (!stateEntry.LocalDeletionPending)
            {
                HostAssets.AppendLog(
                    $"WebDAV local missing without explicit delete: id={stateEntry.ExtensionId}, " +
                    $"stateDeleted={stateEntry.Deleted}, remote pull allowed.");
                continue;
            }

            items.Add(new WebDavSyncEntry
            {
                ExtensionId = stateEntry.ExtensionId,
                Version = stateEntry.Version,
                PackageHash = stateEntry.PackageHash,
                PackagePath = stateEntry.PackagePath,
                UpdatedAtUtc = stateEntry.Deleted ? stateEntry.UpdatedAtUtc : DateTimeOffset.UtcNow.ToString("O"),
                Deleted = true,
                LocalDeletionPending = stateEntry.LocalDeletionPending
            });
        }

        return new LocalSnapshot(items, packageBytesByExtensionId);
    }

    private static WebDavSyncIndex ClearLocalPendingFlags(WebDavSyncIndex index)
    {
        return new WebDavSyncIndex
        {
            SchemaVersion = index.SchemaVersion,
            Items = index.Items.Select(item => new WebDavSyncEntry
            {
                ExtensionId = item.ExtensionId,
                Version = item.Version,
                PackageHash = item.PackageHash,
                PackagePath = item.PackagePath,
                UpdatedAtUtc = item.UpdatedAtUtc,
                Deleted = item.Deleted,
                LocalDeletionPending = false
            }).ToList()
        };
    }

    private static void LogDecision(string extensionId, string decision, WebDavSyncEntry? local, WebDavSyncEntry? remote)
    {
        HostAssets.AppendLog(
            $"WebDAV decision: id={extensionId}, decision={decision}, " +
            $"local={FormatEntry(local)}, remote={FormatEntry(remote)}");
    }

    private static string FormatEntry(WebDavSyncEntry? entry)
    {
        if (entry == null)
        {
            return "(none)";
        }

        var hash = string.IsNullOrWhiteSpace(entry.PackageHash)
            ? "-"
            : entry.PackageHash.Length <= 12
                ? entry.PackageHash
                : entry.PackageHash[..12];
        return $"deleted={entry.Deleted},pendingDelete={entry.LocalDeletionPending},updated={entry.UpdatedAtUtc},hash={hash},path={entry.PackagePath}";
    }

    private async Task<WebDavSyncIndex> LoadRemoteIndexAsync(CancellationToken cancellationToken)
    {
        var bytes = await TryGetBytesAsync(RemoteIndexPath, cancellationToken);
        if (bytes == null || bytes.Length == 0)
        {
            return new WebDavSyncIndex();
        }

        try
        {
            return JsonSerializer.Deserialize<WebDavSyncIndex>(bytes, JsonOptions) ?? new WebDavSyncIndex();
        }
        catch
        {
            return new WebDavSyncIndex();
        }
    }

    private async Task SaveRemoteIndexAsync(WebDavSyncIndex index, CancellationToken cancellationToken)
    {
        var remoteIndex = new WebDavSyncIndex
        {
            SchemaVersion = index.SchemaVersion,
            Items = index.Items.Select(item => new WebDavSyncEntry
            {
                ExtensionId = item.ExtensionId,
                Version = item.Version,
                PackageHash = item.PackageHash,
                PackagePath = item.PackagePath,
                UpdatedAtUtc = item.UpdatedAtUtc,
                Deleted = item.Deleted
            }).ToList()
        };
        var json = JsonSerializer.Serialize(remoteIndex, JsonOptions);
        using var request = CreateRequest(HttpMethod.Put, RemoteIndexPath);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }
    }

    private async Task UploadPackageIfNeededAsync(
        WebDavSyncEntry entry,
        IReadOnlyDictionary<string, byte[]> packageBytesByExtensionId,
        CancellationToken cancellationToken)
    {
        if (!packageBytesByExtensionId.TryGetValue(entry.ExtensionId, out var bytes))
        {
            return;
        }

        await EnsureCollectionAsync($"packages/{entry.ExtensionId}", cancellationToken);
        using var request = CreateRequest(HttpMethod.Put, entry.PackagePath);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }

        HostAssets.AppendLog(
            $"WebDAV uploaded package: id={entry.ExtensionId}, path={entry.PackagePath}, bytes={bytes.Length}, hash={entry.PackageHash}");
        await VerifyUploadedPackageAsync(entry, bytes, cancellationToken);
    }

    private async Task<bool> ApplyRemoteEntryAsync(WebDavSyncEntry entry, CancellationToken cancellationToken)
    {
        var localDirectory = Path.Combine(HostAssets.ExtensionsPath, entry.ExtensionId);
        if (entry.Deleted)
        {
            if (Directory.Exists(localDirectory))
            {
                Directory.Delete(localDirectory, recursive: true);
                return true;
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.PackagePath))
        {
            return false;
        }

        var packageBytes = await GetBytesAsync(entry.PackagePath, cancellationToken);
        if (!TryValidateZipArchive(packageBytes, out var packageError))
        {
            throw new InvalidDataException(
                $"WebDAV 远端扩展包无效：{entry.PackagePath} 不是有效的 zip 文件，bytes={packageBytes.Length}，hash={ComputeSha256(packageBytes)}，head={FormatBytePrefix(packageBytes)}，detail={packageError}。可能是旧版目录同步残留或远端索引已损坏。");
        }

        var targetDirectory = Path.Combine(HostAssets.ExtensionsPath, entry.ExtensionId);
        await ReplaceDirectoryFromPackageAsync(targetDirectory, packageBytes, cancellationToken);
        return true;
    }

    private async Task VerifyUploadedPackageAsync(WebDavSyncEntry entry, byte[] expectedBytes, CancellationToken cancellationToken)
    {
        var remoteBytes = await GetBytesAsync(entry.PackagePath, cancellationToken);
        var remoteHash = ComputeSha256(remoteBytes);
        if (!TryValidateZipArchive(remoteBytes, out var zipError) ||
            !string.Equals(remoteHash, entry.PackageHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"WebDAV 上传校验失败：{entry.PackagePath}，expectedBytes={expectedBytes.Length}, remoteBytes={remoteBytes.Length}, expectedHash={entry.PackageHash}, remoteHash={remoteHash}, remoteHead={FormatBytePrefix(remoteBytes)}, zipError={zipError}。");
        }

        HostAssets.AppendLog(
            $"WebDAV verified package: id={entry.ExtensionId}, path={entry.PackagePath}, bytes={remoteBytes.Length}, hash={remoteHash}");
    }

    private static async Task ReplaceDirectoryFromPackageAsync(string targetDirectory, byte[] packageBytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(HostAssets.ExtensionsPath);
        var tempDirectory = Path.Combine(HostAssets.ExtensionsPath, $".yanzi-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            await using var stream = new MemoryStream(packageBytes, writable: false);
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(tempDirectory, overwriteFiles: true);
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            Directory.Move(tempDirectory, targetDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("WebDAV 未完整配置，请先填写地址、用户名并设置密码。");
        }
    }

    private async Task EnsureCollectionTreeAsync(CancellationToken cancellationToken, params string[] segments)
    {
        var current = string.Empty;
        foreach (var segment in segments)
        {
            current = string.IsNullOrWhiteSpace(current) ? segment : $"{current}/{segment}";
            await EnsureCollectionAsync(current, cancellationToken);
        }
    }

    private async Task EnsureCollectionAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("MKCOL"), relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.Conflict)
        {
            if (await CollectionExistsAsync(relativePath, cancellationToken))
            {
                return;
            }

            throw new InvalidOperationException($"WebDAV 目录不可用：{relativePath}，服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        await ThrowWebDavFailureAsync(request, response, cancellationToken);
    }

    private async Task<bool> CollectionExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("PROPFIND"), relativePath);
        request.Headers.Add("Depth", "0");
        request.Content = new StringContent(
            """
<?xml version="1.0" encoding="utf-8" ?>
<propfind xmlns="DAV:">
  <prop>
    <resourcetype />
  </prop>
</propfind>
""",
            Encoding.UTF8,
            "application/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.StatusCode == HttpStatusCode.MultiStatus ||
               response.StatusCode == HttpStatusCode.OK;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        return new HttpRequestMessage(method, BuildRelativeUri(relativePath));
    }

    private async Task<byte[]?> TryGetBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<byte[]> GetBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var bytes = await TryGetBytesAsync(relativePath, cancellationToken);
        if (bytes == null)
        {
            throw new FileNotFoundException($"WebDAV 文件不存在：{relativePath}", relativePath);
        }

        return bytes;
    }

    private static int CompareEntries(WebDavSyncEntry left, WebDavSyncEntry right)
    {
        var leftUpdated = ParseTimestamp(left.UpdatedAtUtc);
        var rightUpdated = ParseTimestamp(right.UpdatedAtUtc);
        var compare = leftUpdated.CompareTo(rightUpdated);
        if (compare != 0)
        {
            return compare;
        }

        if (left.Deleted != right.Deleted)
        {
            return left.Deleted ? 1 : -1;
        }

        return string.Compare(left.PackageHash, right.PackageHash, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EntriesEquivalent(WebDavSyncEntry left, WebDavSyncEntry right)
    {
        return left.ExtensionId.Equals(right.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
               left.Version.Equals(right.Version, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.PackageHash, right.PackageHash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.PackagePath, right.PackagePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.UpdatedAtUtc, right.UpdatedAtUtc, StringComparison.Ordinal) &&
               left.Deleted == right.Deleted;
    }

    private static bool IndexesEquivalent(WebDavSyncIndex left, WebDavSyncIndex right)
    {
        if (left.Items.Count != right.Items.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Items.Count; index++)
        {
            if (!EntriesEquivalent(left.Items[index], right.Items[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
    }

    private static string BuildRemotePackagePath(string extensionId, string packageHash)
    {
        return $"packages/{extensionId}/{packageHash}.zip";
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool TryValidateZipArchive(byte[] bytes, out string error)
    {
        error = string.Empty;
        if (bytes.Length < 4)
        {
            error = "文件太短。";
            return false;
        }

        var hasZipHeader = bytes[0] == 0x50 &&
                           bytes[1] == 0x4B &&
                           (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07) &&
                           (bytes[3] == 0x04 || bytes[3] == 0x06 || bytes[3] == 0x08);
        if (!hasZipHeader)
        {
            error = "缺少 zip 文件头。";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            _ = archive.Entries.Count;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string FormatBytePrefix(byte[] bytes)
    {
        return bytes.Length == 0
            ? "(empty)"
            : Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 16)));
    }

    private static DateTimeOffset GetDirectoryLastWriteUtc(string directoryPath)
    {
        var latest = Directory.GetLastWriteTimeUtc(directoryPath);
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            var fileTime = File.GetLastWriteTimeUtc(filePath);
            if (fileTime > latest)
            {
                latest = fileTime;
            }
        }

        return new DateTimeOffset(DateTime.SpecifyKind(latest, DateTimeKind.Utc));
    }

    private static async Task ThrowWebDavFailureAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // Some WebDAV servers return empty or non-text error bodies.
        }

        var requestUri = request.RequestUri?.ToString() ?? "(unknown)";
        var detail = string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : $" 响应：{TrimForMessage(body)}";
        throw new HttpRequestException(
            $"WebDAV 请求失败：{request.Method} {requestUri} -> {(int)response.StatusCode} {response.ReasonPhrase}.{detail}",
            null,
            response.StatusCode);
    }

    private static string TrimForMessage(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300] + "...";
    }

    private string BuildRelativeUri(string relativePath)
    {
        var root = NormalizeRootPath(_settings.WebDavRootPath).Trim('/');
        var suffix = NormalizeRelativePath(relativePath);
        return string.IsNullOrWhiteSpace(suffix)
            ? root + "/"
            : root + "/" + string.Join("/", suffix.Split('/').Select(Uri.EscapeDataString));
    }

    private static string NormalizeRootPath(string? rootPath)
    {
        var value = string.IsNullOrWhiteSpace(rootPath) ? "/yanzi" : rootPath.Trim();
        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        return value.TrimEnd('/');
    }

    private static string NormalizeRelativePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record LocalSnapshot(
        IReadOnlyList<WebDavSyncEntry> Items,
        IReadOnlyDictionary<string, byte[]> PackageBytesByExtensionId);
}

public sealed record WebDavSyncResult(int UploadedCount, int PulledCount, string RemoteRoot);

public sealed class WebDavSyncIndex
{
    public int SchemaVersion { get; set; } = 1;

    public List<WebDavSyncEntry> Items { get; set; } = [];
}

public sealed class WebDavSyncEntry
{
    public string ExtensionId { get; set; } = string.Empty;

    public string Version { get; set; } = "0.1.0";

    public string PackageHash { get; set; } = string.Empty;

    public string PackagePath { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public bool Deleted { get; set; }

    public bool LocalDeletionPending { get; set; }
}
