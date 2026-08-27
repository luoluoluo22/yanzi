using System.Reflection;

namespace OpenQuickHost.Sync;

/// <summary>
/// AppSettings 的同步覆盖清单。任何新增设置都必须先在这里明确同步归属，
/// 否则同步验证项目会失败，避免“保存成功但没有进入云端”的静默遗漏。
/// </summary>
internal static class SyncCoverageCatalog
{
    private static readonly Dictionary<string, SyncCoverageEntry> EntriesByProperty =
        CreateEntries().ToDictionary(static entry => entry.PropertyName, StringComparer.Ordinal);

    public static IReadOnlyCollection<SyncCoverageEntry> Entries => EntriesByProperty.Values;

    public static IReadOnlyList<string> FindUnclassifiedAppSettingsProperties()
    {
        return typeof(AppSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Where(propertyName => !EntriesByProperty.ContainsKey(propertyName))
            .OrderBy(static propertyName => propertyName, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> FindUnknownCatalogProperties()
    {
        var settingsProperties = typeof(AppSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        return EntriesByProperty.Keys
            .Where(propertyName => !settingsProperties.Contains(propertyName))
            .OrderBy(static propertyName => propertyName, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> FindAccountSnapshotContractGaps()
    {
        var snapshotProperties = typeof(CloudQuickPanelConfigSnapshot)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        return Entries
            .Where(static entry => entry.Destination == SyncDestination.AccountObjects)
            .Where(static entry => entry.SnapshotPropertyName != null)
            .Where(entry => !snapshotProperties.Contains(entry.SnapshotPropertyName!))
            .Select(static entry => $"{entry.PropertyName} -> {entry.SnapshotPropertyName}")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<SyncCoverageEntry> CreateEntries()
    {
        // 账户对象同步：跨设备一致的用户偏好和业务数据。
        yield return Account(nameof(AppSettings.ThemeMode));
        yield return Account(nameof(AppSettings.AutoCloseToastEnabled));
        yield return Account(nameof(AppSettings.LauncherHotkey));
        yield return Account(nameof(AppSettings.LaunchAtStartup));
        yield return Account(nameof(AppSettings.RefreshCloudOnStartup));
        yield return Account(nameof(AppSettings.CloseToTray));
        yield return Account(nameof(AppSettings.EnableAutoUpdate));
        yield return Account(nameof(AppSettings.QuickPanelSlots));
        yield return Account(nameof(AppSettings.QuickPanelGlobalGroups));
        yield return Account(nameof(AppSettings.QuickPanelContextGroups));
        yield return Account(nameof(AppSettings.SelectedQuickPanelGlobalGroupId));
        yield return Account(nameof(AppSettings.SelectedQuickPanelContextGroupId));
        yield return Account(nameof(AppSettings.QuickPanelTrigger));
        yield return Account(nameof(AppSettings.MouseGestureAppBindings));
        yield return Account(nameof(AppSettings.QuickPanelMouseTriggers));
        yield return Account(nameof(AppSettings.MouseGestureTriggerMode));
        yield return Account(nameof(AppSettings.YarnSelect));
        yield return Account(nameof(AppSettings.RadialMenu));
        yield return Account(nameof(AppSettings.GlobalFavoriteExtensionIds));
        yield return Account(nameof(AppSettings.ContextFavoriteExtensionIds));
        yield return Account(nameof(AppSettings.DisabledExtensionIds));
        yield return Account(nameof(AppSettings.PinnedSearchScopeCommandIds));
        yield return Account(nameof(AppSettings.SearchScopeConfigs));
        yield return Account(nameof(AppSettings.EnableAgentApi));
        yield return Account(nameof(AppSettings.AgentApiPort));
        yield return Account(nameof(AppSettings.EnableBrowserHelper));
        yield return Account(nameof(AppSettings.PreferManualExtensionEditor));
        yield return Account(nameof(AppSettings.AiBaseUrl));
        yield return Entry(nameof(AppSettings.AiApiKey), SyncDestination.LocalSecret,
            SyncPayloadPolicy.Excluded, "AI API Key 保存在本机 DPAPI，不进入普通云端或仓库 JSON。");
        yield return Account(nameof(AppSettings.AiModel));
        yield return Account(nameof(AppSettings.AiSystemPrompt));
        yield return Account(nameof(AppSettings.AiServiceProviders), SyncPayloadPolicy.MetadataOnly,
            "同步服务商、地址和模型；每个服务商的 API Key 保存在本机 DPAPI。");
        yield return Account(nameof(AppSettings.ActiveServiceProviderId));
        yield return Account(nameof(AppSettings.EnvironmentVariables), SyncPayloadPolicy.MetadataOnly,
            "只同步变量名和说明，变量值保留在本机 DPAPI 存储中。");
        yield return Account(nameof(AppSettings.YanyuRules));
        yield return Account(nameof(AppSettings.EnableEverything));
        yield return Account(nameof(AppSettings.EnableWindowSnapAssist));
        yield return Account(nameof(AppSettings.WindowSnapAssistHotkey));
        yield return Account(nameof(AppSettings.WindowSnapAssistMouseTriggerMode));
        yield return Account(nameof(AppSettings.WindowSnapAssistCustomLayouts));
        yield return Account(nameof(AppSettings.WindowBindings));

        // 单独的账户端点和时间戳，不允许主配置快照覆盖。
        yield return Entry(nameof(AppSettings.Yanm), SyncDestination.AccountYanmObjects,
            SyncPayloadPolicy.FullPayload, "布局与组件状态 key 使用统一 revision 对象；旧 yanm-state 仅作迁移双写回退。");

        // 账户私有配置：服务器地址等可跨设备，密码由专用私有配置链处理。
        yield return Entry(nameof(AppSettings.PersonalSync), SyncDestination.AccountPrivateConfig,
            SyncPayloadPolicy.MetadataOnly, "同步后端类型、仓库和路径；Token/密码保存在本机 DPAPI，不进入账号云。");
        yield return Entry(nameof(AppSettings.EnableWebDavSync), SyncDestination.AccountPrivateConfig);
        yield return Entry(nameof(AppSettings.WebDavServerUrl), SyncDestination.AccountPrivateConfig);
        yield return Entry(nameof(AppSettings.WebDavRootPath), SyncDestination.AccountPrivateConfig);
        yield return Entry(nameof(AppSettings.WebDavUsername), SyncDestination.AccountPrivateConfig,
            SyncPayloadPolicy.SensitivePlaintext);
        yield return Entry(nameof(AppSettings.PersonalSyncAutoSyncDelaySeconds), SyncDestination.AccountPrivateConfig);

        // 明确的本机设置：路径、端口入口、窗口位置和运行状态不跨设备复制。
        yield return Device(nameof(AppSettings.AutoBackupFrequency), "本机备份计划。");
        yield return Device(nameof(AppSettings.LastAutoBackupTime), "本机运行时间戳。");
        yield return Device(nameof(AppSettings.CustomBackupDirectory), "本机文件系统路径。");
        yield return Device(nameof(AppSettings.EnableLanSync), "本机局域网服务开关。");
        yield return Device(nameof(AppSettings.EnableWanPush), "本机公网推送入口开关。");
        yield return Device(nameof(AppSettings.WebDavSyncManuallyDisabled), "本机显式停用状态，防止云配置重新开启。");
        yield return Device(nameof(AppSettings.RecentlyAddedExtensionIds), "本机商店提示状态。");
        yield return Device(nameof(AppSettings.UnreadNewExtensionIds), "本机未读提示状态。");
        yield return Device(nameof(AppSettings.LegacyCleanupDismissed), "本机迁移提示状态。");
        yield return Device(nameof(AppSettings.SettingsWindowLeft), "本机窗口几何信息。");
        yield return Device(nameof(AppSettings.SettingsWindowTop), "本机窗口几何信息。");
        yield return Device(nameof(AppSettings.SettingsWindowWidth), "本机窗口几何信息。");
        yield return Device(nameof(AppSettings.SettingsWindowHeight), "本机窗口几何信息。");
        yield return Device(nameof(AppSettings.LastTestArgument), "本机调试输入历史。");
        yield return Device(nameof(AppSettings.MobileExtensionsJson), "本机移动端代理缓存。");

        // 密钥或设备身份只允许留在本机。
        yield return Entry(nameof(AppSettings.AgentApiToken), SyncDestination.LocalSecret,
            SyncPayloadPolicy.Excluded, "本机代理访问令牌。");
        yield return Entry(nameof(AppSettings.WanPushUuid), SyncDestination.LocalSecret,
            SyncPayloadPolicy.Excluded, "本机公网推送身份。");

        // 旧字段只参与迁移，正式同步使用拆分后的全局/场景收藏。
        yield return Entry(nameof(AppSettings.FavoriteExtensionIds), SyncDestination.LegacyMigrationOnly,
            SyncPayloadPolicy.Excluded, "迁移到 GlobalFavoriteExtensionIds 后不再作为独立真源。");

        // 同步引擎自己的游标/时间戳，不属于用户数据。
        yield return Entry(nameof(AppSettings.LauncherConfigUpdatedAtUtc), SyncDestination.SyncMetadata);
        yield return Entry(nameof(AppSettings.YanmStateUpdatedAtUtc), SyncDestination.SyncMetadata);
    }

    private static SyncCoverageEntry Account(
        string propertyName,
        SyncPayloadPolicy policy = SyncPayloadPolicy.FullPayload,
        string reason = "") =>
        Entry(propertyName, SyncDestination.AccountObjects, policy, reason, propertyName);

    private static SyncCoverageEntry Device(string propertyName, string reason) =>
        Entry(propertyName, SyncDestination.DeviceLocal, SyncPayloadPolicy.Excluded, reason);

    private static SyncCoverageEntry Entry(
        string propertyName,
        SyncDestination destination,
        SyncPayloadPolicy policy = SyncPayloadPolicy.FullPayload,
        string reason = "",
        string? snapshotPropertyName = null) =>
        new(propertyName, destination, policy, reason, snapshotPropertyName);
}

internal sealed record SyncCoverageEntry(
    string PropertyName,
    SyncDestination Destination,
    SyncPayloadPolicy PayloadPolicy,
    string Reason,
    string? SnapshotPropertyName);

internal enum SyncDestination
{
    AccountObjects,
    AccountPrivateConfig,
    AccountYanmObjects,
    DeviceLocal,
    LocalSecret,
    LegacyMigrationOnly,
    SyncMetadata
}

internal enum SyncPayloadPolicy
{
    FullPayload,
    MetadataOnly,
    SensitivePlaintext,
    Excluded
}
