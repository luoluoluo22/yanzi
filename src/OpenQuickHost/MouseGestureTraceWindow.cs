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

public enum OriginActionState
{
    None,
    Cancel,
    Edit,
    Pin
}

internal sealed class MouseGestureTraceWindow : Window
{
    private readonly Canvas _canvas;
    private readonly DispatcherTimer _hideTimer;
    private Border? _previewBadge;
    private TextBlock? _previewTitleText;
    private TextBlock? _previewDetailText;
    private Border? _previewIconHost;
    private Border? _originActionBarHost;
    private Border? _editButtonBorder;
    private Border? _pinButtonBorder;
    private Ellipse? _cancelCircle;
    private Ellipse? _cancelHalo;
    private TextBlock? _cancelIcon;
    private TextBlock? _editButtonText;
    private TextBlock? _pinButtonText;
    private Border? _cheatsheetHost;
    private HwndSource? _source;
    private Point? _startPoint;
    private Point? _lastPoint;
    private PathFigure? _pathFigure;
    private Path? _glowPath;
    private Path? _corePath;
    private LinearGradientBrush? _coreGradientBrush;
    private LinearGradientBrush? _glowGradientBrush;
    private double _cheatsheetDesiredWidth;
    private double _cheatsheetDesiredHeight;
    private bool _hasMovedFarFromStart;
    private OriginActionState _currentOriginAction = OriginActionState.None;
    private readonly List<Point> _rawTracePoints = new(capacity: 256);
    private readonly List<Point> _smoothTracePoints = new(capacity: 256);

