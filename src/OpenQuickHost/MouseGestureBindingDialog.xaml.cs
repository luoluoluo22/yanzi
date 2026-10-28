using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace OpenQuickHost;

public partial class MouseGestureBindingDialog : Window
{
    private readonly string _sequence;
    private readonly string _displayName;
    private readonly int[]? _templateData;
    private readonly IReadOnlyList<MouseGestureExtensionOption> _allExtensions;
    private readonly IReadOnlyList<MouseGestureAppOption> _allApps;

    public bool WasSaved { get; private set; }
    public bool WasUnbound { get; private set; }
    public MouseGestureExtensionOption? SelectedExtension { get; private set; }
    public List<MouseGestureAppOption> SelectedWhitelistApps { get; } = [];
    public List<MouseGestureAppOption> SelectedBlacklistApps { get; } = [];

    public ObservableCollection<MouseGestureExtensionOption> FilteredExtensions { get; } = new();
    public ObservableCollection<MouseGestureAppOption> FilteredApps { get; } = new();

    public MouseGestureBindingDialog(
        string sequence,
        string displayName,
        string description,
        string triggerLabel,
        int[]? templateData,
        string? currentAssignedExtensionId,
        IEnumerable<string>? currentBoundWhitelistPaths,
        IEnumerable<string>? currentBoundBlacklistPaths,
        IReadOnlyList<MouseGestureExtensionOption> extensionOptions,
        IReadOnlyList<MouseGestureAppOption> appOptions)
    {
        InitializeComponent();
        _sequence = sequence;
        _displayName = displayName;
        _templateData = templateData;
        _allExtensions = extensionOptions ?? [];
        _allApps = appOptions ?? [];

        GestureTitleText.Text = displayName;
        GestureDescText.Text = string.IsNullOrWhiteSpace(description) ? "支持绑定小程序与配置生效应用范围" : description;
        GestureTriggerText.Text = string.IsNullOrWhiteSpace(triggerLabel) ? "按住右键划线" : triggerLabel;

        // 渲染手势矢量预览
        var (geometry, brush) = MouseGesturePreviewGeometryFactory.CreatePreview(sequence, templateData, size: 36, padding: 4);
        GesturePreviewPath.Data = geometry;
        GesturePreviewPath.Stroke = brush;

        // 初始化已绑定的白名单与黑名单状态
        var whitelistSet = new HashSet<string>(currentBoundWhitelistPaths ?? [], StringComparer.OrdinalIgnoreCase);
        var blacklistSet = new HashSet<string>(currentBoundBlacklistPaths ?? [], StringComparer.OrdinalIgnoreCase);

        foreach (var app in _allApps)
        {
            app.IsWhitelistSelected = whitelistSet.Contains(app.AppPath) || whitelistSet.Contains(Path.GetFileName(app.AppPath));
            app.IsBlacklistSelected = blacklistSet.Contains(app.AppPath) || blacklistSet.Contains(Path.GetFileName(app.AppPath));
        }

        // 如果已有绑定，展示解除绑定按钮
        if (!string.IsNullOrWhiteSpace(currentAssignedExtensionId) || whitelistSet.Count > 0 || blacklistSet.Count > 0)
        {
            UnbindButton.Visibility = Visibility.Visible;
            UnbindButton.ToolTip = "清除该手势的所有小程序与应用绑定";
        }

        ExtensionsListBox.ItemsSource = FilteredExtensions;
        WhitelistAppsListBox.ItemsSource = FilteredApps;
        BlacklistAppsListBox.ItemsSource = FilteredApps;

        ApplyFilter(string.Empty);

        // 回显已选中的小程序
        if (!string.IsNullOrWhiteSpace(currentAssignedExtensionId))
        {
            var matchExt = FilteredExtensions.FirstOrDefault(e =>
                string.Equals(e.ExtensionId, currentAssignedExtensionId, StringComparison.OrdinalIgnoreCase));
            if (matchExt != null)
            {
                ExtensionsListBox.SelectedItem = matchExt;
                SelectedExtension = matchExt;
            }
        }

        UpdateSelectedSummary();

        // 绑定拖拽与快捷键
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private void TabRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (ExtensionsListBox == null || WhitelistAppsListBox == null || BlacklistAppsListBox == null) return;

        if (TabExtensionsRadio.IsChecked == true)
        {
            ExtensionsListBox.Visibility = Visibility.Visible;
            WhitelistAppsListBox.Visibility = Visibility.Collapsed;
            BlacklistAppsListBox.Visibility = Visibility.Collapsed;
            AppsToolbarPanel.Visibility = Visibility.Collapsed;
            SearchPlaceholder.Text = "搜索小程序名称或 ID...";
        }
        else if (TabWhitelistRadio.IsChecked == true)
        {
            ExtensionsListBox.Visibility = Visibility.Collapsed;
            WhitelistAppsListBox.Visibility = Visibility.Visible;
            BlacklistAppsListBox.Visibility = Visibility.Collapsed;
            AppsToolbarPanel.Visibility = Visibility.Visible;
            SearchPlaceholder.Text = "搜索白名单应用名称或路径...";
        }
        else if (TabBlacklistRadio.IsChecked == true)
        {
            ExtensionsListBox.Visibility = Visibility.Collapsed;
            WhitelistAppsListBox.Visibility = Visibility.Collapsed;
            BlacklistAppsListBox.Visibility = Visibility.Visible;
            AppsToolbarPanel.Visibility = Visibility.Visible;
            SearchPlaceholder.Text = "搜索黑名单应用名称或路径...";
        }

        ApplyFilter(SearchBox?.Text);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(text);
    }

