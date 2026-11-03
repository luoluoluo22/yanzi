using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OpenQuickHost;

/// <summary>
/// Observes top-level windows created by native C# mini-apps, including windows
/// they open on a second STA thread. A thread-local CBT hook cannot see those.
/// The short launch correlation window is intentionally conservative: ambiguous
/// concurrent launches are skipped instead of changing an unrelated icon.
/// </summary>
internal static class NativeMiniAppWindowObserver
{
    private const uint EventObjectShow = 0x8002;
    private const int ObjIdWindow = 0;
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;
    private const uint GaRoot = 2;

    private static readonly List<(CommandItem Command, DateTimeOffset Expires)> Pending = [];
    private static readonly Dictionary<uint, CommandItem> WindowThreads = [];
    private static readonly HashSet<IntPtr> Handled = [];
    private static WinEventCallback? _callback;
    private static IntPtr _hook;

    internal static void Arm(CommandItem command)
    {
        if (command.Source != CommandSource.LocalExtension ||
            string.IsNullOrWhiteSpace(command.ExtensionId))
            return;
        var app = System.Windows.Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        try
        {
            app.Dispatcher.Invoke(() =>
            {
                if (_hook == IntPtr.Zero)
                {
                    _callback = OnEvent;
                    _hook = SetWinEventHook(
                        EventObjectShow, EventObjectShow,
                        IntPtr.Zero, _callback, (uint)Environment.ProcessId, 0, 0);
                    if (_hook == IntPtr.Zero)
                        HostAssets.AppendLog("[MiniAppTaskbar] Native window event hook unavailable.");
                }
                Pending.RemoveAll(x => x.Expires < DateTimeOffset.UtcNow);
                Pending.RemoveAll(x => string.Equals(x.Command.ExtensionId,
                    command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                Pending.Add((command, DateTimeOffset.UtcNow.AddSeconds(25)));
            }, DispatcherPriority.Send);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("[MiniAppTaskbar] Native observer arm failed: " + ex.GetType().Name);
        }
    }

    private static void OnEvent(
        IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId,
        uint eventThread, uint eventTime)
    {
        if (eventType != EventObjectShow || objectId != ObjIdWindow || childId != 0 ||
            hwnd == IntPtr.Zero || Handled.Contains(hwnd))
            return;
        try
        {
            var thread = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != (uint)Environment.ProcessId || thread == 0 ||
                GetAncestor(hwnd, GaRoot) != hwnd ||
                GetWindow(hwnd, GwOwner) != IntPtr.Zero ||
                (GetWindowLong(hwnd, GwlExStyle) & WsExToolWindow) != 0)
                return;

            var title = new StringBuilder(260);
            _ = GetWindowText(hwnd, title, title.Capacity);
            var caption = title.ToString();
            if (string.IsNullOrWhiteSpace(caption) ||
                caption is "燕子" or "燕子 · AI 对话" ||
                caption.StartsWith("燕子 · AI 定时任务", StringComparison.Ordinal))
                return;

            // Host-owned WPF dialogs, tool windows and AppExtensionWindow are
            // already managed. Do not relabel them as a native mini-app.
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                try
                {
                    if (new WindowInteropHelper(window).Handle == hwnd)
                        return;
                }
                catch (InvalidOperationException) { }
            }

            if (!WindowThreads.TryGetValue(thread, out var command))
            {
                Pending.RemoveAll(x => x.Expires < DateTimeOffset.UtcNow);
                var candidates = Pending.Select(x => x.Command)
                    .GroupBy(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First()).ToArray();
                if (candidates.Length != 1) return;
                command = candidates[0];
                Pending.RemoveAll(x => string.Equals(x.Command.ExtensionId,
                    command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                WindowThreads[thread] = command;
            }

            Handled.Add(hwnd);
            MiniAppTaskbarIdentity.EnsureNativeHandle(hwnd, command);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("[MiniAppTaskbar] Native window observe failed: " +
                                 ex.GetType().Name + ": " + ex.Message);
        }
    }

    private delegate void WinEventCallback(
        IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId,
        uint eventThread, uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module, WinEventCallback callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
}
