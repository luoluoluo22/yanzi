using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public static class KeyboardDoubleTapService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkCapsLock = 0x14;
    private const uint LlkhfInjected = 0x00000010;

    private static readonly LowLevelKeyboardProc Proc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;
    private static Action<string>? _onDoubleTap;
    private static Action? _onWinHold;
    private static Action? _onWinRelease;
    private static Action? _onWinDoubleTap;
    private static ModifierTapKind _lastTapKind = ModifierTapKind.None;
    private static long _lastTapTimestamp;
    private static long _lastWinTapTimestamp;
    private static long _yanmTriggerDownTimestamp;
    private static bool _sequenceDirty;
    private static bool _leftCtrlDown;
    private static bool _rightCtrlDown;
    private static bool _leftAltDown;
    private static bool _rightAltDown;
    private static bool _leftShiftDown;
    private static bool _rightShiftDown;
    private static bool _leftWinDown;
    private static bool _rightWinDown;
    private static bool _doubleCtrlEnabled = true;
    private static bool _doubleAltEnabled = true;
    private static bool _suppressCurrentAltTap;
    private static bool _winOverlayActive;
    private static bool _winOverlayEnabled = true;
    private static bool _winHoldEnabled = true;
    private static bool _winDoubleTapEnabled = true;
    private static bool _hasReleasedWinForYanmHold;
    private static string _yanmActivationKey = YanmActivationKeys.Win;
    private static bool _capsLockDown;
    private static bool _capsLockUsedForLauncher;
    private static long _capsLockDownTimestamp;

    private static void SendSyntheticCapsLockToggle()
    {
        const uint keyEventKeyUp = 0x0002;
        keybd_event((byte)VkCapsLock, 0x45, 0, UIntPtr.Zero);
        keybd_event((byte)VkCapsLock, 0x45, keyEventKeyUp, UIntPtr.Zero);
    }

    private static bool IsLauncherWindowActive()
    {
        var mainWindow = MainWindow.Instance;
        if (mainWindow == null) return false;
        try
        {
            return mainWindow.Dispatcher.Invoke(() => mainWindow.IsVisible && mainWindow.IsActive && mainWindow.WindowState != WindowState.Minimized);
        }
        catch
        {
            return false;
        }
    }

    private static void DispatchLauncherCapsAction(MainWindow.LauncherCapsAction action, string key)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            MainWindow.Instance?.HandleCapsNavigation(action);
            MainWindow.Instance?.FlashGuideKey(key);
        });
    }

    private static void DispatchCapsGuideState(bool isCapsDown, string? activeKey)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            MainWindow.Instance?.SetCapsGuideState(isCapsDown, activeKey);
        });
    }

    public static bool IsRunning => _hookId != IntPtr.Zero;
    public static bool IsYanmTriggerHeld => _winOverlayActive;

    public static string GetKeyboardStateSummary()
    {
        var pressed = new List<string>();
        if (_leftCtrlDown) pressed.Add("左Ctrl");
        if (_rightCtrlDown) pressed.Add("右Ctrl");
        if (_leftAltDown) pressed.Add("左Alt");
        if (_rightAltDown) pressed.Add("右Alt");
        if (_leftShiftDown) pressed.Add("左Shift");
        if (_rightShiftDown) pressed.Add("右Shift");
        if (_leftWinDown) pressed.Add("左Win");
        if (_rightWinDown) pressed.Add("右Win");
        if (_winOverlayActive) pressed.Add("燕幕触发键");
        return $"键盘钩子={(IsRunning ? "运行" : "停止")}，按下={pressed.Count switch { 0 => "无", _ => string.Join("、", pressed) }}";
    }

    public static void Start(
        Action<string> onDoubleTap,
        Action? onWinHold = null,
        Action? onWinRelease = null,
        Action? onWinDoubleTap = null)
    {
        if (IsRunning)
        {
            HostAssets.AppendLog("Keyboard double tap: start skipped because hook is already running.");
            return;
        }

        _onDoubleTap = onDoubleTap;
        _onWinHold = onWinHold;
        _onWinRelease = onWinRelease;
        _onWinDoubleTap = onWinDoubleTap;
        _sequenceDirty = false;
        _lastTapKind = ModifierTapKind.None;
        _lastTapTimestamp = 0;
        _lastWinTapTimestamp = 0;
        ApplyConfiguredShortcut(AppSettingsStore.Load().LauncherHotkey);
        ApplyYanmSettings(AppSettingsStore.Load().Yanm);
        _hookId = SetHook(Proc);
        if (_hookId == IntPtr.Zero)
        {
            HostAssets.AppendLog($"Keyboard double tap: failed to install hook, lastError={Marshal.GetLastWin32Error()}.");
            return;
        }

        HostAssets.AppendLog($"Keyboard double tap: started. hook=0x{_hookId.ToInt64():X}, triggers=DoubleCtrl,DoubleAlt.");
    }

    public static void ApplyConfiguredShortcut(string? shortcut)
    {
        _doubleCtrlEnabled = string.Equals(shortcut, "DoubleCtrl", StringComparison.OrdinalIgnoreCase);
        _doubleAltEnabled = string.Equals(shortcut, "DoubleAlt", StringComparison.OrdinalIgnoreCase);
        _suppressCurrentAltTap = false;
    }

    public static void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        var unhooked = UnhookWindowsHookEx(_hookId);
        HostAssets.AppendLog($"Keyboard double tap: stopped. unhooked={unhooked}.");
        _hookId = IntPtr.Zero;
        _onDoubleTap = null;
        _onWinHold = null;
        _onWinRelease = null;
        _onWinDoubleTap = null;
        _lastTapKind = ModifierTapKind.None;
        _sequenceDirty = false;
        ResetKeyState();
    }

    public static void ApplyYanmSettings(YanmSettings? settings)
    {
        settings ??= new YanmSettings();
        var normalizedKey = YanmActivationKeys.Normalize(settings.ActivationKey);
        var supportsWinKeyboardTriggers = normalizedKey is YanmActivationKeys.Win or YanmActivationKeys.CapsLock;
        _winOverlayEnabled = settings.Enabled && supportsWinKeyboardTriggers && (settings.TriggerWinHold || settings.TriggerWinDoubleTap);
        _winHoldEnabled = settings.Enabled && supportsWinKeyboardTriggers && settings.TriggerWinHold;
        _winDoubleTapEnabled = settings.Enabled && supportsWinKeyboardTriggers && settings.TriggerWinDoubleTap;
        _yanmActivationKey = YanmActivationKeys.Normalize(settings.ActivationKey);
        if (!_winOverlayEnabled)
        {
            _winOverlayActive = false;
        }
    }

    public static void ResetStuckKeyboardState()
    {
        ResetKeyState();
        _lastTapKind = ModifierTapKind.None;
        _lastTapTimestamp = 0;
        _lastWinTapTimestamp = 0;
        _sequenceDirty = false;

        ReleaseVirtualKey(VkLControl);
        ReleaseVirtualKey(VkRControl);
        ReleaseVirtualKey(VkLMenu);
        ReleaseVirtualKey(VkRMenu);
        ReleaseVirtualKey(VkLShift);
        ReleaseVirtualKey(VkRShift);
        ReleaseVirtualKey(VkLWin);
        ReleaseVirtualKey(VkRWin);
        ReleaseVirtualKey(VkCapsLock);
        CancelForegroundAltMenuMode();
        HostAssets.AppendLog("Keyboard state reset requested from tray: modifiers released and internal state cleared.");
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule!;
        var moduleHandle = GetModuleHandle(currentModule.ModuleName);
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
        if (hook != IntPtr.Zero)
        {
            return hook;
        }

        HostAssets.AppendLog($"Keyboard double tap: SetWindowsHookEx failed with module handle, module={currentModule.ModuleName}, lastError={Marshal.GetLastWin32Error()}; retrying with hMod=0.");
        return SetWindowsHookEx(WhKeyboardLl, proc, IntPtr.Zero, 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((info.flags & LlkhfInjected) != 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            var vkCode = (int)info.vkCode;
            var suppress = false;

            if (message is WmKeyDown or WmSysKeyDown)
            {
                suppress = HandleKeyDown(vkCode);
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                suppress = HandleKeyUp(vkCode);
            }

            if (suppress)
            {
                return 1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool HandleKeyDown(int vkCode)
    {
        if (vkCode == VkCapsLock)
        {
            _capsLockDown = true;
            _capsLockUsedForLauncher = false;
            _capsLockDownTimestamp = Environment.TickCount64;
            if (IsLauncherWindowActive())
            {
                DispatchCapsGuideState(isCapsDown: true, activeKey: null);
                return true; // 拦截 CapsLock 按下，避免在搜索框内直接触发系统大写锁定
            }
            return IsYanmTriggerKey(YanmActivationKeys.CapsLock) ? HandleYanmTriggerDown(YanmActivationKeys.CapsLock) : false;
        }

        // 当按住 CapsLock 且启动器窗口处于前台激活时，拦截 WSAD / 空格 / Enter 等
        if (_capsLockDown && IsLauncherWindowActive())
        {
            switch (vkCode)
            {
                case 0x57: // 'W' - 向上
                case 0x45: // 'E'
                case 0x4B: // 'K'
                    _capsLockUsedForLauncher = true;
                    DispatchLauncherCapsAction(MainWindow.LauncherCapsAction.MoveUp, "W");
                    return true;

                case 0x53: // 'S' - 向下
                case 0x4A: // 'J'
                    _capsLockUsedForLauncher = true;
                    DispatchLauncherCapsAction(MainWindow.LauncherCapsAction.MoveDown, "S");
                    return true;

                case 0x41: // 'A' - 左 (返回输入框)
                case 0x48: // 'H'
                    _capsLockUsedForLauncher = true;
                    DispatchLauncherCapsAction(MainWindow.LauncherCapsAction.ReturnToSearch, "A");
                    return true;

                case 0x44: // 'D' - 右 (操作菜单)
                case 0x4C: // 'L'
                case 0x46: // 'F'
                    _capsLockUsedForLauncher = true;
                    DispatchLauncherCapsAction(MainWindow.LauncherCapsAction.OpenMenu, "D");
                    return true;

                case 0x20: // Space - 空格直接运行！
                    _capsLockUsedForLauncher = true;
                    DispatchLauncherCapsAction(MainWindow.LauncherCapsAction.Execute, "Space");
                    return true;

                case 0x0D: // Enter
                    _capsLockUsedForLauncher = true;
                    DispatchLauncherCapsAction(MainWindow.LauncherCapsAction.Execute, "Space");
                    return true;
            }
        }

        switch (vkCode)
        {
            case VkLControl:
                _leftCtrlDown = true;
                return false;
            case VkRControl:
                _rightCtrlDown = true;
                return false;
            case VkLMenu:
                if (ShouldSuppressCurrentAltTap())
                {
                    _leftAltDown = true;
                    _suppressCurrentAltTap = true;
                    return true;
                }

                _leftAltDown = true;
                return false;
            case VkRMenu:
                if (ShouldSuppressCurrentAltTap())
                {
                    _rightAltDown = true;
                    _suppressCurrentAltTap = true;
                    return true;
                }

                _rightAltDown = true;
                return false;
            case VkLShift:
                _leftShiftDown = true;
                return false;
            case VkRShift:
                _rightShiftDown = true;
                return false;
            case VkLWin:
                _leftWinDown = true;
                return IsYanmTriggerKey(YanmActivationKeys.Win) ? HandleYanmTriggerDown(YanmActivationKeys.Win) : false;
            case VkRWin:
                _rightWinDown = true;
                return IsYanmTriggerKey(YanmActivationKeys.Win) ? HandleYanmTriggerDown(YanmActivationKeys.Win) : false;
        }

        if (ShouldReleaseWinForYanmPassthrough())
        {
            ReleaseWinForYanmHold("key-passthrough");
        }

        _sequenceDirty = true;
        return false;
    }

    private static bool HandleKeyUp(int vkCode)
    {
        if (vkCode == VkCapsLock)
        {
            _capsLockDown = false;
            var isLauncherActive = IsLauncherWindowActive();
            var duration = Environment.TickCount64 - _capsLockDownTimestamp;

            if (isLauncherActive)
            {
                DispatchCapsGuideState(isCapsDown: false, activeKey: null);
                // 如果是短按（< 350ms）且未在按住期间使用任何 WSAD 组合键，则判定为切换大小写
                if (!_capsLockUsedForLauncher && duration < 350)
                {
                    SendSyntheticCapsLockToggle();
                }
                return true;
            }
            return IsYanmTriggerKey(YanmActivationKeys.CapsLock) ? HandleYanmTriggerUp(YanmActivationKeys.CapsLock) : false;
        }

        ModifierTapKind releasedKind;
        switch (vkCode)
        {
            case VkLControl:
                _leftCtrlDown = false;
                releasedKind = ModifierTapKind.Control;
                break;
            case VkRControl:
                _rightCtrlDown = false;
                releasedKind = ModifierTapKind.Control;
                break;
            case VkLMenu:
                _leftAltDown = false;
                releasedKind = ModifierTapKind.Alt;
                break;
            case VkRMenu:
                _rightAltDown = false;
                releasedKind = ModifierTapKind.Alt;
                break;
            case VkLShift:
                _leftShiftDown = false;
                return false;
            case VkRShift:
                _rightShiftDown = false;
                return false;
            case VkLWin:
                _leftWinDown = false;
                return IsYanmTriggerKey(YanmActivationKeys.Win) ? HandleYanmTriggerUp(YanmActivationKeys.Win) : false;
            case VkRWin:
                _rightWinDown = false;
                return IsYanmTriggerKey(YanmActivationKeys.Win) ? HandleYanmTriggerUp(YanmActivationKeys.Win) : false;
            default:
                _sequenceDirty = true;
                return false;
        }

        if (HasOtherModifiersPressed(releasedKind))
        {
            _sequenceDirty = true;
            _suppressCurrentAltTap = false;
            return false;
        }

        var now = Environment.TickCount64;
        if (!_sequenceDirty &&
            _lastTapKind == releasedKind &&
            now - _lastTapTimestamp <= 350)
        {
            var shouldSuppress = releasedKind == ModifierTapKind.Alt && _doubleAltEnabled && _suppressCurrentAltTap;
            _lastTapKind = ModifierTapKind.None;
            _lastTapTimestamp = 0;
            _suppressCurrentAltTap = false;
            HostAssets.AppendLog($"Keyboard double tap: triggered {releasedKind}.");
            if (shouldSuppress)
            {
                CancelForegroundAltMenuMode();
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() => _onDoubleTap?.Invoke(releasedKind.ToString()));
            return shouldSuppress;
        }

        _lastTapKind = releasedKind;
        _lastTapTimestamp = now;
        _sequenceDirty = false;
        if (releasedKind != ModifierTapKind.Alt)
        {
            _suppressCurrentAltTap = false;
        }

        return releasedKind == ModifierTapKind.Alt && _doubleAltEnabled && _suppressCurrentAltTap;
    }

    private static bool IsYanmTriggerKey(string key)
    {
        return string.Equals(_yanmActivationKey, key, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HandleYanmTriggerDown(string key)
    {
        if (!_winOverlayEnabled || HasBlockingModifiersForYanm(key) || !IsYanmAllowedForForegroundProcess())
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (_winOverlayActive && !IsPhysicalYanmTriggerDown(key) && _yanmTriggerDownTimestamp > 0 && now - _yanmTriggerDownTimestamp > 30000)
        {
            HostAssets.AppendLog($"Keyboard {key} hold: stale active state detected, resetting Yanm trigger state.");
            _winOverlayActive = false;
            _hasReleasedWinForYanmHold = false;
            _yanmTriggerDownTimestamp = 0;
        }

        if (!_winOverlayActive)
        {
            _winOverlayActive = true;
            _hasReleasedWinForYanmHold = false;
            _yanmTriggerDownTimestamp = now;
            if (_winHoldEnabled)
            {
                HostAssets.AppendLog($"Keyboard {key} hold: triggered Yanm overlay.");
                InvokeOnUiInputPriority(() => _onWinHold?.Invoke(), $"{key}-hold");
                if (string.Equals(key, YanmActivationKeys.Win, StringComparison.OrdinalIgnoreCase))
                {
                    ReleaseWinForYanmHold("hold-trigger");
                }
            }
        }

        return true;
    }

    private static bool IsPhysicalYanmTriggerDown(string key)
    {
        return string.Equals(key, YanmActivationKeys.Win, StringComparison.OrdinalIgnoreCase)
            ? _leftWinDown || _rightWinDown
            : GetAsyncKeyState(VkCapsLock) < 0;
    }

    private static bool HandleYanmTriggerUp(string key)
    {
        if (!_winOverlayActive)
        {
            return false;
        }

        _winOverlayActive = false;
        _hasReleasedWinForYanmHold = false;
        _yanmTriggerDownTimestamp = 0;
        var now = Environment.TickCount64;
        var isDoubleTap = _winDoubleTapEnabled && now - _lastWinTapTimestamp <= 350;
        _lastWinTapTimestamp = now;
        InvokeOnUiInputPriority(() =>
        {
            if (isDoubleTap)
            {
                HostAssets.AppendLog($"Keyboard {key} double tap: toggled Yanm pin.");
                _onWinDoubleTap?.Invoke();
            }
            else
            {
                if (_winHoldEnabled)
                {
                    _onWinRelease?.Invoke();
                }
            }
        }, isDoubleTap ? $"{key}-double-tap" : $"{key}-release");

        return true;
    }

    private static bool HasBlockingModifiersForYanm(string key)
    {
        return string.Equals(key, YanmActivationKeys.Win, StringComparison.OrdinalIgnoreCase)
            ? HasNonWinModifiersPressed()
            : _leftCtrlDown || _rightCtrlDown || _leftAltDown || _rightAltDown || _leftShiftDown || _rightShiftDown || _leftWinDown || _rightWinDown;
    }

    private static bool IsYanmAllowedForForegroundProcess()
    {
        var settings = AppSettingsStore.Load().Yanm ?? new YanmSettings();
        var whitelist = settings.WhitelistedProcesses ?? [];
        var blacklist = settings.BlacklistedProcesses ?? [];
        if (whitelist.Count == 0 && blacklist.Count == 0)
        {
            return true;
        }

        var processName = GetForegroundProcessName();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return whitelist.Count == 0;
        }

        if (whitelist.Count > 0)
        {
            var allowed = whitelist.Any(item => ProcessNameMatches(processName, item));
            if (!allowed)
            {
                HostAssets.AppendLog($"Keyboard Yanm trigger blocked by whitelist, process={processName}.");
            }

            return allowed;
        }

        var blocked = blacklist.Any(item => ProcessNameMatches(processName, item));
        if (blocked)
        {
            HostAssets.AppendLog($"Keyboard Yanm trigger blocked by blacklist, process={processName}.");
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
            return ProcessHelper.GetProcessNameByPid(processId);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool HasNonWinModifiersPressed()
    {
        return _leftCtrlDown || _rightCtrlDown || _leftAltDown || _rightAltDown || _leftShiftDown || _rightShiftDown;
    }

    private static bool HasOtherModifiersPressed(ModifierTapKind releasedKind)
    {
        return releasedKind switch
        {
            ModifierTapKind.Control => _leftAltDown || _rightAltDown || _leftShiftDown || _rightShiftDown || _leftWinDown || _rightWinDown,
            ModifierTapKind.Alt => _leftCtrlDown || _rightCtrlDown || _leftShiftDown || _rightShiftDown || _leftWinDown || _rightWinDown,
            _ => true
        };
    }

    private static void ResetKeyState()
    {
        _leftCtrlDown = false;
        _rightCtrlDown = false;
        _leftAltDown = false;
        _rightAltDown = false;
        _leftShiftDown = false;
        _rightShiftDown = false;
        _leftWinDown = false;
        _rightWinDown = false;
        _suppressCurrentAltTap = false;
        _winOverlayActive = false;
        _yanmTriggerDownTimestamp = 0;
        _hasReleasedWinForYanmHold = false;
    }

    private static bool ShouldReleaseWinForYanmPassthrough()
    {
        return _winOverlayActive &&
               IsYanmTriggerKey(YanmActivationKeys.Win) &&
               (_leftWinDown || _rightWinDown);
    }

    private static void ReleaseWinForYanmHold(string reason)
    {
        if (_hasReleasedWinForYanmHold)
        {
            return;
        }

        if (_leftWinDown)
        {
            ReleaseVirtualKey(VkLWin);
        }

        if (_rightWinDown)
        {
            ReleaseVirtualKey(VkRWin);
        }

        _hasReleasedWinForYanmHold = true;
        HostAssets.AppendLog($"Keyboard Win hold: released logical Win state for Yanm input passthrough, reason={reason}.");
    }

    private static void InvokeOnUiInputPriority(Action action, string reason)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        var queuedAt = Stopwatch.GetTimestamp();
        dispatcher.BeginInvoke(new Action(() =>
        {
            var delayMs = Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds;
            HostAssets.AppendLog($"Keyboard Yanm UI callback running: reason={reason}, queueDelayMs={delayMs:0.0}.");
            action();
        }), DispatcherPriority.Input);
    }

    private static bool ShouldSuppressCurrentAltTap()
    {
        if (!_doubleAltEnabled || _sequenceDirty)
        {
            return false;
        }

        var now = Environment.TickCount64;
        return _lastTapKind == ModifierTapKind.Alt &&
               now - _lastTapTimestamp <= 350 &&
               !_leftCtrlDown &&
               !_rightCtrlDown &&
               !_leftShiftDown &&
               !_rightShiftDown &&
               !_leftWinDown &&
               !_rightWinDown;
    }

    private static void CancelForegroundAltMenuMode()
    {
        const uint keyEventKeyUp = 0x0002;
        keybd_event((byte)VkEscape, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VkEscape, 0, keyEventKeyUp, UIntPtr.Zero);
    }

    private static void ReleaseVirtualKey(int vkCode)
    {
        const uint keyEventKeyUp = 0x0002;
        keybd_event((byte)vkCode, 0, keyEventKeyUp, UIntPtr.Zero);
    }

    private enum ModifierTapKind
    {
        None,
        Control,
        Alt
    }

    private const int VkEscape = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}
