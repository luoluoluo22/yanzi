using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public class InputHookService
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;

    private static LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;
    private static DispatcherTimer? _longPressTimer;
    private static Action? _onLongPress;
    private static bool _isEnabled;

    public static void Start(Action onLongPress)
    {
        if (_isEnabled) return;
        
        _onLongPress = onLongPress;
        _hookID = SetHook(_proc);
        
        _longPressTimer = new DispatcherTimer();
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(500); // 500ms long press
        _longPressTimer.Tick += (s, e) =>
        {
            _longPressTimer.Stop();
            System.Windows.Application.Current.Dispatcher.Invoke(() => _onLongPress?.Invoke());
        };

        _isEnabled = true;
    }

    public static void Stop()
    {
        if (!_isEnabled) return;
        UnhookWindowsHookEx(_hookID);
        _isEnabled = false;
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
            if (wParam == (IntPtr)WM_MBUTTONDOWN)
            {
                _longPressTimer?.Start();
            }
            else if (wParam == (IntPtr)WM_MBUTTONUP)
            {
                _longPressTimer?.Stop();
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
