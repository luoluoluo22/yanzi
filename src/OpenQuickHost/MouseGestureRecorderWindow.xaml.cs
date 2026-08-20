using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenQuickHost.Sync;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Key = System.Windows.Input.Key;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Canvas = System.Windows.Controls.Canvas;

namespace OpenQuickHost;

/// <summary>
/// 全屏透明蒙版的鼠标手势录制器。
/// 按住右键 / 中键拖动 → 实时画橘色轨迹 → 松开后量化成 8 方向序列。
/// </summary>
public partial class MouseGestureRecorderWindow : Window
{
    private readonly string _trigger; // right-drag / middle-drag
    private readonly List<Point> _path = new();
    private readonly List<UIElement> _strokeElements = new();
    private bool _drawing;
    private PathFigure? _strokeFigure;
    private LinearGradientBrush? _coreBrush;
    private LinearGradientBrush? _glowBrush;

    private static readonly string[] Arrows = ["→","↘","↓","↙","←","↖","↑","↗"];
    private const int MinSegmentDistance = 30; // 单段最小像素距离
    private const double TwoPi = Math.PI * 2;
    private const double EightthPi = Math.PI / 4;

    public string ResultSequence { get; private set; } = string.Empty;
    public string ResultTrigger { get; private set; } = "right-drag";
    public string ResultSign { get; private set; } = string.Empty;
    public int[]? ResultTemplateData { get; private set; }
    public bool WasAccepted { get; private set; }

    /// <summary>
    /// 已注册手势集合，用于显示冲突提示。Key=trigger+"|"+sequence，Value=扩展显示名称列表。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? KnownGestures { get; init; }

    public MouseGestureRecorderWindow(string trigger, string? initialSequence)
    {
        InitializeComponent();
        _trigger = NormalizeTrigger(trigger);
        ResultTrigger = _trigger;
        TriggerLabelRun.Text = _trigger switch
        {
            "middle-drag" => "鼠标中键",
            "ctrl-left-drag" => "Ctrl+左键",
            _ => "鼠标右键"
        };

        // 占满所有显示器（跨多屏虚拟桌面）
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        if (!string.IsNullOrWhiteSpace(initialSequence))
        {
            ResultSequence = initialSequence!;
            ResultSign = MouseGestureNaming.GetDisplayName(ResultSequence);
            GestureNameText.Text = ResultSign;
        }

        PreviewMouseDown += OnMouseDownAny;
        PreviewMouseMove += OnMouseMoveAny;
        PreviewMouseUp += OnMouseUpAny;
        PreviewMouseRightButtonDown += OnRightButtonDown;
        PreviewMouseRightButtonUp += OnRightButtonUp;
        KeyDown += OnKeyDownAny;

        // ContextMenu 默认会在右键弹出，这里抑制
        StrokeCanvas.MouseRightButtonUp += (_, e) => e.Handled = true;

        Loaded += (_, _) => RepositionOverlaysToMonitor();
        SourceInitialized += (_, _) => RepositionOverlaysToMonitor();
    }

