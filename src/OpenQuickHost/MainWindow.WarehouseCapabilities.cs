using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;

namespace OpenQuickHost;

public partial class MainWindow
{
    private void WarehouseCapabilities_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = "仓库 · 能力列表", Width = 790, Height = 610, MinWidth = 580, MinHeight = 380,
            Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(22, 25, 32))
        };
        var root = new DockPanel { Margin = new Thickness(22) };
        window.Content = root;
        var heading = new TextBlock { Text = "能力仓库", FontSize = 22, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var hint = new TextBlock { Text = "按类别浏览已注册能力；点击条目查看调用名称、权限与来源。能力不是可直接启动的小程序。",
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);
        var filters = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(filters, Dock.Top);
        root.Children.Add(filters);
        var list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        root.Children.Add(list);
        var capabilities = YanziCapabilityQueryService.ListCapabilities().OrderBy(x => x.Category).ThenBy(x => x.Name).ToArray();
        var categories = capabilities.Select(x => string.IsNullOrWhiteSpace(x.Category) ? "其他" : x.Category).Distinct().OrderBy(x => x).ToArray();
        filters.Items.Add("全部能力");
        foreach (var category in categories) filters.Items.Add(category);
        void Populate()
        {
            list.Items.Clear();
            var category = filters.SelectedItem as string;
            foreach (var capability in capabilities.Where(x => category == "全部能力" || x.Category == category))
            {
                var item = new ListBoxItem { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6),
                    Background = new SolidColorBrush(Color.FromRgb(35, 39, 49)), Foreground = Brushes.White };
                var row = new DockPanel();
                var badge = new Border { Background = new SolidColorBrush(Color.FromRgb(52, 64, 83)),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 3),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                badge.Child = new TextBlock { Text = "能力 · " + (string.IsNullOrWhiteSpace(capability.Category) ? "其他" : capability.Category),
                    Foreground = Brushes.LightSkyBlue, FontSize = 12 };
                DockPanel.SetDock(badge, Dock.Right);
                row.Children.Add(badge);
                var description = new StackPanel();
                description.Children.Add(new TextBlock { Text = capability.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White });
                description.Children.Add(new TextBlock { Text = capability.Description, Foreground = Brushes.LightGray,
                    TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
                row.Children.Add(description);
                item.Content = row;
                item.ToolTip = $"调用名：{capability.Name}\n来源：{capability.ProviderExtensionId}\n版本：{capability.Version}\n权限：{string.Join(", ", capability.Permissions)}\n风险：{capability.RiskLevel}";
                item.MouseDoubleClick += (_, _) => MessageBox.Show(window,
                    $"调用名称：{capability.Name}\n\n{capability.Description}\n\n类别：{capability.Category}\n来源：{capability.ProviderExtensionId}\n版本：{capability.Version}\n权限：{string.Join(", ", capability.Permissions)}\n风险：{capability.RiskLevel}",
                    "能力详情", MessageBoxButton.OK, MessageBoxImage.Information);
                list.Items.Add(item);
            }
        }
        filters.SelectionChanged += (_, _) => Populate();
        filters.SelectedIndex = 0;
        window.ShowDialog();
    }
}