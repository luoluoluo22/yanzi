using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

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

    private static readonly LowLevelKeyboardProc Proc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;
    private static Action<string>? _onDoubleTap;
    private static ModifierTapKind _lastTapKind = ModifierTapKind.None;
    private static long _lastTapTimestamp;
    private static bool _sequenceDirty;
    private static bool _leftCtrlDown;
    private static bool _rightCtrlDown;
    private static bool _leftAltDown;
    private static bool _rightAltDown;
    private static bool _leftShiftDown;
    private static bool _rightShiftDown;
    private static bool _leftWinDown;
    private static bool _rightWinDown;

    public static bool IsRunning => _hookId != IntPtr.Zero;

    public static void Start(Action<string> onDoubleTap)
    {
        if (IsRunning)
        {
            HostAssets.AppendLog("Keyboard double tap: start skipped because hook is already running.");
            return;
        }

        _onDoubleTap = onDoubleTap;
        _sequenceDirty = false;
        _lastTapKind = ModifierTapKind.None;
        _lastTapTimestamp = 0;
        _hookId = SetHook(Proc);
        if (_hookId == IntPtr.Zero)
        {
            HostAssets.AppendLog($"Keyboard double tap: failed to install hook, lastError={Marshal.GetLastWin32Error()}.");
            return;
        }

        HostAssets.AppendLog($"Keyboard double tap: started. hook=0x{_hookId.ToInt64():X}, triggers=DoubleCtrl,DoubleAlt.");
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
        _lastTapKind = ModifierTapKind.None;
        _sequenceDirty = false;
        ResetKeyState();
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
            var vkCode = (int)info.vkCode;

            if (message is WmKeyDown or WmSysKeyDown)
            {
                HandleKeyDown(vkCode);
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                HandleKeyUp(vkCode);
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void HandleKeyDown(int vkCode)
    {
        switch (vkCode)
        {
            case VkLControl:
                _leftCtrlDown = true;
                return;
            case VkRControl:
                _rightCtrlDown = true;
                return;
            case VkLMenu:
                _leftAltDown = true;
                return;
            case VkRMenu:
                _rightAltDown = true;
                return;
            case VkLShift:
                _leftShiftDown = true;
                return;
            case VkRShift:
                _rightShiftDown = true;
                return;
            case VkLWin:
                _leftWinDown = true;
                return;
            case VkRWin:
                _rightWinDown = true;
                return;
        }

        _sequenceDirty = true;
    }

    private static void HandleKeyUp(int vkCode)
    {
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
                return;
            case VkRShift:
                _rightShiftDown = false;
                return;
            case VkLWin:
                _leftWinDown = false;
                return;
            case VkRWin:
                _rightWinDown = false;
                return;
            default:
                _sequenceDirty = true;
                return;
        }

        if (HasOtherModifiersPressed(releasedKind))
        {
            _sequenceDirty = true;
            return;
        }

        var now = Environment.TickCount64;
        if (!_sequenceDirty &&
            _lastTapKind == releasedKind &&
            now - _lastTapTimestamp <= 350)
        {
            _lastTapKind = ModifierTapKind.None;
            _lastTapTimestamp = 0;
            HostAssets.AppendLog($"Keyboard double tap: triggered {releasedKind}.");
            System.Windows.Application.Current.Dispatcher.Invoke(() => _onDoubleTap?.Invoke(releasedKind.ToString()));
            return;
        }

        _lastTapKind = releasedKind;
        _lastTapTimestamp = now;
        _sequenceDirty = false;
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
    }

    private enum ModifierTapKind
    {
        None,
        Control,
        Alt
    }

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
}
