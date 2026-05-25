using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenQuickHost;

public partial class WindowSnapAssistOverlayWindow : Window
{
    private readonly Action<WindowSnapAssistMode> _onSelected;
    private readonly Dictionary<WindowSnapAssistMode, Border> _modeItems;
    private readonly Dictionary<int, CustomSlotVisual> _customSlotItems = new();
    private readonly Dictionary<int, WindowSnapAssistCustomLayoutSettings> _customLayouts = new();
    private WindowSnapAssistMode _selectedMode = WindowSnapAssistMode.None;
    private WindowSnapAssistActivationMode _activationMode = WindowSnapAssistActivationMode.MouseEdge;
    private bool _isSelecting;
    private bool _wasControlDownForMenu;
    private DismissOverlayWindow? _menuDismissOverlay;

    private const double WheelCenter = 184;
    private const double CancelRadius = 24;
    private const double RestoreRadius = 36;
    private const double CenterNeutralRadius = 52;
    private const double LayoutOuterRadius = 96;
    private const double CustomSlotInnerRadius = 108;
    private const double CustomSlotOuterRadius = 154;
    private const int CustomSlotCount = WindowSnapAssistCustomLayoutSettings.TotalSlotCount;

    public WindowSnapAssistOverlayWindow(Action<WindowSnapAssistMode> onSelected)
    {
        InitializeComponent();
        _onSelected = onSelected;
        _modeItems = new Dictionary<WindowSnapAssistMode, Border>
        {
            [WindowSnapAssistMode.TopLeft] = TopLeftItem,
            [WindowSnapAssistMode.TopHalf] = TopItem,
            [WindowSnapAssistMode.TopRight] = TopRightItem,
            [WindowSnapAssistMode.RightHalf] = RightItem,
            [WindowSnapAssistMode.BottomRight] = BottomRightItem,
            [WindowSnapAssistMode.BottomHalf] = BottomItem,
            [WindowSnapAssistMode.BottomLeft] = BottomLeftItem,
            [WindowSnapAssistMode.LeftHalf] = LeftItem
        };
        InnerMaximizeHalf.Data = CreateSectorGeometry(WheelCenter, WheelCenter, 0, RestoreRadius, -180, 0);
        InnerRestoreHalf.Data = CreateSectorGeometry(WheelCenter, WheelCenter, 0, RestoreRadius, 0, 180);
        BuildCustomSlotVisuals();
        Loaded += (_, _) => EnsureToolWindowStyle();

        if (CenterButton.ContextMenu != null)
        {
            CenterButton.ContextMenu.Closed += (_, _) => HideMenuDismissOverlay();
        }
    }

    /// <summary>
    /// 当用户通过右键菜单禁用时触发
    /// </summary>
    public event Action? DisableRequested;

    public event Action<int>? ClearCustomSlotRequested;

    public event Action<WindowSnapAssistMode>? SelectionChanged;

    public bool IsSelecting => _isSelecting;

    public void SetActivationMode(WindowSnapAssistActivationMode activationMode)
    {
        if (activationMode == _activationMode)
        {
            return;
        }

        _activationMode = activationMode;
        UpdateSelectionVisual();
    }

    public void SetCustomLayouts(IReadOnlyCollection<WindowSnapAssistCustomLayoutSettings> layouts)
    {
        _customLayouts.Clear();
        foreach (var layout in layouts)
        {
            if (layout.SlotIndex is >= 0 and < CustomSlotCount)
            {
                _customLayouts[layout.SlotIndex] = layout;
            }
        }

        UpdateCustomSlotLayerVisibility(GetCustomSlotIndex(_selectedMode) >= 0);
        UpdateSelectionVisual();
    }

    /// <summary>
    /// 设置中心按钮显示的目标窗口图标
    /// </summary>
    public void SetTargetIcon(System.Windows.Media.Imaging.BitmapSource? icon)
    {
        if (icon != null)
        {
            AppIconImage.Source = icon;
            AppIconImage.Visibility = Visibility.Visible;
            DefaultIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            AppIconImage.Visibility = Visibility.Collapsed;
            DefaultIcon.Visibility = Visibility.Visible;
        }
    }

