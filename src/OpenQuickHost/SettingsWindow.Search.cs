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
    private void SearchPopupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem is SearchDisplayItem selectedItem)
        {
            var target = NavigationItems.FirstOrDefault(t => t.Key == selectedItem.TabKey);
            if (target != null)
            {
                SelectedNavigation = target;
            }
        }
    }

    private void SearchPopupListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SearchPopupListBox.SelectedItem is SearchDisplayItem selectedItem)
        {
            ActivateSearchResult(selectedItem, clearSelection: true);
            e.Handled = true;
        }
    }

    private void SearchPopupListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Delete)
        {
            ReturnFocusToSearchBoxForEditing(e.Key);
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Down)
        {
            if (SearchPopupListBox.Items.Count > 0)
            {
                var nextIndex = SearchPopupListBox.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchPopupListBox.SelectedIndex + 1, SearchPopupListBox.Items.Count - 1);
                SearchPopupListBox.SelectedIndex = nextIndex;
                SearchPopupListBox.ScrollIntoView(SearchPopupListBox.SelectedItem);
            }

            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Up)
        {
            if (SearchPopupListBox.Items.Count > 0)
            {
                var previousIndex = SearchPopupListBox.SelectedIndex < 0
                    ? 0
                    : Math.Max(SearchPopupListBox.SelectedIndex - 1, 0);
                SearchPopupListBox.SelectedIndex = previousIndex;
                SearchPopupListBox.ScrollIntoView(SearchPopupListBox.SelectedItem);
            }

            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Enter)
        {
            if (SearchPopupListBox.SelectedItem is SearchDisplayItem selectedItem)
            {
                ActivateSearchResult(selectedItem, clearSelection: true);
            }

            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            IsSearchPopupOpen = false;
            SettingsSearchBox.Focus();
            e.Handled = true;
        }
    }

    private void ReturnFocusToSearchBoxForEditing(System.Windows.Input.Key key)
    {
        SettingsSearchBox.Focus();
        SettingsSearchBox.CaretIndex = SettingsSearchBox.Text?.Length ?? 0;

        var text = SettingsSearchText ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        if (key == System.Windows.Input.Key.Back)
        {
            SettingsSearchText = text[..^1];
        }
        else if (key == System.Windows.Input.Key.Delete)
        {
            SettingsSearchText = string.Empty;
        }

        SettingsSearchBox.CaretIndex = SettingsSearchText?.Length ?? 0;
    }

    private static string? BuildStandardHotkeyString(System.Windows.Input.Key key, ModifierKeys? activeModifiers = null)
    {
        var currentModifiers = activeModifiers ?? System.Windows.Input.Keyboard.Modifiers;
        return HotkeyHelper.FormatHotkey(currentModifiers, key);
    }

    private void SettingsSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ApplySettingsSearch(SettingsSearchText);
            if (SearchPopupListBox.SelectedItem is SearchDisplayItem selectedItem)
            {
                ActivateSearchResult(selectedItem, clearSelection: true);
            }
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Down && IsSearchPopupOpen)
        {
            if (SearchPopupListBox.Items.Count > 0)
            {
                var nextIndex = SearchPopupListBox.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchPopupListBox.SelectedIndex + 1, SearchPopupListBox.Items.Count - 1);
                SearchPopupListBox.SelectedIndex = nextIndex;
                SearchPopupListBox.ScrollIntoView(SearchPopupListBox.SelectedItem);
                SearchPopupListBox.Focus();
                e.Handled = true;
            }
        }
    }

    private void ApplySettingsSearch(string query)
    {
        query = query.Trim();
        HighlightKeyword = query;
        MatchedSearchItems.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchPopupOpen = false;
            return;
        }

        // 1. 匹配 Tab 标题
        var tabMatches = NavigationItems.Where(item =>
            item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var allDetailMatches = SettingsSearchData.AllSearchItems
            .Concat(_dynamicSettingsSearchItems)
            .GroupBy(item => $"{item.TabKey}\u001f{item.DisplayTitle}\u001f{item.MatchTerm}")
            .Select(group => group.First());

        // 2. 匹配右侧具体设置正文
        var detailMatches = allDetailMatches.Where(item =>
            item.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.MatchTerm.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        var searchDisplayItems = new List<SearchDisplayItem>();

        // 优先把 Tab 级别的匹配加在前面
        foreach (var tab in tabMatches)
        {
            searchDisplayItems.Add(new SearchDisplayItem(
                tab.Key,
                tab.Title,
                tab.IconGeometry
            ));
        }

        // 再把具体的设置项正文匹配加在后面
        foreach (var match in detailMatches)
        {
            var tabIcon = NavigationItems.FirstOrDefault(t => t.Key == match.TabKey)?.IconGeometry;
            searchDisplayItems.Add(new SearchDisplayItem(
                match.TabKey,
                match.DisplayTitle,
                tabIcon
            ));
        }

        // 根据 DisplayTitle 去重，保留前 8 个
        var uniqueItems = searchDisplayItems.GroupBy(x => x.DisplayTitle).Select(g => g.First()).Take(8).ToList();

        foreach (var item in uniqueItems)
        {
            MatchedSearchItems.Add(item);
        }

        IsSearchPopupOpen = MatchedSearchItems.Count > 0;

        if (MatchedSearchItems.Count > 0)
        {
            var firstTab = NavigationItems.FirstOrDefault(t => t.Key == MatchedSearchItems[0].TabKey);
            if (firstTab != null)
            {
                SelectedNavigation = firstTab;
            }

            SearchPopupListBox.SelectedIndex = 0;
        }
    }

    private void ActivateSearchResult(SearchDisplayItem selectedItem, bool clearSelection)
    {
        var target = NavigationItems.FirstOrDefault(t => t.Key == selectedItem.TabKey);
        if (target != null)
        {
            SelectedNavigation = target;
        }

        IsSearchPopupOpen = false;
        if (clearSelection)
        {
            SearchPopupListBox.SelectedIndex = -1;
        }
    }

    private void SetSnapAssistRecordingState(bool isRecording)
    {
        if (_isRecordingSnapAssistHotkey == isRecording)
        {
            return;
        }

        _isRecordingSnapAssistHotkey = isRecording;
        OnPropertyChanged(nameof(SnapAssistRecorderText));
        OnPropertyChanged(nameof(SnapAssistRecorderForeground));
    }

    private void NotifySnapAssistHotkeyDisplayChanged()
    {
        OnPropertyChanged(nameof(WindowSnapAssistHotkey));
        OnPropertyChanged(nameof(SnapAssistRecorderText));
        OnPropertyChanged(nameof(SnapAssistRecorderForeground));
    }

    private void SetLauncherRecordingState(bool isRecording)
    {
        if (_isRecordingLauncherHotkey == isRecording)
        {
            return;
        }

        _isRecordingLauncherHotkey = isRecording;
        if (!isRecording)
        {
            _lastLauncherDoubleTapCandidate = null;
            _lastLauncherDoubleTapAtUtc = default;
        }
        OnPropertyChanged(nameof(LauncherRecorderText));
        OnPropertyChanged(nameof(LauncherRecorderForeground));
    }

    private void NotifyLauncherHotkeyDisplayChanged()
    {
        OnPropertyChanged(nameof(LauncherHotkey));
        OnPropertyChanged(nameof(LauncherRecorderText));
        OnPropertyChanged(nameof(LauncherRecorderForeground));
    }

    private string GetLauncherHotkeyDisplayText() =>
        string.IsNullOrWhiteSpace(_launcherHotkey) ? "设置快捷键" : FormatLauncherShortcutForDisplay(_launcherHotkey);

    private static string FormatLauncherShortcutForDisplay(string shortcut) => shortcut switch
    {
        "DoubleCtrl" => "双击Ctrl",
        "DoubleAlt" => "双击Alt",
        _ => shortcut
    };

    private IntPtr SettingsWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 彻底拦截 Windows 默认的背景擦除消息，防止 DirectX 绘制首帧前出现系统白色画刷闪烁
        if (msg == 0x0014 /* WM_ERASEBKGND */)
        {
            handled = true;
            return new IntPtr(1);
        }

        if (!_isRecordingLauncherHotkey)
        {
            return IntPtr.Zero;
        }

        if (msg != WmKeyDown && msg != WmSysKeyDown && msg != WmKeyUp && msg != WmSysKeyUp)
        {
            return IntPtr.Zero;
        }

        var key = KeyInterop.KeyFromVirtualKey(wParam.ToInt32());
        if (key == Key.None)
        {
            return IntPtr.Zero;
        }

        var modifiers = GetCurrentModifiers();
        handled = msg is WmKeyDown or WmSysKeyDown
            ? HandleLauncherRecorderKeyDown(key, modifiers)
            : HandleLauncherRecorderKeyUp(key);
        return IntPtr.Zero;
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        return HotkeyHelper.GetCurrentPhysicalModifiers();
    }

    private void RebuildDynamicSettingsSearchItems()
    {
        _dynamicSettingsSearchItems.Clear();

        foreach (var navigationItem in NavigationItems)
        {
            if (!TryGetSearchSectionRoot(navigationItem.Key, out var root) || root == null)
            {
                continue;
            }

            foreach (var text in CollectSearchableSectionTexts(root))
            {
                _dynamicSettingsSearchItems.Add(new SettingsSearchItem(
                    navigationItem.Key,
                    $"{navigationItem.Title} - {text}",
                    $"{navigationItem.Title} {text} {navigationItem.Key}"));
            }
        }
    }

    private void RefreshSelectedSectionHighlights()
    {
        ClearSelectedSectionHighlights();

        if (string.IsNullOrWhiteSpace(HighlightKeyword) ||
            SelectedNavigation == null ||
            !TryGetSearchSectionRoot(SelectedNavigation.Key, out var root) ||
            root == null)
        {
            return;
        }

        foreach (var textBlock in EnumerateDescendantTextBlocks(root))
        {
            if (textBlock is HighlightedTextBlock)
            {
                continue;
            }

            if (BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
            {
                continue;
            }

            var text = textBlock.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text) || !text.Contains(HighlightKeyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _searchHighlightSnapshots[textBlock] = textBlock.Text ?? string.Empty;
            ApplyInlineHighlight(textBlock, text, HighlightKeyword);
        }
    }

    private void ClearSelectedSectionHighlights()
    {
        foreach (var (textBlock, originalText) in _searchHighlightSnapshots.ToArray())
        {
            textBlock.Inlines.Clear();
            textBlock.Text = originalText;
        }

        _searchHighlightSnapshots.Clear();
    }

    private static void ApplyInlineHighlight(TextBlock textBlock, string text, string keyword)
    {
        textBlock.Inlines.Clear();

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var matchIndex = text.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                textBlock.Inlines.Add(new Run(text[startIndex..]) { Foreground = textBlock.Foreground });
                break;
            }

            if (matchIndex > startIndex)
            {
                textBlock.Inlines.Add(new Run(text[startIndex..matchIndex]) { Foreground = textBlock.Foreground });
            }

            textBlock.Inlines.Add(new Run(text.Substring(matchIndex, keyword.Length))
            {
                Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF111827")),
                Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E6FDE047")),
                FontWeight = FontWeights.SemiBold
            });

            startIndex = matchIndex + keyword.Length;
        }
    }

    private bool TryGetSearchSectionRoot(string sectionKey, out FrameworkElement? root)
    {
        root = sectionKey switch
        {
            "general" => GeneralSectionRoot,
            "ai" => AiSectionRoot,
            "environment" => EnvironmentSectionRoot,
            "sync" => SyncSectionRoot,
            "extensions" => ExtensionsSectionRoot,
            "quickpanel" => QuickPanelSectionRoot,
            "mousegestures" => MouseGesturesSectionRoot,
            "radial" => RadialSectionRoot,
            "yarnselect" => YarnSelectSectionRoot,
            "yanm" => YanmSectionRoot,
            "about" => AboutSectionRoot,
            _ => null
        };

        return root != null;
    }

    private static IEnumerable<string> CollectSearchableSectionTexts(DependencyObject root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var textBlock in EnumerateDescendantTextBlocks(root))
        {
            var text = NormalizeSearchText(textBlock.Text);
            if (text.Length >= 2 && seen.Add(text))
            {
                yield return text;
            }
        }

        foreach (var dependencyObject in EnumerateDescendants(root))
        {
            if (dependencyObject is not FrameworkElement element)
            {
                continue;
            }

            foreach (var candidate in ExtractSearchableElementText(element))
            {
                if (candidate.Length >= 2 && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<TextBlock> EnumerateDescendantTextBlocks(DependencyObject root) =>
        EnumerateDescendants(root).OfType<TextBlock>();

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
            {
                continue;
            }

            yield return dependencyObject;

            foreach (var descendant in EnumerateDescendants(dependencyObject))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<string> ExtractSearchableElementText(FrameworkElement element)
    {
        if (element is System.Windows.Controls.Button button && button.Content is string buttonText)
        {
            var normalized = NormalizeSearchText(buttonText);
            if (normalized.Length >= 2)
            {
                yield return normalized;
            }
        }

        if (element.ToolTip is string tooltip)
        {
            var normalized = NormalizeSearchText(tooltip);
            if (normalized.Length >= 2)
            {
                yield return normalized;
            }
        }
    }

    private static string NormalizeSearchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim();
        if (normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tencent://", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized.Replace(Environment.NewLine, " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static bool SettingsSearchMatches(string sectionKey, string query)
    {
        return GetSettingsSearchTerms(sectionKey)
            .Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         query.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetSettingsSearchTerms(string sectionKey) => sectionKey switch
    {
        "general" =>
        [
            "常规", "开机", "启动", "托盘", "关闭", "主程序", "快捷键", "general", "startup", "launch", "tray", "hotkey"
        ],
        "sync" =>
        [
            "同步", "云", "云同步", "账号", "登录", "注册", "坚果云", "webdav", "cloud", "cloudflare", "服务器", "密码", "配置"
        ],
        "environment" =>
        [
            "环境变量", "密钥", "key", "token", "notion", "api", "secret", "env", "environment"
        ],
        "extensions" =>
        [
            "小程序", "插件", "目录", "本地", "删除", "编辑", "搜索", "打开目录", "extension", "plugin", "folder", "delete", "edit"
        ],
        "recycle" =>
        [
            "回收站", "恢复", "彻底删除", "已删除", "小程序回收站", "recycle", "trash", "restore", "deleted"
        ],
        "shortcuts" =>
        [
            "快捷键", "热键", "组合键", "录制", "全局快捷键", "shortcut", "hotkey", "keyboard"
        ],
        "quickpanel" =>
        [
            "鼠标触发", "鼠标面板", "快捷面板", "面板", "鼠标", "右键", "中键", "x1", "x2", "长按", "滚轮", "松开", "quick panel", "mouse", "middle", "right click"
        ],
        "mousegestures" =>
        [
            "鼠标手势", "手势", "轨迹", "录制", "绑定", "常用手势", "gesture", "mouse gesture", "stroke", "draw"
        ],
        "radial" =>
        [
            "燕环", "轮盘", "游戏轮盘", "capslock", "caps", "radial", "ring", "wheel", "gesture"
        ],
        "yarnselect" =>
        [
            "燕选", "左键辅助", "鼠标选中", "选中操作", "复制", "剪切", "粘贴", "搜索选中", "left button", "selection", "copy", "paste"
        ],
        "yanm" =>
        [
            "燕幕", "全局信息层", "信息展示", "组件", "webview", "html", "win", "capslock", "按住", "双击", "overlay", "dashboard"
        ],
        "about" =>
        [
            "关于", "版本", "协议", "logo", "about", "version", "license"
        ],
        _ => [sectionKey]
    };

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.TextBox or
                System.Windows.Controls.Primitives.ButtonBase or
                Selector or
                System.Windows.Controls.Primitives.ScrollBar or
                ResizeGrip or
                System.Windows.Controls.Menu or
                System.Windows.Controls.ContextMenu)
            {
                return true;
            }

            if (source is FrameworkElement fe && (
                fe.Name == "AccountCardBorder" ||
                fe.Name == "AccountInfoButton" ||
                fe.Name == "ActivateVipButton" ||
                fe.Name == "VipDaysBadgeButton"))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

}
