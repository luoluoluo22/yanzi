using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed class PersonalSyncService
{
    private const string RemoteIndexPath = "index.json";
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private readonly IPersonalSyncBackend _backend;

    public PersonalSyncService(AppSettings settings, bool requireEnabled = true)
    {
        _backend = PersonalSyncBackendFactory.Create(settings, requireEnabled)
            ?? throw new InvalidOperationException("个人同步未完整配置。");
    }

    public string SyncRootDisplay => _backend.DisplayRoot;

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        await RunExclusiveAsync(
            "probe",
            async () =>
            {
                await ProbeCoreAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<string?> TryReadExtensionDataTextAsync(string extensionId, string key, CancellationToken cancellationToken = default)
    {
        var bytes = await _backend.TryReadBytesAsync(BuildExtensionDataPath(extensionId, key), cancellationToken);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    public Task WriteExtensionDataTextAsync(string extensionId, string key, string content, CancellationToken cancellationToken = default)
    {
        return _backend.WriteBytesAsync(
            BuildExtensionDataPath(extensionId, key),
            Encoding.UTF8.GetBytes(content ?? string.Empty),
            "text/plain; charset=utf-8",
            cancellationToken);
    }

    public async Task<WebDavSyncResult> SyncExtensionsAsync(CancellationToken cancellationToken = default)
    {
        return await RunExclusiveAsync(
            "sync-extensions",
            () => SyncExtensionsCoreAsync(cancellationToken),
            cancellationToken);
    }

    public async Task<WebDavYanmStateSyncResult> SyncYanmStateAsync(CancellationToken cancellationToken = default)
    {
        return await RunExclusiveAsync(
            "sync-yanm-state",
            () => SyncYanmStateCoreAsync(cancellationToken),
            cancellationToken);
    }

    public async Task ClearCloudAsync(CancellationToken cancellationToken = default)
    {
        await RunExclusiveAsync(
            "clear-cloud",
            () => ClearCloudCoreAsync(cancellationToken),
            cancellationToken);
    }

    private async Task ProbeCoreAsync(CancellationToken cancellationToken)
    {
        CloudSyncDiagnostics.Log("PersonalSyncService", "Probe started", ("root", SyncRootDisplay));
        await _backend.ProbeAsync(cancellationToken);
        CloudSyncDiagnostics.Log("PersonalSyncService", "Probe completed", ("root", SyncRootDisplay));
    }

    private async Task<WebDavSyncResult> SyncExtensionsCoreAsync(CancellationToken cancellationToken)
    {
        CloudSyncDiagnostics.Log("PersonalSyncService", "Extension sync started", ("root", SyncRootDisplay));
        await ProbeCoreAsync(cancellationToken);

        var remoteIndex = await LoadRemoteIndexAsync(cancellationToken);
        var localState = LoadLocalIndex();
        var snapshot = BuildLocalSnapshot(localState);
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

                try
                {
                    if (await ApplyRemoteEntryAsync(remoteEntry, cancellationToken))
                    {
                        pulled++;
                    }
                }
                catch (FileNotFoundException ex)
                {
                    HostAssets.AppendLog($"Personal sync remote package missing; removing stale index entry: id={remoteEntry.ExtensionId}, path={remoteEntry.PackagePath}, error={ex.Message}");
                    remoteIndexChanged = true;
                    continue;
                }

                mergedMap[extensionId] = remoteEntry;
                continue;
            }

            if (remoteEntry == null)
            {
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
                catch (FileNotFoundException ex)
                {
                    HostAssets.AppendLog($"Personal sync remote package missing; keeping local entry and repairing remote index: id={winner.ExtensionId}, path={winner.PackagePath}, error={ex.Message}");
                    if (localEntry is { Deleted: false })
                    {
                        await UploadPackageIfNeededAsync(localEntry, snapshot.PackageBytesByExtensionId, cancellationToken);
                        uploaded++;
                        winner = localEntry;
                    }
                    else
                    {
                        remoteIndexChanged = true;
                        continue;
                    }
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

        await CleanupPurgedRemotePackagesAsync(mergedIndex, cancellationToken);
        SaveLocalIndex(ClearLocalPendingFlags(mergedIndex));
        var preferRemoteConfigOnConflict = pulled > 0 && uploaded == 0;
        var (configUploaded, configPulled) = await SyncLauncherConfigAsync(preferRemoteConfigOnConflict, cancellationToken);
        CloudSyncDiagnostics.Log(
            "PersonalSyncService",
            "Extension sync completed",
            ("uploaded", uploaded),
            ("pulled", pulled),
            ("configUploaded", configUploaded),
            ("configPulled", configPulled),
            ("preferRemoteConfigOnConflict", preferRemoteConfigOnConflict),
            ("root", SyncRootDisplay));
        return new WebDavSyncResult(uploaded, pulled, SyncRootDisplay, configUploaded, configPulled);
    }

    private async Task<WebDavYanmStateSyncResult> SyncYanmStateCoreAsync(CancellationToken cancellationToken)
    {
        CloudSyncDiagnostics.Log("PersonalSyncService", "Yanm state sync started", ("root", SyncRootDisplay));
        await ProbeCoreAsync(cancellationToken);

        var localSettings = AppSettingsStore.Load();
        localSettings.Yanm ??= new YanmSettings();
        localSettings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localYanm = CloneByJson(localSettings.Yanm);
        var localUpdatedAtUtc = GetLocalYanmUpdatedAtUtc(localSettings);
        var remoteBytes = await _backend.TryReadBytesAsync("state/yanm-state.json", cancellationToken);
        var remote = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<WebDavYanmStateSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions)
            : null;

        if (remote?.Yanm == null)
        {
            var legacyRemote = await TryLoadLegacyRemoteYanmStateAsync(cancellationToken);
            if (legacyRemote?.Yanm != null)
            {
                var legacyUpdatedAtUtc = TryParseUtc(legacyRemote.UpdatedAtUtc) ?? DateTime.MinValue;
                if (legacyUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1) || !HasYanmComponentState(localYanm))
                {
                    ApplyYanmStateSnapshot(legacyRemote.Yanm, legacyUpdatedAtUtc);
                    await UploadYanmStateAsync(legacyRemote.Yanm, legacyUpdatedAtUtc, cancellationToken);
                    return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, legacyUpdatedAtUtc, remoteBytes?.Length ?? 0);
                }
            }

            var uploadedAtUtc = DateTime.UtcNow;
            await UploadYanmStateAsync(localYanm, uploadedAtUtc, cancellationToken);
            SaveYanmStateUpdatedAtUtc(uploadedAtUtc);
            var uploadedBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = uploadedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
            return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, uploadedAtUtc, uploadedBytes);
        }

        var remoteUpdatedAtUtc = TryParseUtc(remote.UpdatedAtUtc) ?? DateTime.MinValue;
        var equivalent = AreJsonPayloadsEqual(localYanm, remote.Yanm);
        var payloadBytes = remoteBytes?.Length ?? Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(remote, JsonOptions));
        HostAssets.AppendLog(
            $"Personal sync Yanm state compare: equivalent={equivalent}, localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc:O}, local={DescribeYanmForSync(localYanm)}, remote={DescribeYanmForSync(remote.Yanm)}");
        if (equivalent)
        {
            if (localUpdatedAtUtc == DateTime.MinValue && remoteUpdatedAtUtc > DateTime.MinValue)
            {
                SaveYanmStateUpdatedAtUtc(remoteUpdatedAtUtc);
            }

            return new WebDavYanmStateSyncResult(false, false, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        if (remoteUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1) || localUpdatedAtUtc == DateTime.MinValue)
        {
            ApplyYanmStateSnapshot(remote.Yanm, remoteUpdatedAtUtc);
            return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        if (!HasYanmComponentState(localYanm) && HasYanmComponentState(remote.Yanm))
        {
            HostAssets.AppendLog(
                $"Personal sync Yanm state pulled to protect remote component data: localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc:O}, local={DescribeYanmForSync(localYanm)}, remote={DescribeYanmForSync(remote.Yanm)}");
            ApplyYanmStateSnapshot(remote.Yanm, remoteUpdatedAtUtc);
            return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        var updatedAtUtc = DateTime.UtcNow;
        HostAssets.AppendLog(
            $"Personal sync Yanm state upload selected: local wins, localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc:O}, local={DescribeYanmForSync(localYanm)}, remote={DescribeYanmForSync(remote.Yanm)}");
        await UploadYanmStateAsync(localYanm, updatedAtUtc, cancellationToken);
        SaveYanmStateUpdatedAtUtc(updatedAtUtc);
        var uploadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = updatedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
        return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, updatedAtUtc, uploadBytes);
    }

    private async Task<bool> ClearCloudCoreAsync(CancellationToken cancellationToken)
    {
        await ProbeCoreAsync(cancellationToken);
        var remoteIndex = await LoadRemoteIndexAsync(cancellationToken);
        foreach (var entry in remoteIndex.Items)
        {
            if (!string.IsNullOrWhiteSpace(entry.PackagePath))
            {
                await _backend.DeleteFileAsync(entry.PackagePath, cancellationToken);
            }
        }

        await _backend.DeleteFileAsync(RemoteIndexPath, cancellationToken);
        await _backend.DeleteFileAsync("state/launcher-config.json", cancellationToken);
        await _backend.DeleteFileAsync("state/yanm-state.json", cancellationToken);
        foreach (var definition in LauncherConfigObjectStore.Definitions)
        {
            await _backend.DeleteFileAsync(LauncherConfigObjectStore.GetPath(definition.ObjectId), cancellationToken);
        }

        if (File.Exists(HostAssets.WebDavSyncStatePath))
        {
            File.Delete(HostAssets.WebDavSyncStatePath);
        }

        return true;
    }

    private async Task<T> RunExclusiveAsync<T>(string operation, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            CloudSyncDiagnostics.Log("PersonalSyncService", "Operation waiting", ("operation", operation), ("root", SyncRootDisplay));
        }
        catch { }

        await OperationLock.WaitAsync(cancellationToken);

        try
        {
            try
            {
                CloudSyncDiagnostics.Log("PersonalSyncService", "Operation acquired", ("operation", operation), ("root", SyncRootDisplay));
            }
            catch { }

            return await action();
        }
        finally
        {
            OperationLock.Release();
            try
            {
                CloudSyncDiagnostics.Log("PersonalSyncService", "Operation released", ("operation", operation), ("root", SyncRootDisplay));
            }
            catch { }
        }
    }

    private async Task<(bool uploaded, bool pulled)> SyncLauncherConfigAsync(bool preferRemoteOnConflict, CancellationToken cancellationToken)
    {
        var localSettings = AppSettingsStore.Load();
        var localConfig = CloudQuickPanelConfigSnapshot.FromSettings(localSettings);
        var explicitLocalUpdatedAtUtc = TryParseUtc(localSettings.LauncherConfigUpdatedAtUtc);
        var legacyLocalUpdatedAtUtc = explicitLocalUpdatedAtUtc == null && HasMeaningfulLauncherConfig(localConfig) && File.Exists(AppSettingsStore.SettingsPath)
            ? File.GetLastWriteTimeUtc(AppSettingsStore.SettingsPath)
            : (DateTime?)null;
        var localUpdatedAtUtc = explicitLocalUpdatedAtUtc ?? legacyLocalUpdatedAtUtc ?? DateTime.MinValue;
        var remoteBytes = await _backend.TryReadBytesAsync("state/launcher-config.json", cancellationToken);
        var remote = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<WebDavLauncherConfigSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions)
            : null;
        var hasRemoteObjects = await HasLauncherConfigObjectsAsync(cancellationToken);
        remote = hasRemoteObjects
            ? await TryLoadLauncherConfigObjectsAsync(remote?.Config, cancellationToken) ?? remote
            : remote;

        if (remote?.Config == null)
        {
            if (CloudQuickPanelConfigSnapshot.IsInitialDefaultSnapshot(localConfig))
            {
                HostAssets.AppendLog("Personal sync launcher config upload skipped: local snapshot has no user content and remote is missing.");
                return (false, false);
            }

            var uploadedAtUtc = DateTime.UtcNow;
            if (!await UploadLauncherConfigAsync(localConfig, uploadedAtUtc, cancellationToken))
            {
                return (false, false);
            }

            SaveLauncherConfigUpdatedAtUtc(uploadedAtUtc);
            return (true, false);
        }

        var remoteUpdatedAtUtc = TryParseUtc(remote.UpdatedAtUtc) ?? DateTime.MinValue;

        // 排除 UpdatedAtUtc 时间戳在配置完全等价性检测中的虚假干扰
        var localTime = localConfig.UpdatedAtUtc;
        var remoteTime = remote.Config.UpdatedAtUtc;
        localConfig.UpdatedAtUtc = null;
        remote.Config.UpdatedAtUtc = null;
        var localMetadata = CaptureMetadata(localConfig);
        var remoteMetadata = CaptureMetadata(remote.Config);
        ClearMetadataForComparison(localConfig);
        ClearMetadataForComparison(remote.Config);

        var equivalent = AreJsonPayloadsEqual(localConfig, remote.Config);

        // 还原时间戳以保证后续时间戳判断逻辑正确
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
                HostAssets.AppendLog("Personal sync launcher config objects backfilled from legacy snapshot.");
                return (true, false);
            }

            // 如果内容完全等价，但本地时间戳与云端存在偏差，则直接将本地时间戳同步为云端时间戳，以防以后再次触发多余的重复判断
            if (remote != null && localSettings.LauncherConfigUpdatedAtUtc != remote.UpdatedAtUtc)
            {
                SaveLauncherConfigUpdatedAtUtc(remoteUpdatedAtUtc);
            }

            return (false, false);
        }

        if (preferRemoteOnConflict && HasMeaningfulLauncherConfig(remote.Config))
        {
            HostAssets.AppendLog(
                $"Personal sync launcher config preferred remote after package pull: localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc:O}, remoteSource={remote.Config.SourceDeviceName ?? remote.Config.SourceDeviceId ?? "unknown"}");
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            return (false, true);
        }

        if (explicitLocalUpdatedAtUtc == null && legacyLocalUpdatedAtUtc == null)
        {
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            return (false, true);
        }

        if (remoteUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1))
        {
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            return (false, true);
        }

        if (IsLikelyFreshLocalLauncherConfig(localConfig) && HasMeaningfulLauncherConfig(remote.Config))
        {
            ApplyLauncherConfigSnapshot(remote.Config, remoteUpdatedAtUtc);
            return (false, true);
        }

        var updatedAtUtc = DateTime.UtcNow;
        if (!await UploadLauncherConfigAsync(localConfig, updatedAtUtc, cancellationToken))
        {
            return (false, false);
        }

        SaveLauncherConfigUpdatedAtUtc(updatedAtUtc);
        return (true, false);
    }

    private async Task<WebDavYanmStateSnapshot?> TryLoadLegacyRemoteYanmStateAsync(CancellationToken cancellationToken)
    {
        var remoteBytes = await _backend.TryReadBytesAsync("state/launcher-config.json", cancellationToken);
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

    private async Task<bool> UploadLauncherConfigAsync(CloudQuickPanelConfigSnapshot config, DateTime updatedAtUtc, CancellationToken cancellationToken)
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
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
        var writes = LauncherConfigObjectStore.PrepareWrites(config, updatedAtUtc);
        var changedWrites = new List<LauncherConfigObjectWrite>();
        var effectiveWrites = new List<LauncherConfigObjectWrite>();
        foreach (var write in writes)
        {
            var remoteBytes = await _backend.TryReadBytesAsync(write.Path, cancellationToken);
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
            HostAssets.AppendLog("Personal sync launcher config upload skipped: all object payloads are unchanged.");
            return false;
        }

        foreach (var write in changedWrites)
        {
            HostAssets.AppendLog($"Personal sync write started: path={write.Path}, bytes={write.Bytes.Length}, contentType=application/json");
            await _backend.WriteBytesAsync(write.Path, write.Bytes, "application/json", cancellationToken);
        }

        var manifestBytes = LauncherConfigObjectStore.SerializeManifest(
            LauncherConfigObjectStore.CreateManifest(effectiveWrites, updatedAtUtc));
        HostAssets.AppendLog($"Personal sync write started: path={LauncherConfigObjectStore.ManifestPath}, bytes={manifestBytes.Length}, contentType=application/json");
        await _backend.WriteBytesAsync(LauncherConfigObjectStore.ManifestPath, manifestBytes, "application/json", cancellationToken);

        var changeBytes = LauncherConfigObjectStore.SerializeChangeSet(
            LauncherConfigObjectStore.CreateChangeSet(changedWrites, updatedAtUtc, "launcher-config-sync"));
        var changePath = LauncherConfigObjectStore.GetChangePath(updatedAtUtc);
        HostAssets.AppendLog($"Personal sync write started: path={changePath}, bytes={changeBytes.Length}, contentType=application/json");
        await _backend.WriteBytesAsync(changePath, changeBytes, "application/json", cancellationToken);

        HostAssets.AppendLog($"Personal sync write started: path=state/launcher-config.json, bytes={bytes.Length}, contentType=application/json");
        await _backend.WriteBytesAsync("state/launcher-config.json", bytes, "application/json", cancellationToken);
        HostAssets.AppendLog($"Personal sync launcher config uploaded: changedObjects={changedWrites.Count}, totalObjects={effectiveWrites.Count}");
        return true;
    }

    private async Task<WebDavLauncherConfigSnapshot?> TryLoadLauncherConfigObjectsAsync(
        CloudQuickPanelConfigSnapshot? baseSnapshot,
        CancellationToken cancellationToken)
    {
        var objects = new List<LauncherConfigObjectEnvelope>();
        foreach (var definition in LauncherConfigObjectStore.Definitions)
        {
            var bytes = await _backend.TryReadBytesAsync(LauncherConfigObjectStore.GetPath(definition.ObjectId), cancellationToken);
            var obj = LauncherConfigObjectStore.Deserialize(bytes);
            if (obj != null)
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
            var bytes = await _backend.TryReadBytesAsync(LauncherConfigObjectStore.GetPath(definition.ObjectId), cancellationToken);
            if (bytes is { Length: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    private async Task UploadYanmStateAsync(YanmSettings yanm, DateTime updatedAtUtc, CancellationToken cancellationToken)
    {
        var remoteBytes = await _backend.TryReadBytesAsync("state/yanm-state.json", cancellationToken);
        if (remoteBytes is { Length: > 0 })
        {
            try
            {
                var remote = JsonSerializer.Deserialize<WebDavYanmStateSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions);
                if (remote?.Yanm != null && AreJsonPayloadsEqual(yanm, remote.Yanm))
                {
                    HostAssets.AppendLog($"Personal sync Yanm state upload skipped: remote content is unchanged except metadata, local={DescribeYanmForSync(yanm)}, remoteUpdated={remote.UpdatedAtUtc}");
                    return;
                }
            }
            catch (JsonException ex)
            {
                HostAssets.AppendLog($"Personal sync Yanm state unchanged check skipped: remote JSON parse failed: {ex.Message}");
            }
        }

        var snapshot = new WebDavYanmStateSnapshot
        {
            UpdatedAtUtc = updatedAtUtc.ToString("O"),
            Yanm = CloneByJson(yanm)
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
        HostAssets.AppendLog($"Personal sync write started: path=state/yanm-state.json, bytes={bytes.Length}, contentType=application/json, yanm={DescribeYanmForSync(yanm)}");
        await _backend.WriteBytesAsync("state/yanm-state.json", bytes, "application/json", cancellationToken);
    }

    private static void ApplyLauncherConfigSnapshot(CloudQuickPanelConfigSnapshot snapshot, DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        var incoming = snapshot.ToAppSettings();
        settings.ThemeMode = incoming.ThemeMode;
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

        if (snapshot.Yanm != null)
        {
            settings.Yanm = incoming.Yanm;
            settings.YanmStateUpdatedAtUtc = string.IsNullOrWhiteSpace(settings.YanmStateUpdatedAtUtc)
                ? updatedAtUtc.ToString("O")
                : settings.YanmStateUpdatedAtUtc;
        }

        if (HasAiConfigPayload(snapshot))
        {
            settings.AiBaseUrl = incoming.AiBaseUrl;
            settings.AiApiKey = incoming.AiApiKey;
            settings.AiModel = incoming.AiModel;
            settings.AiSystemPrompt = incoming.AiSystemPrompt;
            settings.AiServiceProviders = incoming.AiServiceProviders;
            settings.ActiveServiceProviderId = incoming.ActiveServiceProviderId;
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

    private static T CloneByJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? value;
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
        if (left.TriggerCtrlRightClick != right.TriggerCtrlRightClick) return false;
        if (left.TriggerCtrlMiddleClick != right.TriggerCtrlMiddleClick) return false;
        if (left.MouseTriggerMode != right.MouseTriggerMode) return false;
        if (left.DragThresholdPixels != right.DragThresholdPixels) return false;
        if (left.HoldDelayMilliseconds != right.HoldDelayMilliseconds) return false;
        if (left.GridSizePixels != right.GridSizePixels) return false;
        if (Math.Abs(left.OverlayOpacity - right.OverlayOpacity) > 0.001) return false;
        if (left.HasInitializedDefaultComponents != right.HasInitializedDefaultComponents) return false;
        if (left.DefaultComponentVersion != right.DefaultComponentVersion) return false;

        // Lists
        if (!AreListsEqual(left.WhitelistedProcesses, right.WhitelistedProcesses)) return false;
        if (!AreListsEqual(left.BlacklistedProcesses, right.BlacklistedProcesses)) return false;

        // Components
        if (!AreComponentsListsEqual(left.Components, right.Components)) return false;

        // ComponentState dictionary
        if (!AreDictionariesEqual(left.ComponentState, right.ComponentState)) return false;

        return true;
    }

    private static bool AreListsEqual(List<string>? left, List<string>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if ((left == null || left.Count == 0) && (right == null || right.Count == 0)) return true;
        if (left == null || right == null) return false;
        if (left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++)
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
        for (int i = 0; i < left.Count; i++)
        {
            var leftJson = JsonSerializer.Serialize(left[i], JsonOptions);
            var rightJson = JsonSerializer.Serialize(right[i], JsonOptions);
            if (leftJson != rightJson) return false;
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

    private static string DescribeYanmForSync(YanmSettings? yanm)
    {
        if (yanm == null)
        {
            return "null";
        }

        var json = JsonSerializer.Serialize(yanm, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).Substring(0, 12).ToLowerInvariant();
        return $"hash={hash}, enabled={yanm.Enabled}, activation={yanm.ActivationKey}, components={yanm.Components?.Count ?? 0}, stateKeys={yanm.ComponentState?.Count ?? 0}, defaultVersion={yanm.DefaultComponentVersion}";
    }


    private static bool HasMeaningfulLauncherConfig(CloudQuickPanelConfigSnapshot config)
    {
        return CloudQuickPanelConfigSnapshot.HasMeaningfulUserContent(config);
    }

    private static bool IsLikelyFreshLocalLauncherConfig(CloudQuickPanelConfigSnapshot config)
    {
        return CloudQuickPanelConfigSnapshot.IsInitialDefaultSnapshot(config);
    }

    private static bool HasAiConfigPayload(CloudQuickPanelConfigSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.AiBaseUrl) ||
               !string.IsNullOrWhiteSpace(snapshot.AiApiKey) ||
               !string.IsNullOrWhiteSpace(snapshot.AiModel) ||
               !string.IsNullOrWhiteSpace(snapshot.AiSystemPrompt) ||
               !string.IsNullOrWhiteSpace(snapshot.ActiveServiceProviderId) ||
               snapshot.AiServiceProviders.Count > 0;
    }

    private static bool HasYanmComponentState(YanmSettings? yanm)
    {
        return yanm?.ComponentState?.Count > 0;
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
        File.WriteAllText(HostAssets.WebDavSyncStatePath, JsonSerializer.Serialize(index, JsonOptions));
    }

    private LocalSnapshot BuildLocalSnapshot(WebDavSyncIndex localState)
    {
        var stateMap = localState.Items.ToDictionary(item => item.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var items = new List<WebDavSyncEntry>();
        var packageBytesByExtensionId = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in LocalExtensionCatalog.LoadCommands())
        {
            if (string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) || !Directory.Exists(command.ExtensionDirectoryPath))
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
                Title = command.Title,
                Category = command.Category ?? "扩展",
                Version = command.DeclaredVersion,
                PackageHash = packageHash,
                PackagePath = packagePath,
                UpdatedAtUtc = updatedAtUtc,
                Deleted = false
            };
            items.Add(entry);
            packageBytesByExtensionId[command.ExtensionId] = packageBytes;
        }

        foreach (var stateEntry in stateMap.Values)
        {
            if (existingIds.Contains(stateEntry.ExtensionId))
            {
                continue;
            }

            if (!stateEntry.LocalDeletionPending)
            {
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
                Deleted = true,
                Purged = stateEntry.Purged,
                LocalDeletionPending = stateEntry.LocalDeletionPending
            });
        }

        return new LocalSnapshot(items, packageBytesByExtensionId);
    }

    private async Task<WebDavSyncIndex> LoadRemoteIndexAsync(CancellationToken cancellationToken)
    {
        var bytes = await _backend.TryReadBytesAsync(RemoteIndexPath, cancellationToken);
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

    private Task SaveRemoteIndexAsync(WebDavSyncIndex index, CancellationToken cancellationToken)
    {
        var remoteIndex = new WebDavSyncIndex
        {
            SchemaVersion = index.SchemaVersion,
            Items = index.Items.Select(item => new WebDavSyncEntry
            {
                ExtensionId = item.ExtensionId,
                Title = item.Title,
                Category = item.Category,
                Version = item.Version,
                PackageHash = item.PackageHash,
                PackagePath = item.PackagePath,
                UpdatedAtUtc = item.UpdatedAtUtc,
                Deleted = item.Deleted,
                Purged = item.Purged
            }).ToList()
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(remoteIndex, JsonOptions));
        HostAssets.AppendLog($"Personal sync write started: path={RemoteIndexPath}, bytes={bytes.Length}, items={remoteIndex.Items.Count}, contentType=application/json");
        return _backend.WriteBytesAsync(RemoteIndexPath, bytes, "application/json", cancellationToken);
    }

    private async Task CleanupPurgedRemotePackagesAsync(WebDavSyncIndex index, CancellationToken cancellationToken)
    {
        foreach (var item in index.Items.Where(entry => entry.Purged && !string.IsNullOrWhiteSpace(entry.PackagePath)))
        {
            await _backend.DeleteFileAsync(item.PackagePath, cancellationToken);
        }
    }

    private async Task UploadPackageIfNeededAsync(WebDavSyncEntry entry, IReadOnlyDictionary<string, byte[]> packageBytesByExtensionId, CancellationToken cancellationToken)
    {
        if (!packageBytesByExtensionId.TryGetValue(entry.ExtensionId, out var bytes))
        {
            return;
        }

        HostAssets.AppendLog($"Personal sync package upload started: id={entry.ExtensionId}, path={entry.PackagePath}, bytes={bytes.Length}, hash={entry.PackageHash}");
        await _backend.WriteBytesAsync(entry.PackagePath, bytes, "application/zip", cancellationToken);
        byte[]? remoteBytes = null;
        for (int i = 0; i < 5; i++)
        {
            remoteBytes = await _backend.TryReadBytesAsync(entry.PackagePath, cancellationToken);
            if (remoteBytes != null)
            {
                break;
            }
            if (i < 4)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        if (remoteBytes == null)
        {
            throw new FileNotFoundException($"上传校验失败：{entry.PackagePath}");
        }
        var remoteHash = ComputeSha256(remoteBytes);
        if (!TryValidateZipArchive(remoteBytes, out var zipError) ||
            !string.Equals(remoteHash, entry.PackageHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"上传校验失败：{entry.PackagePath}，expectedHash={entry.PackageHash}，remoteHash={remoteHash}，zipError={zipError}");
        }

        HostAssets.AppendLog($"Personal sync package upload verified: id={entry.ExtensionId}, path={entry.PackagePath}, bytes={remoteBytes.Length}, hash={remoteHash}");
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

        var packageBytes = await _backend.TryReadBytesAsync(entry.PackagePath, cancellationToken)
            ?? throw new FileNotFoundException($"远端扩展包不存在：{entry.PackagePath}");
        if (!TryValidateZipArchive(packageBytes, out var packageError))
        {
            throw new InvalidDataException($"远端扩展包无效：{entry.PackagePath}，detail={packageError}");
        }

        await ReplaceDirectoryFromPackageAsync(Path.Combine(HostAssets.ExtensionsPath, entry.ExtensionId), packageBytes, cancellationToken);
        return true;
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
                Purged = item.Purged,
                LocalDeletionPending = false
            }).ToList()
        };
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

        if (left.Purged != right.Purged)
        {
            return left.Purged ? 1 : -1;
        }

        return string.Compare(left.PackageHash, right.PackageHash, StringComparison.OrdinalIgnoreCase);
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

    private static string BuildRemotePackagePath(string extensionId, string packageHash) => $"packages/{extensionId}/{packageHash}.zip";

    private static string BuildExtensionDataPath(string extensionId, string key) => $"appdata/{NormalizeRelativePath(extensionId)}/{NormalizeRelativePath(key)}";

    private static string NormalizeRelativePath(string value)
    {
        var normalized = (value ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("同步路径不能为空。");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("同步路径不能包含 . 或 ..。");
        }

        return string.Join("/", segments);
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool TryValidateZipArchive(byte[] bytes, out string error)
    {
        error = string.Empty;
        if (bytes.Length < 4)
        {
            error = "文件太短。";
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

    private static DateTimeOffset GetDirectoryLastWriteUtc(string path)
    {
        return Directory.Exists(path)
            ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(path))
            : DateTimeOffset.UtcNow;
    }

    private sealed record LocalSnapshot(IReadOnlyList<WebDavSyncEntry> Items, IReadOnlyDictionary<string, byte[]> PackageBytesByExtensionId);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
