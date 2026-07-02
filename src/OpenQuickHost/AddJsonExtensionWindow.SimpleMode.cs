using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace OpenQuickHost;

// 简单模式（类型先行）的事件处理与表单 ↔ 隐藏数据层的同步。
// 设计要点：
//   - 顶部 8 个类型卡决定下面表单的样子；切类型不会清空已经填写的字段，仅切换可见性。
//   - 简单模式新控件 → 把值写入对应的隐藏 TextBox（IdBox / NameBox / RuntimeBox 等），
//     之后保存沿用既有的 BuildManifestFromForm 逻辑；高级 JSON 模式仍用原始 ManualJsonInputBox。
//   - 切到高级模式时把当前简单表单生成一份 JSON 同步过去；切回简单模式时尝试反向解析。
public partial class AddJsonExtensionWindow
{
    private string _currentSimpleType = "open-target";
    private bool _suppressSimpleSync;
    private static readonly char[] GestureChars = ['↑', '↗', '→', '↘', '↓', '↙', '←', '↖'];

    // 上次套用类型模板时写入的字段值快照。比对时只要字段值还等于这个快照，
    // 就视为「用户没改过、属于上一个模板留下的默认值」，可以被新模板覆盖。
    private readonly Dictionary<string, string> _lastTemplateSnapshot = new(StringComparer.Ordinal);

    // 类型预设：每个类型自己的「示例扩展」，覆盖标识、外观、关键字段
    private sealed record TypeTemplate(
        string IdPrefix,
        string Name,
        string Description,
        string Category,
        string Keywords,
        string Icon,
        string AccentHex,
        string OpenTarget = "",
        string QueryTemplate = "",
        string QueryPrefixes = "",
        string Runtime = "",
        string EntryMode = "",
        string Permissions = ""
    );

    private static readonly Dictionary<string, TypeTemplate> TypeTemplates = new()
    {
        ["open-target"] = new TypeTemplate(
            IdPrefix: "open-target",
            Name: "打开记事本",
            Description: "点击后启动 Windows 记事本。",
            Category: "扩展",
            Keywords: "记事本, notepad, 打开",
            Icon: @"C:\Windows\System32\notepad.exe",
            AccentHex: "#FF3B82F6",
            OpenTarget: "notepad.exe"),

        ["search"] = new TypeTemplate(
            IdPrefix: "search-baidu",
            Name: "百度搜索",
            Description: "在百度上搜索关键字。",
            Category: "网页搜索",
            Keywords: "搜索, baidu, 百度",
            Icon: "https://www.baidu.com/favicon.ico",
            AccentHex: "#FF6366F1",
            QueryTemplate: "https://www.baidu.com/s?wd={query}",
            QueryPrefixes: "b, baidu"),

        ["paste-text"] = new TypeTemplate(
            IdPrefix: "paste-snippet",
            Name: "粘贴文本 ",
            Description: "把一段文本粘贴到当前位置",
            Category: "脚本",
            Keywords: "粘贴, paste, 模板",
            Icon: "mdi:clipboard",
            AccentHex: "#FFEC4899",
            Runtime: "csharp",
            EntryMode: "inline",
            Permissions: "clipboard.write"),

        ["hotkey"] = new TypeTemplate(
            IdPrefix: "send-keys",
            Name: "复制快捷键",
            Description: "向当前窗口发送 Ctrl+C。",
            Category: "脚本",
            Keywords: "按键, hotkey, 快捷键",
            Icon: "mdi:shortcut",
            AccentHex: "#FF10B981",
            Runtime: "powershell",
            EntryMode: "inline"),

        ["script-ps"] = new TypeTemplate(
            IdPrefix: "ps-hello",
            Name: "PowerShell 示例",
            Description: "运行一段 PowerShell 并弹出示例窗口。",
            Category: "脚本",
            Keywords: "powershell, 脚本, ps",
            Icon: "mdi:terminal",
            AccentHex: "#FFF59E0B",
            Runtime: "powershell",
            EntryMode: "inline"),

        ["script-cs"] = new TypeTemplate(
            IdPrefix: "csharp-hello",
            Name: "C# 示例",
            Description: "用 C# 打开一个原生示例窗口。",
            Category: "脚本",
            Keywords: "csharp, 脚本, dotnet",
            Icon: "mdi:code",
            AccentHex: "#FF3B82F6",
            Runtime: "csharp",
            EntryMode: "inline"),

        ["workbench"] = new TypeTemplate(
            IdPrefix: "workbench",
            Name: "双栏工作区",
            Description: "左输入右输出，比如翻译、格式化等。",
            Category: "效率工具",
            Keywords: "工作区, 翻译, workbench",
            Icon: "mdi:window",
            AccentHex: "#FF06B6D4",
            Runtime: "powershell",
            EntryMode: "inline"),

        ["folder-search"] = new TypeTemplate(
            IdPrefix: "folder-search",
            Name: "项目文件夹搜索",
            Description: "在指定目录下快速找文件 / 子文件夹。",
            Category: "搜索",
            Keywords: "项目, folder, search",
            Icon: "mdi:folder",
            AccentHex: "#FF84CC16",
            OpenTarget: Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            QueryPrefixes: "项目, p"),
    };

    private void InitializeSimpleMode()
    {
        // 根据当前隐藏数据推断类型，编辑模式下据此选中对应卡片
        var inferred = _isEditMode ? InferTypeFromManifest() : "open-target";

        if (!_isEditMode && string.IsNullOrWhiteSpace(IdBox.Text) && string.IsNullOrWhiteSpace(NameBox.Text))
        {
            // 新增模式：直接套用第一个类型的示例
            ApplyTemplate("open-target", forceOverride: true);
        }
        else if (_isEditMode)
        {
            SeedTemplateSnapshot(inferred);
        }

        SelectTypeCardWithoutSideEffect(inferred);
        ShowFormForType(inferred);
        _currentSimpleType = inferred;

        // 把隐藏数据回填到简单控件（仅 Text 类，不动 RadioButton）
        SyncSimpleFromHiddenForm();
        UpdateTriggerLabels();
        // 强制生成一次 JSON 到 ManualJsonInputBox（即使在 _isInitializing=true 阶段，
        // 否则简单模式默认进入时 JSON 框是空的，导致试运行/保存失败）
        ForceRefreshJsonFromHiddenForm();
        UpdatePreview();
    }

    private void ForceRefreshJsonFromHiddenForm()
    {
        try
        {
            var manifest = BuildManifestFromForm();
            var json = JsonSerializer.Serialize(manifest, CreateJsonOptions());
            _suppressEditTracking = true;
            try
            {
                ManualJsonInputBox.Text = json;
            }
            finally
            {
                _suppressEditTracking = false;
            }
            UpdateManualJsonValidationState();
            RefreshAllState();
        }
        catch
        {
            // 若简单模式下数据还不完整，留空让用户继续填
        }
    }

    private void SelectTypeCardWithoutSideEffect(string typeKey)
    {
        _suppressSimpleSync = true;
        try
        {
            foreach (var child in TypeCardsGrid.Children)
            {
                if (child is WpfRadioButton rb && rb.Tag is string tag)
                {
                    rb.IsChecked = tag == typeKey;
                }
            }
        }
        finally
        {
            _suppressSimpleSync = false;
        }
    }

