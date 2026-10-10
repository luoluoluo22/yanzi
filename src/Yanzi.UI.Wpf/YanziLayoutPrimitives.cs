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

    public static ScrollViewer ScrollArea(UIElement content, double height = 220)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        return new ScrollViewer { Content = content, Height = height,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 60 });
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch,
            KeyboardIncrement = 8, DragIncrement = 2 };
        splitter.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Border");
        grid.Children.Add(left);
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }
}
