using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace OpenQuickHost;

/// <summary>
/// 小程序闲置触发器。
/// 闲置 = 键盘/鼠标持续无输入，并且当前前台没有覆盖整个显示器的全屏窗口。
/// </summary>
public sealed class ExtensionIdleTriggerService
{
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, IdleState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _runningExtensions = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;
    private bool _checking;
    private DateTimeOffset? _lastObservedAt;
    public bool IsStarted => _started;
    public DateTimeOffset? LastObservedAt => _lastObservedAt;

    public ExtensionIdleTriggerService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _timer.Tick += CheckTimer_Tick;
    }

    public void Start()
    {
        if (!HostRuntimeProfile.OwnsBackgroundServices || _started)
        {
            return;
        }

        _started = true;
        _lastObservedAt = null;
        _timer.Start();
        HostAssets.AppendLog("ExtensionIdleTrigger: started.");
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _timer.Stop();
        _states.Clear();
        _runningExtensions.Clear();
        HostAssets.AppendLog("ExtensionIdleTrigger: stopped.");
    }

    private async void CheckTimer_Tick(object? sender, EventArgs e)
    {
        if (!_started || _checking)
        {
            return;
        }

        _checking = true;
        try
        {
            var idleDuration = TryGetIdleDuration();
            if (!idleDuration.HasValue)
            {
                return;
            }

            var fullscreen = IsForegroundWindowFullscreen();
            var now = DateTimeOffset.Now;
            _lastObservedAt = now;
            var commands = _mainWindow.GetAllCommands()
                .Where(command => command.Startup?.Idle is { Enabled: true })
                .ToList();

            var liveIds = commands.Select(command => command.ExtensionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleId in _states.Keys.Where(id => !liveIds.Contains(id)).ToList())
            {
                _states.Remove(staleId);
            }

            foreach (var command in commands)
            {
                var idle = command.Startup!.Idle!;
                var afterMinutes = Math.Clamp(idle.AfterMinutes, 1, 1440);
                var repeatMinutes = Math.Clamp(idle.RepeatMinutes, 0, 1440);
                var state = GetState(command.ExtensionId);
                var idleThreshold = TimeSpan.FromMinutes(afterMinutes);
                var belowThreshold = idleDuration.Value < idleThreshold;
                if (belowThreshold)
                {
                    state.TriggeredInCurrentIdlePeriod = false;
                }

                var enabled = _mainWindow.IsExtensionEnabled(command.ExtensionId);
                var blockedByFullscreen = idle.PauseWhenFullscreen && fullscreen;
                var dispatchInFlight = _runningExtensions.Contains(command.ExtensionId);
                var repeatReady = repeatMinutes > 0
                    && state.LastTriggeredAt.HasValue
                    && now - state.LastTriggeredAt.Value >= TimeSpan.FromMinutes(repeatMinutes);

                var shouldTrigger = !belowThreshold
                    && !blockedByFullscreen
                    && enabled
                    && (!state.TriggeredInCurrentIdlePeriod || repeatReady)
                    && !dispatchInFlight;

                if (shouldTrigger)
                {
                    state.TriggeredInCurrentIdlePeriod = true;
                    state.LastTriggeredAt = now;
                    _runningExtensions.Add(command.ExtensionId);
                    dispatchInFlight = true;
                }

                PublishStatus(
                    command,
                    idle,
                    state,
                    idleDuration.Value,
                    fullscreen,
                    enabled,
                    dispatchInFlight,
                    now);

                if (shouldTrigger)
                {
                    _ = ExecuteIdleExtensionAsync(command, idleDuration.Value, fullscreen);
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ExtensionIdleTrigger: check failed: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }

        await Task.CompletedTask;
    }

    private async Task ExecuteIdleExtensionAsync(CommandItem command, TimeSpan idleDuration, bool fullscreen)
    {
        try
        {
            HostAssets.AppendLog(
                $"ExtensionIdleTrigger: executing {command.Title} ({command.ExtensionId}), idle={idleDuration.TotalMinutes:F1}m, fullscreen={fullscreen}.");
            await _mainWindow.ExecuteIdleTriggeredExtensionAsync(command);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ExtensionIdleTrigger: failed to execute {command.ExtensionId}: {ex.Message}");
        }
        finally
        {
            _runningExtensions.Remove(command.ExtensionId);
        }
    }

    private static void PublishStatus(
        CommandItem command,
        ExtensionIdleTriggerDefinition idle,
        IdleState state,
        TimeSpan idleDuration,
        bool fullscreen,
        bool enabled,
        bool dispatchInFlight,
        DateTimeOffset now)
    {
        try
        {
            var afterMinutes = Math.Clamp(idle.AfterMinutes, 1, 1440);
            var repeatMinutes = Math.Clamp(idle.RepeatMinutes, 0, 1440);
            var threshold = TimeSpan.FromMinutes(afterMinutes);
            DateTimeOffset? nextEligibleAt = null;
            string reason;

            if (!enabled)
            {
                reason = "disabled";
            }
            else if (idle.PauseWhenFullscreen && fullscreen)
            {
                reason = "fullscreen";
            }
            else if (idleDuration < threshold)
            {
                nextEligibleAt = now + (threshold - idleDuration);
                reason = "waiting-idle";
            }
            else if (dispatchInFlight)
            {
                if (repeatMinutes > 0 && state.LastTriggeredAt.HasValue)
                {
                    nextEligibleAt = state.LastTriggeredAt.Value.AddMinutes(repeatMinutes);
                }
                reason = "dispatching";
            }
            else if (!state.TriggeredInCurrentIdlePeriod)
            {
                nextEligibleAt = now;
                reason = "ready";
            }
            else if (repeatMinutes > 0 && state.LastTriggeredAt.HasValue)
            {
                nextEligibleAt = state.LastTriggeredAt.Value.AddMinutes(repeatMinutes);
                reason = nextEligibleAt <= now ? "ready" : "repeat";
            }
            else
            {
                reason = "once-per-idle";
            }

            var json = JsonSerializer.Serialize(new
            {
                observedAt = now,
                idleSeconds = Math.Max(0, (int)Math.Floor(idleDuration.TotalSeconds)),
                fullscreen,
                enabled,
                afterMinutes,
                repeatMinutes,
                triggeredInCurrentIdlePeriod = state.TriggeredInCurrentIdlePeriod,
                lastTriggeredAt = state.LastTriggeredAt,
                nextEligibleAt,
                reason
            });

            var directory = ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId);
            var path = Path.Combine(directory, "idle-trigger-status.json");
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog(
                $"ExtensionIdleTrigger: status publish failed for {command.ExtensionId}: {ex.Message}");
        }
    }

    private IdleState GetState(string extensionId)
    {
        if (!_states.TryGetValue(extensionId, out var state))
        {
            state = new IdleState();
            _states[extensionId] = state;
        }

        return state;
    }

    private static TimeSpan? TryGetIdleDuration()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        var currentTick = unchecked((uint)Environment.TickCount);
        var elapsedMilliseconds = unchecked(currentTick - info.Time);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    internal static bool IsForegroundWindowFullscreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsIconic(hwnd))
        {
            return false;
        }

        var className = new StringBuilder(128);
        _ = GetClassName(hwnd, className, className.Capacity);
        var cls = className.ToString();
        if (string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        const int tolerance = 4;
        return windowRect.Left <= info.Monitor.Left + tolerance
            && windowRect.Top <= info.Monitor.Top + tolerance
            && windowRect.Right >= info.Monitor.Right - tolerance
            && windowRect.Bottom >= info.Monitor.Bottom - tolerance;
    }

    private sealed class IdleState
    {
        public bool TriggeredInCurrentIdlePeriod { get; set; }
        public DateTimeOffset? LastTriggeredAt { get; set; }
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
}
