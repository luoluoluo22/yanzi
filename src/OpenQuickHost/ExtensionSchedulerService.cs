using System.Windows.Threading;

namespace OpenQuickHost;

/// <summary>
/// 扩展定时调度服务 - 解析 cron 表达式并在指定时间执行扩展
/// </summary>
public sealed class ExtensionSchedulerService
{
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _checkTimer;
    private readonly Dictionary<string, DateTime> _nextRunTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _runningExtensions = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    public ExtensionSchedulerService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        _checkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30) // 每 30 秒检查一次
        };
        _checkTimer.Tick += CheckTimer_Tick;
    }

    /// <summary>
    /// 启动调度服务，扫描所有有 Schedule 的扩展并计算下次执行时间
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        RefreshSchedules();
        _checkTimer.Start();
        HostAssets.AppendLog($"ExtensionScheduler: started, {_nextRunTimes.Count} scheduled extension(s).");
    }

    /// <summary>
    /// 停止调度服务
    /// </summary>
    public void Stop()
    {
        _started = false;
        _checkTimer.Stop();
        _nextRunTimes.Clear();
        HostAssets.AppendLog("ExtensionScheduler: stopped.");
    }

    /// <summary>
    /// 当扩展的 Schedule 被修改后调用，刷新该扩展的下次执行时间
    /// </summary>
    public void RefreshSchedules()
    {
        _nextRunTimes.Clear();
        var allCommands = _mainWindow.GetAllCommands();
        var now = DateTime.Now;

        foreach (var command in allCommands)
        {
            if (string.IsNullOrWhiteSpace(command.Startup?.Schedule))
                continue;

            var nextRun = CalculateNextRun(command.Startup.Schedule, now);
            if (nextRun.HasValue)
            {
                _nextRunTimes[command.ExtensionId] = nextRun.Value;
            }
        }

        HostAssets.AppendLog($"ExtensionScheduler: refreshed, {_nextRunTimes.Count} scheduled extension(s).");
    }

    /// <summary>
    /// 刷新单个扩展的调度
    /// </summary>
    public void RefreshExtension(string extensionId, string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            _nextRunTimes.Remove(extensionId);
            HostAssets.AppendLog($"ExtensionScheduler: removed schedule for {extensionId}.");
            return;
        }

        var nextRun = CalculateNextRun(schedule, DateTime.Now);
        if (nextRun.HasValue)
        {
            _nextRunTimes[extensionId] = nextRun.Value;
            HostAssets.AppendLog($"ExtensionScheduler: updated {extensionId}, next run at {nextRun.Value:yyyy-MM-dd HH:mm}.");
        }
        else
        {
            _nextRunTimes.Remove(extensionId);
            HostAssets.AppendLog($"ExtensionScheduler: failed to parse schedule for {extensionId}: {schedule}");
        }
    }

    private async void CheckTimer_Tick(object? sender, EventArgs e)
    {
        if (!_started) return;

        var now = DateTime.Now;
        var toRun = new List<(string extensionId, DateTime scheduledTime)>();

        foreach (var kvp in _nextRunTimes.ToList())
        {
            if (now >= kvp.Value)
            {
                toRun.Add((kvp.Key, kvp.Value));
            }
        }

        foreach (var (extensionId, _) in toRun)
        {
            if (_runningExtensions.Contains(extensionId))
            {
                HostAssets.AppendLog($"ExtensionScheduler: skipping {extensionId}, already running.");
                continue;
            }

            var command = _mainWindow.GetAllCommands()
                .FirstOrDefault(c => c.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));

            if (command == null)
            {
                _nextRunTimes.Remove(extensionId);
                continue;
            }

            // Check if extension is enabled
            if (!_mainWindow.IsExtensionEnabled(command.ExtensionId))
            {
                // Still calculate next run time so it's ready when re-enabled
                UpdateNextRunTime(extensionId, command.Startup?.Schedule);
                continue;
            }

            _runningExtensions.Add(extensionId);
            HostAssets.AppendLog($"ExtensionScheduler: executing {command.Title} ({extensionId}).");

            try
            {
                await _mainWindow.ExecuteScheduledExtensionAsync(command);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"ExtensionScheduler: failed to execute {extensionId}: {ex.Message}");
            }
            finally
            {
                _runningExtensions.Remove(extensionId);
            }

            // Calculate next run time
            UpdateNextRunTime(extensionId, command.Startup?.Schedule);
        }
    }

    private void UpdateNextRunTime(string extensionId, string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            _nextRunTimes.Remove(extensionId);
            return;
        }

        var nextRun = CalculateNextRun(schedule, DateTime.Now);
        if (nextRun.HasValue)
        {
            _nextRunTimes[extensionId] = nextRun.Value;
        }
        else
        {
            _nextRunTimes.Remove(extensionId);
        }
    }

    // ─── Cron 解析 ───────────────────────────────────────

    /// <summary>
    /// 计算 cron 表达式的下一次执行时间（从 now 开始往后找，最多找 366 天）
    /// 支持格式：分 时 日 月 周
    /// 支持 */N, N-M, 逗号分隔, * 通配
    /// </summary>
    private static DateTime? CalculateNextRun(string cron, DateTime from)
    {
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return null;

        if (!TryParseCronField(parts[0], 0, 59, out var minutes)) return null;
        if (!TryParseCronField(parts[1], 0, 23, out var hours)) return null;
        if (!TryParseCronField(parts[2], 1, 31, out var daysOfMonth)) return null;
        if (!TryParseCronField(parts[3], 1, 12, out var months)) return null;
        if (!TryParseCronField(parts[4], 0, 7, out var daysOfWeek)) return null;

        // Normalize: cron uses 0=Sun and 7=Sun, convert 7 to 0
        if (daysOfWeek.Contains(7))
        {
            daysOfWeek.Add(0);
            daysOfWeek.Remove(7);
        }

        // Start searching from the next minute
        var candidate = from.AddMinutes(1);
        candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, candidate.Minute, 0);

        var maxDate = from.AddDays(366);

        while (candidate < maxDate)
        {
            if (!months.Contains(candidate.Month))
            {
                // Skip to next month
                candidate = new DateTime(candidate.Year, candidate.Month, 1).AddMonths(1);
                continue;
            }

            if (!daysOfMonth.Contains(candidate.Day))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            // Check day of week (DayOfWeek: 0=Sun, 1=Mon, ..., 6=Sat)
            var dow = (int)candidate.DayOfWeek;
            if (!daysOfWeek.Contains(dow))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!hours.Contains(candidate.Hour))
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0).AddHours(1);
                continue;
            }

            if (!minutes.Contains(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// 解析单个 cron 字段，支持 *, */N, N, N-M, N,M,... 及其组合
    /// </summary>
    private static bool TryParseCronField(string field, int min, int max, out HashSet<int> values)
    {
        values = new HashSet<int>();

        foreach (var segment in field.Split(','))
        {
            var s = segment.Trim();
            if (string.IsNullOrEmpty(s)) continue;

            // */N or *
            if (s.StartsWith("*/"))
            {
                if (!int.TryParse(s[2..], out int step) || step <= 0) return false;
                for (int i = min; i <= max; i += step) values.Add(i);
            }
            else if (s == "*")
            {
                for (int i = min; i <= max; i++) values.Add(i);
            }
            // N-M range
            else if (s.Contains('-'))
            {
                var rangeParts = s.Split('-');
                if (rangeParts.Length != 2) return false;
                if (!int.TryParse(rangeParts[0], out int rangeStart)) return false;
                if (!int.TryParse(rangeParts[1], out int rangeEnd)) return false;
                for (int i = rangeStart; i <= rangeEnd; i++)
                {
                    if (i >= min && i <= max) values.Add(i);
                }
            }
            // Single value
            else
            {
                if (!int.TryParse(s, out int val)) return false;
                if (val >= min && val <= max) values.Add(val);
            }
        }

        return values.Count > 0;
    }
}
