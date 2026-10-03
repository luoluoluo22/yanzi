using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

internal static class AppSettingsMigration
{
    internal static int NormalizeLongPressMilliseconds(int milliseconds) => Math.Clamp(milliseconds, 50, 1500);

    internal static AppSettings Normalize(AppSettings settings)
    {
        settings.ThemeMode = settings.ThemeMode?.Trim() switch
        {
            "Light" => "Light",
            "System" => "System",
            _ => "Dark"
        };

        settings.QuickPanelGlobalGroups ??= [];
        if (settings.QuickPanelGlobalGroups.Count == 0)
        {
            settings.QuickPanelGlobalGroups.Add(new QuickPanelGroupSettings
            {
                Id = "global-default",
                Name = "默认",
                Slots = settings.QuickPanelSlots.Take(12).ToList(),
                SlotItems = settings.QuickPanelSlots
                    .Take(24)
                    .Select(static slot => string.IsNullOrWhiteSpace(slot)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = slot })
                    .ToList()
            });
        }

        settings.QuickPanelContextGroups ??= [];
        if (settings.QuickPanelContextGroups.Count == 0)
        {
            settings.QuickPanelContextGroups.Add(new QuickPanelGroupSettings
            {
                Id = "context-default",
                Name = "默认"
            });
        }

        if (settings.QuickPanelGlobalRowCount <= 0)
        {
            settings.QuickPanelGlobalRowCount = settings.QuickPanelRowCount > 0 ? settings.QuickPanelRowCount : 3;
        }
        if (settings.QuickPanelGlobalColumnCount <= 0)
        {
            settings.QuickPanelGlobalColumnCount = 4;
        }
        if (settings.QuickPanelContextRowCount <= 0)
        {
            settings.QuickPanelContextRowCount = settings.QuickPanelRowCount > 0 ? settings.QuickPanelRowCount : 3;
        }
        if (settings.QuickPanelContextColumnCount <= 0)
        {
            settings.QuickPanelContextColumnCount = 4;
        }

        settings.QuickPanelGlobalRowCount = Math.Max(1, Math.Min(8, settings.QuickPanelGlobalRowCount));
        settings.QuickPanelGlobalColumnCount = Math.Max(3, Math.Min(8, settings.QuickPanelGlobalColumnCount));
        settings.QuickPanelContextRowCount = Math.Max(1, Math.Min(8, settings.QuickPanelContextRowCount));
        settings.QuickPanelContextColumnCount = Math.Max(3, Math.Min(8, settings.QuickPanelContextColumnCount));

        var globalSlotCount = settings.QuickPanelGlobalRowCount * settings.QuickPanelGlobalColumnCount;
        var contextSlotCount = settings.QuickPanelContextRowCount * settings.QuickPanelContextColumnCount;

        NormalizeGroupList(settings.QuickPanelGlobalGroups, globalSlotCount);
        NormalizeGroupList(settings.QuickPanelContextGroups, contextSlotCount);

        settings.GlobalFavoriteExtensionIds ??= settings.FavoriteExtensionIds?.ToList() ?? [];
        settings.ContextFavoriteExtensionIds ??= [];
        settings.DisabledExtensionIds ??= [];
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        settings.KnownExtensionIds ??= [];
        settings.CompletedQuestIds ??= [];
        settings.UnlockedBadges ??= [];
        settings.YarnSelect ??= new YarnSelectSettings();
        settings.YarnSelect.WhitelistedProcesses ??= [];
        settings.YarnSelect.BlacklistedProcesses ??= [];
        settings.YarnSelect.Rules ??= [];
        if (settings.YarnSelect.Rules.Count == 0)
        {
            settings.YarnSelect.Rules = YarnSelectSettings.CreateDefaultRulesFromLegacy(settings.YarnSelect);
        }