    private void ShowFormForType(string typeKey)
    {
        if (OpenTargetForm == null) return;
        OpenTargetForm.Visibility = typeKey == "open-target" ? Visibility.Visible : Visibility.Collapsed;
        SearchForm.Visibility = typeKey == "search" ? Visibility.Visible : Visibility.Collapsed;
        PasteTextForm.Visibility = typeKey == "paste-text" ? Visibility.Visible : Visibility.Collapsed;
        HotkeyForm.Visibility = typeKey == "hotkey" ? Visibility.Visible : Visibility.Collapsed;
        PowerShellForm.Visibility = typeKey == "script-ps" ? Visibility.Visible : Visibility.Collapsed;
        CSharpForm.Visibility = typeKey == "script-cs" ? Visibility.Visible : Visibility.Collapsed;
        WorkbenchForm.Visibility = typeKey == "workbench" ? Visibility.Visible : Visibility.Collapsed;
        FolderSearchForm.Visibility = typeKey == "folder-search" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseTestResultPanel_Click(object sender, RoutedEventArgs e)
    {
        if (ManualTestResultPanel != null)
        {
            ManualTestResultPanel.Visibility = Visibility.Collapsed;
        }
    }

    // ===================== 触发方式（右侧三段卡） =====================
    private void TriggerStartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        var enabled = TriggerStartupSwitch.IsChecked == true;
        StartupModeBox.Text = enabled ? "on_app_launch" : string.Empty;
        TryRefreshJsonFromHiddenForm();
    }

