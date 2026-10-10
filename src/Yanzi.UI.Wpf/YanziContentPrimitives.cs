using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yanzi.UI.Wpf;

/// <summary>Chat and file UI primitives. These controls never read or upload files.</summary>
public static class YanziContentPrimitives
{
    public static Border MessageBubble(string content, bool outgoing)
    {
        var bubble = new Border { MaxWidth = 300, Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        bubble.SetResourceReference(Border.BackgroundProperty, outgoing ? "Yanzi.Color.Primary" : "Yanzi.Color.Secondary");
        var text = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        text.SetResourceReference(TextBlock.ForegroundProperty, outgoing ? "Yanzi.Color.PrimaryForeground" : "Yanzi.Color.SecondaryForeground");
        bubble.Child = text;
        return bubble;
    }

    public static StackPanel Marker(string label, bool positive = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 7, 0) };
        dot.SetResourceReference(Shape.FillProperty, positive ? "Yanzi.Color.Success" : "Yanzi.Color.Destructive");
        row.Children.Add(dot);
        var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        row.Children.Add(text);
        return row;
    }

    /// <summary>Composed conversation row with optional avatar, header, and footer.</summary>
    public static FrameworkElement MessageRow(string message, bool outgoing,
        string? avatar = null, string? header = null, string? footer = null)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 8) };
        if (!string.IsNullOrWhiteSpace(avatar))
        {
            var portrait = YanziPrimitives.Avatar(avatar!, 30);
            portrait.Margin = outgoing ? new Thickness(9, 0, 0, 0) :
                new Thickness(0, 0, 9, 0);
            DockPanel.SetDock(portrait, outgoing ? Dock.Right : Dock.Left);
            row.Children.Add(portrait);
        }
        var body = new StackPanel
        {
            HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 360
        };
        if (!string.IsNullOrWhiteSpace(header))
        {
            var title = new TextBlock
            {
                Text = header, FontSize = 11,
                Margin = new Thickness(0, 0, 0, 3),
                HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            body.Children.Add(title);
        }
        body.Children.Add(MessageBubble(message, outgoing));
        if (!string.IsNullOrWhiteSpace(footer))
        {
            var stamp = new TextBlock
            {
                Text = footer, FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            stamp.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            body.Children.Add(stamp);
        }
        row.Children.Add(body);
        return row;
    }

    /// <summary>Shadcn marker variants: inline, bordered row, or separator.</summary>
    public static FrameworkElement MarkerVariant(string label, string variant = "default")
    {
        var text = new TextBlock
        {
            Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        if (variant == "separator")
        {
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
            var right = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
            left.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
            right.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Border");
            Grid.SetColumn(text, 1);
            Grid.SetColumn(right, 2);
            grid.Children.Add(left);
            grid.Children.Add(text);
            grid.Children.Add(right);
            return grid;
        }
        var border = new Border
        {
            Padding = new Thickness(2, 8, 2, 8),
            BorderThickness = variant == "border" ? new Thickness(0, 0, 0, 1) : new Thickness(0)
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        border.Child = text;
        return border;
    }

    /// <summary>Displays a file name and optional removal action; storage remains the caller's responsibility.</summary>
    public static Border Attachment(string fileName, Action? remove = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 7, 9, 7), MinWidth = 130 };
        frame.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        frame.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Secondary");
        var row = new DockPanel();
        var title = new TextBlock { Text = "📄  " + fileName, MaxWidth = 240, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.SecondaryForeground");
        if (remove is not null)
        {
            var button = YanziUi.WithStyle(new Button { Content = "×", MinWidth = 28, MinHeight = 26,
                ToolTip = "移除此附件" }, YanziUi.Styles.GhostButton);
            button.Click += (_, _) => remove();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }
        row.Children.Add(title);
        frame.Child = row;
        return frame;
    }

    public static T Direction<T>(T element, FlowDirection direction) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.FlowDirection = direction;
        return element;
    }
}
