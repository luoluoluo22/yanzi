using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public sealed class PersonalSyncService
{
    private const string RemoteIndexPath = "index.json";
    private readonly IPersonalSyncBackend _backend;

    public PersonalSyncService(AppSettings settings)
    {
        _backend = PersonalSyncBackendFactory.Create(settings)
            ?? throw new InvalidOperationException("个人同步未完整配置。");
    }

    public string SyncRootDisplay => _backend.DisplayRoot;

    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        await _backend.ProbeAsync(cancellationToken);
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
        await ProbeAsync(cancellationToken);

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

                if (await ApplyRemoteEntryAsync(remoteEntry, cancellationToken))
                {
                    pulled++;
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
                if (await ApplyRemoteEntryAsync(winner, cancellationToken))
                {
                    pulled++;
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
        var (configUploaded, configPulled) = await SyncLauncherConfigAsync(cancellationToken);
        return new WebDavSyncResult(uploaded, pulled, SyncRootDisplay, configUploaded, configPulled);
    }

    public async Task<WebDavYanmStateSyncResult> SyncYanmStateAsync(CancellationToken cancellationToken = default)
    {
        await ProbeAsync(cancellationToken);

        var localSettings = AppSettingsStore.Load();
        localSettings.Yanm ??= new YanmSettings();
        localSettings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localYanm = CloneByJson(localSettings.Yanm);
        var localUpdatedAtUtc = TryParseUtc(localSettings.LauncherConfigUpdatedAtUtc) ?? DateTime.MinValue;
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
            SaveLauncherConfigUpdatedAtUtc(uploadedAtUtc);
            var uploadedBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = uploadedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
            return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, uploadedAtUtc, uploadedBytes);
        }

        var remoteUpdatedAtUtc = TryParseUtc(remote.UpdatedAtUtc) ?? DateTime.MinValue;
        var equivalent = AreJsonPayloadsEqual(localYanm, remote.Yanm);
        var payloadBytes = remoteBytes?.Length ?? Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(remote, JsonOptions));
        if (equivalent)
        {
            if (localUpdatedAtUtc == DateTime.MinValue && remoteUpdatedAtUtc > DateTime.MinValue)
            {
                SaveLauncherConfigUpdatedAtUtc(remoteUpdatedAtUtc);
            }

            return new WebDavYanmStateSyncResult(false, false, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        if (remoteUpdatedAtUtc > localUpdatedAtUtc.AddSeconds(1) || localUpdatedAtUtc == DateTime.MinValue)
        {
            ApplyYanmStateSnapshot(remote.Yanm, remoteUpdatedAtUtc);
            return new WebDavYanmStateSyncResult(false, true, SyncRootDisplay, remoteUpdatedAtUtc, payloadBytes);
        }

        var updatedAtUtc = DateTime.UtcNow;
        await UploadYanmStateAsync(localYanm, updatedAtUtc, cancellationToken);
        SaveLauncherConfigUpdatedAtUtc(updatedAtUtc);
        var uploadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new WebDavYanmStateSnapshot { UpdatedAtUtc = updatedAtUtc.ToString("O"), Yanm = localYanm }, JsonOptions));
        return new WebDavYanmStateSyncResult(true, false, SyncRootDisplay, updatedAtUtc, uploadBytes);
    }

    public async Task ClearCloudAsync(CancellationToken cancellationToken = default)
    {
        await ProbeAsync(cancellationToken);
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
        if (File.Exists(HostAssets.WebDavSyncStatePath))
        {
            File.Delete(HostAssets.WebDavSyncStatePath);
        }
    }

    private async Task<(bool uploaded, bool pulled)> SyncLauncherConfigAsync(CancellationToken cancellationToken)
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

        if (remote?.Config == null)
        {
            var uploadedAtUtc = DateTime.UtcNow;
            await UploadLauncherConfigAsync(localConfig, uploadedAtUtc, cancellationToken);
            SaveLauncherConfigUpdatedAtUtc(uploadedAtUtc);
            return (true, false);
        }

        var remoteUpdatedAtUtc = TryParseUtc(remote.UpdatedAtUtc) ?? DateTime.MinValue;
        var equivalent = AreJsonPayloadsEqual(localConfig, remote.Config);
        if (equivalent)
        {
            if (explicitLocalUpdatedAtUtc == null && remoteUpdatedAtUtc > DateTime.MinValue)
            {
                SaveLauncherConfigUpdatedAtUtc(remoteUpdatedAtUtc);
            }

            return (false, false);
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

        var updatedAtUtc = DateTime.UtcNow;
        await UploadLauncherConfigAsync(localConfig, updatedAtUtc, cancellationToken);
        SaveLauncherConfigUpdatedAtUtc(updatedAtUtc);
        return (true, false);
    }

    private async Task<WebDavYanmStateSnapshot?> TryLoadLegacyRemoteYanmStateAsync(CancellationToken cancellationToken)
    {
        var remoteBytes = await _backend.TryReadBytesAsync("state/launcher-config.json", cancellationToken);
        var remote = remoteBytes is { Length: > 0 }
            ? JsonSerializer.Deserialize<WebDavLauncherConfigSnapshot>(Encoding.UTF8.GetString(remoteBytes), JsonOptions)
            : null;
        return remote?.Config?.Yanm == null
            ? null
            : new WebDavYanmStateSnapshot
            {
                UpdatedAtUtc = remote.UpdatedAtUtc,
                Yanm = remote.Config.Yanm
            };
    }

    private Task UploadLauncherConfigAsync(CloudQuickPanelConfigSnapshot config, DateTime updatedAtUtc, CancellationToken cancellationToken)
    {
        var snapshot = new WebDavLauncherConfigSnapshot
        {
            UpdatedAtUtc = updatedAtUtc.ToString("O"),
            Config = config
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
        HostAssets.AppendLog($"Personal sync write started: path=state/launcher-config.json, bytes={bytes.Length}, contentType=application/json");
        return _backend.WriteBytesAsync("state/launcher-config.json", bytes, "application/json", cancellationToken);
    }

    private Task UploadYanmStateAsync(YanmSettings yanm, DateTime updatedAtUtc, CancellationToken cancellationToken)
    {
        var snapshot = new WebDavYanmStateSnapshot
        {
            UpdatedAtUtc = updatedAtUtc.ToString("O"),
            Yanm = CloneByJson(yanm)
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, JsonOptions));
        HostAssets.AppendLog($"Personal sync write started: path=state/yanm-state.json, bytes={bytes.Length}, contentType=application/json");
        return _backend.WriteBytesAsync("state/yanm-state.json", bytes, "application/json", cancellationToken);
    }

    private static void ApplyLauncherConfigSnapshot(CloudQuickPanelConfigSnapshot snapshot, DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        var incoming = snapshot.ToAppSettings();
        settings.QuickPanelSlots = incoming.QuickPanelSlots;
        settings.QuickPanelGlobalGroups = incoming.QuickPanelGlobalGroups;
        settings.QuickPanelContextGroups = incoming.QuickPanelContextGroups;
        settings.SelectedQuickPanelGlobalGroupId = incoming.SelectedQuickPanelGlobalGroupId;
        settings.SelectedQuickPanelContextGroupId = incoming.SelectedQuickPanelContextGroupId;
        settings.GlobalFavoriteExtensionIds = incoming.GlobalFavoriteExtensionIds;
        settings.ContextFavoriteExtensionIds = incoming.ContextFavoriteExtensionIds;
        settings.QuickPanelMouseTriggers = incoming.QuickPanelMouseTriggers;
        settings.MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(incoming.MouseGestureTriggerMode);
        settings.WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(incoming.WindowSnapAssistMouseTriggerMode);
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
        }

        if (HasAiConfigPayload(snapshot))
        {
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
        settings.LauncherConfigUpdatedAtUtc = updatedAtUtc.ToString("O");
        AppSettingsStore.Save(settings);
    }

    private static void SaveLauncherConfigUpdatedAtUtc(DateTime updatedAtUtc)
    {
        var settings = AppSettingsStore.Load();
        settings.LauncherConfigUpdatedAtUtc = updatedAtUtc.ToString("O");
        AppSettingsStore.Save(settings);
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
        return string.Equals(JsonSerializer.Serialize(left, JsonOptions), JsonSerializer.Serialize(right, JsonOptions), StringComparison.Ordinal);
    }

    private static bool HasMeaningfulLauncherConfig(CloudQuickPanelConfigSnapshot config)
    {
        return config.QuickPanelSlots.Any(static slot => !string.IsNullOrWhiteSpace(slot)) ||
               HasGroupContent(config.QuickPanelGlobalGroups) ||
               HasGroupContent(config.QuickPanelContextGroups) ||
               config.GlobalFavoriteExtensionIds.Count > 0 ||
               config.ContextFavoriteExtensionIds.Count > 0 ||
               config.YarnSelect != null ||
               config.RadialMenu != null ||
               (config.YanyuRules?.Count ?? 0) > 0 ||
               config.Yanm != null;
    }

    private static bool HasGroupContent(IEnumerable<QuickPanelGroupSettings> groups)
    {
        return groups.Any(group =>
            group.Slots.Any(static slot => !string.IsNullOrWhiteSpace(slot)) ||
            group.SlotItems.Any(static item => item != null));
    }

    private static bool HasAiConfigPayload(CloudQuickPanelConfigSnapshot snapshot)
    {
        return !string.IsNullOrWhiteSpace(snapshot.AiBaseUrl) ||
               !string.IsNullOrWhiteSpace(snapshot.AiApiKey) ||
               !string.IsNullOrWhiteSpace(snapshot.AiModel);
    }

    private static bool HasYanmComponentState(YanmSettings yanm)
    {
        return yanm.ComponentState != null && yanm.ComponentState.Count > 0;
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
            if (previous == null || previous.Deleted || !string.Equals(previous.PackageHash, packageHash, StringComparison.OrdinalIgnoreCase))
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
        var remoteBytes = await _backend.TryReadBytesAsync(entry.PackagePath, cancellationToken)
            ?? throw new FileNotFoundException($"上传校验失败：{entry.PackagePath}");
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
        WriteIndented = true
    };
}