    private void TriggerScheduleConfigure_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ScheduleConfigWindow(StartupScheduleBox.Text ?? string.Empty)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        StartupScheduleBox.Text = dialog.ResultSchedule ?? string.Empty;
        UpdateTriggerLabels();
        TryRefreshJsonFromHiddenForm();
    }

    private void TriggerShortcutEdit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "设置扩展快捷键",
            "窗口激活后，按一次组合键即可完成录制。留空可清除快捷键。",
            GlobalShortcutBox.Text ?? string.Empty,
            allowEmpty: true)
        {
            Owner = this
        };
        var accepted = ExecuteWithListenerServicesPaused(() => dialog.ShowDialog());
        if (accepted != true) return;

        GlobalShortcutBox.Text = dialog.ShortcutText ?? string.Empty;
        UpdateTriggerLabels();
        TryRefreshJsonFromHiddenForm();
    }

    // ===== 鼠标手势 =====
    private void TriggerGestureRecord_Click(object sender, RoutedEventArgs e)
    {
        var trigger = GetSelectedGestureTrigger();
        var dialog = new MouseGestureRecorderWindow(trigger, _manualMouseGesture?.Sequence)
        {
            Owner = this,
            KnownGestures = CollectKnownGestures()
        };
        var accepted = ExecuteWithListenerServicesPaused(() => dialog.ShowDialog());
        if (accepted != true || string.IsNullOrEmpty(dialog.ResultSequence))
        {
            return;
        }

        _manualMouseGesture = new LocalExtensionMouseGestureManifest
        {
            Trigger = dialog.ResultTrigger,
            Sequence = dialog.ResultSequence,
            Sign = string.IsNullOrWhiteSpace(dialog.ResultSign) ? null : dialog.ResultSign,
            Data = dialog.ResultTemplateData,
            Tolerance = ParseOptionalPositiveInt(GestureToleranceBox?.Text),
            MinDistance = ParseOptionalPositiveInt(GestureMinDistanceBox?.Text) ?? 30
        };
        UpdateTriggerLabels();
        TryRefreshJsonFromHiddenForm();
    }

    private void TriggerGestureClear_Click(object sender, RoutedEventArgs e)
    {
        _manualMouseGesture = null;
        UpdateTriggerLabels();
        TryRefreshJsonFromHiddenForm();
    }

    private void GestureTriggerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Kept for old XAML compatibility. Gesture trigger is now a global setting.
    }

    private void GesturePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string rawSequence })
        {
            return;
        }

        var sequence = NormalizeGestureSequence(rawSequence);
        if (string.IsNullOrEmpty(sequence))
        {
            return;
        }

        var minDistance = ParseOptionalPositiveInt(GestureMinDistanceBox?.Text) ?? 30;
        var tolerance = ParseOptionalPositiveInt(GestureToleranceBox?.Text);
        _manualMouseGesture = new LocalExtensionMouseGestureManifest
        {
            Trigger = GetSelectedGestureTrigger(),
            Sequence = sequence,
            Sign = MouseGestureNaming.GetDisplayName(sequence),
            Tolerance = tolerance,
            MinDistance = minDistance
        };
        UpdateTriggerLabels();
        TryRefreshJsonFromHiddenForm();
    }

    private void GestureConfigTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync || _manualMouseGesture == null)
        {
            return;
        }

        var minDistance = ParseOptionalPositiveInt(GestureMinDistanceBox?.Text) ?? 30;
        var tolerance = ParseOptionalPositiveInt(GestureToleranceBox?.Text);
        if (_manualMouseGesture.MinDistance == minDistance &&
            _manualMouseGesture.Tolerance == tolerance)
        {
            return;
        }

        _manualMouseGesture = new LocalExtensionMouseGestureManifest
        {
            Trigger = _manualMouseGesture.Trigger,
            Sequence = _manualMouseGesture.Sequence,
            Sign = _manualMouseGesture.Sign,
            Data = _manualMouseGesture.Data,
            Tolerance = tolerance,
            MinDistance = minDistance
        };
        UpdateTriggerLabels();
        TryRefreshJsonFromHiddenForm();
    }

    private string GetSelectedGestureTrigger()
    {
        var runtimeTrigger = MouseGestureTriggerModes.ToRuntimeTrigger(AppSettingsStore.Load().MouseGestureTriggerMode);
        return string.IsNullOrWhiteSpace(runtimeTrigger) ? "right-drag" : runtimeTrigger;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> CollectKnownGestures()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            var entries = LocalExtensionCatalog.LoadEntries();
            var currentId = (IdBox.Text ?? string.Empty).Trim();
            foreach (var entry in entries)
            {
                var m = entry.Manifest;
                var g = m.MouseGesture;
                if (g == null || string.IsNullOrWhiteSpace(g.Sequence)) continue;
                if (string.Equals(m.Id, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                var key = $"{GetSelectedGestureTrigger()}|{g.Sequence}";
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    map[key] = list;
                }
                list.Add(m.Name);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"CollectKnownGestures failed: {ex.Message}");
        }
        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
    }

    private void UpdateTriggerLabels()
    {
        if (TriggerShortcutLabel != null)
        {
            var sc = (GlobalShortcutBox.Text ?? string.Empty).Trim();
            TriggerShortcutLabel.Text = string.IsNullOrEmpty(sc) ? "未设置 · 点击录制" : sc;
        }
        if (TriggerScheduleLabel != null)
        {
            var sched = (StartupScheduleBox.Text ?? string.Empty).Trim();
            TriggerScheduleLabel.Text = string.IsNullOrEmpty(sched) ? "未配置 · 点击设置 cron" : sched;
        }
        if (TriggerStartupSwitch != null)
        {
            TriggerStartupSwitch.IsChecked = (StartupModeBox.Text ?? string.Empty).Trim() == "on_app_launch";
        }
        if (TriggerGestureLabel != null && TriggerGestureSequenceText != null && TriggerGestureExtraRow != null)
        {
            if (_manualMouseGesture != null && !string.IsNullOrEmpty(_manualMouseGesture.Sequence))
            {
                var gestureDisplayName = string.IsNullOrWhiteSpace(_manualMouseGesture.Sign)
                    ? MouseGestureNaming.GetDisplayName(_manualMouseGesture.Sequence)
                    : _manualMouseGesture.Sign;
                TriggerGestureSequenceText.Text = gestureDisplayName;
                TriggerGestureSequenceText.Visibility = Visibility.Visible;
                var minDistanceText = (_manualMouseGesture.MinDistance ?? 30).ToString();
                TriggerGestureLabel.Text = $"  · {_manualMouseGesture.Sequence} · 全局触发 · 最小距离 {minDistanceText}px";
                TriggerGestureExtraRow.Visibility = Visibility.Visible;
                if (GestureMinDistanceBox != null)
                {
                    _suppressSimpleSync = true;
                    try { GestureMinDistanceBox.Text = (_manualMouseGesture.MinDistance ?? 30).ToString(); }
                    finally { _suppressSimpleSync = false; }
                }
                if (GestureToleranceBox != null)
                {
                    _suppressSimpleSync = true;
                    try { GestureToleranceBox.Text = _manualMouseGesture.Tolerance?.ToString() ?? string.Empty; }
                    finally { _suppressSimpleSync = false; }
                }
                UpdateGesturePresetStyles();
                UpdateGestureConflictHint();
            }
            else
            {
                TriggerGestureSequenceText.Visibility = Visibility.Collapsed;
                TriggerGestureLabel.Text = "未设置 · 点击录制";
                TriggerGestureExtraRow.Visibility = Visibility.Collapsed;
                if (GestureMinDistanceBox != null)
                {
                    _suppressSimpleSync = true;
                    try { GestureMinDistanceBox.Text = "30"; }
                    finally { _suppressSimpleSync = false; }
                }
                if (GestureToleranceBox != null)
                {
                    _suppressSimpleSync = true;
                    try { GestureToleranceBox.Text = string.Empty; }
                    finally { _suppressSimpleSync = false; }
                }
                if (GestureConflictHintText != null)
                {
                    GestureConflictHintText.Text = string.Empty;
                }
                UpdateGesturePresetStyles();
            }
        }
    }

    private void UpdateGesturePresetStyles()
    {
        if (GesturePresetPanel == null)
        {
            return;
        }

        var activeSequence = _manualMouseGesture?.Sequence ?? string.Empty;
        foreach (var child in GesturePresetPanel.Children)
        {
            if (child is not System.Windows.Controls.Button button)
            {
                continue;
            }

            var isSelected = string.Equals(button.Tag as string, activeSequence, StringComparison.Ordinal);
            button.BorderBrush = isSelected ? AccentBrush : BorderSoftBrush;
            button.Background = isSelected ? AccentGlowBrush : new WpfSolidColorBrush(WpfColor.FromArgb(0x00, 0x00, 0x00, 0x00));
            button.Foreground = isSelected ? new WpfSolidColorBrush(WpfColor.FromRgb(0xFF, 0xFF, 0xFF)) : Text2Brush;
        }
    }

    private void UpdateGestureConflictHint()
    {
        if (GestureConflictHintText == null)
        {
            return;
        }

        if (_manualMouseGesture == null || string.IsNullOrWhiteSpace(_manualMouseGesture.Sequence))
        {
            GestureConflictHintText.Text = string.Empty;
            GestureConflictHintText.Foreground = Text3Brush;
            return;
        }

        var key = $"{GetSelectedGestureTrigger()}|{_manualMouseGesture.Sequence}";
        var known = CollectKnownGestures();
        if (known.TryGetValue(key, out var owners) && owners.Count > 0)
        {
            GestureConflictHintText.Text = $"可能冲突：{string.Join("、", owners)} 已使用同一手势。运行时会进入扩展选择。";
            GestureConflictHintText.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xFB, 0x92, 0x3C));
            return;
        }

        GestureConflictHintText.Text = "当前序列未与现有扩展冲突。";
        GestureConflictHintText.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0x34, 0xD3, 0x99));
    }

    private static int? ParseOptionalPositiveInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw.Trim(), out var value) && value > 0 ? value : null;
    }

    private static string NormalizeGestureSequence(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return new string(raw.Where(ch => GestureChars.Contains(ch)).ToArray());
    }

    // 套用类型模板：
    //  - forceOverride=true（首次进入）：所有字段都灌进去
    //  - forceOverride=false（用户切类型）：只覆盖「用户没动过」的字段（值仍等于上次模板留下的快照）
    private void ApplyTemplate(string typeKey, bool forceOverride)
    {
        if (!TypeTemplates.TryGetValue(typeKey, out var tpl)) return;

        if (!_isEditMode && string.IsNullOrWhiteSpace(IdBox.Text))
        {
            IdBox.Text = LocalExtensionCatalog.CreateSystemExtensionId();
        }

        SetIfDefaultOrEmpty(NameBox, tpl.Name, "Name", forceOverride);
        SetIfDefaultOrEmpty(DescriptionBox, tpl.Description, "Description", forceOverride);
        SetIfDefaultOrEmpty(CategoryBox, tpl.Category, "Category", forceOverride);
        SetIfDefaultOrEmpty(KeywordsBox, tpl.Keywords, "Keywords", forceOverride);
        SetIfDefaultOrEmpty(VersionBox, "0.1.0", "Version", forceOverride);

        // 外观永远跟随类型（用户能在右侧手动改，那时再保留）
        SetIfDefaultOrEmpty(IconBox, tpl.Icon, "Icon", forceOverride);
        SetIfDefaultOrEmpty(AccentHexBox, tpl.AccentHex, "AccentHex", forceOverride);

        // 类型相关执行字段
        SetIfDefaultOrEmpty(OpenTargetBox, tpl.OpenTarget, "OpenTarget", forceOverride);
        SetIfDefaultOrEmpty(QueryTargetTemplateBox, tpl.QueryTemplate, "QueryTemplate", forceOverride);
        SetIfDefaultOrEmpty(QueryPrefixesBox, tpl.QueryPrefixes, "QueryPrefixes", forceOverride);
        SetIfDefaultOrEmpty(RuntimeBox, tpl.Runtime, "Runtime", forceOverride: true);
        SetIfDefaultOrEmpty(EntryModeBox, tpl.EntryMode, "EntryMode", forceOverride: true);
        if (!string.IsNullOrEmpty(tpl.Permissions))
        {
            SetIfDefaultOrEmpty(PermissionsBox, tpl.Permissions, "Permissions", forceOverride);
        }
    }

    private void SeedTemplateSnapshot(string typeKey)
    {
        if (!TypeTemplates.TryGetValue(typeKey, out var tpl))
        {
            return;
        }

        _lastTemplateSnapshot["Name"] = tpl.Name;
        _lastTemplateSnapshot["Description"] = tpl.Description;
        _lastTemplateSnapshot["Category"] = tpl.Category;
        _lastTemplateSnapshot["Keywords"] = tpl.Keywords;
        _lastTemplateSnapshot["Version"] = "0.1.0";
        _lastTemplateSnapshot["Icon"] = tpl.Icon;
        _lastTemplateSnapshot["AccentHex"] = tpl.AccentHex;
        _lastTemplateSnapshot["OpenTarget"] = tpl.OpenTarget;
        _lastTemplateSnapshot["QueryTemplate"] = tpl.QueryTemplate;
        _lastTemplateSnapshot["QueryPrefixes"] = tpl.QueryPrefixes;
        _lastTemplateSnapshot["Runtime"] = tpl.Runtime;
        _lastTemplateSnapshot["EntryMode"] = tpl.EntryMode;
        _lastTemplateSnapshot["Permissions"] = tpl.Permissions;
    }

    // 当字段还是上一次模板写入的默认值（或为空）时，用新值覆盖；否则保留用户的修改。
    // prefixMatch 用于 ID：只要 ID 仍是 "{tpl.IdPrefix}-..." 这种模式就算未改动。
    private void SetIfDefaultOrEmpty(System.Windows.Controls.TextBox box, string newValue, string snapshotKey, bool forceOverride, string? prefixMatch = null)
    {
        var current = box.Text ?? string.Empty;
        var snapshot = _lastTemplateSnapshot.TryGetValue(snapshotKey, out var snap) ? snap : string.Empty;

        bool shouldOverride;
        if (forceOverride)
        {
            shouldOverride = true;
        }
        else if (string.IsNullOrEmpty(current))
        {
            shouldOverride = true;
        }
        else if (current == snapshot)
        {
            shouldOverride = true;
        }
        else if (prefixMatch != null && current.StartsWith(prefixMatch + "-", StringComparison.Ordinal))
        {
            // ID 字段特殊：只要前缀还是上一个模板的，就算用户没改过
            shouldOverride = true;
        }
        else
        {
            shouldOverride = false;
        }

        if (shouldOverride)
        {
            box.Text = newValue;
        }
        _lastTemplateSnapshot[snapshotKey] = box.Text ?? string.Empty;
    }

    // ===================== 模式切换 =====================
    private void SimpleModeTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SimpleModePanel == null || AdvancedModePanel == null) return;
        SimpleModePanel.Visibility = Visibility.Visible;
        AdvancedModePanel.Visibility = Visibility.Collapsed;
        if (_isInitializing) return;
        // 从 JSON 同步回简单表单
        if (!string.IsNullOrWhiteSpace(ManualJsonInputBox.Text))
        {
            TryPopulateManualFormFromJson(ManualJsonInputBox.Text, showError: false);
            SyncSimpleFromHiddenForm();
        }
        UpdatePreview();
    }

    private void AdvancedModeTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SimpleModePanel == null || AdvancedModePanel == null) return;
        SimpleModePanel.Visibility = Visibility.Collapsed;
        AdvancedModePanel.Visibility = Visibility.Visible;
        if (_isInitializing) return;

        if (!ShouldKeepCurrentAdvancedJson())
        {
            // 从简单表单刷新一遍 JSON。自定义协议 JSON 不能走这里，否则会丢掉简单表单不认识的字段。
            TryRefreshJsonFromHiddenForm();
        }

        UpdatePreview();
    }

    private bool ShouldKeepCurrentAdvancedJson()
    {
        return _isEditMode && ShouldOpenAdvancedEditorForExistingJson(ManualJsonInputBox.Text);
    }

    private async void HeaderTestButton_Click(object sender, RoutedEventArgs e)
    {
        // 顶部「试运行」直接跑一次测试。简单模式下也能看到结果。
        if (TestDelayCheck != null && TestDelayCheck.IsChecked == true)
        {
            // 给用户 3 秒切窗口（模拟按键、粘贴文本等场景需要）
            var originalContent = HeaderTestButton.Content;
            HeaderTestButton.IsEnabled = false;
            try
            {
                for (var remaining = 3; remaining > 0; remaining--)
                {
                    HeaderTestButton.Content = $"{remaining} 秒后执行";
                    await Task.Delay(1000);
                }
            }
            finally
            {
                HeaderTestButton.Content = originalContent;
                HeaderTestButton.IsEnabled = true;
            }
        }
        ManualTestExtensionButton_Click(sender, e);
    }

    // ===================== 类型卡切换 =====================
    private void TypeCard_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressSimpleSync) return;
        if (sender is not WpfRadioButton { Tag: string typeKey } || string.IsNullOrEmpty(typeKey))
        {
            return;
        }

        _currentSimpleType = typeKey;
        ShowFormForType(typeKey);

        if (_isInitializing) return;

        // 应用类型默认值（仅在用户没填关键字段时填入示例）
        ApplyTypeDefaults(typeKey);
    }

    private void ApplyTypeDefaults(string typeKey)
    {
        _suppressSimpleSync = true;
        try
        {
            // 类型相关字段：清掉本类型不用的，避免落入混合状态
            switch (typeKey)
            {
                case "open-target":
                    QueryTargetTemplateBox.Text = string.Empty;
                    QueryPrefixesBox.Text = string.Empty;
                    ScriptSourceBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualSearchProvider = null;
                    _manualUiMode = null;
                    break;
                case "search":
                    OpenTargetBox.Text = string.Empty;
                    ScriptSourceBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualSearchProvider = null;
                    _manualUiMode = null;
                    break;
                case "paste-text":
                    OpenTargetBox.Text = string.Empty;
                    QueryTargetTemplateBox.Text = string.Empty;
                    QueryPrefixesBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualSearchProvider = null;
                    _manualUiMode = null;
                    if (string.IsNullOrWhiteSpace(PasteTextSimpleBox.Text))
                    {
                        PasteTextSimpleBox.Text = "燕子，没有你我怎么活啊！";
                    }
                    RebuildPasteScript();
                    break;
                case "hotkey":
                    OpenTargetBox.Text = string.Empty;
                    QueryTargetTemplateBox.Text = string.Empty;
                    QueryPrefixesBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualSearchProvider = null;
                    _manualUiMode = null;
                    if (string.IsNullOrWhiteSpace(HotkeySequenceBox.Text))
                    {
                        HotkeySequenceBox.Text = "Ctrl+C";
                    }
                    RebuildHotkeyScript();
                    break;
                case "script-ps":
                    OpenTargetBox.Text = string.Empty;
                    QueryTargetTemplateBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualSearchProvider = null;
                    _manualUiMode = null;
                    if (string.IsNullOrWhiteSpace(PowerShellScriptBox.Text) ||
                        LooksLikeGeneratedSendKeysScript(PowerShellScriptBox.Text) ||
                        LooksLikeOldPowerShellEchoTemplate(PowerShellScriptBox.Text))
                    {
                        PowerShellScriptBox.Text = CreatePowerShellPopupScript();
                    }
                    ScriptSourceBox.Text = PowerShellScriptBox.Text;
                    break;
                case "script-cs":
                    OpenTargetBox.Text = string.Empty;
                    QueryTargetTemplateBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualSearchProvider = null;
                    _manualUiMode = "native-window";
                    if (CSharpUseNativeWindowCheck != null)
                    {
                        CSharpUseNativeWindowCheck.IsChecked = true;
                    }
                    if (string.IsNullOrWhiteSpace(CSharpScriptBox.Text) ||
                        LooksLikeOldCSharpEchoTemplate(CSharpScriptBox.Text))
                    {
                        CSharpScriptBox.Text = CreateCSharpWindowScript();
                    }
                    ScriptSourceBox.Text = CSharpScriptBox.Text;
                    break;
                case "workbench":
                    OpenTargetBox.Text = string.Empty;
                    QueryTargetTemplateBox.Text = string.Empty;
                    QueryPrefixesBox.Text = string.Empty;
                    _manualSearchProvider = null;
                    _manualUiMode = null;
                    if (string.IsNullOrWhiteSpace(WorkbenchInputLabelBox.Text)) WorkbenchInputLabelBox.Text = "输入";
                    if (string.IsNullOrWhiteSpace(WorkbenchInputPlaceholderBox.Text)) WorkbenchInputPlaceholderBox.Text = "输入任意内容...";
                    if (string.IsNullOrWhiteSpace(WorkbenchOutputLabelBox.Text)) WorkbenchOutputLabelBox.Text = "结果";
                    if (string.IsNullOrWhiteSpace(WorkbenchActionButtonBox.Text)) WorkbenchActionButtonBox.Text = "执行脚本";
                    if (string.IsNullOrWhiteSpace(WorkbenchScriptBox.Text) ||
                        LooksLikeOldWorkbenchTemplate(WorkbenchScriptBox.Text))
                    {
                        WorkbenchScriptBox.Text = CreateWorkbenchPowerShellScript();
                    }
                    ScriptSourceBox.Text = WorkbenchScriptBox.Text;
                    UpdateWorkbenchHostedView();
                    break;
                case "folder-search":
                    QueryTargetTemplateBox.Text = string.Empty;
                    ScriptSourceBox.Text = string.Empty;
                    _manualHostedView = null;
                    _manualUiMode = null;
                    break;
            }

            // 套用类型模板：身份字段（id/name/description/icon/accentHex/category/keywords/openTarget 等）
            // 仅当字段还是上一个模板留下的默认值时才覆盖，用户改过的会保留
            ApplyTemplate(typeKey, forceOverride: false);
            if (typeKey == "folder-search")
            {
                FolderSearchPathBox.Text = OpenTargetBox.Text;
                FolderSearchPrefixesBox.Text = QueryPrefixesBox.Text;
                UpdateFolderSearchProvider();
            }
        }
        finally
        {
            _suppressSimpleSync = false;
        }

        // 把隐藏数据回填到简单控件（只回填、不动 RadioButton 选中态）
        SyncSimpleFromHiddenForm();
        TryRefreshJsonFromHiddenForm();
        UpdatePreview();
    }

    // ===================== 简单控件 → 隐藏 TextBox =====================
    private void OpenTargetSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        OpenTargetBox.Text = OpenTargetSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void OpenTargetBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择程序或文件",
            CheckFileExists = false,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenTargetSimpleBox.Text = dialog.FileName;
        }
    }

    private void OpenTargetPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string preset)
        {
            OpenTargetSimpleBox.Text = preset;
            
            var label = btn.Content?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(label))
            {
                var name = $"打开{label}";
                var desc = label == "记事本" ? "点击后启动 Windows 记事本。" : $"点击后打开{label}。";
                
                // 智能判定图标
                var icon = "mdi:application"; // 默认图标
                
                var lowerPreset = preset.ToLowerInvariant();
                if (lowerPreset.Contains("notepad.exe"))
                {
                    icon = @"C:\Windows\System32\notepad.exe";
                }
                else if (lowerPreset.Contains("desktop"))
                {
                    icon = "mdi:monitor";
                }
                else if (lowerPreset.Contains("download"))
                {
                    icon = "mdi:folder-download";
                }
                else if (lowerPreset.StartsWith("http://") || lowerPreset.StartsWith("https://"))
                {
                    try
                    {
                        var uri = new Uri(preset);
                        icon = $"{uri.Scheme}://{uri.Host}/favicon.ico";
                    }
                    catch
                    {
                        icon = "mdi:earth";
                    }
                }
                else if (lowerPreset.StartsWith("ms-settings"))
                {
                    icon = "mdi:cog";
                }
                else if (preset.Contains(@"\") || lowerPreset.Contains("c:") || lowerPreset.Contains("d:"))
                {
                    icon = "mdi:folder";
                }

                NameSimpleBox.Text = name;
                DescriptionSimpleBox.Text = desc;
                IconSimpleBox.Text = icon;
                
                _lastTemplateSnapshot["Name"] = name;
                _lastTemplateSnapshot["Description"] = desc;
                _lastTemplateSnapshot["Icon"] = icon;
                _lastTemplateSnapshot["OpenTarget"] = preset;
            }
        }
    }

    private void SearchPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string preset)
        {
            SearchTemplateSimpleBox.Text = preset;
            
            var label = btn.Content?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(label))
            {
                var name = $"{label}搜索";
                var desc = $"在{label}上搜索关键字。";
                
                var icon = "mdi:search";
                try
                {
                    var uri = new Uri(preset);
                    icon = uri.Host.Contains("bilibili") ? "https://www.bilibili.com/favicon.ico" : $"{uri.Scheme}://{uri.Host}/favicon.ico";
                }
                catch
                {
                    // Ignore
                }
                
                NameSimpleBox.Text = name;
                DescriptionSimpleBox.Text = desc;
                IconSimpleBox.Text = icon;
                
                _lastTemplateSnapshot["Name"] = name;
                _lastTemplateSnapshot["Description"] = desc;
                _lastTemplateSnapshot["Icon"] = icon;
                _lastTemplateSnapshot["QueryTemplate"] = preset;

                var prefixes = string.Empty;
                var lowerLabel = label.ToLowerInvariant();
                if (lowerLabel.Contains("baidu") || lowerLabel.Contains("百度"))
                {
                    prefixes = "b, baidu";
                }
                else if (lowerLabel.Contains("google") || lowerLabel.Contains("谷歌"))
                {
                    prefixes = "g, google";
                }
                else if (lowerLabel.Contains("bing"))
                {
                    prefixes = "bi, bing";
                }
                else if (lowerLabel.Contains("github"))
                {
                    prefixes = "gh, github";
                }
                else if (lowerLabel.Contains("juejin") || lowerLabel.Contains("掘金"))
                {
                    prefixes = "j, juejin";
                }
                else if (lowerLabel.Contains("mdn"))
                {
                    prefixes = "mdn";
                }
                else if (lowerLabel.Contains("bilibili") || lowerLabel.Contains("哔哩"))
                {
                    prefixes = "bili, bilibili";
                }
                else if (lowerLabel.Contains("zhihu") || lowerLabel.Contains("知乎"))
                {
                    prefixes = "zh, zhihu";
                }
                else if (lowerLabel.Contains("xiaohongshu") || lowerLabel.Contains("红书"))
                {
                    prefixes = "xhs, xiaohongshu";
                }
                else
                {
                    prefixes = lowerLabel;
                }

                SearchPrefixesSimpleBox.Text = prefixes;
                _lastTemplateSnapshot["QueryPrefixes"] = prefixes;
            }
        }
    }

    private void SearchTemplateSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        QueryTargetTemplateBox.Text = SearchTemplateSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void SearchPrefixesSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        QueryPrefixesBox.Text = SearchPrefixesSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void PasteTextSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        RebuildPasteScript();
        TryRefreshJsonFromHiddenForm();
    }

    private void RebuildPasteScript()
    {
        var text = PasteTextSimpleBox.Text ?? string.Empty;
        ScriptSourceBox.Text = CreateCSharpPasteScript(text);
        RuntimeBox.Text = "csharp";
        EntryModeBox.Text = "inline";
    }

    private static string CreateCSharpPasteScript(string text)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return $$"""
        using System;
        using System.Runtime.InteropServices;
        using System.Text;
        using System.Threading;
        using System.Threading.Tasks;

        public static class YanziAction
        {
            private const uint InputKeyboard = 1;
            private const ushort VkControl = 0x11;
            private const ushort VkV = 0x56;
            private const uint KeyeventfKeyup = 0x0002;

            public static Task<string> RunAsync(YanziActionContext context)
            {
                var payload = Encoding.UTF8.GetString(Convert.FromBase64String("{{b64}}"));
                System.Windows.Forms.Clipboard.SetDataObject(payload, true, 10, 50);
                Thread.Sleep(35);
                SendCtrlV();
                return Task.FromResult("已粘贴。");
            }

            private static void SendCtrlV()
            {
                var inputs = new[]
                {
                    KeyInput(VkControl, 0),
                    KeyInput(VkV, 0),
                    KeyInput(VkV, KeyeventfKeyup),
                    KeyInput(VkControl, KeyeventfKeyup)
                };
                _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            }

            private static INPUT KeyInput(ushort vk, uint flags) => new()
            {
                type = InputKeyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        dwFlags = flags
                    }
                }
            };

            [DllImport("user32.dll", SetLastError = true)]
            private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

            [StructLayout(LayoutKind.Sequential)]
            private struct INPUT
            {
                public uint type;
                public InputUnion U;
            }

            [StructLayout(LayoutKind.Explicit)]
            private struct InputUnion
            {
                [FieldOffset(0)]
                public MOUSEINPUT mi;

                [FieldOffset(0)]
                public KEYBDINPUT ki;

                [FieldOffset(0)]
                public HARDWAREINPUT hi;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct KEYBDINPUT
            {
                public ushort wVk;
                public ushort wScan;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct HARDWAREINPUT
            {
                public uint uMsg;
                public ushort wParamL;
                public ushort wParamH;
            }
        }
        """;
    }

    private void HotkeySequenceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        RebuildHotkeyScript();
        TryRefreshJsonFromHiddenForm();
    }

    private void HotkeyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string keys)
        {
            HotkeySequenceBox.Text = keys;
            
            var label = btn.Content?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(label))
            {
                var action = label.Split(' ')[0];
                var name = $"发送{action}快捷键";
                var desc = $"发送 {keys} 快捷键。";
                var icon = "mdi:keyboard-outline";
                
                NameSimpleBox.Text = name;
                DescriptionSimpleBox.Text = desc;
                IconSimpleBox.Text = icon;
                
                _lastTemplateSnapshot["Name"] = name;
                _lastTemplateSnapshot["Description"] = desc;
                _lastTemplateSnapshot["Icon"] = icon;
                _lastTemplateSnapshot["Runtime"] = "csharp";
            }
        }
    }

    private void HotkeyCapture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HotkeyCaptureWindow(
            "录制模拟按键",
            "请直接按下要发送给前台窗口的按键组合。Win 键组合暂不支持模拟发送。",
            HotkeySequenceBox.Text,
            allowEmpty: false,
            allowModifierless: true)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!TryConvertShortcutToSendKeys(dialog.ShortcutText, out _, out var error))
        {
            ShowError(error);
            return;
        }

        HotkeySequenceBox.Text = dialog.ShortcutText;
    }

    private void RebuildHotkeyScript()
    {
        if (!TryConvertShortcutToSendKeys(HotkeySequenceBox.Text, out var sendKeys, out _))
        {
            sendKeys = "^c";
        }

        var seq = sendKeys.Replace("\"", "`\"");
        ScriptSourceBox.Text =
            "param([string]$InputText = \"\", [string]$ContextPath = \"\")\r\n" +
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\r\n" +
            "Add-Type -AssemblyName System.Windows.Forms\r\n" +
            $"[System.Windows.Forms.SendKeys]::SendWait(\"{seq}\")\r\n" +
            "Write-Output \"已发送按键。\"";
        RuntimeBox.Text = "powershell";
        EntryModeBox.Text = "inline";
    }

    private static bool TryConvertShortcutToSendKeys(string? shortcutText, out string sendKeys, out string error)
    {
        sendKeys = string.Empty;
        error = string.Empty;

        var text = (shortcutText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "请先录制一个按键组合。";
            return false;
        }

        if (text.Contains('^') ||
            text.Contains('%') ||
            text.StartsWith("+", StringComparison.Ordinal))
        {
            sendKeys = text;
            return true;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "按键组合为空。";
            return false;
        }

        var key = parts[^1];
        var modifiers = parts.Take(parts.Length - 1).ToArray();
        var builder = new StringBuilder();
        foreach (var modifier in modifiers)
        {
            if (modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                modifier.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append('^');
            }
            else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append('%');
            }
            else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append('+');
            }
            else if (modifier.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     modifier.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                error = "SendKeys 不支持可靠模拟 Win 键组合，请改用 Ctrl / Alt / Shift 组合。";
                return false;
            }
            else
            {
                error = $"暂不支持的修饰键：{modifier}";
                return false;
            }
        }

        var sendKey = ConvertKeyNameToSendKeys(key);
        if (string.IsNullOrWhiteSpace(sendKey))
        {
            error = $"暂不支持的按键：{key}";
            return false;
        }

        builder.Append(sendKey);
        sendKeys = builder.ToString();
        return true;
    }

    private static string ConvertKeyNameToSendKeys(string key)
    {
        if (key.Length == 1)
        {
            var ch = key[0];
            return ch switch
            {
                '+' or '^' or '%' or '~' or '(' or ')' or '{' or '}' or '[' or ']' => "{" + ch + "}",
                _ => key.ToLowerInvariant()
            };
        }

        if (key.Length == 2 &&
            (key[0] == 'D' || key[0] == 'd') &&
            char.IsDigit(key[1]))
        {
            return key[1].ToString();
        }

        if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) &&
            key.Length == "NumPad0".Length &&
            char.IsDigit(key[^1]))
        {
            return "{NUMPAD" + key[^1] + "}";
        }

        if (key.Length is >= 2 and <= 3 &&
            (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return "{" + key.ToUpperInvariant() + "}";
        }

        return key.ToLowerInvariant() switch
        {
            "enter" or "return" => "{ENTER}",
            "esc" or "escape" => "{ESC}",
            "backspace" or "back" => "{BACKSPACE}",
            "delete" or "del" => "{DELETE}",
            "insert" or "ins" => "{INSERT}",
            "tab" => "{TAB}",
            "space" => " ",
            "home" => "{HOME}",
            "end" => "{END}",
            "pageup" or "prior" => "{PGUP}",
            "pagedown" or "next" => "{PGDN}",
            "up" => "{UP}",
            "down" => "{DOWN}",
            "left" => "{LEFT}",
            "right" => "{RIGHT}",
            "capslock" or "capital" => "{CAPSLOCK}",
            "apps" => "{APPS}",
            _ => string.Empty
        };
    }

    private static bool LooksLikeGeneratedSendKeysScript(string? script)
    {
        return !string.IsNullOrWhiteSpace(script) &&
               script.Contains("System.Windows.Forms.SendKeys", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeGeneratedPasteScript(string? script)
    {
        return TryExtractGeneratedPasteText(script, out _);
    }

    private static bool TryExtractGeneratedPasteText(string? script, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(script) ||
            !script.Contains("FromBase64String", StringComparison.OrdinalIgnoreCase) ||
            !(script.Contains("Set-Clipboard", StringComparison.OrdinalIgnoreCase) ||
              script.Contains("Clipboard.SetText", StringComparison.OrdinalIgnoreCase) ||
              script.Contains("Clipboard.SetDataObject", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var match = Regex.Match(
            script,
            @"FromBase64String\((['""])(?<payload>[A-Za-z0-9+/=]+)\1\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        try
        {
            text = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["payload"].Value));
            return true;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private static bool LooksLikeOldPowerShellEchoTemplate(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return false;
        }

        return script.Contains("Write-Output \"输入：$InputText\"", StringComparison.OrdinalIgnoreCase) ||
               script.Contains("Write-Output \"输入: $InputText\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOldCSharpEchoTemplate(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return false;
        }

        return script.Contains("Task.FromResult(\"输入：\" + context.InputText)", StringComparison.OrdinalIgnoreCase) ||
               script.Contains("Task.FromResult(\"收到输入：\" + context.InputText)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeOldWorkbenchTemplate(string? script)
    {
        return !string.IsNullOrWhiteSpace(script) &&
               script.Contains("Write-Output \"已收到：$InputText\"", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePowerShellPopupScript() =>
        """
        param([string]$InputText = "", [string]$ContextPath = "")
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        Add-Type -AssemblyName System.Windows.Forms

        $message = if ([string]::IsNullOrWhiteSpace($InputText)) {
            "你好燕子"
        } else {
            "你好燕子`r`n`r`n输入：$InputText"
        }

        [System.Windows.Forms.MessageBox]::Show(
            $message,
            "PowerShell 示例",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information
        ) | Out-Null

        Write-Output "已显示弹窗。"
        """;

    private static string CreateCSharpWindowScript() =>
        """
        using System.Threading.Tasks;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Media;

        public static class YanziAction
        {
            public static Task<string> RunAsync(YanziActionContext context)
            {
                var message = string.IsNullOrWhiteSpace(context.InputText)
                    ? "你好燕子"
                    : "你好燕子\n\n输入：" + context.InputText.Trim();

                var title = new TextBlock
                {
                    Text = "C# 窗口示例",
                    Foreground = Brushes.White,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var body = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    FontSize = 15,
                    TextWrapping = TextWrapping.Wrap
                };

                var closeButton = new Button
                {
                    Content = "关闭",
                    Width = 88,
                    Height = 34,
                    Margin = new Thickness(0, 20, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var panel = new StackPanel();
                panel.Children.Add(title);
                panel.Children.Add(body);
                panel.Children.Add(closeButton);

                var root = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(18, 18, 24)),
                    Padding = new Thickness(24),
                    Child = panel
                };

                var window = new Window
                {
                    Title = "你好燕子",
                    Width = 460,
                    Height = 260,
                    MinWidth = 360,
                    MinHeight = 220,
                    Content = root,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                closeButton.Click += (_, _) => window.Close();
                window.ShowDialog();
                return Task.FromResult("已关闭示例窗口。");
            }
        }
        """;

    private static string CreateWorkbenchPowerShellScript() =>
        """
        param([string]$InputText = "", [string]$ContextPath = "")
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

        if ([string]::IsNullOrWhiteSpace($InputText)) {
            Write-Output "请输入内容后再执行。"
            exit 0
        }

        Write-Output "你好燕子，已处理："
        Write-Output ""
        Write-Output $InputText.Trim()
        """;

    private void ScriptSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        if (sender is not WpfTextBox tb) return;
        ScriptSourceBox.Text = tb.Text ?? string.Empty;
        var tag = tb.Tag as string;
        if (tag == "ps")
        {
            RuntimeBox.Text = "powershell";
        }
        else if (tag == "cs")
        {
            RuntimeBox.Text = "csharp";
        }
        EntryModeBox.Text = "inline";

        if (_currentSimpleType == "workbench")
        {
            UpdateWorkbenchHostedView();
        }
        TryRefreshJsonFromHiddenForm();
    }

    private void CSharpNativeWindow_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        _manualUiMode = CSharpUseNativeWindowCheck.IsChecked == true ? "native-window" : null;
        TryRefreshJsonFromHiddenForm();
    }

    private void WorkbenchSimple_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        UpdateWorkbenchHostedView();
        TryRefreshJsonFromHiddenForm();
    }

    private void UpdateWorkbenchHostedView()
    {
        _manualHostedView = new LocalExtensionHostedViewManifest
        {
            Type = "split-workbench",
            Title = string.IsNullOrWhiteSpace(NameBox.Text) ? "工作区" : NameBox.Text.Trim(),
            Description = NullIfEmpty(DescriptionBox.Text),
            InputLabel = NullIfEmpty(WorkbenchInputLabelBox.Text) ?? "输入",
            InputPlaceholder = NullIfEmpty(WorkbenchInputPlaceholderBox.Text) ?? "输入任意内容...",
            OutputLabel = NullIfEmpty(WorkbenchOutputLabelBox.Text) ?? "结果",
            ActionButtonText = NullIfEmpty(WorkbenchActionButtonBox.Text) ?? "执行脚本",
            ActionType = "script",
            EmptyState = "脚本输出会显示在这里。"
        };
    }

    private void FolderSearchSimple_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        UpdateFolderSearchProvider();
        TryRefreshJsonFromHiddenForm();
    }

    private void FolderSearchSimple_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        UpdateFolderSearchProvider();
        TryRefreshJsonFromHiddenForm();
    }

    private void FolderSearchBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择搜索根目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            FolderSearchPathBox.Text = dialog.SelectedPath;
        }
    }

    private void UpdateFolderSearchProvider()
    {
        var path = FolderSearchPathBox.Text ?? string.Empty;
        OpenTargetBox.Text = path;
        QueryPrefixesBox.Text = FolderSearchPrefixesBox.Text ?? string.Empty;
        _manualSearchProvider = new LocalExtensionSearchProviderManifest
        {
            Type = "folder",
            Path = NullIfEmpty(path),
            IncludeSubdirectories = FolderSearchSubdirsCheck?.IsChecked == true,
            IncludeFiles = FolderSearchIncludeFilesCheck?.IsChecked != false,
            IncludeDirectories = FolderSearchIncludeDirsCheck?.IsChecked == true,
            MaxResults = 128,
            Aliases = SplitCsv(FolderSearchPrefixesBox.Text)
        };
    }

    // ===================== 触发与启动 =====================
    private void GlobalShortcutSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        GlobalShortcutBox.Text = GlobalShortcutSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void StartupModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        if (StartupModeCombo.SelectedItem is ComboBoxItem item)
        {
            StartupModeBox.Text = item.Tag?.ToString() ?? string.Empty;
        }
        TryRefreshJsonFromHiddenForm();
    }

    // ===================== 高级选项 =====================
    private void IdSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        IdBox.Text = IdSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void VersionSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        VersionBox.Text = VersionSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void CategorySimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        CategoryBox.Text = CategorySimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void PermissionsSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        PermissionsBox.Text = PermissionsSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    private void KeywordsSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        KeywordsBox.Text = KeywordsSimpleBox.Text;
        TryRefreshJsonFromHiddenForm();
    }

    // ===================== 基础信息（侧栏） =====================
    private void NameSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        NameBox.Text = NameSimpleBox.Text;
        UpdatePreview();
        if (_currentSimpleType == "workbench") UpdateWorkbenchHostedView();
        TryRefreshJsonFromHiddenForm();
    }

    private void DescriptionSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        DescriptionBox.Text = DescriptionSimpleBox.Text;
        UpdatePreview();
        if (_currentSimpleType == "workbench") UpdateWorkbenchHostedView();
        TryRefreshJsonFromHiddenForm();
    }

    private void IconSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        IconBox.Text = IconSimpleBox.Text;
        UpdatePreview();
        TryRefreshJsonFromHiddenForm();
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            AccentHexSimpleBox.Text = hex;
        }
    }

    private void AccentHexSimpleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressSimpleSync) return;
        AccentHexBox.Text = AccentHexSimpleBox.Text;
        UpdateAccentColorLivePreview();
        UpdatePreview();
        TryRefreshJsonFromHiddenForm();
    }

    private void UpdateAccentColorLivePreview()
    {
        try
        {
            var hex = AccentHexSimpleBox.Text?.Trim();
            if (!string.IsNullOrEmpty(hex))
            {
                AccentColorLivePreview.Background = CreateBrush(NormalizeAccentHexOrDefault(hex));
            }
        }
        catch
        {
            // Ignore
        }
    }

    // ===================== 同步：隐藏数据 → 简单控件 =====================
    private void SyncSimpleFromHiddenForm()
    {
        _suppressSimpleSync = true;
        try
        {
            // 基础信息
            NameSimpleBox.Text = NameBox.Text;
            DescriptionSimpleBox.Text = DescriptionBox.Text;
            IconSimpleBox.Text = IconBox.Text;
            AccentHexSimpleBox.Text = AccentHexBox.Text;
            VersionSimpleBox.Text = VersionBox.Text;
            CategorySimpleBox.Text = CategoryBox.Text;
            KeywordsSimpleBox.Text = KeywordsBox.Text;
            PermissionsSimpleBox.Text = PermissionsBox.Text;
            GlobalShortcutSimpleBox.Text = GlobalShortcutBox.Text;

            // 类型相关
            OpenTargetSimpleBox.Text = OpenTargetBox.Text;
            SearchTemplateSimpleBox.Text = QueryTargetTemplateBox.Text;
            SearchPrefixesSimpleBox.Text = QueryPrefixesBox.Text;
            FolderSearchPathBox.Text = _manualSearchProvider?.Path ?? OpenTargetBox.Text; // 当类型是 folder-search 时
            FolderSearchPrefixesBox.Text = _manualSearchProvider?.Aliases == null
                ? QueryPrefixesBox.Text
                : string.Join(", ", _manualSearchProvider.Aliases);
            if (_manualSearchProvider != null)
            {
                FolderSearchIncludeFilesCheck.IsChecked = _manualSearchProvider.IncludeFiles;
                FolderSearchIncludeDirsCheck.IsChecked = _manualSearchProvider.IncludeDirectories;
                FolderSearchSubdirsCheck.IsChecked = _manualSearchProvider.IncludeSubdirectories;
            }

            var runtime = (RuntimeBox.Text ?? string.Empty).Trim().ToLowerInvariant();
            var scriptSource = ScriptSourceBox.Text ?? string.Empty;
            if (runtime == "powershell")
            {
                PowerShellScriptBox.Text = scriptSource;
                WorkbenchScriptBox.Text = scriptSource;
                if (TryExtractGeneratedPasteText(scriptSource, out var pasteText))
                {
                    PasteTextSimpleBox.Text = pasteText;
                }
            }
            else if (runtime == "csharp")
            {
                CSharpScriptBox.Text = scriptSource;
            }

            if (CSharpUseNativeWindowCheck != null)
            {
                CSharpUseNativeWindowCheck.IsChecked =
                    string.Equals(_manualUiMode, "native-window", StringComparison.OrdinalIgnoreCase);
            }

            // Startup
            var mode = (StartupModeBox.Text ?? string.Empty).Trim();
            if (StartupModeCombo != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in StartupModeCombo.Items)
                {
                    if ((item.Tag?.ToString() ?? string.Empty) == mode)
                    {
                        StartupModeCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            // 类型推断只在初始化时做，简单模式日常切换不再反推
            UpdateAccentColorLivePreview();
        }
        finally
        {
            _suppressSimpleSync = false;
        }
    }

    private string InferTypeFromManifest()
    {
        var hasScript = !string.IsNullOrWhiteSpace(ScriptSourceBox.Text);
        var hasOpenTarget = !string.IsNullOrWhiteSpace(OpenTargetBox.Text);
        var hasQueryTemplate = !string.IsNullOrWhiteSpace(QueryTargetTemplateBox.Text);
        var hasHostedView = _manualHostedView != null;
        var runtime = (RuntimeBox.Text ?? string.Empty).Trim().ToLowerInvariant();

        if (hasHostedView) return "workbench";
        if (_manualSearchProvider != null) return "folder-search";
        if (hasQueryTemplate) return "search";
        if (hasScript)
        {
            if (LooksLikeGeneratedPasteScript(ScriptSourceBox.Text)) return "paste-text";
            if (runtime == "csharp") return "script-cs";
            return "script-ps";
        }
        if (hasOpenTarget && string.IsNullOrEmpty(QueryTargetTemplateBox.Text))
        {
            // 无 script 但有 openTarget；如果还有 prefix 当文件夹搜索
            if (!string.IsNullOrWhiteSpace(QueryPrefixesBox.Text)) return "folder-search";
            return "open-target";
        }
        return "open-target";
    }

    // ===================== 同步：隐藏数据 → JSON =====================
    private void TryRefreshJsonFromHiddenForm()
    {
        if (_isInitializing || _suppressSimpleSync) return;
        try
        {
            var manifest = BuildManifestFromForm();
            var json = JsonSerializer.Serialize(manifest, CreateJsonOptions());
            // 仅当 JSON 有变化时更新（避免触发不必要的事件）
            if (!string.Equals(ManualJsonInputBox.Text, json, StringComparison.Ordinal))
            {
                _suppressEditTracking = true;
                try
                {
                    ManualJsonInputBox.Text = json;
                }
                finally
                {
                    _suppressEditTracking = false;
                }
            }
        }
        catch
        {
            // 简单模式下数据可能暂时不完整（比如 ID 为空），忽略错误
        }
    }

    // ===================== 实时预览 =====================
    private bool _suppressPreviewSync;

    private void UpdatePreview()
    {
        if (PreviewNameText == null || PreviewDescText == null) return;
        _suppressPreviewSync = true;
        try
        {
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名扩展" : NameBox.Text;
            var desc = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? "选择类型并填写信息以预览" : DescriptionBox.Text;
            if (PreviewNameText.Text != name) PreviewNameText.Text = name;
            if (PreviewDescText.Text != desc) PreviewDescText.Text = desc;
        }
        finally
        {
            _suppressPreviewSync = false;
        }

        // 图片图标需要铺满透明底；只有矢量/文字图标使用主题色底。
        if (IconPreviewImage?.Visibility != Visibility.Visible)
        {
            IconPreviewHostBackgroundToAccent();
        }
    }

    // 用户在预览里直接编辑名称
    private void PreviewNameText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressPreviewSync) return;
        var v = PreviewNameText.Text;
        // 用户清空时不要把占位符当成真值
        if (v == "未命名扩展") return;
        NameBox.Text = v;
        // 同步右侧名称输入框
        if (NameSimpleBox.Text != v) NameSimpleBox.Text = v;
        TryRefreshJsonFromHiddenForm();
    }

    private void PreviewDescText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressPreviewSync) return;
        var v = PreviewDescText.Text;
        if (v == "选择类型并填写信息以预览") return;
        DescriptionBox.Text = v;
        if (DescriptionSimpleBox.Text != v) DescriptionSimpleBox.Text = v;
        TryRefreshJsonFromHiddenForm();
    }
}
