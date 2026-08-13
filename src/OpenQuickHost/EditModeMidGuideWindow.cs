using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace OpenQuickHost;

public class EditModeMidGuideWindow : Window
{
    public EditModeMidGuideWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 62;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;

        // 固宽 62px 紧凑发光卡片 Border
        var cardBorder = new Border
        {
            Width = 62,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF4, 0x10, 0x14, 0x1E)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(0, 5, 0, 5),
            Effect = new DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.5
            }
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        // 1. 三个向右流光箭头堆叠 (Triple Right Flow Chevrons)
        var arrowStack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3)
        };

        var arrowGeometry = Geometry.Parse("M 0,0 L 6,6 L 0,12 L 2,12 L 8,6 L 2,0 Z");

        // 箭头 1 (淡蓝)
        var arrow1 = new System.Windows.Shapes.Path
        {
            Data = arrowGeometry,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x60, 0x3B, 0x82, 0xF6)),
            Width = 7,
            Height = 11,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 2, 0)
        };

        // 箭头 2 (中蓝)
        var arrow2 = new System.Windows.Shapes.Path
        {
            Data = arrowGeometry,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0x3B, 0x82, 0xF6)),
            Width = 9,
            Height = 13,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 2, 0)
        };

        // 箭头 3 (高亮发光蓝)
        var arrow3 = new System.Windows.Shapes.Path
        {
            Data = arrowGeometry,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
            Width = 11,
            Height = 15,
            Stretch = Stretch.Uniform
        };

        arrowStack.Children.Add(arrow1);
        arrowStack.Children.Add(arrow2);
        arrowStack.Children.Add(arrow3);

        // 2. 拖拽引导文案
        var guideText = new TextBlock
        {
            Text = "拖拽添加",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        stack.Children.Add(arrowStack);
        stack.Children.Add(guideText);

        cardBorder.Child = stack;
        Content = cardBorder;
    }

    public void UpdatePosition(double launcherRight, double panelLeft, double centerY)
    {
        try
        {
            Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = 62;
            double height = DesiredSize.Height > 0 ? DesiredSize.Height : 42;

            double midX = (launcherRight + panelLeft) / 2;
            Left = midX - (width / 2);
            Top = centerY - (height / 2);
        }
        catch
        {
            // Ignore position calculation errors
        }
    }
}
