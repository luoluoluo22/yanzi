using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace OpenQuickHost;

public enum OverlayPositionMode
{
    /// <summary>以光标为中心居中定位（如燕环）</summary>
    CenterAroundCursor,
    /// <summary>以光标右侧/右下侧展开（如随身背包、菜单浮窗）</summary>
    TrailingCursor,
    /// <summary>在当前屏幕中央居中（如居中对话框、看板提示）</summary>
    CenterOnScreen,
    /// <summary>全屏覆盖当前屏幕工作区（如全屏手势捕捉、全屏编辑模式、燕幕）</summary>
    CoverScreenWorkArea
}

/// <summary>
/// 统一浮窗管理底座 (Overlay Window Manager)
/// 统一负责：
/// 1. 浮窗安全离屏休眠归位（彻底消除 DWM 显存残影）；
/// 2. 多屏幕与 Per-Monitor DPI 精准适配与工作区防溢出贴边；
/// 3. 全局浮窗互斥调度与冲突熔断（如背包/轮盘呼出时自动压制手势识别画线，避免网状耦合）。
/// </summary>
public static class OverlayWindowManager
{
    /// <summary>
    /// 离屏安全停靠坐标（-32000，彻底打碎 DWM 显存残影）
    /// </summary>
    public const double OffScreenCoordinate = -32000;

    private static readonly List<Action> _suppressionHandlers = [];
    private static readonly object _syncLock = new();

    /// <summary>
    /// 注册互斥抑制处理器（当任何浮窗被唤出时，自动回调此处理器进行清场熔断，如手势系统清空画线）
    /// </summary>
    public static void RegisterSuppressionHandler(Action handler)
    {
        if (handler == null) return;
        lock (_syncLock)
        {
            if (!_suppressionHandlers.Contains(handler))
            {
                _suppressionHandlers.Add(handler);
            }
        }
    }

    /// <summary>
    /// 触发全局浮窗互斥熔断，通知并关闭所有冲突手势和临时遮罩
    /// </summary>
    public static void SuppressConflictingOverlays()
    {
        List<Action> snapshot;
        lock (_syncLock)
        {
            snapshot = [.. _suppressionHandlers];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler.Invoke();
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[OverlayWindowManager] Suppression handler error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 为指定的 WPF 窗口应用无焦点与工具窗样式（WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW）
    /// 确保呼出时不抢占用户正在输入的前台窗口焦点
    /// </summary>
    public static void ApplyNoActivateToolWindowStyle(this Window window)
    {
        if (window == null) return;
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var style = Win32Native.GetWindowLongPtr(handle, Win32Native.GWL_EXSTYLE);
            Win32Native.SetWindowLongPtr(
                handle,
                Win32Native.GWL_EXSTYLE,
                new IntPtr(style.ToInt64() | Win32Native.WS_EX_TOOLWINDOW | Win32Native.WS_EX_NOACTIVATE));
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[OverlayWindowManager] ApplyNoActivateToolWindowStyle error: {ex.Message}");
        }
    }

    /// <summary>
    /// 安全离屏隐藏浮窗并清除 DWM 残留渲染缓存
    /// </summary>
    public static void SafeHideAndPark(Window window, Action? beforeHide = null)
    {
        if (window == null) return;

        try
        {
            beforeHide?.Invoke();

            window.Left = OffScreenCoordinate;
            window.Top = OffScreenCoordinate;
            window.Hide();
            window.Opacity = 1.0;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[OverlayWindowManager] SafeHideAndPark error: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据光标所在屏幕智能计算浮窗坐标并做防溢出贴边
    /// </summary>
    public static Rect CalculateTargetBounds(
        Window window,
        Size desiredDipSize,
        OverlayPositionMode mode,
        Point? physicalCursor = null,
        double offsetX = 0,
        double offsetY = 0)
    {
        var cursorPhysical = physicalCursor ?? ScreenHelper.GetCursorPhysicalPosition();
        var screenCtx = ScreenHelper.GetScreenContextAtPoint(cursorPhysical);
        var cursorDip = ScreenHelper.PhysicalToDip(cursorPhysical, screenCtx.DpiScale);

        var width = desiredDipSize.Width > 0 ? desiredDipSize.Width : (window.ActualWidth > 0 ? window.ActualWidth : window.Width);
        var height = desiredDipSize.Height > 0 ? desiredDipSize.Height : (window.ActualHeight > 0 ? window.ActualHeight : window.Height);

        double targetLeft;
        double targetTop;

        switch (mode)
        {
            case OverlayPositionMode.CenterAroundCursor:
                targetLeft = cursorDip.X - (width / 2) + offsetX;
                targetTop = cursorDip.Y - (height / 2) + offsetY;
                break;

            case OverlayPositionMode.TrailingCursor:
                targetLeft = cursorDip.X + offsetX;
                targetTop = cursorDip.Y - (height / 2) + offsetY;
                break;

            case OverlayPositionMode.CenterOnScreen:
                targetLeft = screenCtx.DipWorkArea.Left + (screenCtx.DipWorkArea.Width - width) / 2 + offsetX;
                targetTop = screenCtx.DipWorkArea.Top + (screenCtx.DipWorkArea.Height - height) / 2 + offsetY;
                break;

            case OverlayPositionMode.CoverScreenWorkArea:
                return screenCtx.DipWorkArea;

            default:
                targetLeft = cursorDip.X + offsetX;
                targetTop = cursorDip.Y + offsetY;
                break;
        }

        var desiredRect = new Rect(targetLeft, targetTop, width, height);
        return ScreenHelper.ClampToWorkArea(desiredRect, screenCtx.DipWorkArea);
    }

    /// <summary>
    /// 将浮窗精准定位到目标屏幕位置并准备入场，同时触发互斥清场
    /// </summary>
    public static void PrepareAndPosition(
        Window window,
        Size desiredDipSize,
        OverlayPositionMode mode,
        Point? physicalCursor = null,
        double offsetX = 0,
        double offsetY = 0,
        bool suppressConflicting = true)
    {
        if (window == null) return;

        if (suppressConflicting)
        {
            SuppressConflictingOverlays();
        }

        var targetBounds = CalculateTargetBounds(window, desiredDipSize, mode, physicalCursor, offsetX, offsetY);

        window.Left = targetBounds.X;
        window.Top = targetBounds.Y;
        if (mode == OverlayPositionMode.CoverScreenWorkArea)
        {
            window.Width = targetBounds.Width;
            window.Height = targetBounds.Height;
        }
        else
        {
            if (desiredDipSize.Width > 0) window.Width = desiredDipSize.Width;
            if (desiredDipSize.Height > 0) window.Height = desiredDipSize.Height;
        }

        window.Opacity = 1.0;
    }

    /// <summary>
    /// 全屏覆盖当前光标所在屏幕工作区，同时触发互斥清场
    /// </summary>
    public static void CoverActiveScreen(Window window, Point? physicalPoint = null, bool suppressConflicting = true)
    {
        if (window == null) return;

        if (suppressConflicting)
        {
            SuppressConflictingOverlays();
        }

        var screenCtx = ScreenHelper.GetScreenContextAtPoint(physicalPoint);
        window.Left = screenCtx.DipWorkArea.Left;
        window.Top = screenCtx.DipWorkArea.Top;
        window.Width = screenCtx.DipWorkArea.Width;
        window.Height = screenCtx.DipWorkArea.Height;
        window.Opacity = 1.0;
    }
}
