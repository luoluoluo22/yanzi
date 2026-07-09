using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public class InputHookService
{
    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
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
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_CONTROL = 0x11;
    private const int VK_CAPITAL = 0x14;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int XBUTTON1 = 1;
    private const int XBUTTON2 = 2;
    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint LLKHF_INJECTED = 0x00000010;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    private static LowLevelMouseProc _mouseProc = MouseHookCallback;
    private static LowLevelKeyboardProc _keyboardProc = KeyboardHookCallback;
    private static IntPtr _mouseHookID = IntPtr.Zero;
    private static IntPtr _keyboardHookID = IntPtr.Zero;
    private static DispatcherTimer? _longPressTimer;
    private static Action? _onLongPressRelease;
    private static Action? _onRadialRelease;
    public static event Action? OnGlobalMouseDown;
    private static Action? _onShowPanel;
    private static Action? _onShowRadial;
    private static Action? _onShowYanm;
    private static Action? _onYanmRelease;
    private static Func<bool>? _onShowWindowSnap;
    private static Action? _onWindowSnapMove;
    private static Action? _onWindowSnapRelease;
    private static QuickPanelMouseTriggerSettings _settings = new();
    private static RadialMenuSettings _radialSettings = new();
    private static YanmSettings _yanmSettings = new();
    private static bool _windowSnapAssistEnabled;
    private static string _windowSnapAssistMouseTriggerMode = MouseTriggerModes.None;
    private static bool _isEnabled;
    private static bool _dragTriggered;
    private static bool _releaseShouldExecute;
    private static bool _rightButtonDownSwallowed;
    private static bool _middleButtonDownSwallowed;
    private static ActiveTriggerTarget _activeTriggerTarget = ActiveTriggerTarget.None;
    private static ActiveTriggerTarget _pendingLongPressTarget = ActiveTriggerTarget.None;
    private static bool _capsRadialActive;
    private static bool _rightButtonDown;
    private static bool _middleButtonDown;
    private static bool _x1ButtonDown;
    private static bool _x2ButtonDown;
    private static TrackedMouseButton _trackedButton = TrackedMouseButton.None;
    private static POINT _downPoint;
    private static long _lastMouseTriggerReleaseTick;
    private static long _capsLockDownTick;
    private static bool _capsLockUsed;

    public static bool IsRunning => _isEnabled;

    public static bool HasActiveMouseTrigger =>
        _dragTriggered || _releaseShouldExecute || _activeTriggerTarget != ActiveTriggerTarget.None;

    public static bool WasMouseTriggerReleasedRecently(int milliseconds = 250)
    {
        var tick = _lastMouseTriggerReleaseTick;
        return tick > 0 && Environment.TickCount64 - tick <= milliseconds;
    }

    public static void MarkCapsLockAsUsed()
    {
        _capsLockUsed = true;
    }

    public static string GetMouseStateSummary()
    {
        var pressed = new List<string>();
        if (_rightButtonDown) pressed.Add("右键");
        if (_middleButtonDown) pressed.Add("中键");
        if (_x1ButtonDown) pressed.Add("侧键1");
        if (_x2ButtonDown) pressed.Add("侧键2");
        var tracked = _trackedButton == TrackedMouseButton.None ? "无" : _trackedButton.ToString();
        var active = _activeTriggerTarget == ActiveTriggerTarget.None ? "无" : _activeTriggerTarget.ToString();
        return $"鼠标钩子={(IsRunning ? "运行" : "停止")}，按下={pressed.Count switch { 0 => "无", _ => string.Join("、", pressed) }}，跟踪={tracked}，触发={active}，右键拦截={_rightButtonDownSwallowed}";
    }

    public static void ResetMouseState(string reason = "tray")
    {
        ResetTransientMouseState();
        HostAssets.AppendLog($"Input hook: mouse state reset requested from {reason}.");
    }

    public static void Start(
        Action onLongPress,
        Action? onLongPressRelease = null,
        Action? onRadial = null,
        Action? onRadialRelease = null,
        Action? onShowYanm = null,
        Action? onYanmRelease = null,
        Func<bool>? onShowWindowSnap = null,
        Action? onWindowSnapMove = null,
        Action? onWindowSnapRelease = null)
    {
        if (_isEnabled)
        {
            HostAssets.AppendLog("Input hook: start skipped because hook is already running.");
            return;
        }
        
        _onShowPanel = onLongPress;
        _onLongPressRelease = onLongPressRelease;
        _onShowRadial = onRadial;
        _onRadialRelease = onRadialRelease;
        _onShowYanm = onShowYanm;
        _onYanmRelease = onYanmRelease;
        _onShowWindowSnap = onShowWindowSnap;
        _onWindowSnapMove = onWindowSnapMove;
        _onWindowSnapRelease = onWindowSnapRelease;
        ReloadSettings();
        _mouseHookID = SetMouseHook(_mouseProc);
        if (_mouseHookID == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            HostAssets.AppendLog($"Input hook: failed to install low level mouse hook, lastError={error}.");
            return;
        }

        _keyboardHookID = SetKeyboardHook(_keyboardProc);
        if (_keyboardHookID == IntPtr.Zero)
        {
            HostAssets.AppendLog($"Input hook: failed to install low level keyboard hook, lastError={Marshal.GetLastWin32Error()}; CapsLock radial trigger disabled for this session.");
        }
        
        _longPressTimer = new DispatcherTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.LongPressMilliseconds, 50, 1500));
        _longPressTimer.Tick += (s, e) =>
        {
            _longPressTimer.Stop();
            if (_trackedButton == TrackedMouseButton.None || _pendingLongPressTarget == ActiveTriggerTarget.None)
            {
                HostAssets.AppendLog($"Input hook: ignored long press tick because state is inactive, tracked={_trackedButton}, target={_pendingLongPressTarget}.");
                _pendingLongPressTarget = ActiveTriggerTarget.None;
                return;
            }

            _dragTriggered = true;
            _releaseShouldExecute = true;
            _activeTriggerTarget = _pendingLongPressTarget;
            _pendingLongPressTarget = ActiveTriggerTarget.None;
            HostAssets.AppendLog($"Input hook: {_trackedButton} long press triggered for {_activeTriggerTarget}.");
            
            // Invoke the appropriate show method based on the target
            if (_activeTriggerTarget == ActiveTriggerTarget.Radial)
            {
                InvokeShowRadial();
            }
            else if (_activeTriggerTarget == ActiveTriggerTarget.Yanm)
            {
                InvokeShowYanm();
            }
            else
            {
                InvokeShowPanel();
            }
        };

        _isEnabled = true;
        HostAssets.AppendLog($"Input hook: started. mouseHook=0x{_mouseHookID.ToInt64():X}, keyboardHook=0x{_keyboardHookID.ToInt64():X}, triggers={DescribeSettings()}.");
    }

    public static void Stop()
    {
        if (!_isEnabled)
        {
            ResetTransientMouseState();
            return;
        }

        var mouseUnhooked = _mouseHookID == IntPtr.Zero || UnhookWindowsHookEx(_mouseHookID);
        var keyboardUnhooked = _keyboardHookID == IntPtr.Zero || UnhookWindowsHookEx(_keyboardHookID);
        HostAssets.AppendLog($"Input hook: stopped. mouseUnhooked={mouseUnhooked}, keyboardUnhooked={keyboardUnhooked}.");
        _longPressTimer?.Stop();
        _mouseHookID = IntPtr.Zero;
        _keyboardHookID = IntPtr.Zero;
        ResetTransientMouseState();
        _isEnabled = false;
    }

    public static void ReloadSettings()
    {
        var appSettings = AppSettingsStore.Load();
        _settings = appSettings.QuickPanelMouseTriggers ?? new QuickPanelMouseTriggerSettings();
        _radialSettings = appSettings.RadialMenu ?? new RadialMenuSettings();
        _yanmSettings = appSettings.Yanm ?? new YanmSettings();
        _windowSnapAssistEnabled = appSettings.EnableWindowSnapAssist;
        _windowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(appSettings.WindowSnapAssistMouseTriggerMode);
        if (_longPressTimer != null)
        {
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.LongPressMilliseconds, 50, 1500));
        }

        ResetTransientMouseState();
        HostAssets.AppendLog($"Input hook: settings reloaded, transient mouse state reset, triggers={DescribeSettings()}.");
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc)
    {
        return SetHook(WH_MOUSE_LL, proc);
    }

    private static IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
    {
        return SetHook(WH_KEYBOARD_LL, proc);
    }

    private static IntPtr SetHook<TDelegate>(int hookType, TDelegate proc) where TDelegate : Delegate
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            var moduleHandle = GetModuleHandle(curModule.ModuleName);
            var hook = SetWindowsHookEx(hookType, proc, moduleHandle, 0);
            if (hook != IntPtr.Zero)
            {
                return hook;
            }

            var firstError = Marshal.GetLastWin32Error();
            HostAssets.AppendLog($"Input hook: SetWindowsHookEx failed with module handle, type={hookType}, module={curModule.ModuleName}, hMod=0x{moduleHandle.ToInt64():X}, lastError={firstError}; retrying with hMod=0.");
            return SetWindowsHookEx(hookType, proc, IntPtr.Zero, 0);
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
        if (_settings.MiddleButtonDrag) enabled.Add($"MiddleDrag:{_settings.DragThresholdPixels}px");
        if (_radialSettings.Enabled && MouseTriggerModes.Normalize(_radialSettings.MouseTriggerMode) != MouseTriggerModes.None) enabled.Add($"Radial:{_radialSettings.MouseTriggerMode}");
        if (IsRadialKeyboardHoldEnabled()) enabled.Add($"RadialKey:{RadialActivationKeys.Normalize(_radialSettings.ActivationKey)}");
        if (_yanmSettings.Enabled && MouseTriggerModes.Normalize(_yanmSettings.MouseTriggerMode) != MouseTriggerModes.None) enabled.Add($"Yanm:{_yanmSettings.MouseTriggerMode}");
        if (_windowSnapAssistEnabled && _windowSnapAssistMouseTriggerMode != MouseTriggerModes.None) enabled.Add($"WindowSnap:{_windowSnapAssistMouseTriggerMode}");
        if (_settings.HorizontalWheel) enabled.Add("HorizontalWheel");
        if (_settings.ExecuteOnButtonRelease) enabled.Add("ReleaseExec");
        return enabled.Count == 0 ? "none" : string.Join(",", enabled);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var mouse = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((mouse.flags & LLMHF_INJECTED) != 0)
            {
                return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
            }

            if (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN || message == WM_MBUTTONDOWN)
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        OnGlobalMouseDown?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        HostAssets.AppendLog($"Error in OnGlobalMouseDown: {ex.Message}");
                    }
                }));
            }

            if (message == WM_LBUTTONDOWN)
            {
                if (_settings.CtrlLeftClick && IsControlDown())
                {
                    HostAssets.AppendLog("Input hook: Ctrl+left click triggered.");
                    InvokeShowPanel();
                }
                else if (IsControlDown() && TryTriggerMouseMode(MouseTriggerModes.CtrlLeftClick, mouse.pt))
                {
                    return (IntPtr)1;
                }
            }
            else if (message == WM_RBUTTONDOWN)
            {
                _rightButtonDown = true;
                BeginTracking(TrackedMouseButton.Right, mouse.pt);
                HostAssets.AppendLog($"Input hook: right button down, rightLong={_settings.RightButtonLongPress}, rightDrag={_settings.RightButtonDrag}, radialTrigger={_radialSettings.MouseTriggerMode}, radialRightDrag={_radialSettings.TriggerRightButtonDrag}, yanmTrigger={_yanmSettings.MouseTriggerMode}, yanmRightDrag={_yanmSettings.TriggerRightButtonDrag}, ctrlRight={_settings.CtrlRightClick}, ctrlDown={IsControlDown()}, pt=({mouse.pt.x},{mouse.pt.y}).");
                _rightButtonDownSwallowed = ShouldDelayRightButtonClick();
                if (_settings.CtrlRightClick && IsControlDown())
                {
                    _releaseShouldExecute = true;
                    _activeTriggerTarget = ActiveTriggerTarget.Panel;
                    HostAssets.AppendLog("Input hook: Ctrl+right click triggered.");
                    InvokeShowPanel();
                }
                else if (IsControlDown() && TryTriggerMouseMode(MouseTriggerModes.CtrlRightClick, mouse.pt))
                {
                    return (IntPtr)1;
                }
                else if (ShouldStartLongPress(TrackedMouseButton.Right))
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
                _middleButtonDown = true;
                BeginTracking(TrackedMouseButton.Middle, mouse.pt);
                HostAssets.AppendLog($"Input hook: middle button down, middleDown={_settings.MiddleButtonDown}, middleLong={_settings.MiddleButtonLongPress}, ctrlMiddle={_settings.CtrlMiddleClick}, ctrlDown={IsControlDown()}, pt=({mouse.pt.x},{mouse.pt.y}).");
                
                // Check Ctrl+Middle first
                if (_settings.CtrlMiddleClick && IsControlDown())
                {
                    _releaseShouldExecute = true;
                    _activeTriggerTarget = ActiveTriggerTarget.Panel;
                    HostAssets.AppendLog("Input hook: Ctrl+middle click triggered for panel.");
                    InvokeShowPanel();
                }
                else if (IsControlDown() && TryTriggerMouseMode(MouseTriggerModes.CtrlMiddleClick, mouse.pt))
                {
                    _middleButtonDownSwallowed = true;
                    return (IntPtr)1;
                }
                // Check middle button down triggers
                else if (_settings.MiddleButtonDown)
                {
                    _releaseShouldExecute = true;
                    _activeTriggerTarget = ActiveTriggerTarget.Panel;
                    HostAssets.AppendLog("Input hook: middle button down triggered for panel.");
                    InvokeShowPanel();
                }
                else if (TryTriggerMouseMode(MouseTriggerModes.MiddleDown, mouse.pt))
                {
                    _middleButtonDownSwallowed = true;
                    return (IntPtr)1;
                }
                else if (ShouldStartLongPress(TrackedMouseButton.Middle))
                {
                    StartLongPressTimer();
                }
            }
            else if (message == WM_XBUTTONDOWN)
            {
                var xButton = GetXButton(mouse.mouseData);
                if (xButton == XBUTTON1)
                {
                    _x1ButtonDown = true;
                    BeginTracking(TrackedMouseButton.X1, mouse.pt);
                    if (_settings.X1ButtonDown)
                    {
                        _releaseShouldExecute = true;
                        _activeTriggerTarget = ActiveTriggerTarget.Panel;
                        HostAssets.AppendLog("Input hook: X1 button down triggered.");
                        InvokeShowPanel();
                    }
                    else if (TryTriggerMouseMode(MouseTriggerModes.X1Down, mouse.pt))
                    {
                        return (IntPtr)1;
                    }
                }
                else if (xButton == XBUTTON2)
                {
                    _x2ButtonDown = true;
                    BeginTracking(TrackedMouseButton.X2, mouse.pt);
                    if (_settings.X2ButtonDown)
                    {
                        _releaseShouldExecute = true;
                        _activeTriggerTarget = ActiveTriggerTarget.Panel;
                        HostAssets.AppendLog("Input hook: X2 button down triggered.");
                        InvokeShowPanel();
                    }
                    else if (TryTriggerMouseMode(MouseTriggerModes.X2Down, mouse.pt))
                    {
                        return (IntPtr)1;
                    }
                }
            }
            else if (message == WM_MOUSEMOVE)
            {
                HandleMouseMove(mouse.pt);
            }
            else if (message == WM_RBUTTONUP)
            {
                _rightButtonDown = false;
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
                _middleButtonDown = false;
                HostAssets.AppendLog($"Input hook: middle button up, tracked={_trackedButton}, releaseShouldExecute={_releaseShouldExecute}.");
                if (EndTracking(TrackedMouseButton.Middle))
                {
                    return (IntPtr)1;
                }
            }
            else if (message == WM_XBUTTONUP)
            {
                var xButton = GetXButton(mouse.mouseData);
                if (xButton == XBUTTON1)
                {
                    _x1ButtonDown = false;
                }
                else if (xButton == XBUTTON2)
                {
                    _x2ButtonDown = false;
                }

                if (EndTracking(xButton == XBUTTON1 ? TrackedMouseButton.X1 : TrackedMouseButton.X2))
                {
                    return (IntPtr)1;
                }
            }
            else if (message == WM_MOUSEHWHEEL && _settings.HorizontalWheel)
            {
                HostAssets.AppendLog("Input hook: horizontal wheel triggered.");
                InvokeShowPanel();
            }
            else if (message == WM_MOUSEHWHEEL && TryTriggerMouseMode(MouseTriggerModes.HorizontalWheel, mouse.pt))
            {
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var keyboard = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((keyboard.flags & LLKHF_INJECTED) != 0)
            {
                return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
            }

            if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) &&
                IsRadialActivationKey(keyboard.vkCode) &&
                IsRadialKeyboardHoldEnabled())
            {
                if (!_capsRadialActive)
                {
                    _capsRadialActive = true;
                    _releaseShouldExecute = true;
                    _activeTriggerTarget = ActiveTriggerTarget.Radial;
                    if (keyboard.vkCode == VK_CAPITAL)
                    {
                        _capsLockDownTick = Environment.TickCount64;
                        _capsLockUsed = false;
                    }
                    HostAssets.AppendLog($"Input hook: {RadialActivationKeys.Normalize(_radialSettings.ActivationKey)} hold radial triggered.");
                    InvokeShowRadial();
                }

                return (IntPtr)1;
            }

            if ((message == WM_KEYUP || message == WM_SYSKEYUP) &&
                IsRadialActivationKey(keyboard.vkCode) &&
                _capsRadialActive)
            {
                _capsRadialActive = false;
                _releaseShouldExecute = false;
                HostAssets.AppendLog($"Input hook: {RadialActivationKeys.Normalize(_radialSettings.ActivationKey)} hold radial released.");
                System.Windows.Application.Current.Dispatcher.Invoke(() => _onRadialRelease?.Invoke());
                _activeTriggerTarget = ActiveTriggerTarget.None;

                if (keyboard.vkCode == VK_CAPITAL && !_capsLockUsed && (Environment.TickCount64 - _capsLockDownTick < 350))
                {
                    SendSyntheticCapsLock();
                }

                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
    }

    private static void SendSyntheticCapsLock()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            var inputs = new[]
            {
                new INPUT
                {
                    type = 1, // INPUT_KEYBOARD
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)VK_CAPITAL,
                            wScan = 0,
                            dwFlags = 0, // Key down
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                },
                new INPUT
                {
                    type = 1, // INPUT_KEYBOARD
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)VK_CAPITAL,
                            wScan = 0,
                            dwFlags = 2, // KEYEVENTF_KEYUP
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                }
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            HostAssets.AppendLog("Input hook: simulated CapsLock keypress for toggle.");
        });
    }

    private static void BeginTracking(TrackedMouseButton button, POINT point)
    {
        _trackedButton = button;
        _downPoint = point;
        _dragTriggered = false;
        _releaseShouldExecute = false;
        _pendingLongPressTarget = ActiveTriggerTarget.None;
        if (button == TrackedMouseButton.Right)
        {
            _rightButtonDownSwallowed = false;
        }
        else if (button == TrackedMouseButton.Middle)
        {
            _middleButtonDownSwallowed = false;
        }
    }

    private static bool ShouldDelayRightButtonClick()
    {
        if (MouseGestureService.HasRightDragRegistrations)
        {
            return false;
        }

        return _settings.RightButtonLongPress ||
               _settings.RightButtonDrag ||
               IsRadialRightDragEnabled() ||
               IsYanmRightDragEnabled() ||
               (_settings.CtrlRightClick && IsControlDown());
    }

    private static void StartLongPressTimer()
    {
        _longPressTimer?.Stop();
        _pendingLongPressTarget = ResolveLongPressTarget(_trackedButton);
        if (_pendingLongPressTarget == ActiveTriggerTarget.None)
        {
            HostAssets.AppendLog($"Input hook: long press timer skipped for {_trackedButton}, target=None.");
            return;
        }

        if (_longPressTimer != null)
        {
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.LongPressMilliseconds, 50, 1500));
        }

        _longPressTimer?.Start();
        HostAssets.AppendLog($"Input hook: long press timer started for {_trackedButton}, target={_pendingLongPressTarget}, interval={Math.Clamp(_settings.LongPressMilliseconds, 50, 1500)}ms.");
    }

    private static void HandleMouseMove(POINT point)
    {
        CancelLongPressOnMovement(point);

        if (_activeTriggerTarget == ActiveTriggerTarget.WindowSnap && _dragTriggered)
        {
            InvokeWindowSnapMove();
            return;
        }

        if (MouseGestureService.HasRightDragRegistrations && _trackedButton == TrackedMouseButton.Right)
        {
            return;
        }

        if (MouseGestureService.HasMiddleDragRegistrations && _trackedButton == TrackedMouseButton.Middle)
        {
            return;
        }

        if (_trackedButton is not (TrackedMouseButton.Right or TrackedMouseButton.Middle) ||
            _dragTriggered ||
            _activeTriggerTarget != ActiveTriggerTarget.None)
        {
            return;
        }

        var mode = _trackedButton == TrackedMouseButton.Middle
            ? MouseTriggerModes.MiddleDrag
            : MouseTriggerModes.RightDrag;

        var panelDrag = mode == MouseTriggerModes.MiddleDrag ? _settings.MiddleButtonDrag : _settings.RightButtonDrag;
        var yanmDrag = (mode == MouseTriggerModes.MiddleDrag
                ? _yanmSettings.TriggerMiddleButtonDrag || IsMouseTriggerModeActive(MouseTriggerModes.MiddleDrag, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled)
                : _yanmSettings.TriggerRightButtonDrag || IsMouseTriggerModeActive(MouseTriggerModes.RightDrag, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled)) &&
                       IsTriggerAllowedForTarget(ActiveTriggerTarget.Yanm, logBlocked: false);
        var radialDrag = (mode == MouseTriggerModes.MiddleDrag
                ? _radialSettings.TriggerMiddleButtonDrag || IsMouseTriggerModeActive(MouseTriggerModes.MiddleDrag, _radialSettings.MouseTriggerMode, _radialSettings.Enabled)
                : _radialSettings.TriggerRightButtonDrag || IsMouseTriggerModeActive(MouseTriggerModes.RightDrag, _radialSettings.MouseTriggerMode, _radialSettings.Enabled)) &&
                         IsTriggerAllowedForTarget(ActiveTriggerTarget.Radial, logBlocked: false);
        var windowSnapDrag = _windowSnapAssistEnabled &&
                             _onShowWindowSnap != null &&
                             string.Equals(_windowSnapAssistMouseTriggerMode, mode, StringComparison.OrdinalIgnoreCase);
        if (!radialDrag && !yanmDrag && !panelDrag && !windowSnapDrag)
        {
            return;
        }

        var threshold = yanmDrag
            ? Math.Clamp(_yanmSettings.DragThresholdPixels, 8, 120)
            : radialDrag
                ? Math.Clamp(_radialSettings.DragThresholdPixels, 8, 120)
                : Math.Clamp(_settings.DragThresholdPixels, 8, 120);
        var dx = point.x - _downPoint.x;
        var dy = point.y - _downPoint.y;
        var distanceSquared = (dx * dx) + (dy * dy);

        if ((dx * dx) + (dy * dy) < threshold * threshold)
        {
            return;
        }

        _dragTriggered = true;
        _longPressTimer?.Stop();
        if (panelDrag)
        {
            _releaseShouldExecute = true;
            _activeTriggerTarget = ActiveTriggerTarget.Panel;
            HostAssets.AppendLog($"Input hook: {mode} triggered for mouse panel.");
            InvokeShowPanel();
        }
        else if (radialDrag && IsTriggerAllowedForTarget(ActiveTriggerTarget.Radial))
        {
            _releaseShouldExecute = true;
            _activeTriggerTarget = ActiveTriggerTarget.Radial;
            HostAssets.AppendLog($"Input hook: radial {mode} triggered.");
            InvokeShowRadial();
        }
        else if (yanmDrag && IsTriggerAllowedForTarget(ActiveTriggerTarget.Yanm))
        {
            _releaseShouldExecute = true;
            _activeTriggerTarget = ActiveTriggerTarget.Yanm;
            HostAssets.AppendLog($"Input hook: Yanm {mode} triggered.");
            InvokeShowYanm();
        }
        else if (windowSnapDrag && InvokeShowWindowSnap())
        {
            _releaseShouldExecute = true;
            _activeTriggerTarget = ActiveTriggerTarget.WindowSnap;
            HostAssets.AppendLog($"Input hook: window snap {mode} triggered.");
            InvokeWindowSnapMove();
        }
    }

    private static void CancelLongPressOnMovement(POINT point)
    {
        if (_longPressTimer?.IsEnabled != true || _trackedButton == TrackedMouseButton.None)
        {
            return;
        }

        var dx = point.x - _downPoint.x;
        var dy = point.y - _downPoint.y;
        const int dragIntentPixels = 6;
        if ((dx * dx) + (dy * dy) < dragIntentPixels * dragIntentPixels)
        {
            return;
        }

        _longPressTimer.Stop();
        _pendingLongPressTarget = ActiveTriggerTarget.None;
        HostAssets.AppendLog($"Input hook: canceled {_trackedButton} long press because mouse moved.");
    }

    private static bool EndTracking(TrackedMouseButton button)
    {
        if (_trackedButton != button)
        {
            return false;
        }

        _longPressTimer?.Stop();
        var swallowRelease = (button is TrackedMouseButton.Right or TrackedMouseButton.Middle or TrackedMouseButton.X1 or TrackedMouseButton.X2) && _releaseShouldExecute;
        var downSwallowedByMouseGesture = button switch
        {
            TrackedMouseButton.Right => MouseGestureService.HasRightDragRegistrations,
            TrackedMouseButton.Middle => MouseGestureService.HasMiddleDragRegistrations,
            _ => false
        };
        var shouldReplaySwallowedRelease = swallowRelease &&
            !downSwallowedByMouseGesture &&
            ((button == TrackedMouseButton.Right && !_rightButtonDownSwallowed) ||
             (button == TrackedMouseButton.Middle && !_middleButtonDownSwallowed));
        if (_releaseShouldExecute && _settings.ExecuteOnButtonRelease)
        {
            _lastMouseTriggerReleaseTick = Environment.TickCount64;
            HostAssets.AppendLog($"Input hook: {button} released after trigger.");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_activeTriggerTarget == ActiveTriggerTarget.Radial)
                {
                    _onRadialRelease?.Invoke();
                }
                else if (_activeTriggerTarget == ActiveTriggerTarget.Yanm)
                {
                    _onYanmRelease?.Invoke();
                }
                else if (_activeTriggerTarget == ActiveTriggerTarget.WindowSnap)
                {
                    _onWindowSnapRelease?.Invoke();
                }
                else
                {
                    _onLongPressRelease?.Invoke();
                }
            });
        }

        if (shouldReplaySwallowedRelease)
        {
            ReplayMouseButtonUpAfterHookReturns(button);
        }
        else if (swallowRelease && downSwallowedByMouseGesture)
        {
            HostAssets.AppendLog($"Input hook: skipped replaying {button} button up because mouse gesture hook swallowed the button down.");
        }

        _dragTriggered = false;
        _releaseShouldExecute = false;
        _activeTriggerTarget = ActiveTriggerTarget.None;
        _pendingLongPressTarget = ActiveTriggerTarget.None;
        if (button == TrackedMouseButton.Right)
        {
            _rightButtonDownSwallowed = false;
        }
        else if (button == TrackedMouseButton.Middle)
        {
            _middleButtonDownSwallowed = false;
        }

        _trackedButton = TrackedMouseButton.None;
        return swallowRelease;
    }

    private static void ResetTransientMouseState()
    {
        _longPressTimer?.Stop();
        _dragTriggered = false;
        _releaseShouldExecute = false;
        _rightButtonDownSwallowed = false;
        _middleButtonDownSwallowed = false;
        _activeTriggerTarget = ActiveTriggerTarget.None;
        _pendingLongPressTarget = ActiveTriggerTarget.None;
        _capsRadialActive = false;
        _rightButtonDown = false;
        _middleButtonDown = false;
        _x1ButtonDown = false;
        _x2ButtonDown = false;
        _trackedButton = TrackedMouseButton.None;
        _downPoint = default;
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

    private static void ReplayMouseButtonUpAfterHookReturns(TrackedMouseButton button)
    {
        var flags = button switch
        {
            TrackedMouseButton.Right => MOUSEEVENTF_RIGHTUP,
            TrackedMouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
            _ => 0u
        };
        if (flags == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            var sent = SendInput(1, [MouseInput(flags)], Marshal.SizeOf<INPUT>());
            HostAssets.AppendLog($"Input hook: replayed {button} button up after swallowed release, SendInput sent={sent}/1.");
        });
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

    private static void InvokeShowRadial()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _onShowRadial?.Invoke());
    }

    private static void InvokeShowYanm()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _onShowYanm?.Invoke());
    }

    private static bool InvokeShowWindowSnap()
    {
        return System.Windows.Application.Current.Dispatcher.Invoke(() => _onShowWindowSnap?.Invoke() == true);
    }

    private static void InvokeWindowSnapMove()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _onWindowSnapMove?.Invoke());
    }

    private static void InvokeWindowSnapRelease()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _onWindowSnapRelease?.Invoke());
    }

    private static bool IsMouseTriggerModeActive(string mode, string selectedMode, bool enabled)
    {
        return enabled &&
               string.Equals(MouseTriggerModes.Normalize(selectedMode), mode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTriggerAllowedForTarget(ActiveTriggerTarget target, bool logBlocked = true)
    {
        var processName = GetForegroundProcessName();
        var (whitelist, blacklist) = target switch
        {
            ActiveTriggerTarget.Radial => (_radialSettings.WhitelistedProcesses ?? [], _radialSettings.BlacklistedProcesses ?? []),
            ActiveTriggerTarget.Yanm => (_yanmSettings.WhitelistedProcesses ?? [], _yanmSettings.BlacklistedProcesses ?? []),
            _ => (new List<string>(), new List<string>())
        };

        if (whitelist.Count == 0 && blacklist.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return whitelist.Count == 0;
        }

        if (whitelist.Count > 0)
        {
            var allowed = whitelist.Any(item => ProcessNameMatches(processName, item));
            if (!allowed && logBlocked)
            {
                HostAssets.AppendLog($"Input hook: {target} trigger blocked by whitelist, process={processName}.");
            }

            return allowed;
        }

        var blocked = blacklist.Any(item => ProcessNameMatches(processName, item));
        if (blocked && logBlocked)
        {
            HostAssets.AppendLog($"Input hook: {target} trigger blocked by blacklist, process={processName}.");
        }

        return !blocked;
    }

    private static bool ProcessNameMatches(string processName, string pattern)
    {
        var normalizedProcess = NormalizeProcessName(processName);
        var normalizedPattern = NormalizeProcessName(pattern);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            return false;
        }

        if (normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            var parts = normalizedPattern.Split('*', StringSplitOptions.RemoveEmptyEntries);
            var index = 0;
            foreach (var part in parts)
            {
                var found = normalizedProcess.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    return false;
                }

                index = found + part.Length;
            }

            return true;
        }

        return normalizedProcess.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string value)
    {
        value = (value ?? string.Empty).Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            var className = new System.Text.StringBuilder(256);
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
            return processId == 0 ? string.Empty : Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsRadialRightDragEnabled() =>
        (_radialSettings.TriggerRightButtonDrag || IsMouseTriggerModeActive(MouseTriggerModes.RightDrag, _radialSettings.MouseTriggerMode, _radialSettings.Enabled)) && _onShowRadial != null;

    private static bool IsYanmRightDragEnabled() =>
        (_yanmSettings.TriggerRightButtonDrag || IsMouseTriggerModeActive(MouseTriggerModes.RightDrag, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled)) && _onShowYanm != null;

    private static bool TryTriggerMouseMode(string mode, POINT point)
    {
        // Check radial menu triggers (both old MouseTriggerMode and new boolean properties)
        if (_radialSettings.Enabled && _onShowRadial != null)
        {
            var radialTriggered = IsMouseTriggerModeActive(mode, _radialSettings.MouseTriggerMode, _radialSettings.Enabled) ||
                                  (mode == MouseTriggerModes.MiddleDown && _radialSettings.TriggerMiddleButtonDown) ||
                                  (mode == MouseTriggerModes.X1Down && _radialSettings.TriggerX1ButtonDown) ||
                                  (mode == MouseTriggerModes.X2Down && _radialSettings.TriggerX2ButtonDown) ||
                                  (mode == MouseTriggerModes.HorizontalWheel && _radialSettings.TriggerHorizontalWheel) ||
                                  (mode == MouseTriggerModes.CtrlLeftClick && _radialSettings.TriggerCtrlLeftClick) ||
                                  (mode == MouseTriggerModes.CtrlRightClick && _radialSettings.TriggerCtrlRightClick) ||
                                  (mode == MouseTriggerModes.MiddleLongPress && _radialSettings.TriggerMiddleButtonLongPress) ||
                                  (mode == MouseTriggerModes.RightLongPress && _radialSettings.TriggerRightButtonLongPress) ||
                                  (mode == MouseTriggerModes.RightDrag && _radialSettings.TriggerRightButtonDrag) ||
                                  (mode == MouseTriggerModes.MiddleDrag && _radialSettings.TriggerMiddleButtonDrag);
            
            if (radialTriggered && IsTriggerAllowedForTarget(ActiveTriggerTarget.Radial))
            {
                _releaseShouldExecute = true;
                _activeTriggerTarget = ActiveTriggerTarget.Radial;
                HostAssets.AppendLog($"Input hook: radial mouse mode triggered: {mode}, pt=({point.x},{point.y}).");
                InvokeShowRadial();
                return true;
            }
        }

        // Check Yanm triggers (both old MouseTriggerMode and new boolean properties)
        if (_yanmSettings.Enabled && _onShowYanm != null)
        {
            var yanmTriggered = IsMouseTriggerModeActive(mode, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled) ||
                                (mode == MouseTriggerModes.MiddleDown && _yanmSettings.TriggerMiddleButtonDown) ||
                                (mode == MouseTriggerModes.X1Down && _yanmSettings.TriggerX1ButtonDown) ||
                                (mode == MouseTriggerModes.X2Down && _yanmSettings.TriggerX2ButtonDown) ||
                                (mode == MouseTriggerModes.HorizontalWheel && _yanmSettings.TriggerHorizontalWheel) ||
                                (mode == MouseTriggerModes.CtrlLeftClick && _yanmSettings.TriggerCtrlLeftClick) ||
                                (mode == MouseTriggerModes.CtrlRightClick && _yanmSettings.TriggerCtrlRightClick) ||
                                (mode == MouseTriggerModes.MiddleLongPress && _yanmSettings.TriggerMiddleButtonLongPress) ||
                                (mode == MouseTriggerModes.RightLongPress && _yanmSettings.TriggerRightButtonLongPress) ||
                                (mode == MouseTriggerModes.RightDrag && _yanmSettings.TriggerRightButtonDrag) ||
                                (mode == MouseTriggerModes.MiddleDrag && _yanmSettings.TriggerMiddleButtonDrag);
            
            if (yanmTriggered && IsTriggerAllowedForTarget(ActiveTriggerTarget.Yanm))
            {
                _releaseShouldExecute = true;
                _activeTriggerTarget = ActiveTriggerTarget.Yanm;
                HostAssets.AppendLog($"Input hook: Yanm mouse mode triggered: {mode}, pt=({point.x},{point.y}).");
                InvokeShowYanm();
                return true;
            }
        }

        return false;
    }

    private static bool ShouldStartLongPress(TrackedMouseButton button)
    {
        return button == TrackedMouseButton.Right
            ? _settings.RightButtonLongPress ||
              _radialSettings.TriggerRightButtonLongPress ||
              _yanmSettings.TriggerRightButtonLongPress ||
              IsMouseTriggerModeActive(MouseTriggerModes.RightLongPress, _radialSettings.MouseTriggerMode, _radialSettings.Enabled) ||
              IsMouseTriggerModeActive(MouseTriggerModes.RightLongPress, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled)
            : button == TrackedMouseButton.Middle &&
              (_settings.MiddleButtonLongPress ||
               _radialSettings.TriggerMiddleButtonLongPress ||
               _yanmSettings.TriggerMiddleButtonLongPress ||
               IsMouseTriggerModeActive(MouseTriggerModes.MiddleLongPress, _radialSettings.MouseTriggerMode, _radialSettings.Enabled) ||
               IsMouseTriggerModeActive(MouseTriggerModes.MiddleLongPress, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled));
    }

    private static ActiveTriggerTarget ResolveLongPressTarget(TrackedMouseButton button)
    {
        if (button == TrackedMouseButton.Right)
        {
            if (_settings.RightButtonLongPress) return ActiveTriggerTarget.Panel;
            if ((_radialSettings.TriggerRightButtonLongPress || IsMouseTriggerModeActive(MouseTriggerModes.RightLongPress, _radialSettings.MouseTriggerMode, _radialSettings.Enabled)) &&
                IsTriggerAllowedForTarget(ActiveTriggerTarget.Radial)) return ActiveTriggerTarget.Radial;
            if ((_yanmSettings.TriggerRightButtonLongPress || IsMouseTriggerModeActive(MouseTriggerModes.RightLongPress, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled)) &&
                IsTriggerAllowedForTarget(ActiveTriggerTarget.Yanm)) return ActiveTriggerTarget.Yanm;
        }

        if (button == TrackedMouseButton.Middle)
        {
            if (_settings.MiddleButtonLongPress) return ActiveTriggerTarget.Panel;
            if ((_radialSettings.TriggerMiddleButtonLongPress || IsMouseTriggerModeActive(MouseTriggerModes.MiddleLongPress, _radialSettings.MouseTriggerMode, _radialSettings.Enabled)) &&
                IsTriggerAllowedForTarget(ActiveTriggerTarget.Radial)) return ActiveTriggerTarget.Radial;
            if ((_yanmSettings.TriggerMiddleButtonLongPress || IsMouseTriggerModeActive(MouseTriggerModes.MiddleLongPress, _yanmSettings.MouseTriggerMode, _yanmSettings.Enabled)) &&
                IsTriggerAllowedForTarget(ActiveTriggerTarget.Yanm)) return ActiveTriggerTarget.Yanm;
        }

        return ActiveTriggerTarget.None;
    }

    private static bool IsRadialKeyboardHoldEnabled() =>
        _radialSettings.Enabled &&
        _radialSettings.TriggerCapsLockHold &&
        !string.Equals(RadialActivationKeys.Normalize(_radialSettings.ActivationKey), RadialActivationKeys.None, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(RadialActivationKeys.Normalize(_radialSettings.ActivationKey), RadialActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) &&
        IsTriggerAllowedForTarget(ActiveTriggerTarget.Radial, logBlocked: false) &&
        _onShowRadial != null;

    private static bool IsRadialActivationKey(int vkCode)
    {
        return RadialActivationKeys.Normalize(_radialSettings.ActivationKey) switch
        {
            RadialActivationKeys.Win => vkCode is VK_LWIN or VK_RWIN,
            RadialActivationKeys.CapsLock => vkCode == VK_CAPITAL,
            _ => false
        };
    }

    private static bool IsControlDown() => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

    private static int GetXButton(uint mouseData) => (int)((mouseData >> 16) & 0xffff);

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
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

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

    private enum ActiveTriggerTarget
    {
        None,
        Panel,
        Radial,
        Yanm,
        WindowSnap
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
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public uint flags;
        public int time;
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
