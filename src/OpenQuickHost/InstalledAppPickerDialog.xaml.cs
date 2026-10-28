using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenQuickHost;

internal sealed class InstalledAppDisplayItem
{
    public InstalledApplicationEntry Entry { get; }
    public string Title => Entry.Title;
    public string Subtitle => Entry.Subtitle;
    public string LaunchTarget => Entry.LaunchTarget;
    public string? IconPath => Entry.IconPath;
    public ImageSource? IconSource { get; set; }

    public InstalledAppDisplayItem(InstalledApplicationEntry entry, ImageSource? iconSource)
    {
        Entry = entry;
        IconSource = iconSource;
    }
}

public partial class InstalledAppPickerDialog : Window
{
    private List<InstalledAppDisplayItem> _allApps = [];
    internal InstalledAppDisplayItem? SelectedApp { get; private set; }

    public InstalledAppPickerDialog(Window? owner = null)
    {
        InitializeComponent();
        if (owner != null)
        {
            Owner = owner;
        }

        Loaded += InstalledAppPickerDialog_Loaded;
    }

    private async void InstalledAppPickerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();

        try
        {
            // 异步后台加载本机应用程序
            var rawEntries = await Task.Run(() => InstalledApplicationCatalog.Load());

            // 转化为显示模型并预取图标
            var displayItems = new List<InstalledAppDisplayItem>(rawEntries.Count);
            foreach (var entry in rawEntries)
            {
                var iconRef = entry.IconPath ?? entry.LaunchTarget;
                var iconSource = ExtensionIconLibrary.ResolveImageSource(iconRef, null);
                displayItems.Add(new InstalledAppDisplayItem(entry, iconSource));
            }

            _allApps = displayItems;
            AppsListBox.ItemsSource = _allApps;
            StatsText.Text = $"共发现 {_allApps.Count} 个已安装程序，双击可直接选定";

            if (_allApps.Count > 0)
            {
                AppsListBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            StatsText.Text = "扫描已安装程序失败";
            HostAssets.AppendLog($"[InstalledAppPickerDialog] Load apps failed: {ex.Message}");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            AppsListBox.ItemsSource = _allApps;
            StatsText.Text = $"共发现 {_allApps.Count} 个已安装程序，双击可直接选定";
            if (_allApps.Count > 0)
            {
                AppsListBox.SelectedIndex = 0;
            }
            return;
        }

        var terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var filtered = _allApps.Where(app =>
        {
            foreach (var term in terms)
            {
                bool matchesTitle = app.Title.Contains(term, StringComparison.OrdinalIgnoreCase);
                bool matchesTarget = app.LaunchTarget.Contains(term, StringComparison.OrdinalIgnoreCase);
                bool matchesKeyword = app.Entry.Keywords.Any(k => k.Contains(term, StringComparison.OrdinalIgnoreCase));
                if (!matchesTitle && !matchesTarget && !matchesKeyword)
                {
                    return false;
                }
            }
            return true;
        }).ToList();

        AppsListBox.ItemsSource = filtered;
        StatsText.Text = $"匹配到 {filtered.Count} 个程序";

        if (filtered.Count > 0)
        {
            AppsListBox.SelectedIndex = 0;
        }
    }

    private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (AppsListBox.Items.Count > 0)
            {
                AppsListBox.Focus();
                if (AppsListBox.SelectedIndex < 0)
                {
                    AppsListBox.SelectedIndex = 0;
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void AppsListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void AppsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppsListBox.SelectedItem is InstalledAppDisplayItem item)
        {
            SelectedHintText.Text = $"已选择：{item.Title}";
        }
        else
        {
            SelectedHintText.Text = "提示：双击任意程序即可直接选定并填充。";
        }
    }

    private void AppsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppsListBox.SelectedItem is InstalledAppDisplayItem)
        {
            ConfirmSelection();
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (AppsListBox.SelectedItem is InstalledAppDisplayItem item)
        {
            SelectedApp = item;
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
