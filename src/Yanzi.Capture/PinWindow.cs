using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Yanzi.Capture;

public sealed class PinWindow : Window
{
    public PinWindow(BitmapSource bitmap)
    {
        Title = "燕子贴图";
        Width = Math.Min(720, Math.Max(260, bitmap.PixelWidth));
        Height = Math.Min(520, Math.Max(180, bitmap.PixelHeight));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var border = new Border
        {
            Background = Brush("#111318"),
            CornerRadius = new CornerRadius(12),
            BorderBrush = Brush("#404752"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5)
        };

        var grid = new Grid();
        grid.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        });

        var close = new Button
        {
            Content = "×",
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Background = Brush("#CC171A20"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 18,
            Cursor = Cursors.Hand,
            ToolTip = "关闭贴图"
        };
        close.Click += (_, _) => Close();
        grid.Children.Add(close);
        border.Child = grid;
        Content = border;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button) return;
            if (e.ClickCount == 2)
            {
                Close();
                return;
            }
            try { DragMove(); } catch { }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
