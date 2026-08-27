using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenQuickHost;

public class EditModeGuideBannerWindow : Window
{
    public Action? OnExitEditModeRequested { get; set; }

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
        Cursor = System.Windows.Input.Cursors.Hand;

        var normalBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF4, 0x10, 0x14, 0x1E));
        var hoverBg = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF8, 0x1A, 0x24, 0x3A));
        var normalBorder = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        var hoverBorder = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA));

        var border = new Border
        {
            Background = normalBg,
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.6
            }
        };

        // 鼠标悬浮与点击交互 (Hover & Clickable Button Behavior)
        border.MouseEnter += (s, e) =>
        {
            border.Background = hoverBg;
            border.BorderBrush = hoverBorder;
        };

        border.MouseLeave += (s, e) =>
        {
            border.Background = normalBg;
            border.BorderBrush = normalBorder;
        };

        border.MouseLeftButtonUp += (s, e) =>
        {
            OnExitEditModeRequested?.Invoke();
            e.Handled = true;
        };

        var stack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. Esc 按键键帽
        var escBadge = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x35, 0x3B, 0x82, 0xF6)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 0, 6, 0),
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

        // 2. 退出文案 ("退出编辑")
        var exitText = new TextBlock
        {
            Text = "退出编辑",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(escBadge);
        stack.Children.Add(exitText);

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
            double bannerWidth = DesiredSize.Width > 0 ? DesiredSize.Width : 130;
            double bannerHeight = DesiredSize.Height > 0 ? DesiredSize.Height : 34;

            double targetLeft = minLeft + (totalWidth - bannerWidth) / 2;
            double targetTop = bottomY + 12;

            if (targetTop + bannerHeight > workArea.Bottom)
            {
                targetTop = bottomY - bannerHeight - 12;
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
