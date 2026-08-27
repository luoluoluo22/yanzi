using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace OpenQuickHost;

public sealed class WindowBoundExtensionsService : IDisposable
{
    private readonly MainWindow _host;
    private readonly DispatcherTimer _fallbackTimer;
    private readonly EventHandler _fallbackTickHandler;
    private readonly Dictionary<string, WindowBoundExtensionOverlayWindow> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DispatcherTimer> _hideTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hoverVisible = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IntPtr> _trackedTargetWindows = [];
    private readonly WinEventDelegate _winEventCallback;
    private WindowBindingSettings _settings = new();
    private IntPtr _lastForegroundWindow;
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;

    public WindowBoundExtensionsService(MainWindow host)
    {
        _host = host;
        _fallbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _fallbackTickHandler = (_, _) => SafeTick();
        _fallbackTimer.Tick += _fallbackTickHandler;
        _winEventCallback = HandleWinEvent;
    }

    public void Start(WindowBindingSettings settings)
    {
        _settings = settings ?? new WindowBindingSettings();
        EnsureWinEventHooks();
        if (!_fallbackTimer.IsEnabled)
        {
            _fallbackTimer.Start();
        }

        _lastForegroundWindow = GetForegroundWindow();
        SafeTick();
    }

    public void Reload(WindowBindingSettings settings)
    {
        _settings = settings ?? new WindowBindingSettings();
        if (!_settings.Enabled)
        {
            HideAll();
            return;
        }

        SafeTick();
    }

    public void Stop()
    {
        _fallbackTimer.Stop();
        RemoveWinEventHooks();
        HideAll();
    }

