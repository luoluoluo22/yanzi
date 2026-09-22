using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenQuickHost;

public enum WindowToggleAction
{
    Activated, // 找到后台/最小化窗口，已成功激活到前台
    Minimized, // 已经在前台，已置于后台 (最小化)
    Launched,  // 未找到已打开窗口，已通过 Process.Start 启动
    Failed     // 发生异常
}

public sealed record WindowToggleResult(
    WindowToggleAction Action,
    IntPtr WindowHandle,
    string Message,
    Exception? Exception = null);

/// <summary>
/// 智能窗口快速切换服务：
/// 针对打开网址/程序、文件夹、Shell 协议的小程序，实现快速窗口切换（Toggle）：
/// 1. 若目标窗口未启动：启动该程序或打开文件夹；
/// 2. 若目标窗口在后台或已最小化：恢复并激活到前台；
/// 3. 若目标窗口当前已在前台：将其置于后台（最小化），自动交还焦点给下层工作窗口。
/// </summary>
public static class QuickWindowSwitchService
{
    private const int SW_SHOW = 5;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const long WS_VISIBLE = 0x10000000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>
    /// 判断目标是否属于支持智能前后台切换的类型（程序、文件夹、系统协议、系统设置等）
    /// </summary>
    public static bool IsToggleEligibleTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        var t = target.Trim();

