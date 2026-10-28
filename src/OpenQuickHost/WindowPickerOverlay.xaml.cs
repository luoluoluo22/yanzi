using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace OpenQuickHost
{
    public partial class WindowPickerOverlay : Window
    {
        private TaskCompletionSource<string> _tcs = new();
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private LowLevelMouseProc _mouseProc;
        private LowLevelKeyboardProc _keyboardProc;
        private IntPtr _lastHwnd = IntPtr.Zero;

        public WindowPickerOverlay()
        {
            InitializeComponent();
            
            // 获取虚拟屏幕的大小，覆盖所有显示器
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            _mouseProc = MouseHookCallback;
            _keyboardProc = KeyboardHookCallback;
        }

        public async Task<string> ShowPickerAsync()
        {
            Show();
            Activate();
            InstallHooks();

            try
            {
                return await _tcs.Task;
            }
            finally
            {
                UninstallHooks();
                Close();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // 设置窗口属性为无激活、置顶、鼠标穿透
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        }

        private void InstallHooks()
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                var moduleName = curModule?.ModuleName ?? string.Empty;
                var moduleHandle = GetModuleHandle(moduleName);
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            }
        }

        private void UninstallHooks()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    UpdateHighlight(hookStruct.pt);
                }
                else if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    // 获取当前鼠标下的进程并返回
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var processName = GetProcessNameFromPoint(hookStruct.pt);
                    _tcs.TrySetResult(processName);
                    return (IntPtr)1; // 拦截点击
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_ESCAPE)
                {
                    _tcs.TrySetResult(string.Empty); // 按 ESC 取消
                    return (IntPtr)1; // 拦截 ESC
                }
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void UpdateHighlight(POINT pt)
        {
            var hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return;

            // 获取顶级父窗口
            var topHwnd = GetAncestor(hwnd, GA_ROOT);
            if (topHwnd == IntPtr.Zero) topHwnd = hwnd;

            if (topHwnd == _lastHwnd) return;
            _lastHwnd = topHwnd;

            // 检查是不是我们自己的窗口，如果是则忽略
            _ = GetWindowThreadProcessId(topHwnd, out var processId);
            if (processId == Environment.ProcessId)
            {
                HighlightBorder.Visibility = Visibility.Collapsed;
                WindowTitleText.Text = "无";
                ProcessNameText.Text = "无";
                return;
            }

            if (GetWindowRect(topHwnd, out var rect))
            {
                // 将屏幕坐标转换为 OverlayCanvas 内的相对坐标
                var virtualLeft = SystemParameters.VirtualScreenLeft;
                var virtualTop = SystemParameters.VirtualScreenTop;

                var left = rect.left - virtualLeft;
                var top = rect.top - virtualTop;
                var width = rect.right - rect.left;
                var height = rect.bottom - rect.top;

                HighlightBorder.Width = Math.Max(0, width);
                HighlightBorder.Height = Math.Max(0, height);
                Canvas.SetLeft(HighlightBorder, left);
                Canvas.SetTop(HighlightBorder, top);
                HighlightBorder.Visibility = Visibility.Visible;

                // 读取窗口标题和进程名
                var title = GetWindowTitle(topHwnd);
                var processName = GetProcessNameByHwnd(topHwnd);

                WindowTitleText.Text = string.IsNullOrWhiteSpace(title) ? "[无标题窗口]" : title;
                ProcessNameText.Text = processName;
            }
        }

        private string GetProcessNameFromPoint(POINT pt)
        {
            var hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return string.Empty;

            var topHwnd = GetAncestor(hwnd, GA_ROOT);
            if (topHwnd == IntPtr.Zero) topHwnd = hwnd;

            return GetProcessNameByHwnd(topHwnd);
        }

        private string GetProcessNameByHwnd(IntPtr hwnd)
        {
            try
            {
                var className = new StringBuilder(256);
                if (GetClassName(hwnd, className, className.Capacity) > 0)
                {
                    var classStr = className.ToString();
                    if (string.Equals(classStr, "Progman", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(classStr, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(classStr, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(classStr, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                    {
                        return "desktop";
                    }
                }

                _ = GetWindowThreadProcessId(hwnd, out var processId);
                if (processId == 0) return string.Empty;
                
                using var proc = Process.GetProcessById((int)processId);
                return proc.ProcessName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            var len = GetWindowTextLength(hwnd);
            if (len <= 0) return string.Empty;

            var sb = new StringBuilder(len + 1);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        // Win32 API
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_ESCAPE = 0x1B;
        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}