    private void SafeTick()
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Window binding tick failed: {ex}");
            HideAll();
        }
    }

    private void Tick()
    {
        if (!_settings.Enabled || _settings.Rules.Count == 0)
        {
            HideAll();
            _trackedTargetWindows.Clear();
            return;
        }

        var foregroundWindow = GetForegroundWindow();
        var foregroundChanged = foregroundWindow != _lastForegroundWindow;
        if (foregroundChanged)
        {
            _lastForegroundWindow = foregroundWindow;
        }

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trackedWindows = new HashSet<IntPtr>();

        foreach (var hwnd in EnumerateTopLevelWindows())
        {
            if (!IsValidTargetWindow(hwnd) ||
                !TryDescribeWindow(hwnd, out var processName, out var className, out var title, out var rect, out var dpi))
            {
                continue;
            }

            var matches = _settings.Rules
                .Where(rule => rule.Enabled && IsRuleMatch(rule, processName, className, title))
                .ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            trackedWindows.Add(hwnd);

            foreach (var rule in matches)
            {
                if (!_host.TryResolveExtensionCommand(rule.ExtensionId, out var command))
                {
                    continue;
                }

                var overlayKey = BuildOverlayKey(rule.Id, hwnd);
                keep.Add(overlayKey);

                if (_overlays.TryGetValue(overlayKey, out var overlayWindow))
                {
                    UpdateOverlayPosition(overlayWindow, rect, dpi, rule);
                    UpdateHoverVisibility(overlayKey, rule, overlayWindow);
                    SyncOverlayZOrder(hwnd, rule, overlayWindow);
                    continue;
                }

                var targetWindow = hwnd;
                var overlay = new WindowBoundExtensionOverlayWindow(
                    command,
                    getTargetWindowHandle: () => targetWindow,
                    onExecute: () => _host.ExecuteExtensionFromWindowBinding(rule.ExtensionId),
                    onContextMenu: window => _host.ShowWindowBindingContextMenu(command, rule.Id, window),
                    onMoved: window => SaveOverlayOffset(rule.Id, targetWindow, window));

                UpdateOverlayPosition(overlay, rect, dpi, rule);

                if (rule.HoverMode)
                {
                    // Start hidden for hover mode.
                    overlay.Opacity = 0;
                    overlay.Show();
                    overlay.Visibility = Visibility.Hidden;
                }
                else
                {
                    overlay.Show();
                }

                _overlays[overlayKey] = overlay;
                SyncOverlayZOrder(hwnd, rule, overlay);
                HostAssets.AppendLog($"Window binding overlay shown: extension={rule.ExtensionId}, process={processName}, class={className}, corner={rule.Corner}.");
            }
        }

        foreach (var existing in _overlays.Keys.ToList())
        {
            if (!keep.Contains(existing))
            {
                CloseOverlay(existing);
            }
        }

        _trackedTargetWindows.Clear();
        foreach (var hwnd in trackedWindows)
        {
            _trackedTargetWindows.Add(hwnd);
        }
    }

    private void SyncOverlayZOrder(
        IntPtr hwnd,
        WindowBindingRuleSettings rule,
        WindowBoundExtensionOverlayWindow overlay)
    {
        var targetIsForeground = hwnd != IntPtr.Zero && hwnd == _lastForegroundWindow;
        if (targetIsForeground && rule.HoverMode && (overlay.Visibility != Visibility.Visible || overlay.Opacity <= 0))
        {
            targetIsForeground = false;
        }

        overlay.SyncOverlayZOrder(hwnd, targetIsForeground);
    }

    public void RefreshForWindow(IntPtr hwnd)
    {
        try
        {
            RefreshForWindowCore(hwnd);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Window binding explicit refresh failed: {ex}");
        }
    }

    private void RefreshForWindowCore(IntPtr hwnd)
    {
        if (!_settings.Enabled || _settings.Rules.Count == 0 || !IsValidTargetWindow(hwnd))
        {
            return;
        }

        if (!TryDescribeWindow(hwnd, out var processName, out var className, out var title, out var rect, out var dpi))
        {
            return;
        }

        var matches = _settings.Rules
            .Where(rule => rule.Enabled && IsRuleMatch(rule, processName, className, title))
            .ToList();

        foreach (var rule in matches)
        {
            if (!_host.TryResolveExtensionCommand(rule.ExtensionId, out var command))
            {
                continue;
            }

            var overlayKey = BuildOverlayKey(rule.Id, hwnd);
            if (!_overlays.TryGetValue(overlayKey, out var overlay))
            {
                var targetWindow = hwnd;
                overlay = new WindowBoundExtensionOverlayWindow(
                    command,
                    getTargetWindowHandle: () => targetWindow,
                    onExecute: () => _host.ExecuteExtensionFromWindowBinding(rule.ExtensionId),
                    onContextMenu: window => _host.ShowWindowBindingContextMenu(command, rule.Id, window),
                    onMoved: window => SaveOverlayOffset(rule.Id, targetWindow, window));
                _overlays[overlayKey] = overlay;
            }

            UpdateOverlayPosition(overlay, rect, dpi, rule);
            UpdateHoverVisibility(overlayKey, rule, overlay);
            SyncOverlayZOrder(hwnd, rule, overlay);
        }

        _trackedTargetWindows.Add(hwnd);
    }

    private const double OverlayPaddingDip = 17; // Padding around the 34×34 icon content within the 68×68 window

    private void UpdateOverlayPosition(WindowBoundExtensionOverlayWindow overlay, RECT rect, uint dpi, WindowBindingRuleSettings rule)
    {
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var marginDip = _settings.MarginPixels / scale;
        var contentWidth = overlay.Width - OverlayPaddingDip * 2; // 34
        var contentHeight = overlay.Height - OverlayPaddingDip * 2; // 34
        var widthDip = overlay.Width;
        var heightDip = overlay.Height;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        var baseLeft = GetBaseLeft(rect, dpi, contentWidth, rule.Corner, marginDip) - OverlayPaddingDip;
        var baseTop = GetBaseTop(rect, dpi, contentHeight, rule.Corner, marginDip) - OverlayPaddingDip;
        var desiredLeft = baseLeft + rule.OffsetX;
        var desiredTop = baseTop + rule.OffsetY;

        // For interior positions, also clamp within the target window bounds
        if (WindowBindingCorners.IsInterior(rule.Corner))
        {
            var scale2 = dpi <= 0 ? 1 : dpi / 96.0;
            var winLeftDip = rect.Left / scale2;
            var winTopDip = rect.Top / scale2;
            var winRightDip = rect.Right / scale2;
            var winBottomDip = rect.Bottom / scale2;
            desiredLeft = Math.Clamp(desiredLeft, winLeftDip, Math.Max(winLeftDip, winRightDip - widthDip));
            desiredTop = Math.Clamp(desiredTop, winTopDip, Math.Max(winTopDip, winBottomDip - heightDip));
        }

        overlay.Left = Math.Clamp(desiredLeft, virtualLeft, Math.Max(virtualLeft, virtualRight - widthDip));
        overlay.Top = Math.Clamp(desiredTop, virtualTop, Math.Max(virtualTop, virtualBottom - heightDip));
    }

    private void SaveOverlayOffset(string ruleId, IntPtr targetWindow, WindowBoundExtensionOverlayWindow overlay)
    {
        var rule = _settings.Rules.FirstOrDefault(item => item.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (rule == null || targetWindow == IntPtr.Zero ||
            !TryDescribeWindow(targetWindow, out _, out _, out _, out var rect, out var dpi))
        {
            return;
        }

        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var marginDip = _settings.MarginPixels / scale;
        var contentWidth = overlay.Width - OverlayPaddingDip * 2;
        var contentHeight = overlay.Height - OverlayPaddingDip * 2;
        var baseLeft = GetBaseLeft(rect, dpi, contentWidth, rule.Corner, marginDip) - OverlayPaddingDip;
        var baseTop = GetBaseTop(rect, dpi, contentHeight, rule.Corner, marginDip) - OverlayPaddingDip;
        var offsetX = RoundToGrid(overlay.Left - baseLeft, 10);
        var offsetY = RoundToGrid(overlay.Top - baseTop, 10);
        _host.UpdateWindowBindingOffset(rule.Id, offsetX, offsetY);
        rule.OffsetX = offsetX;
        rule.OffsetY = offsetY;
        UpdateOverlayPosition(overlay, rect, dpi, rule);
    }

    private static double GetBaseLeft(RECT rect, uint dpi, double widthDip, string corner, double marginDip)
    {
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var leftDip = rect.Left / scale;
        var rightDip = rect.Right / scale;
        var normalizedCorner = WindowBindingCorners.Normalize(corner);

        if (WindowBindingCorners.IsInterior(normalizedCorner))
        {
            // Interior positions: place inside the window bounds
            return normalizedCorner switch
            {
                WindowBindingCorners.InsideTopRight or WindowBindingCorners.InsideBottomRight
                    => rightDip - widthDip - marginDip,
                _ => leftDip + marginDip // InsideTopLeft, InsideBottomLeft
            };
        }

        // External positions: place outside the window bounds
        return normalizedCorner switch
        {
            WindowBindingCorners.TopRight or WindowBindingCorners.BottomRight => rightDip + marginDip,
            _ => leftDip - widthDip - marginDip
        };
    }

    private static double GetBaseTop(RECT rect, uint dpi, double heightDip, string corner, double marginDip = 0)
    {
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var topDip = rect.Top / scale;
        var bottomDip = rect.Bottom / scale;
        var normalizedCorner = WindowBindingCorners.Normalize(corner);

        if (WindowBindingCorners.IsInterior(normalizedCorner))
        {
            // Interior positions: place inside the window bounds
            return normalizedCorner switch
            {
                WindowBindingCorners.InsideBottomLeft or WindowBindingCorners.InsideBottomRight
                    => bottomDip - heightDip - marginDip,
                _ => topDip + marginDip // InsideTopLeft, InsideTopRight
            };
        }

        // External positions: original logic
        return normalizedCorner switch
        {
            WindowBindingCorners.BottomLeft or WindowBindingCorners.BottomRight => bottomDip - heightDip,
            _ => topDip
        };
    }

    private static int RoundToGrid(double value, int gridSize)
    {
        return (int)Math.Round(value / gridSize, MidpointRounding.AwayFromZero) * gridSize;
    }

    private static bool IsRuleMatch(WindowBindingRuleSettings rule, string processName, string className, string title)
    {
        if (!IsProcessNameMatch(rule.ProcessName, processName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.WindowClass) &&
            !string.Equals(rule.WindowClass, className, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.TitleContains) &&
            title.IndexOf(rule.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    private static bool IsProcessNameMatch(string ruleProcess, string processName)
    {
        ruleProcess = NormalizeProcessName(ruleProcess);
        processName = NormalizeProcessName(processName);
        if (ruleProcess.Length == 0)
        {
            return false;
        }

        return string.Equals(ruleProcess, processName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProcessName(string value)
    {
        value = (value ?? string.Empty).Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }

    private static string BuildOverlayKey(string ruleId, IntPtr hwnd) => $"{ruleId}:{hwnd.ToInt64():X}";

    private static IntPtr[] EnumerateTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    private void HideAll()
    {
        foreach (var overlayKey in _overlays.Keys.ToList())
        {
            CloseOverlay(overlayKey);
        }
        _overlays.Clear();
        foreach (var timer in _hideTimers.Values)
        {
            timer.Stop();
        }
        _hideTimers.Clear();
        _hoverVisible.Clear();
        _trackedTargetWindows.Clear();
    }

    private void CloseOverlay(string overlayKey)
    {
        CancelScheduledHide(overlayKey);
        _hoverVisible.Remove(overlayKey);
        if (_overlays.Remove(overlayKey, out var overlay))
        {
            overlay.Close();
        }
    }

    private void UpdateHoverVisibility(string overlayKey, WindowBindingRuleSettings rule, WindowBoundExtensionOverlayWindow overlay)
    {
        if (!rule.HoverMode)
        {
            CancelScheduledHide(overlayKey);
            _hoverVisible.Remove(overlayKey);
            overlay.ShowImmediately();
            return;
        }

        var inZone = IsInHoverDetectionZone(overlay);

        if (inZone)
        {
            CancelScheduledHide(overlayKey);
            if (!_hoverVisible.Contains(overlayKey))
            {
                _hoverVisible.Add(overlayKey);
                overlay.Visibility = Visibility.Visible;
                overlay.AnimateFadeIn();
            }
            else if (!overlay.IsVisible || overlay.Visibility != Visibility.Visible)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.Show();
            }
        }
        else
        {
            if (_hoverVisible.Contains(overlayKey))
            {
                ScheduleHideOverlay(overlayKey, overlay);
            }
            else
            {
                overlay.HideImmediately();
            }
        }
    }

    private static bool IsInHoverDetectionZone(WindowBoundExtensionOverlayWindow overlay)
    {
        if (!GetCursorPos(out var point))
        {
            return false;
        }

        var dpi = ScreenHelper.GetDpiForWindow(GetForegroundWindow());
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var x = point.X / scale;
        var y = point.Y / scale;
        const double padding = 20;

        return x >= overlay.Left - padding && x <= overlay.Left + overlay.Width + padding &&
               y >= overlay.Top - padding && y <= overlay.Top + overlay.Height + padding;
    }

    private void ScheduleHideOverlay(string overlayKey, WindowBoundExtensionOverlayWindow overlay)
    {
        if (_hideTimers.ContainsKey(overlayKey))
        {
            return; // Already scheduled
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _hideTimers.Remove(overlayKey);
            _hoverVisible.Remove(overlayKey);
            if (_overlays.TryGetValue(overlayKey, out var currentOverlay) && currentOverlay.IsVisible)
            {
                currentOverlay.AnimateFadeOut(() =>
                {
                    if (_overlays.TryGetValue(overlayKey, out var o))
                    {
                        if (!IsHoverModeEnabledForOverlayKey(overlayKey))
                        {
                            o.ShowImmediately();
                            return;
                        }

                        o.Visibility = Visibility.Hidden;
                    }
                });
            }
        };
        _hideTimers[overlayKey] = timer;
        timer.Start();
    }

    private void CancelScheduledHide(string overlayKey)
    {
        if (_hideTimers.Remove(overlayKey, out var timer))
        {
            timer.Stop();
        }
    }

    private bool IsHoverModeEnabledForOverlayKey(string overlayKey)
    {
        var separatorIndex = overlayKey.IndexOf(':');
        var ruleId = separatorIndex >= 0 ? overlayKey[..separatorIndex] : overlayKey;
        return _settings.Rules.Any(rule =>
            rule.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            rule.HoverMode);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static bool IsValidTargetWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        if (!TryGetWindowThreadProcessId(hwnd, out var pid))
        {
            return false;
        }

        if (pid == 0)
        {
            return false;
        }

        try
        {
            if (pid == (uint)Environment.ProcessId)
            {
                return false;
            }
        }
        catch
        {
            // Best effort.
        }

        return true;
    }

    private static bool TryDescribeWindow(IntPtr hwnd, out string processName, out string className, out string title, out RECT rect, out uint dpi)
    {
        processName = string.Empty;
        className = string.Empty;
        title = string.Empty;
        rect = default;
        dpi = 96;

        try
        {
            if (!GetWindowRect(hwnd, out rect))
            {
                return false;
            }

            if (!TryGetWindowThreadProcessId(hwnd, out var pid))
            {
                return false;
            }

            processName = pid == 0 ? string.Empty : Process.GetProcessById((int)pid).ProcessName;

            var classBuilder = new StringBuilder(256);
            _ = GetClassName(hwnd, classBuilder, classBuilder.Capacity);
            className = classBuilder.ToString();

            var titleBuilder = new StringBuilder(1024);
            _ = GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            title = titleBuilder.ToString();

            dpi = ScreenHelper.GetDpiForWindow(hwnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            return processName.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _fallbackTimer.Tick -= _fallbackTickHandler;
    }

    private void EnsureWinEventHooks()
    {
        if (_foregroundHook == IntPtr.Zero)
        {
            _foregroundHook = SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                _winEventCallback,
                0,
                0,
                WineventOutofcontext | WineventSkipownprocess);
        }

        if (_locationHook == IntPtr.Zero)
        {
            _locationHook = SetWinEventHook(
                EventObjectLocationchange,
                EventObjectLocationchange,
                IntPtr.Zero,
                _winEventCallback,
                0,
                0,
                WineventOutofcontext | WineventSkipownprocess);
        }
    }

    private void RemoveWinEventHooks()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        if (_locationHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
    }

    private void HandleWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (eventType == EventObjectLocationchange)
        {
            var movedWindow = hwnd;
            _host.Dispatcher.BeginInvoke(() =>
            {
                if (_trackedTargetWindows.Contains(movedWindow))
                {
                    SafeTick();
                }
            }, DispatcherPriority.Background);
            return;
        }

        _host.Dispatcher.BeginInvoke(SafeTick, DispatcherPriority.Background);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessIdNative(IntPtr hWnd, out uint lpdwProcessId);

    private static bool TryGetWindowThreadProcessId(IntPtr hWnd, out uint pid)
    {
        pid = 0;
        try
        {
            _ = GetWindowThreadProcessIdNative(hWnd, out pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationchange = 0x800B;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
}
