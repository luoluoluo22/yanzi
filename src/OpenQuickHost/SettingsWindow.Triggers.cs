using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using OpenQuickHost.Sync;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

public partial class SettingsWindow
{
    private void UpdateQuickPanelMouseTrigger(bool value, Action<QuickPanelMouseTriggerSettings> update)
    {
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        update(_settings.QuickPanelMouseTriggers);
        OnPropertyChanged();
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
    }

    private void UpdateRadialMenu<T>(T value, Action<RadialMenuSettings> update)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        update(_settings.RadialMenu);
        OnPropertyChanged();
        OnPropertyChanged(nameof(RadialMouseTriggerMode));
        OnPropertyChanged(nameof(RadialUsesCustomShortcut));
        OnPropertyChanged(nameof(RadialCustomShortcut));
        OnPropertyChanged(nameof(RadialMenuSummary));
    }

    private static bool IsRightMouseTriggerMode(string mode)
    {
        return string.Equals(mode, MouseTriggerModes.RightDrag, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, MouseTriggerModes.RightLongPress, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mode, MouseTriggerModes.CtrlRightClick, StringComparison.OrdinalIgnoreCase);
    }



    public string RadialPreviewDebugLog
    {
        get => _radialPreviewDebugLog;
        private set
        {
            if (value == _radialPreviewDebugLog)
            {
                return;
            }

            _radialPreviewDebugLog = value;
            OnPropertyChanged();
        }
    }







    private void QueueQuickPanelTriggerSave(int delayMs = 500)
    {
        if (_quickPanelSaveTimer == null)
        {
            _quickPanelSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _quickPanelSaveTimer.Tick += (s, e) => { _quickPanelSaveTimer.Stop(); SaveQuickPanelTriggerSettings(); };
        }
        else
        {
            _quickPanelSaveTimer.Stop();
            _quickPanelSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _quickPanelSaveTimer.Start();
    }

    private void FlushQuickPanelTriggerSave()
    {
        if (_quickPanelSaveTimer != null && _quickPanelSaveTimer.IsEnabled)
        {
            _quickPanelSaveTimer.Stop();
            SaveQuickPanelTriggerSettings();
        }
    }

    private void SaveQuickPanelTriggerSettings()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.Yanm ??= new YanmSettings();
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        _settings.RadialMenu.ActivationKey = RadialActivationKeys.Normalize(_settings.RadialMenu.ActivationKey);
        _settings.RadialMenu.CustomShortcut = (_settings.RadialMenu.CustomShortcut ?? string.Empty).Trim();
        _settings.RadialMenu.WhitelistedProcesses = ParseProcessList(string.Join(", ", _settings.RadialMenu.WhitelistedProcesses ?? []));
        _settings.RadialMenu.BlacklistedProcesses = ParseProcessList(string.Join(", ", _settings.RadialMenu.BlacklistedProcesses ?? []));
        _settings = _settingsPersistence.Save(_settings);
        _mainWindow.RefreshAppSettings();
        _mainWindow.NotifyQuickPanelSettingsChanged("quickpanel-trigger-settings-saved");
        SyncStatusText = $"背包触发已保存：{QuickPanelTriggerSummary}";
        _quickPanelStatusHideTimer = ShowSaveStatusTemporarily(_quickPanelStatusHideTimer, visible => IsQuickPanelSaveStatusVisible = visible);
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerMode));
        OnPropertyChanged(nameof(MouseGestureManagementSummary));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(RadialAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(RadialMenuSummary));
        if (IsMouseGesturesSelected)
        {
            RefreshMouseGestureManagement();
        }
    }

    private void SaveYarnSelectSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveYarnSelectSettings();
    }

    private void PickYanmWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕幕白名单", _settings.Yanm.WhitelistedProcesses ?? new(), list =>
        {
            _settings.Yanm.WhitelistedProcesses = list;
            OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
            SaveYanmSettings(requireCustomShortcut: false);
        });
    }

    private void PickYanmBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕幕黑名单", _settings.Yanm.BlacklistedProcesses ?? new(), list =>
        {
            _settings.Yanm.BlacklistedProcesses = list;
            OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
            SaveYanmSettings(requireCustomShortcut: false);
        });
    }

    private void PickRadialWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕环白名单", _settings.RadialMenu.WhitelistedProcesses ?? new(), list =>
        {
            _settings.RadialMenu.WhitelistedProcesses = list;
            OnPropertyChanged(nameof(RadialWhitelistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private void PickGlobalServiceBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow("全局服务黑名单", "请选择要加入 全局服务黑名单 的进程：", string.Empty, _settings.GlobalServiceBlacklistedProcesses ?? new(), showFullscreenSwitch: true, disableInFullscreen: _settings.DisableInFullScreen);
        if (picker.ShowDialog() == true)
        {
            _settings.DisableInFullScreen = picker.DisableInFullscreen;
            foreach (var b in picker.Blacklist)
            {
                if (!string.IsNullOrWhiteSpace(b.ExecutablePath))
                {
                    _settings.ProcessExecutablePaths[b.ProcessName] = b.ExecutablePath;
                }
            }
            _settings.GlobalServiceBlacklistedProcesses = picker.Blacklist.Select(b => b.ProcessName).ToList();
            OnPropertyChanged(nameof(GlobalServiceBlacklistedProcessesText));
            SaveQuickPanelTriggerSettings();
        }
    }

    private void PickRadialBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕环黑名单", _settings.RadialMenu.BlacklistedProcesses ?? new(), list =>
        {
            _settings.RadialMenu.BlacklistedProcesses = list;
            OnPropertyChanged(nameof(RadialBlacklistedProcessesText));
            SaveQuickPanelTriggerSettings();
        });
    }

    private void PickYarnSelectWhitelistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕选白名单", _settings.YarnSelect.WhitelistedProcesses ?? new(), list =>
        {
            _settings.YarnSelect.WhitelistedProcesses = list;
            OnPropertyChanged(nameof(YarnSelectWhitelistedProcessesText));
            SaveYarnSelectSettings();
        });
    }

    private void PickYarnSelectBlacklistProcessButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProcessPickerForList("燕选黑名单", _settings.YarnSelect.BlacklistedProcesses ?? new(), list =>
        {
            _settings.YarnSelect.BlacklistedProcesses = list;
            OnPropertyChanged(nameof(YarnSelectBlacklistedProcessesText));
            SaveYarnSelectSettings();
        });
    }

    private void OpenProcessPickerForList(string targetName, List<string> currentList, Action<List<string>> updateAction)
    {
        var picker = new ProcessPickerWindow(targetName, $"请选择要加入 {targetName} 的进程：", string.Empty, currentList);
        if (picker.ShowDialog() == true)
        {
            foreach (var b in picker.Blacklist)
            {
                if (!string.IsNullOrWhiteSpace(b.ExecutablePath))
                {
                    _settings.ProcessExecutablePaths[b.ProcessName] = b.ExecutablePath;
                }
            }
            updateAction(picker.Blacklist.Select(b => b.ProcessName).ToList());
        }
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var className = new System.Text.StringBuilder(256);
            if (GetClassName(hwnd, className, className.Capacity) > 0)
            {
                var classStr = className.ToString();
                if (string.Equals(classStr, "Progman", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return "desktop";
                }
            }

            _ = GetWindowThreadProcessId(hwnd, out var processId);
            return processId == 0 ? string.Empty : Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Settings: failed to get foreground process, error={ex.Message}");
            return string.Empty;
        }
    }

    private void UpdateYarnSelect(bool value, Action<YarnSelectSettings> update)
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        update(_settings.YarnSelect);
        OnPropertyChanged();
        OnPropertyChanged(nameof(YarnSelectSummary));
        QueueYarnSelectSave(200);
    }

    private void QueueYarnSelectSave(int delayMs = 500)
    {
        if (_yarnSelectSaveTimer == null)
        {
            _yarnSelectSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            _yarnSelectSaveTimer.Tick += (s, e) =>
            {
                _yarnSelectSaveTimer.Stop();
                SaveYarnSelectSettings();
            };
        }
        else
        {
            _yarnSelectSaveTimer.Stop();
            _yarnSelectSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }

        _yarnSelectSaveTimer.Start();
    }

    private void FlushYarnSelectSave()
    {
        if (_yarnSelectSaveTimer != null && _yarnSelectSaveTimer.IsEnabled)
        {
            _yarnSelectSaveTimer.Stop();
            SaveYarnSelectSettings();
        }
    }

    private void UpdateYanm<T>(T value, Action<YanmSettings> update)
    {
        _settings.Yanm ??= new YanmSettings();
        update(_settings.Yanm);
        OnPropertyChanged();
        OnPropertyChanged(nameof(YanmUsesCustomShortcut));
        OnPropertyChanged(nameof(YanmCustomShortcut));
        OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
        OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
        OnPropertyChanged(nameof(YanmMouseTriggerMode));
        OnPropertyChanged(nameof(YanmMouseTriggerRightDrag));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        QueueYanmSave(200);
    }

    private void QueueYanmSave(int delayMs = 500)
    {
        if (_yanmSaveTimer == null)
        {
            _yanmSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _yanmSaveTimer.Tick += (s, e) => { _yanmSaveTimer.Stop(); SaveYanmSettings(); };
        }
        else
        {
            _yanmSaveTimer.Stop();
            _yanmSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _yanmSaveTimer.Start();
    }

    private void FlushYanmSave()
    {
        if (_yanmSaveTimer != null && _yanmSaveTimer.IsEnabled)
        {
            _yanmSaveTimer.Stop();
            SaveYanmSettings();
        }
    }

    private void SaveYanmSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveYanmSettings();
    }

    private void YanmActivationKey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings || _isRefreshingSettingsFromDisk)
        {
            return;
        }

        SaveYanmSettings(requireCustomShortcut: false);
        if (string.Equals(_settings.Yanm?.ActivationKey, YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(_settings.Yanm?.CustomShortcut))
        {
            SyncStatusText = "已切换为自定义快捷键，请点击“录制快捷键”完成设置。";
            OnPropertyChanged(nameof(YanmSummary));
        }
    }

    private void SaveYanmSettings(bool requireCustomShortcut = true)
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _settings.Yanm ??= new YanmSettings();
        _settings.Yanm.ActivationKey = YanmActivationKeys.Normalize(_settings.Yanm.ActivationKey);
        _settings.Yanm.MouseTriggerMode = MouseTriggerModes.Normalize(_settings.Yanm.MouseTriggerMode);
        _settings.Yanm.WhitelistedProcesses = ParseProcessList(string.Join(", ", _settings.Yanm.WhitelistedProcesses ?? []));
        _settings.Yanm.BlacklistedProcesses = ParseProcessList(string.Join(", ", _settings.Yanm.BlacklistedProcesses ?? []));
        SettingsTriggerController.ApplyMouseTriggerModeToYanmFlags(_settings.Yanm);
        SettingsTriggerController.SyncYanmMouseTriggerModeFromFlags(_settings.Yanm);
        if (!SettingsTriggerController.HasRequiredYanmShortcut(_settings.Yanm, requireCustomShortcut))
        {
            System.Windows.MessageBox.Show(this, "已选择自定义快捷键，请先录制一个快捷键再保存。", "缺少快捷键", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settings = _settingsPersistence.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged("yanm-settings-saved");
        SyncStatusText = $"燕幕设置已保存：{YanmSummary}";
        _yanmStatusHideTimer = ShowSaveStatusTemporarily(_yanmStatusHideTimer, visible => IsYanmSaveStatusVisible = visible);
        OnPropertyChanged(nameof(EnableYanm));
        OnPropertyChanged(nameof(YanmActivationKey));
        OnPropertyChanged(nameof(YanmTriggerHold));
        OnPropertyChanged(nameof(YanmTriggerDoubleTap));
        OnPropertyChanged(nameof(YanmUsesCustomShortcut));
        OnPropertyChanged(nameof(YanmCustomShortcut));
        OnPropertyChanged(nameof(YanmWhitelistedProcessesText));
        OnPropertyChanged(nameof(YanmBlacklistedProcessesText));
        OnPropertyChanged(nameof(YanmMouseTriggerMode));
        OnPropertyChanged(nameof(YanmMouseTriggerRightDrag));
        OnPropertyChanged(nameof(YanmSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
    }

    private void SaveYarnSelectSettings()
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.YarnSelect.WhitelistedProcesses ??= [];
        _settings.YarnSelect.BlacklistedProcesses ??= [];
        _settings.YarnSelect.Rules = YarnSelectRules
            .Select(item => YarnSelectSettings.NormalizeRule(new YarnSelectRuleSettings
            {
                Enabled = item.Enabled,
                TriggerKey = item.TriggerKey,
                ActionType = item.ActionType,
                ExtensionId = ResolveYarnSelectExtensionId(item),
                Description = item.Description
            }))
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerKey))
            .DistinctBy(static rule => rule.TriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings = _settingsPersistence.Save(_settings);
        _mainWindow.NotifyQuickPanelSettingsChanged("yarnselect-settings-saved");
        SyncStatusText = $"燕选设置已保存：{YarnSelectSummary}";
        _yarnSelectStatusHideTimer = ShowSaveStatusTemporarily(_yarnSelectStatusHideTimer, visible => IsYarnSelectSaveStatusVisible = visible);
        RefreshYarnSelectBindings();
    }

    private string ResolveYarnSelectExtensionId(YarnSelectRuleItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExtensionId) &&
            YarnSelectExtensionOptions.Any(option => option.ExtensionId.Equals(item.ExtensionId, StringComparison.OrdinalIgnoreCase)))
        {
            return item.ExtensionId;
        }

        var searchText = (item.ExtensionSearchText ?? string.Empty).Trim();
        return YarnSelectExtensionOptions.FirstOrDefault(option =>
            option.Title.Equals(searchText, StringComparison.OrdinalIgnoreCase) ||
            option.ExtensionId.Equals(searchText, StringComparison.OrdinalIgnoreCase))
            ?.ExtensionId ?? string.Empty;
    }

    private void RefreshYarnSelectBindings()
    {
        _settings.YarnSelect ??= new YarnSelectSettings();
        _settings.YarnSelect.Rules ??= [];
        if (_settings.YarnSelect.Rules.Count == 0)
        {
            _settings.YarnSelect.Rules = YarnSelectSettings.CreateDefaultRulesFromLegacy(_settings.YarnSelect);
        }

        RefreshYarnSelectExtensionOptions();
        YarnSelectRules.Clear();
        foreach (var rule in _settings.YarnSelect.Rules.Select(YarnSelectSettings.NormalizeRule))
        {
            var item = new YarnSelectRuleItem(rule);
            item.OnChangedAction = () => { RefreshYarnSelectPreviewMap(); QueueYarnSelectSave(500); };
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }
        RefreshYarnSelectPreviewMap();

        OnPropertyChanged(nameof(EnableYarnSelect));
        OnPropertyChanged(nameof(YarnSelectCopy));
        OnPropertyChanged(nameof(YarnSelectCut));
        OnPropertyChanged(nameof(YarnSelectPaste));
        OnPropertyChanged(nameof(YarnSelectSearch));
        OnPropertyChanged(nameof(YarnSelectRun));
        OnPropertyChanged(nameof(YarnSelectSmartCopyPaste));
        OnPropertyChanged(nameof(YarnSelectSidePaste));
        OnPropertyChanged(nameof(YarnSelectWhitelistedProcessesText));
        OnPropertyChanged(nameof(YarnSelectBlacklistedProcessesText));
        OnPropertyChanged(nameof(YarnSelectSummary));
    }

    private void RefreshYarnSelectExtensionOptions()
    {
        YarnSelectExtensionOptions.Clear();
        RadialMenuExtensionOptions.Clear();
        YarnSelectExtensionOptions.Add(new YarnSelectExtensionOption(string.Empty, "不绑定小程序"));
        foreach (var command in _mainWindow.GetLocalExtensionsForSettings())
        {
            var option = new YarnSelectExtensionOption(command);
            YarnSelectExtensionOptions.Add(option);
        }

        foreach (var command in _mainWindow.GetAllCommands()
                     .Where(IsRadialMenuCommandCandidate)
                     .DistinctBy(static command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static command => command.ItemKindLabel, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static command => command.Title, StringComparer.OrdinalIgnoreCase))
        {
            RadialMenuExtensionOptions.Add(new YarnSelectExtensionOption(command));
        }

        RefreshRadialMenuCommandCandidates(RadialMenuSearchText);
    }

    private void RefreshRadialMenuSlots()
    {
        if (_isRefreshingRadialMenu)
        {
            return;
        }

        try
        {
            _isRefreshingRadialMenu = true;
            RefreshYarnSelectExtensionOptions();
            _settings.RadialMenu ??= new RadialMenuSettings();
            _settings.RadialMenu.Pages ??= [];
            if (_settings.RadialMenu.Pages.Count == 0)
            {
                _settings.RadialMenu.Pages.Add(new RadialMenuPageSettings { Id = "default", Name = "全局" });
            }

            if (_settings.RadialMenu.Pages.All(page => !page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.RadialMenu.SelectedPageId = _settings.RadialMenu.Pages[0].Id;
            }

            RadialMenuPages.Clear();
            var allPages = _settings.RadialMenu.Pages;
            var pageMap = allPages.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var childIdsSet = _settings.RadialMenu.GetChildPageIdsSet();
            var rootPages = allPages.Where(p => !childIdsSet.Contains(p.Id)).ToList();
            if (rootPages.Count == 0 && allPages.Count > 0)
            {
                rootPages = [allPages[0]];
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddPageHierarchy(RadialMenuPageSettings page, int level)
            {
                if (visited.Contains(page.Id)) return;
                visited.Add(page.Id);

                var isAppPage = !string.IsNullOrEmpty(page.ContextProcessName);
                var icon = isAppPage ? GetProcessIcon(page.ContextProcessName!) : null;
                string prefix = level switch
                {
                    0 => "",
                    1 => "└─ ",
                    2 => "   └─ ",
                    3 => "      └─ ",
                    _ => new string(' ', (level - 1) * 3) + "└─ "
                };
                string dispName = prefix + page.Name;

                RadialMenuPages.Add(new RadialMenuPageEditorItem(page.Id, page.Name, icon, isAppPage, level, dispName));

                if (page.ChildPageIds != null)
                {
                    foreach (var childId in page.ChildPageIds)
                    {
                        if (!string.IsNullOrWhiteSpace(childId) && pageMap.TryGetValue(childId, out var childPage))
                        {
                            AddPageHierarchy(childPage, level + 1);
                        }
                    }
                }
            }

            foreach (var root in rootPages)
            {
                AddPageHierarchy(root, 0);
            }

            foreach (var page in allPages)
            {
                if (!visited.Contains(page.Id))
                {
                    AddPageHierarchy(page, 0);
                }
            }

            RadialMenuChildPageOptions.Clear();
            RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(string.Empty, "不进入子环", null, false, 0, "不进入子环"));
            foreach (var item in RadialMenuPages)
            {
                RadialMenuChildPageOptions.Add(new RadialMenuPageEditorItem(item.Id, item.Name, null, false, item.Level, item.DisplayName));
            }

            var selectedPage = _settings.RadialMenu.Pages.First(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase));
            selectedPage.Slots ??= [];
            selectedPage.SlotTitles ??= [];
            selectedPage.ChildPageIds ??= [];
            while (selectedPage.Slots.Count < RadialMenuSettings.TotalSlotCount) selectedPage.Slots.Add(null);
            while (selectedPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) selectedPage.SlotTitles.Add(null);
            while (selectedPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) selectedPage.ChildPageIds.Add(null);

            RadialMenuSlots.Clear();
            BuildRadialPreviewSeparators();
            var center = 180.0;
            var runtimeItems = _mainWindow.GetRadialMenuItems(selectedPage.Id);
            for (var index = 0; index < RadialMenuSettings.TotalSlotCount; index++)
            {
                double step, offset, radius, innerR, outerR, startAngleDegrees;
                bool isOuter;

                if (index < RadialMenuSettings.InnerSlotCount)
                {
                    // 第 1 层：内圈 8 方向
                    step = 45.0;
                    offset = index;
                    radius = 58.0;
                    innerR = 35.0;
                    outerR = 85.0;
                    startAngleDegrees = -112.5 + offset * step;
                    isOuter = false;
                }
                else if (index < RadialMenuSettings.InnerSlotCount + RadialMenuSettings.MiddleSlotCount)
                {
                    // 第 2 层：中圈 16 槽位
                    step = 22.5;
                    offset = index - RadialMenuSettings.InnerSlotCount;
                    radius = 110.0;
                    innerR = 85.0;
                    outerR = 135.0;
                    startAngleDegrees = -101.25 + offset * step;
                    isOuter = true;
                }
                else
                {
                    // 第 3 层：最外圈 8 方向
                    step = 45.0;
                    offset = index - (RadialMenuSettings.InnerSlotCount + RadialMenuSettings.MiddleSlotCount);
                    radius = 158.0;
                    innerR = 135.0;
                    outerR = 180.0;
                    startAngleDegrees = -112.5 + offset * step;
                    isOuter = true;
                }

                var angle = (-90 + offset * step) * Math.PI / 180.0;
                var runtimeItem = runtimeItems.ElementAtOrDefault(index);
                var runtimeCommand = runtimeItem?.Command;
                var childPageId = selectedPage.ChildPageIds.ElementAtOrDefault(index) ?? string.Empty;
                var hasChildPage = !string.IsNullOrWhiteSpace(childPageId);
                var extTitle = !string.IsNullOrWhiteSpace(childPageId) && runtimeCommand == null
                    ? string.Empty
                    : ResolveRadialExtensionTitle(
                        selectedPage.Slots.ElementAtOrDefault(index),
                        selectedPage.SlotTitles.ElementAtOrDefault(index));
                RadialMenuSlots.Add(new RadialMenuSlotEditorItem(
                    index,
                    selectedPage.Slots.ElementAtOrDefault(index) ?? string.Empty,
                    selectedPage.SlotTitles.ElementAtOrDefault(index) ?? string.Empty,
                    childPageId,
                    extTitle,
                    ResolveRadialChildPageTitle(childPageId),
                    center + Math.Cos(angle) * radius - (isOuter ? 26 : 32),
                    center + Math.Sin(angle) * radius - (isOuter ? 20 : 25),
                    isOuter,
                    BuildRadialSectorGeometry(center, center, innerR, outerR, startAngleDegrees, step),
                    runtimeCommand?.IconSource,
                    runtimeCommand?.VectorIcon,
                    runtimeCommand?.AccentBrush ?? (hasChildPage ? ResolveRadialChildPageAccentBrush() : System.Windows.Media.Brushes.Transparent),
                    runtimeCommand?.DisplayGlyph ?? (hasChildPage ? "›" : string.Empty)));
            }

            OnPropertyChanged(nameof(SelectedRadialMenuPageName));
        }
        finally
        {
            _isRefreshingRadialMenu = false;
            // 直接通过SelectedIndex设置ComboBox选中项，
            // 避免SelectedValue/SelectedValuePath在ItemTemplate场景下不渲染选择框的WPF问题。
            SyncRadialMenuPageComboBoxSelection();
        }
    }

    /// <summary>
    /// 将ComboBox的选中项同步为当前设置中保存的SelectedPageId。
    /// 通过SelectedIndex而非SelectedValue来设置，确保ItemTemplate正确渲染。
    /// </summary>
    private void SyncRadialMenuPageComboBoxSelection()
    {
    }

    private void RadialMenuPageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static string? FindExecutablePath(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        var exeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        try
        {
            var nameOnly = Path.GetFileNameWithoutExtension(exeName);
            var processes = System.Diagnostics.Process.GetProcessesByName(nameOnly);
            if (processes.Length > 0)
            {
                var path = processes[0].MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch { }

        string[] registryKeys = [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\App Paths"
        ];
        foreach (var keyPath in registryKeys)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Path.Combine(keyPath, exeName))
                             ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Path.Combine(keyPath, exeName));
                if (key != null)
                {
                    var path = key.GetValue("")?.ToString();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
        }

        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var p in pathEnv.Split(';'))
                {
                    var fullPath = Path.Combine(p.Trim(), exeName);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
        }
        catch { }

        string[] systemDirs = [
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        ];
        foreach (var dir in systemDirs)
        {
            try
            {
                var fullPath = Path.Combine(dir, exeName);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch { }
        }

        return null;
    }

    private static ImageSource? GetProcessIcon(string processName)
    {
        try
        {
            var path = FindExecutablePath(processName);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return ExtensionIconLibrary.TryExtractAssociatedIcon(path);
            }
        }
        catch { }
        return null;
    }

    private void BuildRadialPreviewSeparators()
    {
        RadialMenuPreviewSeparators.Clear();
        const double center = 180.0;
        // 第 1 层：内圈 8 条
        for (var index = 0; index < RadialMenuSettings.InnerSlotCount; index++)
        {
            var angle = (-112.5 + index * 45.0) * Math.PI / 180.0;
            RadialMenuPreviewSeparators.Add(new RadialSeparatorViewModel(
                center + Math.Cos(angle) * 35.0,
                center + Math.Sin(angle) * 35.0,
                center + Math.Cos(angle) * 85.0,
                center + Math.Sin(angle) * 85.0));
        }

        // 第 2 层：中圈 16 条
        for (var index = 0; index < RadialMenuSettings.MiddleSlotCount; index++)
        {
            var angle = (-101.25 + index * 22.5) * Math.PI / 180.0;
            RadialMenuPreviewSeparators.Add(new RadialSeparatorViewModel(
                center + Math.Cos(angle) * 85.0,
                center + Math.Sin(angle) * 85.0,
                center + Math.Cos(angle) * 135.0,
                center + Math.Sin(angle) * 135.0));
        }

        // 第 3 层：外圈 8 条
        for (var index = 0; index < RadialMenuSettings.OuterSlotCount; index++)
        {
            var angle = (-112.5 + index * 45.0) * Math.PI / 180.0;
            RadialMenuPreviewSeparators.Add(new RadialSeparatorViewModel(
                center + Math.Cos(angle) * 135.0,
                center + Math.Sin(angle) * 135.0,
                center + Math.Cos(angle) * 180.0,
                center + Math.Sin(angle) * 180.0));
        }
    }

    private static Geometry BuildRadialSectorGeometry(double centerX, double centerY, double innerRadius, double outerRadius, double startAngleDegrees, double sweepDegrees)
    {
        static System.Windows.Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
        {
            var radians = angleDegrees * Math.PI / 180.0;
            return new System.Windows.Point(
                cx + Math.Cos(radians) * radius,
                cy + Math.Sin(radians) * radius);
        }

        var endAngleDegrees = startAngleDegrees + sweepDegrees;
        var outerStart = PointOnCircle(centerX, centerY, outerRadius, startAngleDegrees);
        var outerEnd = PointOnCircle(centerX, centerY, outerRadius, endAngleDegrees);
        var innerEnd = PointOnCircle(centerX, centerY, innerRadius, endAngleDegrees);
        var innerStart = PointOnCircle(centerX, centerY, innerRadius, startAngleDegrees);
        var isLargeArc = sweepDegrees >= 180.0;

        var figure = new PathFigure
        {
            StartPoint = outerStart,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment(outerEnd, new System.Windows.Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new System.Windows.Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private void SaveRadialMenuSlots()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        if (RadialMenuSlots.Count == 0)
        {
            HostAssets.AppendLog("Settings: skipped saving radial slots because the radial editor has not loaded any slot items.");
            return;
        }

        var selectedPage = _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(_settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase));
        if (selectedPage == null)
        {
            return;
        }

        selectedPage.Slots = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item.ExtensionId.Trim())
            .Cast<string?>()
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
        selectedPage.SlotTitles = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.DisplayTitle) ? null : item.DisplayTitle.Trim())
            .Cast<string?>()
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
        selectedPage.ChildPageIds = RadialMenuSlots
            .OrderBy(static item => item.Index)
            .Select(static item => string.IsNullOrWhiteSpace(item.ChildPageId) ? null : item.ChildPageId.Trim())
            .Cast<string?>()
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
        while (selectedPage.Slots.Count < RadialMenuSettings.TotalSlotCount)
        {
            selectedPage.Slots.Add(null);
        }

        while (selectedPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount)
        {
            selectedPage.SlotTitles.Add(null);
        }

        while (selectedPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount)
        {
            selectedPage.ChildPageIds.Add(null);
        }
        var firstPageSlots = _settings.RadialMenu.Pages[0].Slots ?? [];
        _settings.RadialMenu.Slots = firstPageSlots
            .Concat(Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount))
            .Take(RadialMenuSettings.TotalSlotCount)
            .ToList();
    }

    private static bool IsRadialMenuCommandCandidate(CommandItem command)
    {
        return command.Source is CommandSource.LocalExtension or CommandSource.WebSearch or CommandSource.Application or CommandSource.Local;
    }

    private void RefreshRadialMenuCommandCandidates(string? keyword)
    {
        FilteredRadialMenuCommandOptions.Clear();
        keyword = (keyword ?? string.Empty).Trim();
        var candidates = RadialMenuExtensionOptions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            candidates = candidates.Where(option =>
                option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var option in candidates.Take(40))
        {
            FilteredRadialMenuCommandOptions.Add(option);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var fileResults = EverythingSearchService.Search(keyword, 20);
            if (fileResults.Success)
            {
                foreach (var result in fileResults.Results)
                {
                    var command = BuildRadialFileCommand(result);
                    if (FilteredRadialMenuCommandOptions.Any(option =>
                            option.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    FilteredRadialMenuCommandOptions.Add(new YarnSelectExtensionOption(command));
                }
            }
        }
    }

    private void SelectRadialMenuSlot(RadialMenuSlotEditorItem slot)
    {
        _selectedRadialMenuSlot = slot;
        OnPropertyChanged(nameof(RadialMenuSelectedSlotSummary));
    }

    private static string FormatRadialTraceValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    }

    private static string GetRadialTraceListValue(IReadOnlyList<string?>? values, int index)
    {
        if (values == null || index < 0 || index >= values.Count)
        {
            return string.Empty;
        }

        return values[index] ?? string.Empty;
    }

    private static string FirstRadialTraceValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string DescribeRadialTraceSlot(RadialMenuSlotEditorItem? slot)
    {
        if (slot == null)
        {
            return "(slot missing)";
        }

        return $"index={slot.Index + 1}, ext={FormatRadialTraceValue(slot.ExtensionId)}, title={FormatRadialTraceValue(slot.DisplayTitle)}, child={FormatRadialTraceValue(slot.ChildPageId)}, childTitle={FormatRadialTraceValue(slot.ChildPageTitle)}";
    }

    private static string DescribeRadialTracePageSlot(RadialMenuPageSettings? page, int index)
    {
        if (page == null)
        {
            return "(page missing)";
        }

        return $"pageId={FormatRadialTraceValue(page.Id)}, pageName={FormatRadialTraceValue(page.Name)}, slot={index + 1}, ext={FormatRadialTraceValue(GetRadialTraceListValue(page.Slots, index))}, title={FormatRadialTraceValue(GetRadialTraceListValue(page.SlotTitles, index))}, child={FormatRadialTraceValue(GetRadialTraceListValue(page.ChildPageIds, index))}";
    }

    private static HashSet<string> CollectRadialChildPageTreeIds(RadialMenuSettings radial, string rootPageId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootPageId) || radial.Pages == null)
        {
            return result;
        }

        var stack = new Stack<string>();
        stack.Push(rootPageId.Trim());
        while (stack.Count > 0)
        {
            var pageId = stack.Pop();
            if (!result.Add(pageId))
            {
                continue;
            }

            var page = radial.Pages.FirstOrDefault(item => item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
            if (page?.ChildPageIds == null)
            {
                continue;
            }

            foreach (var childPageId in page.ChildPageIds)
            {
                if (!string.IsNullOrWhiteSpace(childPageId))
                {
                    stack.Push(childPageId.Trim());
                }
            }
        }

        return result;
    }

    private RadialMenuSlotEditorItem? ResolveRadialSlotFromMenuSender(object sender)
    {
        DependencyObject? current = sender as DependencyObject;
        while (current != null)
        {
            if (current is ContextMenu { PlacementTarget: FrameworkElement { DataContext: RadialMenuSlotEditorItem slot } })
            {
                return slot;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        if (sender is FrameworkElement { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: RadialMenuSlotEditorItem fallbackSlot } } })
        {
            return fallbackSlot;
        }

        HostAssets.AppendLog($"Settings radial slot resolve fallback: sender={sender?.GetType().Name ?? "(null)"}, fallback={DescribeRadialTraceSlot(_selectedRadialMenuSlot)}.");
        return _selectedRadialMenuSlot;
    }

    private void ApplyRadialMenuCommandToSlot(RadialMenuSlotEditorItem slot, YarnSelectExtensionOption option)
    {
        if (string.IsNullOrWhiteSpace(option.ExtensionId))
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        slot.ExtensionId = option.ExtensionId;
        slot.DisplayTitle = string.Empty;
        slot.ExtensionTitle = option.Title;
        UpdateRadialSlotPresentation(slot);
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private static CommandItem BuildRadialFileCommand(EverythingSearchResult result)
    {
        var subtitle = string.IsNullOrWhiteSpace(result.SizeText)
            ? result.DirectoryPath
            : $"{result.DirectoryPath}   ·   {result.SizeText}";
        return new CommandItem(
            glyph: result.IsFolder ? "夹" : "文",
            title: result.Name,
            subtitle: subtitle,
            category: result.IsFolder ? "文件夹" : "文件",
            accentHex: result.IsFolder ? "#FF3B82F6" : "#FF4B5563",
            openTarget: result.FullPath,
            keywords: [result.FullPath, result.DirectoryPath, result.Name],
            source: CommandSource.File,
            extensionId: $"{ExtensionIdPrefixes.SearchResult}{result.FullPath}",
            resultKind: result.IsFolder ? ResultItemKind.Folder : ResultItemKind.File,
            resultProviderTitle: "Everything 文件",
            iconSourceOverride: NativeFileIconService.GetIcon(result.FullPath, result.IsFolder));
    }

    private void AddRadialMenuPageButton_Click(object sender, RoutedEventArgs e)
    {
        SaveRadialMenuSlots();
        _settings.RadialMenu ??= new RadialMenuSettings();
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"页面 {_settings.RadialMenu.Pages.Count + 1}"
        };
        _settings.RadialMenu.Pages.Add(page);
        _settings.RadialMenu.SelectedPageId = page.Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
    }

    private void DeleteRadialMenuPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.RadialMenu.Pages.Count <= 1)
        {
            return;
        }

        var removePageId = ResolveSelectedRadialMenuPageIdFromEditor();
        var currentPage = _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(removePageId, StringComparison.OrdinalIgnoreCase));
        if (currentPage == null)
        {
            return;
        }

        var pageName = currentPage?.Name ?? "当前轮盘";
        var result = System.Windows.MessageBox.Show(
            $"确定要删除轮盘“{pageName}”吗？\n删除后该轮盘配置将无法恢复。",
            "确认删除轮盘",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var removedId = removePageId;
        var parentPageId = _settings.RadialMenu.Pages.FirstOrDefault(page =>
            page.ChildPageIds?.Any(id => string.Equals(id, removedId, StringComparison.OrdinalIgnoreCase)) == true)?.Id;
        _settings.RadialMenu.CascadeDeletePages([removedId]);

        var remainingChildIds = _settings.RadialMenu.GetChildPageIdsSet();
        var fallbackPage =
            (!string.IsNullOrWhiteSpace(parentPageId)
                ? _settings.RadialMenu.Pages.FirstOrDefault(page => page.Id.Equals(parentPageId, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? _settings.RadialMenu.Pages.FirstOrDefault(page =>
                string.IsNullOrWhiteSpace(page.ContextProcessName) && !remainingChildIds.Contains(page.Id))
            ?? _settings.RadialMenu.Pages.FirstOrDefault(page => !remainingChildIds.Contains(page.Id))
            ?? _settings.RadialMenu.Pages.FirstOrDefault();
        if (fallbackPage == null)
        {
            return;
        }

        _settings.RadialMenu.SelectedPageId = fallbackPage.Id;
        RefreshRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
    }

    private string ResolveSelectedRadialMenuPageIdFromEditor()
    {
        return SelectedRadialMenuPageId;
    }

    private void RadialMenuCenter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        RenameCurrentRadialMenuPage();
        e.Handled = true;
    }

    private void RadialMenuCenter_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void RenameRadialMenuPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RenameCurrentRadialMenuPage();
    }

    private void RenameCurrentRadialMenuPage()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var pageId = SelectedRadialMenuPageId;
        var page = _settings.RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            HostAssets.AppendLog($"Radial rename skipped: selected page not found, pageId={pageId}.");
            return;
        }

        var oldName = page.Name;
        var dialog = new SimpleTextInputWindow("重命名轮盘", "输入新的轮盘名称。", page.Name)
        {
            Owner = this
        };
        bool accepted;
        try
        {
            _isRenamingRadialMenuPage = true;
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            _isRenamingRadialMenuPage = false;
        }

        if (!accepted)
        {
            HostAssets.AppendLog($"Radial rename cancelled: pageId={pageId}, oldName={oldName}.");
            return;
        }

        var trimmedName = dialog.ValueText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            HostAssets.AppendLog($"Radial rename ignored empty name: pageId={pageId}, oldName={oldName}.");
            return;
        }

        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        page = _settings.RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
        if (page == null)
        {
            HostAssets.AppendLog($"Radial rename failed after dialog: page missing, pageId={pageId}, newName={trimmedName}.");
            return;
        }

        page.Name = trimmedName;
        HostAssets.AppendLog($"Radial rename saving: pageId={pageId}, oldName={oldName}, newName={trimmedName}.");
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
        OnPropertyChanged(nameof(SelectedRadialMenuPageName));
        var saved = AppSettingsStore.Load().RadialMenu.Pages.FirstOrDefault(item =>
            item.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name ?? string.Empty;
        HostAssets.AppendLog($"Radial rename saved: pageId={pageId}, savedName={saved}, currentName={SelectedRadialMenuPageName}.");
    }

    private void RadialExtensionDragStart_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: YarnSelectExtensionOption option } ||
            string.IsNullOrWhiteSpace(option.ExtensionId))
        {
            return;
        }

        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, option.ExtensionId, System.Windows.DragDropEffects.Copy);
    }

    private void RadialSlot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            SelectRadialMenuSlot(slot);
            if (slot.HasChildPageTitle && !string.IsNullOrWhiteSpace(slot.ChildPageId))
            {
                SelectedRadialMenuPageId = slot.ChildPageId;
                e.Handled = true;
                return;
            }

            if (slot.IsEmpty)
            {
                OpenRadialSlotPicker(slot);
                e.Handled = true;
                return;
            }

            var command = ResolveRadialSlotCommand(slot);
            if (command != null)
            {
                _mainWindow.ExecuteCommandExternally(command, launchSource: "settings-radial-preview");
                e.Handled = true;
            }
        }
    }

    private void RadialSlot_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            SelectRadialMenuSlot(slot);
        }
    }

    private void RadialSlot_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void RadialSlot_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: RadialMenuSlotEditorItem slot } ||
            !e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat))
        {
            return;
        }

        slot.ExtensionId = e.Data.GetData(System.Windows.DataFormats.StringFormat) as string ?? string.Empty;
        slot.DisplayTitle = string.Empty;
        slot.ExtensionTitle = ResolveRadialExtensionTitle(slot.ExtensionId);
        UpdateRadialSlotPresentation(slot);
        e.Handled = true;
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void RadialSlot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            slot.IsHovered = true;
            SelectRadialMenuSlot(slot);
        }
    }

    private void RadialSlot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RadialMenuSlotEditorItem slot })
        {
            slot.IsHovered = false;
        }
    }

    private void RadialSlotAddCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        OpenRadialSlotPicker(slot);
    }

    private void RadialSlotSetSimulatedKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        var initialShortcut = slot.ExtensionId.StartsWith(RadialSimulatedKeyPrefix, StringComparison.OrdinalIgnoreCase)
            ? slot.ExtensionId[RadialSimulatedKeyPrefix.Length..]
            : string.Empty;
        var dialog = new HotkeyCaptureWindow(
            "模拟按键",
            "录制要在此槽位执行的组合键，并设置轮盘里显示的名称。",
            initialShortcut,
            slot.DisplayTitle,
            allowEmpty: false,
            allowDoubleTap: false,
            allowModifierless: true)
        {
            Owner = this
        };
        bool? dialogResult;
        _suspendActivationRefresh = true;
        try
        {
            dialogResult = dialog.ShowDialog() == true;
        }
        finally
        {
            _suspendActivationRefresh = false;
        }

        if (dialogResult != true)
        {
            return;
        }

        var shortcut = dialog.ShortcutText.Trim();
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return;
        }

        var currentSlot = RadialMenuSlots.FirstOrDefault(item => item.Index == slot.Index) ?? slot;
        SelectRadialMenuSlot(currentSlot);
        currentSlot.ExtensionId = $"{RadialSimulatedKeyPrefix}{shortcut}";
        currentSlot.DisplayTitle = string.IsNullOrWhiteSpace(dialog.DisplayNameText)
            ? shortcut
            : dialog.DisplayNameText.Trim();
        currentSlot.ExtensionTitle = ResolveRadialExtensionTitle(currentSlot.ExtensionId, currentSlot.DisplayTitle);
        HostAssets.AppendLog($"Radial simulated key assigned: slot={slot.Index + 1}, shortcut={shortcut}, displayTitle={currentSlot.DisplayTitle}, page={SelectedRadialMenuPageId}.");
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private void RadialSlotClearCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        DeleteRadialSlotContent(slot);
    }

    private void RadialSlotEnterChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot != null && slot.HasChildPageTitle && !string.IsNullOrWhiteSpace(slot.ChildPageId))
        {
            SelectedRadialMenuPageId = slot.ChildPageId;
        }
    }

    private void RadialSlotAddChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        SelectRadialMenuSlot(slot);
        CreateRadialChildPageForSlot(slot, GetNextRadialChildPageName(), SelectedRadialMenuPageId);
    }

    private void CreateRadialChildPageForSlot(RadialMenuSlotEditorItem slot, string pageName, string? parentPageId = null)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var effectiveParentPageId = string.IsNullOrWhiteSpace(parentPageId)
            ? SelectedRadialMenuPageId
            : parentPageId.Trim();
        var parentPage = _settings.RadialMenu.Pages.FirstOrDefault(page =>
            page.Id.Equals(effectiveParentPageId, StringComparison.OrdinalIgnoreCase));
        if (parentPage == null)
        {
            HostAssets.AppendLog($"Settings radial child page add skipped: parent page missing, parent={effectiveParentPageId}, slot={slot.Index + 1}.");
            return;
        }

        parentPage.Slots ??= [];
        parentPage.SlotTitles ??= [];
        parentPage.ChildPageIds ??= [];
        while (parentPage.Slots.Count < RadialMenuSettings.TotalSlotCount) parentPage.Slots.Add(null);
        while (parentPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) parentPage.SlotTitles.Add(null);
        while (parentPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) parentPage.ChildPageIds.Add(null);
        if (!string.IsNullOrWhiteSpace(parentPage.ChildPageIds[slot.Index]))
        {
            HostAssets.AppendLog($"Settings radial child page add skipped: slot already has child, parent={effectiveParentPageId}, slot={slot.Index + 1}.");
            return;
        }

        var childPageName = pageName.Trim();
        var page = new RadialMenuPageSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = childPageName
        };
        _settings.RadialMenu.Pages.Add(page);
        parentPage.ChildPageIds[slot.Index] = page.Id;
        _settings.RadialMenu.SelectedPageId = effectiveParentPageId;

        var currentSlot = RadialMenuSlots.FirstOrDefault(item => item.Index == slot.Index) ?? slot;
        currentSlot.ChildPageId = page.Id;
        currentSlot.ChildPageTitle = childPageName;
        UpdateRadialSlotPresentation(currentSlot);

        SaveRadialMenuSlots();
        SaveQuickPanelTriggerSettings();
        RefreshRadialMenuSlots();
    }

    private async void OpenRadialSlotPicker(RadialMenuSlotEditorItem slot)
    {
        var parentPageId = SelectedRadialMenuPageId;
        _suspendActivationRefresh = true;
        try
        {
            var result = await _mainWindow.ShowForRadialPickerAsync(!slot.HasChildPageTitle);
            if (result == null)
            {
                return;
            }

            if (result.Action == RadialSlotPickerWindow.PickerAction.AddChildPage)
            {
                CreateRadialChildPageForSlot(slot, GetNextRadialChildPageName(), parentPageId);
                return;
            }

            if (result.Command == null)
            {
                return;
            }

            ApplyRadialMenuCommandToSlot(slot, new YarnSelectExtensionOption(result.Command));
        }
        finally
        {
            _suspendActivationRefresh = false;
        }
    }

    private CommandItem? ResolveRadialSlotCommand(RadialMenuSlotEditorItem slot)
    {
        return _mainWindow
            .GetRadialMenuItems(SelectedRadialMenuPageId)
            .ElementAtOrDefault(slot.Index)?
            .Command;
    }

    private string GetNextRadialChildPageName()
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];
        var usedNumbers = _settings.RadialMenu.Pages
            .Select(page => page.Name ?? string.Empty)
            .Select(name =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(name, @"^子环\s*(\d+)$");
                return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : (int?)null;
            })
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToHashSet();
        var next = 1;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        return $"子环 {next}";
    }

    private void UpdateRadialSlotPresentation(RadialMenuSlotEditorItem slot)
    {
        var command = ResolveRadialSlotCommand(slot);
        if (command != null)
        {
            slot.IconSource = command.IconSource;
            slot.VectorIcon = command.VectorIcon;
            slot.AccentBrush = command.AccentBrush;
            slot.DisplayGlyph = command.DisplayGlyph;
            slot.ExtensionTitle = command.Title;
            return;
        }

        if (slot.HasChildPageTitle)
        {
            slot.IconSource = null;
            slot.VectorIcon = null;
            slot.AccentBrush = ResolveRadialChildPageAccentBrush();
            slot.DisplayGlyph = "›";
            slot.ExtensionTitle = string.Empty;
            return;
        }

        slot.IconSource = null;
        slot.VectorIcon = null;
        slot.AccentBrush = System.Windows.Media.Brushes.Transparent;
        slot.DisplayGlyph = string.Empty;
    }

    private void RadialSlotClearChildPageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        if (slot == null)
        {
            return;
        }

        DeleteRadialSlotContent(slot);
    }

    private void RadialSlotDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var slot = ResolveRadialSlotFromMenuSender(sender);
        HostAssets.AppendLog($"Settings radial delete menu clicked: selectedPage={FormatRadialTraceValue(_settings.RadialMenu?.SelectedPageId)}, resolvedSlot={DescribeRadialTraceSlot(slot)}.");
        if (slot == null)
        {
            return;
        }

        DeleteRadialSlotContent(slot);
    }

    private void DeleteRadialSlotContent(RadialMenuSlotEditorItem slot)
    {
        _settings.RadialMenu ??= new RadialMenuSettings();
        _settings.RadialMenu.Pages ??= [];

        var editorPageId = ResolveSelectedRadialMenuPageIdFromEditor();
        var currentSlot = RadialMenuSlots.FirstOrDefault(item => item.Index == slot.Index) ?? slot;
        var slotIndex = currentSlot.Index;
        var currentPage = string.IsNullOrWhiteSpace(editorPageId)
            ? null
            : _settings.RadialMenu.Pages.FirstOrDefault(page =>
                page.Id.Equals(editorPageId, StringComparison.OrdinalIgnoreCase));

        HostAssets.AppendLog($"Settings radial delete requested: editorPage={FormatRadialTraceValue(editorPageId)}, settingsSelectedPage={FormatRadialTraceValue(_settings.RadialMenu.SelectedPageId)}, slotCount={RadialMenuSlots.Count}, pageCount={_settings.RadialMenu.Pages.Count}, sourceSlot={DescribeRadialTraceSlot(slot)}, currentSlot={DescribeRadialTraceSlot(currentSlot)}, pageSlotBefore={DescribeRadialTracePageSlot(currentPage, slotIndex)}.");

        if (string.IsNullOrWhiteSpace(editorPageId))
        {
            HostAssets.AppendLog("Settings radial delete skipped: editor page id is empty.");
            return;
        }

        if (slotIndex < 0 || slotIndex >= RadialMenuSettings.TotalSlotCount)
        {
            HostAssets.AppendLog($"Settings radial delete skipped: slot index out of range, slot={slotIndex}.");
            return;
        }

        if (currentPage == null)
        {
            HostAssets.AppendLog($"Settings radial delete skipped: page not found, editorPage={editorPageId}.");
            return;
        }

        _settings.RadialMenu.SelectedPageId = editorPageId;
        currentPage.Slots ??= [];
        currentPage.SlotTitles ??= [];
        currentPage.ChildPageIds ??= [];
        while (currentPage.Slots.Count < RadialMenuSettings.TotalSlotCount) currentPage.Slots.Add(null);
        while (currentPage.SlotTitles.Count < RadialMenuSettings.TotalSlotCount) currentPage.SlotTitles.Add(null);
        while (currentPage.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount) currentPage.ChildPageIds.Add(null);

        var pageExtensionId = GetRadialTraceListValue(currentPage.Slots, slotIndex);
        var pageSlotTitle = GetRadialTraceListValue(currentPage.SlotTitles, slotIndex);
        var pageChildPageId = GetRadialTraceListValue(currentPage.ChildPageIds, slotIndex);
        var removedExtensionId = FirstRadialTraceValue(currentSlot.ExtensionId, pageExtensionId);
        var removedChildPageId = FirstRadialTraceValue(currentSlot.ChildPageId, pageChildPageId);
        var removedDisplayTitle = FirstRadialTraceValue(currentSlot.DisplayTitle, pageSlotTitle);
        var hasCommand = !string.IsNullOrWhiteSpace(removedExtensionId) || !string.IsNullOrWhiteSpace(removedDisplayTitle);
        var hasChildPage = !string.IsNullOrWhiteSpace(removedChildPageId);
        if (!hasCommand && !hasChildPage)
        {
            HostAssets.AppendLog($"Settings radial delete skipped: slot is empty after checking editor and settings, pageSlot={DescribeRadialTracePageSlot(currentPage, slotIndex)}.");
            return;
        }

        var message = hasChildPage
            ? "确定要删除该槽位吗？\n这会同时删除当前槽位里的子环。"
            : "确定要删除该槽位吗？";
        var result = System.Windows.MessageBox.Show(
            message,
            "确认删除",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            HostAssets.AppendLog($"Settings radial delete cancelled: editorPage={editorPageId}, slot={slotIndex + 1}, child={FormatRadialTraceValue(removedChildPageId)}, ext={FormatRadialTraceValue(removedExtensionId)}.");
            return;
        }

        var removedChildPageIds = hasChildPage
            ? CollectRadialChildPageTreeIds(_settings.RadialMenu, removedChildPageId)
            : [];
        var pagesBefore = _settings.RadialMenu.Pages.Count;
        HostAssets.AppendLog($"Settings radial delete applying: editorPage={editorPageId}, slot={slotIndex + 1}, ext={FormatRadialTraceValue(removedExtensionId)}, child={FormatRadialTraceValue(removedChildPageId)}, childTreeCount={removedChildPageIds.Count}, pageSlotBefore={DescribeRadialTracePageSlot(currentPage, slotIndex)}.");

        SelectRadialMenuSlot(currentSlot);
        var slotsToClear = RadialMenuSlots.Where(item => item.Index == slotIndex).ToList();
        if (!slotsToClear.Any(item => ReferenceEquals(item, slot)))
        {
            slotsToClear.Add(slot);
        }

        foreach (var editorSlot in slotsToClear.Distinct())
        {
            editorSlot.ExtensionId = string.Empty;
            editorSlot.DisplayTitle = string.Empty;
            editorSlot.ChildPageId = string.Empty;
            editorSlot.ChildPageTitle = string.Empty;
            editorSlot.ExtensionTitle = string.Empty;
            UpdateRadialSlotPresentation(editorSlot);
        }

        currentPage.Slots[slotIndex] = null;
        currentPage.SlotTitles[slotIndex] = null;
        currentPage.ChildPageIds[slotIndex] = null;

        var removedPageCount = 0;
        if (removedChildPageIds.Count > 0)
        {
            removedPageCount = _settings.RadialMenu.Pages.RemoveAll(page => removedChildPageIds.Contains(page.Id));
            foreach (var page in _settings.RadialMenu.Pages)
            {
                if (page.ChildPageIds == null)
                {
                    continue;
                }

                for (int i = 0; i < page.ChildPageIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(page.ChildPageIds[i]) && removedChildPageIds.Contains(page.ChildPageIds[i]!))
                    {
                        page.ChildPageIds[i] = null;
                    }
                }
            }
        }

        _settings.RadialMenu.SelectedPageId = editorPageId;
        SaveQuickPanelTriggerSettings();
        var savedSettings = AppSettingsStore.Load();
        var savedRadial = savedSettings.RadialMenu ?? new RadialMenuSettings();
        var savedPage = savedRadial.Pages.FirstOrDefault(page =>
            page.Id.Equals(editorPageId, StringComparison.OrdinalIgnoreCase));
        var savedChildPageId = GetRadialTraceListValue(savedPage?.ChildPageIds, slotIndex);
        var deletedChildStillExists = removedChildPageIds.Count > 0 &&
            savedRadial.Pages.Any(page => removedChildPageIds.Contains(page.Id));
        HostAssets.AppendLog($"Settings radial delete saved: editorPage={editorPageId}, slot={slotIndex + 1}, pagesBefore={pagesBefore}, pagesAfter={savedRadial.Pages.Count}, removedPageCount={removedPageCount}, savedChild={FormatRadialTraceValue(savedChildPageId)}, deletedChildStillExists={deletedChildStillExists}, savedPageSlot={DescribeRadialTracePageSlot(savedPage, slotIndex)}.");
        if (!string.IsNullOrWhiteSpace(savedChildPageId) || deletedChildStillExists)
        {
            HostAssets.AppendLog($"Settings radial delete failed verification: editorPage={editorPageId}, slot={slotIndex + 1}, expectedChildCleared={FormatRadialTraceValue(removedChildPageId)}, savedChild={FormatRadialTraceValue(savedChildPageId)}, deletedChildStillExists={deletedChildStillExists}.");
        }

        RefreshRadialMenuSlots();
    }

    private void RadialMenuSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Down || FilteredRadialMenuCommandOptions.Count == 0)
        {
            return;
        }

        if (FindSiblingListBox(sender as DependencyObject) is { } listBox)
        {
            listBox.SelectedIndex = 0;
            listBox.Focus();
            e.Handled = true;
        }
    }

    private void RadialMenuCommandListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitRadialMenuCommandCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RadialMenuSearchText = string.Empty;
            e.Handled = true;
        }
    }

    private void RadialMenuCommandListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject dep)
        {
            var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(dep);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta / 3.0));
                e.Handled = true;
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject dep) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void RadialMenuCommandListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitRadialMenuCommandCandidate(listBox);
        }
    }

    private void CommitRadialMenuCommandCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (_selectedRadialMenuSlot == null ||
            listBox.SelectedItem is not YarnSelectExtensionOption option)
        {
            return;
        }

        ApplyRadialMenuCommandToSlot(_selectedRadialMenuSlot, option);
    }

    private string ResolveRadialExtensionTitle(string? extensionId, string? displayTitleOverride = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return "拖入小程序";
        }

        if (extensionId.StartsWith(RadialSimulatedKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(displayTitleOverride)
                ? extensionId[RadialSimulatedKeyPrefix.Length..]
                : displayTitleOverride.Trim();
        }

        if (extensionId.StartsWith(ExtensionIdPrefixes.SearchResult, StringComparison.OrdinalIgnoreCase))
        {
            var path = extensionId[ExtensionIdPrefixes.SearchResult.Length..];
            var title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(title) ? path : title;
        }

        return RadialMenuExtensionOptions.FirstOrDefault(option =>
            option.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase))?.Title ?? "未知小程序";
    }


    private string ResolveRadialChildPageTitle(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return string.Empty;
        }

        var name = _settings.RadialMenu?.Pages?.FirstOrDefault(page =>
            page.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase))?.Name;
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }

    private static System.Windows.Media.Brush ResolveRadialChildPageAccentBrush()
    {
        return System.Windows.Application.Current.TryFindResource("BrushRadialChildAccentSector") as System.Windows.Media.Brush
               ?? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF3B82F6")!;
    }

    public Dictionary<string, YarnSelectPreviewKeyItem> YarnSelectPreviewKeyMap { get; } = InitYarnSelectPreviewKeyMap();

    private static Dictionary<string, YarnSelectPreviewKeyItem> InitYarnSelectPreviewKeyMap()
    {
        var keys = new (string key, string name)[]
        {
            ("Right", "右键"), ("X1", "侧键1"), ("X2", "侧键2"),
            ("1", "1"), ("2", "2"), ("3", "3"), ("4", "4"), ("5", "5"),
            ("6", "6"), ("7", "7"), ("8", "8"), ("9", "9"), ("0", "0"),
            ("Q", "Q"), ("W", "W"), ("E", "E"), ("R", "R"), ("T", "T"),
            ("Y", "Y"), ("U", "U"), ("I", "I"), ("O", "O"), ("P", "P"),
            ("A", "A"), ("S", "S"), ("D", "D"), ("F", "F"), ("G", "G"),
            ("H", "H"), ("J", "J"), ("K", "K"), ("L", "L"),
            ("Z", "Z"), ("X", "X"), ("C", "C"), ("V", "V"), ("B", "B"),
            ("N", "N"), ("M", "M")
        };

        var dict = new Dictionary<string, YarnSelectPreviewKeyItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in keys)
        {
            dict[key] = new YarnSelectPreviewKeyItem
            {
                KeyCode = key,
                DisplayName = name,
                RuleSummary = $"【触发键: {name}】\n状态: 未配置\n💡 点击可在右侧/下方快速创建以此键触发的新规则"
            };
        }

        return dict;
    }

    public void RefreshYarnSelectPreviewMap()
    {
        var ruleDict = YarnSelectRules.ToDictionary(
            rule => YarnSelectSettings.NormalizeTriggerKey(rule.TriggerKey),
            rule => rule,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (key, previewItem) in YarnSelectPreviewKeyMap)
        {
            if (ruleDict.TryGetValue(key, out var rule))
            {
                previewItem.IsConfigured = true;
                previewItem.RuleEnabled = rule.Enabled;
                var actionLabel = GetYarnSelectActionLabel(rule.ActionType);
                var descStr = string.IsNullOrWhiteSpace(rule.Description) ? "" : $" ({rule.Description})";
                previewItem.RuleSummary = $"【触发键: {previewItem.DisplayName}】\n状态: {(rule.Enabled ? "🟢 已启用" : "⚪ 已禁用")}\n动作: {actionLabel}{descStr}\n💡 点击可自动高亮定位此规则";
            }
            else
            {
                previewItem.IsConfigured = false;
                previewItem.RuleEnabled = false;
                previewItem.RuleSummary = $"【触发键: {previewItem.DisplayName}】\n状态: 未配置\n💡 点击可在右侧/下方快速创建以此键触发的新规则";
            }
        }
    }

    private void YarnSelectPreviewKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string rawKey })
        {
            var normalized = YarnSelectSettings.NormalizeTriggerKey(rawKey);
            var existing = YarnSelectRules.FirstOrDefault(rule =>
                YarnSelectSettings.NormalizeTriggerKey(rule.TriggerKey).Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var newRule = new YarnSelectRuleItem(new YarnSelectRuleSettings
                {
                    Enabled = true,
                    TriggerKey = normalized,
                    ActionType = YarnSelectActionTypes.Copy,
                    Description = $"{normalized} 触发动作"
                });
                newRule.OnChangedAction = () => { RefreshYarnSelectPreviewMap(); QueueYarnSelectSave(500); };
                ApplyYarnSelectExtensionSelection(newRule);
                YarnSelectRules.Add(newRule);
                SaveYarnSelectSettings();
                existing = newRule;
            }

            foreach (var rule in YarnSelectRules)
            {
                rule.IsHighlighted = (rule == existing);
            }

            RefreshYarnSelectPreviewMap();
        }
    }

    private void AddYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var item = new YarnSelectRuleItem(new YarnSelectRuleSettings
        {
            TriggerKey = "A",
            ActionType = YarnSelectActionTypes.RunExtension,
            Description = "新燕选规则"
        });
        item.OnChangedAction = () => { RefreshYarnSelectPreviewMap(); QueueYarnSelectSave(500); };
        ApplyYarnSelectExtensionSelection(item);
        YarnSelectRules.Add(item);
        OnPropertyChanged(nameof(YarnSelectSummary));
        RefreshYarnSelectPreviewMap();
        QueueYarnSelectSave(200);
    }

    private void DeleteYarnSelectRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            return;
        }

        YarnSelectRules.Remove(item);
        SaveYarnSelectSettings();
        RefreshYarnSelectPreviewMap();
    }

    private void YarnSelectKeyPickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            foreach (var rule in YarnSelectRules)
            {
                if (rule != item)
                {
                    rule.IsKeyPickerOpen = false;
                }
            }

            item.IsKeyPickerOpen = !item.IsKeyPickerOpen;
        }
    }

    private void YarnSelectKeyOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item, Tag: string key })
        {
            item.SelectTriggerKey(key);
            SaveYarnSelectSettings();
        }
    }

    private void CloseYarnSelectKeyPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            item.IsKeyPickerOpen = false;
        }
    }

    private void KeyPickerPopup_Opened(object sender, EventArgs e)
    {
        if (sender is Popup { Child: FrameworkElement element })
        {
            element.Focus();
        }
    }

    private void YarnSelectKeyPickerPopup_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            string? key = null;
            if (e.Key >= System.Windows.Input.Key.A && e.Key <= System.Windows.Input.Key.Z)
            {
                key = e.Key.ToString();
            }
            else if (e.Key >= System.Windows.Input.Key.D0 && e.Key <= System.Windows.Input.Key.D9)
            {
                key = ((char)('0' + (e.Key - System.Windows.Input.Key.D0))).ToString();
            }
            else if (e.Key >= System.Windows.Input.Key.NumPad0 && e.Key <= System.Windows.Input.Key.NumPad9)
            {
                key = ((char)('0' + (e.Key - System.Windows.Input.Key.NumPad0))).ToString();
            }

            if (!string.IsNullOrEmpty(key))
            {
                item.SelectTriggerKey(key);
                SaveYarnSelectSettings();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                item.IsKeyPickerOpen = false;
                e.Handled = true;
            }
        }
    }

    private void ResetYarnSelectRulesButton_Click(object sender, RoutedEventArgs e)
    {
        YarnSelectRules.Clear();
        foreach (var rule in YarnSelectSettings.CreateDefaultRulesFromLegacy(new YarnSelectSettings()))
        {
            var item = new YarnSelectRuleItem(rule);
            ApplyYarnSelectExtensionSelection(item);
            YarnSelectRules.Add(item);
        }

        SaveYarnSelectSettings();
    }

    private static string GetYarnSelectActionLabel(string actionType)
    {
        return YarnSelectActionTypes.Normalize(actionType) switch
        {
            YarnSelectActionTypes.Cut => "剪切",
            YarnSelectActionTypes.Paste => "粘贴",
            YarnSelectActionTypes.Search => "搜索",
            YarnSelectActionTypes.Run => "运行",
            YarnSelectActionTypes.SmartCopyPaste => "智能复制/粘贴",
            YarnSelectActionTypes.RunExtension => "运行小程序",
            _ => "复制"
        };
    }

    private void ApplyYarnSelectExtensionSelection(YarnSelectRuleItem item)
    {
        var selected = YarnSelectExtensionOptions.FirstOrDefault(option =>
            option.ExtensionId.Equals(item.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        item.ExtensionSearchText = selected?.Title ?? string.Empty;
        item.FilteredExtensionOptions = [];
    }

    private void RefreshYarnSelectExtensionCandidates(YarnSelectRuleItem item, string keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();
        var candidates = string.IsNullOrEmpty(keyword)
            ? YarnSelectExtensionOptions.Take(12)
            : YarnSelectExtensionOptions
                .Where(option =>
                    option.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    option.Detail.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(12);

        item.FilteredExtensionOptions = new ObservableCollection<YarnSelectExtensionOption>(candidates);
    }

    private void YarnSelectExtensionDropdownToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is YarnSelectRuleItem item)
        {
            e.Handled = true;
            RefreshYarnSelectExtensionCandidates(item, string.Empty);
            item.IsExtensionPickerOpen = true;

            if (fe.Parent is Grid grid && grid.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault() is { } textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }
    }

    private void YarnSelectExtensionSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.SelectAll();
            }

            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
            item.IsExtensionPickerOpen = true;
        }
    }

    private void YarnSelectExtensionSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: YarnSelectRuleItem item })
        {
            var selected = YarnSelectExtensionOptions.FirstOrDefault(option =>
                option.ExtensionId.Equals(item.ExtensionId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (selected == null ||
                !selected.Title.Equals(item.ExtensionSearchText ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                item.ExtensionId = string.Empty;
            }

            RefreshYarnSelectExtensionCandidates(item, item.ExtensionSearchText ?? string.Empty);
            item.IsExtensionPickerOpen = true;
        }
    }

    private void YarnSelectExtensionSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not DependencyObject source ||
            source is not FrameworkElement { DataContext: YarnSelectRuleItem item } ||
            e.Key != Key.Down ||
            item.FilteredExtensionOptions.Count == 0)
        {
            return;
        }

        var listBox = FindYarnSelectExtensionListBox(source);
        if (listBox != null)
        {
            listBox.SelectedIndex = 0;
            var itemContainer = listBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
            itemContainer?.Focus();
            listBox.Focus();
            e.Handled = true;
        }
    }

    private static System.Windows.Controls.ListBox? FindYarnSelectExtensionListBox(DependencyObject source)
    {
        var parent = VisualTreeHelper.GetParent(source);
        while (parent != null)
        {
            if (parent is Grid grid)
            {
                var popup = grid.Children.OfType<System.Windows.Controls.Primitives.Popup>().FirstOrDefault();
                if (popup?.Child is Border border && border.Child is System.Windows.Controls.ListBox listBox)
                {
                    return listBox;
                }
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void YarnSelectExtensionListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitYarnSelectExtensionCandidate(listBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && listBox.DataContext is YarnSelectRuleItem item)
        {
            item.IsExtensionPickerOpen = false;
            e.Handled = true;
        }
    }

    private void YarnSelectExtensionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox)
        {
            CommitYarnSelectExtensionCandidate(listBox);
        }
    }

    private void CommitYarnSelectExtensionCandidate(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.DataContext is not YarnSelectRuleItem item ||
            listBox.SelectedItem is not YarnSelectExtensionOption option)
        {
            return;
        }

        item.ExtensionId = option.ExtensionId;
        item.ExtensionSearchText = option.Title;
        item.IsExtensionPickerOpen = false;
    }

    private static System.Windows.Controls.ListBox? FindSiblingListBox(DependencyObject? source)
    {
        var parent = source == null ? null : VisualTreeHelper.GetParent(source);
        while (parent != null)
        {
            if (parent is StackPanel panel)
            {
                return panel.Children.OfType<System.Windows.Controls.ListBox>().FirstOrDefault();
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static System.Windows.Controls.ListBox? FindDescendantListBox(DependencyObject source, object itemsSource)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is System.Windows.Controls.ListBox listBox &&
                ReferenceEquals(listBox.ItemsSource, itemsSource))
            {
                return listBox;
            }

            var nested = FindDescendantListBox(child, itemsSource);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RefreshQuickPanelTriggerBindings()
    {
        _settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        OnPropertyChanged(nameof(TriggerMiddleButtonDown));
        OnPropertyChanged(nameof(TriggerX1ButtonDown));
        OnPropertyChanged(nameof(TriggerX2ButtonDown));
        OnPropertyChanged(nameof(TriggerCtrlLeftClick));
        OnPropertyChanged(nameof(TriggerCtrlRightClick));
        OnPropertyChanged(nameof(TriggerMiddleButtonLongPress));
        OnPropertyChanged(nameof(TriggerRightButtonLongPress));
        OnPropertyChanged(nameof(TriggerRightButtonDrag));
        OnPropertyChanged(nameof(TriggerHorizontalWheel));

        OnPropertyChanged(nameof(ExecuteOnButtonRelease));
        OnPropertyChanged(nameof(QuickPanelTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerSummary));
        OnPropertyChanged(nameof(MouseGestureTriggerMode));
        OnPropertyChanged(nameof(MouseGestureManagementSummary));
        OnPropertyChanged(nameof(EnableRadialMenu));
        OnPropertyChanged(nameof(EnableRadialCapsLockHold));
        OnPropertyChanged(nameof(RadialActivationKey));
        OnPropertyChanged(nameof(RadialUsesCustomShortcut));
        OnPropertyChanged(nameof(RadialCustomShortcut));
        OnPropertyChanged(nameof(RadialWhitelistedProcessesText));
        OnPropertyChanged(nameof(RadialBlacklistedProcessesText));
        OnPropertyChanged(nameof(RadialAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(YanmAssignedMouseTriggerSummary));
        OnPropertyChanged(nameof(RadialMenuSummary));
        RefreshRadialMenuSlots();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

}
