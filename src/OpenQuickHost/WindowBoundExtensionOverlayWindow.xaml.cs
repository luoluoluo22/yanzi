using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace OpenQuickHost;

public partial class WindowBoundExtensionOverlayWindow : Window
{
    private readonly Action _onExecute;
    private readonly Action<WindowBoundExtensionOverlayWindow> _onContextMenu;
    private readonly Action<WindowBoundExtensionOverlayWindow> _onMoved;
    private readonly Func<IntPtr> _getTargetWindowHandle;
    private System.Windows.Point? _dragStartScreenPoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private bool _dragMoved;

    public WindowBoundExtensionOverlayWindow(
        CommandItem command,
        Func<IntPtr> getTargetWindowHandle,
        Action onExecute,
        Action<WindowBoundExtensionOverlayWindow> onContextMenu,
        Action<WindowBoundExtensionOverlayWindow> onMoved)
    {
        InitializeComponent();
        DataContext = command;
        _onExecute = onExecute;
        _onContextMenu = onContextMenu;
        _onMoved = onMoved;
        _getTargetWindowHandle = getTargetWindowHandle;
        Loaded += (_, _) => EnsureToolWindowStyle();
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dragMoved)
        {
            _dragMoved = false;
            return;
        }

        try
        {
            HostAssets.AppendLog($"Window binding overlay clicked: executing extension.");
            _onExecute();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Window binding overlay click failed: {ex}");
        }
        finally
        {
            var target = _getTargetWindowHandle();
            if (target != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(target);
            }
        }
    }

    private void ActionButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartScreenPoint = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragMoved = false;
        // Don't capture mouse here — let Click event fire normally.
        // Capture will happen on first drag move.
    }

    private void ActionButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragStartScreenPoint == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var deltaX = current.X - _dragStartScreenPoint.Value.X;
        var deltaY = current.Y - _dragStartScreenPoint.Value.Y;
        if (!_dragMoved &&
            Math.Abs(deltaX) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(deltaY) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!_dragMoved)
        {
            _dragMoved = true;
            ActionButton.CaptureMouse();
        }

        Left = _dragStartLeft + deltaX;
        Top = _dragStartTop + deltaY;
        e.Handled = true;
    }

    private void ActionButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStartScreenPoint != null)
        {
            if (_dragMoved)
            {
                ActionButton.ReleaseMouseCapture();
                _onMoved(this);
                e.Handled = true;
            }
            _dragStartScreenPoint = null;
        }
    }

    private void ActionButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _onContextMenu(this);
        e.Handled = true;
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
            // Ignore style failures to keep overlay functional.
        }
    }

    public void SetOverlayTopmost(bool topmost)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                Topmost = topmost;
                return;
            }

            _ = SetWindowPos(handle, topmost ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        catch
        {
            // Best effort; the overlay remains functional even if z-order refresh fails.
        }
    }

    public void SyncOverlayZOrder(IntPtr targetWindow, bool targetIsForeground)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                Topmost = targetIsForeground;
                return;
            }

            if (targetIsForeground)
            {
                _ = SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                return;
            }

            _ = SetWindowPos(handle, HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

            if (targetWindow == IntPtr.Zero)
            {
                return;
            }

            var windowAboveTarget = GetWindow(targetWindow, GwHwndPrev);
            if (windowAboveTarget == handle)
            {
                return;
            }

            var insertAfter = windowAboveTarget == IntPtr.Zero ? HwndTop : windowAboveTarget;
            _ = SetWindowPos(handle, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
        catch
        {
            // Best effort; the overlay will be corrected on the next service tick.
        }
    }

    public void BringOverlayToFront()
    {
        if (!IsVisible || Visibility != Visibility.Visible)
        {
            return;
        }

        SetOverlayTopmost(true);
    }

    public void ShowImmediately()
    {
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }
        Opacity = 1;
    }

    public void HideImmediately()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Visibility = Visibility.Hidden;
    }

    public void AnimateFadeIn()
    {
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }
        Opacity = 0;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    public void AnimateFadeOut(Action? onCompleted = null)
    {
        BeginAnimation(OpacityProperty, null);
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        if (onCompleted != null)
        {
            animation.Completed += (_, _) => onCompleted();
        }
        BeginAnimation(OpacityProperty, animation);
    }

    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExNoactivate = 0x08000000L;
    private static readonly IntPtr HwndTop = new(0);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint GwHwndPrev = 3;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
}
