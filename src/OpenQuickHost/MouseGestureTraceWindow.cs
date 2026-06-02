using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace OpenQuickHost;

internal sealed class MouseGestureTraceWindow : Window
{
    private readonly Canvas _canvas;
    private readonly DispatcherTimer _hideTimer;
    private Border? _previewBadge;
    private TextBlock? _previewTitleText;
    private TextBlock? _previewDetailText;
    private Border? _previewIconHost;
    private HwndSource? _source;
    private Point? _lastPoint;
    private Polyline? _gestureLine;

    public MouseGestureTraceWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;

        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        Content = _canvas;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
            Clear();
        };

        SourceInitialized += (_, _) =>
        {
            AttachHwndHook();
            EnsureClickThrough();
        };
    }

    public void Start(Point screenPoint)
    {
        SyncBounds();
        Clear();
        _hideTimer.Stop();

        if (!IsVisible)
        {
            Show();
        }

        AttachHwndHook();
        EnsureClickThrough();
        var localPoint = ToLocal(screenPoint);
        _lastPoint = localPoint;
        AddDot(localPoint, 12, Color.FromRgb(0xFB, 0x92, 0x3C));

        _gestureLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(220, 0xFB, 0x92, 0x3C)),
            StrokeThickness = 6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(_gestureLine, 1);
        _canvas.Children.Add(_gestureLine);
        _gestureLine.Points.Add(localPoint);
    }

    public void AddPoint(Point screenPoint)
    {
        if (!IsVisible || _gestureLine == null)
        {
            return;
        }

        var point = ToLocal(screenPoint);
        if (_lastPoint is not { } last)
        {
            _lastPoint = point;
            _gestureLine.Points.Add(point);
            return;
        }

        if ((point - last).Length < 4)
        {
            return;
        }

        _gestureLine.Points.Add(point);
        _lastPoint = point;
    }

    public void UpdatePreview(MouseGesturePreviewInfo? preview, Point screenPoint)
    {
        if (!IsVisible)
        {
            return;
        }

        if (preview == null)
        {
            if (_previewBadge != null)
            {
                _previewBadge.Visibility = Visibility.Collapsed;
            }
            return;
        }

        EnsurePreviewBadge();
        ApplyPreviewContent(preview, "匹配");
        _previewBadge!.Visibility = Visibility.Visible;
        var point = ToLocal(screenPoint);
        Canvas.SetLeft(_previewBadge, point.X + 18);
        Canvas.SetTop(_previewBadge, point.Y - 18);
    }

    public void Finish(MouseGesturePreviewInfo? preview, bool matched)
    {
        if (!IsVisible)
        {
            return;
        }

        if (_previewBadge != null)
        {
            _previewBadge.Visibility = Visibility.Collapsed;
        }

        if (_lastPoint is { } last)
        {
            AddDot(last, 14, matched ? Color.FromRgb(0x3B, 0x82, 0xF6) : Color.FromRgb(0xA1, 0xA1, 0xAA));
            if (matched && preview != null)
            {
                AddResultBadge(preview, last);
            }
        }

        _hideTimer.Interval = TimeSpan.FromMilliseconds(matched ? 760 : 360);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void Cancel()
    {
        _hideTimer.Stop();
        Hide();
        Clear();
    }

    private void SyncBounds()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private Point ToLocal(Point screenPoint)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var logicalPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
            return new Point(logicalPoint.X - Left, logicalPoint.Y - Top);
        }
        return new Point(screenPoint.X - Left, screenPoint.Y - Top);
    }


    private void AddDot(Point point, double size, Color color)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Color.FromArgb(120, 0x00, 0x00, 0x00)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(dot, 2);
        Canvas.SetLeft(dot, point.X - (size / 2));
        Canvas.SetTop(dot, point.Y - (size / 2));
        _canvas.Children.Add(dot);
    }

    private void AddResultBadge(MouseGesturePreviewInfo preview, Point point)
    {
        var content = CreatePreviewContent(preview, "已触发");
        var border = new Border
        {
            Child = content,
            Background = new SolidColorBrush(Color.FromArgb(210, 0x17, 0x17, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 10, 6),
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(border, 10);
        Canvas.SetLeft(border, point.X + 14);
        Canvas.SetTop(border, point.Y - 16);
        _canvas.Children.Add(border);
    }

    private void EnsurePreviewBadge()
    {
        if (_previewBadge != null)
        {
            return;
        }

        var content = CreatePreviewContent(null, "匹配");
        _previewBadge = new Border
        {
            Child = content,
            Background = new SolidColorBrush(Color.FromArgb(220, 0x17, 0x17, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0xFB, 0x92, 0x3C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 6, 10, 6),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(_previewBadge, 20);
        _canvas.Children.Add(_previewBadge);
    }

    private void Clear()
    {
        _canvas.Children.Clear();
        _previewBadge = null;
        _previewTitleText = null;
        _previewDetailText = null;
        _previewIconHost = null;
        _lastPoint = null;
        _gestureLine = null;
    }

    private Grid CreatePreviewContent(MouseGesturePreviewInfo? preview, string prefix)
    {
        var grid = new Grid
        {
            IsHitTestVisible = false
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _previewIconHost = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(44, 0xFB, 0x92, 0x3C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0xFB, 0x92, 0x3C)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0),
            Child = BuildIcon(preview),
            IsHitTestVisible = false
        };
        grid.Children.Add(_previewIconHost);

        var textStack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            IsHitTestVisible = false
        };
        Grid.SetColumn(textStack, 1);
        _previewTitleText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Text = BuildTitle(prefix, preview),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220
        };
        _previewDetailText = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
            Text = BuildDetail(preview),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            Margin = new Thickness(0, 1, 0, 0)
        };
        textStack.Children.Add(_previewTitleText);
        textStack.Children.Add(_previewDetailText);
        grid.Children.Add(textStack);

        return grid;
    }

    private void ApplyPreviewContent(MouseGesturePreviewInfo preview, string prefix)
    {
        if (_previewTitleText == null || _previewDetailText == null || _previewIconHost == null)
        {
            return;
        }

        _previewTitleText.Text = BuildTitle(prefix, preview);
        _previewDetailText.Text = BuildDetail(preview);
        _previewIconHost.Child = BuildIcon(preview);
    }

    private static string BuildTitle(string prefix, MouseGesturePreviewInfo? preview)
    {
        var name = preview?.ExtensionName;
        return string.IsNullOrWhiteSpace(name) ? prefix : $"{prefix}：{name}";
    }

    private static string BuildDetail(MouseGesturePreviewInfo? preview)
    {
        if (preview == null)
        {
            return "鼠标手势";
        }

        var sign = preview.Sign?.Trim();
        if (!string.IsNullOrWhiteSpace(sign) && sign.Length <= 18)
        {
            return $"鼠标手势 · {sign}";
        }

        return "鼠标手势";
    }

    private static UIElement BuildIcon(MouseGesturePreviewInfo? preview)
    {
        if (preview != null)
        {
            var image = ExtensionIconLibrary.ResolveImageSource(preview.IconReference, preview.ExtensionDirectoryPath);
            if (image != null)
            {
                return new System.Windows.Controls.Image
                {
                    Source = image,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
            }

            var geometry = ExtensionIconLibrary.ResolveVectorIcon(preview.IconReference);
            if (geometry != null)
            {
                return new Viewbox
                {
                    Width = 15,
                    Height = 15,
                    Child = new Path
                    {
                        Data = geometry,
                        Fill = Brushes.White,
                        Stretch = Stretch.Uniform,
                        IsHitTestVisible = false
                    },
                    IsHitTestVisible = false
                };
            }
        }

        return new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(preview?.DisplayGlyph) ? "势" : preview!.DisplayGlyph,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };
    }

    private void EnsureClickThrough()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var style = GetWindowLongPtr(handle, GwlExstyle);
            SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style.ToInt64() | WsExToolwindow | WsExNoactivate | WsExTransparent | WsExLayered));
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch
        {
            // Best effort: the window is still visually useful if extended styles fail.
        }
    }

    private void AttachHwndHook()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || _source != null)
        {
            return;
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(HtTransparent);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        base.OnClosed(e);
    }

    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoactivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
