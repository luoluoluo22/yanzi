using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using static OpenQuickHost.Win32Native;

namespace OpenQuickHost;

public sealed class WindowSnapAssistService : IDisposable
{
    private const int OutsideCornerBandPixels = 72;
    private const int InsideTolerancePixels = 20;
    private const int CircleOutsideOffsetPixels = 30;
    // Keep the layout button away from the resize cursor to reduce accidental clicks.
    private const int MouseEdgeOverlayOffsetPixels = 48;
    private const int MouseEdgeDetectionBandPixels = 24;
    private const int ActiveIntervalMs = 90;
    private const int IdleIntervalMs = 500;
    private const int IdleThreshold = 3; // consecutive same-position ticks before going idle

    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _shortcutSelectionTimer;
    private readonly EventHandler _tickHandler;
    private readonly EventHandler _shortcutSelectionTickHandler;
    private readonly WindowSnapAssistOverlayWindow _overlay;
    private readonly WindowSnapAssistPreviewWindow _previewWindow;
    private IntPtr _targetWindow;
    private POINT _lastCursorPos;
    private int _stationaryCount;
    private uint _shortcutSelectionModifiers;
    private int _shortcutSelectionVirtualKey;

    public WindowSnapAssistService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ActiveIntervalMs)
        };
        _tickHandler = (_, _) => SafeTick();
        _timer.Tick += _tickHandler;
        _shortcutSelectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _shortcutSelectionTickHandler = (_, _) => ShortcutSelectionTick();
        _shortcutSelectionTimer.Tick += _shortcutSelectionTickHandler;
        _overlay = new WindowSnapAssistOverlayWindow(ApplySnapMode);
        _overlay.DisableRequested += OnDisableRequested;
        _overlay.ClearCustomSlotRequested += ClearCustomLayout;
        _overlay.SelectionChanged += OnOverlaySelectionChanged;
        _previewWindow = new WindowSnapAssistPreviewWindow();
        ReloadCustomLayouts();
    }

    /// <summary>
    /// 当用户通过右键菜单禁用时触发，外部可订阅此事件来持久化设置
    /// </summary>
    public event Action? DisabledByUser;

    private void OnDisableRequested()
    {
        Stop();
        DisabledByUser?.Invoke();
    }

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _shortcutSelectionTimer.Stop();
        HideOverlay(force: true);
    }

    public void ReloadCustomLayouts()
    {
        _overlay.SetCustomLayouts(AppSettingsStore.Load().WindowSnapAssistCustomLayouts);
    }

    /// <summary>
    /// 通过快捷键触发：在前台窗口位置显示排列轮盘
    /// </summary>
    public void TriggerAtForegroundWindow()
    {
        var hwnd = GetForegroundWindow();
        if (!IsValidTargetWindow(hwnd) || !TryGetVisibleWindowRect(hwnd, out var rect))
        {
            return;
        }

        _targetWindow = hwnd;
        UpdateOverlayIcon(_targetWindow);
        // Show at center of the window
        var centerX = (rect.Left + rect.Right) / 2.0;
        var centerY = (rect.Top + rect.Bottom) / 2.0;
        ShowOverlay(centerX, centerY, WindowSnapAssistActivationMode.Shortcut);
    }

    public void BeginShortcutSelectionAtMouse(uint modifiers, int virtualKey)
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        var hwnd = ResolveTargetWindowFromPoint(point);
        if (!IsValidTargetWindow(hwnd))
        {
            return;
        }

        _shortcutSelectionModifiers = modifiers;
        _shortcutSelectionVirtualKey = virtualKey;
        _targetWindow = hwnd;
        UpdateOverlayIcon(_targetWindow);
        ShowOverlay(point.X, point.Y, WindowSnapAssistActivationMode.Shortcut);
        _overlay.BeginSelectionAtScreenPoint(point.X, point.Y);

        if (!_shortcutSelectionTimer.IsEnabled)
        {
            _shortcutSelectionTimer.Start();
        }
    }

    public bool BeginMouseDragSelectionAtMouse()
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        var hwnd = ResolveTargetWindowFromPoint(point);
        if (!IsValidTargetWindow(hwnd))
        {
            hwnd = GetForegroundWindow();
        }

        if (!IsValidTargetWindow(hwnd))
        {
            return false;
        }

        _targetWindow = hwnd;
        UpdateOverlayIcon(_targetWindow);
        ShowOverlay(point.X, point.Y, WindowSnapAssistActivationMode.Shortcut);
        _overlay.BeginSelectionAtScreenPoint(point.X, point.Y);
        return true;
    }

    public void UpdateMouseDragSelectionAtMouse()
    {
        if (_overlay.IsSelecting && GetCursorPos(out var point))
        {
            _overlay.UpdateSelectionFromScreenPoint(point.X, point.Y);
        }
    }

    public void CompleteMouseDragSelectionAtMouse()
    {
        if (!_overlay.IsSelecting || !GetCursorPos(out var point))
        {
            HideOverlay(force: true);
            return;
        }

        var selected = _overlay.CompleteSelectionAtScreenPoint(point.X, point.Y);
        if (selected == WindowSnapAssistMode.None)
        {
            HideOverlay(force: true);
            return;
        }

        ApplySnapMode(selected);
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= _tickHandler;
        _shortcutSelectionTimer.Tick -= _shortcutSelectionTickHandler;
        _overlay.ClearCustomSlotRequested -= ClearCustomLayout;
        _overlay.SelectionChanged -= OnOverlaySelectionChanged;
        _overlay.Close();
        _previewWindow.Close();
    }

    private void ShortcutSelectionTick()
    {
        if (!_overlay.IsSelecting || !GetCursorPos(out var point))
        {
            _shortcutSelectionTimer.Stop();
            HideOverlay(force: true);
            return;
        }

        _overlay.UpdateSelectionFromScreenPoint(point.X, point.Y);
        if (IsShortcutSelectionKeyDown())
        {
            return;
        }

        _shortcutSelectionTimer.Stop();
        var selected = _overlay.CompleteSelectionAtScreenPoint(point.X, point.Y);
        if (selected == WindowSnapAssistMode.None)
        {
            HideOverlay(force: true);
            return;
        }

        ApplySnapMode(selected);
    }

    private static IntPtr ResolveTargetWindowFromPoint(POINT point)
    {
        var hwnd = WindowFromPoint(point);
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GaRoot);
    }

    private void ClearCustomLayout(int slotIndex)
    {
        var settings = AppSettingsStore.Load();
        var removed = settings.WindowSnapAssistCustomLayouts.RemoveAll(slot => slot.SlotIndex == slotIndex);
        if (removed == 0)
        {
            return;
        }

        AppSettingsStore.Save(settings);
        ReloadCustomLayouts();
        _previewWindow.Hide();
        HostAssets.AppendLog($"Window snap assist custom layout cleared: slot={slotIndex + 1}.");
    }

    private void OnOverlaySelectionChanged(WindowSnapAssistMode mode)
    {
        var customSlotIndex = WindowSnapAssistOverlayWindow.GetCustomSlotIndex(mode);
        if (customSlotIndex < 0 || !TryResolveCustomLayoutTarget(customSlotIndex, out var target))
        {
            _previewWindow.Hide();
            return;
        }

        _previewWindow.ShowPreview(
            target.Left,
            target.Top,
            Math.Max(1, target.Right - target.Left),
            Math.Max(1, target.Bottom - target.Top));
    }

    private bool TryResolveCustomLayoutTarget(int slotIndex, out RECT target)
    {
        target = default;
        if (_targetWindow == IntPtr.Zero || !TryGetMonitorWorkArea(_targetWindow, out var workArea))
        {
            return false;
        }

        var layout = AppSettingsStore.Load().WindowSnapAssistCustomLayouts.FirstOrDefault(slot => slot.SlotIndex == slotIndex);
        if (layout == null)
        {
            return false;
        }

        target = CreateTargetRect(layout, workArea);
        return target.Right > target.Left && target.Bottom > target.Top;
    }

    private bool IsShortcutSelectionKeyDown()
    {
        return IsVirtualKeyDown(_shortcutSelectionVirtualKey) &&
            ((_shortcutSelectionModifiers & ModControl) == 0 || IsVirtualKeyDown(VkControl)) &&
            ((_shortcutSelectionModifiers & ModAlt) == 0 || IsVirtualKeyDown(VkMenu)) &&
            ((_shortcutSelectionModifiers & ModShift) == 0 || IsVirtualKeyDown(VkShift)) &&
            ((_shortcutSelectionModifiers & ModWin) == 0 || IsVirtualKeyDown(VkLwin) || IsVirtualKeyDown(VkRwin));
    }

    private void SafeTick()
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Window snap assist tick failed: {ex}");
            HideOverlay();
        }
    }

    private void Tick()
    {
        if (_overlay.IsVisible)
        {
            _overlay.UpdateControlKeyForCustomSlotMenu(IsVirtualKeyDown(VkControl));
        }

        if (!GetCursorPos(out var point))
        {
            HideOverlay();
            return;
        }

        // Adaptive polling: slow down when cursor is stationary
        if (point.X == _lastCursorPos.X && point.Y == _lastCursorPos.Y)
        {
            _stationaryCount++;
            if (_stationaryCount >= IdleThreshold && _timer.Interval.TotalMilliseconds < IdleIntervalMs)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(IdleIntervalMs);
            }
        }
        else
        {
            _stationaryCount = 0;
            if (_timer.Interval.TotalMilliseconds > ActiveIntervalMs)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(ActiveIntervalMs);
            }
        }
        _lastCursorPos = point;

        if (_overlay.IsSelecting || _overlay.ContainsScreenPoint(point.X, point.Y))
        {
            return;
        }

        // Detect if cursor is a resize cursor (any edge/corner resize)
        var cursorInfo = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        var isResizeCursor = false;
        IntPtr targetFromCursor = IntPtr.Zero;

        if (GetCursorInfo(ref cursorInfo) && cursorInfo.hCursor != IntPtr.Zero)
        {
            isResizeCursor = IsResizeCursorHandle(cursorInfo.hCursor);
        }

        // Strategy 1: Resize cursor detected - find the window under cursor
        if (isResizeCursor)
        {
            var hwndUnder = WindowFromPoint(point);
            if (hwndUnder != IntPtr.Zero)
            {
                targetFromCursor = GetAncestor(hwndUnder, GaRoot);
            }
        }

        // Strategy 2: Corner proximity (original + background window support)
        var hwnd = GetForegroundWindow();
        if (TryMatchWindow(hwnd, point, out var anchorX, out var anchorY))
        {
            _targetWindow = hwnd;
            UpdateOverlayIcon(_targetWindow);
            ShowOverlay(anchorX, anchorY, WindowSnapAssistActivationMode.MouseEdge);
            return;
        }

        // Try background window under cursor
        var hwndBg = WindowFromPoint(point);
        if (hwndBg != IntPtr.Zero)
        {
            hwndBg = GetAncestor(hwndBg, GaRoot);
            if (hwndBg != hwnd && TryMatchWindow(hwndBg, point, out anchorX, out anchorY))
            {
                _targetWindow = hwndBg;
                UpdateOverlayIcon(_targetWindow);
                ShowOverlay(anchorX, anchorY, WindowSnapAssistActivationMode.MouseEdge);
                return;
            }
        }

        // Strategy 3: Resize cursor active on a valid window — only if cursor is on the window border
        if (isResizeCursor && targetFromCursor != IntPtr.Zero && IsValidTargetWindow(targetFromCursor))
        {
            if (GetWindowRect(targetFromCursor, out var targetRect) && IsCursorOnWindowBorder(point, targetRect))
            {
                _targetWindow = targetFromCursor;
                UpdateOverlayIcon(_targetWindow);
                ShowOverlay(point.X, point.Y, WindowSnapAssistActivationMode.MouseEdge);
                return;
            }
        }

        HideOverlay();
    }

    /// <summary>
    /// 检查光标是否在窗口边框区域（距离窗口边缘 8px 以内），
    /// 排除在窗口内部（如网页中调整图片大小）的误触发
    /// </summary>
    private static bool IsCursorOnWindowBorder(POINT point, RECT rect)
    {
        const int borderThickness = 8;

        // Must be within or very near the window rect
        if (point.X < rect.Left - borderThickness || point.X > rect.Right + borderThickness ||
            point.Y < rect.Top - borderThickness || point.Y > rect.Bottom + borderThickness)
        {
            return false;
        }

        // Check if cursor is within the border band (not deep inside the window)
        var nearLeftEdge   = point.X >= rect.Left - borderThickness && point.X <= rect.Left + borderThickness;
        var nearRightEdge  = point.X >= rect.Right - borderThickness && point.X <= rect.Right + borderThickness;
        var nearTopEdge    = point.Y >= rect.Top - borderThickness && point.Y <= rect.Top + borderThickness;
        var nearBottomEdge = point.Y >= rect.Bottom - borderThickness && point.Y <= rect.Bottom + borderThickness;

        return nearLeftEdge || nearRightEdge || nearTopEdge || nearBottomEdge;
    }

    private void UpdateOverlayIcon(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            _overlay.SetTargetIcon(null);
            return;
        }

        try
        {
            // Get the window's icon (small icon first, then large)
            var icon = SendMessageHungSafe(hwnd, WmGeticon, (IntPtr)IconSmall2, IntPtr.Zero);
            if (icon == IntPtr.Zero)
                icon = SendMessageHungSafe(hwnd, WmGeticon, (IntPtr)IconSmall, IntPtr.Zero);
            if (icon == IntPtr.Zero)
                icon = SendMessageHungSafe(hwnd, WmGeticon, (IntPtr)IconBig, IntPtr.Zero);
            if (icon == IntPtr.Zero)
                icon = GetClassLongPtr(hwnd, GclHiconSm);
            if (icon == IntPtr.Zero)
                icon = GetClassLongPtr(hwnd, GclHicon);

            if (icon != IntPtr.Zero)
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                _overlay.SetTargetIcon(source);
            }
            else
            {
                _overlay.SetTargetIcon(null);
            }
        }
        catch
        {
            _overlay.SetTargetIcon(null);
        }
    }

    private static bool IsResizeCursorHandle(IntPtr hCursor)
    {
        // Compare against standard resize cursors
        var sizeNWSE = LoadCursor(IntPtr.Zero, IdcSizenwse);
        var sizeNESW = LoadCursor(IntPtr.Zero, IdcSizenesw);
        var sizeWE   = LoadCursor(IntPtr.Zero, IdcSizewe);
        var sizeNS   = LoadCursor(IntPtr.Zero, IdcSizens);
        var sizeAll  = LoadCursor(IntPtr.Zero, IdcSizeall);

        return hCursor == sizeNWSE || hCursor == sizeNESW ||
               hCursor == sizeWE || hCursor == sizeNS || hCursor == sizeAll;
    }

    private bool TryMatchWindow(IntPtr hwnd, POINT point, out double anchorX, out double anchorY)
    {
        anchorX = 0;
        anchorY = 0;
        if (!IsValidTargetWindow(hwnd) || !GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return TryResolveCorner(point, rect, out anchorX, out anchorY);
    }

    private void ShowOverlay(double anchorX, double anchorY, WindowSnapAssistActivationMode activationMode)
    {
        ReloadCustomLayouts();
        _overlay.SetActivationMode(activationMode);
        var anchorPoint = new POINT((int)Math.Round(anchorX), (int)Math.Round(anchorY));
        var scale = TryGetMonitorInfoForPoint(anchorPoint, out var monitorInfo)
            ? GetDpiScaleForMonitor(MonitorFromPoint(anchorPoint, MonitorDefaulttonearest))
            : new DpiScale(1, 1);
        var overlayWidth = _overlay.ActualWidth > 1 ? _overlay.ActualWidth : _overlay.Width;
        var overlayHeight = _overlay.ActualHeight > 1 ? _overlay.ActualHeight : _overlay.Height;
        var left = (anchorX / scale.X) - (overlayWidth / 2);
        var top = (anchorY / scale.Y) - (overlayHeight / 2);

        if (activationMode == WindowSnapAssistActivationMode.MouseEdge &&
            GetCursorPos(out var cursorPoint) &&
            TryGetVisibleWindowRect(_targetWindow, out var targetRect))
        {
            var (offsetXDips, offsetYDips) = ComputeMouseEdgeOverlayOffset(cursorPoint, targetRect, scale);
            left += offsetXDips;
            top += offsetYDips;
        }

        if (monitorInfo.cbSize > 0)
        {
            var workLeft = monitorInfo.rcWork.Left / scale.X;
            var workTop = monitorInfo.rcWork.Top / scale.Y;
            var workRight = monitorInfo.rcWork.Right / scale.X;
            var workBottom = monitorInfo.rcWork.Bottom / scale.Y;
            _overlay.Left = Math.Clamp(left, workLeft, Math.Max(workLeft, workRight - overlayWidth));
            _overlay.Top = Math.Clamp(top, workTop, Math.Max(workTop, workBottom - overlayHeight));
        }
        else
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
            _overlay.Left = Math.Clamp(left, virtualLeft, Math.Max(virtualLeft, virtualRight - overlayWidth));
            _overlay.Top = Math.Clamp(top, virtualTop, Math.Max(virtualTop, virtualBottom - overlayHeight));
        }

        if (!_overlay.IsVisible)
        {
            _overlay.Show();
        }
    }

    private static (double offsetXDips, double offsetYDips) ComputeMouseEdgeOverlayOffset(
        POINT cursorPoint,
        RECT targetRect,
        DpiScale scale)
    {
        var nearLeftEdge = Math.Abs(cursorPoint.X - targetRect.Left) <= MouseEdgeDetectionBandPixels;
        var nearRightEdge = Math.Abs(cursorPoint.X - targetRect.Right) <= MouseEdgeDetectionBandPixels;
        var nearTopEdge = Math.Abs(cursorPoint.Y - targetRect.Top) <= MouseEdgeDetectionBandPixels;
        var nearBottomEdge = Math.Abs(cursorPoint.Y - targetRect.Bottom) <= MouseEdgeDetectionBandPixels;

        var offsetX = 0.0;
        var offsetY = 0.0;

        if (nearLeftEdge)
        {
            offsetX -= MouseEdgeOverlayOffsetPixels / scale.X;
        }
        else if (nearRightEdge)
        {
            offsetX += MouseEdgeOverlayOffsetPixels / scale.X;
        }

        if (nearTopEdge)
        {
            offsetY -= MouseEdgeOverlayOffsetPixels / scale.Y;
        }
        else if (nearBottomEdge)
        {
            offsetY += MouseEdgeOverlayOffsetPixels / scale.Y;
        }

        return (offsetX, offsetY);
    }

    private void HideOverlay(bool force = false)
    {
        if (_overlay.IsSelecting && !force)
        {
            return;
        }

        _targetWindow = IntPtr.Zero;
        _previewWindow.Hide();
        _overlay.ResetWheel();
        if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
    }

    private void ApplySnapMode(WindowSnapAssistMode mode)
    {
        if (_targetWindow == IntPtr.Zero || mode == WindowSnapAssistMode.None)
        {
            return;
        }

        try
        {
            var customSlotIndex = WindowSnapAssistOverlayWindow.GetCustomSlotIndex(mode);
            if (customSlotIndex >= 0)
            {
                ApplyOrSaveCustomLayout(customSlotIndex);
                return;
            }

            // Special modes: Restore and Maximize
            if (mode == WindowSnapAssistMode.Restore)
            {
                _ = ShowWindow(_targetWindow, SwRestore);
                HostAssets.AppendLog($"Window snap assist: restored window hwnd=0x{_targetWindow.ToInt64():X}.");
                return;
            }

            if (mode == WindowSnapAssistMode.Maximize)
            {
                _ = ShowWindow(_targetWindow, SwMaximize);
                HostAssets.AppendLog($"Window snap assist: maximized window hwnd=0x{_targetWindow.ToInt64():X}.");
                return;
            }

        if (!TryGetMonitorWorkArea(_targetWindow, out var workArea))
            {
                return;
            }

            var width = workArea.Right - workArea.Left;
            var height = workArea.Bottom - workArea.Top;
            var halfWidth = width / 2;
            var halfHeight = height / 2;

            var target = mode switch
            {
                WindowSnapAssistMode.TopLeft => new RECT(workArea.Left, workArea.Top, workArea.Left + halfWidth, workArea.Top + halfHeight),
                WindowSnapAssistMode.TopHalf => new RECT(workArea.Left, workArea.Top, workArea.Right, workArea.Top + halfHeight),
                WindowSnapAssistMode.TopRight => new RECT(workArea.Left + halfWidth, workArea.Top, workArea.Right, workArea.Top + halfHeight),
                WindowSnapAssistMode.BottomLeft => new RECT(workArea.Left, workArea.Top + halfHeight, workArea.Left + halfWidth, workArea.Bottom),
                WindowSnapAssistMode.BottomRight => new RECT(workArea.Left + halfWidth, workArea.Top + halfHeight, workArea.Right, workArea.Bottom),
                WindowSnapAssistMode.BottomHalf => new RECT(workArea.Left, workArea.Top + halfHeight, workArea.Right, workArea.Bottom),
                WindowSnapAssistMode.LeftHalf => new RECT(workArea.Left, workArea.Top, workArea.Left + halfWidth, workArea.Bottom),
                WindowSnapAssistMode.RightHalf => new RECT(workArea.Left + halfWidth, workArea.Top, workArea.Right, workArea.Bottom),
                _ => default
            };

            if (target.Right <= target.Left || target.Bottom <= target.Top)
            {
                return;
            }

            ApplyVisibleTargetRect(target);
            HostAssets.AppendLog($"Window snap assist applied: hwnd=0x{_targetWindow.ToInt64():X}, mode={mode}, rect=({target.Left},{target.Top},{target.Right},{target.Bottom}).");
        }
        finally
        {
            HideOverlay();
        }
    }

    private void ApplyOrSaveCustomLayout(int slotIndex)
    {
        if (!TryGetMonitorWorkArea(_targetWindow, out var workArea) ||
            !TryGetVisibleWindowRect(_targetWindow, out var currentRect))
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.WindowSnapAssistCustomLayouts ??= [];
        var existing = settings.WindowSnapAssistCustomLayouts.FirstOrDefault(slot => slot.SlotIndex == slotIndex);
        if (existing == null)
        {
            var width = Math.Max(1, workArea.Right - workArea.Left);
            var height = Math.Max(1, workArea.Bottom - workArea.Top);
            settings.WindowSnapAssistCustomLayouts.Add(new WindowSnapAssistCustomLayoutSettings
            {
                SlotIndex = slotIndex,
                LeftRatio = (currentRect.Left - workArea.Left) / (double)width,
                TopRatio = (currentRect.Top - workArea.Top) / (double)height,
                WidthRatio = (currentRect.Right - currentRect.Left) / (double)width,
                HeightRatio = (currentRect.Bottom - currentRect.Top) / (double)height
            });
            AppSettingsStore.Save(settings);
            ReloadCustomLayouts();
            HostAssets.AppendLog($"Window snap assist custom layout saved: slot={slotIndex + 1}, hwnd=0x{_targetWindow.ToInt64():X}.");
            return;
        }

        var target = CreateTargetRect(existing, workArea);

        if (target.Right <= target.Left || target.Bottom <= target.Top)
        {
            return;
        }

        ApplyVisibleTargetRect(target);
        HostAssets.AppendLog($"Window snap assist custom layout applied: slot={slotIndex + 1}, hwnd=0x{_targetWindow.ToInt64():X}, rect=({target.Left},{target.Top},{target.Right},{target.Bottom}).");
    }

    private void ApplyVisibleTargetRect(RECT visibleTarget)
    {
        _ = ShowWindow(_targetWindow, SwRestore);
        var target = GetOuterRectForVisibleTarget(_targetWindow, visibleTarget);
        _ = SetWindowPos(
            _targetWindow,
            IntPtr.Zero,
            target.Left,
            target.Top,
            target.Right - target.Left,
            target.Bottom - target.Top,
            SwpNozorder | SwpNoactivate);
    }

    private static RECT GetOuterRectForVisibleTarget(IntPtr hwnd, RECT visibleTarget)
    {
        if (!GetWindowRect(hwnd, out var outerRect) ||
            !TryGetVisibleWindowRect(hwnd, out var visibleRect))
        {
            return visibleTarget;
        }

        return new RECT(
            visibleTarget.Left + (outerRect.Left - visibleRect.Left),
            visibleTarget.Top + (outerRect.Top - visibleRect.Top),
            visibleTarget.Right + (outerRect.Right - visibleRect.Right),
            visibleTarget.Bottom + (outerRect.Bottom - visibleRect.Bottom));
    }

    private static RECT CreateTargetRect(WindowSnapAssistCustomLayoutSettings layout, RECT workArea)
    {
        var workWidth = workArea.Right - workArea.Left;
        var workHeight = workArea.Bottom - workArea.Top;
        return new RECT(
            workArea.Left + (int)Math.Round(layout.LeftRatio * workWidth),
            workArea.Top + (int)Math.Round(layout.TopRatio * workHeight),
            workArea.Left + (int)Math.Round((layout.LeftRatio + layout.WidthRatio) * workWidth),
            workArea.Top + (int)Math.Round((layout.TopRatio + layout.HeightRatio) * workHeight));
    }

    /// <summary>
    /// 检测鼠标是否在窗口角落附近，智能处理贴边情况：
    /// - 正常情况：图标显示在角落外侧
    /// - 贴边情况：图标显示在有空间的一侧（内侧或偏移方向）
    /// </summary>
    private static bool TryResolveCorner(POINT point, RECT rect, out double anchorX, out double anchorY)
    {
        anchorX = 0;
        anchorY = 0;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 120 || height <= 120)
        {
            return false;
        }

        // Use the physical monitor bounds because GetCursorPos/GetWindowRect are physical pixels.
        var monitorRect = TryGetMonitorInfoForRect(rect, out var monitorInfo)
            ? monitorInfo.rcMonitor
            : rect;
        var screenLeft = monitorRect.Left;
        var screenTop = monitorRect.Top;
        var screenRight = monitorRect.Right;
        var screenBottom = monitorRect.Bottom;

        // Detect if window edges are stuck to screen edges (within 4px tolerance)
        var stuckLeft = rect.Left <= screenLeft + 4;
        var stuckRight = rect.Right >= screenRight - 4;
        var stuckTop = rect.Top <= screenTop + 4;
        var stuckBottom = rect.Bottom >= screenBottom - 4;

        var nearLeft = point.X >= rect.Left - OutsideCornerBandPixels && point.X <= rect.Left + InsideTolerancePixels;
        var nearRight = point.X <= rect.Right + OutsideCornerBandPixels && point.X >= rect.Right - InsideTolerancePixels;
        var nearTop = point.Y >= rect.Top - OutsideCornerBandPixels && point.Y <= rect.Top + InsideTolerancePixels;
        var nearBottom = point.Y <= rect.Bottom + OutsideCornerBandPixels && point.Y >= rect.Bottom - InsideTolerancePixels;

        // For stuck edges, extend the inside tolerance to allow detection from inside
        if (stuckLeft)
            nearLeft = point.X >= rect.Left && point.X <= rect.Left + OutsideCornerBandPixels;
        if (stuckRight)
            nearRight = point.X >= rect.Right - OutsideCornerBandPixels && point.X <= rect.Right;
        if (stuckTop)
            nearTop = point.Y >= rect.Top && point.Y <= rect.Top + OutsideCornerBandPixels;
        if (stuckBottom)
            nearBottom = point.Y >= rect.Bottom - OutsideCornerBandPixels && point.Y <= rect.Bottom;

        if (nearLeft && nearTop)
        {
            anchorX = stuckLeft ? rect.Left + CircleOutsideOffsetPixels : rect.Left - CircleOutsideOffsetPixels;
            anchorY = stuckTop ? rect.Top + CircleOutsideOffsetPixels : rect.Top - CircleOutsideOffsetPixels;
            return true;
        }

        if (nearRight && nearTop)
        {
            anchorX = stuckRight ? rect.Right - CircleOutsideOffsetPixels : rect.Right + CircleOutsideOffsetPixels;
            anchorY = stuckTop ? rect.Top + CircleOutsideOffsetPixels : rect.Top - CircleOutsideOffsetPixels;
            return true;
        }

        if (nearLeft && nearBottom)
        {
            anchorX = stuckLeft ? rect.Left + CircleOutsideOffsetPixels : rect.Left - CircleOutsideOffsetPixels;
            anchorY = stuckBottom ? rect.Bottom - CircleOutsideOffsetPixels : rect.Bottom + CircleOutsideOffsetPixels;
            return true;
        }

        if (nearRight && nearBottom)
        {
            anchorX = stuckRight ? rect.Right - CircleOutsideOffsetPixels : rect.Right + CircleOutsideOffsetPixels;
            anchorY = stuckBottom ? rect.Bottom - CircleOutsideOffsetPixels : rect.Bottom + CircleOutsideOffsetPixels;
            return true;
        }

        return false;
    }

    // Keep old method for reference but unused
    private static bool TryResolveOutsideCorner(POINT point, RECT rect, out double anchorX, out double anchorY)
    {
        return TryResolveCorner(point, rect, out anchorX, out anchorY);
    }

    private static bool IsValidTargetWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return false;
        }

        if (pid == (uint)Environment.ProcessId)
        {
            return IsOwnProcessSnapTargetWindow(hwnd);
        }

        try
        {
            var process = Process.GetProcessById((int)pid);
            if (string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                // Allow explorer windows that have a title (file explorer windows)
                // but exclude desktop and taskbar (no title or shell windows)
                var titleLength = GetWindowTextLength(hwnd);
                if (titleLength <= 0)
                {
                    return false;
                }

                // Exclude the desktop window (Program Manager / Progman)
                var className = new char[64];
                var classLen = GetClassName(hwnd, className, className.Length);
                var classStr = new string(className, 0, classLen);
                if (string.Equals(classStr, "Progman", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(classStr, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        return (style & WsCaption) == WsCaption;
    }

    private static bool IsOwnProcessSnapTargetWindow(IntPtr hwnd)
    {
        var exStyle = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        if ((exStyle & (WsExToolwindow | WsExNoactivate | WsExTransparent)) != 0)
        {
            return false;
        }

        var title = GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title) || IsSnapAssistAuxiliaryTitle(title))
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var rect) ||
            rect.Right - rect.Left < 120 ||
            rect.Bottom - rect.Top < 120)
        {
            return false;
        }

        return true;
    }

    private static bool IsSnapAssistAuxiliaryTitle(string title)
    {
        return title.Equals("窗口排列", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("拖拽绑定窗口", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("窗口绑定扩展", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("燕环", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("手机消息", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetMonitorWorkArea(IntPtr hwnd, out RECT workArea)
    {
        workArea = default;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaulttonearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        workArea = info.rcWork;
        return true;
    }

    private static bool TryGetMonitorInfoForPoint(POINT point, out MONITORINFO info)
    {
        info = default;
        var monitor = MonitorFromPoint(point, MonitorDefaulttonearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        info = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };
        return GetMonitorInfo(monitor, ref info);
    }

    private static bool TryGetMonitorInfoForRect(RECT rect, out MONITORINFO info)
    {
        var center = new POINT(
            rect.Left + ((rect.Right - rect.Left) / 2),
            rect.Top + ((rect.Bottom - rect.Top) / 2));
        return TryGetMonitorInfoForPoint(center, out info);
    }

    private static DpiScale GetDpiScaleForRect(RECT rect)
    {
        var center = new POINT(
            rect.Left + ((rect.Right - rect.Left) / 2),
            rect.Top + ((rect.Bottom - rect.Top) / 2));
        return GetDpiScaleForMonitor(MonitorFromPoint(center, MonitorDefaulttonearest));
    }

    private static DpiScale GetDpiScaleForMonitor(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return new DpiScale(1, 1);
        }

        try
        {
            if (GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out var dpiX, out var dpiY) == 0)
            {
                return new DpiScale(
                    Math.Clamp(dpiX / 96.0, 0.5, 4.0),
                    Math.Clamp(dpiY / 96.0, 0.5, 4.0));
            }
        }
        catch
        {
            // Fall back to 100% scaling when monitor DPI cannot be queried.
        }

        return new DpiScale(1, 1);
    }

    private static bool TryGetVisibleWindowRect(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        try
        {
            if (DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<RECT>()) == 0 &&
                rect.Right > rect.Left &&
                rect.Bottom > rect.Top)
            {
                return true;
            }
        }
        catch
        {
            // Fall back to GetWindowRect when DWM bounds are unavailable.
        }

        return GetWindowRect(hwnd, out rect);
    }

    private sealed class WindowSnapAssistPreviewWindow : Window
    {
        private readonly System.Windows.Controls.Border _border;

        public WindowSnapAssistPreviewWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            _border = new System.Windows.Controls.Border
            {
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(245, 103, 232, 249)),
                BorderThickness = new Thickness(3),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(58, 103, 232, 249)),
                CornerRadius = new CornerRadius(10)
            };
            Content = _border;
            Loaded += (_, _) => EnsurePreviewStyle();
        }

        public void ShowPreview(int left, int top, int width, int height)
        {
            var rect = new RECT(left, top, left + width, top + height);
            var scale = GetDpiScaleForRect(rect);
            Left = left / scale.X;
            Top = top / scale.Y;
            Width = Math.Max(1, width / scale.X);
            Height = Math.Max(1, height / scale.Y);

            if (!IsVisible)
            {
                Show();
            }

            EnsurePreviewStyle();
        }

        private void EnsurePreviewStyle()
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var style = GetWindowLongPtr(handle, GwlExstyle);
                SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style.ToInt64() | WsExToolwindow | WsExNoactivate | WsExTransparent));
            }
            catch
            {
                // Best effort; the preview remains visual even without extended styles.
            }
        }

        private const int GwlExstyle = -20;
        private const long WsExToolwindow = 0x00000080L;
        private const long WsExTransparent = 0x00000020L;
        private const long WsExNoactivate = 0x08000000L;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private readonly record struct DpiScale(double X, double Y);

    private enum MonitorDpiType
    {
        EffectiveDpi = 0
    }

    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint MonitorDefaulttonearest = 0x00000002;
    private const int GwlStyle = -16;
    private const int GwlExstyle = -20;
    private const long WsCaption = 0x00C00000L;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExNoactivate = 0x08000000L;
    private const uint GaRoot = 2;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    // Icon constants
    private const int WmGeticon = 0x007F;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int GclHicon = -14;
    private const int GclHiconSm = -34;

    // Cursor constants
    private const int IdcSizenwse = 32642;
    private const int IdcSizenesw = 32643;
    private const int IdcSizewe   = 32644;
    private const int IdcSizens   = 32645;
    private const int IdcSizeall  = 32646;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private static bool IsVirtualKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    private const uint SmtoAbortIfHung = 0x0002;

    // 带超时的跨线程消息：目标进程挂起（未响应窗口很常见）时 SendMessage 会无限期阻塞
    // 本应用 UI 线程，导致整个启动器跟着“未响应”
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private static IntPtr SendMessageHungSafe(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (SendMessageTimeout(hWnd, msg, wParam, lParam, SmtoAbortIfHung, 200, out var result) == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            return result;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);
}