    private void ApplyFilter(string? keyword)
    {
        keyword = (keyword ?? string.Empty).Trim();

        FilteredExtensions.Clear();
        foreach (var ext in _allExtensions.Where(x =>
            string.IsNullOrWhiteSpace(keyword) ||
            x.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            x.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            x.Category.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredExtensions.Add(ext);
        }

        FilteredApps.Clear();
        foreach (var app in _allApps.Where(x =>
            string.IsNullOrWhiteSpace(keyword) ||
            x.AppName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            x.AppPath.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredApps.Add(app);
        }
    }

    private void ItemListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabExtensionsRadio.IsChecked == true && ExtensionsListBox.SelectedItem is MouseGestureExtensionOption ext)
        {
            SelectedExtension = ext;
        }
        UpdateSelectedSummary();
    }

    private void ItemListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TabExtensionsRadio.IsChecked == true && ExtensionsListBox.SelectedItem is MouseGestureExtensionOption ext)
        {
            SelectedExtension = ext;
            GatherSelectedApps();
            WasSaved = true;
            DialogResult = true;
            Close();
        }
    }

    private void AppCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSelectedSummary();
    }

    private void SelectAllAppsButton_Click(object sender, RoutedEventArgs e)
    {
        bool isBlacklistTab = TabBlacklistRadio.IsChecked == true;
        foreach (var app in FilteredApps)
        {
            if (isBlacklistTab)
            {
                app.IsBlacklistSelected = true;
            }
            else
            {
                app.IsWhitelistSelected = true;
            }
        }
        UpdateSelectedSummary();
    }

    private void ClearAllAppsButton_Click(object sender, RoutedEventArgs e)
    {
        bool isBlacklistTab = TabBlacklistRadio.IsChecked == true;
        foreach (var app in _allApps)
        {
            if (isBlacklistTab)
            {
                app.IsBlacklistSelected = false;
            }
            else
            {
                app.IsWhitelistSelected = false;
            }
        }
        UpdateSelectedSummary();
    }

    private void BrowseExeButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要配置手势的应用程序",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) == true && dlg.FileNames.Length > 0)
        {
            bool isBlacklistTab = TabBlacklistRadio.IsChecked == true;

            foreach (var exePath in dlg.FileNames)
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) continue;

                var existing = _allApps.FirstOrDefault(a => string.Equals(a.AppPath, exePath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (isBlacklistTab) existing.IsBlacklistSelected = true;
                    else existing.IsWhitelistSelected = true;
                }
                else
                {
                    var appName = Path.GetFileNameWithoutExtension(exePath);
                    var customApp = new MouseGestureAppOption(appName, exePath, "自定义应用", false);
                    if (isBlacklistTab) customApp.IsBlacklistSelected = true;
                    else customApp.IsWhitelistSelected = true;

                    if (_allApps is ObservableCollection<MouseGestureAppOption> obs)
                    {
                        obs.Insert(0, customApp);
                    }
                    FilteredApps.Insert(0, customApp);
                }
            }
            UpdateSelectedSummary();
        }
    }

    private void GatherSelectedApps()
    {
        SelectedWhitelistApps.Clear();
        foreach (var app in _allApps.Where(a => a.IsWhitelistSelected))
        {
            SelectedWhitelistApps.Add(app);
        }

        SelectedBlacklistApps.Clear();
        foreach (var app in _allApps.Where(a => a.IsBlacklistSelected))
        {
            SelectedBlacklistApps.Add(app);
        }
    }

    private void UpdateSelectedSummary()
    {
        if (SelectedSummaryText == null) return;

        var whitelistCount = _allApps.Count(a => a.IsWhitelistSelected);
        var blacklistCount = _allApps.Count(a => a.IsBlacklistSelected);

        var extText = SelectedExtension != null ? $"触发: [{SelectedExtension.Label}]" : "未选小程序";
        var whiteText = whitelistCount > 0 ? $"限定 {whitelistCount} 个应用" : "全局生效";
        var blackText = blacklistCount > 0 ? $" | 禁用 {blacklistCount} 个应用" : string.Empty;

        SelectedSummaryText.Text = $"{extText} | {whiteText}{blackText}";
    }

    private void ConfirmBindButton_Click(object sender, RoutedEventArgs e)
    {
        if (ExtensionsListBox.SelectedItem is MouseGestureExtensionOption ext)
        {
            SelectedExtension = ext;
        }

        GatherSelectedApps();
        WasSaved = true;
        DialogResult = true;
        Close();
    }

    private void UnbindButton_Click(object sender, RoutedEventArgs e)
    {
        WasUnbound = true;
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
