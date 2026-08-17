using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class HotkeyCaptureWindow : Window
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private readonly bool _allowEmpty;
    private readonly bool _allowDoubleTap;
    private readonly bool _allowModifierless;
    private HwndSource? _source;
    private Key? _pendingModifierKey;
    private long _lastModifierTapTimestamp;
    private string? _lastModifierShortcut;
    private bool _capturedChordDuringModifierPress;
    private bool _suppressDisplayNameSync;
    private bool _displayNameManuallyEdited;

    public HotkeyCaptureWindow(string title, string description, string? initialValue = null, string? initialDisplayName = null, bool allowEmpty = false, bool allowDoubleTap = false, bool allowModifierless = false)
    {
        InitializeComponent();
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
        _source?.RemoveHook(WndProc);
        _source = null;
        HostAssets.AppendLog($"Hotkey capture dialog closed: title={Title}, shortcut={ShortcutText}.");
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmKeyDown && msg != WmSysKeyDown)
        {
            return IntPtr.Zero;
        }

        if (IsEditingDisplayName())
        {
            return IntPtr.Zero;
        }

        var key = KeyInterop.KeyFromVirtualKey(wParam.ToInt32());
        if (key == Key.None)
        {
            return IntPtr.Zero;
        }

        // Use GetKeyState for reliable modifier detection in WndProc
        var modifiers = GetCurrentModifiers();
        HostAssets.AppendLog($"Hotkey capture WndProc: msg=0x{msg:X}, key={key}, modifiers={modifiers}, foreHwnd=0x{GetForegroundWindow():X}.");
        handled = HandleCapturedKey(key, modifiers);
        return IntPtr.Zero;
    }

    private static ModifierKeys GetCurrentModifiers()
    {
        var mods = ModifierKeys.None;
        if ((GetKeyState(0x11) & 0x8000) != 0) mods |= ModifierKeys.Control; // VK_CONTROL
        if ((GetKeyState(0x12) & 0x8000) != 0) mods |= ModifierKeys.Alt;     // VK_MENU
        if ((GetKeyState(0x10) & 0x8000) != 0) mods |= ModifierKeys.Shift;   // VK_SHIFT
        if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0)
            mods |= ModifierKeys.Windows; // VK_LWIN / VK_RWIN
        return mods;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

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

        if (key is Key.Escape)
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

        var key = ResolveActualKey(e);
        if (!IsModifierKey(key))
        {
            return;
        }

        if (!_allowDoubleTap)
        {
            _pendingModifierKey = null;
            _capturedChordDuringModifierPress = false;
            e.Handled = true;
            return;
        }

        if (_capturedChordDuringModifierPress)
        {
            _pendingModifierKey = null;
            _capturedChordDuringModifierPress = false;
            e.Handled = true;
            return;
        }

        if (_pendingModifierKey != key)
        {
            e.Handled = true;
            return;
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
            e.Handled = true;
            return;
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
        e.Handled = true;
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
        _pendingModifierKey = null;
        _lastModifierShortcut = null;
        _lastModifierTapTimestamp = 0;
        _capturedChordDuringModifierPress = false;
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
        _pendingModifierKey = null;
        _lastModifierShortcut = null;
        _lastModifierTapTimestamp = 0;
        _capturedChordDuringModifierPress = false;
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
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
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
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(key));
        return parts.Count == 1 ? parts[0] : string.Join("+", parts);
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
        return key switch
        {
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            Key.Capital => "CapsLock",
            _ => key.ToString()
        };
    }
}
