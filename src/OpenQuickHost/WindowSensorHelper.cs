using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace OpenQuickHost;

public enum ScreenEdgePosition
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    TopLeft = 5,
    TopRight = 6,
    BottomLeft = 7,
    BottomRight = 8
}

/// <summary>
/// 控件级与屏幕边缘/特殊区域嗅探底座（借鉴 StrokesPlus.net / Quicker 区域与控件感知机制）
/// </summary>
public static class WindowSensorHelper
{
    private const int MaxClassNameLength = 256;

    /// <summary>
    /// 获取指定窗口句柄的 Win32 类名
    /// </summary>
    public static string GetWindowClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(MaxClassNameLength);
        Win32Native.GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// 获取光标所在位置的顶级或子控件窗口句柄
    /// </summary>
    public static IntPtr GetWindowAtCursor()
    {
        if (Win32Native.GetCursorPos(out var pt))
        {
            return Win32Native.WindowFromPoint(pt);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 判断是否是 Windows 任务栏（主任务栏或副屏任务栏）
    /// </summary>
    public static bool IsTaskbarWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        var className = GetWindowClassName(hWnd);
        return string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断是否是 Windows 桌面窗口（涵盖 Progman、WorkerW、桌面视图 SHELLDLL_DefView、桌面图标列表 SysListView32 及 ShellWindow）
    /// </summary>
    public static bool IsDesktopWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        if (hWnd == Win32Native.GetShellWindow()) return true;

        var className = GetWindowClassName(hWnd);
        if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "SysListView32", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var root = Win32Native.GetAncestor(hWnd, Win32Native.GA_ROOT);
            if (root != IntPtr.Zero && root != hWnd)
            {
                var rootClass = GetWindowClassName(root);
                if (string.Equals(rootClass, "Progman", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rootClass, "WorkerW", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// 判断是否是 Windows 桌面或任务栏
    /// </summary>
    public static bool IsDesktopOrTaskbarWindow(IntPtr hWnd)
    {
        return IsDesktopWindow(hWnd) || IsTaskbarWindow(hWnd);
    }

    private static readonly uint CurrentPid = (uint)Environment.ProcessId;

    /// <summary>
    /// 判断指定窗口是否属于当前燕子程序自身的所有实例和辅助浮窗
    /// </summary>
    public static bool IsCurrentProcessWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hWnd, out var pid);
        return pid != 0 && pid == CurrentPid;
    }

    /// <summary>
    /// 获取指定窗口的规范化应用标识（桌面统一返回 "desktop"，普通窗口返回其进程名，属于宿主自身返回空字符串）
    /// </summary>
    public static string GetWindowProcessName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        if (!Win32Native.IsWindow(hWnd)) return string.Empty;
        if (IsDesktopOrTaskbarWindow(hWnd)) return "desktop";

        try
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0 || pid == CurrentPid) return string.Empty;

            // 优先走 Limited Information 句柄路径：对高权限/受保护窗口更稳，
            // 也能复用已有 pid 名称缓存，避免 Process.GetProcessById 的 AccessDenied。
            var name = ProcessHelper.GetProcessNameByPid(pid);
            if (string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    name = proc.ProcessName;
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"[WindowSensorHelper] GetProcessById failed for pid={pid}: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // 严格过滤自身（兼容历史名称与当前名称）
            if (string.Equals(name, "OpenQuickHost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Yanzi", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // 动态壁纸（Wallpaper Engine）窗口归类为桌面
            if (string.Equals(name, "wallpaper32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "wallpaper64", StringComparison.OrdinalIgnoreCase))
            {
                return "desktop";
            }

            return name;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[WindowSensorHelper] GetWindowProcessName error: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 呼出时智能解析目标窗口与所属进程（借鉴背包 100% 可靠前台检测机制，支持光标窗口与前台窗口双向容错回退）
    /// </summary>
    public static (IntPtr TargetHwnd, string ProcessName) ResolveActiveTargetWindowAndProcess(
        IntPtr excludeHwnd = default,
        System.Drawing.Point? cursorPoint = null)
    {
        // 1. 获取系统真实的前台活动窗口（背包采用的核心基准）
        var fgHwnd = Win32Native.GetForegroundWindow();
        if (fgHwnd != IntPtr.Zero && (fgHwnd == excludeHwnd || IsCurrentProcessWindow(fgHwnd)))
        {
            fgHwnd = IntPtr.Zero;
        }

        // 2. 探测光标所在位置的顶级根窗口
        IntPtr cursorRootHwnd = IntPtr.Zero;
        try
        {
            Win32Native.POINT pt;
            if (cursorPoint.HasValue)
            {
                pt = new Win32Native.POINT(cursorPoint.Value.X, cursorPoint.Value.Y);
            }
            else if (!Win32Native.GetCursorPos(out pt))
            {
                pt = default;
            }

            if (pt.X != 0 || pt.Y != 0)
            {
                var under = Win32Native.WindowFromPoint(pt);
                if (under != IntPtr.Zero && under != excludeHwnd && !IsCurrentProcessWindow(under))
                {
                    cursorRootHwnd = Win32Native.GetAncestor(under, Win32Native.GA_ROOT);
                    if (cursorRootHwnd == excludeHwnd || IsCurrentProcessWindow(cursorRootHwnd))
                    {
                        cursorRootHwnd = IntPtr.Zero;
                    }
                }
            }
        }
        catch { }

        // 3. 优先探测光标所在窗口（用户光标悬停在哪，直觉上倾向于使用该窗口的专属轮盘）
        if (cursorRootHwnd != IntPtr.Zero && Win32Native.IsWindow(cursorRootHwnd))
        {
            var cursorProc = GetWindowProcessName(cursorRootHwnd);
            if (!string.IsNullOrWhiteSpace(cursorProc))
            {
                HostAssets.AppendLog($"[WindowSensorHelper] ResolveActiveTarget: chosen cursorRoot=0x{cursorRootHwnd.ToInt64():X}({cursorProc}), fg=0x{fgHwnd.ToInt64():X}.");
                return (cursorRootHwnd, cursorProc);
            }
        }

        // 4. 关键兜底回退：当光标下为特殊子控件（如 Electron/Chromium 渲染窗口）、无效句柄或解析不出进程时，
        //    坚决无条件回退到系统真实前台活动窗口（对齐背包做法，解决无法识别当前应用的根本问题）
        if (fgHwnd != IntPtr.Zero && Win32Native.IsWindow(fgHwnd))
        {
            var fgProc = GetWindowProcessName(fgHwnd);
            if (!string.IsNullOrWhiteSpace(fgProc))
            {
                HostAssets.AppendLog($"[WindowSensorHelper] ResolveActiveTarget: chosen fg=0x{fgHwnd.ToInt64():X}({fgProc}), cursorRoot=0x{cursorRootHwnd.ToInt64():X}.");
                return (fgHwnd, fgProc);
            }
        }

        // 5. 桌面与任务栏兜底判断
        if (cursorRootHwnd != IntPtr.Zero && IsDesktopOrTaskbarWindow(cursorRootHwnd))
        {
            HostAssets.AppendLog($"[WindowSensorHelper] ResolveActiveTarget: chosen desktop via cursorRoot=0x{cursorRootHwnd.ToInt64():X}.");
            return (cursorRootHwnd, "desktop");
        }
        if (fgHwnd != IntPtr.Zero && IsDesktopOrTaskbarWindow(fgHwnd))
        {
            HostAssets.AppendLog($"[WindowSensorHelper] ResolveActiveTarget: chosen desktop via fg=0x{fgHwnd.ToInt64():X}.");
            return (fgHwnd, "desktop");
        }

        // 6. 最终降级：无有效目标
        var fallbackHwnd = fgHwnd != IntPtr.Zero ? fgHwnd : cursorRootHwnd;
        HostAssets.AppendLog($"[WindowSensorHelper] ResolveActiveTarget: fallback to 0x{fallbackHwnd.ToInt64():X}, process=(none).");
        return (fallbackHwnd, string.Empty);
    }

    /// <summary>
    /// 判断物理点是否处于某个屏幕的边缘或角落（默认 5 像素热区）
    /// </summary>
    public static ScreenEdgePosition GetScreenEdge(System.Drawing.Point pt, int edgeThreshold = 5)
    {
        var screen = Screen.FromPoint(pt);
        var bounds = screen.Bounds;

        bool isLeft = pt.X <= bounds.Left + edgeThreshold;
        bool isRight = pt.X >= bounds.Right - edgeThreshold;
        bool isTop = pt.Y <= bounds.Top + edgeThreshold;
        bool isBottom = pt.Y >= bounds.Bottom - edgeThreshold;

        if (isTop && isLeft) return ScreenEdgePosition.TopLeft;
        if (isTop && isRight) return ScreenEdgePosition.TopRight;
        if (isBottom && isLeft) return ScreenEdgePosition.BottomLeft;
        if (isBottom && isRight) return ScreenEdgePosition.BottomRight;

        if (isTop) return ScreenEdgePosition.Top;
        if (isBottom) return ScreenEdgePosition.Bottom;
        if (isLeft) return ScreenEdgePosition.Left;
        if (isRight) return ScreenEdgePosition.Right;

        return ScreenEdgePosition.None;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// 获取当前前台活动窗口所属进程的完整可执行文件路径
    /// </summary>
    public static string GetForegroundProcessPath()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return string.Empty;
        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == 0) return string.Empty;

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 检查当前前台活动窗口是否匹配指定的目标应用列表（支持多个目标 exe 路径或 exe 文件名）
    /// </summary>
    public static bool IsForegroundProcessMatch(System.Collections.Generic.IEnumerable<string>? targetAppPaths)
    {
        if (targetAppPaths == null) return true;
        var targets = new System.Collections.Generic.HashSet<string>(targetAppPaths, StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0) return true; // 未限定时默认全局匹配

        var currentPath = GetForegroundProcessPath();
        if (string.IsNullOrWhiteSpace(currentPath)) return false;

        var currentExeName = System.IO.Path.GetFileName(currentPath);

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target)) continue;
            if (string.Equals(target, currentPath, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(target, currentExeName, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(System.IO.Path.GetFileName(target), currentExeName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// 呼出前探测光标所在位置的窗口并将其激活为前台活动窗口（支持普通应用、桌面与任务栏）。
    /// 解决呼出前仅检查置顶窗口导致光标放在桌面或后台应用时上下文不符合直觉的问题。
    /// </summary>
    /// <param name="cursorPoint">可选光标物理坐标</param>
    /// <param name="excludeHwnd">需排除的自身窗口句柄</param>
    /// <returns>激活后（或无需切换时）的目标前台窗口句柄</returns>
    public static IntPtr ActivateWindowUnderCursor(System.Drawing.Point? cursorPoint = null, IntPtr excludeHwnd = default)
    {
        try
        {
            var currentFg = Win32Native.GetForegroundWindow();

            // 1. 获取光标物理坐标
            Win32Native.POINT pt;
            if (cursorPoint.HasValue)
            {
                pt = new Win32Native.POINT(cursorPoint.Value.X, cursorPoint.Value.Y);
            }
            else if (!Win32Native.GetCursorPos(out pt))
            {
                return currentFg;
            }

            // 2. 探测光标下的窗口句柄
            var under = Win32Native.WindowFromPoint(pt);
            if (under == IntPtr.Zero || under == excludeHwnd || IsCurrentProcessWindow(under))
            {
                // 光标悬停在燕子自身窗口（主界面、背包、浮窗等）或无效位置，不切换前台
                return currentFg;
            }

            // 3. 解析目标顶级根窗口
            var targetHwnd = Win32Native.GetAncestor(under, Win32Native.GA_ROOT);
            if (targetHwnd == IntPtr.Zero || !Win32Native.IsWindow(targetHwnd))
            {
                targetHwnd = under;
            }

            if (targetHwnd == excludeHwnd || IsCurrentProcessWindow(targetHwnd))
            {
                return currentFg;
            }

            // 4. 判断是否是桌面或任务栏
            bool isDesktop = IsDesktopWindow(targetHwnd) || IsDesktopWindow(under);
            if (!isDesktop)
            {
                var underClass = GetWindowClassName(under);
                if (string.Equals(underClass, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(underClass, "SysListView32", StringComparison.OrdinalIgnoreCase))
                {
                    isDesktop = true;
                }
                else
                {
                    var proc = GetWindowProcessName(targetHwnd);
                    if (string.Equals(proc, "desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        isDesktop = true;
                    }
                }
            }

            if (isDesktop)
            {
                // 如果当前前台已经是桌面，无需重复激活
                if (IsDesktopWindow(currentFg))
                {
                    return currentFg;
                }

                // 激活桌面：优先使用 ShellWindow 或 Progman，若无效则用探测到的根窗口
                var shellWnd = Win32Native.GetShellWindow();
                if (shellWnd == IntPtr.Zero)
                {
                    shellWnd = Win32Native.FindWindow("Progman", null);
                }
                var desktopToActivate = (shellWnd != IntPtr.Zero && Win32Native.IsWindow(shellWnd))
                    ? shellWnd
                    : targetHwnd;

                if (desktopToActivate != IntPtr.Zero)
                {
                    ForceSetForegroundWindow(desktopToActivate);
                    HostAssets.AppendLog($"[WindowSensorHelper] ActivateWindowUnderCursor: activated desktop 0x{desktopToActivate.ToInt64():X}.");
                    var newFg = Win32Native.GetForegroundWindow();
                    return (newFg != IntPtr.Zero && IsDesktopWindow(newFg)) ? newFg : desktopToActivate;
                }
                return currentFg;
            }

            // 5. 若光标已经在当前前台窗口上，无需重复激活
            if (targetHwnd == currentFg)
            {
                return currentFg;
            }

            // 6. 普通后台应用窗口：强制将其激活至前台
            if (Win32Native.IsWindow(targetHwnd))
            {
                ForceSetForegroundWindow(targetHwnd);
                HostAssets.AppendLog($"[WindowSensorHelper] ActivateWindowUnderCursor: activated window 0x{targetHwnd.ToInt64():X}, previousFg=0x{currentFg.ToInt64():X}.");
                var newFg = Win32Native.GetForegroundWindow();
                return newFg != IntPtr.Zero ? newFg : targetHwnd;
            }

            return currentFg;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[WindowSensorHelper] ActivateWindowUnderCursor error: {ex.Message}");
            return Win32Native.GetForegroundWindow();
        }
    }

    /// <summary>
    /// 突破 Windows 前台限制强制激活指定窗口并带到前台
    /// </summary>
    public static bool ForceSetForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !Win32Native.IsWindow(hWnd)) return false;

        var currentFg = Win32Native.GetForegroundWindow();
        if (currentFg == hWnd) return true;

        if (Win32Native.IsIconic(hWnd))
        {
            Win32Native.ShowWindow(hWnd, Win32Native.SW_RESTORE);
        }

        uint fgThread = Win32Native.GetWindowThreadProcessId(currentFg, out _);
        uint targetThread = Win32Native.GetWindowThreadProcessId(hWnd, out _);
        uint currentThread = Win32Native.GetCurrentThreadId();

        bool attachedFg = false;
        if (fgThread != 0 && fgThread != currentThread)
        {
            attachedFg = Win32Native.AttachThreadInput(currentThread, fgThread, true);
        }

        bool attachedTarget = false;
        if (targetThread != 0 && targetThread != currentThread && targetThread != fgThread)
        {
            attachedTarget = Win32Native.AttachThreadInput(currentThread, targetThread, true);
        }

        try
        {
            Win32Native.BringWindowToTop(hWnd);
            var result = Win32Native.SetForegroundWindow(hWnd);
            Win32Native.SetActiveWindow(hWnd);
            return result;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (attachedTarget)
            {
                Win32Native.AttachThreadInput(currentThread, targetThread, false);
            }
            if (attachedFg)
            {
                Win32Native.AttachThreadInput(currentThread, fgThread, false);
            }
        }
    }
}