        settings.YarnSelect.Rules = settings.YarnSelect.Rules
            .Select(YarnSelectSettings.NormalizeRule)
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerKey))
            .DistinctBy(static rule => rule.TriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.YarnSelect.WhitelistedProcesses = settings.YarnSelect.WhitelistedProcesses
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.YarnSelect.BlacklistedProcesses = settings.YarnSelect.BlacklistedProcesses
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        if (settings.RadialMenu.Pages.Count == 0)
        {
            settings.RadialMenu.Pages.Add(new RadialMenuPageSettings
            {
                Id = "default",
                Name = "全局",
                Slots = settings.RadialMenu.Slots?.ToList() ?? Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
            });
        }

        foreach (var page in settings.RadialMenu.Pages)
        {
            page.Id = string.IsNullOrWhiteSpace(page.Id) ? Guid.NewGuid().ToString("N") : page.Id.Trim();
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "未命名" : page.Name.Trim();
            if (page.Id.Equals("default", StringComparison.OrdinalIgnoreCase) &&
                (page.Name == "默认" || page.Name == "办公" || page.Name == "燕环"))
            {
                page.Name = "全局";
            }
            page.Slots ??= [];
            while (page.Slots.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.Slots.Add(null);
            }

            if (page.Slots.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.Slots = page.Slots.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.Slots = page.Slots
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
            page.SlotTitles ??= [];
            while (page.SlotTitles.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.SlotTitles.Add(null);
            }

            if (page.SlotTitles.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.SlotTitles = page.SlotTitles.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.SlotTitles = page.SlotTitles
                .Select(static title => string.IsNullOrWhiteSpace(title) ? null : title.Trim())
                .ToList();
            page.ChildPageIds ??= [];
            while (page.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.ChildPageIds.Add(null);
            }

            if (page.ChildPageIds.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.ChildPageIds = page.ChildPageIds.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.ChildPageIds = page.ChildPageIds
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
        }

        settings.RadialMenu.SelectedPageId = settings.RadialMenu.Pages.Any(page => page.Id.Equals(settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase))
            ? settings.RadialMenu.SelectedPageId
            : settings.RadialMenu.Pages[0].Id;
        settings.RadialMenu.ActivationKey = RadialActivationKeys.Normalize(settings.RadialMenu.ActivationKey);
        settings.RadialMenu.CustomShortcut = (settings.RadialMenu.CustomShortcut ?? string.Empty).Trim();
        settings.GlobalServiceBlacklistedProcesses = NormalizeProcessList(settings.GlobalServiceBlacklistedProcesses);
        settings.RadialMenu.WhitelistedProcesses = NormalizeProcessList(settings.RadialMenu.WhitelistedProcesses);
        settings.RadialMenu.BlacklistedProcesses = NormalizeProcessList(settings.RadialMenu.BlacklistedProcesses);
        settings.RadialMenu.Slots = settings.RadialMenu.Pages[0].Slots.ToList();
        settings.RadialMenu.DeadZonePixels = Math.Clamp(settings.RadialMenu.DeadZonePixels, 12, 120);
        settings.RadialMenu.RadiusPixels = Math.Clamp(settings.RadialMenu.RadiusPixels, 80, 240);
        settings.RadialMenu.DragThresholdPixels = Math.Clamp(settings.RadialMenu.DragThresholdPixels, 8, 120);
        settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        settings.QuickPanelMouseTriggers.LongPressMilliseconds =
            NormalizeLongPressMilliseconds(settings.QuickPanelMouseTriggers.LongPressMilliseconds);
        settings.QuickPanelMouseTriggers.DragThresholdPixels = Math.Clamp(settings.QuickPanelMouseTriggers.DragThresholdPixels, 8, 120);
        settings.MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(settings.MouseGestureTriggerMode);
        settings.YanyuRules ??= [];
        settings.YanyuRules = settings.YanyuRules
            .Select(NormalizeYanyuRule)
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerText))
            .ToList();
        settings.RecentlyAddedExtensionIds = settings.RecentlyAddedExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        settings.UnreadNewExtensionIds = settings.UnreadNewExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelGlobalGroupId) ||
            settings.QuickPanelGlobalGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelGlobalGroupId = settings.QuickPanelGlobalGroups[0].Id;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelContextGroupId) ||
            settings.QuickPanelContextGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelContextGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelContextGroupId = settings.QuickPanelContextGroups[0].Id;
        }

        settings.PersonalSync ??= new PersonalSyncSettings();
        settings.PersonalSync.Provider = PersonalSyncProviders.Normalize(settings.PersonalSync.Provider);
        settings.PersonalSync.GitHub ??= new PersonalSyncGitHubConfig();
        settings.PersonalSync.Gitee ??= new PersonalSyncGiteeConfig();
        settings.PersonalSync.GitLab ??= new PersonalSyncGitLabConfig();
        settings.PersonalSync.Gitea ??= new PersonalSyncGiteaConfig();
        settings.PersonalSync.S3 ??= new PersonalSyncS3Config();
        settings.PersonalSync.WebDav ??= new PersonalSyncWebDavConfig();
        var hasLegacyWebDavConfig = HasWebDavConfigValues(
            settings.WebDavServerUrl,
            settings.WebDavRootPath,
            settings.WebDavUsername);
        settings.PersonalSync.GitHub.Username = settings.PersonalSync.GitHub.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.GitHub.Repo = string.IsNullOrWhiteSpace(settings.PersonalSync.GitHub.Repo) ? "yanzi-sync" : settings.PersonalSync.GitHub.Repo.Trim();
        settings.PersonalSync.GitHub.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.GitHub.Branch) ? "main" : settings.PersonalSync.GitHub.Branch.Trim();
        settings.PersonalSync.GitHub.PathPrefix = settings.PersonalSync.GitHub.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitee.Username = settings.PersonalSync.Gitee.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitee.Repo = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitee.Repo) ? "yanzi-sync" : settings.PersonalSync.Gitee.Repo.Trim();
        settings.PersonalSync.Gitee.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitee.Branch) ? "master" : settings.PersonalSync.Gitee.Branch.Trim();
        settings.PersonalSync.Gitee.PathPrefix = settings.PersonalSync.Gitee.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.GitLab.BaseUrl = string.IsNullOrWhiteSpace(settings.PersonalSync.GitLab.BaseUrl) ? "https://gitlab.com" : settings.PersonalSync.GitLab.BaseUrl.Trim();
        settings.PersonalSync.GitLab.ProjectPath = settings.PersonalSync.GitLab.ProjectPath?.Trim() ?? string.Empty;
        settings.PersonalSync.GitLab.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.GitLab.Branch) ? "main" : settings.PersonalSync.GitLab.Branch.Trim();
        settings.PersonalSync.GitLab.PathPrefix = settings.PersonalSync.GitLab.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitea.BaseUrl = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitea.BaseUrl) ? "https://gitea.com" : settings.PersonalSync.Gitea.BaseUrl.Trim();
        settings.PersonalSync.Gitea.Username = settings.PersonalSync.Gitea.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitea.Repo = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitea.Repo) ? "yanzi-sync" : settings.PersonalSync.Gitea.Repo.Trim();
        settings.PersonalSync.Gitea.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitea.Branch) ? "main" : settings.PersonalSync.Gitea.Branch.Trim();
        settings.PersonalSync.Gitea.PathPrefix = settings.PersonalSync.Gitea.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.AccessKeyId = settings.PersonalSync.S3.AccessKeyId?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.Region = settings.PersonalSync.S3.Region?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.Bucket = settings.PersonalSync.S3.Bucket?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.Endpoint = settings.PersonalSync.S3.Endpoint?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.PathPrefix = settings.PersonalSync.S3.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.WebDav.Url = string.IsNullOrWhiteSpace(settings.PersonalSync.WebDav.Url) ? "https://dav.jianguoyun.com/dav/" : settings.PersonalSync.WebDav.Url.Trim();
        settings.PersonalSync.WebDav.Username = settings.PersonalSync.WebDav.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.WebDav.PathPrefix = string.IsNullOrWhiteSpace(settings.PersonalSync.WebDav.PathPrefix) ? "/yanzi" : settings.PersonalSync.WebDav.PathPrefix.Trim();

        var shouldAdoptLegacyWebDavConfig =
            hasLegacyWebDavConfig &&
            settings.PersonalSync.Provider == PersonalSyncProviders.None;
        if (shouldAdoptLegacyWebDavConfig)
        {
            CloudSyncDiagnostics.Log(
                "AppSettingsStore",
                "Adopting legacy WebDAV config into personal sync settings",
                ("providerBefore", settings.PersonalSync.Provider),
                ("enableWebDavSync", settings.EnableWebDavSync),
                ("webDavUrl", settings.WebDavServerUrl),
                ("webDavRootPath", settings.WebDavRootPath),
                ("webDavUsername", settings.WebDavUsername));
            settings.PersonalSync.Provider = PersonalSyncProviders.WebDav;
            settings.PersonalSync.Enabled = settings.EnableWebDavSync;
            settings.PersonalSync.WebDav.Url = string.IsNullOrWhiteSpace(settings.WebDavServerUrl)
                ? settings.PersonalSync.WebDav.Url
                : settings.WebDavServerUrl.Trim();
            settings.PersonalSync.WebDav.PathPrefix = string.IsNullOrWhiteSpace(settings.WebDavRootPath)
                ? settings.PersonalSync.WebDav.PathPrefix
                : settings.WebDavRootPath.Trim();
            settings.PersonalSync.WebDav.Username = settings.WebDavUsername?.Trim() ?? string.Empty;
        }

        if (!settings.WebDavSyncManuallyDisabled &&
            hasLegacyWebDavConfig)
        {
            settings.EnableWebDavSync = true;
        }

        if (settings.PersonalSync.Provider == PersonalSyncProviders.WebDav)
        {
            settings.EnableWebDavSync = settings.PersonalSync.Enabled;
            settings.WebDavServerUrl = settings.PersonalSync.WebDav.Url;
            settings.WebDavRootPath = settings.PersonalSync.WebDav.PathPrefix;
            settings.WebDavUsername = settings.PersonalSync.WebDav.Username;
        }

        settings.PersonalSyncAutoSyncDelaySeconds = NormalizePersonalSyncAutoSyncDelay(settings.PersonalSyncAutoSyncDelaySeconds);

        settings.AiBaseUrl = settings.AiBaseUrl?.Trim() ?? string.Empty;
        settings.AiApiKey = settings.AiApiKey?.Trim() ?? string.Empty;
        settings.AiModel = settings.AiModel?.Trim() ?? string.Empty;
        settings.AiSystemPrompt = string.IsNullOrWhiteSpace(settings.AiSystemPrompt)
            ? AppSettingsStore.DefaultAiSystemPrompt
            : settings.AiSystemPrompt.Trim();

        settings.AiServiceProviders ??= [];
        if (settings.AiServiceProviders.Count == 0)
        {
            var defaultProvider = new AiServiceProviderSettings
            {
                Id = Guid.NewGuid().ToString(),
                Name = "默认提供商",
                ProviderType = "OpenAI",
                BaseUrl = settings.AiBaseUrl,
                ApiKey = settings.AiApiKey,
                IsEnabled = true,
                Models = string.IsNullOrWhiteSpace(settings.AiModel) ? [] : new List<string> { settings.AiModel },
                SelectedModel = settings.AiModel
            };
            settings.AiServiceProviders.Add(defaultProvider);
            settings.ActiveServiceProviderId = defaultProvider.Id;
        }
        settings.Yanm ??= new YanmSettings();
        settings.Yanm.Components ??= [];
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.Yanm.ActivationKey = YanmActivationKeys.Normalize(settings.Yanm.ActivationKey);
        settings.Yanm.WhitelistedProcesses = NormalizeProcessList(settings.Yanm.WhitelistedProcesses);
        settings.Yanm.BlacklistedProcesses = NormalizeProcessList(settings.Yanm.BlacklistedProcesses);
        settings.Yanm.HoldDelayMilliseconds = Math.Clamp(settings.Yanm.HoldDelayMilliseconds, 0, 1000);
        settings.Yanm.GridSizePixels = Math.Clamp(settings.Yanm.GridSizePixels, 5, 80);
        settings.Yanm.OverlayOpacity = settings.Yanm.OverlayOpacity <= 0.581
            ? 0.85
            : Math.Clamp(settings.Yanm.OverlayOpacity, 0.05, 0.85);
        if (!settings.Yanm.HasInitializedDefaultComponents &&
            settings.Yanm.Components.Count == 0)
        {
            settings.Yanm.Components = YanmComponentSettings.CreateDefaultComponents();
            settings.Yanm.HasInitializedDefaultComponents = true;
            settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        }
        else if (settings.Yanm.DefaultComponentVersion < YanmSettings.CurrentDefaultComponentVersion)
        {
            YanmComponentSettings.UpgradeDefaultComponents(settings.Yanm.Components);
            settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        }

        NormalizeDefaultYanmComponentIds(settings.Yanm);

        var yanmComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in settings.Yanm.Components)
        {
            component.Id = NormalizeYanmComponentId(component.Id, yanmComponentIds);
            component.Title = string.IsNullOrWhiteSpace(component.Title) ? "燕幕组件" : component.Title.Trim();
            component.X = Math.Max(0, component.X);
            component.Y = Math.Max(0, component.Y);
            component.Width = Math.Max(settings.Yanm.GridSizePixels * 8, component.Width);
            component.Height = Math.Max(settings.Yanm.GridSizePixels * 6, component.Height);
            component.Html = string.IsNullOrWhiteSpace(component.Html) ? YanmComponentSettings.DefaultHtml(component.Title) : component.Html;
            component.Locked = component.Locked;
        }

        settings.WindowSnapAssistHotkey = settings.WindowSnapAssistHotkey?.Trim() ?? string.Empty;
        settings.WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(settings.WindowSnapAssistMouseTriggerMode);
        settings.WindowSnapAssistCustomLayouts ??= [];
        settings.WindowSnapAssistCustomLayouts = settings.WindowSnapAssistCustomLayouts
            .Where(static slot => slot.SlotIndex is >= 0 and < WindowSnapAssistCustomLayoutSettings.TotalSlotCount)
            .GroupBy(static slot => slot.SlotIndex)
            .Select(static group => NormalizeWindowSnapAssistCustomLayout(group.Last()))
            .OrderBy(static slot => slot.SlotIndex)
            .ToList();
        settings.WindowBindings = NormalizeWindowBindings(settings.WindowBindings);
        settings.LastTestArgument = string.IsNullOrWhiteSpace(settings.LastTestArgument) ? "示例参数" : settings.LastTestArgument.Trim();
        settings.LastExtensionEditorTab = string.Equals(settings.LastExtensionEditorTab, "ai", StringComparison.OrdinalIgnoreCase) ? "ai" : "simple";
        settings.LauncherResultViewMode = string.Equals(settings.LauncherResultViewMode, "Grid", StringComparison.OrdinalIgnoreCase) ? "Grid" : "List";

        settings.AutoBackupFrequency = string.IsNullOrWhiteSpace(settings.AutoBackupFrequency)
            ? "Weekly"
            : settings.AutoBackupFrequency.Trim();
        settings.LastAutoBackupTime = (settings.LastAutoBackupTime ?? string.Empty).Trim();
        settings.CustomBackupDirectory = (settings.CustomBackupDirectory ?? string.Empty).Trim();

        settings.SearchScopeConfigs ??= [];
        var defaultList = new List<(string Key, string Label)>
        {
            ("all", "全部"),
            ("extension", BrandTerms.DefaultMiniApp),
            ("application", "软件"),
            ("file", "文件"),
            ("system", "系统"),
            ("yanyu", BrandTerms.DefaultYanVoice),
            ("ai", "AI对话"),
            ("store", $"{BrandTerms.DefaultMiniApp}商店")
        };

        foreach (var def in defaultList)
        {
            if (!settings.SearchScopeConfigs.Any(c => string.Equals(c.Key, def.Key, StringComparison.OrdinalIgnoreCase)))
            {
                settings.SearchScopeConfigs.Add(new SearchScopeConfigItem { Key = def.Key, Label = def.Label, IsVisible = true, IsPinned = false });
            }
        }

        // 自动升级旧配置中的历史名词
        foreach (var cfg in settings.SearchScopeConfigs)
        {
            if (string.Equals(cfg.Key, "extension", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(cfg.Label, "扩展", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(cfg.Label)))
            {
                cfg.Label = BrandTerms.Current.MiniApp;
            }
            else if (string.Equals(cfg.Key, "store", StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(cfg.Label, "扩展商店", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(cfg.Label)))
            {
                cfg.Label = $"{BrandTerms.Current.MiniApp}商店";
            }
            else if (string.Equals(cfg.Key, "application", StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(cfg.Label, "应用", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(cfg.Label)))
            {
                cfg.Label = "软件";
            }
        }

        settings.PinnedSearchScopeCommandIds ??= [];
        foreach (var id in settings.PinnedSearchScopeCommandIds)
        {
            var key = $"pinned_{id}";
            if (!settings.SearchScopeConfigs.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                settings.SearchScopeConfigs.Add(new SearchScopeConfigItem { Key = key, Label = $"固定:{id}", IsVisible = true, IsPinned = true });
            }
        }

        settings.SearchScopeConfigs.RemoveAll(c => c.IsPinned && !settings.PinnedSearchScopeCommandIds.Contains(c.Key.Replace("pinned_", ""), StringComparer.OrdinalIgnoreCase));

        return settings;
    }

    internal static void NormalizeDefaultYanmComponentIds(YanmSettings yanm)
    {
        if (yanm.Components.Count == 0)
        {
            return;
        }

        var usedIds = yanm.Components
            .Where(static component => !string.IsNullOrWhiteSpace(component.Id))
            .Select(static component => component.Id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var component in yanm.Components)
        {
            if (!YanmComponentSettings.TryGetDefaultComponentId(component.Title, out var stableId))
            {
                continue;
            }

            var currentId = component.Id?.Trim() ?? string.Empty;
            if (string.Equals(currentId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (usedIds.Contains(stableId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentId) &&
                yanm.ComponentState.TryGetValue(currentId, out var state) &&
                !yanm.ComponentState.ContainsKey(stableId))
            {
                yanm.ComponentState[stableId] = state;
                yanm.ComponentState.Remove(currentId);
            }

            if (!string.IsNullOrWhiteSpace(currentId))
            {
                usedIds.Remove(currentId);
            }

            component.Id = stableId;
            usedIds.Add(stableId);
        }
    }

    internal static string NormalizeYanmComponentId(string? id, HashSet<string> usedIds)
    {
        var normalized = id?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || usedIds.Contains(normalized))
        {
            do
            {
                normalized = YanmComponentSettings.CreateSystemComponentId();
            }
            while (usedIds.Contains(normalized));
        }

        usedIds.Add(normalized);
        return normalized;
    }

    internal static QuickPanelSlotItem? NormalizeSlotItem(QuickPanelSlotItem? item)
    {
        if (item == null)
        {
            return null;
        }

        item.ItemType = string.IsNullOrWhiteSpace(item.ItemType) ? "extension" : item.ItemType.Trim().ToLowerInvariant();
        if (item.IsFolder)
        {
            item.FolderName = string.IsNullOrWhiteSpace(item.FolderName) ? "新分组" : item.FolderName.Trim();
            item.FolderExtensionIds ??= [];
            item.FolderExtensionIds = item.FolderExtensionIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            item.FolderSlotItems ??= [];
            if (item.FolderSlotItems.Count == 0)
            {
                item.FolderSlotItems = item.FolderExtensionIds
                    .Take(24)
                    .Select(static id => string.IsNullOrWhiteSpace(id)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = id })
                    .ToList();
            }

            while (item.FolderSlotItems.Count < 24)
            {
                item.FolderSlotItems.Add(null);
            }

            if (item.FolderSlotItems.Count > 24)
            {
                item.FolderSlotItems = item.FolderSlotItems.Take(24).ToList();
            }

            for (var index = 0; index < item.FolderSlotItems.Count; index++)
            {
                item.FolderSlotItems[index] = NormalizeSlotItem(item.FolderSlotItems[index]);
            }

            item.FolderExtensionIds = item.FolderSlotItems
                .Where(static slot => slot != null && !slot.IsFolder && !string.IsNullOrWhiteSpace(slot.ExtensionId))
                .Select(static slot => slot!.ExtensionId!)
                .ToList();
            return item.FolderSlotItems.Any(static slot => slot != null) ? item : null;
        }

        item.ExtensionId = string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item.ExtensionId.Trim();
        return string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item;
    }

    internal static List<string?> ProjectLegacySlots(IReadOnlyList<QuickPanelSlotItem?> slotItems)
    {
        var result = slotItems
            .Take(12)
            .Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null)
            .ToList();
        while (result.Count < 12)
        {
            result.Add(null);
        }

        return result;
    }

    internal static void NormalizeGroupList(List<QuickPanelGroupSettings> groups, int slotCount)
    {
        foreach (var group in groups)
        {
            group.Id = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("N") : group.Id;
            group.Name = string.IsNullOrWhiteSpace(group.Name) ? "未命名" : group.Name.Trim();
            group.ContextProcessName = group.ContextProcessName?.Trim();
            group.ContextDisplayName = group.ContextDisplayName?.Trim();
            group.Slots ??= [];
            group.SlotItems ??= [];
            while (group.Slots.Count < slotCount)
            {
                group.Slots.Add(null);
            }
            if (group.Slots.Count > slotCount)
            {
                group.Slots = group.Slots.Take(slotCount).ToList();
            }

            if (group.SlotItems.Count == 0)
            {
                group.SlotItems = group.Slots
                    .Take(slotCount)
                    .Select(static slot => string.IsNullOrWhiteSpace(slot)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = slot })
                    .ToList();
            }

            while (group.SlotItems.Count < slotCount)
            {
                group.SlotItems.Add(null);
            }

            if (group.SlotItems.Count > slotCount)
            {
                group.SlotItems = group.SlotItems.Take(slotCount).ToList();
            }

            for (var index = 0; index < group.SlotItems.Count; index++)
            {
                group.SlotItems[index] = NormalizeSlotItem(group.SlotItems[index]);
            }

            group.Slots = ProjectLegacySlots(group.SlotItems);
        }
    }

    internal static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username);
    }

    internal static int NormalizePersonalSyncAutoSyncDelay(int value)
    {
        return value is 0 or 2 or 3 or 5 or 10 or 20 or 30 or 60 or 120
            ? value
            : 10;
    }

    internal static List<string> NormalizeProcessList(IEnumerable<string>? processes) =>
        (processes ?? [])
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Select(static item => item.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    internal static WindowSnapAssistCustomLayoutSettings NormalizeWindowSnapAssistCustomLayout(WindowSnapAssistCustomLayoutSettings slot)
    {
        slot.LeftRatio = Math.Clamp(slot.LeftRatio, -2, 3);
        slot.TopRatio = Math.Clamp(slot.TopRatio, -2, 3);
        slot.WidthRatio = Math.Clamp(slot.WidthRatio, 0.05, 3);
        slot.HeightRatio = Math.Clamp(slot.HeightRatio, 0.05, 3);
        return slot;
    }

    internal static YanyuRuleSettings NormalizeYanyuRule(YanyuRuleSettings? rule)
    {
        rule ??= new YanyuRuleSettings();
        rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim();
        rule.TriggerText = (rule.TriggerText ?? string.Empty).Trim();
        rule.Description = (rule.Description ?? string.Empty).Trim();
        rule.BoundProcessName = (rule.BoundProcessName ?? string.Empty).Trim();
        rule.ActionType = YanyuActionTypes.Normalize(rule.ActionType);
        rule.TextContent ??= string.Empty;
        rule.ExtensionId = string.IsNullOrWhiteSpace(rule.ExtensionId) ? string.Empty : rule.ExtensionId.Trim();
        rule.TriggerSuffix = YanyuTriggerSuffix.Normalize(rule.TriggerSuffix);
        return rule;
    }

    internal static WindowBindingSettings NormalizeWindowBindings(WindowBindingSettings? bindings)
    {
        bindings ??= new WindowBindingSettings();
        bindings.Rules ??= [];
        bindings.MarginPixels = Math.Clamp(bindings.MarginPixels, 0, 64);
        bindings.Rules = bindings.Rules
            .Select(NormalizeWindowBindingRule)
            .Where(static rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.ExtensionId) && !string.IsNullOrWhiteSpace(rule.ProcessName))
            .DistinctBy(static rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return bindings;
    }

    internal static WindowBindingRuleSettings NormalizeWindowBindingRule(WindowBindingRuleSettings? rule)
    {
        rule ??= new WindowBindingRuleSettings();
        rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim();
        rule.ExtensionId = (rule.ExtensionId ?? string.Empty).Trim();
        rule.ProcessName = (rule.ProcessName ?? string.Empty).Trim();
        rule.WindowClass = (rule.WindowClass ?? string.Empty).Trim();
        rule.TitleContains = (rule.TitleContains ?? string.Empty).Trim();
        rule.Corner = WindowBindingCorners.Normalize(rule.Corner);
        rule.OffsetX = RoundToGrid(rule.OffsetX, 10);
        rule.OffsetY = RoundToGrid(rule.OffsetY, 10);
        return rule;
    }

    internal static int RoundToGrid(int value, int gridSize)
    {
        if (gridSize <= 0)
        {
            return value;
        }

        return (int)Math.Round(value / (double)gridSize, MidpointRounding.AwayFromZero) * gridSize;
    }
}
