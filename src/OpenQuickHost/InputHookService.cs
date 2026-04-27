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
    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private static LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static DispatcherTimer? _longPressTimer;
    private static Action? _onLongPressRelease;
    private static Action? _onShowPanel;
    private static QuickPanelMouseTriggerSettings _settings = new();
    private static bool _isEnabled;
    private static bool _dragTriggered;
    private static bool _releaseShouldExecute;
    private static bool _rightButtonDownSwallowed;
    private static TrackedMouseButton _trackedButton = TrackedMouseButton.None;
    private static POINT _downPoint;

    public static bool IsRunning => _isEnabled;

    public static void Start(Action onLongPress, Action? onLongPressRelease = null)
    {
        if (_isEnabled)
        {
            HostAssets.AppendLog("Input hook: start skipped because hook is already running.");
            return;
        }
        
        _onShowPanel = onLongPress;
        _onLongPressRelease = onLongPressRelease;
        ReloadSettings();
        _hookID = SetHook(_proc);
        if (_hookID == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            HostAssets.AppendLog($"Input hook: failed to install low level mouse hook, lastError={error}.");
            return;
        }
        
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
        HostAssets.AppendLog($"Input hook: started. hook=0x{_hookID.ToInt64():X}, triggers={DescribeSettings()}.");
    }

    public static void Stop()
    {
        if (!_isEnabled) return;
        var unhooked = UnhookWindowsHookEx(_hookID);
        HostAssets.AppendLog($"Input hook: stopped. unhooked={unhooked}.");
        _longPressTimer?.Stop();
        _hookID = IntPtr.Zero;
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
            var moduleHandle = GetModuleHandle(curModule.ModuleName);
            var hook = SetWindowsHookEx(WH_MOUSE_LL, proc, moduleHandle, 0);
            if (hook != IntPtr.Zero)
            {
                return hook;
            }

            var firstError = Marshal.GetLastWin32Error();
            HostAssets.AppendLog($"Input hook: SetWindowsHookEx failed with module handle, module={curModule.ModuleName}, hMod=0x{moduleHandle.ToInt64():X}, lastError={firstError}; retrying with hMod=0.");
            return SetWindowsHookEx(WH_MOUSE_LL, proc, IntPtr.Zero, 0);
        }
    }

    private static string DescribeSettings()
    {
        var enabled = new List<string>();
        if (_settings.MiddleButtonDown) enabled.Add("MiddleDown");
        if (_settings.X1ButtonDown) enabled.Add("X1Down");
        if (_settings.X2ButtonDown) enabled.Add("X2Down");
        if (_settings.CtrlLeftClick) enabled.Add("CtrlLeft");
        if (_settings.CtrlRightClick) enabled.Add("CtrlRight");
        if (_settings.MiddleButtonLongPress) enabled.Add($"MiddleLong:{_settings.LongPressMilliseconds}ms");
        if (_settings.RightButtonLongPress) enabled.Add($"RightLong:{_settings.LongPressMilliseconds}ms");
        if (_settings.RightButtonDrag) enabled.Add($"RightDrag:{_settings.DragThresholdPixels}px");
        if (_settings.HorizontalWheel) enabled.Add("HorizontalWheel");
        if (_settings.ExecuteOnButtonRelease) enabled.Add("ReleaseExec");
        return enabled.Count == 0 ? "none" : string.Join(",", enabled);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((mouse.flags & LLMHF_INJECTED) != 0)
            {
                return CallNextHookEx(_hookID, nCode, wParam, lParam);
            }

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
                HostAssets.AppendLog($"Input hook: right button down, rightLong={_settings.RightButtonLongPress}, rightDrag={_settings.RightButtonDrag}, ctrlRight={_settings.CtrlRightClick}, ctrlDown={IsControlDown()}, pt=({mouse.pt.x},{mouse.pt.y}).");
                _rightButtonDownSwallowed = ShouldDelayRightButtonClick();
                if (_settings.CtrlRightClick && IsControlDown())
                {
                    _releaseShouldExecute = true;
                    HostAssets.AppendLog("Input hook: Ctrl+right click triggered.");
                    InvokeShowPanel();
                }
                else if (_settings.RightButtonLongPress)
                {
                    StartLongPressTimer();
                }

                if (_rightButtonDownSwallowed)
                {
                    return (IntPtr)1;
                }
            }
            else if (message == WM_MBUTTONDOWN)
            {
                BeginTracking(TrackedMouseButton.Middle, mouse.pt);
                HostAssets.AppendLog($"Input hook: middle button down, middleDown={_settings.MiddleButtonDown}, middleLong={_settings.MiddleButtonLongPress}, pt=({mouse.pt.x},{mouse.pt.y}).");
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
                HostAssets.AppendLog($"Input hook: right button up, tracked={_trackedButton}, releaseShouldExecute={_releaseShouldExecute}, rightDownSwallowed={_rightButtonDownSwallowed}.");
                var shouldReplayShortClick = _rightButtonDownSwallowed && !_releaseShouldExecute;
                var shouldSwallow = _rightButtonDownSwallowed || _releaseShouldExecute;
                if (EndTracking(TrackedMouseButton.Right))
                {
                    HostAssets.AppendLog("Input hook: swallowed right button up after panel trigger.");
                    return (IntPtr)1;
                }

                if (shouldReplayShortClick)
                {
                    ReplayShortRightClickAfterHookReturns();
                }

                if (shouldSwallow)
                {
                    return (IntPtr)1;
                }
            }
            else if (message == WM_MBUTTONUP)
            {
                HostAssets.AppendLog($"Input hook: middle button up, tracked={_trackedButton}, releaseShouldExecute={_releaseShouldExecute}.");
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
        if (button == TrackedMouseButton.Right)
        {
            _rightButtonDownSwallowed = false;
        }
    }

    private static bool ShouldDelayRightButtonClick()
    {
        return _settings.RightButtonLongPress ||
               _settings.RightButtonDrag ||
               (_settings.CtrlRightClick && IsControlDown());
    }

    private static void StartLongPressTimer()
    {
        ReloadSettings();
        _longPressTimer?.Stop();
        _longPressTimer?.Start();
        HostAssets.AppendLog($"Input hook: long press timer started for {_trackedButton}, interval={Math.Clamp(_settings.LongPressMilliseconds, 150, 2000)}ms.");
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

    private static bool EndTracking(TrackedMouseButton button)
    {
        if (_trackedButton != button)
        {
            return false;
        }

        _longPressTimer?.Stop();
        var swallowRelease = button == TrackedMouseButton.Right && _releaseShouldExecute;
        if (_releaseShouldExecute && _settings.ExecuteOnButtonRelease)
        {
            HostAssets.AppendLog($"Input hook: {button} released after trigger.");
            System.Windows.Application.Current.Dispatcher.Invoke(() => _onLongPressRelease?.Invoke());
        }

        _dragTriggered = false;
        _releaseShouldExecute = false;
        if (button == TrackedMouseButton.Right)
        {
            _rightButtonDownSwallowed = false;
        }

        _trackedButton = TrackedMouseButton.None;
        return swallowRelease;
    }

    private static void ReplayShortRightClickAfterHookReturns()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            SendSyntheticRightClick();
        });
    }

    private static void SendSyntheticRightClick()
    {
        var inputs = new[]
        {
            MouseInput(MOUSEEVENTF_RIGHTDOWN),
            MouseInput(MOUSEEVENTF_RIGHTUP)
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        HostAssets.AppendLog($"Input hook: replayed short right click, SendInput sent={sent}/2.");
    }

    private static INPUT MouseInput(uint flags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags
                }
            }
        };
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
