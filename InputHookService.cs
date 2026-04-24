using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public class InputHookService
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int VK_CONTROL = 0x11;
    private const int XBUTTON1 = 1;
    private const int XBUTTON2 = 2;

    private static LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static DispatcherTimer? _longPressTimer;
    private static Action? _onLongPressRelease;
    private static Action? _onShowPanel;
    private static QuickPanelMouseTriggerSettings _settings = new();
    private static bool _isEnabled;
    private static bool _dragTriggered;
    private static bool _releaseShouldExecute;
    private static TrackedMouseButton _trackedButton = TrackedMouseButton.None;
    private static POINT _downPoint;

    public static void Start(Action onLongPress, Action? onLongPressRelease = null)
    {
        if (_isEnabled) return;
        
        _onShowPanel = onLongPress;
        _onLongPressRelease = onLongPressRelease;
        ReloadSettings();
        _hookID = SetHook(_proc);
        
        _longPressTimer = new DispatcherTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.LongPressMilliseconds, 150, 2000));
        _longPressTimer.Tick += (s, e) =>
        {
            _longPressTimer.Stop();
            _releaseShouldExecute = true;
            HostAssets.AppendLog($"Input hook: {_trackedButton} long press triggered.");
            InvokeShowPanel();
        };

        _isEnabled = true;
    }

    public static void Stop()
    {
        if (!_isEnabled) return;
        UnhookWindowsHookEx(_hookID);
        _longPressTimer?.Stop();
        _isEnabled = false;
    }

    public static void ReloadSettings()
    {
        _settings = AppSettingsStore.Load().QuickPanelMouseTriggers ?? new QuickPanelMouseTriggerSettings();
        if (!_settings.MiddleButtonDown &&
            !_settings.X1ButtonDown &&
            !_settings.X2ButtonDown &&
            !_settings.CtrlLeftClick &&
            !_settings.CtrlRightClick &&
            !_settings.MiddleButtonLongPress &&
            !_settings.RightButtonLongPress &&
            !_settings.RightButtonDrag &&
            !_settings.HorizontalWheel)
        {
            _settings.MiddleButtonLongPress = true;
        }

        if (_longPressTimer != null)
        {
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.LongPressMilliseconds, 150, 2000));
        }
    }

    private static IntPtr SetHook(LowLevelMouseProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (message == WM_LBUTTONDOWN)
            {
                if (_settings.CtrlLeftClick && IsControlDown())
                {
                    HostAssets.AppendLog("Input hook: Ctrl+left click triggered.");
                    InvokeShowPanel();
                }
            }
            else if (message == WM_RBUTTONDOWN)
            {
                BeginTracking(TrackedMouseButton.Right, mouse.pt);
                if (_settings.CtrlRightClick && IsControlDown())
                {
                    HostAssets.AppendLog("Input hook: Ctrl+right click triggered.");
                    InvokeShowPanel();
                }
                else if (_settings.RightButtonLongPress)
                {
                    StartLongPressTimer();
                }
            }
            else if (message == WM_MBUTTONDOWN)
            {
                BeginTracking(TrackedMouseButton.Middle, mouse.pt);
                if (_settings.MiddleButtonDown)
                {
                    _releaseShouldExecute = true;
                    HostAssets.AppendLog("Input hook: middle button down triggered.");
                    InvokeShowPanel();
                }
                else if (_settings.MiddleButtonLongPress)
                {
                    StartLongPressTimer();
                }
            }
            else if (message == WM_XBUTTONDOWN)
            {
                var xButton = GetXButton(mouse.mouseData);
                if (xButton == XBUTTON1)
                {
                    BeginTracking(TrackedMouseButton.X1, mouse.pt);
                    if (_settings.X1ButtonDown)
                    {
                        _releaseShouldExecute = true;
                        HostAssets.AppendLog("Input hook: X1 button down triggered.");
                        InvokeShowPanel();
                    }
                }
                else if (xButton == XBUTTON2)
                {
                    BeginTracking(TrackedMouseButton.X2, mouse.pt);
                    if (_settings.X2ButtonDown)
                    {
                        _releaseShouldExecute = true;
                        HostAssets.AppendLog("Input hook: X2 button down triggered.");
                        InvokeShowPanel();
                    }
                }
            }
            else if (message == WM_MOUSEMOVE)
            {
                HandleMouseMove(mouse.pt);
            }
            else if (message == WM_RBUTTONUP)
            {
                EndTracking(TrackedMouseButton.Right);
            }
            else if (message == WM_MBUTTONUP)
            {
                EndTracking(TrackedMouseButton.Middle);
            }
            else if (message == WM_XBUTTONUP)
            {
                var xButton = GetXButton(mouse.mouseData);
                EndTracking(xButton == XBUTTON1 ? TrackedMouseButton.X1 : TrackedMouseButton.X2);
            }
            else if (message == WM_MOUSEHWHEEL && _settings.HorizontalWheel)
            {
                HostAssets.AppendLog("Input hook: horizontal wheel triggered.");
                InvokeShowPanel();
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static void BeginTracking(TrackedMouseButton button, POINT point)
    {
        _trackedButton = button;
        _downPoint = point;
        _dragTriggered = false;
        _releaseShouldExecute = false;
    }

    private static void StartLongPressTimer()
    {
        ReloadSettings();
        _longPressTimer?.Stop();
        _longPressTimer?.Start();
    }

    private static void HandleMouseMove(POINT point)
    {
        if (_trackedButton != TrackedMouseButton.Right || !_settings.RightButtonDrag || _dragTriggered)
        {
            return;
        }

        var threshold = Math.Clamp(_settings.DragThresholdPixels, 8, 120);
        var dx = point.x - _downPoint.x;
        var dy = point.y - _downPoint.y;
        if ((dx * dx) + (dy * dy) < threshold * threshold)
        {
            return;
        }

        _dragTriggered = true;
        _releaseShouldExecute = true;
        _longPressTimer?.Stop();
        HostAssets.AppendLog("Input hook: right button drag triggered.");
        InvokeShowPanel();
    }

    private static void EndTracking(TrackedMouseButton button)
    {
        if (_trackedButton != button)
        {
            return;
        }

        _longPressTimer?.Stop();
        if (_releaseShouldExecute && _settings.ExecuteOnButtonRelease)
        {
            HostAssets.AppendLog($"Input hook: {button} released after trigger.");
            System.Windows.Application.Current.Dispatcher.Invoke(() => _onLongPressRelease?.Invoke());
        }

        _dragTriggered = false;
        _releaseShouldExecute = false;
        _trackedButton = TrackedMouseButton.None;
    }

    private static void InvokeShowPanel()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _onShowPanel?.Invoke());
    }

    private static bool IsControlDown() => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

    private static int GetXButton(uint mouseData) => (int)((mouseData >> 16) & 0xffff);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private enum TrackedMouseButton
    {
        None,
        Right,
        Middle,
        X1,
        X2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
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