        // 排除内部特殊协议
        if (t.StartsWith("oqh://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("scope:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 网页链接（http/https）通常在浏览器中作为标签页打开，若无法嗅探独立窗口则默认由系统打开
        // 但 shell:、ms-settings:、本地文件夹、可执行程序均完全支持切换
        if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
            t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(t) ||
            File.Exists(t))
        {
            return true;
        }

        // 常见命令行程序（如 notepad, calc, cmd 等）
        if (!t.Contains('/') && !t.Contains('\\') && !t.Contains(':'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 执行智能前后台切换或启动
    /// </summary>
    public static WindowToggleResult ExecuteToggleOrLaunch(
        string target,
        string? arguments = null,
        string? workingDirectory = null,
        string? title = null,
        IntPtr previousForeground = default)
    {
        var displayTitle = string.IsNullOrWhiteSpace(title) ? target : title.Trim();

        try
        {
            var targetHwnd = FindExistingTargetWindow(target);
            if (targetHwnd != IntPtr.Zero)
            {
                var currentFg = Win32Native.GetForegroundWindow();
                var isForeground = false;

                if (currentFg != IntPtr.Zero)
                {
                    if (currentFg == targetHwnd || Win32Native.GetAncestor(currentFg, Win32Native.GA_ROOT) == targetHwnd)
                    {
                        isForeground = true;
                    }
                }

                if (!isForeground && previousForeground != IntPtr.Zero)
                {
                    if (previousForeground == targetHwnd || Win32Native.GetAncestor(previousForeground, Win32Native.GA_ROOT) == targetHwnd)
                    {
                        isForeground = true;
                    }
                }

                if (isForeground)
                {
                    // 已经在前台 -> 置于后台（最小化），自动交还焦点给下层窗口
                    Win32Native.ShowWindow(targetHwnd, SW_MINIMIZE);
                    HostAssets.AppendLog($"[QuickWindowSwitch] Minimized foreground window 0x{targetHwnd.ToInt64():X} for '{displayTitle}' ({target})");
                    return new WindowToggleResult(WindowToggleAction.Minimized, targetHwnd, $"已将【{displayTitle}】置于后台。");
                }
                else
                {
                    // 在后台或最小化 -> 激活到前台
                    ForceForegroundWindow(targetHwnd);
                    HostAssets.AppendLog($"[QuickWindowSwitch] Activated background window 0x{targetHwnd.ToInt64():X} for '{displayTitle}' ({target})");
                    return new WindowToggleResult(WindowToggleAction.Activated, targetHwnd, $"已激活【{displayTitle}】到前台。");
                }
            }

            // 未找到任何已有窗口 -> 启动新进程
            var psi = CreateLaunchProcessStartInfo(target, arguments, workingDirectory);
            Process.Start(psi);
            HostAssets.AppendLog($"[QuickWindowSwitch] Launched new target for '{displayTitle}': {psi.FileName} {psi.Arguments}".Trim());
            return new WindowToggleResult(WindowToggleAction.Launched, IntPtr.Zero, $"已启动：{displayTitle}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[QuickWindowSwitch] Toggle/Launch failed for '{displayTitle}': {ex.Message}");
            return new WindowToggleResult(WindowToggleAction.Failed, IntPtr.Zero, $"运行失败：{displayTitle}，{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 寻找匹配目标的现有顶级窗口句柄
    /// </summary>
    public static IntPtr FindExistingTargetWindow(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return IntPtr.Zero;
        var t = target.Trim();

        // 1. 文件夹或资源管理器路径（shell:Desktop, shell:Downloads, 本地路径）
        if (IsFolderOrShellTarget(t))
        {
            var folderHwnd = FindExplorerFolderWindow(t);
            if (folderHwnd != IntPtr.Zero)
            {
                return folderHwnd;
            }
        }

        // 2. 系统设置（ms-settings:）
        if (t.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            var settingsHwnd = FindWindowByProcessName(["SystemSettings"]);
            if (settingsHwnd != IntPtr.Zero)
            {
                return settingsHwnd;
            }
        }

        // 3. 应用程序进程匹配（notepad.exe, calc.exe, 绝对路径等）
        var appHwnd = FindApplicationWindow(t);
        if (appHwnd != IntPtr.Zero)
        {
            return appHwnd;
        }

        return IntPtr.Zero;
    }

    private static bool IsFolderOrShellTarget(string target)
    {
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return true;
        if (Directory.Exists(target)) return true;
        if (Path.IsPathRooted(target) && !Path.HasExtension(target)) return true;
        return false;
    }

    /// <summary>
    /// 查找 Windows 资源管理器文件夹窗口
    /// </summary>
    private static IntPtr FindExplorerFolderWindow(string target)
    {
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 收集待匹配的标准路径与文件夹名称
        CollectFolderCandidates(target, candidatePaths, candidateNames);

        // 手段 A：优先通过 Shell COM (Shell.Application) Windows 集合精确定位
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic? shellApp = Activator.CreateInstance(shellType);
                dynamic? windows = shellApp?.Windows();
                if (windows != null)
                {
                    int count = (int)windows.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            dynamic? win = windows.Item(i);
                            if (win == null) continue;
                            long hwndVal = (long)win.HWND;
                            IntPtr hwnd = new IntPtr(hwndVal);
                            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) continue;

                            // 严密排除桌面背景 Progman/WorkerW 和任务栏，必须是真正的文件夹资源管理器窗口
                            var winClass = WindowSensorHelper.GetWindowClassName(hwnd);
                            if (winClass is not ("CabinetWClass" or "ExploreWClass"))
                            {
                                continue;
                            }

                            string? winPath = null;
                            try { winPath = (string?)win.Document?.Folder?.Self?.Path; } catch { }

                            string? winName = null;
                            try { winName = (string?)win.LocationName; } catch { }

                            string? winUrl = null;
                            try { winUrl = (string?)win.LocationURL; } catch { }

                            if (IsFolderMatch(winPath, winName, winUrl, candidatePaths, candidateNames))
                            {
                                return hwnd;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[QuickWindowSwitch] Shell.Application query error: {ex.Message}");
        }

        // 手段 B：通过 EnumWindows 枚举 CabinetWClass 顶级窗口标题进行兜底匹配
        IntPtr matchedHwnd = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return true;
            if (WindowSensorHelper.IsCurrentProcessWindow(hwnd)) return true;

            var className = WindowSensorHelper.GetWindowClassName(hwnd);
            if (className is "CabinetWClass" or "ExploreWClass")
            {
                var title = GetWindowTitleText(hwnd).Trim();
                if (!string.IsNullOrEmpty(title))
                {
                    // 检查标题是否与文件夹名或路径相匹配
                    foreach (var name in candidateNames)
                    {
                        if (string.Equals(title, name, StringComparison.OrdinalIgnoreCase) ||
                            title.StartsWith(name + " - ", StringComparison.OrdinalIgnoreCase) ||
                            title.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase) ||
                            title.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                            (title.Length <= name.Length + 25 && title.Contains(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchedHwnd = hwnd;
                            return false; // 终止遍历
                        }
                    }

                    foreach (var path in candidatePaths)
                    {
                        var folderName = Path.GetFileName(path.TrimEnd('\\', '/'));
                        if (string.Equals(title, path, StringComparison.OrdinalIgnoreCase) ||
                            title.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(folderName) && (string.Equals(title, folderName, StringComparison.OrdinalIgnoreCase) || title.StartsWith(folderName + " - ", StringComparison.OrdinalIgnoreCase))))
                        {
                            matchedHwnd = hwnd;
                            return false;
                        }
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return matchedHwnd;
    }

    private static void CollectFolderCandidates(string target, HashSet<string> paths, HashSet<string> names)
    {
        if (target.Equals("shell:Desktop", StringComparison.OrdinalIgnoreCase))
        {
            AddPath(paths, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            AddPath(paths, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddPath(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"));
            names.Add("桌面");
            names.Add("Desktop");
        }
        else if (target.Equals("shell:Downloads", StringComparison.OrdinalIgnoreCase))
        {
            AddPath(paths, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            names.Add("下载");
            names.Add("Downloads");
        }
        else if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var rawName = target.Substring(6).Trim();
            if (!string.IsNullOrEmpty(rawName))
            {
                names.Add(rawName);
            }
        }
        else
        {
            try
            {
                var full = Path.GetFullPath(target).TrimEnd('\\', '/');
                AddPath(paths, full);
                var dirName = Path.GetFileName(full);
                if (!string.IsNullOrWhiteSpace(dirName))
                {
                    names.Add(dirName);
                }
            }
            catch { }
        }

        // 尝试通过 Shell COM 解析 shell: 对应的真实文件系统路径与显示名称
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shellApp = Activator.CreateInstance(shellType);
                    dynamic? ns = shellApp?.NameSpace(target);
                    if (ns != null)
                    {
                        string? resolvedPath = ns.Self?.Path;
                        string? resolvedName = ns.Self?.Name;
                        AddPath(paths, resolvedPath);
                        if (!string.IsNullOrWhiteSpace(resolvedName))
                        {
                            names.Add(resolvedName.Trim());
                        }
                    }
                }
            }
            catch { }
        }
    }

    private static void AddPath(HashSet<string> set, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var normalized = Path.GetFullPath(path).TrimEnd('\\', '/');
            set.Add(normalized);
        }
        catch
        {
            set.Add(path.TrimEnd('\\', '/'));
        }
    }

    private static bool IsFolderMatch(
        string? winPath, string? winName, string? winUrl,
        HashSet<string> targetPaths, HashSet<string> targetNames)
    {
        if (!string.IsNullOrWhiteSpace(winPath))
        {
            var normalized = winPath.TrimEnd('\\', '/');
            foreach (var path in targetPaths)
            {
                if (string.Equals(normalized, path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(winName))
        {
            var trimmedName = winName.Trim();
            foreach (var name in targetNames)
            {
                if (string.Equals(trimmedName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(winUrl))
        {
            foreach (var path in targetPaths)
            {
                var urlPath = path.Replace('\\', '/');
                if (winUrl.Contains(urlPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 查找应用程序顶级窗口
    /// </summary>
    private static IntPtr FindApplicationWindow(string target)
    {
        var candidateProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseName = Path.GetFileNameWithoutExtension(target).Trim();
        if (string.IsNullOrEmpty(baseName)) return IntPtr.Zero;

        candidateProcessNames.Add(baseName);

        // 常用工具别名映射
        if (baseName.Equals("calc", StringComparison.OrdinalIgnoreCase))
        {
            candidateProcessNames.Add("CalculatorApp");
            candidateProcessNames.Add("Calculator");
        }

        IntPtr matchedHwnd = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return true;
            if (WindowSensorHelper.IsCurrentProcessWindow(hwnd)) return true;

            var exStyle = Win32Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

            Win32Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;

            var procName = ProcessHelper.GetProcessNameByPid(pid);
            if (string.IsNullOrWhiteSpace(procName)) return true;

            if (candidateProcessNames.Contains(procName))
            {
                var title = GetWindowTitleText(hwnd);
                Win32Native.GetWindowRect(hwnd, out var rect);

                // 排除大小过小或零尺寸的隐藏辅助窗口
                if ((rect.Width > 60 && rect.Height > 60) || !string.IsNullOrWhiteSpace(title))
                {
                    // 若目标给定了完整的绝对文件路径，进一步核实可执行文件路径
                    if (File.Exists(target) && Path.IsPathFullyQualified(target))
                    {
                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            var procPath = ProcessHelper.GetProcessExecutablePath(proc);
                            if (!string.IsNullOrWhiteSpace(procPath) &&
                                !string.Equals(Path.GetFullPath(procPath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                            {
                                return true; // 不是同一个可执行文件
                            }
                        }
                        catch { }
                    }

                    matchedHwnd = hwnd;
                    return false; // 找到目标窗口，停止枚举
                }
            }

            return true;
        }, IntPtr.Zero);

        return matchedHwnd;
    }

    private static IntPtr FindWindowByProcessName(IEnumerable<string> processNames)
    {
        var nameSet = new HashSet<string>(processNames, StringComparer.OrdinalIgnoreCase);
        IntPtr matchedHwnd = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return true;
            if (WindowSensorHelper.IsCurrentProcessWindow(hwnd)) return true;

            Win32Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return true;

            var procName = ProcessHelper.GetProcessNameByPid(pid);
            if (!string.IsNullOrWhiteSpace(procName) && nameSet.Contains(procName))
            {
                Win32Native.GetWindowRect(hwnd, out var rect);
                if (rect.Width > 60 && rect.Height > 60)
                {
                    matchedHwnd = hwnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return matchedHwnd;
    }

    /// <summary>
    /// 强力前台激活指定窗口
    /// </summary>
    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        if (IsIconic(hWnd))
        {
            Win32Native.ShowWindow(hWnd, SW_RESTORE);
        }
        else
        {
            Win32Native.ShowWindow(hWnd, SW_SHOW);
        }

        var fg = Win32Native.GetForegroundWindow();
        if (fg == hWnd) return;

        uint fgThread = Win32Native.GetWindowThreadProcessId(fg, out _);
        uint targetThread = Win32Native.GetWindowThreadProcessId(hWnd, out _);
        uint currentThread = GetCurrentThreadId();

        bool attached = false;
        if (fgThread != 0 && fgThread != currentThread)
        {
            attached = Win32Native.AttachThreadInput(currentThread, fgThread, true);
        }

        try
        {
            Win32Native.SetForegroundWindow(hWnd);
            Win32Native.BringWindowToTop(hWnd);
            Win32Native.SetActiveWindow(hWnd);
            Win32Native.SetFocus(hWnd);
        }
        finally
        {
            if (attached)
            {
                Win32Native.AttachThreadInput(currentThread, fgThread, false);
            }
        }
    }

    private static string GetWindowTitleText(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string? ResolveShellTargetToPhysicalPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var t = target.Trim();

        if (t.Equals("shell:Desktop", StringComparison.OrdinalIgnoreCase))
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop)) return desktop;
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktopDir) && Directory.Exists(desktopDir)) return desktopDir;
        }
        else if (t.Equals("shell:Downloads", StringComparison.OrdinalIgnoreCase))
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) return downloads;
        }

        if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic? shellApp = Activator.CreateInstance(shellType);
                    dynamic? ns = shellApp?.NameSpace(t);
                    string? p = (string?)ns?.Self?.Path;
                    if (!string.IsNullOrWhiteSpace(p) && (Directory.Exists(p) || File.Exists(p)))
                    {
                        return p;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    public static ProcessStartInfo CreateLaunchProcessStartInfo(
        string target,
        string? arguments = null,
        string? workingDirectory = null)
    {
        var t = target.Trim();

        if (t.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var physicalPath = ResolveShellTargetToPhysicalPath(t);
            if (!string.IsNullOrWhiteSpace(physicalPath) && Directory.Exists(physicalPath))
            {
                return new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{physicalPath}\"",
                    UseShellExecute = true
                };
            }

            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = t,
                UseShellExecute = true
            };
        }

        if (Directory.Exists(t))
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{t}\"",
                UseShellExecute = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = t,
            Arguments = arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = true
        };
    }
}
