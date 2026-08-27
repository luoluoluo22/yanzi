using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace OpenQuickHost;

/// <summary>
/// 屏幕与显示器上下文信息
/// 包含物理像素与 WPF 设备无关像素 (DIP) 边界及独立 Per-Monitor DPI 缩放
/// </summary>
public sealed record ScreenContext
{
    public IntPtr HMonitor { get; init; }
    public Rect PhysicalBounds { get; init; }
    public Rect PhysicalWorkArea { get; init; }
    public Rect DipBounds { get; init; }
    public Rect DipWorkArea { get; init; }
    public DpiScale DpiScale { get; init; }
    public bool IsPrimary { get; init; }

    public ScreenContext(
        IntPtr hMonitor,
        Rect physicalBounds,
        Rect physicalWorkArea,
        DpiScale dpiScale,
        bool isPrimary)
    {
        HMonitor = hMonitor;
        PhysicalBounds = physicalBounds;
        PhysicalWorkArea = physicalWorkArea;
        DpiScale = dpiScale;
        IsPrimary = isPrimary;

        var sx = Math.Max(0.1, dpiScale.DpiScaleX);
        var sy = Math.Max(0.1, dpiScale.DpiScaleY);

        DipBounds = new Rect(
            physicalBounds.X / sx,
            physicalBounds.Y / sy,
            physicalBounds.Width / sx,
            physicalBounds.Height / sy);

        DipWorkArea = new Rect(
            physicalWorkArea.X / sx,
            physicalWorkArea.Y / sy,
            physicalWorkArea.Width / sx,
            physicalWorkArea.Height / sy);
    }
}

/// <summary>
/// 全局屏幕与多显示器统一调度助手
/// 负责 Per-Monitor DPI v2 解析、物理/DIP 坐标无损换算及工作区防溢出贴边
/// </summary>
public static class ScreenHelper
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorInfoFPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private enum MonitorDpiType
    {
        EffectiveDpi = 0,
        AngularDpi = 1,
        RawDpi = 2
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindowInternal(IntPtr hwnd);

    /// <summary>
    /// 获取指定窗口当前的 DPI（支持 Windows 10+ Per-Monitor DPI，兜底 96 DPI 即 100%）
    /// </summary>
    public static uint GetDpiForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 96u;

        try
        {
            var dpi = GetDpiForWindowInternal(hwnd);
            if (dpi > 0) return dpi;
        }
        catch
        {
            // 旧版 Windows 兜底
        }

        try
        {
            var hMonitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0)
            {
                return dpiX > 0 ? dpiX : 96u;
            }
        }
        catch { }

        return 96u;
    }

    /// <summary>
    /// 获取当前物理光标坐标（物理像素）
    /// </summary>
    public static Point GetCursorPhysicalPosition()
    {
        if (GetCursorPos(out var pt))
        {
            return new Point(pt.X, pt.Y);
        }
        return new Point(0, 0);
    }

    /// <summary>
    /// 获取指定物理点（默认为当前鼠标位置）所在显示器的屏幕上下文信息
    /// </summary>
    public static ScreenContext GetScreenContextAtPoint(Point? physicalPoint = null)
    {
        var targetPt = physicalPoint ?? GetCursorPhysicalPosition();
        var pt = new POINT((int)Math.Round(targetPt.X), (int)Math.Round(targetPt.Y));
        var hMonitor = MonitorFromPoint(pt, MonitorDefaultToNearest);

        return BuildScreenContext(hMonitor);
    }

    /// <summary>
    /// 获取指定 WPF 窗口当前所在显示器的屏幕上下文信息
    /// </summary>
    public static ScreenContext GetScreenContextForWindow(Window window)
    {
        if (window == null) return GetScreenContextAtPoint();

        var helper = new WindowInteropHelper(window);
        var handle = helper.Handle;
        if (handle == IntPtr.Zero)
        {
            try { helper.EnsureHandle(); handle = helper.Handle; } catch { }
        }

        var hMonitor = handle != IntPtr.Zero
            ? MonitorFromWindow(handle, MonitorDefaultToNearest)
            : MonitorFromPoint(new POINT(0, 0), MonitorDefaultToNearest);

        return BuildScreenContext(hMonitor);
    }

    /// <summary>
    /// 读取指定显示器的精准 DPI 缩放比（Per-Monitor DPI v2）
    /// </summary>
    public static DpiScale GetDpiScaleForMonitor(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero)
        {
            try
            {
                if (GetDpiForMonitor(hMonitor, MonitorDpiType.EffectiveDpi, out var dpiX, out var dpiY) == 0)
                {
                    return new DpiScale(dpiX / 96.0, dpiY / 96.0);
                }
            }
            catch
            {
                // Shcore.dll 不可用时的兜底
            }
        }

        return new DpiScale(1.0, 1.0);
    }

    /// <summary>
    /// 将物理像素坐标转为指定屏幕下的 WPF DIP (设备无关像素)
    /// </summary>
    public static Point PhysicalToDip(Point physical, DpiScale dpiScale)
    {
        var sx = Math.Max(0.1, dpiScale.DpiScaleX);
        var sy = Math.Max(0.1, dpiScale.DpiScaleY);
        return new Point(physical.X / sx, physical.Y / sy);
    }

    /// <summary>
    /// 将 DIP 坐标转为物理像素坐标
    /// </summary>
    public static Point DipToPhysical(Point dip, DpiScale dpiScale)
    {
        return new Point(dip.X * dpiScale.DpiScaleX, dip.Y * dpiScale.DpiScaleY);
    }

    /// <summary>
    /// 将窗口边界安全贴边裁剪至工作区内（防止超出屏幕边缘或被任务栏遮挡）
    /// </summary>
    public static Rect ClampToWorkArea(Rect desiredDipRect, Rect workAreaDip)
    {
        var clampedX = desiredDipRect.X;
        var clampedY = desiredDipRect.Y;

        // X 轴贴边防溢出
        if (clampedX + desiredDipRect.Width > workAreaDip.Right)
        {
            clampedX = workAreaDip.Right - desiredDipRect.Width;
        }
        if (clampedX < workAreaDip.Left)
        {
            clampedX = workAreaDip.Left;
        }

        // Y 轴贴边防溢出
        if (clampedY + desiredDipRect.Height > workAreaDip.Bottom)
        {
            clampedY = workAreaDip.Bottom - desiredDipRect.Height;
        }
        if (clampedY < workAreaDip.Top)
        {
            clampedY = workAreaDip.Top;
        }

        return new Rect(clampedX, clampedY, desiredDipRect.Width, desiredDipRect.Height);
    }

    private static ScreenContext BuildScreenContext(IntPtr hMonitor)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref mi))
        {
            var dpi = GetDpiScaleForMonitor(hMonitor);
            var bounds = new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top);
            var work = new Rect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
            var isPrimary = (mi.dwFlags & MonitorInfoFPrimary) != 0;

            return new ScreenContext(hMonitor, bounds, work, dpi, isPrimary);
        }

        // 终极兜底：使用 WPF 虚拟桌面系统参数
        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vWidth = SystemParameters.VirtualScreenWidth;
        var vHeight = SystemParameters.VirtualScreenHeight;
        var defaultDpi = new DpiScale(1.0, 1.0);
        var defaultRect = new Rect(vLeft, vTop, vWidth, vHeight);

        return new ScreenContext(IntPtr.Zero, defaultRect, defaultRect, defaultDpi, isPrimary: true);
    }
}
