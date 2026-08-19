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
    private Border? _cancelZoneHost;
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
    private bool _isCancelled;
    private readonly List<Point> _rawTracePoints = new(capacity: 256);
    private readonly List<Point> _smoothTracePoints = new(capacity: 256);

    public bool IsCancelled => _isCancelled;

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
        UpdateLayout();

        SyncBounds();

        var localPoint = ToLocal(screenPoint);
        _startPoint = localPoint;
        _lastPoint = localPoint;
        _hasMovedFarFromStart = false;
        _isCancelled = false;
        _rawTracePoints.Clear();
        _smoothTracePoints.Clear();
        _rawTracePoints.Add(localPoint);
        _smoothTracePoints.Add(localPoint);

        // 1. 创建起始点同心发光圆点 (白核心 + 翠绿晕圈)
        AddStartIndicator(localPoint);

        // 2. 创建起点取消格子
        CreateCancelZone(localPoint);

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
            _canvas.UpdateLayout();
        }

        UpdateLayout();
        Opacity = 1.0;

        if (!IsVisible)
        {
            Show();
        }

        AttachHwndHook();
        EnsureClickThrough();
        ForceDwmRepaint();
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
        if (distFromStart > 28)
        {
            _hasMovedFarFromStart = true;
        }

        // 检查是否移回起点取消区（距离起点小于 24px）
        if (_hasMovedFarFromStart && distFromStart < 24)
        {
            if (!_isCancelled)
            {
                _isCancelled = true;
                UpdateCancelZoneVisual(isHovered: true);
                if (_glowPath != null) _glowPath.Stroke = new SolidColorBrush(Color.FromArgb(50, 0xEF, 0x44, 0x44));
                if (_corePath != null) _corePath.Stroke = new SolidColorBrush(Color.FromArgb(160, 0xEF, 0x44, 0x44));
                if (_previewBadge != null) _previewBadge.Visibility = Visibility.Collapsed;
            }
        }
        else if (_isCancelled && distFromStart >= 28)
        {
            // 移出取消区，恢复白到绿渐变轨迹
            _isCancelled = false;
            UpdateCancelZoneVisual(isHovered: false);
            if (_glowPath != null && _glowGradientBrush != null) _glowPath.Stroke = _glowGradientBrush;
            if (_corePath != null && _coreGradientBrush != null) _corePath.Stroke = _coreGradientBrush;
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
        if (!IsVisible || _isCancelled)
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
            if (_isCancelled)
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
        Opacity = 0;
        if (_canvas != null)
        {
            _canvas.Visibility = Visibility.Hidden;
        }
        Clear();
        UpdateLayout();
        Left = -32000;
        Top = -32000;
        base.Hide();
        ForceDwmRepaint();
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

    private void CreateCancelZone(Point point)
    {
        var grid = new Grid
        {
            Width = 60,
            Height = 60,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        // 外层脉冲光圈（hover 时显现）
        var halo = new Ellipse
        {
            Width = 48,
            Height = 48,
            Fill = new SolidColorBrush(Color.FromArgb(35, 0xEF, 0x44, 0x44)),
            Stroke = new SolidColorBrush(Color.FromArgb(90, 0xEF, 0x44, 0x44)),
            StrokeThickness = 1,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        var circle = new Ellipse
        {
            Width = 32,
            Height = 32,
            Fill = new SolidColorBrush(Color.FromArgb(200, 0x1F, 0x1F, 0x24)),
            Stroke = new SolidColorBrush(Color.FromArgb(220, 0xEF, 0x44, 0x44)),
            StrokeThickness = 2,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var icon = new TextBlock
        {
            Text = "✕",
            Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        grid.Children.Add(halo);
        grid.Children.Add(circle);
        grid.Children.Add(icon);

        _cancelZoneHost = new Border
        {
            Width = 60,
            Height = 60,
            Child = grid,
            IsHitTestVisible = false
        };

        Canvas.SetZIndex(_cancelZoneHost, 5);
        Canvas.SetLeft(_cancelZoneHost, point.X - 30);
        Canvas.SetTop(_cancelZoneHost, point.Y - 30);
        _canvas.Children.Add(_cancelZoneHost);
    }

    private void UpdateCancelZoneVisual(bool isHovered)
    {
        if (_cancelZoneHost?.Child is not Grid grid) return;
        if (grid.Children[0] is Ellipse halo)
        {
            halo.Visibility = isHovered ? Visibility.Visible : Visibility.Collapsed;
        }
        if (grid.Children[1] is Ellipse circle)
        {
            circle.Fill = new SolidColorBrush(isHovered ? Color.FromArgb(240, 0xEF, 0x44, 0x44) : Color.FromArgb(200, 0x1F, 0x1F, 0x24));
            circle.Stroke = new SolidColorBrush(isHovered ? Brushes.White.Color : Color.FromArgb(220, 0xEF, 0x44, 0x44));
            circle.Width = isHovered ? 40 : 32;
            circle.Height = isHovered ? 40 : 32;
        }
        if (grid.Children[2] is TextBlock icon)
        {
            icon.Foreground = isHovered ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            icon.FontSize = isHovered ? 16 : 14;
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

        var count = Math.Min(cheatItems.Count, 12);
        // 根据手势数量自适应网格宽度（1-2个单列/双列，3个以上2-3列）
        var maxWrapWidth = count switch
        {
            1 => 170,
            2 => 350,
            3 => 520,
            4 => 350,
            _ => 520
        };

        var wrap = new WrapPanel
        {
            MaxWidth = maxWrapWidth,
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        foreach (var item in cheatItems.Take(12))
        {
            var itemBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(125, 0x18, 0x18, 0x22)), // 高通透磨砂底色
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Width = 164
            };

            var itemGrid = new Grid();
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 左侧：高通透深色圆角完整手势轨迹预览框
            var previewBox = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(150, 0x0A, 0x0A, 0x10)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 8, 0),
                ClipToBounds = true
            };

            var gestureGeometry = MouseGesturePreviewGeometryFactory.Create(item.DisplaySequence, item.Data, size: 36, padding: 5);
            var thumbnailGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            thumbnailGradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
            thumbnailGradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 110, 231, 183), 0.35));
            thumbnailGradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 16, 185, 129), 0.85));
            thumbnailGradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 5, 150, 105), 1.0));

            var path = new Path
            {
                Data = gestureGeometry,
                Stroke = thumbnailGradient,
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            previewBox.Child = path;
            Grid.SetColumn(previewBox, 0);
            itemGrid.Children.Add(previewBox);

            // 右侧：功能名称 + 纯净手势名
            var textStack = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var nameText = new TextBlock
            {
                Text = item.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                MaxWidth = 104,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textStack.Children.Add(nameText);

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
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                    MaxWidth = 104,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                textStack.Children.Add(signText);
            }

            Grid.SetColumn(textStack, 1);
            itemGrid.Children.Add(textStack);

            itemBorder.Child = itemGrid;
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

        // 垂直方向响应式避让：
        // 如果手势正在向下划动（deltaY > 12），将看板放到起点上方，腾出下方划线空间；
        // 如果手势向上划动（deltaY < -12），将看板放到起点下方；
        // 初始状态（|deltaY| <= 12）：优先放下方（除非贴近屏幕底边）。
        double top;
        if (deltaY > 12)
        {
            top = start.Y - _cheatsheetDesiredHeight - 36;
            if (top < 15) top = start.Y + 36;
        }
        else if (deltaY < -12)
        {
            top = start.Y + 36;
            if (top + _cheatsheetDesiredHeight > Height - 15) top = start.Y - _cheatsheetDesiredHeight - 36;
        }
        else
        {
            top = start.Y + 36;
            if (top + _cheatsheetDesiredHeight > Height - 20)
            {
                top = start.Y - _cheatsheetDesiredHeight - 36;
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
        UpdateLayout();

        SyncBounds();

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
        UpdateLayout();
        _canvas?.UpdateLayout();
        Opacity = 1.0;

        if (!IsVisible)
        {
            Show();
        }

        AttachHwndHook();
        EnsureClickThrough();
        ForceDwmRepaint();

        _hideTimer.Interval = TimeSpan.FromMilliseconds(450);
        _hideTimer.Stop();
        _hideTimer.Start();
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
        _cancelZoneHost = null;
        _cheatsheetHost = null;
        _startPoint = null;
        _lastPoint = null;
        _pathFigure = null;
        _glowPath = null;
        _corePath = null;
        _coreGradientBrush = null;
        _glowGradientBrush = null;
        _hasMovedFarFromStart = false;
        _isCancelled = false;
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

    private void ForceDwmRepaint()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwUpdateNow | RdwAllChildren);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwAllChildren = 0x0080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
}
