using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenQuickHost;

public class EditModeGuideBannerWindow : Window
{
    public EditModeGuideBannerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;

        var border = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF4, 0x10, 0x14, 0x1E)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 6, 16, 6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.6
            }
        };

        var stack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. 拖动文案 (6字以内，蓝色)
        var dragText = new TextBlock
        {
            Text = "拖扩展/文件添加",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 2. 分隔符
        var dot = new TextBlock
        {
            Text = "  •  ",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 3. Esc 按键键帽
        var escBadge = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x35, 0x3B, 0x82, 0xF6)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var escText = new TextBlock
        {
            Text = "Esc",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        escBadge.Child = escText;

        // 4. 退出文案 (6字以内)
        var exitText = new TextBlock
        {
            Text = "退出编辑",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };

        var exitStack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        exitStack.Children.Add(escBadge);
        exitStack.Children.Add(exitText);

        stack.Children.Add(dragText);
        stack.Children.Add(dot);
        stack.Children.Add(exitStack);

        border.Child = stack;
        Content = border;
    }

    public void UpdatePosition(double minLeft, double maxRight, double bottomY)
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            double totalWidth = Math.Max(300, maxRight - minLeft);

            Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double bannerWidth = DesiredSize.Width > 0 ? DesiredSize.Width : 280;
            double bannerHeight = DesiredSize.Height > 0 ? DesiredSize.Height : 36;

            double targetLeft = minLeft + (totalWidth - bannerWidth) / 2;
            double targetTop = bottomY + 10;

            if (targetTop + bannerHeight > workArea.Bottom)
            {
                targetTop = bottomY - bannerHeight - 10;
            }

            Left = Math.Max(workArea.Left, Math.Min(targetLeft, workArea.Right - bannerWidth));
            Top = Math.Max(workArea.Top, targetTop);
        }
        catch
        {
            // Ignore position calculation errors
        }
    }
}
