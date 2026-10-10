using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Yanzi.UI.Wpf;

namespace Yanzi.UI.Gallery;

internal sealed partial class GalleryWindow
{
    private UniformGrid? _overviewCatalogGrid;
    private FrameworkElement? _overviewCatalogHeading;
    private string _overviewFilter = string.Empty;
    private double _overviewReturnScrollOffset;

    /// <summary>
    /// The homepage never maintains a second component list or hand-drawn thumbnails:
    /// it consumes the public registry and the same WPF preview factory as each detail page.
    /// </summary>
    private void OverviewComponentWall()
    {
        var heading = new Grid { Margin = new Thickness(3, 0, 3, 16) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var captions = new StackPanel();
        captions.Children.Add(Text("全部公共组件", 23, true, "Yanzi.Color.Foreground",
            new Thickness(0, 0, 0, 7)));
        captions.Children.Add(Text("直接引用详情页的真实 WPF 预览 · 点击组件名称查看交互、用法与核对记录",
            12, false, "Yanzi.Color.MutedForeground"));
        heading.Children.Add(captions);
        var total = new YanziBadge
        {
            Content = YanziComponentRegistry.Components.Count + " / " + YanziComponentRegistry.Components.Count,
            Variant = YanziBadgeVariant.Secondary,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(total, 1);
        heading.Children.Add(total);
        _overviewCatalogHeading = heading;
        _body.Children.Add(heading);

        var tools = new Grid { Margin = new Thickness(2, 0, 2, 13) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = YanziUi.WithStyle(new TextBox
        {
            Height = 36, ToolTip = "按组件名、分组或公共 API 筛选", Margin = new Thickness(0, 0, 12, 0)
        }, YanziUi.Styles.InputSoft);
        AutomationProperties.SetName(search, "首页筛选组件");
        tools.Children.Add(search);
        var shown = Text("", 12, false, "Yanzi.Color.MutedForeground");
        shown.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(shown, 1);
        tools.Children.Add(shown);
        _body.Children.Add(tools);

        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
        _overviewCatalogGrid = grid;
        var entries = new List<(YanziComponentDescriptor Item, Border Tile)>();
        foreach (var item in YanziComponentRegistry.Components)
        {
            var tile = CreateComponentWallTile(item);
            grid.Children.Add(tile);
            entries.Add((item, tile));
        }

        search.TextChanged += (_, _) =>
        {
            _overviewFilter = search.Text;
            var query = _overviewFilter.Trim();
            foreach (var (item, tile) in entries)
            {
                tile.Visibility = query.Length == 0 ||
                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Api.Contains(query, StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible : Visibility.Collapsed;
            }
            shown.Text = entries.Count(x => x.Tile.Visibility == Visibility.Visible) +
                " / " + entries.Count + " 项";
        };
        search.Text = _overviewFilter;
        if (_overviewFilter.Length == 0)
            shown.Text = entries.Count + " / " + entries.Count + " 项";
        _body.Children.Add(grid);
        _body.Children.Add(Text("以下是独立业务场景示例；上方组件缩略图与详情页由同一预览工厂创建。",
            12, false, "Yanzi.Color.MutedForeground", new Thickness(3, 26, 0, 20)));
        UpdateOverviewColumns();
    }

    private void ReturnToOverviewWall()
    {
        var offset = _overviewReturnScrollOffset;
        ShowPage(0);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => _scroll?.ScrollToVerticalOffset(offset)));
    }

    private Border CreateComponentWallTile(YanziComponentDescriptor component)
    {
        var tile = YanziUi.WithStyle(new Border
        {
            Height = 247,
            Margin = new Thickness(5, 5, 5, 8),
            Padding = new Thickness(13, 10, 13, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        }, YanziUi.Styles.Card);
        AutomationProperties.SetName(tile, "首页组件卡片 " + component.Name);

        var contents = new Grid();
        contents.RowDefinitions.Add(new RowDefinition { Height = new GridLength(43) });
        contents.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tile.Child = contents;

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var button = YanziUi.WithStyle(new Button
        {
            Content = component.Name + "  ↗",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(1, 0, 0, 0),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0),
            ToolTip = "进入 " + component.Name + " 独立详情页"
        }, YanziUi.Styles.GhostButton);
        AutomationProperties.SetName(button, "查看组件 " + component.Name);
        button.Click += (_, _) => ShowComponent(component);
        titleRow.Children.Add(button);
        var number = Text((GetComponentIndex(component) + 1).ToString("00"), 11, false,
            "Yanzi.Color.MutedForeground");
        number.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(number, 1);
        titleRow.Children.Add(number);
        contents.Children.Add(titleRow);

        var previewHost = new StackPanel
        {
            Width = 420,
            Height = 184,
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top
        };
        // Exactly the same factory used in ShowComponent; controls are instances of
        // the real shared library, so a library/style change affects both views.
        if (!TryRenderIsolatedPreview(component.Name, previewHost))
            throw new InvalidOperationException("Missing shared UI preview: " + component.Name);
        if (previewHost.Children.Count == 1 && previewHost.Children[0] is Border stage)
        {
            // Only the surrounding showcase frame is compacted; its real child
            // control and its public style/resource references stay unchanged.
            stage.MinHeight = 0;
            stage.Height = 184;
            stage.Padding = new Thickness(9, 11, 9, 11);
            stage.Margin = new Thickness(0);
            stage.ClipToBounds = true;
        }
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Child = previewHost
        };
        var frame = new Border
        {
            ClipToBounds = true,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Child = viewbox
        };
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        AutomationProperties.SetName(frame, "首页真实预览 " + component.Name);
        Grid.SetRow(frame, 1);
        contents.Children.Add(frame);
        return tile;
    }
}
