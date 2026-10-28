using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>Small WPF layout primitives that can be reused without a web renderer.</summary>
public static class YanziLayoutPrimitives
{
    public static Border AspectRatio(FrameworkElement child, double ratio, double width = 240)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!double.IsFinite(ratio) || ratio <= 0 || !double.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(ratio), "Ratio and width must be positive finite values.");
        return new Border
        {
            Width = width, Height = width / ratio,
            ClipToBounds = true, CornerRadius = new CornerRadius(8), Child = child
        };
    }

    public static StackPanel Breadcrumb(params (string Label, Action? Navigate)[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                var sep = new TextBlock { Text = "  /  ", VerticalAlignment = VerticalAlignment.Center };
                sep.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
                row.Children.Add(sep);
            }

            var current = segments[i];
            if (current.Navigate is not null)
            {
                var link = YanziUi.WithStyle(new Button
                {
                    Content = current.Label,
                    Padding = new Thickness(5, 3, 5, 3),
                    MinWidth = 0,
                    MinHeight = 27
                }, YanziUi.Styles.GhostButton);
                link.Click += (_, _) => current.Navigate();
                row.Children.Add(link);
            }
            else
            {
                var text = new TextBlock { Text = current.Label, VerticalAlignment = VerticalAlignment.Center };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
                row.Children.Add(text);
            }
        }
        return row;
    }

    public static Expander Collapsible(string title, UIElement content, bool expanded = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        return YanziUi.WithStyle(new Expander
        {
            Header = title,
            Content = content,
            IsExpanded = expanded
        }, YanziUi.Styles.Expander);
    }

    public static ScrollViewer ScrollArea(UIElement content, double height = 220, bool horizontal = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        var area = new ScrollViewer
        {
            Content = content,
            Height = height,
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
        };
        area.Loaded += (_, _) =>
        {
            var key = horizontal ? "Yanzi.Scrollbar.Horizontal" : "Yanzi.Scrollbar";
            if (area.TryFindResource(key) is not Style style) return;
            UpdateScrollBars(area, style, horizontal ? Orientation.Horizontal : Orientation.Vertical);
        };
        return area;
    }

    private static void UpdateScrollBars(DependencyObject parent, Style style, Orientation orientation)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.Primitives.ScrollBar bar &&
                bar.Orientation == orientation)
                bar.Style = style;
            else
                UpdateScrollBars(child, style, orientation);
        }
    }

    public static Grid Resizable(UIElement left, UIElement right, double initialLeft = 180, double height = 200)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (initialLeft < 60 || height < 60 || !double.IsFinite(initialLeft) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(initialLeft));
        var grid = new Grid { Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(initialLeft),
            MinWidth = 60 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 60 });
        var splitter = YanziUi.WithStyle(new GridSplitter
        {
            Width = 10, HorizontalAlignment = HorizontalAlignment.Stretch,
            KeyboardIncrement = 8, DragIncrement = 2,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        }, YanziUi.Styles.ResizableHandle);
        System.Windows.Automation.AutomationProperties.SetName(splitter, "调整面板宽度");
        grid.Children.Add(left);
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Top/bottom split with the same keyboard and pointer semantics as the horizontal version.</summary>
    public static Grid ResizableVertical(UIElement top, UIElement bottom, double initialTop = 130,
        double height = 300)
    {
        ArgumentNullException.ThrowIfNull(top);
        ArgumentNullException.ThrowIfNull(bottom);
        if (!double.IsFinite(initialTop) || initialTop < 60 || !double.IsFinite(height) ||
            height < initialTop + 70)
            throw new ArgumentOutOfRangeException(nameof(height),
                "Height must leave at least 60 DIP for the lower pane plus its handle.");

        var grid = new Grid { Height = height };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(initialTop), MinHeight = 60 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 60 });
        var splitter = YanziUi.WithStyle(new GridSplitter
        {
            Height = 10, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            KeyboardIncrement = 8, DragIncrement = 2
        }, YanziUi.Styles.ResizableHandleVertical);
        System.Windows.Automation.AutomationProperties.SetName(splitter, "调整面板高度");
        grid.Children.Add(top);
        Grid.SetRow(splitter, 1);
        grid.Children.Add(splitter);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);
        return grid;
    }

    /// <summary>Snapshot the current first-pane DIP size for later restoration.</summary>
    public static double GetResizableFirstSize(Grid grid, bool vertical = false)
    {
        ValidateResizableGrid(grid, vertical);
        var actual = vertical ? grid.RowDefinitions[0].ActualHeight : grid.ColumnDefinitions[0].ActualWidth;
        return actual > 0 ? actual : vertical ? grid.RowDefinitions[0].Height.Value :
            grid.ColumnDefinitions[0].Width.Value;
    }

    /// <summary>Restore a saved first-pane DIP size, respecting 60 DIP minimum for both panes.</summary>
    public static void SetResizableFirstSize(Grid grid, double size, bool vertical = false)
    {
        ValidateResizableGrid(grid, vertical);
        if (!double.IsFinite(size)) throw new ArgumentOutOfRangeException(nameof(size));
        var available = vertical ? (grid.ActualHeight > 0 ? grid.ActualHeight : grid.Height)
            : (grid.ActualWidth > 0 ? grid.ActualWidth : grid.Width);
        var newSize = Math.Max(60, size);
        if (double.IsFinite(available) && available >= 130)
            newSize = Math.Min(newSize, available - 70);
        if (vertical) grid.RowDefinitions[0].Height = new GridLength(newSize);
        else grid.ColumnDefinitions[0].Width = new GridLength(newSize);
    }

    private static void ValidateResizableGrid(Grid grid, bool vertical)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (vertical)
        {
            if (grid.RowDefinitions.Count != 3 ||
                !grid.Children.OfType<GridSplitter>().Any(x => x.ResizeDirection == GridResizeDirection.Rows))
                throw new ArgumentException("A vertical Yanzi Resizable grid is required.", nameof(grid));
        }
        else if (grid.ColumnDefinitions.Count != 3 ||
                 !grid.Children.OfType<GridSplitter>().Any(x => x.ResizeDirection == GridResizeDirection.Columns))
            throw new ArgumentException("A horizontal Yanzi Resizable grid is required.", nameof(grid));
    }
}
