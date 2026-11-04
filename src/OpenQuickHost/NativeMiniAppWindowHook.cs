using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenQuickHost;

/// <summary>
/// A thread-scoped CBT hook identifies true top-level native WPF windows created
/// by an in-process C# mini-app. It does not intercept other programs, host UI,
/// tool windows or overlays. The hook is removed when the extension run ends.
/// </summary>
internal sealed class NativeMiniAppWindowHook : IDisposable
{
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const int WmSetIcon = 0x0080;

    private static readonly ConcurrentDictionary<string, IntPtr> LoadedIcons =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly CommandItem _command;
    private readonly HookCallback _callback;
    private readonly HashSet<IntPtr> _handled = [];
    private readonly uint _threadId;
    private IntPtr _hook;

    private NativeMiniAppWindowHook(CommandItem command)
    {
        _command = command;
        _threadId = GetCurrentThreadId();
        _callback = OnCbtEvent;
        _hook = SetWindowsHookEx(WhCbt, _callback, IntPtr.Zero, _threadId);
        if (_hook == IntPtr.Zero)
            HostAssets.AppendLog("[MiniAppTaskbar] Native CBT hook unavailable: " + command.ExtensionId);
    }

    internal static NativeMiniAppWindowHook Install(CommandItem command) => new(command);

    private IntPtr OnCbtEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HcbtActivate && wParam != IntPtr.Zero && !_handled.Contains(wParam))
        {
            try
            {
                var ownerThread = GetWindowThreadProcessId(wParam, out var pid);
                if (ownerThread == _threadId && pid == (uint)Environment.ProcessId &&
                    GetWindow(wParam, GwOwner) == IntPtr.Zero &&
                    (GetWindowLong(wParam, GwlExStyle) & WsExToolWindow) == 0)
                {
                    var title = new StringBuilder(256);
                    _ = GetWindowText(wParam, title, title.Capacity);
                    if (!string.IsNullOrWhiteSpace(title.ToString()))
                    {
                        _handled.Add(wParam);
                        MiniAppTaskbarIdentity.EnsureNativeHandle(wParam, _command);
                    }
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog("[MiniAppTaskbar] Native window hook error: " +
                                     ex.GetType().Name);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    internal static void ApplyWindowIcon(IntPtr hwnd, string iconPath)
    {
        var icon = LoadedIcons.GetOrAdd(iconPath, path =>
            LoadImage(IntPtr.Zero, path, ImageIcon, 64, 64, LrLoadFromFile));
        if (icon == IntPtr.Zero) return;
        _ = SendMessage(hwnd, WmSetIcon, IntPtr.Zero, icon);
        _ = SendMessage(hwnd, WmSetIcon, (IntPtr)1, icon);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId, HookCallback callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder value, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string image, int type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
}
