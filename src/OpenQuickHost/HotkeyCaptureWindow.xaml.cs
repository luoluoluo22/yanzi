using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class HotkeyCaptureWindow : Window
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_ALTDOWN = 0x00000020;

    private readonly bool _allowEmpty;
    private readonly bool _allowDoubleTap;
    private readonly bool _allowModifierless;
    private readonly LowLevelKeyboardProc _keyboardHookProc;
    private IntPtr _keyboardHookHandle = IntPtr.Zero;
    private HwndSource? _source;
    private Key? _pendingModifierKey;
    private long _lastModifierTapTimestamp;
    private string? _lastModifierShortcut;
    private bool _capturedChordDuringModifierPress;
    private bool _suppressDisplayNameSync;
    private bool _displayNameManuallyEdited;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public HotkeyCaptureWindow(string title, string description, string? initialValue = null, string? initialDisplayName = null, bool allowEmpty = false, bool allowDoubleTap = false, bool allowModifierless = false)
    {
        InitializeComponent();
        _keyboardHookProc = LowLevelKeyboardCallback;
        _allowEmpty = allowEmpty;
        _allowDoubleTap = allowDoubleTap;
        _allowModifierless = allowModifierless;
        Title = title;
        TitleText.Text = title;
        DescriptionText.Text = description;
        ClearButton.Visibility = allowEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(initialValue))
        {
            ShortcutText = initialValue.Trim();
            CapturedHotkeyText.Text = ShortcutText;
            ConfirmButton.IsEnabled = true;
        }

        var initialName = string.IsNullOrWhiteSpace(initialDisplayName)
            ? ShortcutText
            : initialDisplayName.Trim();
        _suppressDisplayNameSync = true;
        DisplayNameTextBox.Text = initialName;
        _suppressDisplayNameSync = false;
        _displayNameManuallyEdited = !string.IsNullOrWhiteSpace(initialDisplayName) &&
            !string.Equals(initialDisplayName?.Trim(), ShortcutText, StringComparison.OrdinalIgnoreCase);

        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HostAssets.AppendLog($"[HotkeyCaptureLog] Loaded: hwnd=0x{hwnd:X}, foreHwnd=0x{GetForegroundWindow():X}, IsActive={IsActive}, IsFocused={IsFocused}");
            ForceSetForeground(hwnd);
            Activate();
            Focus();
            Keyboard.Focus(this);
            InstallKeyboardHook();
        };
        Unloaded += (_, _) =>
        {
            UninstallKeyboardHook();
        };
        ContentRendered += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HostAssets.AppendLog($"[HotkeyCaptureLog] ContentRendered: hwnd=0x{hwnd:X}, foreHwnd=0x{GetForegroundWindow():X}, IsActive={IsActive}, IsFocused={IsFocused}");
            ForceSetForeground(hwnd);
            Activate();
            Focus();
            Keyboard.Focus(this);
        };
        Activated += (_, _) =>
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] Activated: IsFocused={IsFocused}, KeyboardFocus={Keyboard.FocusedElement?.GetType().Name ?? "null"}");
        };
        Deactivated += (_, _) =>
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] Deactivated: foreHwnd=0x{GetForegroundWindow():X}");
            ResetModifierTracking();
        };
        GotKeyboardFocus += (_, e) =>
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] GotKeyboardFocus: source={e.OriginalSource?.GetType().Name ?? "null"}");
        };
        LostKeyboardFocus += (_, e) =>
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] LostKeyboardFocus: newFocus={e.NewFocus?.GetType().Name ?? "null"}");
        };
        PreviewMouseDown += (_, _) =>
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] PreviewMouseDown: foreHwnd=0x{GetForegroundWindow():X}, IsActive={IsActive}");
        };

        HostAssets.AppendLog($"Hotkey capture dialog opened: title={title}, initialValue={initialValue ?? string.Empty}, initialDisplayName={initialDisplayName ?? string.Empty}, allowEmpty={allowEmpty}, allowDoubleTap={allowDoubleTap}, allowModifierless={allowModifierless}.");
    }

    public string ShortcutText { get; private set; } = string.Empty;

    public string DisplayNameText { get; private set; } = string.Empty;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HostAssets.AppendLog($"[HotkeyCaptureLog] OnSourceInitialized: hwnd=0x{hwnd:X}, foreHwnd=0x{GetForegroundWindow():X}");
        ForceSetForeground(hwnd);
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        _source?.AddHook(WndProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        UninstallKeyboardHook();
        _source?.RemoveHook(WndProc);
        _source = null;
        HostAssets.AppendLog($"Hotkey capture dialog closed: title={Title}, shortcut={ShortcutText}.");
        base.OnClosed(e);
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = GetModuleHandle(curModule?.ModuleName);
            _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, moduleHandle, 0);
            HostAssets.AppendLog($"[HotkeyCaptureLog] Low-level keyboard hook installed: handle=0x{_keyboardHookHandle:X}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] Failed to install low-level keyboard hook: {ex.Message}");
        }
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            HostAssets.AppendLog($"[HotkeyCaptureLog] Low-level keyboard hook uninstalled: handle=0x{_keyboardHookHandle:X}");
            _keyboardHookHandle = IntPtr.Zero;
        }
    }

    private bool _isLWinDown;
    private bool _isRWinDown;
    private bool _isAltDown;
    private bool _isCtrlDown;
    private bool _isShiftDown;

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            var isKeyDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var isKeyUp = msg is WM_KEYUP or WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                var hookStruct = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (int)hookStruct.vkCode;
                var key = KeyInterop.KeyFromVirtualKey(vkCode);

                // 显式维护物理修饰键状态（特别是 Win 键，由于被钩子吃掉后系统消息队列不会记录，必须在钩子中实时追踪）
                if (vkCode is 0x5B) _isLWinDown = isKeyDown;
                else if (vkCode is 0x5C) _isRWinDown = isKeyDown;
                else if (vkCode is 0x12 or 0xA4 or 0xA5) _isAltDown = isKeyDown;
                else if (vkCode is 0x11 or 0xA2 or 0xA3) _isCtrlDown = isKeyDown;
                else if (vkCode is 0x10 or 0xA0 or 0xA1) _isShiftDown = isKeyDown;

                // 如果用户正在编辑显示名称文本框，放行所有输入
                if (IsEditingDisplayName())
                {
                    return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
                }

                if (key != Key.None)
                {
                    if (isKeyDown)
                    {
                        var modifiers = GetCurrentModifiers();
                        if (_isLWinDown || _isRWinDown)
                        {
                            modifiers |= ModifierKeys.Windows;
                        }
                        if (_isAltDown || (hookStruct.flags & LLKHF_ALTDOWN) != 0)
                        {
                            modifiers |= ModifierKeys.Alt;
                        }
                        if (_isCtrlDown)
                        {
                            modifiers |= ModifierKeys.Control;
                        }
                        if (_isShiftDown)
                        {
                            modifiers |= ModifierKeys.Shift;
                        }

                        HostAssets.AppendLog($"[HotkeyCaptureLog] LLHook KeyDown: vk=0x{vkCode:X}, key={key}, modifiers={modifiers}, flags=0x{hookStruct.flags:X}");

                        var handled = HandleCapturedKey(key, modifiers);
                        if (handled)
                        {
                            // 拦截按键，彻底防止系统处理 Alt+Tab 切屏、Win+R 运行、Win+E 资源管理器、Win+D 桌面等系统热键
                            return (IntPtr)1;
                        }
                    }
                    else if (isKeyUp)
                    {
                        HostAssets.AppendLog($"[HotkeyCaptureLog] LLHook KeyUp: vk=0x{vkCode:X}, key={key}");
                        var handled = HandleCapturedKeyUp(key);
                        if (_allowDoubleTap && handled)
                        {
                            return (IntPtr)1;
                        }
                    }
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN)
        {
            return IntPtr.Zero;
        }

        if (IsEditingDisplayName())
        {
            return IntPtr.Zero;
        }

        // 如果低级钩子已处于工作状态，避免重复处理
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            handled = true;
            return IntPtr.Zero;
        }

        var key = KeyInterop.KeyFromVirtualKey(wParam.ToInt32());
        if (key == Key.None)
        {
            return IntPtr.Zero;
        }

        var modifiers = GetCurrentModifiers();
        HostAssets.AppendLog($"Hotkey capture WndProc fallback: msg=0x{msg:X}, key={key}, modifiers={modifiers}.");
        handled = HandleCapturedKey(key, modifiers);
        return IntPtr.Zero;
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        return HotkeyHelper.GetCurrentPhysicalModifiers();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static void ForceSetForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        try
        {
            var foreHwnd = GetForegroundWindow();
            var foreThread = GetWindowThreadProcessId(foreHwnd, out _);
            var curThread = GetCurrentThreadId();
            if (foreThread != 0 && foreThread != curThread)
            {
                AttachThreadInput(curThread, foreThread, true);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                AttachThreadInput(curThread, foreThread, false);
            }
            else
            {
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[HotkeyCaptureLog] ForceSetForeground error: {ex.Message}");
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsEditingDisplayName())
        {
            return;
        }

        e.Handled = HandleCapturedKey(ResolveActualKey(e), GetCurrentModifiers());
    }

    private bool HandleCapturedKey(Key key, ModifierKeys modifiers)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (key is Key.Return or Key.Enter && modifiers == ModifierKeys.None && !string.IsNullOrWhiteSpace(ShortcutText))
        {
            HostAssets.AppendLog("Hotkey capture confirmed by Enter key.");
            ConfirmButton_Click(this, new RoutedEventArgs());
            return true;
        }

        if (key is Key.Escape && modifiers == ModifierKeys.None)
        {
            HostAssets.AppendLog("Hotkey capture cancelled by Escape.");
            DialogResult = false;
            return true;
        }

        if (IsModifierKey(key))
        {
            _pendingModifierKey = key;
            _capturedChordDuringModifierPress = false;
            return true;
        }

        if (modifiers == ModifierKeys.None)
        {
            if (_allowModifierless)
            {
                ShortcutText = BuildShortcutText(modifiers, key);
                CapturedHotkeyText.Text = ShortcutText;
                SyncDisplayNameFromShortcut();
                ConfirmButton.IsEnabled = true;
                _capturedChordDuringModifierPress = true;
                HostAssets.AppendLog($"Hotkey capture recorded modifierless shortcut: {ShortcutText}.");
            }
            else
            {
                ErrorText.Text = "请至少包含 Ctrl、Alt、Shift 或 Win 中的一个修饰键。";
                ErrorText.Visibility = Visibility.Visible;
                ConfirmButton.IsEnabled = false;
                HostAssets.AppendLog($"Hotkey capture rejected modifierless key: {key}.");
            }

            return true;
        }

        ShortcutText = BuildShortcutText(modifiers, key);
        CapturedHotkeyText.Text = ShortcutText;
        SyncDisplayNameFromShortcut();
        ConfirmButton.IsEnabled = true;
        _capturedChordDuringModifierPress = true;
        HostAssets.AppendLog($"Hotkey capture recorded shortcut: {ShortcutText}.");
        return true;
    }

    private void Window_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsEditingDisplayName())
        {
            return;
        }

        e.Handled = HandleCapturedKeyUp(ResolveActualKey(e));
    }

    private bool HandleCapturedKeyUp(Key key)
    {
        if (IsEditingDisplayName())
        {
            return false;
        }

        if (!IsModifierKey(key))
        {
            return false;
        }

        if (!_allowDoubleTap)
        {
            _pendingModifierKey = null;
            _capturedChordDuringModifierPress = false;
            return true;
        }

        if (_capturedChordDuringModifierPress)
        {
            _pendingModifierKey = null;
            _capturedChordDuringModifierPress = false;
            return true;
        }

        if (_pendingModifierKey != key)
        {
            return true;
        }

        var shortcut = key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "DoubleCtrl",
            Key.LeftAlt or Key.RightAlt => "DoubleAlt",
            _ => string.Empty
        };

        _pendingModifierKey = null;
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return true;
        }

        var now = Environment.TickCount64;
        if (string.Equals(_lastModifierShortcut, shortcut, StringComparison.Ordinal) &&
            now - _lastModifierTapTimestamp <= 400)
        {
            ShortcutText = shortcut;
            CapturedHotkeyText.Text = shortcut;
            SyncDisplayNameFromShortcut();
            ConfirmButton.IsEnabled = true;
            _lastModifierShortcut = null;
            _lastModifierTapTimestamp = 0;
            HostAssets.AppendLog($"Hotkey capture recorded double tap shortcut: {shortcut}.");
        }
        else
        {
            _lastModifierShortcut = shortcut;
            _lastModifierTapTimestamp = now;
            CapturedHotkeyText.Text = $"{shortcut}（再按一次确认）";
            ConfirmButton.IsEnabled = false;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ResetModifierTracking()
    {
        _isLWinDown = false;
        _isRWinDown = false;
        _isAltDown = false;
        _isCtrlDown = false;
        _isShiftDown = false;
        _pendingModifierKey = null;
        _lastModifierShortcut = null;
        _lastModifierTapTimestamp = 0;
        _capturedChordDuringModifierPress = false;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        ShortcutText = string.Empty;
        CapturedHotkeyText.Text = "请直接按下新的组合键";
        ConfirmButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        _suppressDisplayNameSync = true;
        DisplayNameTextBox.Text = string.Empty;
        _suppressDisplayNameSync = false;
        DisplayNameText = string.Empty;
        _displayNameManuallyEdited = false;
        ResetModifierTracking();
        Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog("Hotkey capture cancelled by button.");
        DialogResult = false;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty)
        {
            return;
        }

        ShortcutText = string.Empty;
        CapturedHotkeyText.Text = "当前将清空快捷键";
        _suppressDisplayNameSync = true;
        DisplayNameTextBox.Text = string.Empty;
        _suppressDisplayNameSync = false;
        DisplayNameText = string.Empty;
        _displayNameManuallyEdited = false;
        ConfirmButton.IsEnabled = true;
        ErrorText.Visibility = Visibility.Collapsed;
        ResetModifierTracking();
        Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(ShortcutText))
        {
            ErrorText.Text = "请先录制一个快捷键。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DisplayNameText = string.IsNullOrWhiteSpace(DisplayNameTextBox.Text)
            ? ShortcutText
            : DisplayNameTextBox.Text.Trim();

        HostAssets.AppendLog($"Hotkey capture confirmed: {ShortcutText}, displayName={DisplayNameText}.");
        DialogResult = true;
    }

    private void DisplayNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressDisplayNameSync)
        {
            return;
        }

        DisplayNameText = DisplayNameTextBox.Text.Trim();
        _displayNameManuallyEdited = true;
    }

    private static bool IsModifierKey(Key key)
    {
        return HotkeyHelper.IsModifierKey(key);
    }

    private static Key ResolveActualKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.System)
        {
            return e.SystemKey;
        }

        if (e.Key == Key.ImeProcessed)
        {
            return e.ImeProcessedKey;
        }

        if (e.Key == Key.DeadCharProcessed)
        {
            return e.DeadCharProcessedKey;
        }

        return e.Key;
    }

    private static string BuildShortcutText(ModifierKeys modifiers, Key key)
    {
        return HotkeyHelper.FormatHotkey(modifiers, key) ?? string.Empty;
    }

    private void SyncDisplayNameFromShortcut()
    {
        var currentName = DisplayNameTextBox.Text.Trim();
        if (_displayNameManuallyEdited &&
            !string.IsNullOrWhiteSpace(currentName) &&
            !string.Equals(currentName, ShortcutText, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _suppressDisplayNameSync = true;
        DisplayNameTextBox.Text = ShortcutText;
        _suppressDisplayNameSync = false;
        DisplayNameText = ShortcutText;
        _displayNameManuallyEdited = false;
    }

    private bool IsEditingDisplayName()
    {
        return DisplayNameTextBox.IsKeyboardFocusWithin;
    }

    private static string FormatKey(Key key)
    {
        return HotkeyHelper.FormatKey(key);
    }
}