    private void BuildCustomSlotVisuals()
    {
        CustomSlotLayer.Children.Clear();
        _customSlotItems.Clear();

        for (var index = 0; index < CustomSlotCount; index++)
        {
            var startAngle = (index * 360.0 / CustomSlotCount) - 90;
            var endAngle = ((index + 1) * 360.0 / CustomSlotCount) - 90;
            var sector = new System.Windows.Shapes.Path
            {
                Data = CreateSectorGeometry(WheelCenter, WheelCenter, CustomSlotInnerRadius, CustomSlotOuterRadius, startAngle + 1.4, endAngle - 1.4),
                Fill = System.Windows.Media.Brushes.Black,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(34, 103, 232, 249)),
                StrokeThickness = 1
            };
            var content = new Canvas
            {
                Width = 30,
                Height = 22,
                IsHitTestVisible = false
            };
            var midAngle = (startAngle + endAngle) / 2.0;
            var contentCenter = PointOnCircle(WheelCenter, WheelCenter, (CustomSlotInnerRadius + CustomSlotOuterRadius) / 2.0, midAngle);
            Canvas.SetLeft(content, contentCenter.X - (content.Width / 2.0));
            Canvas.SetTop(content, contentCenter.Y - (content.Height / 2.0));

            _customSlotItems[index] = new CustomSlotVisual(sector, content);
            CustomSlotLayer.Children.Add(sector);
            CustomSlotLayer.Children.Add(content);
        }
    }

    private static Geometry CreateSectorGeometry(double centerX, double centerY, double innerRadius, double outerRadius, double startAngle, double endAngle)
    {
        var startOuter = PointOnCircle(centerX, centerY, outerRadius, startAngle);
        var endOuter = PointOnCircle(centerX, centerY, outerRadius, endAngle);
        var startInner = PointOnCircle(centerX, centerY, innerRadius, startAngle);
        var endInner = PointOnCircle(centerX, centerY, innerRadius, endAngle);
        var isLargeArc = Math.Abs(endAngle - startAngle) > 180;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(startOuter, true, true);
            context.ArcTo(endOuter, new System.Windows.Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            context.LineTo(endInner, true, false);
            context.ArcTo(startInner, new System.Windows.Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static System.Windows.Point PointOnCircle(double centerX, double centerY, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new System.Windows.Point(
            centerX + Math.Cos(radians) * radius,
            centerY + Math.Sin(radians) * radius);
    }

    public bool ContainsScreenPoint(double x, double y)
    {
        if (!IsVisible)
        {
            return false;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
        {
            return x >= rect.Left &&
                y >= rect.Top &&
                x <= rect.Right &&
                y <= rect.Bottom;
        }

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var left = Left * transform.M11;
        var top = Top * transform.M22;
        var width = Math.Max(Width, ActualWidth) * transform.M11;
        var height = Math.Max(Height, ActualHeight) * transform.M22;
        return x >= left &&
            y >= top &&
            x <= left + width &&
            y <= top + height;
    }

    public void ResetWheel()
    {
        _isSelecting = false;
        WheelLayer.Visibility = Visibility.Collapsed;
        ReleaseMouseCapture();
        SetSelectedMode(WindowSnapAssistMode.None);
    }

    public void BeginSelectionAtScreenPoint(double screenX, double screenY)
    {
        _isSelecting = true;
        WheelLayer.Visibility = Visibility.Visible;
        UpdateSelectionFromScreenPoint(screenX, screenY);
    }

    public void UpdateSelectionFromScreenPoint(double screenX, double screenY)
    {
        UpdateSelectionFromPoint(PointFromScreen(new System.Windows.Point(screenX, screenY)));
    }

    public WindowSnapAssistMode CompleteSelectionAtScreenPoint(double screenX, double screenY)
    {
        if (!_isSelecting)
        {
            return WindowSnapAssistMode.None;
        }

        UpdateSelectionFromScreenPoint(screenX, screenY);
        var selected = _selectedMode;
        ResetWheel();
        return selected;
    }

    private void CenterButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isSelecting = true;
        _selectedMode = WindowSnapAssistMode.None;
        WheelLayer.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelectionFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    private void CenterButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CenterButton.ContextMenu == null)
        {
            return;
        }

        // WPF ContextMenu opened from a WS_EX_NOACTIVATE window can fail to auto-dismiss when
        // the user clicks outside of the app. Use a transparent full-screen overlay to capture
        // the outside click and close the menu explicitly.
        e.Handled = true;
        ShowMenuDismissOverlay();
        CenterButton.ContextMenu.PlacementTarget = CenterButton;
        CenterButton.ContextMenu.IsOpen = true;
    }

    private void DisableSnapAssist_Click(object sender, RoutedEventArgs e)
    {
        // Hide the overlay immediately before firing the event
        Hide();
        DisableRequested?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        HideMenuDismissOverlay();
    }

    private void ShowMenuDismissOverlay()
    {
        if (CenterButton.ContextMenu == null)
        {
            return;
        }

        if (_menuDismissOverlay == null)
        {
            _menuDismissOverlay = new DismissOverlayWindow(() =>
            {
                if (CenterButton.ContextMenu != null)
                {
                    CenterButton.ContextMenu.IsOpen = false;
                }
            });
        }

        _menuDismissOverlay.ShowForContextMenu(this);
    }

    private void HideMenuDismissOverlay()
    {
        _menuDismissOverlay?.Hide();
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        UpdateSelectionFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        var screenPoint = PointToScreen(e.GetPosition(this));
        var selected = CompleteSelectionAtScreenPoint(screenPoint.X, screenPoint.Y);
        if (selected != WindowSnapAssistMode.None)
        {
            _onSelected(selected);
        }

        e.Handled = true;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var slotIndex = GetCustomSlotIndex(_selectedMode);
        if (slotIndex < 0 || !_customLayouts.ContainsKey(slotIndex))
        {
            return;
        }

        e.Handled = true;
        if (_activationMode == WindowSnapAssistActivationMode.MouseEdge)
        {
            return;
        }

        ShowCustomSlotContextMenu(slotIndex);
    }

    public void UpdateControlKeyForCustomSlotMenu(bool isControlDown)
    {
        if (_activationMode != WindowSnapAssistActivationMode.MouseEdge)
        {
            _wasControlDownForMenu = isControlDown;
            return;
        }

        if (!isControlDown)
        {
            _wasControlDownForMenu = false;
            return;
        }

        if (_wasControlDownForMenu)
        {
            return;
        }

        _wasControlDownForMenu = true;
        var slotIndex = GetCustomSlotIndex(_selectedMode);
        if (slotIndex >= 0 && _customLayouts.ContainsKey(slotIndex))
        {
            ShowCustomSlotContextMenu(slotIndex);
        }
    }

    private void ShowCustomSlotContextMenu(int slotIndex)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        var clearItem = new MenuItem
        {
            Header = $"清除槽位 {slotIndex + 1}"
        };
        clearItem.Click += (_, _) =>
        {
            ClearCustomSlotRequested?.Invoke(slotIndex);
            ResetWheel();
        };
        menu.Items.Add(clearItem);
        menu.IsOpen = true;
    }

    private void UpdateSelectionFromPoint(System.Windows.Point point)
    {
        var dx = point.X - WheelCenter;
        var dy = point.Y - WheelCenter;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < CancelRadius)
        {
            UpdateCustomSlotLayerVisibility(false);
            SetSelectedMode(WindowSnapAssistMode.None);
            return;
        }

        if (distance < RestoreRadius)
        {
            SetSelectedMode(dy < 0 ? WindowSnapAssistMode.Maximize : WindowSnapAssistMode.Restore);
            return;
        }

        if (distance < CenterNeutralRadius)
        {
            UpdateCustomSlotLayerVisibility(false);
            SetSelectedMode(WindowSnapAssistMode.None);
            return;
        }

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (distance >= CustomSlotInnerRadius && distance <= CustomSlotOuterRadius)
        {
            UpdateCustomSlotLayerVisibility(true);
            SetSelectedMode(CustomSlotModeFromAngle(angle));
            return;
        }

        UpdateCustomSlotLayerVisibility(false);

        if (distance > CustomSlotOuterRadius)
        {
            SetSelectedMode(WindowSnapAssistMode.None);
            return;
        }

        SetSelectedMode(angle switch
        {
            >= -22.5 and < 22.5 => WindowSnapAssistMode.RightHalf,
            >= 22.5 and < 67.5 => WindowSnapAssistMode.BottomRight,
            >= 67.5 and < 112.5 => WindowSnapAssistMode.BottomHalf,
            >= 112.5 and < 157.5 => WindowSnapAssistMode.BottomLeft,
            >= 157.5 or < -157.5 => WindowSnapAssistMode.LeftHalf,
            >= -157.5 and < -112.5 => WindowSnapAssistMode.TopLeft,
            >= -112.5 and < -67.5 => WindowSnapAssistMode.TopHalf,
            >= -67.5 and < -22.5 => WindowSnapAssistMode.TopRight,
            _ => WindowSnapAssistMode.None
        });
    }

    private void SetSelectedMode(WindowSnapAssistMode mode)
    {
        if (mode == _selectedMode)
        {
            UpdateSelectionVisual();
            return;
        }

        _selectedMode = mode;
        UpdateSelectionVisual();
        SelectionChanged?.Invoke(_selectedMode);
    }

    private static WindowSnapAssistMode CustomSlotModeFromAngle(double angle)
    {
        var normalized = (angle + 90 + 360) % 360;
        var index = (int)Math.Floor(normalized / (360.0 / CustomSlotCount));
        return (WindowSnapAssistMode)((int)WindowSnapAssistMode.CustomSlot1 + Math.Clamp(index, 0, CustomSlotCount - 1));
    }

    public static int GetCustomSlotIndex(WindowSnapAssistMode mode)
    {
        var index = mode - WindowSnapAssistMode.CustomSlot1;
        return index is >= 0 and < CustomSlotCount ? index : -1;
    }

    private void UpdateSelectionVisual()
    {
        UpdateInnerHalfVisual(InnerMaximizeHalf, _selectedMode == WindowSnapAssistMode.Maximize);
        UpdateInnerHalfVisual(InnerRestoreHalf, _selectedMode == WindowSnapAssistMode.Restore);

        var selectedCustomSlotIndex = GetCustomSlotIndex(_selectedMode);
        foreach (var (index, visual) in _customSlotItems)
        {
            var selected = index == selectedCustomSlotIndex;
            var saved = _customLayouts.TryGetValue(index, out var layout);
            visual.Sector.Fill = System.Windows.Media.Brushes.Black;
            visual.Sector.Stroke = selected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 232, 249))
                : saved
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 103, 232, 249))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(34, 103, 232, 249));
            visual.Sector.StrokeThickness = selected ? 1.4 : 1;
            RenderCustomSlotContent(visual.Content, layout, selected);
        }

        // Update layout items
        foreach (var (mode, item) in _modeItems)
        {
            var selected = mode == _selectedMode;
            item.Background = selected
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 8, 145, 178))
                : System.Windows.Media.Brushes.Black;
            item.BorderBrush = selected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 232, 249))
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 75, 212, 255));

            if (item.Child is Canvas canvas)
            {
                foreach (var child in canvas.Children)
                {
                    if (child is System.Windows.Shapes.Rectangle rect && rect.Fill != null && rect.Fill != System.Windows.Media.Brushes.Transparent)
                    {
                        if (rect.StrokeDashArray == null || rect.StrokeDashArray.Count == 0)
                        {
                            rect.Fill = selected
                                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 103, 232, 249))
                                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(204, 59, 130, 246));
                        }
                    }
                }
            }
        }

        // Update dynamic hint text
        HintText.Text = _selectedMode switch
        {
            WindowSnapAssistMode.Restore => "松开恢复全屏前状态",
            WindowSnapAssistMode.Maximize => "松开全屏",
            WindowSnapAssistMode.TopLeft => "松开 → 左上 1/4",
            WindowSnapAssistMode.TopHalf => "松开 → 上半屏",
            WindowSnapAssistMode.TopRight => "松开 → 右上 1/4",
            WindowSnapAssistMode.RightHalf => "松开 → 右半屏",
            WindowSnapAssistMode.BottomRight => "松开 → 右下 1/4",
            WindowSnapAssistMode.BottomHalf => "松开 → 下半屏",
            WindowSnapAssistMode.BottomLeft => "松开 → 左下 1/4",
            WindowSnapAssistMode.LeftHalf => "松开 → 左半屏",
            _ when selectedCustomSlotIndex >= 0 && _customLayouts.ContainsKey(selectedCustomSlotIndex) => BuildCustomSlotHint(selectedCustomSlotIndex),
            _ when selectedCustomSlotIndex >= 0 => $"松开保存到槽 {selectedCustomSlotIndex + 1}",
            WindowSnapAssistMode.None when _isSelecting => "松开取消",
            _ => "拖动选择布局"
        };
    }

    private string BuildCustomSlotHint(int slotIndex)
    {
        var menuHint = _activationMode == WindowSnapAssistActivationMode.Shortcut
            ? "右键菜单"
            : "按 Ctrl 菜单";
        return $"松开应用记忆槽 {slotIndex + 1} · {menuHint}";
    }

    private void UpdateCustomSlotLayerVisibility(bool isOuterRingActive)
    {
        CustomSlotLayer.Visibility = _customLayouts.Count > 0 || isOuterRingActive
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void UpdateInnerHalfVisual(System.Windows.Shapes.Path path, bool selected)
    {
        path.Stroke = selected
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 232, 249))
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(34, 103, 232, 249));
        path.Fill = selected
            ? System.Windows.Media.Brushes.Black
            : System.Windows.Media.Brushes.Black;
    }

    private static void RenderCustomSlotContent(Canvas canvas, WindowSnapAssistCustomLayoutSettings? layout, bool selected)
    {
        canvas.Children.Clear();
        if (layout == null)
        {
            if (!selected)
            {
                return;
            }

            var horizontal = new System.Windows.Shapes.Rectangle
            {
                Width = 16,
                Height = 2,
                RadiusX = 1,
                RadiusY = 1,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 103, 232, 249))
            };
            var vertical = new System.Windows.Shapes.Rectangle
            {
                Width = 2,
                Height = 16,
                RadiusX = 1,
                RadiusY = 1,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 103, 232, 249))
            };
            Canvas.SetLeft(horizontal, 7);
            Canvas.SetTop(horizontal, 10);
            Canvas.SetLeft(vertical, 14);
            Canvas.SetTop(vertical, 3);
            canvas.Children.Add(horizontal);
            canvas.Children.Add(vertical);
            return;
        }

        var screen = new System.Windows.Shapes.Rectangle
        {
            Width = 30,
            Height = 20,
            RadiusX = 2,
            RadiusY = 2,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(selected ? (byte)245 : (byte)175, 255, 255, 255)),
            StrokeThickness = selected ? 1.4 : 1,
            StrokeDashArray = new DoubleCollection { 2, 2 },
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(selected ? (byte)45 : (byte)24, 255, 255, 255))
        };
        Canvas.SetLeft(screen, 0);
        Canvas.SetTop(screen, 1);
        canvas.Children.Add(screen);

        var left = Math.Clamp(layout.LeftRatio, 0, 1) * 30;
        var top = 1 + Math.Clamp(layout.TopRatio, 0, 1) * 20;
        var right = Math.Clamp(layout.LeftRatio + layout.WidthRatio, 0, 1) * 30;
        var bottom = 1 + Math.Clamp(layout.TopRatio + layout.HeightRatio, 0, 1) * 20;
        var preview = new System.Windows.Shapes.Rectangle
        {
            Width = Math.Max(4, right - left),
            Height = Math.Max(4, bottom - top),
            RadiusX = 1,
            RadiusY = 1,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(selected ? (byte)120 : (byte)82, 103, 232, 249)),
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(selected ? (byte)245 : (byte)210, 103, 232, 249)),
            StrokeThickness = selected ? 1.3 : 1
        };
        Canvas.SetLeft(preview, left);
        Canvas.SetTop(preview, top);
        canvas.Children.Add(preview);
    }

    private void EnsureToolWindowStyle()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var style = GetWindowLongPtr(handle, GwlExstyle);
            SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style.ToInt64() | WsExToolwindow | WsExNoactivate));
        }
        catch
        {
            // Best effort; snap assist remains usable without extended styles.
        }
    }

    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExNoactivate = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed class DismissOverlayWindow : Window
{
    private readonly Action _onDismiss;

    public DismissOverlayWindow(Action onDismiss)
    {
        _onDismiss = onDismiss;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = true;
        Loaded += (_, _) => EnsureToolWindowStyle();
        PreviewMouseDown += (_, e) =>
        {
            e.Handled = true;
            _onDismiss();
        };
        PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            _onDismiss();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _onDismiss();
            }
        };
    }

    public void ShowForContextMenu(Window owner)
    {
        Owner = owner;

        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var width = SystemParameters.VirtualScreenWidth;
        var height = SystemParameters.VirtualScreenHeight;

        Left = left;
        Top = top;
        Width = width;
        Height = height;

        if (!IsVisible)
        {
            Show();
        }
    }

    private void EnsureToolWindowStyle()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var style = GetWindowLongPtr(handle, GwlExstyle);
            SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style.ToInt64() | WsExToolwindow | WsExNoactivate));
        }
        catch
        {
            // Best effort.
        }
    }

    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExNoactivate = 0x08000000L;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

}

internal sealed record CustomSlotVisual(System.Windows.Shapes.Path Sector, Canvas Content);

public enum WindowSnapAssistMode
{
    None,
    TopLeft,
    TopHalf,
    TopRight,
    RightHalf,
    BottomRight,
    BottomHalf,
    BottomLeft,
    LeftHalf,
    Restore,
    Maximize,
    CustomSlot1,
    CustomSlot2,
    CustomSlot3,
    CustomSlot4,
    CustomSlot5,
    CustomSlot6,
    CustomSlot7,
    CustomSlot8,
    CustomSlot9,
    CustomSlot10,
    CustomSlot11,
    CustomSlot12,
    CustomSlot13,
    CustomSlot14,
    CustomSlot15,
    CustomSlot16
}

public enum WindowSnapAssistActivationMode
{
    MouseEdge,
    Shortcut
}
