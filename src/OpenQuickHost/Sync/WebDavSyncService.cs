using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

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
        // 修复：根路径已经在BuildRelativeUri中处理，这里不需要再创建
        // 只需要确保packages目录存在即可
        await EnsureCollectionAsync("packages", cancellationToken);
    }

    public async Task<string?> TryReadExtensionDataTextAsync(string extensionId, string key, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureCollectionAsync("appdata", cancellationToken);
        var bytes = await TryGetBytesAsync(BuildExtensionDataPath(extensionId, key), cancellationToken);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task WriteExtensionDataTextAsync(string extensionId, string key, string content, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureCollectionAsync("appdata", cancellationToken);

        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var collectionSegments = new List<string> { "appdata", extensionId };
        if (segments.Length > 1)
        {
            collectionSegments.AddRange(segments.Take(segments.Length - 1));
        }

        await EnsureCollectionTreeAsync(cancellationToken, collectionSegments.ToArray());

        using var request = CreateRequest(HttpMethod.Put, BuildExtensionDataPath(extensionId, key));
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content ?? string.Empty));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
        {
            CharSet = "utf-8"
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }
    }

    public async Task<byte[]?> TryReadTemporaryFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (!IsAllowedTemporaryPath(relativePath))
        {
            throw new InvalidOperationException("只允许读取 WebDAV 临时文件。");
        }

        return await TryGetBytesAsync(relativePath, cancellationToken);
    }

    public async Task DeleteTemporaryFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (!IsAllowedTemporaryPath(relativePath))
        {
            throw new InvalidOperationException("只允许删除 WebDAV 临时文件。");
        }

        await DeleteRemoteFileAsync(relativePath, cancellationToken);
    }

    private static bool IsAllowedTemporaryPath(string relativePath)
    {
        return relativePath.StartsWith("temp/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith("mobile-screenshot-", StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith("mobile-photo-", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WebDavSyncResult> SyncExtensionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await ProbeAsync(cancellationToken);
        
        // 确保基础目录存在（特别是在清空远程后）
        await EnsureCollectionAsync("packages", cancellationToken);
        await EnsureCollectionAsync("state", cancellationToken);

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
                catch (FileNotFoundException ex)
                {
                    // 远程索引中有记录但文件不存在（可能被手动删除），从索引中移除
                    HostAssets.AppendLog($"WebDAV skipped missing remote package for {remoteEntry.ExtensionId}: {ex.Message}");
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
            Revision = mergedMap.Values.Select(static item => item.Revision).DefaultIfEmpty().Max(),
            UpdatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            UpdatedByDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId(),
            UpdatedByDeviceName = DeviceIdentityStore.GetDesktopDisplayName(),
            Items = mergedMap.Values
                .OrderBy(item => item.ExtensionId, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        if (remoteIndexChanged || !IndexesEquivalent(remoteIndex, mergedIndex))
        {
            await SaveRemoteIndexAsync(mergedIndex, cancellationToken);
        }

        await CleanupPurgedRemotePackagesAsync(mergedIndex, cancellationToken);

        SaveLocalIndex(ClearLocalPendingFlags(mergedIndex));
        await SyncSearchMemoryAsync(cancellationToken);
        var (configUploaded, configPulled) = await SyncLauncherConfigAsync(cancellationToken);
        return new WebDavSyncResult(uploaded, pulled, SyncRootDisplay, configUploaded, configPulled);
    }

    public async Task<WebDavYanmStateSyncResult> SyncYanmStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureCollectionAsync("state", cancellationToken);

        var localSettings = AppSettingsStore.Load();
        localSettings.Yanm ??= new YanmSettings();
        localSettings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localYanm = CloneByJson(localSettings.Yanm);
        var localUpdatedAtUtc = GetLocalYanmUpdatedAtUtc(localSettings);
        var remoteInfo = await TryGetRemoteFileInfoAsync("state/yanm-state.json", cancellationToken);
        if (remoteInfo != null && localUpdatedAtUtc > DateTime.MinValue)
        {
            if (remoteInfo.LastModifiedUtc <= localUpdatedAtUtc.AddSeconds(1) &&
                localUpdatedAtUtc <= remoteInfo.LastModifiedUtc.AddSeconds(1))
            {
                HostAssets.AppendLog(
                    $"WebDAV Yanm state sync: metadata unchanged, bytes={remoteInfo.ContentLength}.");
                return new WebDavYanmStateSyncResult(false, false, SyncRootDisplay, remoteInfo.LastModifiedUtc, (int)Math.Min(remoteInfo.ContentLength, int.MaxValue));
            }

            if (localUpdatedAtUtc > remoteInfo.LastModifiedUtc.AddSeconds(1) &&
                HasYanmComponentState(localYanm))
            {
                var uploadedAtUtc = DateTime.UtcNow;
                await UploadYanmStateAsync(localYanm, uploadedAtUtc, cancellationToken);
                SaveYanmStateUpdatedAtUtc(uploadedAtUtc);
                var uploadedBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = uploadedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
                HostAssets.AppendLog(
                    $"WebDAV Yanm state uploaded by metadata: localUpdated={localUpdatedAtUtc:O}, remoteModified={remoteInfo.LastModifiedUtc:O}, bytes={uploadedBytes}.");
                return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, uploadedAtUtc, uploadedBytes);
            }
        }

        var remoteBytes = await TryGetBytesAsync("state/yanm-state.json", cancellationToken);
        var remote = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<WebDavYanmStateSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions)
            : null;

        if (remote?.Yanm == null)
        {
            var legacyRemote = await TryLoadLegacyRemoteYanmStateAsync(cancellationToken);
            if (legacyRemote?.Yanm != null)
            {
                var legacyUpdatedAtUtc = TryParseUtc(legacyRemote.UpdatedAtUtc) ?? DateTime.MinValue;
                var shouldApplyLegacy =
                    legacyUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1) ||
                    !HasYanmComponentState(localYanm);
                if (shouldApplyLegacy)
                {
                    ApplyYanmStateSnapshot(legacyRemote.Yanm, legacyUpdatedAtUtc);
                    await UploadYanmStateAsync(legacyRemote.Yanm, legacyUpdatedAtUtc, cancellationToken);
                    HostAssets.AppendLog(
                        $"WebDAV Yanm state bootstrapped from launcher config: legacyUpdated={legacyUpdatedAtUtc:O}, localUpdated={localUpdatedAtUtc:O}.");
                    return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, legacyUpdatedAtUtc, Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(legacyRemote, JsonOptions)));
                }
            }

            var uploadedAtUtc = DateTime.UtcNow;
            await UploadYanmStateAsync(localYanm, uploadedAtUtc, cancellationToken);
            SaveYanmStateUpdatedAtUtc(uploadedAtUtc);
            var uploadedBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = uploadedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
            HostAssets.AppendLog($"WebDAV Yanm state uploaded: remote missing, bytes={uploadedBytes}.");
            return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, uploadedAtUtc, uploadedBytes);
        }

        var remoteUpdatedAtUtc = TryParseUtc(remote.UpdatedAtUtc) ?? DateTime.MinValue;
        var equivalent = AreJsonPayloadsEqual(localYanm, remote.Yanm);
        var payloadBytes = remoteBytes?.Length ?? Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(remote, JsonOptions));
        if (equivalent)
        {
            if (localUpdatedAtUtc == DateTime.MinValue && remoteUpdatedAtUtc > DateTime.MinValue)
            {
                SaveYanmStateUpdatedAtUtc(remoteUpdatedAtUtc);
            }

            HostAssets.AppendLog($"WebDAV Yanm state sync: no changes detected, bytes={payloadBytes}.");
            return new WebDavYanmStateSyncResult(false, false, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        if (remoteUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1) || localUpdatedAtUtc == DateTime.MinValue)
        {
            ApplyYanmStateSnapshot(remote.Yanm, remoteUpdatedAtUtc);
            HostAssets.AppendLog(
                $"WebDAV Yanm state pulled: remoteUpdated={remoteUpdatedAtUtc:O}, localUpdated={localUpdatedAtUtc:O}, bytes={payloadBytes}.");
            return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        if (!HasYanmComponentState(localYanm) && HasYanmComponentState(remote.Yanm))
        {
            ApplyYanmStateSnapshot(remote.Yanm, remoteUpdatedAtUtc);
            HostAssets.AppendLog(
                $"WebDAV Yanm state pulled to protect remote component data: remoteUpdated={remoteUpdatedAtUtc:O}, localUpdated={localUpdatedAtUtc:O}, bytes={payloadBytes}.");
            return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        var updatedAtUtc = DateTime.UtcNow;
        await UploadYanmStateAsync(localYanm, updatedAtUtc, cancellationToken);
        SaveYanmStateUpdatedAtUtc(updatedAtUtc);
        var uploadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = updatedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
        HostAssets.AppendLog(
            $"WebDAV Yanm state uploaded: localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc:O}, bytes={uploadBytes}.");
        return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, updatedAtUtc, uploadBytes);
    }

    public async Task ClearCloudAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await ProbeAsync(cancellationToken);

        HostAssets.AppendLog("WebDAV clear cloud started");

        // 1. 先清空本地状态，避免后续同步冲突
        if (File.Exists(HostAssets.WebDavSyncStatePath))
        {
            File.Delete(HostAssets.WebDavSyncStatePath);
            HostAssets.AppendLog("WebDAV clear cloud: cleared local state");
        }

        // 2. 删除根目录的index.json（旧版本可能存在）
        try
        {
            await DeleteRemoteFileAsync("index.json", cancellationToken);
            HostAssets.AppendLog("WebDAV clear cloud: deleted root index.json");
            await Task.Delay(500, cancellationToken); // 避免频率限制
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"WebDAV clear cloud: failed to delete root index: {ex.Message}");
        }

        // 3. 删除state目录（包括索引和配置）
        try
        {
            await DeleteRemoteDirectoryAsync("state", cancellationToken);
            HostAssets.AppendLog("WebDAV clear cloud: deleted state directory");
            await Task.Delay(500, cancellationToken); // 避免频率限制
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"WebDAV clear cloud: failed to delete state: {ex.Message}");
        }

        // 4. 删除packages目录（最大的目录，最后删除）
        try
        {
            await DeleteRemoteDirectoryAsync("packages", cancellationToken);
            HostAssets.AppendLog("WebDAV clear cloud: deleted packages directory");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"WebDAV clear cloud: failed to delete packages: {ex.Message}");
            // 如果删除packages失败，尝试逐个删除子目录
            await DeletePackagesGraduallyAsync(cancellationToken);
        }

        HostAssets.AppendLog("WebDAV clear cloud completed");
    }

    private async Task DeletePackagesGraduallyAsync(CancellationToken cancellationToken)
    {
        try
        {
            HostAssets.AppendLog("WebDAV clear cloud: attempting gradual deletion of packages");
            
            // 读取远程索引，获取所有扩展ID
            var remoteIndex = await LoadRemoteIndexAsync(cancellationToken);
            var extensionIds = remoteIndex.Items
                .Select(item => item.ExtensionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            HostAssets.AppendLog($"WebDAV clear cloud: found {extensionIds.Count} extensions to delete");

            // 逐个删除扩展目录，每次删除后延迟
            for (int i = 0; i < extensionIds.Count; i++)
            {
                var extensionId = extensionIds[i];
                try
                {
                    await DeleteRemotePackageTreeAsync(extensionId, cancellationToken);
                    HostAssets.AppendLog($"WebDAV clear cloud: deleted extension {i + 1}/{extensionIds.Count}: {extensionId}");
                    
                    // 每删除5个扩展延迟1秒，避免频率限制
                    if ((i + 1) % 5 == 0)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(200, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"WebDAV clear cloud: failed to delete extension {extensionId}: {ex.Message}");
                }
            }

            // 最后尝试删除packages目录本身
            await Task.Delay(1000, cancellationToken);
            await DeleteRemoteDirectoryAsync("packages", cancellationToken);
            HostAssets.AppendLog("WebDAV clear cloud: deleted packages directory after gradual cleanup");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"WebDAV clear cloud: gradual deletion failed: {ex.Message}");
        }
    }

    private async Task DeleteRemoteDirectoryAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }
    }

    private async Task DeleteRemoteFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete, relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }
    }

    private async Task<(bool uploaded, bool pulled)> SyncLauncherConfigAsync(CancellationToken cancellationToken)
    {
        await EnsureCollectionAsync("state", cancellationToken);

        var localSettings = AppSettingsStore.Load();
        var localConfig = CloudQuickPanelConfigSnapshot.FromSettings(localSettings);
        var explicitLocalUpdatedAtUtc = TryParseUtc(localSettings.LauncherConfigUpdatedAtUtc);
        var legacyLocalUpdatedAtUtc = explicitLocalUpdatedAtUtc == null && HasMeaningfulLauncherConfig(localConfig) && File.Exists(AppSettingsStore.SettingsPath)
            ? File.GetLastWriteTimeUtc(AppSettingsStore.SettingsPath)
            : (DateTime?)null;
        var localUpdatedAtUtc = explicitLocalUpdatedAtUtc ?? legacyLocalUpdatedAtUtc ?? DateTime.MinValue;
        var remoteBytes = await TryGetBytesAsync("state/launcher-config.json", cancellationToken);
        var remote = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<WebDavLauncherConfigSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions)
            : null;
        var hasRemoteObjects = await HasLauncherConfigObjectsAsync(cancellationToken);
        remote = hasRemoteObjects
            ? await TryLoadLauncherConfigObjectsAsync(remote?.Config, cancellationToken) ?? remote
            : remote;
        if (remote?.Config != null)
        {
            // 独立 yanm-state 是燕幕的唯一写入权威；旧主快照字段仅供迁移读取。
            remote.Config.Yanm = null;
        }

        if (remote?.Config == null)
        {
            if (CloudQuickPanelConfigSnapshot.IsInitialDefaultSnapshot(localConfig))
            {
                HostAssets.AppendLog("WebDAV launcher config upload skipped: local snapshot has no user content and remote is missing.");
                return (false, false);
            }

            var uploadedAtUtc = DateTime.UtcNow;
            if (!await UploadLauncherConfigAsync(localConfig, uploadedAtUtc, cancellationToken))
            {
                return (false, false);
            }

            SaveLauncherConfigUpdatedAtUtc(uploadedAtUtc);
            HostAssets.AppendLog("WebDAV launcher config uploaded: remote missing.");
            return (true, false);
        }

        var remoteUpdatedAtUtc = TryParseUtc(remote.UpdatedAtUtc) ?? DateTime.MinValue;
        var localTime = localConfig.UpdatedAtUtc;
        var remoteTime = remote.Config.UpdatedAtUtc;
        localConfig.UpdatedAtUtc = null;
        remote.Config.UpdatedAtUtc = null;
        var localMetadata = CaptureMetadata(localConfig);
        var remoteMetadata = CaptureMetadata(remote.Config);
        ClearMetadataForComparison(localConfig);
        ClearMetadataForComparison(remote.Config);
        var equivalent = AreJsonPayloadsEqual(localConfig, remote.Config);
        localConfig.UpdatedAtUtc = localTime;
        remote.Config.UpdatedAtUtc = remoteTime;
        RestoreMetadata(localConfig, localMetadata);
        RestoreMetadata(remote.Config, remoteMetadata);
        if (equivalent)
        {
            if (!hasRemoteObjects && remote?.Config != null)
            {
                var backfillUpdatedAtUtc = remoteUpdatedAtUtc == DateTime.MinValue ? DateTime.UtcNow : remoteUpdatedAtUtc;
                if (!await UploadLauncherConfigAsync(localConfig, backfillUpdatedAtUtc, cancellationToken))
                {
                    return (false, false);
                }

                SaveLauncherConfigUpdatedAtUtc(backfillUpdatedAtUtc);
                HostAssets.AppendLog("WebDAV launcher config objects backfilled from legacy snapshot.");
                return (true, false);
            }

            if (explicitLocalUpdatedAtUtc == null && remoteUpdatedAtUtc > DateTime.MinValue)
            {
                SaveLauncherConfigUpdatedAtUtc(remoteUpdatedAtUtc);
            }

            HostAssets.AppendLog("WebDAV launcher config sync: no changes detected.");
            return (false, false);
        }

        if (explicitLocalUpdatedAtUtc == null && legacyLocalUpdatedAtUtc == null)
        {
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            HostAssets.AppendLog(
                $"WebDAV launcher config pulled: local timestamp missing, remoteUpdated={remoteUpdatedAtUtc:O}.");
            return (false, true);
        }

        if (remoteUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1))
        {
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            HostAssets.AppendLog(
                $"WebDAV launcher config pulled: remoteUpdated={remoteUpdatedAtUtc:O}, localUpdated={localUpdatedAtUtc:O}.");
            return (false, true);
        }

        if (IsLikelyFreshLocalLauncherConfig(localConfig) && HasMeaningfulLauncherConfig(remote.Config))
        {
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            HostAssets.AppendLog(
                $"WebDAV launcher config pulled over fresh local config: remoteUpdated={remoteUpdatedAtUtc:O}, localUpdated={localUpdatedAtUtc:O}.");
            return (false, true);
        }

        var updatedAtUtc = DateTime.UtcNow;
        if (!await UploadLauncherConfigAsync(localConfig, updatedAtUtc, cancellationToken))
        {
            return (false, false);
        }

        SaveLauncherConfigUpdatedAtUtc(updatedAtUtc);
        HostAssets.AppendLog(
            $"WebDAV launcher config uploaded: localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc:O}.");
        return (true, false);
    }

    private async Task<bool> UploadLauncherConfigAsync(
        CloudQuickPanelConfigSnapshot config,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        config.UpdatedAtUtc = updatedAtUtc.ToString("O");
        config.SourceDeviceId = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        config.SourceDeviceName = DeviceIdentityStore.GetDesktopDisplayName();
        config.HasUserContent = CloudQuickPanelConfigSnapshot.HasMeaningfulUserContent(config);
        config.IsInitialDefaultConfig = !config.HasUserContent;
        var snapshot = new WebDavLauncherConfigSnapshot
        {
            UpdatedAtUtc = updatedAtUtc.ToString("O"),
            Config = config
        };
        var writes = LauncherConfigObjectStore.PrepareWrites(config, updatedAtUtc);
        var changedWrites = new List<LauncherConfigObjectWrite>();
        var effectiveWrites = new List<LauncherConfigObjectWrite>();
        foreach (var write in writes)
        {
            var remoteBytes = await TryGetBytesAsync(write.Path, cancellationToken);
            var remoteEnvelope = LauncherConfigObjectStore.Deserialize(remoteBytes);
            if (LauncherConfigObjectStore.HasEquivalentPayload(write.Envelope, remoteEnvelope))
            {
                effectiveWrites.Add(LauncherConfigObjectStore.CreateWrite(remoteEnvelope!));
                continue;
            }

            changedWrites.Add(write);
            effectiveWrites.Add(write);
        }

        if (changedWrites.Count == 0)
        {
            HostAssets.AppendLog("WebDAV launcher config upload skipped: all object payloads are unchanged.");
            return false;
        }

        foreach (var write in changedWrites)
        {
            using var objectRequest = CreateRequest(HttpMethod.Put, write.Path);
            objectRequest.Content = new ByteArrayContent(write.Bytes);
            objectRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var objectResponse = await _httpClient.SendAsync(objectRequest, cancellationToken);
            if (!objectResponse.IsSuccessStatusCode)
            {
                await ThrowWebDavFailureAsync(objectRequest, objectResponse, cancellationToken);
            }
        }

        var manifestBytes = LauncherConfigObjectStore.SerializeManifest(
            LauncherConfigObjectStore.CreateManifest(effectiveWrites, updatedAtUtc));
        using (var manifestRequest = CreateRequest(HttpMethod.Put, LauncherConfigObjectStore.ManifestPath))
        {
            manifestRequest.Content = new ByteArrayContent(manifestBytes);
            manifestRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var manifestResponse = await _httpClient.SendAsync(manifestRequest, cancellationToken);
            if (!manifestResponse.IsSuccessStatusCode)
            {
                await ThrowWebDavFailureAsync(manifestRequest, manifestResponse, cancellationToken);
            }
        }

        var changeBytes = LauncherConfigObjectStore.SerializeChangeSet(
            LauncherConfigObjectStore.CreateChangeSet(changedWrites, updatedAtUtc, "launcher-config-sync"));
        var changePath = LauncherConfigObjectStore.GetChangePath(updatedAtUtc);
        using (var changeRequest = CreateRequest(HttpMethod.Put, changePath))
        {
            changeRequest.Content = new ByteArrayContent(changeBytes);
            changeRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var changeResponse = await _httpClient.SendAsync(changeRequest, cancellationToken);
            if (!changeResponse.IsSuccessStatusCode)
            {
                await ThrowWebDavFailureAsync(changeRequest, changeResponse, cancellationToken);
            }
        }

        await WriteLauncherRestorePointAsync(effectiveWrites, changedWrites, updatedAtUtc, changePath, cancellationToken);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        using var request = CreateRequest(HttpMethod.Put, "state/launcher-config.json");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }

        HostAssets.AppendLog($"WebDAV launcher config uploaded: changedObjects={changedWrites.Count}, totalObjects={effectiveWrites.Count}");
        return true;
    }

    private async Task WriteLauncherRestorePointAsync(
        IReadOnlyList<LauncherConfigObjectWrite> effectiveWrites,
        IReadOnlyList<LauncherConfigObjectWrite> changedWrites,
        DateTime updatedAtUtc,
        string changeSetPath,
        CancellationToken cancellationToken)
    {
        var point = LauncherConfigObjectStore.CreateRestorePoint(
            effectiveWrites,
            changedWrites,
            updatedAtUtc,
            "launcher-config-sync");
        var pointBytes = LauncherConfigObjectStore.SerializeRestorePoint(point);
        var pointPath = LauncherConfigObjectStore.GetRestorePointPath(updatedAtUtc);
        using (var pointRequest = CreateRequest(HttpMethod.Put, pointPath))
        {
            pointRequest.Content = new ByteArrayContent(pointBytes);
            pointRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var pointResponse = await _httpClient.SendAsync(pointRequest, cancellationToken);
            if (!pointResponse.IsSuccessStatusCode)
            {
                await ThrowWebDavFailureAsync(pointRequest, pointResponse, cancellationToken);
            }
        }

        var indexBytes = await TryGetBytesAsync(LauncherConfigObjectStore.HistoryIndexPath, cancellationToken);
        LauncherConfigHistoryIndex index;
        try
        {
            index = LauncherConfigObjectStore.DeserializeHistoryIndex(indexBytes);
        }
        catch (JsonException ex)
        {
            HostAssets.AppendLog($"WebDAV restore index was invalid and will be rebuilt: {ex.Message}");
            index = new LauncherConfigHistoryIndex();
        }
        index.RestorePoints.RemoveAll(item => item.RestorePointId.Equals(point.RestorePointId, StringComparison.Ordinal));
        index.RestorePoints.Add(new LauncherConfigRestorePointInfo
        {
            RestorePointId = point.RestorePointId,
            Revision = point.Revision,
            CreatedAtUtc = point.CreatedAtUtc,
            SourceDeviceId = point.SourceDeviceId,
            SourceDeviceName = point.SourceDeviceName,
            Reason = point.Reason,
            Path = pointPath,
            ChangeSetPath = changeSetPath,
            Sha256 = Convert.ToHexString(SHA256.HashData(pointBytes)).ToLowerInvariant(),
            SizeBytes = pointBytes.Length,
            ObjectCount = point.Objects.Count,
            ChangedObjectIds = point.ChangedObjectIds.ToList()
        });
        index.RestorePoints = index.RestorePoints.OrderByDescending(static item => item.Revision).ToList();
        var pruned = index.RestorePoints.Skip(LauncherConfigObjectStore.MaxRestorePointCount).ToArray();
        index.RestorePoints = index.RestorePoints.Take(LauncherConfigObjectStore.MaxRestorePointCount).ToList();
        index.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
        var historyIndexBytes = LauncherConfigObjectStore.SerializeHistoryIndex(index);
        using (var indexRequest = CreateRequest(HttpMethod.Put, LauncherConfigObjectStore.HistoryIndexPath))
        {
            indexRequest.Content = new ByteArrayContent(historyIndexBytes);
            indexRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var indexResponse = await _httpClient.SendAsync(indexRequest, cancellationToken);
            if (!indexResponse.IsSuccessStatusCode)
            {
                await ThrowWebDavFailureAsync(indexRequest, indexResponse, cancellationToken);
            }
        }
        foreach (var stale in pruned)
        {
            try
            {
                await DeleteRemoteFileAsync(stale.Path, cancellationToken);
                if (!string.IsNullOrWhiteSpace(stale.ChangeSetPath))
                {
                    await DeleteRemoteFileAsync(stale.ChangeSetPath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"WebDAV restore point prune deferred: path={stale.Path}, error={ex.Message}");
            }
        }
    }

    private async Task<WebDavYanmStateSnapshot?> TryLoadLegacyRemoteYanmStateAsync(CancellationToken cancellationToken)
    {
        var remoteBytes = await TryGetBytesAsync("state/launcher-config.json", cancellationToken);
        var remote = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<WebDavLauncherConfigSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions)
            : null;
        remote = await TryLoadLauncherConfigObjectsAsync(remote?.Config, cancellationToken) ?? remote;
        return remote?.Config?.Yanm == null
            ? null
            : new WebDavYanmStateSnapshot
            {
                UpdatedAtUtc = remote.UpdatedAtUtc,
                Yanm = remote.Config.Yanm
            };
    }

    private async Task<WebDavLauncherConfigSnapshot?> TryLoadLauncherConfigObjectsAsync(
        CloudQuickPanelConfigSnapshot? baseSnapshot,
        CancellationToken cancellationToken)
    {
        var objects = new List<LauncherConfigObjectEnvelope>();
        foreach (var definition in LauncherConfigObjectStore.Definitions)
        {
            var bytes = await TryGetBytesAsync(LauncherConfigObjectStore.GetPath(definition.ObjectId), cancellationToken);
            var obj = LauncherConfigObjectStore.Deserialize(bytes);
            if (obj != null && obj.ObjectId.Equals(definition.ObjectId, StringComparison.OrdinalIgnoreCase))
            {
                objects.Add(obj);
            }
        }

        var snapshot = LauncherConfigObjectStore.Compose(baseSnapshot, objects, out var updatedAtUtc);
        return snapshot == null
            ? null
            : new WebDavLauncherConfigSnapshot
            {
                SchemaVersion = 2,
                UpdatedAtUtc = updatedAtUtc == DateTime.MinValue
                    ? snapshot.UpdatedAtUtc ?? string.Empty
                    : updatedAtUtc.ToString("O"),
                Config = snapshot
            };
    }

    private async Task<bool> HasLauncherConfigObjectsAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in LauncherConfigObjectStore.Definitions)
        {
            var bytes = await TryGetBytesAsync(LauncherConfigObjectStore.GetPath(definition.ObjectId), cancellationToken);
            if (bytes is { Length: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    private async Task UploadYanmStateAsync(
        YanmSettings yanm,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var remoteBytes = await TryGetBytesAsync("state/yanm-state.json", cancellationToken);
        if (remoteBytes is { Length: > 0 })
        {
            try
            {
                var remote = JsonSerializer.Deserialize<WebDavYanmStateSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions);
                if (remote?.Yanm != null && AreJsonPayloadsEqual(yanm, remote.Yanm))
                {
                    HostAssets.AppendLog("WebDAV Yanm state upload skipped: remote content is unchanged except metadata.");
                    return;
                }
            }
            catch (JsonException ex)
            {
                HostAssets.AppendLog($"WebDAV Yanm state unchanged check skipped: remote JSON parse failed: {ex.Message}");
            }
        }

        var snapshot = new WebDavYanmStateSnapshot
        {
            UpdatedAtUtc = updatedAtUtc.ToString("O"),
            Yanm = CloneByJson(yanm)
        };
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        using var request = CreateRequest(HttpMethod.Put, "state/yanm-state.json");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }
    }

    private static void ApplyLauncherConfigSnapshot(CloudQuickPanelConfigSnapshot snapshot, DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        var incoming = snapshot.ToAppSettings();
        settings.ThemeMode = incoming.ThemeMode;
        if (snapshot.AutoCloseToastEnabled != null) settings.AutoCloseToastEnabled = incoming.AutoCloseToastEnabled;
        if (snapshot.EnableAutoUpdate != null) settings.EnableAutoUpdate = incoming.EnableAutoUpdate;
        if (snapshot.EnableBrowserHelper != null) settings.EnableBrowserHelper = incoming.EnableBrowserHelper;
        if (snapshot.PreferManualExtensionEditor != null) settings.PreferManualExtensionEditor = incoming.PreferManualExtensionEditor;
        if (snapshot.EnableEverything != null) settings.EnableEverything = incoming.EnableEverything;
        settings.LauncherHotkey = incoming.LauncherHotkey;
        settings.LaunchAtStartup = incoming.LaunchAtStartup;
        settings.RefreshCloudOnStartup = incoming.RefreshCloudOnStartup;
        settings.CloseToTray = incoming.CloseToTray;
        settings.QuickPanelTrigger = incoming.QuickPanelTrigger;
        settings.QuickPanelSlots = incoming.QuickPanelSlots;
        settings.QuickPanelGlobalGroups = incoming.QuickPanelGlobalGroups;
        settings.QuickPanelContextGroups = incoming.QuickPanelContextGroups;
        settings.SelectedQuickPanelGlobalGroupId = incoming.SelectedQuickPanelGlobalGroupId;
        settings.SelectedQuickPanelContextGroupId = incoming.SelectedQuickPanelContextGroupId;
        settings.GlobalFavoriteExtensionIds = incoming.GlobalFavoriteExtensionIds;
        settings.ContextFavoriteExtensionIds = incoming.ContextFavoriteExtensionIds;
        settings.DisabledExtensionIds = incoming.DisabledExtensionIds;
        settings.PinnedSearchScopeCommandIds = incoming.PinnedSearchScopeCommandIds;
        settings.QuickPanelMouseTriggers = incoming.QuickPanelMouseTriggers;
        if (snapshot.MouseGestureAppBindings != null) settings.MouseGestureAppBindings = incoming.MouseGestureAppBindings;
        if (snapshot.SearchScopeConfigs != null) settings.SearchScopeConfigs = incoming.SearchScopeConfigs;
        if (snapshot.EnvironmentVariables != null)
        {
            settings.EnvironmentVariables = AppEnvironmentVariableStore.PrepareSyncedMetadata(incoming.EnvironmentVariables);
        }
        settings.MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(incoming.MouseGestureTriggerMode);
        settings.WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(incoming.WindowSnapAssistMouseTriggerMode);
        settings.EnableWindowSnapAssist = incoming.EnableWindowSnapAssist;
        settings.WindowSnapAssistHotkey = incoming.WindowSnapAssistHotkey;
        settings.WindowSnapAssistCustomLayouts = incoming.WindowSnapAssistCustomLayouts;
        settings.WindowBindings = incoming.WindowBindings;
        settings.EnableAgentApi = incoming.EnableAgentApi;
        settings.AgentApiPort = incoming.AgentApiPort;
        if (snapshot.YarnSelect != null)
        {
            settings.YarnSelect = incoming.YarnSelect;
        }

        if (snapshot.RadialMenu != null)
        {
            settings.RadialMenu = incoming.RadialMenu;
        }

        if (snapshot.YanyuRules != null)
        {
            settings.YanyuRules = incoming.YanyuRules;
        }

        if (HasAiConfigPayload(snapshot))
        {
            AiCredentialStore.PreserveLocalSecrets(settings, incoming);
            settings.AiBaseUrl = incoming.AiBaseUrl;
            settings.AiApiKey = incoming.AiApiKey;
            settings.AiModel = incoming.AiModel;
        }

        settings.LauncherConfigUpdatedAtUtc = updatedAtUtc.ToString("O");

        AppSettingsStore.Save(settings);
    }

    private static void ApplyYanmStateSnapshot(YanmSettings yanm, DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        settings.Yanm = CloneByJson(yanm);
        settings.YanmStateUpdatedAtUtc = updatedAtUtc.ToString("O");
        AppSettingsStore.Save(settings);
    }

    private static void SaveYanmStateUpdatedAtUtc(DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        settings.YanmStateUpdatedAtUtc = updatedAtUtc.ToString("O");
        AppSettingsStore.Save(settings);
    }

    private static void SaveLauncherConfigUpdatedAtUtc(DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        settings.LauncherConfigUpdatedAtUtc = updatedAtUtc.ToString("O");
        AppSettingsStore.Save(settings);
    }

    private static DateTime GetLocalYanmUpdatedAtUtc(AppSettings settings)
    {
        return TryParseUtc(settings.YanmStateUpdatedAtUtc) ?? DateTime.MinValue;
    }

    private static DateTime? TryParseUtc(string? value)
    {
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static bool AreJsonPayloadsEqual<T>(T left, T right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;

        if (left is YanmSettings leftYanm && right is YanmSettings rightYanm)
        {
            return AreYanmSettingsEqual(leftYanm, rightYanm);
        }

        return string.Equals(JsonSerializer.Serialize(left, JsonOptions), JsonSerializer.Serialize(right, JsonOptions), StringComparison.Ordinal);
    }

    private static bool AreYanmSettingsEqual(YanmSettings left, YanmSettings right)
    {
        if (left.Enabled != right.Enabled) return false;
        if (left.ActivationKey != right.ActivationKey) return false;
        if (left.CustomShortcut != right.CustomShortcut) return false;
        if (left.TriggerWinHold != right.TriggerWinHold) return false;
        if (left.TriggerWinDoubleTap != right.TriggerWinDoubleTap) return false;
        if (left.TriggerRightButtonDrag != right.TriggerRightButtonDrag) return false;
        if (left.TriggerMiddleButtonDrag != right.TriggerMiddleButtonDrag) return false;
        if (left.TriggerRightButtonLongPress != right.TriggerRightButtonLongPress) return false;
        if (left.TriggerMiddleButtonLongPress != right.TriggerMiddleButtonLongPress) return false;
        if (left.TriggerMiddleButtonDown != right.TriggerMiddleButtonDown) return false;
        if (left.TriggerX1ButtonDown != right.TriggerX1ButtonDown) return false;
        if (left.TriggerX2ButtonDown != right.TriggerX2ButtonDown) return false;
        if (left.TriggerHorizontalWheel != right.TriggerHorizontalWheel) return false;
        if (left.TriggerCtrlLeftClick != right.TriggerCtrlLeftClick) return false;
        if (left.TriggerCtrlLeftDrag != right.TriggerCtrlLeftDrag) return false;
        if (left.TriggerCtrlRightClick != right.TriggerCtrlRightClick) return false;
        if (left.TriggerCtrlMiddleClick != right.TriggerCtrlMiddleClick) return false;
        if (left.MouseTriggerMode != right.MouseTriggerMode) return false;
        if (left.DragThresholdPixels != right.DragThresholdPixels) return false;
        if (left.HoldDelayMilliseconds != right.HoldDelayMilliseconds) return false;
        if (left.GridSizePixels != right.GridSizePixels) return false;
        if (Math.Abs(left.OverlayOpacity - right.OverlayOpacity) > 0.001) return false;
        if (left.HasInitializedDefaultComponents != right.HasInitializedDefaultComponents) return false;
        if (left.DefaultComponentVersion != right.DefaultComponentVersion) return false;
        if (!AreListsEqual(left.WhitelistedProcesses, right.WhitelistedProcesses)) return false;
        if (!AreListsEqual(left.BlacklistedProcesses, right.BlacklistedProcesses)) return false;
        if (!AreComponentsListsEqual(left.Components, right.Components)) return false;
        if (!AreDictionariesEqual(left.ComponentState, right.ComponentState)) return false;
        return true;
    }

    private static bool AreListsEqual(List<string>? left, List<string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if ((left == null || left.Count == 0) && (right == null || right.Count == 0)) return true;
        if (left == null || right == null) return false;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    private static bool AreComponentsListsEqual(List<YanmComponentSettings>? left, List<YanmComponentSettings>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if ((left == null || left.Count == 0) && (right == null || right.Count == 0)) return true;
        if (left == null || right == null) return false;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (JsonSerializer.Serialize(left[i], JsonOptions) != JsonSerializer.Serialize(right[i], JsonOptions)) return false;
        }
        return true;
    }

    private static bool AreDictionariesEqual(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if ((left == null || left.Count == 0) && (right == null || right.Count == 0)) return true;
        if (left == null || right == null) return false;
        if (left.Count != right.Count) return false;
        foreach (var kvp in left)
        {
            if (!right.TryGetValue(kvp.Key, out var val) || val != kvp.Value)
            {
                return false;
            }
        }
        return true;
    }

    private static T CloneByJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? value;
    }

    private static bool HasMeaningfulLauncherConfig(CloudQuickPanelConfigSnapshot config)
    {
        return CloudQuickPanelConfigSnapshot.HasMeaningfulUserContent(config);
    }

    private static bool IsLikelyFreshLocalLauncherConfig(CloudQuickPanelConfigSnapshot config)
    {
        return CloudQuickPanelConfigSnapshot.IsInitialDefaultSnapshot(config);
    }

    private static bool HasAiSettings(CloudQuickPanelConfigSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.AiBaseUrl) ||
               !string.IsNullOrWhiteSpace(snapshot.AiApiKey) ||
               !string.IsNullOrWhiteSpace(snapshot.AiModel);
    }

    private static bool HasAiConfigPayload(CloudQuickPanelConfigSnapshot snapshot)
    {
        return snapshot.AiBaseUrl != null ||
               snapshot.AiApiKey != null ||
               snapshot.AiModel != null;
    }

    private static bool HasGroupContent(IEnumerable<QuickPanelGroupSettings> groups)
    {
        return groups.Any(static group =>
            group.Slots.Any(static slot => !string.IsNullOrWhiteSpace(slot)) ||
            group.SlotItems.Any(static slot => slot != null));
    }

    private static bool HasRadialContent(RadialMenuSettings? settings)
    {
        return settings != null &&
               (settings.Enabled ||
                settings.Slots.Any(static slot => !string.IsNullOrWhiteSpace(slot)) ||
                settings.Pages.Any(static page =>
                    page.Slots.Any(static slot => !string.IsNullOrWhiteSpace(slot)) ||
                    page.ChildPageIds.Any(static pageId => !string.IsNullOrWhiteSpace(pageId))));
    }

    private static bool HasYanmComponentState(YanmSettings? settings)
    {
        return settings?.ComponentState?.Count > 0;
    }

    private static SnapshotMetadata CaptureMetadata(CloudQuickPanelConfigSnapshot config) =>
        new(config.SchemaVersion, config.SourceDeviceId, config.SourceDeviceName, config.IsInitialDefaultConfig, config.HasUserContent);

    private static void ClearMetadataForComparison(CloudQuickPanelConfigSnapshot config)
    {
        config.SchemaVersion = 0;
        config.SourceDeviceId = null;
        config.SourceDeviceName = null;
        config.IsInitialDefaultConfig = false;
        config.HasUserContent = false;
    }

    private static void RestoreMetadata(CloudQuickPanelConfigSnapshot config, SnapshotMetadata metadata)
    {
        config.SchemaVersion = metadata.SchemaVersion;
        config.SourceDeviceId = metadata.SourceDeviceId;
        config.SourceDeviceName = metadata.SourceDeviceName;
        config.IsInitialDefaultConfig = metadata.IsInitialDefaultConfig;
        config.HasUserContent = metadata.HasUserContent;
    }

    private sealed record SnapshotMetadata(
        int SchemaVersion,
        string? SourceDeviceId,
        string? SourceDeviceName,
        bool IsInitialDefaultConfig,
        bool HasUserContent);

    private async Task SyncSearchMemoryAsync(CancellationToken cancellationToken)
    {
        await EnsureCollectionAsync("state", cancellationToken);

        var localMemory = SearchUsageMemory.Load();
        var remoteBytes = await TryGetBytesAsync("state/search-memory.json", cancellationToken);
        var remoteMemory = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<SearchUsageMemory>(Encoding.UTF8.GetString(remoteBytes), JsonOptions) ?? new SearchUsageMemory()
            : new SearchUsageMemory();

        var merged = SearchUsageMemory.Merge(localMemory, remoteMemory);
        SearchUsageMemory.Save(merged);

        var json = JsonSerializer.Serialize(merged, JsonOptions);
        using var request = CreateRequest(HttpMethod.Put, "state/search-memory.json");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }
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
        // 原子写：状态文件写坏会让删除墓碑丢失 → 已删除的扩展在下次同步时“复活”
        SafeFile.AtomicWriteText(HostAssets.WebDavSyncStatePath, json);
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
        CaptureExtensionBaseline(existing);
        existing.Deleted = true;
        existing.Purged = false;
        existing.LocalDeletionPending = true;
        ExtensionSyncRevision.Stamp(existing, index.Revision);
        index.Revision = existing.Revision;
        index.UpdatedAtUtc = existing.UpdatedAtUtc;
        index.UpdatedByDeviceId = existing.UpdatedByDeviceId;
        index.UpdatedByDeviceName = existing.UpdatedByDeviceName;
        SaveLocalIndex(index);
    }

    public static void MarkExtensionRestoredLocally(string extensionId, string? version = null)
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
        CaptureExtensionBaseline(existing);
        existing.Deleted = false;
        existing.Purged = false;
        existing.LocalDeletionPending = false;
        ExtensionSyncRevision.Stamp(existing, index.Revision);
        index.Revision = existing.Revision;
        index.UpdatedAtUtc = existing.UpdatedAtUtc;
        index.UpdatedByDeviceId = existing.UpdatedByDeviceId;
        index.UpdatedByDeviceName = existing.UpdatedByDeviceName;
        SaveLocalIndex(index);
    }

    public static void MarkExtensionPurgedLocally(string extensionId, string? version = null)
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
        CaptureExtensionBaseline(existing);
        existing.Deleted = true;
        existing.Purged = true;
        existing.LocalDeletionPending = true;
        ExtensionSyncRevision.Stamp(existing, index.Revision);
        index.Revision = existing.Revision;
        index.UpdatedAtUtc = existing.UpdatedAtUtc;
        index.UpdatedByDeviceId = existing.UpdatedByDeviceId;
        index.UpdatedByDeviceName = existing.UpdatedByDeviceName;
        SaveLocalIndex(index);
    }

    private static void CaptureExtensionBaseline(WebDavSyncEntry entry)
    {
        if (entry.BaseRevision > 0 || entry.Revision <= 0) return;
        entry.BaseRevision = entry.Revision;
        entry.BasePackageHash = entry.PackageHash;
        entry.BaseDeleted = entry.Deleted;
        entry.BasePurged = entry.Purged;
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
            var contentChanged = previous == null || previous.Revision <= 0 || previous.Deleted ||
                                 !string.Equals(previous.PackageHash, packageHash, StringComparison.OrdinalIgnoreCase) ||
                                 !string.Equals(previous.Title, command.Title, StringComparison.Ordinal) ||
                                 !string.Equals(previous.Category, command.Category ?? "扩展", StringComparison.Ordinal) ||
                                 !string.Equals(previous.Version, command.DeclaredVersion, StringComparison.OrdinalIgnoreCase);
            var entry = new WebDavSyncEntry
            {
                ExtensionId = command.ExtensionId,
                Title = command.Title,
                Category = command.Category ?? "扩展",
                Version = command.DeclaredVersion,
                PackageHash = packageHash,
                PackagePath = packagePath,
                UpdatedAtUtc = updatedAtUtc,
                Revision = contentChanged ? ExtensionSyncRevision.Next(previous?.Revision ?? 0) : previous!.Revision,
                UpdatedByDeviceId = contentChanged ? DeviceIdentityStore.GetOrCreateDesktopDeviceId() : previous!.UpdatedByDeviceId,
                UpdatedByDeviceName = contentChanged ? DeviceIdentityStore.GetDesktopDisplayName() : previous!.UpdatedByDeviceName,
                BaseRevision = contentChanged && previous != null
                    ? (previous.BaseRevision > 0 ? previous.BaseRevision : previous.Revision)
                    : previous?.BaseRevision ?? 0,
                BasePackageHash = contentChanged && previous != null
                    ? (previous.BaseRevision > 0 ? previous.BasePackageHash : previous.PackageHash)
                    : previous?.BasePackageHash ?? string.Empty,
                BaseDeleted = contentChanged && previous != null
                    ? (previous.BaseRevision > 0 ? previous.BaseDeleted : previous.Deleted)
                    : previous?.BaseDeleted ?? false,
                BasePurged = contentChanged && previous != null
                    ? (previous.BaseRevision > 0 ? previous.BasePurged : previous.Purged)
                    : previous?.BasePurged ?? false,
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
                Title = stateEntry.Title,
                Category = stateEntry.Category,
                Version = stateEntry.Version,
                PackageHash = stateEntry.PackageHash,
                PackagePath = stateEntry.PackagePath,
                UpdatedAtUtc = stateEntry.Deleted ? stateEntry.UpdatedAtUtc : DateTimeOffset.UtcNow.ToString("O"),
                Revision = stateEntry.Revision > 0 ? stateEntry.Revision : ExtensionSyncRevision.Next(localState.Revision),
                UpdatedByDeviceId = string.IsNullOrWhiteSpace(stateEntry.UpdatedByDeviceId)
                    ? DeviceIdentityStore.GetOrCreateDesktopDeviceId()
                    : stateEntry.UpdatedByDeviceId,
                UpdatedByDeviceName = string.IsNullOrWhiteSpace(stateEntry.UpdatedByDeviceName)
                    ? DeviceIdentityStore.GetDesktopDisplayName()
                    : stateEntry.UpdatedByDeviceName,
                BaseRevision = stateEntry.BaseRevision,
                BasePackageHash = stateEntry.BasePackageHash,
                BaseDeleted = stateEntry.BaseDeleted,
                BasePurged = stateEntry.BasePurged,
                Deleted = true,
                Purged = stateEntry.Purged,
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
            Revision = index.Revision,
            UpdatedAtUtc = index.UpdatedAtUtc,
            UpdatedByDeviceId = index.UpdatedByDeviceId,
            UpdatedByDeviceName = index.UpdatedByDeviceName,
            Items = index.Items.Select(item => new WebDavSyncEntry
            {
                ExtensionId = item.ExtensionId,
                Title = item.Title,
                Category = item.Category,
                Version = item.Version,
                PackageHash = item.PackageHash,
                PackagePath = item.PackagePath,
                UpdatedAtUtc = item.UpdatedAtUtc,
                Revision = item.Revision,
                UpdatedByDeviceId = item.UpdatedByDeviceId,
                UpdatedByDeviceName = item.UpdatedByDeviceName,
                Deleted = item.Deleted,
                Purged = item.Purged,
                LocalDeletionPending = false,
                BaseRevision = item.Revision,
                BasePackageHash = item.PackageHash,
                BaseDeleted = item.Deleted,
                BasePurged = item.Purged
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
        return $"deleted={entry.Deleted},purged={entry.Purged},pendingDelete={entry.LocalDeletionPending},updated={entry.UpdatedAtUtc},hash={hash},path={entry.PackagePath}";
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
            SchemaVersion = 2,
            Revision = index.Revision,
            UpdatedAtUtc = index.UpdatedAtUtc,
            UpdatedByDeviceId = index.UpdatedByDeviceId,
            UpdatedByDeviceName = index.UpdatedByDeviceName,
            Items = index.Items.Select(item => new WebDavSyncEntry
            {
                ExtensionId = item.ExtensionId,
                Title = item.Title,
                Category = item.Category,
                Version = item.Version,
                PackageHash = item.PackageHash,
                PackagePath = item.PackagePath,
                UpdatedAtUtc = item.UpdatedAtUtc,
                Revision = item.Revision,
                UpdatedByDeviceId = item.UpdatedByDeviceId,
                UpdatedByDeviceName = item.UpdatedByDeviceName,
                Deleted = item.Deleted,
                Purged = item.Purged
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

    private async Task CleanupPurgedRemotePackagesAsync(WebDavSyncIndex index, CancellationToken cancellationToken)
    {
        foreach (var item in index.Items.Where(entry => entry.Purged))
        {
            await DeleteRemotePackageTreeAsync(item.ExtensionId, cancellationToken);
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

        // 确保整个目录树存在（packages 和 packages/{extensionId}）
        await EnsureCollectionTreeAsync(cancellationToken, "packages", entry.ExtensionId);
        
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
        if (entry.Purged)
        {
            var changed = false;
            if (Directory.Exists(localDirectory))
            {
                Directory.Delete(localDirectory, recursive: true);
                changed = true;
            }

            if (ExtensionRecycleBinService.PurgeAllByExtensionId(entry.ExtensionId) > 0)
            {
                changed = true;
            }

            return changed;
        }

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
        // 验证上传的文件，如果失败则重试
        const int maxRetries = 3;
        const int retryDelayMs = 500;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
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
                return; // 验证成功
            }
            catch (FileNotFoundException) when (attempt < maxRetries)
            {
                // 文件可能还在同步中，等待后重试
                HostAssets.AppendLog($"WebDAV verify retry {attempt}/{maxRetries}: {entry.PackagePath} not found yet, waiting...");
                await Task.Delay(retryDelayMs * attempt, cancellationToken);
            }
        }
        
        // 所有重试都失败，抛出异常
        throw new FileNotFoundException($"WebDAV 上传验证失败：{entry.PackagePath} 上传后无法读取，可能是服务器延迟或权限问题。", entry.PackagePath);
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
        const int maxRetries = 3;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
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

                // 503错误：频率限制
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (responseBody.Contains("Too many requests") || responseBody.Contains("BlockedTemporarily"))
                    {
                        if (attempt < maxRetries)
                        {
                            var delay = attempt * 2000; // 2秒、4秒、6秒
                            HostAssets.AppendLog($"WebDAV rate limited, retrying in {delay}ms (attempt {attempt}/{maxRetries})");
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }

                        throw new InvalidOperationException(
                            "坚果云频率限制：请求过于频繁，账号已被临时封禁。\n\n" +
                            "请等待10-30分钟后再试，期间不要进行任何WebDAV操作。");
                    }
                }

                await ThrowWebDavFailureAsync(request, response, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                throw; // 重新抛出我们自己的异常
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delay = attempt * 1000;
                HostAssets.AppendLog($"WebDAV request failed, retrying in {delay}ms (attempt {attempt}/{maxRetries}): {ex.Message}");
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException($"WebDAV 目录创建失败：{relativePath}");
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

    private async Task<WebDavRemoteFileInfo?> TryGetRemoteFileInfoAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(new HttpMethod("PROPFIND"), relativePath);
        request.Headers.Add("Depth", "0");
        request.Content = new StringContent(
            """
<?xml version="1.0" encoding="utf-8" ?>
<propfind xmlns="DAV:">
  <prop>
    <getlastmodified />
    <getcontentlength />
  </prop>
</propfind>
""",
            Encoding.UTF8,
            "application/xml");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.StatusCode != HttpStatusCode.MultiStatus && response.StatusCode != HttpStatusCode.OK)
        {
            await ThrowWebDavFailureAsync(request, response, cancellationToken);
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            XNamespace dav = "DAV:";
            var document = XDocument.Parse(xml);
            var prop = document.Descendants(dav + "prop").FirstOrDefault();
            var modifiedText = prop?.Element(dav + "getlastmodified")?.Value;
            var lengthText = prop?.Element(dav + "getcontentlength")?.Value;
            var modifiedUtc = DateTimeOffset.TryParse(modifiedText, out var modified)
                ? modified.UtcDateTime
                : DateTime.MinValue;
            var length = long.TryParse(lengthText, out var parsedLength) ? parsedLength : 0;
            return modifiedUtc == DateTime.MinValue
                ? null
                : new WebDavRemoteFileInfo(modifiedUtc, length);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"WebDAV remote file metadata parse failed: path={relativePath}, error={ex.Message}");
            return null;
        }
    }

    private async Task DeleteRemotePackageTreeAsync(string extensionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        var relativePath = $"packages/{extensionId}";
        using var request = CreateRequest(HttpMethod.Delete, relativePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            HostAssets.AppendLog($"WebDAV purged remote package tree: id={extensionId}, path={relativePath}, status={(int)response.StatusCode}");
            return;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            HostAssets.AppendLog($"WebDAV remote package tree missing parent or already removed: id={extensionId}, path={relativePath}");
            return;
        }

        await ThrowWebDavFailureAsync(request, response, cancellationToken);
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
        return ExtensionSyncRevision.Compare(left, right);
    }

    private static bool EntriesEquivalent(WebDavSyncEntry left, WebDavSyncEntry right)
    {
        return left.ExtensionId.Equals(right.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Title, right.Title, StringComparison.Ordinal) &&
               string.Equals(left.Category, right.Category, StringComparison.Ordinal) &&
               left.Version.Equals(right.Version, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.PackageHash, right.PackageHash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.PackagePath, right.PackagePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.UpdatedAtUtc, right.UpdatedAtUtc, StringComparison.Ordinal) &&
               left.Revision == right.Revision &&
               string.Equals(left.UpdatedByDeviceId, right.UpdatedByDeviceId, StringComparison.OrdinalIgnoreCase) &&
               left.Deleted == right.Deleted &&
               left.Purged == right.Purged;
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

    private static string BuildExtensionDataPath(string extensionId, string key)
    {
        return $"appdata/{NormalizeRelativePath(extensionId)}/{NormalizeRelativePath(key)}";
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

    private sealed record WebDavRemoteFileInfo(DateTime LastModifiedUtc, long ContentLength);
}

public sealed record WebDavSyncResult(
    int UploadedCount,
    int PulledCount,
    string RemoteRoot,
    bool ConfigUploaded = false,
    bool ConfigPulled = false);

public sealed record WebDavYanmStateSyncResult(
    bool Uploaded,
    bool Pulled,
    string RemoteRoot,
    DateTime UpdatedAtUtc,
    int PayloadBytes);

public sealed class WebDavLauncherConfigSnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public CloudQuickPanelConfigSnapshot? Config { get; set; }
}

public sealed class WebDavYanmStateSnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public YanmSettings? Yanm { get; set; }
}

public sealed class WebDavSyncIndex
{
    public int SchemaVersion { get; set; } = 2;

    public long Revision { get; set; }

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public string? UpdatedByDeviceId { get; set; }

    public string? UpdatedByDeviceName { get; set; }

    public List<WebDavSyncEntry> Items { get; set; } = [];
}

public sealed class WebDavSyncEntry
{
    public string ExtensionId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Version { get; set; } = "0.1.0";

    public string PackageHash { get; set; } = string.Empty;

    public string PackagePath { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public long Revision { get; set; }

    public string? UpdatedByDeviceId { get; set; }

    public string? UpdatedByDeviceName { get; set; }

    public bool Deleted { get; set; }

    public bool Purged { get; set; }

    public bool LocalDeletionPending { get; set; }

    // 以下基线字段只保存在本机索引，用于识别同一扩展的双端并发修改。
    public long BaseRevision { get; set; }

    public string BasePackageHash { get; set; } = string.Empty;

    public bool BaseDeleted { get; set; }

    public bool BasePurged { get; set; }
}