    private void OnKeyDownAny(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            WasAccepted = false;
            Close();
        }
    }

    private void OnRightButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger != "right-drag") return;
        StartStroke(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnRightButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger != "right-drag") return;
        FinishStroke();
        e.Handled = true;
    }

    private void OnMouseDownAny(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger == "middle-drag" && e.ChangedButton == MouseButton.Middle)
        {
            StartStroke(e.GetPosition(this));
            e.Handled = true;
        }
        else if (_trigger == "ctrl-left-drag" && e.ChangedButton == MouseButton.Left && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            StartStroke(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMouseUpAny(object? sender, MouseButtonEventArgs e)
    {
        if (_trigger == "middle-drag" && e.ChangedButton == MouseButton.Middle)
        {
            FinishStroke();
            e.Handled = true;
        }
        else if (_trigger == "ctrl-left-drag" && e.ChangedButton == MouseButton.Left)
        {
            FinishStroke();
            e.Handled = true;
        }
    }

    private void OnMouseMoveAny(object? sender, MouseEventArgs e)
    {
        if (!_drawing) return;
        var pt = e.GetPosition(this);
        if (_path.Count > 0)
        {
            var last = _path[^1];
            if ((pt - last).Length < 3.0) return; // 抖动过滤
            AppendStrokePoint(pt);
        }
        _path.Add(pt);
    }

    private void StartStroke(Point start)
    {
        ClearStroke();
        _drawing = true;
        _path.Add(start);
        RepositionOverlaysToMonitor(start);
        ResultPanel.Visibility = Visibility.Collapsed;
        HintRing.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        HintText.Text = "正在录制… 松开即可识别";

        // 起点发光标记 (白绿翡翠)
        var halo = new Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = new SolidColorBrush(Color.FromArgb(90, 0x10, 0xB9, 0x81)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(halo, start.X - 11);
        Canvas.SetTop(halo, start.Y - 11);
        StrokeCanvas.Children.Add(halo);
        _strokeElements.Add(halo);

        var dot = new Ellipse
        {
            Width = 12, Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(dot, start.X - 6);
        Canvas.SetTop(dot, start.Y - 6);
        StrokeCanvas.Children.Add(dot);
        _strokeElements.Add(dot);

        _strokeFigure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(_strokeFigure);

        _coreBrush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = start,
            EndPoint = start
        };
        _coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
        _coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 110, 231, 183), 0.3));
        _coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 16, 185, 129), 0.8));
        _coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 5, 150, 105), 1.0));

        _glowBrush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = start,
            EndPoint = start
        };
        _glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
        _glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 16, 185, 129), 0.5));
        _glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 5, 150, 105), 1.0));

        // 外层柔光 (白到绿流光晕)
        var glowPath = new Path
        {
            Data = pathGeometry,
            Stroke = _glowBrush,
            StrokeThickness = 12,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        // 内层高亮核心 (白到绿亮芯)
        var corePath = new Path
        {
            Data = pathGeometry,
            Stroke = _coreBrush,
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        StrokeCanvas.Children.Add(glowPath);
        StrokeCanvas.Children.Add(corePath);
        _strokeElements.Add(glowPath);
        _strokeElements.Add(corePath);
    }

    private void AppendStrokePoint(Point point)
    {
        if (_strokeFigure == null) return;

        if (_coreBrush != null) _coreBrush.EndPoint = point;
        if (_glowBrush != null) _glowBrush.EndPoint = point;

        var count = _path.Count;
        if (count == 1)
        {
            _strokeFigure.Segments.Add(new LineSegment(point, isStroked: true));
        }
        else if (count == 2)
        {
            var p0 = _path[0];
            var p1 = _path[1];
            var cp = new Point((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0);
            _strokeFigure.Segments.Add(new QuadraticBezierSegment(cp, point, isStroked: true));
        }
        else
        {
            // Catmull-Rom 转换三次贝塞尔，确保切线平滑连接
            var p0 = _path[^3];
            var p1 = _path[^2];
            var p2 = _path[^1];
            var p3 = point;

            var cp1 = new Point(p1.X + (p2.X - p0.X) / 6.0, p1.Y + (p2.Y - p0.Y) / 6.0);
            var cp2 = new Point(p2.X - (p3.X - p1.X) / 6.0, p2.Y - (p3.Y - p1.Y) / 6.0);
            _strokeFigure.Segments.Add(new BezierSegment(cp1, cp2, p2, isStroked: true));
        }
    }

    private void FinishStroke()
    {
        if (!_drawing) return;
        _drawing = false;
        HintRing.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        HintText.Text = "可以重新录制 · 也可保存";
        // 终点标记
        if (_path.Count > 0)
        {
            var last = _path[^1];
            var dot = new Ellipse
            {
                Width = 14, Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(dot, last.X - 7);
            Canvas.SetTop(dot, last.Y - 7);
            StrokeCanvas.Children.Add(dot);
            _strokeElements.Add(dot);
        }

        var sequence = SimplifyPath(_path);
        if (sequence.Length == 0)
        {
            ResultSequence = string.Empty;
            ResultSign = string.Empty;
            ResultTemplateData = null;
            GestureNameText.Text = "未识别";
            ArrowList.ItemsSource = new[] { "?" };
            RawSeqText.Text = "轨迹太短或未识别，请再试一次";
            ConflictText.Text = string.Empty;
        }
        else
        {
            ResultSequence = sequence;
            ResultSign = MouseGestureNaming.GetDisplayName(sequence, _path);
            ResultTemplateData = MouseGestureTemplateRecognizer.CreateTemplateData(_path);
            var builtIn = MouseGestureTemplateRecognizer.RecognizeBuiltInSign(_path);
            GestureNameText.Text = string.IsNullOrWhiteSpace(builtIn) ? ResultSign : $"{ResultSign} (特征识别)";
            ArrowList.ItemsSource = sequence.ToCharArray();
            RawSeqText.Text = sequence;
            UpdateConflictHint();
        }
        RepositionOverlaysToMonitor(_path.Count > 0 ? _path[^1] : null);
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void ClearStroke()
    {
        foreach (var el in _strokeElements)
        {
            StrokeCanvas.Children.Remove(el);
        }
        _strokeElements.Clear();
        _path.Clear();
        _strokeFigure = null;
    }

    private void UpdateConflictHint()
    {
        if (KnownGestures == null || string.IsNullOrEmpty(ResultSequence))
        {
            ConflictText.Text = string.Empty;
            return;
        }
        var key = $"{_trigger}|{ResultSequence}";
        if (KnownGestures.TryGetValue(key, out var owners) && owners.Count > 0)
        {
            ConflictText.Text = $"⚠ 与已有扩展冲突：{string.Join("、", owners)}。保存后多扩展共用同序列时将以选择气泡确认。";
            ConflictText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));
        }
        else
        {
            ConflictText.Text = "✓ 没有冲突，这是一个新手势";
            ConflictText.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        }
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        ClearStroke();
        ResultSequence = string.Empty;
        ResultSign = string.Empty;
        ResultTemplateData = null;
        GestureNameText.Text = string.Empty;
        ResultPanel.Visibility = Visibility.Collapsed;
        HintRing.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        HintText.Text = "准备就绪 · 按住右键开始";
        RepositionOverlaysToMonitor();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasAccepted = false;
        DialogResult = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResultSequence))
        {
            CancelButton_Click(sender, e);
            return;
        }
        WasAccepted = true;
        DialogResult = true;
    }

    private static string SimplifyPath(IReadOnlyList<Point> pts)
    {
        return MouseGestureTemplateRecognizer.ExtractSequence(pts, minStepDistance: MinSegmentDistance);
    }

    private static string NormalizeTrigger(string? raw)
    {
        return raw switch
        {
            "middle-drag" => "middle-drag",
            "ctrl-left-drag" => "ctrl-left-drag",
            _ => "right-drag"
        };
    }

    private void RepositionOverlaysToMonitor(Point? localAnchorPoint = null)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            POINT physicalPoint;
            if (localAnchorPoint.HasValue && helper.Handle != IntPtr.Zero)
            {
                var pt = new POINT { x = (int)Math.Round(localAnchorPoint.Value.X), y = (int)Math.Round(localAnchorPoint.Value.Y) };
                if (ClientToScreen(helper.Handle, ref pt))
                {
                    physicalPoint = pt;
                }
                else
                {
                    GetCursorPos(out physicalPoint);
                }
            }
            else
            {
                GetCursorPos(out physicalPoint);
            }

            var monitor = MonitorFromPoint(physicalPoint, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref mi))
                {
                    var topLeft = ToLocal(new Point(mi.rcMonitor.left, mi.rcMonitor.top));
                    var bottomRight = ToLocal(new Point(mi.rcMonitor.right, mi.rcMonitor.bottom));
                    var monWidth = Math.Max(100, Math.Abs(bottomRight.X - topLeft.X));
                    var monHeight = Math.Max(100, Math.Abs(bottomRight.Y - topLeft.Y));
                    var monRect = new Rect(topLeft.X, topLeft.Y, monWidth, monHeight);

                    // 1. 顶部提示居中在当前屏幕
                    if (TopHintPanel != null)
                    {
                        TopHintPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                        TopHintPanel.VerticalAlignment = VerticalAlignment.Top;
                        TopHintPanel.Width = monWidth;
                        TopHintPanel.Margin = new Thickness(monRect.Left, monRect.Top + 40, 0, 0);
                    }

                    // 2. 中央指示居中在当前屏幕
                    if (CenterHint != null)
                    {
                        CenterHint.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                        CenterHint.VerticalAlignment = VerticalAlignment.Top;
                        CenterHint.Width = monWidth;
                        var centerY = monRect.Top + (monHeight / 2.0) - 70;
                        CenterHint.Margin = new Thickness(monRect.Left, centerY, 0, 0);
                    }

                    // 3. 底部结果面板居中在当前屏幕下方
                    if (ResultPanel != null)
                    {
                        ResultPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                        ResultPanel.VerticalAlignment = VerticalAlignment.Top;
                        ResultPanel.Measure(new System.Windows.Size(monWidth, monHeight));
                        var panelWidth = ResultPanel.DesiredSize.Width > 0 ? ResultPanel.DesiredSize.Width : (ResultPanel.ActualWidth > 0 ? ResultPanel.ActualWidth : 480);
                        var panelLeft = monRect.Left + Math.Max(0, (monWidth - panelWidth) / 2.0);
                        var panelHeight = ResultPanel.DesiredSize.Height > 0 ? ResultPanel.DesiredSize.Height : (ResultPanel.ActualHeight > 0 ? ResultPanel.ActualHeight : 220);
                        var panelTop = monRect.Bottom - panelHeight - 40;
                        ResultPanel.Margin = new Thickness(panelLeft, panelTop, 0, 0);
                    }
                }
            }
        }
        catch
        {
            // 忽略异常并使用默认对齐
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

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

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
}