    public OriginActionState CurrentOriginAction => _currentOriginAction;
    public bool IsCancelled => _currentOriginAction == OriginActionState.Cancel;

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
        SnapsToDevicePixels = false;
        Opacity = 0;

        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = false
        };
        RenderOptions.SetEdgeMode(_canvas, EdgeMode.Unspecified);
        Content = _canvas;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        SourceInitialized += (_, _) =>
        {
            AttachHwndHook();
            EnsureClickThrough();
        };

        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        try
        {
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
        }
        catch { /* best effort */ }
    }

    public void Start(Point screenPoint, IReadOnlyList<MouseGestureCheatItem>? cheatItems = null)
    {
        _hideTimer.Stop();
        Opacity = 0;
        if (_canvas != null)
        {
            _canvas.Visibility = Visibility.Hidden;
        }
        Clear();

        SyncBounds(screenPoint);

        var localPoint = ToLocal(screenPoint);
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            HostAssets.AppendLog($"[MouseGestureTraceWindow] Start: screenPoint=({screenPoint.X:F0}, {screenPoint.Y:F0}) -> localPoint=({localPoint.X:F1}, {localPoint.Y:F1}), WindowBounds=({Left:F0}, {Top:F0}, {Width:F0}, {Height:F0}), dpiScale=({dpi.DpiScaleX:F2}, {dpi.DpiScaleY:F2})");
        }
        catch { /* ignore */ }

        _startPoint = localPoint;
        _lastPoint = localPoint;
        _hasMovedFarFromStart = false;
        _currentOriginAction = OriginActionState.None;
        _rawTracePoints.Clear();
        _smoothTracePoints.Clear();
        _rawTracePoints.Add(localPoint);
        _smoothTracePoints.Add(localPoint);

        // 1. 创建起始点同心发光圆点 (白核心 + 翠绿晕圈)
        AddStartIndicator(localPoint);

        // 2. 创建起点动作栏 (取消 + 下一行编辑与置顶)
        CreateOriginActionBar(localPoint);

        // 3. 创建可用手势速查看板
        if (cheatItems != null && cheatItems.Count > 0)
        {
            CreateCheatsheetHUD(localPoint, cheatItems);
        }

        // 4. 构建白到绿流光渐变笔刷
        _coreGradientBrush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = localPoint,
            EndPoint = localPoint
        };
        _coreGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
        _coreGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 110, 231, 183), 0.3));
        _coreGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 16, 185, 129), 0.8));
        _coreGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 5, 150, 105), 1.0));

        _glowGradientBrush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = localPoint,
            EndPoint = localPoint
        };
        _glowGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
        _glowGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 16, 185, 129), 0.5));
        _glowGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 5, 150, 105), 1.0));

        _pathFigure = new PathFigure
        {
            StartPoint = localPoint,
            IsClosed = false,
            IsFilled = false
        };

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(_pathFigure);

        // 外层柔光 (白到绿流光晕)
        _glowPath = new Path
        {
            Data = pathGeometry,
            Stroke = _glowGradientBrush,
            StrokeThickness = 12,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        // 内层高亮核心 (白到绿亮芯)
        _corePath = new Path
        {
            Data = pathGeometry,
            Stroke = _coreGradientBrush,
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        Canvas.SetZIndex(_glowPath, 2);
        Canvas.SetZIndex(_corePath, 3);
        if (_canvas != null)
        {
            _canvas.Children.Add(_glowPath);
            _canvas.Children.Add(_corePath);
            _canvas.Visibility = Visibility.Visible;
        }

        Opacity = 1.0;

        if (!IsVisible)
        {
            Show();
        }
    }

    public void AddPoint(Point screenPoint)
    {
        if (!IsVisible || _pathFigure == null)
        {
            return;
        }

        var point = ToLocal(screenPoint);
        if (_lastPoint is not { } last)
        {
            _lastPoint = point;
            _rawTracePoints.Add(point);
            return;
        }

        var distFromStart = _startPoint.HasValue ? (point - _startPoint.Value).Length : 0;
        if (distFromStart > 26)
        {
            _hasMovedFarFromStart = true;
        }

        // 检查是否移回起点动作栏（取消 / 编辑 / 置顶）
        if (_hasMovedFarFromStart && _startPoint.HasValue)
        {
            var start = _startPoint.Value;
            var dx = point.X - start.X;
            var dy = point.Y - start.Y;

            OriginActionState hoveredAction = OriginActionState.None;
            if (dy >= 10 && dy <= 42 && dx >= -52 && dx <= -2)
            {
                // 编辑按钮区域
                hoveredAction = OriginActionState.Edit;
            }
            else if (dy >= 10 && dy <= 42 && dx >= 2 && dx <= 52)
            {
                // 置顶按钮区域
                hoveredAction = OriginActionState.Pin;
            }
            else if (distFromStart < 22 || (Math.Abs(dx) < 22 && dy >= -22 && dy <= 12))
            {
                // 上方取消按钮区域
                hoveredAction = OriginActionState.Cancel;
            }

            if (hoveredAction != _currentOriginAction)
            {
                UpdateOriginActionVisual(hoveredAction);
            }
        }

        if ((point - last).Length < 2.5)
        {
            return;
        }

        // 1. 低通指数加权平滑滤波，彻底熨平人手微抖与阶梯抖动
        var prevSmooth = _smoothTracePoints[^1];
        const double alpha = 0.65; // 平滑权重系数：兼顾超丝滑弧度与毫秒级跟手度
        var smoothedPoint = new Point(
            prevSmooth.X + alpha * (point.X - prevSmooth.X),
            prevSmooth.Y + alpha * (point.Y - prevSmooth.Y)
        );

        _rawTracePoints.Add(point);
        _smoothTracePoints.Add(smoothedPoint);
        _lastPoint = point;

        // 实时更新白到绿流光渐变端点
        if (_coreGradientBrush != null && _startPoint.HasValue)
        {
            _coreGradientBrush.StartPoint = _startPoint.Value;
            _coreGradientBrush.EndPoint = point;
        }
        if (_glowGradientBrush != null && _startPoint.HasValue)
        {
            _glowGradientBrush.StartPoint = _startPoint.Value;
            _glowGradientBrush.EndPoint = point;
        }

        // 响应式自适应更新可用手势看板位置（向下划动时自动避让至上方）
        UpdateCheatsheetPosition(point);

        // 2. 连续 C1 导数切线平滑三次贝塞尔样条曲线插值（Catmull-Rom Spline to Cubic Bezier）
        var count = _smoothTracePoints.Count;
        if (count == 2)
        {
            _pathFigure.Segments.Add(new LineSegment(_smoothTracePoints[1], isStroked: true));
        }
        else if (count == 3)
        {
            var p0 = _smoothTracePoints[0];
            var p1 = _smoothTracePoints[1];
            var p2 = _smoothTracePoints[2];
            var cp = new Point((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0);
            _pathFigure.Segments.Add(new QuadraticBezierSegment(cp, p2, isStroked: true));
        }
        else
        {
            // 对于连续 4 点 p0, p1, p2, p3，在 p1 -> p2 之间构造两端切线连续的三次贝塞尔曲线
            var p0 = _smoothTracePoints[^4];
            var p1 = _smoothTracePoints[^3];
            var p2 = _smoothTracePoints[^2];
            var p3 = _smoothTracePoints[^1];

            var cp1 = new Point(
                p1.X + (p2.X - p0.X) / 6.0,
                p1.Y + (p2.Y - p0.Y) / 6.0
            );
            var cp2 = new Point(
                p2.X - (p3.X - p1.X) / 6.0,
                p2.Y - (p3.Y - p1.Y) / 6.0
            );

            _pathFigure.Segments.Add(new BezierSegment(cp1, cp2, p2, isStroked: true));
        }
    }

    public void UpdatePreview(MouseGesturePreviewInfo? preview, Point screenPoint)
    {
        if (!IsVisible || IsCancelled)
        {
            if (_previewBadge != null) _previewBadge.Visibility = Visibility.Collapsed;
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

        if (_cheatsheetHost != null)
        {
            _cheatsheetHost.Visibility = Visibility.Collapsed;
        }

        if (_lastPoint is { } last)
        {
            if (IsCancelled)
            {
                AddCancelBadge(last);
            }
            else
            {
                AddDot(last, 14, matched ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0xA1, 0xA1, 0xAA));
                if (matched && preview != null)
                {
                    AddResultBadge(preview, last);
                }
            }
        }

        _hideTimer.Interval = TimeSpan.FromMilliseconds(matched ? 80 : 50);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public new void Hide()
    {
        _hideTimer.Stop();
        OverlayWindowManager.SafeHideAndPark(this, () =>
        {
            if (_canvas != null)
            {
                _canvas.Visibility = Visibility.Hidden;
            }
            Clear();
        });
    }

    public void Cancel()
    {
        Hide();
    }

    private void AddStartIndicator(Point point)
    {
        // 外层柔和翡翠绿光晕
        var halo = new Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = new SolidColorBrush(Color.FromArgb(90, 0x10, 0xB9, 0x81)),
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(halo, 1);
        Canvas.SetLeft(halo, point.X - 11);
        Canvas.SetTop(halo, point.Y - 11);
        _canvas.Children.Add(halo);

        // 中层翠绿环
        var ring = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(ring, 2);
        Canvas.SetLeft(ring, point.X - 6);
        Canvas.SetTop(ring, point.Y - 6);
        _canvas.Children.Add(ring);

        // 内层纯白中心起笔核
        var core = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = Brushes.White,
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(core, 3);
        Canvas.SetLeft(core, point.X - 3);
        Canvas.SetTop(core, point.Y - 3);
        _canvas.Children.Add(core);
    }

    private void CreateOriginActionBar(Point point)
    {
        var mainStack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        // 上行：取消区域
        var cancelGrid = new Grid
        {
            Width = 44,
            Height = 44,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        _cancelHalo = new Ellipse
        {
            Width = 44,
            Height = 44,
            Fill = new SolidColorBrush(Color.FromArgb(35, 0xEF, 0x44, 0x44)),
            Stroke = new SolidColorBrush(Color.FromArgb(90, 0xEF, 0x44, 0x44)),
            StrokeThickness = 1,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _cancelCircle = new Ellipse
        {
            Width = 30,
            Height = 30,
            Fill = new SolidColorBrush(Color.FromArgb(200, 0x1F, 0x1F, 0x24)),
            Stroke = new SolidColorBrush(Color.FromArgb(220, 0xEF, 0x44, 0x44)),
            StrokeThickness = 2,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        _cancelIcon = new TextBlock
        {
            Text = "✕",
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        cancelGrid.Children.Add(_cancelHalo);
        cancelGrid.Children.Add(_cancelCircle);
        cancelGrid.Children.Add(_cancelIcon);
        mainStack.Children.Add(cancelGrid);

        // 下行：【编辑】与【置顶】胶囊按钮行
        var actionRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        _editButtonText = new TextBlock
        {
            Text = "✏️ 编辑",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 0x10, 0xB9, 0x81)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        _editButtonBorder = new Border
        {
            Child = _editButtonText,
            Background = new SolidColorBrush(Color.FromArgb(180, 0x12, 0x16, 0x15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0x10, 0xB9, 0x81)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 6, 0),
            IsHitTestVisible = false
        };

        _pinButtonText = new TextBlock
        {
            Text = "📌 置顶",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 0x38, 0xBD, 0xF8)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        _pinButtonBorder = new Border
        {
            Child = _pinButtonText,
            Background = new SolidColorBrush(Color.FromArgb(180, 0x12, 0x14, 0x1C)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0x38, 0xBD, 0xF8)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(7, 3, 7, 3),
            IsHitTestVisible = false
        };

        actionRow.Children.Add(_editButtonBorder);
        actionRow.Children.Add(_pinButtonBorder);
        mainStack.Children.Add(actionRow);

        _originActionBarHost = new Border
        {
            Child = mainStack,
            IsHitTestVisible = false
        };

        Canvas.SetZIndex(_originActionBarHost, 5);
        _canvas.Children.Add(_originActionBarHost);

        // 测量并居中对齐在 point 处（取消按钮正中心对齐 point）
        _originActionBarHost.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var hostW = _originActionBarHost.DesiredSize.Width;
        Canvas.SetLeft(_originActionBarHost, point.X - (hostW / 2.0));
        Canvas.SetTop(_originActionBarHost, point.Y - 22);
    }

    private void UpdateOriginActionVisual(OriginActionState state)
    {
        _currentOriginAction = state;

        // 1. 取消按钮反馈
        var isCancelHovered = state == OriginActionState.Cancel;
        if (_cancelHalo != null) _cancelHalo.Visibility = isCancelHovered ? Visibility.Visible : Visibility.Collapsed;
        if (_cancelCircle != null)
        {
            _cancelCircle.Fill = new SolidColorBrush(isCancelHovered ? Color.FromArgb(240, 0xEF, 0x44, 0x44) : Color.FromArgb(200, 0x1F, 0x1F, 0x24));
            _cancelCircle.Stroke = new SolidColorBrush(isCancelHovered ? Brushes.White.Color : Color.FromArgb(220, 0xEF, 0x44, 0x44));
            _cancelCircle.Width = isCancelHovered ? 38 : 30;
            _cancelCircle.Height = isCancelHovered ? 38 : 30;
        }
        if (_cancelIcon != null)
        {
            _cancelIcon.Foreground = isCancelHovered ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            _cancelIcon.FontSize = isCancelHovered ? 15 : 13;
        }

        // 2. 编辑按钮反馈
        var isEditHovered = state == OriginActionState.Edit;
        if (_editButtonBorder != null)
        {
            _editButtonBorder.Background = new SolidColorBrush(isEditHovered ? Color.FromArgb(240, 0x10, 0xB9, 0x81) : Color.FromArgb(180, 0x12, 0x16, 0x15));
            _editButtonBorder.BorderBrush = new SolidColorBrush(isEditHovered ? Brushes.White.Color : Color.FromArgb(100, 0x10, 0xB9, 0x81));
        }
        if (_editButtonText != null)
        {
            _editButtonText.Foreground = isEditHovered ? Brushes.White : new SolidColorBrush(Color.FromArgb(220, 0x10, 0xB9, 0x81));
        }

        // 3. 置顶按钮反馈
        var isPinHovered = state == OriginActionState.Pin;
        if (_pinButtonBorder != null)
        {
            _pinButtonBorder.Background = new SolidColorBrush(isPinHovered ? Color.FromArgb(240, 0x02, 0x84, 0xC7) : Color.FromArgb(180, 0x12, 0x14, 0x1C));
            _pinButtonBorder.BorderBrush = new SolidColorBrush(isPinHovered ? Brushes.White.Color : Color.FromArgb(100, 0x38, 0xBD, 0xF8));
        }
        if (_pinButtonText != null)
        {
            _pinButtonText.Foreground = isPinHovered ? Brushes.White : new SolidColorBrush(Color.FromArgb(220, 0x38, 0xBD, 0xF8));
        }

        // 4. 轨迹画笔色调联动与匹配徽标联动
        if (isCancelHovered)
        {
            if (_glowPath != null) _glowPath.Stroke = new SolidColorBrush(Color.FromArgb(50, 0xEF, 0x44, 0x44));
            if (_corePath != null) _corePath.Stroke = new SolidColorBrush(Color.FromArgb(160, 0xEF, 0x44, 0x44));
            if (_previewBadge != null) _previewBadge.Visibility = Visibility.Collapsed;
        }
        else if (isEditHovered)
        {
            if (_glowPath != null) _glowPath.Stroke = new SolidColorBrush(Color.FromArgb(80, 0x10, 0xB9, 0x81));
            if (_corePath != null) _corePath.Stroke = new SolidColorBrush(Color.FromArgb(200, 0x10, 0xB9, 0x81));
            if (_previewBadge != null) _previewBadge.Visibility = Visibility.Collapsed;
        }
        else if (isPinHovered)
        {
            if (_glowPath != null) _glowPath.Stroke = new SolidColorBrush(Color.FromArgb(80, 0x02, 0x84, 0xC7));
            if (_corePath != null) _corePath.Stroke = new SolidColorBrush(Color.FromArgb(200, 0x38, 0xBD, 0xF8));
            if (_previewBadge != null) _previewBadge.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (_glowPath != null && _glowGradientBrush != null) _glowPath.Stroke = _glowGradientBrush;
            if (_corePath != null && _coreGradientBrush != null) _corePath.Stroke = _coreGradientBrush;
        }
    }

    private void CreateCheatsheetHUD(Point point, IReadOnlyList<MouseGestureCheatItem> cheatItems)
    {
        var rootStack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical,
            Margin = new Thickness(10, 8, 10, 8)
        };

        // 顶部小标题（居中对齐）
        var header = new TextBlock
        {
            Text = "可用手势 · 移回起点 ✕ 取消",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 0x10, 0xB9, 0x81)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        rootStack.Children.Add(header);

        var uniqueItems = cheatItems
            .DistinctBy(x => $"{x.Name}:{x.Sign ?? x.DisplaySequence}")
            .Take(12)
            .ToList();

        var count = uniqueItems.Count;
        // 根据手势数量自适应紧凑网格宽度（1-4个单行，5个以上多行）
        var maxWrapWidth = count switch
        {
            1 => 86,
            2 => 166,
            3 => 246,
            4 => 326,
            5 or 6 => 246,
            _ => 326
        };

        var wrap = new WrapPanel
        {
            MaxWidth = maxWrapWidth,
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        foreach (var item in uniqueItems)
        {
            var itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(135, 0x18, 0x18, 0x22)), // 高通透磨砂底色
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4, 5, 4, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Width = 74,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var itemStack = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            // 上部分：高通透深色圆角手势轨迹预览框
            var previewBox = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(160, 0x0A, 0x0A, 0x10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                ClipToBounds = true
            };

            var (gestureGeometry, gestureBrush) = MouseGesturePreviewGeometryFactory.CreatePreview(item.DisplaySequence, item.Data, size: 34, padding: 4);

            var path = new Path
            {
                Data = gestureGeometry,
                Stroke = gestureBrush,
                StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            previewBox.Child = path;
            itemStack.Children.Add(previewBox);

            // 下部分：功能名称 + 手势名
            var nameText = new TextBlock
            {
                Text = item.Name,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                MaxWidth = 66,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            itemStack.Children.Add(nameText);

            var isRawSequence = string.IsNullOrWhiteSpace(item.Sign)
                || item.Sign.Contains('-')
                || item.Sign.Equals("UP", StringComparison.OrdinalIgnoreCase)
                || item.Sign.Equals("DOWN", StringComparison.OrdinalIgnoreCase)
                || item.Sign.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
                || item.Sign.Equals("RIGHT", StringComparison.OrdinalIgnoreCase)
                || item.Sign.Any(static ch => ch is '↑' or '↗' or '→' or '↘' or '↓' or '↙' or '←' or '↖');
            if (!isRawSequence)
            {
                var signText = new TextBlock
                {
                    Text = item.Sign,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    MaxWidth = 66,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                itemStack.Children.Add(signText);
            }

            itemBorder.Child = itemStack;
            wrap.Children.Add(itemBorder);
        }

        rootStack.Children.Add(wrap);

        _cheatsheetHost = new Border
        {
            Child = rootStack,
            Background = new SolidColorBrush(Color.FromArgb(145, 0x10, 0x10, 0x16)), // 高通透半透明磨砂背景
            BorderBrush = new SolidColorBrush(Color.FromArgb(75, 0x10, 0xB9, 0x81)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            IsHitTestVisible = false
        };

        // 测量尺寸以实现以起点为水平中心的精确对齐
        _cheatsheetHost.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        _cheatsheetDesiredWidth = _cheatsheetHost.DesiredSize.Width;
        _cheatsheetDesiredHeight = _cheatsheetHost.DesiredSize.Height;

        Canvas.SetZIndex(_cheatsheetHost, 10);
        _canvas.Children.Add(_cheatsheetHost);

        UpdateCheatsheetPosition(point);
    }

    private void UpdateCheatsheetPosition(Point currentPoint)
    {
        if (_cheatsheetHost == null || !_startPoint.HasValue || _cheatsheetDesiredWidth <= 0) return;

        var start = _startPoint.Value;
        var deltaY = currentPoint.Y - start.Y;

        // 水平方向：以激发起点 X 为中心
        var left = start.X - (_cheatsheetDesiredWidth / 2.0);

        // 垂直方向响应式避让（预留避开【编辑/置顶】按钮的安全距离）：
        // 如果手势正在向下划动（deltaY > 12），将看板放到起点上方，腾出下方划线空间；
        // 如果手势向上划动（deltaY < -12），将看板放到起点下方；
        // 初始状态（|deltaY| <= 12）：优先放下方（距离起点 +54px 彻底避开起点按钮组）。
        double top;
        if (deltaY > 12)
        {
            top = start.Y - _cheatsheetDesiredHeight - 42;
            if (top < 15) top = start.Y + 54;
        }
        else if (deltaY < -12)
        {
            top = start.Y + 54;
            if (top + _cheatsheetDesiredHeight > Height - 15) top = start.Y - _cheatsheetDesiredHeight - 42;
        }
        else
        {
            top = start.Y + 54;
            if (top + _cheatsheetDesiredHeight > Height - 20)
            {
                top = start.Y - _cheatsheetDesiredHeight - 42;
            }
        }

        // 边界防溢出保护
        if (left < 15) left = 15;
        if (left + _cheatsheetDesiredWidth > Width - 15) left = Width - _cheatsheetDesiredWidth - 15;
        if (top < 15) top = 15;
        if (top + _cheatsheetDesiredHeight > Height - 15) top = Height - _cheatsheetDesiredHeight - 15;

        Canvas.SetLeft(_cheatsheetHost, left);
        Canvas.SetTop(_cheatsheetHost, top);
    }

    private void AddCancelBadge(Point point)
    {
        var text = new TextBlock
        {
            Text = "✕ 手势已取消",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
        };
        var border = new Border
        {
            Child = text,
            Background = new SolidColorBrush(Color.FromArgb(220, 0x17, 0x17, 0x1F)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0xEF, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 8, 5),
            IsHitTestVisible = false
        };
        Canvas.SetZIndex(border, 10);
        Canvas.SetLeft(border, point.X + 10);
        Canvas.SetTop(border, point.Y - 14);
        _canvas.Children.Add(border);
    }

    public void ShowInstantAction(string title, string detail, Point screenPoint, string glyph = "势")
    {
        _hideTimer.Stop();
        Opacity = 0;
        if (_canvas != null)
        {
            _canvas.Visibility = Visibility.Hidden;
        }
        Clear();

        SyncBounds(screenPoint);

        var localPoint = ToLocal(screenPoint);
        _lastPoint = localPoint;
        AddDot(localPoint, 14, Color.FromRgb(0x3B, 0x82, 0xF6));

        var preview = new MouseGesturePreviewInfo(
            ExtensionName: title,
            IconReference: null,
            ExtensionDirectoryPath: null,
            DisplayGlyph: glyph,
            Sign: detail,
            Sequence: string.Empty);

        AddResultBadge(preview, localPoint);

        if (_canvas != null)
        {
            _canvas.Visibility = Visibility.Visible;
        }
        Opacity = 1.0;

        if (!IsVisible)
        {
            Show();
        }

        _hideTimer.Interval = TimeSpan.FromMilliseconds(450);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void SyncBounds(Point screenPoint)
    {
        try
        {
            var screenCtx = ScreenHelper.GetScreenContextAtPoint(screenPoint);
            var monLeft = screenCtx.DipBounds.Left;
            var monTop = screenCtx.DipBounds.Top;
            var monWidth = screenCtx.DipBounds.Width;
            var monHeight = screenCtx.DipBounds.Height;

            if (Math.Abs(Left - monLeft) > 1 || Math.Abs(Top - monTop) > 1 ||
                Math.Abs(Width - monWidth) > 1 || Math.Abs(Height - monHeight) > 1)
            {
                Left = monLeft;
                Top = monTop;
                Width = monWidth;
                Height = monHeight;
            }
        }
        catch
        {
            // fallback
        }

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private Point ToLocal(Point screenPoint)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                var pt = new POINT { x = (int)Math.Round(screenPoint.X), y = (int)Math.Round(screenPoint.Y) };
                if (ScreenToClient(helper.Handle, ref pt))
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                    var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
                    return new Point(pt.x / scaleX, pt.y / scaleY);
                }
            }
        }
        catch { /* fallback below */ }

        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var logicalPoint = source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
                return new Point(logicalPoint.X - Left, logicalPoint.Y - Top);
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
            return new Point((screenPoint.X / scaleX) - Left, (screenPoint.Y / scaleY) - Top);
        }
        catch
        {
            return new Point(screenPoint.X - Left, screenPoint.Y - Top);
        }
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
        _originActionBarHost = null;
        _editButtonBorder = null;
        _pinButtonBorder = null;
        _cancelCircle = null;
        _cancelHalo = null;
        _cancelIcon = null;
        _editButtonText = null;
        _pinButtonText = null;
        _cheatsheetHost = null;
        _startPoint = null;
        _lastPoint = null;
        _pathFigure = null;
        _glowPath = null;
        _corePath = null;
        _coreGradientBrush = null;
        _glowGradientBrush = null;
        _hasMovedFarFromStart = false;
        _currentOriginAction = OriginActionState.None;
        _rawTracePoints.Clear();
        _smoothTracePoints.Clear();
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

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
}
