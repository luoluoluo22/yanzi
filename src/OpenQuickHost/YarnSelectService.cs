using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public static class YarnSelectService
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkC = 0x43;
    private const int VkR = 0x52;
    private const int VkS = 0x53;
    private const int VkV = 0x56;
    private const int VkX = 0x58;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int XButton1 = 1;
    private const int XButton2 = 2;
    private static readonly IntPtr SyntheticExtraInfo = (IntPtr)0x59414E5A; // "YANZ"
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    private static readonly LowLevelMouseProc MouseProc = MouseHookCallback;
    private static readonly LowLevelKeyboardProc KeyboardProc = KeyboardHookCallback;
    private static IntPtr _mouseHookId;
    private static IntPtr _keyboardHookId;
    private static YarnSelectSettings _settings = new();
    private static Action<YarnSelectActionRequest>? _onAction;
    private static bool _isRunning;
    private static bool _leftButtonDown;
    private static bool _triggeredThisHold;
    private static bool _swallowRightUp;
    private static bool _swallowXButtonUp;
    private static DateTimeOffset _leftButtonDownAt;
    private static string _lastBlockedForegroundProcess = string.Empty;
    private static DateTimeOffset _lastBlockedForegroundProcessLogAt = DateTimeOffset.MinValue;

    public static bool IsRunning => _isRunning;

    public static string GetMouseStateSummary()
    {
        return $"燕选={(IsRunning ? "运行" : "停止")}，左键={(_leftButtonDown ? "已按下" : "未按下")}，已触发={_triggeredThisHold}，吞右键={_swallowRightUp}，吞侧键={_swallowXButtonUp}";
    }

    public static void ResetMouseState()
    {
        ResetTransientMouseState();
        HostAssets.AppendLog("YarnSelect: mouse state reset requested from tray.");
    }

    public static void Start(Action<YarnSelectActionRequest> onAction)
    {
        if (_isRunning)
        {
            HostAssets.AppendLog("YarnSelect: start skipped because hook is already running.");
            return;
        }

        _onAction = onAction;
        ReloadSettings();
        if (!_settings.Enabled)
        {
            HostAssets.AppendLog("YarnSelect: start skipped because feature is disabled.");
            return;
        }

        _mouseHookId = SetMouseHook(MouseProc);
        _keyboardHookId = SetKeyboardHook(KeyboardProc);
        if (_mouseHookId == IntPtr.Zero || _keyboardHookId == IntPtr.Zero)
        {
            HostAssets.AppendLog($"YarnSelect: failed to install hooks, mouse=0x{_mouseHookId.ToInt64():X}, keyboard=0x{_keyboardHookId.ToInt64():X}, lastError={Marshal.GetLastWin32Error()}.");
            Stop();
            return;
        }

        _isRunning = true;
        HostAssets.AppendLog($"YarnSelect: started. {DescribeSettings()}");
    }

    public static void Stop()
    {
        if (_mouseHookId != IntPtr.Zero)
        {
            var unhooked = UnhookWindowsHookEx(_mouseHookId);
            HostAssets.AppendLog($"YarnSelect: mouse hook stopped. unhooked={unhooked}.");
            _mouseHookId = IntPtr.Zero;
        }

        if (_keyboardHookId != IntPtr.Zero)
        {
            var unhooked = UnhookWindowsHookEx(_keyboardHookId);
            HostAssets.AppendLog($"YarnSelect: keyboard hook stopped. unhooked={unhooked}.");
            _keyboardHookId = IntPtr.Zero;
        }

        _isRunning = false;
        ResetTransientMouseState();
        HostAssets.AppendLog("YarnSelect: stopped and transient mouse state reset.");
        _onAction = null;
    }

    public static void ReloadSettings()
    {
        _settings = AppSettingsStore.Load().YarnSelect ?? new YarnSelectSettings();
        _settings.WhitelistedProcesses ??= [];
        _settings.BlacklistedProcesses ??= [];
        ResetTransientMouseState();
        if (_isRunning && !_settings.Enabled)
        {
            Stop();
        }
        else
        {
            HostAssets.AppendLog($"YarnSelect: settings reloaded, transient mouse state reset. {DescribeSettings()}");
        }
    }

    private static string DescribeSettings()
    {
        var enabled = (_settings.Rules ?? [])
            .Where(static rule => rule.Enabled)
            .Select(static rule => $"Left+{rule.TriggerKey}:{rule.ActionType}")
            .ToList();
        enabled.Add($"delay={Math.Clamp(_settings.TriggerDelayMilliseconds, 0, 1000)}ms");
        var whitelist = _settings.WhitelistedProcesses ?? [];
        var blacklist = _settings.BlacklistedProcesses ?? [];
        if (whitelist.Count > 0)
        {
            enabled.Add($"whitelist={whitelist.Count}");
        }
        else if (blacklist.Count > 0)
        {
            enabled.Add($"blacklist={blacklist.Count}");
        }
        return string.Join(", ", enabled);
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);
        var hook = SetWindowsHookEx(WhMouseLl, proc, moduleHandle, 0);
        return hook != IntPtr.Zero ? hook : SetWindowsHookEx(WhMouseLl, proc, IntPtr.Zero, 0);
    }

    private static IntPtr SetKeyboardHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
        return hook != IntPtr.Zero ? hook : SetWindowsHookEx(WhKeyboardLl, proc, IntPtr.Zero, 0);
    }

    private static unsafe IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_settings.Enabled)
        {
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        var message = (int)wParam;
        // YarnSelect 不需要处理 WM_MOUSEMOVE 消息，1 纳秒极速透传
        if (message == WmMouseMove)
        {
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        var mouse = *(MSLLHOOKSTRUCT*)lParam;
        if (mouse.dwExtraInfo == SyntheticExtraInfo)
        {
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        if (message == WmLButtonDown)
        {
            _leftButtonDown = true;
            _triggeredThisHold = false;
            _leftButtonDownAt = DateTimeOffset.UtcNow;
        }
        else if (message == WmLButtonUp)
        {
            _leftButtonDown = false;
            _triggeredThisHold = false;
            _swallowRightUp = false;
            _swallowXButtonUp = false;
        }
        else if (message == WmRButtonDown && CanTrigger() && TryGetRule("Right", out var rightRule))
        {
            _swallowRightUp = true;
            Trigger(rightRule, "Left+Right");
            return (IntPtr)1;
        }
        else if (message == WmRButtonUp && _swallowRightUp)
        {
            _swallowRightUp = false;
            return (IntPtr)1;
        }
        else if (message == WmXButtonDown && CanTrigger())
        {
            var xButton = GetXButton(mouse.mouseData);
            var triggerKey = xButton == XButton1 ? "X1" : xButton == XButton2 ? "X2" : string.Empty;
            if (!string.IsNullOrWhiteSpace(triggerKey) && TryGetRule(triggerKey, out var xRule))
            {
                _swallowXButtonUp = true;
                Trigger(xRule, $"Left+{triggerKey}");
                return (IntPtr)1;
            }
        }
        else if (message == WmXButtonUp && _swallowXButtonUp)
        {
            _swallowXButtonUp = false;
            return (IntPtr)1;
        }

        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static unsafe IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_settings.Enabled || !_leftButtonDown)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        var message = (int)wParam;
        if (message != WmKeyDown && message != WmSysKeyDown)
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        var keyboard = *(KBDLLHOOKSTRUCT*)lParam;
        if (keyboard.dwExtraInfo == SyntheticExtraInfo || HasModifierDown() || !CanTrigger())
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        if (!TryGetKeyboardTriggerKey(keyboard.vkCode, out var triggerKey) ||
            !TryGetRule(triggerKey, out var rule))
        {
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        Trigger(rule, $"Left+{triggerKey}");
        return (IntPtr)1;
    }

    private static bool CanTrigger()
    {
        if (!_leftButtonDown || _triggeredThisHold || !IsForegroundProcessAllowed(logBlocked: true))
        {
            return false;
        }

        var delay = Math.Clamp(_settings.TriggerDelayMilliseconds, 0, 1000);
        return DateTimeOffset.UtcNow - _leftButtonDownAt >= TimeSpan.FromMilliseconds(delay);
    }

    private static bool TryGetKeyboardTriggerKey(int vkCode, out string triggerKey)
    {
        if ((vkCode >= 0x30 && vkCode <= 0x39) || (vkCode >= 0x41 && vkCode <= 0x5A))
        {
            triggerKey = ((char)vkCode).ToString();
            return true;
        }

        triggerKey = string.Empty;
        return false;
    }

    private static bool TryGetRule(string triggerKey, out YarnSelectRuleSettings rule)
    {
        var normalized = YarnSelectSettings.NormalizeTriggerKey(triggerKey);
        rule = (_settings.Rules ?? [])
            .Select(YarnSelectSettings.NormalizeRule)
            .FirstOrDefault(item =>
                item.Enabled &&
                item.TriggerKey.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ?? new YarnSelectRuleSettings();
        return !string.IsNullOrWhiteSpace(rule.TriggerKey);
    }

    private static void Trigger(YarnSelectRuleSettings rule, string gesture)
    {
        _triggeredThisHold = true;
        HostAssets.AppendLog($"YarnSelect: triggered {rule.ActionType}, gesture={gesture}, extensionId={rule.ExtensionId}.");
        var request = new YarnSelectActionRequest(
            YarnSelectActionTypes.Normalize(rule.ActionType),
            rule.ExtensionId,
            gesture,
            GetForegroundProcessName());
        _ = Task.Run(() => ExecuteRequest(request));
    }

    private static void ExecuteRequest(YarnSelectActionRequest request)
    {
        try
        {
            switch (request.ActionType)
            {
                case YarnSelectActionTypes.Copy:
                    SendCtrlKey(VkC);
                    break;
                case YarnSelectActionTypes.Cut:
                    SendCtrlKey(VkX);
                    break;
                case YarnSelectActionTypes.Paste:
                    SendCtrlKey(VkV);
                    break;
                case YarnSelectActionTypes.Search:
                    _ = TryCopySelectedText(out var searchText);
                    QueueAction(request with { Text = searchText });
                    break;
                case YarnSelectActionTypes.Run:
                    _ = TryCopySelectedText(out var runText);
                    QueueAction(request with { Text = runText });
                    break;
                case YarnSelectActionTypes.RunExtension:
                    _ = TryCopySelectedText(out var extensionInput);
                    QueueAction(request with { Text = extensionInput });
                    break;
                case YarnSelectActionTypes.SmartCopyPaste:
                    if (!TryCopySelectedText(out _))
                    {
                        SendCtrlKey(VkV);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"YarnSelect: action failed, action={request.ActionType}, error={ex.Message}");
        }
    }

    private static void QueueAction(YarnSelectActionRequest request)
    {
        var callback = _onAction;
        if (callback == null)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            () => callback(request),
            DispatcherPriority.Background);
    }

    private static bool TryCopySelectedText(out string text)
    {
        text = string.Empty;
        var before = ReadClipboardText();
        SendCtrlKey(VkC);
        for (var index = 0; index < 10; index++)
        {
            Thread.Sleep(20);
            var current = ReadClipboardText();
            if (!string.IsNullOrWhiteSpace(current) &&
                !string.Equals(current, before, StringComparison.Ordinal))
            {
                text = current;
                return true;
            }
        }

        return false;
    }

    private static string ReadClipboardText()
    {
        try
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
                System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SendCtrlKey(ushort key)
    {
        var inputs = new[]
        {
            KeyInput(VkControl, keyUp: false),
            KeyInput(key, keyUp: false),
            KeyInput(key, keyUp: true),
            KeyInput(VkControl, keyUp: true)
        };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort key, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = keyUp ? KeyEventFKeyUp : 0
                }
            }
        };
    }

    private static bool HasModifierDown() =>
        IsKeyDown(VkControl) ||
        IsKeyDown(VkMenu) ||
        IsKeyDown(VkLWin) ||
        IsKeyDown(VkRWin);

    private static bool IsKeyDown(int vkCode) => (GetAsyncKeyState(vkCode) & 0x8000) != 0;

    private static bool IsForegroundProcessAllowed(bool logBlocked = false)
    {
        var processName = GetForegroundProcessName();
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var whitelist = _settings.WhitelistedProcesses ?? [];
        if (whitelist.Count > 0)
        {
            var allowedByWhitelist = whitelist.Any(item => ProcessNameMatches(processName, item));
            if (!allowedByWhitelist && logBlocked)
            {
                LogBlockedForegroundProcess(processName, "not-in-whitelist");
            }

            return allowedByWhitelist;
        }

        var blockedByBlacklist = _settings.BlacklistedProcesses.Any(item => ProcessNameMatches(processName, item));
        if (blockedByBlacklist && logBlocked)
        {
            LogBlockedForegroundProcess(processName, "blacklist");
        }

        return !blockedByBlacklist;
    }

    public static string GetForegroundProcessName()
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

    private static bool ProcessNameMatches(string processName, string pattern)
    {
        return ProcessHelper.ProcessNameMatches(processName, pattern);
    }

    private static void LogBlockedForegroundProcess(string processName, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        if (processName.Equals(_lastBlockedForegroundProcess, StringComparison.OrdinalIgnoreCase) &&
            now - _lastBlockedForegroundProcessLogAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        _lastBlockedForegroundProcess = processName;
        _lastBlockedForegroundProcessLogAt = now;
        HostAssets.AppendLog($"YarnSelect: foreground process blocked, process={processName}, reason={reason}.");
    }

    private static void ResetTransientMouseState()
    {
        _leftButtonDown = false;
        _triggeredThisHold = false;
        _swallowRightUp = false;
        _swallowXButtonUp = false;
    }

    private static int GetXButton(uint mouseData) => (int)((mouseData >> 16) & 0xffff);

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
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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

public sealed record YarnSelectActionRequest(
    string ActionType,
    string ExtensionId,
    string Gesture,
    string ForegroundProcessName,
    string Text = "");
