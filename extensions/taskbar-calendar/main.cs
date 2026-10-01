using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Media;
using System.Text.Json;
using OpenQuickHost.CSharpRuntime;
using System.Windows.Interop;
using System.Windows.Automation;


public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        string input = context.InputText?.Trim() ?? string.Empty;

        // 1. 跨 AssemblyLoadContext 检查宿主是否已有常驻运行的服务实例
        var existingService = GetExistingRunningService(context);
        if (existingService != null)
        {
            return await HandleCommandForRunningServiceAsync(existingService, input, context);
        }

        // 2. 首次启动常驻服务
        var service = TaskbarCalendarService.Instance;
        service.Initialize(context);

        // 注册到宿主 HostObjectRegistry，供后续呼出交互及右键“终止小程序”反射调用 Quit()
        try
        {
            context.RegisterObject?.Invoke("taskbar-calendar-service", service);
            context.RegisterObject?.Invoke($"{context.ExtensionId}-window", service);
        }
        catch { }

        // 3. 区分触发场景：开机自启（静默常驻，不弹大窗扰民） vs 用户主动唤起（弹出日历）
        bool isStartup = string.Equals(context.LaunchSource, "app-startup", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(context.LaunchSource, "startup", StringComparison.OrdinalIgnoreCase);

        if (isStartup)
        {
            context.Log("【日历】开机自启动初始化完毕，已在后台静默建立常驻监听。");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(input))
            {
                await HandleCommandForRunningServiceAsync(service, input, context);
            }
            else
            {
                service.ShowCalendar();
            }
        }

        // 4. 关键：进入异步常驻挂起，使宿主线程保持活跃，RunningExtensionRegistry 保持运行绿点，loadContext 不会被卸载！
        await service.WaitForStopAsync();

        // 5. 退出后注销全局引用
        try
        {
            context.RegisterObject?.Invoke("taskbar-calendar-service", null!);
            context.RegisterObject?.Invoke($"{context.ExtensionId}-window", null!);
        }
        catch { }

        return "日历监听服务已停止。";
    }

    private static object? GetExistingRunningService(YanziActionContext context)
    {
        try
        {
            var obj = context.GetRegisteredObject?.Invoke("taskbar-calendar-service");
            if (obj != null)
            {
                var isRunningProp = obj.GetType().GetProperty("IsRunning");
                if (isRunningProp != null && isRunningProp.GetValue(obj) is bool isRunning && isRunning)
                {
                    return obj;
                }
            }
        }
        catch { }
        return null;
    }

    private static async Task<string> HandleCommandForRunningServiceAsync(object service, string input, YanziActionContext context)
    {
        var svcType = service.GetType();
        if (input.StartsWith("add-reminder:", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("rename-reminder:", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("delete-reminder:", StringComparison.OrdinalIgnoreCase)
            || input == "sync-status" || input == "sync-use-cloud" || input == "sync-keep-local")
            return svcType.GetMethod("Run", [typeof(string)])?.Invoke(service, [input]) as string ?? "日历命令未执行";

        if (string.Equals(input, "test", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "benchmark", StringComparison.OrdinalIgnoreCase))
        {
            var report = svcType.GetMethod("GetBenchmarkReport")?.Invoke(service, null) as string ?? CalendarBenchmarkRunner.RunBenchmark();
            context.Log("基准测试完成:\n" + report);
            svcType.GetMethod("ShowCalendar", [typeof(string)])?.Invoke(service, [report]);
            return report;
        }

        if (string.Equals(input, "hover", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "mode:hover", StringComparison.OrdinalIgnoreCase))
        {
            svcType.GetMethod("SetMode", [typeof(int)])?.Invoke(service, [(int)TriggerMode.Hover]);
            await context.Storage.WriteTextAsync("trigger_mode", "hover", "local");
            return "已成功切换至【形态1：右下角悬停触发角模式】";
        }

        if (string.Equals(input, "click", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "mode:click", StringComparison.OrdinalIgnoreCase))
        {
            svcType.GetMethod("SetMode", [typeof(int)])?.Invoke(service, [(int)TriggerMode.ClickIntercept]);
            await context.Storage.WriteTextAsync("trigger_mode", "click", "local");
            return "已成功切换至【形态2：任务栏时钟点击拦截模式】";
        }

        if (string.Equals(input, "stop", StringComparison.OrdinalIgnoreCase))
        {
            svcType.GetMethod("Stop")?.Invoke(service, null);
            return "日历监听服务已停止。";
        }

        if (string.Equals(input, "status", StringComparison.OrdinalIgnoreCase))
        {
            var curMode = svcType.GetProperty("CurrentMode")?.GetValue(service);
            var isRunning = svcType.GetProperty("IsRunning")?.GetValue(service);
            return $"日历服务运行状态: {isRunning}, 当前工作模式: {curMode}";
        }

        if (input.StartsWith("snapshot:", StringComparison.OrdinalIgnoreCase))
        {
            string path = input.Substring("snapshot:".Length).Trim();
            var res = svcType.GetMethod("CaptureSnapshot")?.Invoke(service, [path]) as string;
            return res ?? "ok";
        }

        if (input.StartsWith("test-range:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Substring("test-range:".Length).Split(',');
            if (parts.Length == 2 && DateTime.TryParse(parts[0], out var s) && DateTime.TryParse(parts[1], out var e))
            {
                svcType.GetMethod("TestSelectRange")?.Invoke(service, [s, e]);
                return $"已选择区间: {s:yyyy-MM-dd} 至 {e:yyyy-MM-dd}";
            }
        }

        if (input.StartsWith("test-calc:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(input.Substring("test-calc:".Length), out var days))
            {
                svcType.GetMethod("TestOpenCalc")?.Invoke(service, [DateTime.Today, days]);
                return $"已打开推算面板，推算 {days} 天";
            }
        }

        if (input.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
        {
            var dateStr = input.Substring("select:".Length).Trim();
            if (DateTime.TryParse(dateStr, out var d))
            {
                svcType.GetMethod("TestSelectDate")?.Invoke(service, [d]);
                return $"已选中日期: {d:yyyy-MM-dd}";
            }
        }

        // 默认无参唤起/收起
        svcType.GetMethod("ToggleCalendar")?.Invoke(service, null);
        return "日历已切换展示。";
    }
}

public enum TriggerMode
{
    Hover = 1,
    ClickIntercept = 2
}

/// <summary>
/// 后台常驻监听服务（单例）
/// </summary>
public class TaskbarCalendarService
{
    public static readonly TaskbarCalendarService Instance = new();
    private readonly object _syncLock = new();
    private volatile bool _initialized;
    private volatile bool _shouldStop;
    private volatile TriggerMode _mode = TriggerMode.Hover;
    private TaskCompletionSource<bool> _stopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _threadsRemaining;
    private YanziActionContext? _context;
    private Dispatcher? _uiDispatcher;
    private Dispatcher? _hookDispatcher;
    private CalendarWindow? _calendarWindow;
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _hookRetryTimer;
    private int _hookRetries;
    private IntPtr _mouseHookHandle;
    private LowLevelMouseProc? _mouseHookDelegate;
    private NativeMethods.WinEventProc? _winEventDelegate;
    private IntPtr _winEventHook;
    private TaskbarClockCache? _clockCache;
    private bool _swallowLeftUp;
    // 监听线程只读轻量快照，不跨线程访问 WPF 对象。
    private volatile bool _flyoutVisible;
    private volatile bool _menuOpen;
    private volatile bool _suppressClicks;
    private IntPtr _windowHandle;
    private DateTime _hoverStartTime = DateTime.MinValue;
    private bool _isHoverTriggered;

    public TriggerMode CurrentMode => _mode;
    public bool IsRunning => _initialized && !_shouldStop;
    // Public, read-only window metadata for the host's scoped UI testing API.
    public IntPtr WindowHandle => _windowHandle;
    public bool IsFlyoutVisible => _flyoutVisible;
    public YanziActionContext? ActionContext => _context;
    public Task WaitForStopAsync() => _stopSignal.Task;
    public string GetBenchmarkReport() => CalendarBenchmarkRunner.RunBenchmark();
    public TaskbarClockCache.Region[] ClockRegions => _clockCache?.Snapshot ?? Array.Empty<TaskbarClockCache.Region>();
    public void Quit() => Stop();

    public void Initialize(YanziActionContext context)
    {
        lock (_syncLock)
        {
            if (_initialized) return;
            _context = context;
            CalendarReminderManager.Instance.Initialize(context);
            _mode = TriggerMode.ClickIntercept;
            _shouldStop = false;
            _initialized = true;
            _threadsRemaining = 2;
            _stopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _clockCache = new TaskbarClockCache(context.Log);
            _clockCache.Start();
            var ui = new Thread(StartUIThread) { IsBackground = true, Name = "TaskbarCalendarUI" };
            ui.SetApartmentState(ApartmentState.STA);
            var hook = new Thread(StartHookThread) { IsBackground = true, Name = "TaskbarCalendarInput" };
            hook.SetApartmentState(ApartmentState.STA);
            ui.Start();
            hook.Start();
            context.Log($"日历已启动，模式: {_mode}；输入监听独立线程，时钟区域后台缓存。");
        }
    }

    private static void Post(Dispatcher? dispatcher, Action action)
    {
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    public void SetMode(TriggerMode mode)
    {
        if (mode != TriggerMode.Hover && mode != TriggerMode.ClickIntercept) return;
        _mode = mode;
        Post(_uiDispatcher, ApplyHoverMode);
        Post(_hookDispatcher, () => { _hookRetries = 0; SyncMouseHook(); });
        _context?.Log($"切换工作模式为: {mode}");
    }
    public void SetMode(int modeValue) => SetMode((TriggerMode)modeValue);
    public async Task SaveModeAsync(string mode)
    {
        if (_context == null) return;
        try { await _context.Storage.WriteTextAsync("trigger_mode", mode, "local"); } catch { }
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_initialized || _shouldStop) return;
            _shouldStop = true;
            CalendarReminderManager.Instance.StopSync();
            _clockCache?.Stop();
            Post(_hookDispatcher, () => _hookDispatcher?.InvokeShutdown());
            Post(_uiDispatcher, () => _uiDispatcher?.InvokeShutdown());
        }
    }

    private void ThreadStopped()
    {
        if (Interlocked.Decrement(ref _threadsRemaining) != 0) return;
        lock (_syncLock)
        {
            _initialized = false;
            _stopSignal.TrySetResult(true);
        }
        _context?.Log("日历已停止，鼠标/窗口事件监听与界面计时器已释放。");
    }

    private void StartUIThread()
    {
        try
        {
            _uiDispatcher = Dispatcher.CurrentDispatcher;
            if (_shouldStop) return;
            _calendarWindow = new CalendarWindow();
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _hoverTimer.Tick += (_, _) => { if (!_shouldStop && _mode == TriggerMode.Hover) CheckHoverCorner(); };
            ApplyHoverMode();
            // ScheduleAutoVerification();
            if (!_shouldStop) Dispatcher.Run();
        }
        catch (Exception ex) { _context?.Log("日历界面线程异常: " + ex.Message); Stop(); }
        finally
        {
            _hoverTimer?.Stop();
            _calendarWindow?.Close();
            _calendarWindow = null;
            _flyoutVisible = false;
            _menuOpen = false;
            Interlocked.Exchange(ref _windowHandle, IntPtr.Zero);
            ThreadStopped();
        }
    }

    private void StartHookThread()
    {
        try
        {
            _hookDispatcher = Dispatcher.CurrentDispatcher;
            if (_shouldStop) return;
            _mouseHookDelegate = MouseHookCallback;
            _winEventDelegate = (_, eventType, hwnd, objectId, childId, threadId, time) =>
            {
                if (_shouldStop || hwnd == IntPtr.Zero || (objectId != 0 && objectId != -4)) return;
                if (eventType != 0x8000 && eventType != 0x8001 && eventType != 0x8002 && eventType != 0x8003 && eventType != 0x800B) return;
                var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
                if (TaskbarClockCache.IsTaskbar(root) || _clockCache?.HasTaskbar(hwnd) == true)
                    _clockCache?.RequestRefresh();
            };
            _winEventHook = NativeMethods.SetWinEventHook(0x8000, 0x800B, IntPtr.Zero, _winEventDelegate, 0, 0, 2);
            if (_winEventHook == IntPtr.Zero) _context?.Log("任务栏事件监听失败；显示设置变化和命中失效仍会请求刷新缓存。");
            _hookRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hookRetryTimer.Tick += (_, _) => { _hookRetryTimer.Stop(); SyncMouseHook(); };
            SyncMouseHook();
            if (!_shouldStop) Dispatcher.Run();
        }
        catch (Exception ex) { _context?.Log("日历输入线程异常: " + ex.Message); Stop(); }
        finally
        {
            _hookRetryTimer?.Stop();
            UninstallMouseHook();
            if (_winEventHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
            _swallowLeftUp = false;
            ThreadStopped();
        }
    }

    private void ApplyHoverMode()
    {
        _hoverTimer?.Stop();
        _hoverStartTime = _hoverLeaveTime = DateTime.MinValue;
        _isHoverTriggered = false;
        if (!_shouldStop && _mode == TriggerMode.Hover) _hoverTimer?.Start();
    }

    public void PublishWindowState(CalendarWindow window)
    {
        Interlocked.Exchange(ref _windowHandle, window.WindowHandle);
        _menuOpen = window.IsMenuOpen;
        bool changed = _flyoutVisible != window.IsVisible;
        _flyoutVisible = window.IsVisible;
        if (changed) Post(_hookDispatcher, SyncMouseHook);
    }
    public void RefreshPresentationState() => _suppressClicks = IsFullScreenOrPresentation();
    public void EnsureMouseHookForFlyout() => Post(_hookDispatcher, SyncMouseHook);
    public void ReleaseMouseHookIfHoverMode() => Post(_hookDispatcher, SyncMouseHook);

    public void ToggleCalendar() => Post(_uiDispatcher, () =>
    {
        if (_shouldStop) return;
        _calendarWindow ??= new CalendarWindow();
        if (_calendarWindow.IsVisible) _calendarWindow.HideFlyout(); else _calendarWindow.ShowFlyout();
    });
    public void ShowCalendar(string? benchmarkReport = null) => Post(_uiDispatcher, () =>
    {
        if (_shouldStop) return;
        _calendarWindow ??= new CalendarWindow();
        _calendarWindow.ShowFlyout();
    });
    public void HideCalendar() => Post(_uiDispatcher, () => _calendarWindow?.HideFlyout());

    public string CaptureSnapshot(string savePath)
    {
        if (_uiDispatcher == null) return "error: no dispatcher";
        string result = "ok";
        _uiDispatcher.Invoke(() =>
        {
            try
            {
                _calendarWindow ??= new CalendarWindow();
                // Screenshot requests often originate in a browser. Do not ShowFlyout():
                // browser focus would immediately trigger Deactivated and hide the calendar.
                // Render the retained content directly without activating any window.
                var content = _calendarWindow.Content as FrameworkElement
                    ?? throw new InvalidOperationException("Calendar visual root is unavailable.");
                int w = (int)Math.Ceiling(Math.Max(1, _calendarWindow.Width));
                int h = (int)Math.Ceiling(Math.Max(1, _calendarWindow.Height));
                if (!_calendarWindow.IsVisible || content.ActualWidth < 1 || content.ActualHeight < 1)
                {
                    content.Measure(new Size(w, h));
                    content.Arrange(new Rect(0, 0, w, h));
                }
                content.UpdateLayout();
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(savePath))
                {
                    encoder.Save(fs);
                }
                result = savePath;
            }
            catch (Exception ex)
            {
                result = "error: " + ex.Message;
            }
        });
        return result;
    }

    public void TestSelectRange(DateTime start, DateTime end)
    {
        Post(_uiDispatcher, () =>
        {
            _calendarWindow ??= new CalendarWindow();
            if (!_calendarWindow.IsVisible) _calendarWindow.ShowFlyout();
            _calendarWindow.HandleDateCellDoubleClick(start);
            _calendarWindow.HandleDateCellDoubleClick(end);
        });
    }

    public void TestOpenCalc(DateTime baseDate, int days)
    {
        Post(_uiDispatcher, () =>
        {
            _calendarWindow ??= new CalendarWindow();
            if (!_calendarWindow.IsVisible) _calendarWindow.ShowFlyout();
            _calendarWindow.OpenCalculationPanel(baseDate, days);
        });
    }

    public void TestSelectDate(DateTime date)
    {
        Post(_uiDispatcher, () =>
        {
            _calendarWindow ??= new CalendarWindow();
            if (!_calendarWindow.IsVisible) _calendarWindow.ShowFlyout();
            _calendarWindow.HandleDateCellClick(date);
        });
    }

    public string Run(string input)
    {
        if (input == "sync-status") return CalendarReminderManager.Instance.SyncStatus;
        if (input == "sync-use-cloud" || input == "sync-keep-local")
            return CalendarReminderManager.Instance.ResolveConflictsAsync(input == "sync-keep-local").GetAwaiter().GetResult();
        if (input.StartsWith("add-reminder:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input["add-reminder:".Length..].Split('|', 2);
            if (parts.Length != 2 || !DateTime.TryParse(parts[0], out var date) || string.IsNullOrWhiteSpace(parts[1]))
                return "格式：add-reminder:YYYY-MM-DD|标题";
            CalendarReminderManager.Instance.AddReminder(date, parts[1]);
            return "待办已保存，等待账号同步";
        }
        if (input.StartsWith("rename-reminder:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input["rename-reminder:".Length..].Split('|', 2);
            var item = parts.Length == 2 ? CalendarReminderManager.Instance.GetItemById(parts[0]) : null;
            if (item == null || string.IsNullOrWhiteSpace(parts[1])) return "事项不存在或标题为空";
            CalendarReminderManager.Instance.UpdateItem(item.Id, parts[1], item.IsAlarm, item.AlarmTime);
            return "事项已更新，等待账号同步";
        }
        if (input.StartsWith("delete-reminder:", StringComparison.OrdinalIgnoreCase))
        {
            CalendarReminderManager.Instance.RemoveItemById(input["delete-reminder:".Length..].Trim());
            return "事项已删除，等待账号同步";
        }
        if (string.IsNullOrWhiteSpace(input))
        {
            ToggleCalendar();
            return "已切换日历展示。";
        }
        if (input.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            Stop();
            return "日历服务已停止。";
        }
        if (input.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
        {
            var dateStr = input.Substring("select:".Length).Trim();
            if (DateTime.TryParse(dateStr, out var d))
            {
                TestSelectDate(d);
                return $"已选中日期: {d:yyyy-MM-dd}";
            }
        }
        if (input.StartsWith("snapshot:", StringComparison.OrdinalIgnoreCase))
        {
            var path = input.Substring("snapshot:".Length).Trim();
            return CaptureSnapshot(path);
        }
        if (input.StartsWith("test-range:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Substring("test-range:".Length).Split(',');
            if (parts.Length == 2 && DateTime.TryParse(parts[0], out var s) && DateTime.TryParse(parts[1], out var e))
            {
                TestSelectRange(s, e);
                return $"已选择区间: {s:yyyy-MM-dd} 至 {e:yyyy-MM-dd}";
            }
        }
        ToggleCalendar();
        return "已执行。";
    }

    private void ScheduleAutoVerification()
    {
        Task.Run(async () =>
        {
            await Task.Delay(1000);
            if (_uiDispatcher == null || _shouldStop) return;
            await _uiDispatcher.InvokeAsync(() =>
            {
                try
                {
                    string artifactDir = @"C:\Users\Administrator\.gemini\antigravity\brain\529973f9-d2ca-47bd-9e8e-b53d7ac3534f";
                    if (!Directory.Exists(artifactDir) || _calendarWindow == null) return;

                    string f1 = System.IO.Path.Combine(artifactDir, "calendar_default_view.png");
                    string f2 = System.IO.Path.Combine(artifactDir, "calendar_range_selected.png");
                    string f3 = System.IO.Path.Combine(artifactDir, "calendar_calc_30days.png");
                    string f4 = System.IO.Path.Combine(artifactDir, "calendar_reminder_view.png");
                    string f5 = System.IO.Path.Combine(artifactDir, "calendar_editor_view.png");

                    // 准备测试数据 (为 9月28日 添加事项，验证右侧列表与倒计时)
                    var testDate = new DateTime(2026, 9, 28);
                    CalendarReminderManager.Instance.ClearItemsForDate(testDate);
                    CalendarReminderManager.Instance.AddReminder(testDate, "项目阶段总结与规划");
                    CalendarReminderManager.Instance.AddAlarm(new DateTime(2026, 9, 28, 14, 30, 0), "核心评审会议");

                    _calendarWindow.ShowFlyout();
                    _calendarWindow.UpdateLayout();
                    _calendarWindow.RefreshCountdownPanel();
                    _calendarWindow.RefreshRightPanel(new DateTime(2026, 9, 26));
                    SaveWindowSnapshot(_calendarWindow, f1);

                    // 测试模拟悬浮在年份上的状态 (验证左右箭头浮现时文字 0 位移)
                    _calendarWindow.SimulateYearHover(true);
                    _calendarWindow.UpdateLayout();
                    string fHover = System.IO.Path.Combine(artifactDir, "calendar_hover_year.png");
                    SaveWindowSnapshot(_calendarWindow, fHover);
                    _calendarWindow.SimulateYearHover(false);
                    _calendarWindow.UpdateLayout();

                    // 测试选中 9月28日 (展示右侧详细列表与编辑/删除按钮)
                    _calendarWindow.HandleDateCellClick(testDate);
                    _calendarWindow.UpdateLayout();
                    SaveWindowSnapshot(_calendarWindow, f4);

                    // 测试展开新建/编辑事项表单卡片
                    _calendarWindow.OpenEditorForNew();
                    _calendarWindow.UpdateLayout();
                    SaveWindowSnapshot(_calendarWindow, f5);
                    _calendarWindow.HideEditor();

                    // 测试双选日期跨度计算 (双击固定端点，跨月：9月22日 到 10月8日)
                    _calendarWindow.ClearRangeSelection();
                    _calendarWindow.HandleDateCellDoubleClick(new DateTime(2026, 9, 22));
                    _calendarWindow.HandleDateCellDoubleClick(new DateTime(2026, 10, 8));
                    _calendarWindow.UpdateLayout();
                    SaveWindowSnapshot(_calendarWindow, f2);

                    // 测试极简推算面板 (推算30天)
                    _calendarWindow.ClearRangeSelection();
                    _calendarWindow.OpenCalculationPanel(new DateTime(2026, 9, 26), 30);
                    _calendarWindow.UpdateLayout();
                    SaveWindowSnapshot(_calendarWindow, f3);
                    _calendarWindow.HideCalculationPanel();

                    // 清理并复位
                    CalendarReminderManager.Instance.ClearItemsForDate(testDate);
                    _calendarWindow.ClearRangeSelection();
                    _calendarWindow.RefreshRightPanel(DateTime.Today);
                    _calendarWindow.RefreshCountdownPanel();
                    _calendarWindow.RenderMonthGrid();
                }
                catch { }
            });
        });
    }

    public static void SaveWindowSnapshot(Window window, string filePath)
    {
        window.UpdateLayout();
        int w = (int)Math.Max(100, window.ActualWidth > 0 ? window.ActualWidth : window.Width);
        int h = (int)Math.Max(100, window.ActualHeight > 0 ? window.ActualHeight : window.Height);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }

    private DateTime _hoverLeaveTime = DateTime.MinValue;

    private void CheckHoverCorner()
    {
        if (!NativeMethods.GetCursorPos(out POINT pt))
            return;

        // 全屏免打扰防护检测
        if (IsFullScreenOrPresentation())
        {
            _isHoverTriggered = false;
            return;
        }

        IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            return;

        int screenRight = mi.rcMonitor.right;
        int screenBottom = mi.rcMonitor.bottom;

        // 右下角极值热区（右下角 8x8 像素热区）
        bool inCorner = (pt.x >= screenRight - 8 && pt.y >= screenBottom - 8);
        bool isCalVisible = _calendarWindow != null && _calendarWindow.IsFlyoutVisible;

        if (inCorner)
        {
            _hoverLeaveTime = DateTime.MinValue;
            if (!_isHoverTriggered)
            {
                if (_hoverStartTime == DateTime.MinValue)
                {
                    _hoverStartTime = DateTime.UtcNow;
                }
                else
                {
                    // 停留超过 180 毫秒视为有意停留，触发日历展示
                    if ((DateTime.UtcNow - _hoverStartTime).TotalMilliseconds >= 180)
                    {
                        _isHoverTriggered = true;
                        ShowCalendar();
                        _context?.Log("【形态1】右下角悬停检测成功触发日历展出");
                    }
                }
            }
        }
        else
        {
            _hoverStartTime = DateTime.MinValue;

            // 悬停移开自动收回机制：若日历已展出，但鼠标移开日历和角落超过 400ms，自动收起日历
            if (_isHoverTriggered && isCalVisible)
            {
                // 关键防线：若用户正在右键菜单中交互，冻结移出收起检测，绝不收起日历！
                if (_calendarWindow != null && _calendarWindow.IsMenuOpen)
                {
                    _hoverLeaveTime = DateTime.MinValue;
                    return;
                }

                bool insideCal = _calendarWindow != null && _calendarWindow.IsPointInside(pt);
                bool nearClock = (pt.x >= screenRight - 130 && pt.y >= screenBottom - 50);

                if (!insideCal && !nearClock)
                {
                    if (_hoverLeaveTime == DateTime.MinValue)
                    {
                        _hoverLeaveTime = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - _hoverLeaveTime).TotalMilliseconds >= 400)
                    {
                        HideCalendar();
                        _isHoverTriggered = false;
                        _hoverLeaveTime = DateTime.MinValue;
                        _context?.Log("【形态1】鼠标移离日历区域 400ms，日历静默自动收起");
                    }
                }
                else
                {
                    _hoverLeaveTime = DateTime.MinValue;
                }
            }
            else if (_isHoverTriggered && !isCalVisible)
            {
                _isHoverTriggered = false;
                _hoverLeaveTime = DateTime.MinValue;
            }
        }
    }



    private void SyncMouseHook()
    {
        if (_shouldStop || (_mode != TriggerMode.ClickIntercept && !_flyoutVisible && !_swallowLeftUp))
        {
            _hookRetryTimer?.Stop();
            UninstallMouseHook();
            return;
        }
        if (_mouseHookHandle != IntPtr.Zero) return;
        _mouseHookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseHookDelegate!, NativeMethods.GetModuleHandle(null!), 0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            _context?.Log($"鼠标监听安装失败: {Marshal.GetLastWin32Error()}");
            if (++_hookRetries <= 3) _hookRetryTimer?.Start();
        }
        else _hookRetries = 0;
    }
    private void UninstallMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        int msg = (int)wParam;
        // 移动/滚轮立即放行；仅对已吞下的左键按下配对吞下抬起。
        if (msg == NativeMethods.WM_LBUTTONUP && _swallowLeftUp)
        {
            _swallowLeftUp = false;
            Post(_hookDispatcher, SyncMouseHook);
            return (IntPtr)1;
        }
        if (_shouldStop || (msg != NativeMethods.WM_LBUTTONDOWN && msg != NativeMethods.WM_RBUTTONDOWN && msg != NativeMethods.WM_MBUTTONDOWN))
            return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        try
        {
            if (_menuOpen) return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
            var pt = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
            bool clockClick = msg == NativeMethods.WM_LBUTTONDOWN && _mode == TriggerMode.ClickIntercept && !_suppressClicks && HitTestTaskbarClock(pt);
            if (clockClick)
            {
                bool show = !_flyoutVisible;
                _swallowLeftUp = true;
                Post(_uiDispatcher, () =>
                {
                    if (_shouldStop) return;
                    _calendarWindow ??= new CalendarWindow();
                    // 按下时决定开/关，避免后续窗口失焦先关闭导致 toggle 再次打开。
                    if (show) _calendarWindow.ShowFlyout(); else _calendarWindow.HideFlyout();
                });
                return (IntPtr)1;
            }
            var hwnd = Interlocked.CompareExchange(ref _windowHandle, IntPtr.Zero, IntPtr.Zero);
            if (_flyoutVisible && (!NativeMethods.GetWindowRect(hwnd, out RECT rect) || !TaskbarClockCache.Contains(rect, pt))) HideCalendar();
        }
        catch { /* 输入回调异常一律放行，不阻塞系统输入。 */ }
        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    public static bool HitTestTaskbarClock(POINT pt) => Instance._clockCache?.HitTest(pt) == true;
    private static bool IsFullScreenOrPresentation()
    {
        return NativeMethods.SHQueryUserNotificationState(out USER_NOTIFICATION_STATE state) == 0 &&
            (state == USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN || state == USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE);
    }
}

// UI Automation 只在独立 MTA 线程读取；鼠标钩子只使用不可变区域快照。
public sealed class TaskbarClockCache
{
    public sealed record Region(IntPtr Taskbar, RECT TaskbarBounds, RECT ClockBounds, uint Dpi, string Source);
    private Region[] _regions = Array.Empty<Region>();
    private readonly AutoResetEvent _refresh = new(false);
    private readonly object _lifecycle = new();
    private readonly Action<string> _log;
    private volatile bool _stopped;
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => _completion.Task;
    private int _pending;
    private long _lastRequest;
    public Region[] Snapshot => Volatile.Read(ref _regions);
    public TaskbarClockCache(Action<string> log) => _log = log;
    public void Start()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        var worker = new Thread(Worker) { IsBackground = true, Name = "TaskbarCalendarClockCache" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        RequestRefresh();
    }
    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_stopped) return;
            _stopped = true;
            Volatile.Write(ref _regions, Array.Empty<Region>());
            _refresh.Set();
        }
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    }
    private void OnDisplayChanged(object? s, EventArgs e) => RequestRefresh();
    private void OnPreferenceChanged(object s, Microsoft.Win32.UserPreferenceChangedEventArgs e) => RequestRefresh();
    public void RequestRefresh()
    {
        lock (_lifecycle)
            if (!_stopped && Interlocked.Exchange(ref _pending, 1) == 0) _refresh.Set();
    }
    public bool HasTaskbar(IntPtr hwnd) => Snapshot.Any(r => r.Taskbar == hwnd);
    public static bool Contains(RECT r, POINT p) => p.x >= r.left && p.x < r.right && p.y >= r.top && p.y < r.bottom;
    public static bool SameRect(RECT a, RECT b) => a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom;
    public static bool IsTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var name = new StringBuilder(64);
        NativeMethods.GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
    public bool HitTest(POINT pt)
    {
        foreach (var region in Snapshot)
        {
            if (!Contains(region.ClockBounds, pt)) continue;
            if (!NativeMethods.IsWindowVisible(region.Taskbar) || !NativeMethods.GetWindowRect(region.Taskbar, out RECT current) ||
                !SameRect(current, region.TaskbarBounds) || NativeMethods.GetDpiForWindow(region.Taskbar) != region.Dpi)
            { RequestRefresh(); return false; }
            return NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT) == region.Taskbar;
        }
        // 缓存未就绪或时钟布局已变化时，异步补查并放行本次点击；不猜测热区。
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastRequest) >= 2000 && IsTaskbar(NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT)))
        {
            Interlocked.Exchange(ref _lastRequest, now);
            RequestRefresh();
        }
        return false;
    }
    private void Worker()
    {
        try
        {
            while (true)
            {
                _refresh.WaitOne();
                if (_stopped) return;
                // 合并任务栏重排通知；空闲时无限等待，不做定时轮询。
                _refresh.WaitOne(200);
                if (_stopped) return;
                Interlocked.Exchange(ref _pending, 0);
                try
                {
                    var result = Discover();
                    var previous = Snapshot;
                    lock (_lifecycle)
                    {
                        if (_stopped) return;
                        Volatile.Write(ref _regions, result);
                    }
                    if (previous.Length != result.Length || !previous.Select(r => r.ClockBounds).SequenceEqual(result.Select(r => r.ClockBounds)))
                        _log("时钟区域缓存: " + (result.Length == 0 ? "未识别，保留系统点击" : string.Join("; ", result.Select(r => $"{r.Source} [{r.ClockBounds.left},{r.ClockBounds.top},{r.ClockBounds.right},{r.ClockBounds.bottom}]"))));
                }
                catch (Exception ex) { _log("时钟区域读取失败: " + ex.Message); }
            }
        }
        finally
        {
            lock (_lifecycle) _refresh.Dispose();
            _completion.TrySetResult(true);
        }
    }
    public static Region[] Discover()
    {
        var taskbars = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) => { if (IsTaskbar(hwnd)) taskbars.Add(hwnd); return true; }, IntPtr.Zero);
        var result = new List<Region>();
        foreach (var tray in taskbars)
        {
            if (!NativeMethods.IsWindowVisible(tray) || !NativeMethods.GetWindowRect(tray, out RECT bounds)) continue;
            RECT clock = default;
            bool found = false;
            NativeMethods.EnumChildWindows(tray, (child, _) =>
            {
                var name = new StringBuilder(64);
                NativeMethods.GetClassName(child, name, name.Capacity);
                if (name.ToString() != "TrayClockWClass" || !NativeMethods.IsWindowVisible(child)) return true;
                found = NativeMethods.GetWindowRect(child, out clock);
                return !found;
            }, IntPtr.Zero);
            string source = "原生时钟控件";
            if (!found)
            {
                try
                {
                    var root = AutomationElement.FromHandle(tray);
                    var condition = new OrCondition(
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTray.DateTimeIcon"),
                        new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTray.Clock"));
                    var element = root.FindFirst(TreeScope.Descendants, condition);
                    if (element != null && !element.Current.IsOffscreen)
                    {
                        var rect = element.Current.BoundingRectangle;
                        if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                        {
                            clock = new RECT { left = (int)Math.Floor(rect.Left), top = (int)Math.Floor(rect.Top), right = (int)Math.Ceiling(rect.Right), bottom = (int)Math.Ceiling(rect.Bottom) };
                            found = true;
                            source = "UI Automation 时钟控件";
                        }
                    }
                }
                catch (ElementNotAvailableException) { }
                catch (System.Runtime.InteropServices.COMException) { }
            }
            if (found && clock.right > clock.left && clock.bottom > clock.top &&
                clock.left >= bounds.left && clock.top >= bounds.top && clock.right <= bounds.right && clock.bottom <= bounds.bottom)
                result.Add(new Region(tray, bounds, clock, NativeMethods.GetDpiForWindow(tray), source));
        }
        return result.ToArray();
    }
}


/// <summary>
/// 性能基准测试与耗时比对评测套件
/// </summary>
public static class CalendarBenchmarkRunner
{
    public static string RunBenchmark()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 日历【形态1 vs 形态2】性能基准测试报告 ===");
        sb.AppendLine($"测试时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"系统环境: .NET 9.0 (x64), Windows 11/10 Desktop");
        sb.AppendLine("--------------------------------------------------");

        var sw = new Stopwatch();

        sb.AppendLine("【触发机制与测试范围】");
        sb.AppendLine("  - 点击模式：独立消息线程；无 50ms 轮询；时钟区域按事件后台刷新。");
        sb.AppendLine("  - 悬停模式：仅此模式启用 50ms 检测；日历打开时临时监听外部点击。");
        sb.AppendLine("  - 以下仅测缓存命中函数，不代表端到端点击延迟、常驻 CPU 或全局鼠标钩子开销。");
        var regions = TaskbarCalendarService.Instance.ClockRegions;
        sb.AppendLine($"  - 已识别时钟区域: {regions.Length} 个");
        if (regions.Length == 0) sb.AppendLine("  - 缓存未就绪或未识别，系统日期点击保持原行为。");
        foreach (var region in regions)
        {
            var pt = new POINT { x = (region.ClockBounds.left + region.ClockBounds.right) / 2,
                y = (region.ClockBounds.top + region.ClockBounds.bottom) / 2 };
            TaskbarCalendarService.HitTestTaskbarClock(pt); // 预热
            int hits = 0;
            const int count = 1000;
            sw.Restart();
            for (int i = 0; i < count; i++) if (TaskbarCalendarService.HitTestTaskbarClock(pt)) hits++;
            sw.Stop();
            sb.AppendLine($"  - {region.Source}: {count} 次平均 {sw.Elapsed.TotalMilliseconds * 1000 / count:F3} μs，成功命中 {hits} 次");
            if (hits != count) sb.AppendLine("    区域被遮挡或布局已改变；本次不能视为完整命中路径的耗时。");
        }
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        long workingSetMb = proc.WorkingSet64 / (1024 * 1024);
        long privateMemMb = proc.PrivateMemorySize64 / (1024 * 1024);
        // 4. 节假日与二十四节气离线推算性能评测 (测试整整一整年 365 天日历详情推算耗时)
        sw.Restart();
        int calDays = 365;
        var startDay = new DateTime(2026, 1, 1);
        for (int i = 0; i < calDays; i++)
        {
            _ = ChineseCalendarHelper.GetDetail(startDay.AddDays(i));
        }
        sw.Stop();
        double calTotalMs = sw.Elapsed.TotalMilliseconds;
        double calAvgUs = (calTotalMs / calDays) * 1000.0;

        sb.AppendLine();
        sb.AppendLine($"【农历·节假日·二十四节气 离线推算性能】");
        sb.AppendLine($"  - 单日综合计算耗时: {calAvgUs:F3} 微秒 (μs)");
        sb.AppendLine($"  - 全年 365 天推算总耗时: {calTotalMs:F2} 毫秒 (ms) [纯内存离线计算，0 外部网络依赖]");
        var midAutumnDetail = ChineseCalendarHelper.GetDetail(new DateTime(2026, 9, 25));
        var todayDetail = ChineseCalendarHelper.GetDetail(DateTime.Today);
        sb.AppendLine($"  - 2026年中秋正日: {midAutumnDetail.CellDisplayText} (角标: {midAutumnDetail.BadgeText})");
        sb.AppendLine($"  - 今日节日状态: {ChineseCalendarHelper.GetTodayBanner(todayDetail)}");

        sb.AppendLine();
        sb.AppendLine($"【进程基础开销 (宿主)】");
        sb.AppendLine($"  - 物理工作集内存 (WorkingSet): {workingSetMb} MB");
        sb.AppendLine($"  - 私有提交内存 (PrivateMemory): {privateMemMb} MB");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine("宿主内存包含其他小程序和界面，不能作为本日历的独立内存占用。");
        sb.AppendLine("实际性能需另测空闲 CPU、高频鼠标移动与点击到显示延迟，不按不同路径的微基准判定模式优劣。");
        return sb.ToString();
    }
}

/// <summary>
/// 日历备忘与定时闹钟实体
/// </summary>
public class CalendarReminderItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime TargetDate { get; set; }
    public DateTime? AlarmTime { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsAlarm { get; set; }
    public bool IsTriggered { get; set; }
    public DateTime CreatedTime { get; set; } = DateTime.Now;
}

/// <summary>
/// 待办事项与闹钟本地持久化管理器（单例）
/// </summary>
public class CalendarReminderManager
{
    public static readonly CalendarReminderManager Instance = new();
    private readonly object _lock = new();
    private readonly List<CalendarReminderItem> _items = new();
    private string? _filePath;
    private CalendarAccountSync? _accountSync;
    private bool _applyingCloud;
    public long DataVersion { get; private set; }

    public void Initialize(YanziActionContext? context)
    {
        lock (_lock)
        {
            try
            {
                string baseDir = context?.ExtensionDataDirectory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "ExtensionStorage", "taskbar-calendar");
                if (!System.IO.Directory.Exists(baseDir))
                {
                    System.IO.Directory.CreateDirectory(baseDir);
                }
                _filePath = System.IO.Path.Combine(baseDir, "calendar_reminders.json");

                if (System.IO.File.Exists(_filePath))
                {
                    string json = System.IO.File.ReadAllText(_filePath, Encoding.UTF8);
                    var list = JsonSerializer.Deserialize<List<CalendarReminderItem>>(json);
                    if (list != null)
                    {
                        _items.Clear();
                        _items.AddRange(list);
                    }
                }
            }
            catch { }
            if (context != null && _filePath != null)
            {
                _accountSync?.Dispose();
                try
                {
                    _accountSync = new CalendarAccountSync(context, _filePath, this);
                    _accountSync.Track(_items);
                }
                catch (Exception ex)
                {
                    _accountSync?.Dispose(); _accountSync = null;
                    context.Log("日历同步初始化失败，已保留本地事项：" + ex.GetType().Name);
                }
            }
        }
    }

    public void StopSync() { _accountSync?.Dispose(); }
    public string SyncStatus => _accountSync?.StatusText ?? "日历尚未启动";
    public Task<string> ResolveConflictsAsync(bool keepLocal) => _accountSync?.ResolveConflictsAsync(keepLocal)
        ?? Task.FromResult("日历尚未启动");
    public void ApplyCloud(Func<List<CalendarReminderItem>> buildItems)
    {
        lock (_lock)
        {
            _applyingCloud = true;
            try
            {
                var items = buildItems();
                if (JsonSerializer.Serialize(_items) == JsonSerializer.Serialize(items)) return;
                _items.Clear(); _items.AddRange(items); SaveInternal();
            }
            finally { _applyingCloud = false; }
        }
    }

    private void SaveInternal()
    {
        if (string.IsNullOrEmpty(_filePath)) return;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_items, options);
            System.IO.File.WriteAllText(_filePath, json, Encoding.UTF8);
            DataVersion++;
            if (!_applyingCloud) _accountSync?.Track(_items);
        }
        catch { }
    }

    public void AddReminder(DateTime date, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        lock (_lock)
        {
            _items.Add(new CalendarReminderItem
            {
                TargetDate = date.Date,
                Title = title.Trim(),
                IsAlarm = false
            });
            SaveInternal();
        }
    }

    public void AddAlarm(DateTime triggerTime, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        lock (_lock)
        {
            _items.Add(new CalendarReminderItem
            {
                TargetDate = triggerTime.Date,
                AlarmTime = triggerTime,
                Title = title.Trim(),
                IsAlarm = true,
                IsTriggered = false
            });
            SaveInternal();
        }
    }

    public List<CalendarReminderItem> GetItemsForDate(DateTime date)
    {
        lock (_lock)
        {
            return _items.Where(x => x.TargetDate.Date == date.Date).ToList();
        }
    }

    public void ClearItemsForDate(DateTime date)
    {
        lock (_lock)
        {
            _items.RemoveAll(x => x.TargetDate.Date == date.Date);
            SaveInternal();
        }
    }

    public List<CalendarReminderItem> GetAllItems()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public CalendarReminderItem? GetItemById(string id)
    {
        lock (_lock)
        {
            return _items.FirstOrDefault(x => x.Id == id);
        }
    }

    public void UpdateItem(string id, string newTitle, bool isAlarm, DateTime? newAlarmTime)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        lock (_lock)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.Title = newTitle.Trim();
                item.IsAlarm = isAlarm;
                item.AlarmTime = newAlarmTime;
                if (newAlarmTime.HasValue)
                {
                    item.TargetDate = newAlarmTime.Value.Date;
                }
                SaveInternal();
            }
        }
    }

    public void RemoveItemById(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_lock)
        {
            _items.RemoveAll(x => x.Id == id);
            SaveInternal();
        }
    }

    public List<CalendarReminderItem> SearchItems(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return new List<CalendarReminderItem>();
        lock (_lock)
        {
            return _items
                .Where(x => x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.TargetDate)
                .ToList();
        }
    }

    public List<CalendarReminderItem> CheckPendingAlarms(YanziActionContext? context, Action<CalendarReminderItem>? onAlarmTriggered)
    {
        var triggered = new List<CalendarReminderItem>();
        lock (_lock)
        {
            var now = DateTime.Now;
            foreach (var item in _items)
            {
                if (item.IsAlarm && !item.IsTriggered && item.AlarmTime.HasValue && now >= item.AlarmTime.Value)
                {
                    item.IsTriggered = true;
                    triggered.Add(item);
                }
            }

            if (triggered.Count > 0)
            {
                SaveInternal();
            }
        }

        foreach (var item in triggered)
        {
            PlayAlarmSound();

            try
            {
                context?.ShowDesktopNotification("燕子日历闹钟响铃", $"{item.AlarmTime:HH:mm} 到点了！\n{item.Title}");
            }
            catch { }


            try
            {
                onAlarmTriggered?.Invoke(item);
            }
            catch { }
        }

        return triggered;
    }

    public static void PlayAlarmSound()
    {
        Task.Run(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    SystemSounds.Exclamation.Play();
                }
                catch { }
                Thread.Sleep(650);
            }
        });
    }
}

/// <summary>
/// 临近重大节点实体（用于倒计时计算）
/// </summary>
public sealed class CalendarAccountRecord
{
    public string version { get; set; } = "";
    public bool deleted { get; set; }
    public CalendarReminderItem? item { get; set; }
}
public sealed class CalendarAccountChange
{
    public string expected { get; set; } = "";
    public CalendarAccountRecord record { get; set; } = new();
}
public sealed class CalendarAccountDocument
{
    public int schemaVersion { get; set; } = 1;
    public Dictionary<string, CalendarAccountRecord> records { get; set; } = new();
}
public sealed class CalendarAccountJournal
{
    public string accountId { get; set; } = "";
    public Dictionary<string, CalendarAccountRecord> records { get; set; } = new();
    public Dictionary<string, CalendarAccountChange> pending { get; set; } = new();
    public Dictionary<string, string> localItems { get; set; } = new();
}
public sealed class CalendarAccountSync : IDisposable
{
    private readonly object _gate = new();
    private readonly YanziActionContext _context;
    private readonly CalendarReminderManager _manager;
    private readonly string _journalPath;
    private readonly System.Threading.Timer _timer;
    private readonly System.Net.Http.HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private CalendarAccountJournal _state = new();
    private int _busy;
    private string _lastStatus = "";
    private readonly HashSet<string> _conflictIds = new();
    public string StatusText => _lastStatus;
    public CalendarAccountSync(YanziActionContext context, string legacyPath, CalendarReminderManager manager)
    {
        _context = context; _manager = manager; _journalPath = legacyPath + ".sync.json";
        if (File.Exists(_journalPath)) _state = JsonSerializer.Deserialize<CalendarAccountJournal>(File.ReadAllText(_journalPath))
            ?? throw new InvalidOperationException("日历同步日志无法读取，已保留原文件");
        // Keep a one-time original backup before the first cloud import.
        if (File.Exists(legacyPath) && !File.Exists(legacyPath + ".before-sync.bak")) File.Copy(legacyPath, legacyPath + ".before-sync.bak");
        _http = new System.Net.Http.HttpClient { BaseAddress = new Uri(context.AgentApiBaseUrl), Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.Add("X-Yanzi-Token", context.AgentApiToken);
        _timer = new System.Threading.Timer(_ => { _ = SynchronizeAsync(); }, null, 1500, 10000);
    }
    private void Persist()
    {
        var temp = _journalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_state), new UTF8Encoding(false));
        File.Move(temp, _journalPath, true);
    }
    public void Track(IEnumerable<CalendarReminderItem> items)
    {
        lock (_gate)
        {
            var local = items.ToDictionary(item => item.Id, item => JsonSerializer.Serialize(item));
            foreach (var id in local.Keys.Concat(_state.localItems.Keys).Distinct().ToList())
            {
                var exists = local.TryGetValue(id, out var json);
                if (exists && _state.localItems.TryGetValue(id, out var prior) && json == prior) continue;
                _state.records.TryGetValue(id, out var previous);
                _state.pending.TryGetValue(id, out var pending);
                var record = new CalendarAccountRecord { version = Guid.NewGuid().ToString("N"), deleted = !exists,
                    item = exists ? JsonSerializer.Deserialize<CalendarReminderItem>(json!) : previous?.item };
                _state.pending[id] = new CalendarAccountChange { expected = pending?.expected ?? previous?.version ?? "", record = record };
                _state.records[id] = record;
            }
            _state.localItems = local; Persist();
        }
    }
    private void Status(string value)
    {
        if (_lastStatus == value) return; _lastStatus = value;
        _context.Log("日历同步：" + value);
    }
    private async Task SynchronizeAsync()
    {
        if (_stop.IsCancellationRequested || Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            using var response = await _http.GetAsync("/v1/account-storage/taskbar-calendar?key=calendar.v1.json", _stop.Token);
            response.EnsureSuccessStatusCode();
            using var read = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_stop.Token));
            var root = read.RootElement;
            var account = root.GetProperty("accountId").GetString() ?? "";
            if (account.Length == 0) throw new InvalidOperationException("未登录账号");
            Dictionary<string, CalendarAccountChange> pending;
            lock (_gate)
            {
                if (_state.accountId.Length > 0 && account != _state.accountId)
                { Status("账号已切换，已暂停同步，原账号的本地数据已保留"); return; }
                _state.accountId = account; Persist();
                pending = JsonSerializer.Deserialize<Dictionary<string, CalendarAccountChange>>(JsonSerializer.Serialize(_state.pending))!;
            }
            var document = new CalendarAccountDocument();
            if (root.GetProperty("exists").GetBoolean())
            {
                using var raw = JsonDocument.Parse(root.GetProperty("content").GetString()!);
                if (!raw.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1
                    || !raw.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("不支持的日历格式");
                document = JsonSerializer.Deserialize<CalendarAccountDocument>(raw.RootElement.GetRawText())!;
            }
            if (document.schemaVersion != 1 || document.records == null) throw new InvalidOperationException("不支持的日历格式");
            if (document.records.Any(pair => string.IsNullOrEmpty(pair.Value.version)
                || (!pair.Value.deleted && (pair.Value.item == null || pair.Value.item.Id != pair.Key))))
                throw new InvalidOperationException("日历记录格式错误，已保留本地数据");
            var accepted = new Dictionary<string, CalendarAccountChange>();
            int conflicts = 0;
            var conflictIds = new HashSet<string>();
            foreach (var change in pending)
            {
                document.records.TryGetValue(change.Key, out var remote);
                if ((remote?.version ?? "") == change.Value.record.version) { accepted[change.Key] = change.Value; continue; }
                if ((remote?.version ?? "") != change.Value.expected) { conflicts++; conflictIds.Add(change.Key); continue; }
                document.records[change.Key] = change.Value.record; accepted[change.Key] = change.Value;
            }
            if (accepted.Count > 0)
            {
                var body = JsonSerializer.Serialize(new { key = "calendar.v1.json", content = JsonSerializer.Serialize(document),
                    expectedRevision = root.GetProperty("revision").GetInt64(), accountId = account });
                using var write = await _http.PutAsync("/v1/account-storage/taskbar-calendar",
                    new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json"), _stop.Token);
                if ((int)write.StatusCode == 409) { Status("云端版本变化，保留修改并稍后重试"); return; }
                write.EnsureSuccessStatusCode();
                using var saved = JsonDocument.Parse(await write.Content.ReadAsStringAsync(_stop.Token));
                if (saved.RootElement.GetProperty("accountId").GetString() != account) { Status("账号已切换，保留修改"); return; }
            }
            _manager.ApplyCloud(() =>
            {
                lock (_gate)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    foreach (var done in accepted)
                    {
                        if (!_state.pending.TryGetValue(done.Key, out var current)) continue;
                        if (current.record.version == done.Value.record.version) _state.pending.Remove(done.Key);
                        else if (current.expected == done.Value.expected) current.expected = done.Value.record.version;
                    }
                    _state.records = document.records;
                    _conflictIds.Clear(); _conflictIds.UnionWith(conflictIds);
                    foreach (var change in _state.pending) _state.records[change.Key] = change.Value.record;
                    var items = _state.records.Values.Where(r => !r.deleted && r.item != null).Select(r => r.item!).ToList();
                    _state.localItems = items.ToDictionary(i => i.Id, i => JsonSerializer.Serialize(i)); Persist();
                    return items;
                }
            });
            Status(conflicts > 0 ? $"{conflicts} 项同时被修改，本地修改已保留。输入 sync-use-cloud 或 sync-keep-local 处理电脑端冲突" : "已连接账号，待办和闹钟双向同步正常");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status("暂不可用，离线修改已保留（" + ex.GetType().Name + "）"); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
    public void Dispose() { lock (_gate) { _stop.Cancel(); _timer.Dispose(); _http.Dispose(); } }
    public async Task<string> ResolveConflictsAsync(bool keepLocal)
    {
        using var response = await _http.GetAsync("/v1/account-storage/taskbar-calendar?key=calendar.v1.json", _stop.Token);
        response.EnsureSuccessStatusCode();
        using var read = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_stop.Token));
        var root = read.RootElement;
        var doc = root.GetProperty("exists").GetBoolean()
            ? JsonSerializer.Deserialize<CalendarAccountDocument>(root.GetProperty("content").GetString()!)! : new CalendarAccountDocument();
        lock (_gate)
        {
            if (root.GetProperty("accountId").GetString() != _state.accountId) return "账号已切换，未修改同步日志";
            File.WriteAllText(_journalPath + ".conflicts.bak", JsonSerializer.Serialize(_state), Encoding.UTF8);
            foreach (var id in _conflictIds)
            {
                if (!_state.pending.TryGetValue(id, out var change)) continue;
                if (!keepLocal) _state.pending.Remove(id);
                else { doc.records.TryGetValue(id, out var current); change.expected = current?.version ?? ""; }
            }
            // Preserve the local versions before an explicit resolution, including deletions.
            Persist();
        }
        await SynchronizeAsync(); return StatusText;
    }
}

public class UpcomingEventItem
{
    public DateTime Date { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "节日", "节气", "待办", "闹钟"
    public int DaysRemaining { get; set; }
}

/// <summary>
/// 临近节点与倒计时助手
/// </summary>
public static class UpcomingEventsHelper
{
    public static List<UpcomingEventItem> GetUpcomingEvents(DateTime baseDate, int count = 3)
    {
        var result = new List<UpcomingEventItem>();
        var seen = new HashSet<string>();

        // 1. 获取所有未来的待办与闹钟 (排除今天，只算未来)
        var allReminders = CalendarReminderManager.Instance.GetAllItems();
        foreach (var r in allReminders)
        {
            if (r.TargetDate.Date > baseDate.Date)
            {
                int days = (r.TargetDate.Date - baseDate.Date).Days;
                string cat = r.IsAlarm ? "闹钟" : "待办";
                string key = $"{r.TargetDate:yyyyMMdd}_{r.Title}_{cat}";
                if (seen.Add(key))
                {
                    result.Add(new UpcomingEventItem
                    {
                        Date = r.TargetDate.Date,
                        Title = r.Title,
                        Category = cat,
                        DaysRemaining = days
                    });
                }
            }
        }

        // 2. 扫描未来 180 天内的法定节日、传统节日、二十四节气 (排除今天)
        for (int i = 1; i <= 180; i++)
        {
            var d = baseDate.Date.AddDays(i);
            var detail = ChineseCalendarHelper.GetDetail(d);

            if (!string.IsNullOrEmpty(detail.TraditionalFestival))
            {
                string key = $"{d:yyyyMMdd}_{detail.TraditionalFestival}_节日";
                if (seen.Add(key))
                {
                    result.Add(new UpcomingEventItem
                    {
                        Date = d,
                        Title = detail.TraditionalFestival,
                        Category = "传统节日",
                        DaysRemaining = i
                    });
                }
            }
            else if (!string.IsNullOrEmpty(detail.SolarFestival))
            {
                string key = $"{d:yyyyMMdd}_{detail.SolarFestival}_节日";
                if (seen.Add(key))
                {
                    result.Add(new UpcomingEventItem
                    {
                        Date = d,
                        Title = detail.SolarFestival,
                        Category = "法定节日",
                        DaysRemaining = i
                    });
                }
            }
            else if (!string.IsNullOrEmpty(detail.SolarTerm))
            {
                string key = $"{d:yyyyMMdd}_{detail.SolarTerm}_节气";
                if (seen.Add(key))
                {
                    result.Add(new UpcomingEventItem
                    {
                        Date = d,
                        Title = detail.SolarTerm,
                        Category = "二十四节气",
                        DaysRemaining = i
                    });
                }
            }
        }

        // 3. 排序：天数正序，如果天数相同，用户待办/闹钟排在节日前
        return result
            .OrderBy(x => x.DaysRemaining)
            .ThenBy(x => (x.Category == "待办" || x.Category == "闹钟") ? 0 : 1)
            .Take(count)
            .ToList();
    }
}

/// <summary>
/// 纯本地高精度日期速算与推算引擎
/// </summary>
public class DateCalculationDetail
{
    public DateTime BaseDate { get; set; }
    public DateTime TargetDate { get; set; }
    public int DaysOffset { get; set; }
    public DayCalendarDetail Detail { get; set; } = null!;
    public string TargetDateString { get; set; } = string.Empty;
    public string LunarString { get; set; } = string.Empty;
    public string FestivalString { get; set; } = string.Empty;
    public string SpanString { get; set; } = string.Empty;
}

public static class DateCalculatorHelper
{
    public static DateCalculationDetail CalculateDetailed(DateTime baseDate, int daysOffset)
    {
        var target = baseDate.AddDays(daysOffset);
        var detail = ChineseCalendarHelper.GetDetail(target);

        string relText = daysOffset == 0 ? "当天" : (daysOffset > 0 ? $"{daysOffset} 天后" : $"{Math.Abs(daysOffset)} 天前");
        string targetStr = $"{target:yyyy年M月d日 dddd} ({relText})";
        string lunarStr = $"{detail.GanZhiYear} {detail.LunarMonthName}{detail.LunarDayName}";

        string festStr = string.Empty;
        if (!string.IsNullOrEmpty(detail.TraditionalFestival))
            festStr = detail.TraditionalFestival;
        else if (!string.IsNullOrEmpty(detail.SolarFestival))
            festStr = detail.SolarFestival;
        else if (!string.IsNullOrEmpty(detail.SolarTerm))
            festStr = $"节气: {detail.SolarTerm}";

        if (detail.IsHoliday)
            festStr = string.IsNullOrEmpty(festStr) ? "法定节假日 (休)" : $"{festStr} (休)";
        else if (detail.IsWorkday)
            festStr = string.IsNullOrEmpty(festStr) ? "工作日调休 (班)" : $"{festStr} (班)";

        int absDays = Math.Abs(daysOffset);
        int weeks = absDays / 7;
        int remDays = absDays % 7;
        string weekStr = weeks > 0 ? (remDays > 0 ? $"{weeks}周{remDays}天" : $"{weeks}周整") : $"{remDays}天";
        string spanStr = daysOffset == 0 ? "与基准日期为同一天" : (weeks > 0 ? $"相差 {absDays} 天 ({weekStr})" : $"相差 {absDays} 天");

        return new DateCalculationDetail
        {
            BaseDate = baseDate,
            TargetDate = target,
            DaysOffset = daysOffset,
            Detail = detail,
            TargetDateString = targetStr,
            LunarString = lunarStr,
            FestivalString = festStr,
            SpanString = spanStr
        };
    }

    public static (DateTime TargetDate, string Description) CalculateOffset(DateTime baseDate, int daysOffset)
    {
        var result = CalculateDetailed(baseDate, daysOffset);
        string desc = $"{result.TargetDateString} · {result.LunarString}";
        if (!string.IsNullOrEmpty(result.FestivalString))
        {
            desc += $" · {result.FestivalString}";
        }
        return (result.TargetDate, desc);
    }
}

/// <summary>
/// 农历日期与节假日/节气详细信息实体
/// </summary>
public class DayCalendarDetail
{
    public DateTime Date { get; set; }
    public int Year => Date.Year;
    public int Month => Date.Month;
    public int Day => Date.Day;

    // 农历基本数据
    public int LunarYear { get; set; }
    public int LunarMonth { get; set; }
    public int LunarDay { get; set; }
    public bool IsLeapMonth { get; set; }
    public string GanZhiYear { get; set; } = string.Empty;
    public string LunarMonthName { get; set; } = string.Empty;
    public string LunarDayName { get; set; } = string.Empty;

    // 节日与节气
    public string? TraditionalFestival { get; set; }
    public string? SolarFestival { get; set; }
    public string? SolarTerm { get; set; }

    // 法定休假与调休
    public bool IsHoliday { get; set; }
    public bool IsWorkday { get; set; }
    public string? HolidayName { get; set; }

    // 单元格展示字段
    public string CellDisplayText { get; set; } = string.Empty;
    public Brush CellDisplayBrush { get; set; } = Brushes.Gray;
    public FontWeight CellFontWeight { get; set; } = FontWeights.Normal;

    // 角标字段（休/班）
    public bool HasBadge => IsHoliday || IsWorkday;
    public string BadgeText => IsHoliday ? "休" : (IsWorkday ? "班" : string.Empty);
    public Brush BadgeBackground => IsHoliday
        ? new SolidColorBrush(Color.FromRgb(10, 138, 92))   // 调低色值深翡翠绿休假标签，白字更清晰
        : new SolidColorBrush(Color.FromRgb(239, 68, 68));   // #EF4444 红色调休上班标签

    // 待办事项与闹钟标识
    public bool HasReminder { get; set; }
    public bool HasAlarm { get; set; }
    public List<string> ReminderList { get; set; } = new();
    public List<string> AlarmList { get; set; } = new();

    // ToolTip 完整描述
    public string ToolTipFestivalSummary { get; set; } = string.Empty;
}

/// <summary>
/// 纯高精度本地中国农历、二十四节气、节假日与休假推算引擎
/// </summary>
public static class ChineseCalendarHelper
{
    private static readonly ChineseLunisolarCalendar _lunar = new();

    private static readonly string[] GanZhiTiangan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
    private static readonly string[] GanZhiDizhi = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
    private static readonly string[] ShengXiao = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };

    private static readonly string[] LunarMonths = { "", "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
    private static readonly string[] LunarDays = {
        "", "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    };

    private static readonly string[] SolarTermNames = {
        "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
        "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
        "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
        "寒露", "霜降", "立冬", "小雪", "大雪", "冬至"
    };

    // 21世纪(2000-2099)二十四节气寿星天文 C 常数
    private static readonly double[] SolarTermC = {
        5.4055, 20.12,  3.87,   18.73,  5.63,   20.646,
        4.81,   20.1,   5.52,   21.04,  5.678,  21.37,
        7.108,  22.83,  7.5,    23.13,  7.646,  23.042,
        8.318,  23.438, 7.438,  22.36,  7.18,   21.94
    };

    public static DayCalendarDetail GetDetail(DateTime dt)
    {
        var d = new DayCalendarDetail { Date = dt.Date };

        try
        {
            int lYear = _lunar.GetYear(dt);
            int lMonth = _lunar.GetMonth(dt);
            int lDay = _lunar.GetDayOfMonth(dt);
            int leapMonth = _lunar.GetLeapMonth(lYear);

            bool isLeap = (leapMonth > 0 && lMonth == leapMonth);
            int displayMonth = lMonth;
            if (leapMonth > 0 && lMonth >= leapMonth)
            {
                displayMonth = lMonth - 1;
            }

            d.LunarYear = lYear;
            d.LunarMonth = displayMonth;
            d.LunarDay = lDay;
            d.IsLeapMonth = isLeap;

            int tgIndex = (lYear - 4) % 10;
            int dzIndex = (lYear - 4) % 12;
            d.GanZhiYear = $"{GanZhiTiangan[tgIndex]}{GanZhiDizhi[dzIndex]}({ShengXiao[dzIndex]})年";
            d.LunarMonthName = (isLeap ? "闰" : "") + (displayMonth >= 1 && displayMonth <= 12 ? LunarMonths[displayMonth] : $"{displayMonth}月");
            d.LunarDayName = (lDay >= 1 && lDay <= 30 ? LunarDays[lDay] : $"{lDay}日");

            // 1. 传统农历节日
            d.TraditionalFestival = GetTraditionalFestival(dt, displayMonth, lDay, isLeap);

            // 2. 公历现代节日
            d.SolarFestival = GetSolarFestival(dt);

            // 3. 二十四节气天文公式计算
            d.SolarTerm = GetSolarTerm(dt);

            // 4. 法定节假日假期联动感知
            CheckHolidayAndWorkday(dt, d);

            // 5. 格式化单元格展示文本与视觉配色
            DetermineCellDisplay(d);

            // 6. 整合本地待办事项与闹钟
            var items = CalendarReminderManager.Instance.GetItemsForDate(dt);
            if (items.Count > 0)
            {
                foreach (var it in items)
                {
                    if (it.IsAlarm)
                    {
                        d.HasAlarm = true;
                        d.AlarmList.Add($"{it.AlarmTime:HH:mm} {it.Title}");
                    }
                    else
                    {
                        d.HasReminder = true;
                        d.ReminderList.Add(it.Title);
                    }
                }
            }
        }
        catch
        {
            d.CellDisplayText = dt.Day.ToString();
            d.CellDisplayBrush = Brushes.Gray;
        }

        return d;
    }


    private static string? GetTraditionalFestival(DateTime dt, int displayMonth, int lDay, bool isLeap)
    {
        if (isLeap) return null; // 闰月通常不按节日计

        // 除夕精准推算：若明日为农历正月初一，则今日必为除夕（自动兼容腊月29/30大小月）
        var tomorrow = dt.AddDays(1);
        try
        {
            int nextYear = _lunar.GetYear(tomorrow);
            int nextMonth = _lunar.GetMonth(tomorrow);
            int nextLeap = _lunar.GetLeapMonth(nextYear);
            int nextDisplayMonth = nextMonth;
            if (nextLeap > 0 && nextMonth >= nextLeap)
            {
                nextDisplayMonth = nextMonth - 1;
            }
            int nextDay = _lunar.GetDayOfMonth(tomorrow);
            if (nextDisplayMonth == 1 && nextDay == 1)
            {
                return "除夕";
            }
        }
        catch { }

        if (displayMonth == 1 && lDay == 1) return "春节";
        if (displayMonth == 1 && lDay == 15) return "元宵节";
        if (displayMonth == 2 && lDay == 2) return "龙抬头";
        if (displayMonth == 5 && lDay == 5) return "端午节";
        if (displayMonth == 7 && lDay == 7) return "七夕节";
        if (displayMonth == 7 && lDay == 15) return "中元节";
        if (displayMonth == 8 && lDay == 15) return "中秋节";
        if (displayMonth == 9 && lDay == 9) return "重阳节";
        if (displayMonth == 12 && lDay == 8) return "腊八节";
        if (displayMonth == 12 && lDay == 23) return "小年";

        return null;
    }

    private static string? GetSolarFestival(DateTime dt)
    {
        int m = dt.Month;
        int d = dt.Day;

        if (m == 1 && d == 1) return "元旦";
        if (m == 2 && d == 14) return "情人节";
        if (m == 3 && d == 8) return "妇女节";
        if (m == 3 && d == 12) return "植树节";
        if (m == 4 && d == 1) return "愚人节";
        if (m == 5 && d == 1) return "劳动节";
        if (m == 5 && d == 4) return "青年节";
        if (m == 6 && d == 1) return "儿童节";
        if (m == 7 && d == 1) return "建党节";
        if (m == 8 && d == 1) return "建军节";
        if (m == 9 && d == 10) return "教师节";
        if (m == 10 && d == 1) return "国庆节";
        if (m == 12 && d == 24) return "平安夜";
        if (m == 12 && d == 25) return "圣诞节";

        return null;
    }

    public static string? GetSolarTerm(DateTime dt)
    {
        int year = dt.Year;
        if (year < 2000 || year > 2099) return null;
        int month = dt.Month;
        int y = year % 100;
        double dFactor = 0.2422;

        int termIndex1 = (month - 1) * 2;
        int termIndex2 = termIndex1 + 1;

        int day1 = (int)(y * dFactor + SolarTermC[termIndex1]) - ((y - 1) / 4);
        int day2 = (int)(y * dFactor + SolarTermC[termIndex2]) - ((y - 1) / 4);

        if (termIndex1 == 0 && year == 2019) day1 = 5;
        if (termIndex1 == 6 && year == 2010) day1 = 5;

        if (dt.Day == day1) return SolarTermNames[termIndex1];
        if (dt.Day == day2) return SolarTermNames[termIndex2];

        return null;
    }

    // 调休上班（补班）官方日期映射表 (yyyy-MM-dd -> 事项说明)
    private static readonly Dictionary<string, string> WorkdayTable = new()
    {
        // 2024
        ["2024-02-04"] = "春节调休", ["2024-02-18"] = "春节调休",
        ["2024-04-07"] = "清明调休",
        ["2024-04-28"] = "劳动节调休", ["2024-05-11"] = "劳动节调休",
        ["2024-09-14"] = "中秋节调休",
        ["2024-09-29"] = "国庆节调休", ["2024-10-12"] = "国庆节调休",
        // 2025
        ["2025-01-26"] = "春节调休", ["2025-02-08"] = "春节调休",
        ["2025-04-27"] = "劳动节调休",
        ["2025-09-28"] = "国庆节调休", ["2025-10-11"] = "国庆节调休",
        // 2026 (当前年份)
        ["2026-01-04"] = "元旦调休",
        ["2026-02-14"] = "春节调休", ["2026-02-28"] = "春节调休",
        ["2026-04-26"] = "劳动节调休", ["2026-05-09"] = "劳动节调休",
        ["2026-09-20"] = "国庆节调休", ["2026-10-10"] = "国庆节调休",
        // 2027
        ["2027-02-07"] = "春节调休", ["2027-02-20"] = "春节调休",
        ["2027-04-25"] = "劳动节调休", ["2027-05-08"] = "劳动节调休",
        ["2027-09-26"] = "国庆节调休", ["2027-10-09"] = "国庆节调休"
    };

    private static void CheckHolidayAndWorkday(DateTime dt, DayCalendarDetail d)
    {
        // 优先检查是否为调休上班日 (补班)
        string dateKey = dt.ToString("yyyy-MM-dd");
        if (WorkdayTable.TryGetValue(dateKey, out string? workDesc))
        {
            d.IsWorkday = true;
            d.IsHoliday = false;
            d.HolidayName = workDesc;
            return;
        }

        // 1. 国庆节黄金周: 10月1日 ~ 10月7日 (休)
        if (dt.Month == 10 && dt.Day >= 1 && dt.Day <= 7)
        {
            d.IsHoliday = true;
            d.HolidayName = (dt.Day == 1) ? "国庆节" : "国庆节假期";
            return;
        }

        // 2. 元旦假期: 1月1日 ~ 1月3日 (休)
        if (dt.Month == 1 && dt.Day >= 1 && dt.Day <= 3)
        {
            d.IsHoliday = true;
            d.HolidayName = (dt.Day == 1) ? "元旦" : "元旦假期";
            return;
        }

        // 3. 劳动节假期: 5月1日 ~ 5月5日 (休)
        if (dt.Month == 5 && dt.Day >= 1 && dt.Day <= 5)
        {
            d.IsHoliday = true;
            d.HolidayName = (dt.Day == 1) ? "劳动节" : "劳动节假期";
            return;
        }

        // 4. 春节假期: 农历除夕、正月初一至初六 (休)
        if (d.TraditionalFestival == "除夕")
        {
            d.IsHoliday = true;
            d.HolidayName = "除夕";
            return;
        }
        if (d.LunarMonth == 1 && d.LunarDay >= 1 && d.LunarDay <= 6)
        {
            d.IsHoliday = true;
            d.HolidayName = (d.LunarDay == 1) ? "春节" : "春节假期";
            return;
        }

        // 5. 中秋节假期 (农历八月十五为正日，连休3天)
        if (d.LunarMonth == 8)
        {
            if (d.LunarDay == 15)
            {
                d.IsHoliday = true;
                d.HolidayName = "中秋节";
                return;
            }
            // 2026年精准覆盖: 9.25(八月十五), 9.26(八月十六), 9.27(八月十七) 连休
            if (dt.Year == 2026 && dt.Month == 9 && (dt.Day == 26 || dt.Day == 27))
            {
                d.IsHoliday = true;
                d.HolidayName = "中秋节假期";
                return;
            }
            // 通用规则: 紧邻八月十五且为周末的连休
            if (d.LunarDay == 16 || d.LunarDay == 17)
            {
                if (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
                {
                    d.IsHoliday = true;
                    d.HolidayName = "中秋节假期";
                    return;
                }
            }
        }

        // 6. 端午节假期 (农历五月初五连休3天)
        if (d.LunarMonth == 5 && d.LunarDay == 5)
        {
            d.IsHoliday = true;
            d.HolidayName = "端午节";
            return;
        }

        // 7. 清明节 (4月4日/5日)
        if (d.SolarTerm == "清明")
        {
            d.IsHoliday = true;
            d.HolidayName = "清明节";
            return;
        }
    }

    private static void DetermineCellDisplay(DayCalendarDetail d)
    {
        // 统一副文本为纯净优雅的浅灰，去除花哨杂乱的多种颜色
        var normalBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170));
        d.CellDisplayBrush = normalBrush;
        d.CellFontWeight = FontWeights.Normal;

        // 优先级 1: 传统重大节日 (中秋、春节、除夕、端午、重阳、元宵)
        if (!string.IsNullOrEmpty(d.TraditionalFestival))
        {
            d.CellDisplayText = d.TraditionalFestival;
            d.CellFontWeight = FontWeights.SemiBold;
            d.ToolTipFestivalSummary = $"传统节日 · {d.TraditionalFestival}" + (d.IsHoliday ? " (休)" : "");
            return;
        }

        // 优先级 2: 公历节日 (国庆节、元旦、劳动节、儿童节、教师节等)
        if (!string.IsNullOrEmpty(d.SolarFestival))
        {
            d.CellDisplayText = d.SolarFestival;
            d.CellFontWeight = FontWeights.SemiBold;
            d.ToolTipFestivalSummary = $"节日 · {d.SolarFestival}" + (d.IsHoliday ? " (休)" : "");
            return;
        }

        // 优先级 3: 二十四节气 (秋分、白露、冬至等)
        if (!string.IsNullOrEmpty(d.SolarTerm))
        {
            d.CellDisplayText = d.SolarTerm;
            d.CellFontWeight = FontWeights.SemiBold;
            d.ToolTipFestivalSummary = $"二十四节气 · {d.SolarTerm}";
            return;
        }

        // 普通日期：不显示农历，保持极简
        d.CellDisplayText = string.Empty;
    }

    public static string GetTodayBanner(DayCalendarDetail d)
    {
        // 1. 如果今天是中秋假期中
        if (d.IsHoliday && d.HolidayName != null && d.HolidayName.Contains("中秋"))
        {
            return $"中秋节 (假期中 · {d.LunarMonthName}{d.LunarDayName})";
        }

        // 2. 如果是国庆节黄金周中
        if (d.IsHoliday && d.HolidayName != null && d.HolidayName.Contains("国庆"))
        {
            return $"国庆黄金周 (假期中 · {d.LunarMonthName}{d.LunarDayName})";
        }

        // 3. 如果是春节假期中
        if (d.IsHoliday && d.HolidayName != null && (d.HolidayName.Contains("春节") || d.HolidayName.Contains("除夕")))
        {
            return $"欢度春节 (阖家团圆 · {d.LunarMonthName}{d.LunarDayName})";
        }

        // 4. 当天有传统节日
        if (!string.IsNullOrEmpty(d.TraditionalFestival))
        {
            return $"今日传统节日 · {d.TraditionalFestival}";
        }

        // 5. 当天有公历重大节日
        if (!string.IsNullOrEmpty(d.SolarFestival))
        {
            return $"今日节日 · {d.SolarFestival}";
        }

        // 6. 当天是二十四节气
        if (!string.IsNullOrEmpty(d.SolarTerm))
        {
            return $"二十四节气 · {d.SolarTerm}";
        }

        // 7. 若在其他休假中
        if (d.IsHoliday && !string.IsNullOrEmpty(d.HolidayName))
        {
            return $"{d.HolidayName} (假期中)";
        }

        return string.Empty;
    }
}

/// <summary>
/// 现代化 Fluent 风格日历悬浮窗口
/// </summary>
// 菜单弹出层是独立 HWND；用屏幕坐标统一判断分类和子菜单的鼠标范围。
// 短暂离开分类不立即接受 WPF 的关闭请求，跨层移动时保留展开状态。
public sealed class CalendarSubmenuItem : MenuItem
{
    private static readonly CoerceValueCallback? BaseCoerce =
        IsSubmenuOpenProperty.GetMetadata(typeof(MenuItem)).CoerceValueCallback;
    private readonly ContextMenu _ownerMenu;
    private readonly DispatcherTimer _leaveTimer;
    private bool _holdOpen;
    private bool _pointerVisited;
    private DateTime _lastInside;
    public Popup? SubmenuPopup { get; set; }

    static CalendarSubmenuItem()
    {
        IsSubmenuOpenProperty.OverrideMetadata(typeof(CalendarSubmenuItem),
            new FrameworkPropertyMetadata(false, null, (d, value) =>
            {
                var item = (CalendarSubmenuItem)d;
                if (item._holdOpen && item._ownerMenu.IsOpen) return true;
                return BaseCoerce?.Invoke(d, value) ?? value;
            }));
    }

    public CalendarSubmenuItem(ContextMenu ownerMenu)
    {
        _ownerMenu = ownerMenu;
        SetResourceReference(StyleProperty, typeof(MenuItem));
        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _leaveTimer.Tick += (_, _) =>
        {
            if (!_ownerMenu.IsOpen) { ReleaseSubmenu(); return; }
            if (!NativeMethods.GetCursorPos(out POINT point)) return;
            if (ContainsScreenPoint(this, point) || ContainsScreenPoint(SubmenuPopup?.Child, point))
            {
                _pointerVisited = true;
                _lastInside = DateTime.UtcNow;
            }
            else if (_pointerVisited && (DateTime.UtcNow - _lastInside).TotalMilliseconds >= 400)
            {
                ReleaseSubmenu();
            }
        };
        SubmenuOpened += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, this)) return;
            CloseOtherCategories();
            _holdOpen = true;
            _pointerVisited = IsMouseOver;
            _lastInside = DateTime.UtcNow;
            _leaveTimer.Start();
        };
        MouseEnter += (_, _) => CloseOtherCategories();
        ownerMenu.Closed += (_, _) => ReleaseSubmenu();
        ownerMenu.PreviewKeyDown += (_, e) =>
        {
            // 键盘关闭必须优先于鼠标停留保护，交给 WPF 继续处理焦点和根菜单。
            if (e.Key == Key.Escape || e.Key == Key.Left) ReleaseSubmenu();
        };
        Unloaded += (_, _) => ReleaseSubmenu();
    }

    private void CloseOtherCategories()
    {
        foreach (var other in _ownerMenu.Items.OfType<CalendarSubmenuItem>())
            if (!ReferenceEquals(other, this)) other.ReleaseSubmenu();
    }

    private void ReleaseSubmenu()
    {
        _holdOpen = false;
        _leaveTimer.Stop();
        SetCurrentValue(IsSubmenuOpenProperty, false);
        CoerceValue(IsSubmenuOpenProperty);
    }

    private static bool ContainsScreenPoint(UIElement? element, POINT point)
    {
        if (element == null || !element.IsVisible || PresentationSource.FromVisual(element) == null)
            return false;
        var local = element.PointFromScreen(new Point(point.x, point.y));
        return new Rect(new Point(), element.RenderSize).Contains(local);
    }
}

public class CalendarWindow : Window
{
    // 左侧顶部交互式年/月/日微调部件
    private TextBlock _navYearText = null!;
    private Button _navPrevYearBtn = null!;
    private Button _navNextYearBtn = null!;
    private Border _navYearBox = null!;

    private TextBlock _navMonthText = null!;
    private Button _navPrevMonthBtn = null!;
    private Button _navNextMonthBtn = null!;
    private Border _navMonthBox = null!;

    private TextBlock _navDayText = null!;
    private Button _navPrevDayBtn = null!;
    private Button _navNextDayBtn = null!;
    private Border _navDayBox = null!;

    private Button _navTodayBtn = null!;

    // 当前聚焦选定的日期 (默认今天)
    private DateTime _selectedDate = DateTime.Today;

    // 双选日期跨度计算
    private DateTime? _rangeStartDate;
    private DateTime? _rangeEndDate;
    private readonly Border _rangeCard;
    private readonly TextBlock _rangeInfoText;
    private readonly Button _rangeClearBtn;

    // 极简日期推算面板
    private DateTime _calcBaseDate = DateTime.Today;
    private readonly Border _calcCard;
    private readonly TextBlock _calcBaseDateLabel;
    private readonly TextBox _calcDaysInput;
    private readonly TextBlock _calcResultDateText;
    private readonly TextBlock _calcResultLunarText;
    private readonly TextBlock _calcResultFestText;
    private readonly TextBlock _calcResultSpanText;

    private readonly Border _inlineCard;
    private readonly TextBlock _inlineTitle;
    private readonly TextBox _inlineInput;
    private readonly Button _inlineConfirmBtn;
    private readonly Button _inlineCancelBtn;
    private Action<string>? _inlineAction;

    // 左侧月历连续滚动视口与面板 (文章式无缝滚动)
    private readonly ScrollViewer _calendarScroll;
    private readonly StackPanel _calendarScrollPanel;
    private readonly Dictionary<DateTime, Border> _dateCellElements = new();
    private readonly Dictionary<(int year, int month), FrameworkElement> _monthSectionElements = new();
    private bool _isProgrammaticScrolling;

    private sealed class DateCellHolder
    {
        public DateTime Date;
        public Border CellBorder = null!;
        public TextBlock DayText = null!;
        public TextBlock SubText = null!;
        public DayCalendarDetail Detail = null!;
    }
    private readonly Dictionary<DateTime, DateCellHolder> _dateCellHolders = new();

    // 右侧板块 1：顶部搜索框与添加按钮
    private readonly TextBox _searchInput;
    private readonly TextBlock _searchPlaceholder;
    private bool _isSearchMode;

    // 右侧板块 1：临近倒计时组件
    private readonly Border _countdownCard;
    private readonly StackPanel _countdownStack;

    // 右侧板块 2：选定日期事项列表与操作
    private readonly TextBlock _selectedDateTitle;
    private readonly Button _addItemBtn;
    private readonly ScrollViewer _remindersScrollViewer;
    private readonly StackPanel _remindersListStack;
    private readonly Border _emptyStateBorder;

    // 右侧板块 2 之增/改编辑表单
    private readonly Border _editorCard;
    private readonly TextBlock _editorHeader;
    private readonly RadioButton _radioReminder;
    private readonly RadioButton _radioAlarm;
    private readonly TextBox _editorTitleInput;
    private readonly StackPanel _editorTimeRow;
    private readonly TextBox _editorHourInput;
    private readonly TextBox _editorMinInput;
    private readonly Button _editorSaveBtn;
    private readonly Button _editorCancelBtn;
    private string? _editingItemId;

    private readonly DispatcherTimer _clockTimer;
    private DateTime _displayMonth = DateTime.Today;
    private DateTime? _highlightDate;

    public IntPtr WindowHandle { get; private set; }
    public bool IsFlyoutVisible => IsVisible;
    private bool _isMenuOpen;
    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        set { _isMenuOpen = value; TaskbarCalendarService.Instance.PublishWindowState(this); }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowHandle = new WindowInteropHelper(this).Handle;
        TaskbarCalendarService.Instance.PublishWindowState(this);
    }

    public bool IsPointInside(POINT pt)
    {
        if (!IsVisible || WindowHandle == IntPtr.Zero) return false;
        if (NativeMethods.GetWindowRect(WindowHandle, out RECT rc))
        {
            return (pt.x >= rc.left && pt.x <= rc.right && pt.y >= rc.top && pt.y <= rc.bottom);
        }
        return false;
    }

    private bool _isTodayHovered;
    private void UpdateTodayBtnState()
    {
        if (_navTodayBtn == null) return;
        if (_isTodayHovered)
        {
            _navTodayBtn.Content = "今";
            _navTodayBtn.Background = new SolidColorBrush(Color.FromArgb(60, 10, 138, 92));
            _navTodayBtn.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            _navTodayBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 10, 138, 92));
            _navTodayBtn.ToolTip = "回到今天";
        }
        else
        {
            string weekChar = _selectedDate.DayOfWeek switch
            {
                DayOfWeek.Monday => "一",
                DayOfWeek.Tuesday => "二",
                DayOfWeek.Wednesday => "三",
                DayOfWeek.Thursday => "四",
                DayOfWeek.Friday => "五",
                DayOfWeek.Saturday => "六",
                DayOfWeek.Sunday => "日",
                _ => "今"
            };
            _navTodayBtn.Content = weekChar;
            bool isWeekend = (_selectedDate.DayOfWeek == DayOfWeek.Saturday || _selectedDate.DayOfWeek == DayOfWeek.Sunday);
            _navTodayBtn.Foreground = isWeekend
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) // 周六和周日是绿色
                : new SolidColorBrush(Color.FromRgb(161, 161, 170)); // 其余是灰色
            _navTodayBtn.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            _navTodayBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            _navTodayBtn.ToolTip = $"星期{weekChar} (悬停点击回到今天)";
        }
    }

    private FrameworkElement CreateDateNavHeader()
    {
        var navBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };

        // 1. 年份交互块
        _navYearBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Padding = new Thickness(3, 2, 3, 2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var yearSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _navPrevYearBtn = CreateNavArrowBtn("‹", "上一年");
        _navPrevYearBtn.Click += (_, _) => StepYear(-1);
        _navNextYearBtn = CreateNavArrowBtn("›", "下一年");
        _navNextYearBtn.Click += (_, _) => StepYear(1);

        _navYearText = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0)
        };

        yearSp.Children.Add(_navPrevYearBtn);
        yearSp.Children.Add(_navYearText);
        yearSp.Children.Add(_navNextYearBtn);
        _navYearBox.Child = yearSp;

        _navYearBox.MouseEnter += (_, _) =>
        {
            _navYearBox.Background = new SolidColorBrush(Color.FromArgb(35, 10, 138, 92));
            _navPrevYearBtn.Visibility = Visibility.Visible;
            _navNextYearBtn.Visibility = Visibility.Visible;
        };
        _navYearBox.MouseLeave += (_, _) =>
        {
            _navYearBox.Background = Brushes.Transparent;
            _navPrevYearBtn.Visibility = Visibility.Hidden;
            _navNextYearBtn.Visibility = Visibility.Hidden;
        };
        _navYearBox.MouseWheel += (_, e) =>
        {
            StepYear(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };

        // 2. 月份交互块
        _navMonthBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Padding = new Thickness(3, 2, 3, 2),
            Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var monthSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _navPrevMonthBtn = CreateNavArrowBtn("‹", "上一月");
        _navPrevMonthBtn.Click += (_, _) => StepMonth(-1);
        _navNextMonthBtn = CreateNavArrowBtn("›", "下一月");
        _navNextMonthBtn.Click += (_, _) => StepMonth(1);

        _navMonthText = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0)
        };

        monthSp.Children.Add(_navPrevMonthBtn);
        monthSp.Children.Add(_navMonthText);
        monthSp.Children.Add(_navNextMonthBtn);
        _navMonthBox.Child = monthSp;

        _navMonthBox.MouseEnter += (_, _) =>
        {
            _navMonthBox.Background = new SolidColorBrush(Color.FromArgb(35, 10, 138, 92));
            _navPrevMonthBtn.Visibility = Visibility.Visible;
            _navNextMonthBtn.Visibility = Visibility.Visible;
        };
        _navMonthBox.MouseLeave += (_, _) =>
        {
            _navMonthBox.Background = Brushes.Transparent;
            _navPrevMonthBtn.Visibility = Visibility.Hidden;
            _navNextMonthBtn.Visibility = Visibility.Hidden;
        };
        _navMonthBox.MouseWheel += (_, e) =>
        {
            StepMonth(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };

        // 3. 日期交互块 (两位数纯白日，绝不横跳)
        _navDayBox = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Padding = new Thickness(3, 2, 3, 2),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var daySp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _navPrevDayBtn = CreateNavArrowBtn("‹", "上一天");
        _navPrevDayBtn.Click += (_, _) => StepDay(-1);
        _navNextDayBtn = CreateNavArrowBtn("›", "下一天");
        _navNextDayBtn.Click += (_, _) => StepDay(1);

        _navDayText = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0)
        };

        daySp.Children.Add(_navPrevDayBtn);
        daySp.Children.Add(_navDayText);
        daySp.Children.Add(_navNextDayBtn);
        _navDayBox.Child = daySp;

        _navDayBox.MouseEnter += (_, _) =>
        {
            _navDayBox.Background = new SolidColorBrush(Color.FromArgb(35, 10, 138, 92));
            _navPrevDayBtn.Visibility = Visibility.Visible;
            _navNextDayBtn.Visibility = Visibility.Visible;
        };
        _navDayBox.MouseLeave += (_, _) =>
        {
            _navDayBox.Background = Brushes.Transparent;
            _navPrevDayBtn.Visibility = Visibility.Hidden;
            _navNextDayBtn.Visibility = Visibility.Hidden;
        };
        _navDayBox.MouseWheel += (_, e) =>
        {
            StepDay(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };

        // 4. 今日快捷复位按钮 (未悬浮时显示星期单字；悬浮时显示今字，可点击回到今天)
        _navTodayBtn = new Button
        {
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Width = 24,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Visible
        };
        _navTodayBtn.MouseEnter += (_, _) => { _isTodayHovered = true; UpdateTodayBtnState(); };
        _navTodayBtn.MouseLeave += (_, _) => { _isTodayHovered = false; UpdateTodayBtnState(); };
        _navTodayBtn.Click += (_, _) =>
        {
            var old = _selectedDate;
            _displayMonth = DateTime.Today;
            _selectedDate = DateTime.Today;
            UpdateCellVisual(old);
            UpdateCellVisual(_selectedDate);
            RefreshRightPanel(_selectedDate);
            UpdateNavHeader();
            ScrollToDate(DateTime.Today);
        };
        UpdateTodayBtnState();

        navBar.Children.Add(_navYearBox);
        navBar.Children.Add(_navMonthBox);
        navBar.Children.Add(_navDayBox);
        navBar.Children.Add(_navTodayBtn);

        return navBar;
    }

    private static Button CreateNavArrowBtn(string content, string toolTip)
    {
        var btn = new Button
        {
            Content = content,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Width = 20,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = toolTip,
            Visibility = Visibility.Hidden,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        btn.MouseEnter += (_, _) =>
        {
            btn.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // 悬停绿色高亮
            btn.Background = new SolidColorBrush(Color.FromArgb(40, 10, 138, 92));
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216));
            btn.Background = Brushes.Transparent;
        };
        return btn;
    }

    public void SimulateYearHover(bool hover)
    {
        if (_navYearBox != null && _navPrevYearBtn != null && _navNextYearBtn != null)
        {
            _navYearBox.Background = hover ? new SolidColorBrush(Color.FromArgb(35, 10, 138, 92)) : Brushes.Transparent;
            _navPrevYearBtn.Visibility = hover ? Visibility.Visible : Visibility.Hidden;
            _navNextYearBtn.Visibility = hover ? Visibility.Visible : Visibility.Hidden;
            if (hover) _navNextYearBtn.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            else _navNextYearBtn.Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216));
        }
    }

    private void StepDay(int offset)
    {
        var old = _selectedDate;
        _selectedDate = _selectedDate.AddDays(offset);
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        UpdateCellVisual(old);
        UpdateCellVisual(_selectedDate);
        RefreshRightPanel(_selectedDate);
        UpdateNavHeader();
        ScrollToDate(_selectedDate);
    }

    private void StepMonth(int offset)
    {
        _displayMonth = _displayMonth.AddMonths(offset);
        int days = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        int day = Math.Min(_selectedDate.Day, days);
        var old = _selectedDate;
        _selectedDate = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
        UpdateCellVisual(old);
        UpdateCellVisual(_selectedDate);
        RefreshRightPanel(_selectedDate);
        UpdateNavHeader();
        ScrollToMonth(_displayMonth.Year, _displayMonth.Month);
    }

    private void StepYear(int offset)
    {
        _displayMonth = _displayMonth.AddYears(offset);
        int days = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        int day = Math.Min(_selectedDate.Day, days);
        var old = _selectedDate;
        _selectedDate = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
        UpdateCellVisual(old);
        UpdateCellVisual(_selectedDate);
        RefreshRightPanel(_selectedDate);
        UpdateNavHeader();
        ScrollToMonth(_displayMonth.Year, _displayMonth.Month);
    }

    private void UpdateNavHeader()
    {
        if (_navYearText == null) return;
        _navYearText.Text = $"{_selectedDate.Year}年";
        _navMonthText.Text = $"{_selectedDate.Month:D2}月";
        _navDayText.Text = $"{_selectedDate:dd日}";
        UpdateTodayBtnState();
    }

    public CalendarWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        Width = 720;
        Height = 490;

        // 主卡片容器（深色磨砂、圆角、发光微边框）
        var rootBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 24, 24, 27)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 16, 16, 16),
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Opacity = 0.5,
                Color = Colors.Black
            }
        };

        // 左右分栏主 Grid
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(385) }); // 左侧日历主体宽度扩展到 385
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });      // 中间竖向分割线
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 右侧事项与倒计时面板

        // ==================== 左侧面板 ====================
        var leftStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. 顶部交互式年/月/日微调控制器 (悬浮箭头切换)
        var dateNavHeader = CreateDateNavHeader();
        leftStack.Children.Add(dateNavHeader);

        // 双选日期跨度计算横条卡片 (固定高度与占位，常驻显示预填提示，消除上下布局跳动)
        _rangeCard = new Border
        {
            CornerRadius = new CornerRadius(7),
            Height = 28,
            Padding = new Thickness(12, 3, 10, 3),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, // 水平居中对齐
            Visibility = Visibility.Visible
        };
        var rangeDock = new DockPanel();
        _rangeClearBtn = new Button
        {
            Content = "×",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 235, 255)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 1, 2, 1),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "清除跨度选择",
            Visibility = Visibility.Collapsed
        };
        _rangeClearBtn.Click += (_, _) => ClearRangeSelection();
        DockPanel.SetDock(_rangeClearBtn, Dock.Right);
        rangeDock.Children.Add(_rangeClearBtn);

        _rangeInfoText = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            VerticalAlignment = VerticalAlignment.Center
        };
        rangeDock.Children.Add(_rangeInfoText);
        _rangeCard.Child = rangeDock;

        // 极简日期推算面板
        _calcCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(245, 30, 32, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 52, 211, 153)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        var calcStack = new StackPanel();
        var calcTopDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var closeCalcBtn = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        closeCalcBtn.Click += (_, _) => HideCalculationPanel();
        DockPanel.SetDock(closeCalcBtn, Dock.Right);
        calcTopDock.Children.Add(closeCalcBtn);

        _calcDaysInput = new TextBox
        {
            Width = 80,
            Height = 24,
            FontSize = 11.5,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.White
        };
        _calcDaysInput.TextChanged += (_, _) =>
        {
            string txt = _calcDaysInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(txt)) return;
            if (int.TryParse(txt.TrimStart('+'), out int offset))
                PerformCalculation(offset);
        };
        _calcDaysInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { HideCalculationPanel(); e.Handled = true; }
        };

        var titleSp = new StackPanel { Orientation = Orientation.Horizontal };
        titleSp.Children.Add(new TextBlock
        {
            Text = "日期推算",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        _calcBaseDateLabel = new TextBlock
        {
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        _calcBaseDateLabel.ToolTip = "点击重置基准日期为今天";
        _calcBaseDateLabel.MouseLeftButtonDown += (_, _) =>
        {
            _calcBaseDate = DateTime.Today;
            if (int.TryParse(_calcDaysInput.Text?.Trim().TrimStart('+'), out int d)) PerformCalculation(d);
            else PerformCalculation(3);
        };
        titleSp.Children.Add(_calcBaseDateLabel);
        calcTopDock.Children.Add(titleSp);
        calcStack.Children.Add(calcTopDock);

        var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        int[] quickDays = { 3, 5, 7, 30 };
        foreach (int qd in quickDays)
        {
            int d = qd;
            var qBtn = new Button
            {
                Content = d.ToString(),
                Width = 32,
                Height = 24,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 50)),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 225)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 5, 0)
            };
            qBtn.Click += (_, _) =>
            {
                _calcDaysInput.Text = d.ToString();
                _calcDaysInput.CaretIndex = _calcDaysInput.Text.Length;
                PerformCalculation(d);
            };
            inputRow.Children.Add(qBtn);
        }
        inputRow.Children.Add(_calcDaysInput);
        inputRow.Children.Add(new TextBlock
        {
            Text = "天",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        });
        calcStack.Children.Add(inputRow);

        var resultBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(45, 16, 185, 129)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 52, 211, 153)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6)
        };
        var resultSp = new StackPanel();
        _calcResultDateText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
            Margin = new Thickness(0, 0, 0, 2)
        };
        _calcResultLunarText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Margin = new Thickness(0, 0, 0, 2)
        };
        _calcResultFestText = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
            Margin = new Thickness(0, 0, 0, 2),
            Visibility = Visibility.Collapsed
        };
        _calcResultSpanText = new TextBlock
        {
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170))
        };
        resultSp.Children.Add(_calcResultDateText);
        resultSp.Children.Add(_calcResultLunarText);
        resultSp.Children.Add(_calcResultFestText);
        resultSp.Children.Add(_calcResultSpanText);
        resultBorder.Child = resultSp;
        calcStack.Children.Add(resultBorder);
        _calcCard.Child = calcStack;

        // 内嵌交互小卡片
        _inlineCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };
        var inlineStack = new StackPanel();
        _inlineTitle = new TextBlock
        {
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216)),
            Margin = new Thickness(0, 0, 0, 6)
        };
        _inlineInput = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            Padding = new Thickness(6, 4, 6, 4),
            CaretBrush = Brushes.White
        };
        _inlineInput.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; ConfirmInlineInput(); }
            else if (e.Key == Key.Escape) { e.Handled = true; HideInlineInput(); }
        };
        var inlineBtnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        _inlineConfirmBtn = new Button
        {
            Content = "确定",
            Width = 52,
            Height = 24,
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        _inlineConfirmBtn.Click += (_, _) => ConfirmInlineInput();
        _inlineCancelBtn = new Button
        {
            Content = "取消",
            Width = 48,
            Height = 24,
            FontSize = 11,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0)
        };
        _inlineCancelBtn.Click += (_, _) => HideInlineInput();
        inlineBtnRow.Children.Add(_inlineConfirmBtn);
        inlineBtnRow.Children.Add(_inlineCancelBtn);
        inlineStack.Children.Add(_inlineTitle);
        inlineStack.Children.Add(_inlineInput);
        inlineStack.Children.Add(inlineBtnRow);
        _inlineCard.Child = inlineStack;

        // 分割线
        leftStack.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        // 星期标题栏（周六和周日两列使用绿色字体）
        var weekHeaderGrid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) };
        string[] weekNames = { "一", "二", "三", "四", "五", "六", "日" };
        foreach (var w in weekNames)
        {
            bool isWeekendTitle = (w == "六" || w == "日");
            weekHeaderGrid.Children.Add(new TextBlock
            {
                Text = w,
                FontSize = 12.5,
                FontWeight = isWeekendTitle ? FontWeights.SemiBold : FontWeights.Medium,
                Foreground = isWeekendTitle
                    ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                    : new SolidColorBrush(Color.FromRgb(180, 180, 185)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        leftStack.Children.Add(weekHeaderGrid);

        // 4. 文章式连续滚动日历视口 (高度 320，滚轮丝滑上下滚动)
        _calendarScrollPanel = new StackPanel();
        _calendarScroll = new ScrollViewer
        {
            Height = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
            Content = _calendarScrollPanel
        };
        _calendarScroll.PreviewMouseWheel += (s, e) =>
        {
            double step = (e.Delta > 0 ? -1 : 1) * 72.0;
            double target = Math.Max(0, Math.Min(_calendarScroll.ScrollableHeight, _calendarScroll.VerticalOffset + step));
            _calendarScroll.ScrollToVerticalOffset(target);
            e.Handled = true;
        };
        _calendarScroll.ScrollChanged += CalendarScroll_ScrollChanged;

        leftStack.PreviewMouseWheel += (s, e) =>
        {
            if (_calcCard.Visibility == Visibility.Visible && _calcCard.IsMouseOver) return;
            double step = (e.Delta > 0 ? -1 : 1) * 72.0;
            double target = Math.Max(0, Math.Min(_calendarScroll.ScrollableHeight, _calendarScroll.VerticalOffset + step));
            _calendarScroll.ScrollToVerticalOffset(target);
            e.Handled = true;
        };

        leftStack.Children.Add(_calendarScroll);
        leftStack.Children.Add(_rangeCard); // 计算天数结果放置在左侧日历下方水平居中

        Grid.SetColumn(leftStack, 0);
        mainGrid.Children.Add(leftStack);

        // ==================== 竖向分割线 ====================
        var verticalDivider = new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Margin = new Thickness(14, 4, 14, 4)
        };
        Grid.SetColumn(verticalDivider, 1);
        mainGrid.Children.Add(verticalDivider);

        // ==================== 右侧面板 ====================
        var rightDock = new DockPanel();

        // 1. 顶部：搜索框 与 添加按钮 (右侧最上方是搜索框，搜索框右侧是添加按钮)
        var topSearchDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };

        _addItemBtn = new Button
        {
            Content = "+ 添加",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Height = 36,
            MinWidth = 72,
            Padding = new Thickness(12, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromRgb(46, 46, 50)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 225)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 75, 82)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        // A slim, rounded button template keeps its corners intact on hover.
        var addButtonChrome = new FrameworkElementFactory(typeof(Border));
        addButtonChrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        addButtonChrome.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        addButtonChrome.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        addButtonChrome.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        var addButtonContent = new FrameworkElementFactory(typeof(ContentPresenter));
        addButtonContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        addButtonContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        addButtonChrome.AppendChild(addButtonContent);
        _addItemBtn.Template = new ControlTemplate(typeof(Button)) { VisualTree = addButtonChrome };
        _addItemBtn.MouseEnter += (_, _) => _addItemBtn.Background = new SolidColorBrush(Color.FromRgb(60, 60, 66));
        _addItemBtn.MouseLeave += (_, _) => _addItemBtn.Background = new SolidColorBrush(Color.FromRgb(46, 46, 50));
        _addItemBtn.Click += (_, _) => OpenEditorForNew();
        AutomationProperties.SetAutomationId(_addItemBtn, "calendar.add");
        DockPanel.SetDock(_addItemBtn, Dock.Right);
        topSearchDock.Children.Add(_addItemBtn);

        // Rounded search surface with a subtle leading icon and a focus accent.
        var searchChrome = new Border
        {
            Height = 36,
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(11, 0, 10, 0),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromRgb(35, 35, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 73)),
            BorderThickness = new Thickness(1)
        };
        var searchBoxGrid = new Grid();
        searchBoxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(23) });
        searchBoxGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var searchGlyph = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 6.5,1.5 A 5,5 0 1 1 6.5,11.5 A 5,5 0 1 1 6.5,1.5 M 10.3,10.3 L 15,15"),
            Stroke = new SolidColorBrush(Color.FromRgb(145, 145, 153)),
            StrokeThickness = 1.5,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Grid.SetColumn(searchGlyph, 0);
        searchBoxGrid.Children.Add(searchGlyph);
        _searchPlaceholder = new TextBlock
        {
            Text = "搜索待办 / 闹钟...",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(142, 142, 152)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            IsHitTestVisible = false
        };
        _searchInput = new TextBox
        {
            Height = 32,
            FontSize = 12.5,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153))
        };
        AutomationProperties.SetAutomationId(_searchInput, "calendar.search");
        _searchInput.TextChanged += (_, _) =>
        {
            string q = _searchInput.Text?.Trim() ?? string.Empty;
            _searchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;
            HandleSearch(q);
        };
        _searchInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _searchInput.Text = string.Empty;
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        Grid.SetColumn(_searchInput, 1);
        Grid.SetColumn(_searchPlaceholder, 1);
        searchBoxGrid.Children.Add(_searchInput);
        searchBoxGrid.Children.Add(_searchPlaceholder);
        searchChrome.Child = searchBoxGrid;
        _searchInput.GotKeyboardFocus += (_, _) =>
            searchChrome.BorderBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        _searchInput.LostKeyboardFocus += (_, _) =>
            searchChrome.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 73));
        searchChrome.MouseEnter += (_, _) =>
        {
            if (!_searchInput.IsKeyboardFocused)
                searchChrome.BorderBrush = new SolidColorBrush(Color.FromRgb(98, 98, 108));
        };
        searchChrome.MouseLeave += (_, _) =>
        {
            if (!_searchInput.IsKeyboardFocused)
                searchChrome.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 73));
        };
        topSearchDock.Children.Add(searchChrome);

        DockPanel.SetDock(topSearchDock, Dock.Top);
        rightDock.Children.Add(topSearchDock);

        // 2. 搜索框下方内容展示：极简推算卡片与内嵌卡片
        DockPanel.SetDock(_calcCard, Dock.Top);
        rightDock.Children.Add(_calcCard);

        DockPanel.SetDock(_inlineCard, Dock.Top);
        rightDock.Children.Add(_inlineCard);

        // 3. 底部：倒计时卡片 (放到下面，排除今天，移除右侧最近3个节点多余文字)
        _countdownCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(90, 39, 39, 42)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var countdownInner = new StackPanel();
        var cdTitleDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        cdTitleDock.Children.Add(new TextBlock
        {
            Text = "倒计时",
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231))
        });
        countdownInner.Children.Add(cdTitleDock);

        _countdownStack = new StackPanel();
        countdownInner.Children.Add(_countdownStack);
        _countdownCard.Child = countdownInner;

        DockPanel.SetDock(_countdownCard, Dock.Bottom);
        rightDock.Children.Add(_countdownCard);

        // 4. 中间主区域：对应日期的事项管理 (优先凸显，移除了重复多余的顶部日期标题)
        var reminderMainDock = new DockPanel();

        _selectedDateTitle = new TextBlock
        {
            Visibility = Visibility.Collapsed
        };

        // 事项列表与编辑卡片的宿主 Grid
        var contentGrid = new Grid();

        _remindersScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 310
        };
        _remindersListStack = new StackPanel();
        _remindersScrollViewer.Content = _remindersListStack;
        contentGrid.Children.Add(_remindersScrollViewer);

        // 空状态只保留轻量提示：新增入口已在右上角，无需重复按钮。
        _emptyStateBorder = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 20, 14, 20),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "无",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(135, 135, 143)),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        contentGrid.Children.Add(_emptyStateBorder);

        // 编辑/新增表单卡片
        _editorCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        var editFormStack = new StackPanel();
        _editorHeader = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        editFormStack.Children.Add(_editorHeader);

        var radioRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _radioReminder = new RadioButton
        {
            Content = "待办备忘",
            Foreground = Brushes.White,
            IsChecked = true,
            FontSize = 11.5,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 14, 0)
        };
        _radioAlarm = new RadioButton
        {
            Content = "定时闹钟",
            Foreground = Brushes.White,
            FontSize = 11.5,
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetAutomationId(_radioReminder, "calendar.editor.reminder");
        AutomationProperties.SetAutomationId(_radioAlarm, "calendar.editor.alarm");
        _radioReminder.Checked += (_, _) => _editorTimeRow.Visibility = Visibility.Collapsed;
        _radioAlarm.Checked += (_, _) => _editorTimeRow.Visibility = Visibility.Visible;
        radioRow.Children.Add(_radioReminder);
        radioRow.Children.Add(_radioAlarm);
        editFormStack.Children.Add(radioRow);

        _editorTitleInput = new TextBox
        {
            Height = 26,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2),
            CaretBrush = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        };
        AutomationProperties.SetAutomationId(_editorTitleInput, "calendar.editor.title");
        _editorTitleInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; SaveEditor(); }
            else if (e.Key == Key.Escape) { e.Handled = true; HideEditor(); }
        };
        editFormStack.Children.Add(_editorTitleInput);

        _editorTimeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        _editorTimeRow.Children.Add(new TextBlock
        {
            Text = "响铃时间: ",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _editorHourInput = new TextBox
        {
            Width = 32,
            Height = 22,
            FontSize = 11.5,
            Text = "09",
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            BorderThickness = new Thickness(1),
            TextAlignment = TextAlignment.Center,
            CaretBrush = Brushes.White
        };
        _editorMinInput = new TextBox
        {
            Width = 32,
            Height = 22,
            FontSize = 11.5,
            Text = "00",
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            BorderThickness = new Thickness(1),
            TextAlignment = TextAlignment.Center,
            CaretBrush = Brushes.White
        };
        _editorTimeRow.Children.Add(_editorHourInput);
        _editorTimeRow.Children.Add(new TextBlock
        {
            Text = " : ",
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        });
        _editorTimeRow.Children.Add(_editorMinInput);
        editFormStack.Children.Add(_editorTimeRow);

        var editorBtnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _editorSaveBtn = new Button
        {
            Content = "保存",
            Width = 52,
            Height = 24,
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(10, 138, 92)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0)
        };
        _editorSaveBtn.MouseEnter += (_, _) => _editorSaveBtn.Background = new SolidColorBrush(Color.FromRgb(13, 155, 103));
        _editorSaveBtn.MouseLeave += (_, _) => _editorSaveBtn.Background = new SolidColorBrush(Color.FromRgb(10, 138, 92));
        _editorSaveBtn.Click += (_, _) => SaveEditor();
        _editorCancelBtn = new Button
        {
            Content = "取消",
            Width = 48,
            Height = 24,
            FontSize = 11,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetAutomationId(_editorSaveBtn, "calendar.editor.save");
        AutomationProperties.SetAutomationId(_editorCancelBtn, "calendar.editor.cancel");
        _editorCancelBtn.Click += (_, _) => HideEditor();
        editorBtnRow.Children.Add(_editorSaveBtn);
        editorBtnRow.Children.Add(_editorCancelBtn);
        editFormStack.Children.Add(editorBtnRow);

        _editorCard.Child = editFormStack;
        contentGrid.Children.Add(_editorCard);

        reminderMainDock.Children.Add(contentGrid);
        rightDock.Children.Add(reminderMainDock);

        Grid.SetColumn(rightDock, 2);
        mainGrid.Children.Add(rightDock);

        rootBorder.Child = mainGrid;
        Content = rootBorder;

        // 失去焦点自动关闭与按键关闭
        Deactivated += (_, _) => { if (!IsMenuOpen) HideFlyout(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_editorCard.Visibility == Visibility.Visible)
                {
                    HideEditor();
                }
                else if (_calcCard.Visibility == Visibility.Visible)
                {
                    HideCalculationPanel();
                }
                else if (_inlineCard.Visibility == Visibility.Visible)
                {
                    HideInlineInput();
                }
                else if (_rangeStartDate.HasValue)
                {
                    ClearRangeSelection();
                }
                else
                {
                    HideFlyout();
                }
            }
        };

        // 时钟刷新定时器
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        IsVisibleChanged += (_, _) => TaskbarCalendarService.Instance.PublishWindowState(this);
        Closed += (_, _) => { _clockTimer.Stop(); };

        UpdateClock();
        RenderMonthGrid();
        UpdateRangeCard();
        RefreshRightPanel(_selectedDate);
        RefreshCountdownPanel();
    }

    public void UpdateWindowLayout()
    {
        Height = 490;
        Width = 720;
        Reposition();
    }

    public void UpdateRangeCard()
    {
        _rangeCard.Visibility = Visibility.Visible;

        if (!_rangeStartDate.HasValue && !_rangeEndDate.HasValue)
        {
            // 预填默认操作提示（常驻占位，消除高度跳动）
            _rangeCard.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            _rangeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            _rangeCard.BorderThickness = new Thickness(1);
            _rangeInfoText.Text = "双击日期计算天数差";
            _rangeInfoText.Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170));
            _rangeInfoText.FontWeight = FontWeights.Normal;
            _rangeClearBtn.Visibility = Visibility.Collapsed;
            return;
        }

        if (_rangeStartDate.HasValue && !_rangeEndDate.HasValue)
        {
            // 单选起始端点态：提示双击另一日期
            _rangeCard.Background = new SolidColorBrush(Color.FromArgb(45, 59, 130, 246));
            _rangeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 96, 165, 250));
            _rangeCard.BorderThickness = new Thickness(1);
            _rangeInfoText.Text = $"已选 {_rangeStartDate.Value:M.d}，双击另一日期计算差";
            _rangeInfoText.Foreground = new SolidColorBrush(Color.FromRgb(191, 219, 254));
            _rangeInfoText.FontWeight = FontWeights.Medium;
            _rangeClearBtn.Visibility = Visibility.Visible;
            return;
        }

        // 双选完成跨度态：经典纯蓝高亮白字
        var (start, end) = _rangeStartDate!.Value <= _rangeEndDate!.Value
            ? (_rangeStartDate.Value, _rangeEndDate.Value)
            : (_rangeEndDate.Value, _rangeStartDate.Value);

        int span = (end - start).Days;
        int weeks = span / 7;
        int rem = span % 7;
        string weekDesc = weeks > 0 ? (rem > 0 ? $" ({weeks}周{rem}天)" : $" ({weeks}周)") : "";

        _rangeCard.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        _rangeCard.BorderBrush = Brushes.Transparent;
        _rangeCard.BorderThickness = new Thickness(0);
        _rangeInfoText.Text = $"相差 {span} 天{weekDesc}  ({start:M.d} ~ {end:M.d})";
        _rangeInfoText.Foreground = Brushes.White;
        _rangeInfoText.FontWeight = FontWeights.Bold;
        _rangeClearBtn.Visibility = Visibility.Visible;
    }

    public void ClearRangeSelection()
    {
        _rangeStartDate = null;
        _rangeEndDate = null;
        UpdateRangeCard();
        UpdateAllCellVisuals();
    }

    public void HandleDateCellClick(DateTime date)
    {
        // 单击：仅切换当前选中日期，更新右侧内容与顶部星期，显示绿色边框，不改变跨度，保持文章式滚动位置稳定
        var old = _selectedDate;
        _selectedDate = date.Date;
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        UpdateCellVisual(old);
        UpdateCellVisual(_selectedDate);
        RefreshRightPanel(_selectedDate);
        UpdateNavHeader();
    }

    public void HandleDateCellDoubleClick(DateTime date)
    {
        // 双击：固定起止日期进行天数差计算（蓝色高亮）
        var old = _selectedDate;
        _selectedDate = date.Date;
        _displayMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        UpdateCellVisual(old);
        UpdateCellVisual(_selectedDate);
        RefreshRightPanel(_selectedDate);
        UpdateNavHeader();

        if (!_rangeStartDate.HasValue)
        {
            _rangeStartDate = date.Date;
            _rangeEndDate = null;
        }
        else if (!_rangeEndDate.HasValue)
        {
            if (date.Date == _rangeStartDate.Value.Date)
            {
                // 双击同一已选起点，清除选择
                _rangeStartDate = null;
                _rangeEndDate = null;
            }
            else
            {
                _rangeEndDate = date.Date;
            }
        }
        else
        {
            if (date.Date == _rangeStartDate.Value.Date || date.Date == _rangeEndDate.Value.Date)
            {
                // 双击已有端点，清除跨度
                _rangeStartDate = null;
                _rangeEndDate = null;
            }
            else
            {
                // 双击其他日期，开启新的一轮跨度
                _rangeStartDate = date.Date;
                _rangeEndDate = null;
            }
        }

        UpdateRangeCard();
        UpdateAllCellVisuals();
    }

    public void RefreshCountdownPanel()
    {
        _countdownStack.Children.Clear();
        var upcoming = UpcomingEventsHelper.GetUpcomingEvents(DateTime.Today, 3);
        if (upcoming.Count == 0)
        {
            _countdownStack.Children.Add(new TextBlock
            {
                Text = "近期无待办或节假日",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 185)),
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        foreach (var item in upcoming)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 5),
                Cursor = Cursors.Hand
            };
            DateTime targetDate = item.Date;
            row.MouseEnter += (s, e) => row.Background = new SolidColorBrush(Color.FromArgb(85, 59, 130, 246));
            row.MouseLeave += (s, e) => row.Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
            row.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                var old = _selectedDate;
                _displayMonth = new DateTime(targetDate.Year, targetDate.Month, 1);
                _selectedDate = targetDate;
                UpdateCellVisual(old);
                UpdateCellVisual(_selectedDate);
                RefreshRightPanel(_selectedDate);
                UpdateNavHeader();
                ScrollToDate(targetDate);
            };

            var dock = new DockPanel();

            // 右侧倒计时 Badge (精炼数字天数，排除今天)
            string daysDesc = item.DaysRemaining == 1 ? "明天" : $"{item.DaysRemaining}天";
            var badgeBg = item.DaysRemaining == 1
                ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // 橙
                : new SolidColorBrush(Color.FromArgb(180, 59, 130, 246)); // 清晰亮蓝

            var badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = badgeBg,
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeText = new TextBlock
            {
                Text = daysDesc,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            badgeBorder.Child = badgeText;
            DockPanel.SetDock(badgeBorder, Dock.Right);
            dock.Children.Add(badgeBorder);

            // 左侧类型小徽标
            var catBorder = new Border
            {
                CornerRadius = new CornerRadius(3),
                Background = item.Category switch
                {
                    "传统节日" => new SolidColorBrush(Color.FromArgb(70, 239, 68, 68)),
                    "法定节日" => new SolidColorBrush(Color.FromArgb(70, 16, 185, 129)),
                    "二十四节气" => new SolidColorBrush(Color.FromArgb(70, 168, 85, 247)),
                    "闹钟" => new SolidColorBrush(Color.FromArgb(70, 245, 158, 11)),
                    _ => new SolidColorBrush(Color.FromArgb(70, 59, 130, 246))
                },
                MinWidth = 36,
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            // 倒计时标签统一两个汉字，保留完整 Category 用于分类和配色。
            string categoryLabel = item.Category switch
            {
                "法定节日" => "法定",
                "传统节日" => "传统",
                "二十四节气" => "节气",
                "闹钟" => "闹钟",
                "待办" => "待办",
                _ => "其他"
            };
            var catText = new TextBlock
            {
                Text = categoryLabel,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.White
            };
            catBorder.Child = catText;
            DockPanel.SetDock(catBorder, Dock.Left);
            dock.Children.Add(catBorder);

            // 中间标题与日期 (统一浅色字体，不要深色)
            var infoSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleTxt = new TextBlock
            {
                Text = item.Title,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var dateTxt = new TextBlock
            {
                Text = item.Date.ToString("M月d日 dddd"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 216))
            };
            infoSp.Children.Add(titleTxt);
            infoSp.Children.Add(dateTxt);
            dock.Children.Add(infoSp);

            row.Child = dock;
            _countdownStack.Children.Add(row);
        }
    }

    public void RefreshRightPanel(DateTime date)
    {
        _selectedDate = date.Date;
        HideEditor();

        if (_isSearchMode && _searchInput != null && !string.IsNullOrEmpty(_searchInput.Text?.Trim()))
        {
            HandleSearch(_searchInput.Text.Trim());
            return;
        }

        string isTodayStr = date.Date == DateTime.Today ? " (今天)" : "";
        _selectedDateTitle.Text = $"{date:M月d日}{isTodayStr}";

        var items = CalendarReminderManager.Instance.GetItemsForDate(date);
        _remindersListStack.Children.Clear();

        if (items.Count == 0)
        {
            _emptyStateBorder.Visibility = Visibility.Visible;
            _remindersScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            _emptyStateBorder.Visibility = Visibility.Collapsed;
            _remindersScrollViewer.Visibility = Visibility.Visible;

            foreach (var item in items)
            {
                _remindersListStack.Children.Add(CreateReminderItemCard(item, showDate: false));
            }
        }
    }

    private void HandleSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _isSearchMode = false;
            RefreshRightPanel(_selectedDate);
            return;
        }

        _isSearchMode = true;
        HideEditor();

        var matches = CalendarReminderManager.Instance.SearchItems(query);
        _selectedDateTitle.Text = $"搜索结果 (找到 {matches.Count} 项)";
        _remindersListStack.Children.Clear();

        if (matches.Count == 0)
        {
            _emptyStateBorder.Visibility = Visibility.Visible;
            _remindersScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            _emptyStateBorder.Visibility = Visibility.Collapsed;
            _remindersScrollViewer.Visibility = Visibility.Visible;

            foreach (var item in matches)
            {
                _remindersListStack.Children.Add(CreateReminderItemCard(item, showDate: true));
            }
        }
    }

    private FrameworkElement CreateReminderItemCard(CalendarReminderItem item, bool showDate)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 40)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 60)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = showDate ? Cursors.Hand : Cursors.Arrow
        };

        if (showDate)
        {
            card.MouseEnter += (s, e) => card.Background = new SolidColorBrush(Color.FromRgb(45, 45, 52));
            card.MouseLeave += (s, e) => card.Background = new SolidColorBrush(Color.FromRgb(36, 36, 40));
            card.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                var old = _selectedDate;
                _displayMonth = new DateTime(item.TargetDate.Year, item.TargetDate.Month, 1);
                _selectedDate = item.TargetDate.Date;
                _searchInput.Text = string.Empty;
                Keyboard.ClearFocus();
                UpdateCellVisual(old);
                UpdateCellVisual(_selectedDate);
                RefreshRightPanel(_selectedDate);
                UpdateNavHeader();
                ScrollToDate(_selectedDate);
            };
        }

        var dock = new DockPanel();

        // 右侧操作按钮组 (编辑 ✎ 与 删除 ×)
        var btnSp = new StackPanel { Orientation = Orientation.Horizontal };

        var editBtn = new Button
        {
            Content = "✎",
            Width = 20,
            Height = 20,
            FontSize = 11,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "编辑事项"
        };
        var curItem = item;
        editBtn.Click += (_, e) =>
        {
            e.Handled = true;
            OpenEditorForEdit(curItem);
        };
        btnSp.Children.Add(editBtn);

        var delBtn = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "删除事项"
        };
        delBtn.Click += (_, e) =>
        {
            e.Handled = true;
            CalendarReminderManager.Instance.RemoveItemById(curItem.Id);
            RefreshRightPanel(_selectedDate);
            RefreshCountdownPanel();
            RenderMonthGrid();
        };
        btnSp.Children.Add(delBtn);

        DockPanel.SetDock(btnSp, Dock.Right);
        dock.Children.Add(btnSp);

        // 左侧类型胶囊
        var tagBorder = new Border
        {
            CornerRadius = new CornerRadius(3),
            Background = item.IsAlarm
                ? new SolidColorBrush(Color.FromArgb(60, 245, 158, 11))
                : new SolidColorBrush(Color.FromArgb(60, 59, 130, 246)),
            Padding = new Thickness(4, 1.5, 4, 1.5),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var tagText = new TextBlock
        {
            Text = item.IsAlarm ? (item.AlarmTime.HasValue ? $"闹钟 {item.AlarmTime.Value:HH:mm}" : "闹钟") : "备忘",
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Foreground = item.IsAlarm
                ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                : new SolidColorBrush(Color.FromRgb(96, 165, 250))
        };
        tagBorder.Child = tagText;
        DockPanel.SetDock(tagBorder, Dock.Left);
        dock.Children.Add(tagBorder);

        // 中间标题与日期（若处于搜索模式展示日期）
        var infoSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Title
        };
        infoSp.Children.Add(titleText);

        if (showDate)
        {
            infoSp.Children.Add(new TextBlock
            {
                Text = item.TargetDate.ToString("yyyy年M月d日"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        dock.Children.Add(infoSp);
        card.Child = dock;
        return card;
    }

    public void OpenEditorForNew()
    {
        _editingItemId = null;
        _editorHeader.Text = $"新增事项 ({_selectedDate:M月d日})";
        _radioReminder.IsChecked = true;
        _editorTitleInput.Text = string.Empty;
        var now = DateTime.Now;
        _editorHourInput.Text = ((now.Hour + 1) % 24).ToString("D2");
        _editorMinInput.Text = "00";
        _editorTimeRow.Visibility = Visibility.Collapsed;

        _emptyStateBorder.Visibility = Visibility.Collapsed;
        _remindersScrollViewer.Visibility = Visibility.Collapsed;
        _editorCard.Visibility = Visibility.Visible;
        _editorTitleInput.Focus();
    }

    public void OpenEditorForEdit(CalendarReminderItem item)
    {
        _editingItemId = item.Id;
        _editorHeader.Text = $"编辑事项 ({item.TargetDate:M月d日})";
        _editorTitleInput.Text = item.Title;
        if (item.IsAlarm)
        {
            _radioAlarm.IsChecked = true;
            _editorTimeRow.Visibility = Visibility.Visible;
            if (item.AlarmTime.HasValue)
            {
                _editorHourInput.Text = item.AlarmTime.Value.Hour.ToString("D2");
                _editorMinInput.Text = item.AlarmTime.Value.Minute.ToString("D2");
            }
            else
            {
                _editorHourInput.Text = "09";
                _editorMinInput.Text = "00";
            }
        }
        else
        {
            _radioReminder.IsChecked = true;
            _editorTimeRow.Visibility = Visibility.Collapsed;
        }

        _emptyStateBorder.Visibility = Visibility.Collapsed;
        _remindersScrollViewer.Visibility = Visibility.Collapsed;
        _editorCard.Visibility = Visibility.Visible;
        _editorTitleInput.Focus();
        _editorTitleInput.SelectAll();
    }

    public void HideEditor()
    {
        _editingItemId = null;
        _editorCard.Visibility = Visibility.Collapsed;
        // 取消编辑后恢复原先的事项列表或极简空状态，避免右侧留白。
        bool hasItems = _remindersListStack.Children.Count > 0;
        _emptyStateBorder.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        _remindersScrollViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveEditor()
    {
        string title = _editorTitleInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(title))
        {
            _editorTitleInput.Focus();
            return;
        }

        bool isAlarm = _radioAlarm.IsChecked == true;
        DateTime? alarmTime = null;
        if (isAlarm)
        {
            int h = 9, m = 0;
            int.TryParse(_editorHourInput.Text?.Trim(), out h);
            int.TryParse(_editorMinInput.Text?.Trim(), out m);
            if (h < 0) h = 0; if (h > 23) h = 23;
            if (m < 0) m = 0; if (m > 59) m = 59;
            alarmTime = new DateTime(_selectedDate.Year, _selectedDate.Month, _selectedDate.Day, h, m, 0);
        }

        if (string.IsNullOrEmpty(_editingItemId))
        {
            // 新增
            if (isAlarm && alarmTime.HasValue)
            {
                CalendarReminderManager.Instance.AddAlarm(alarmTime.Value, title);
            }
            else
            {
                CalendarReminderManager.Instance.AddReminder(_selectedDate, title);
            }
        }
        else
        {
            // 修改
            CalendarReminderManager.Instance.UpdateItem(_editingItemId, title, isAlarm, alarmTime);
        }

        HideEditor();
        RefreshRightPanel(_selectedDate);
        RefreshCountdownPanel();
        RenderMonthGrid();
    }

    public void OpenCalculationPanel(DateTime baseDate, int initialDays = 3)
    {
        _calcBaseDate = baseDate.Date;
        _calcDaysInput.Text = initialDays.ToString();
        _calcCard.Visibility = Visibility.Visible;
        PerformCalculation(initialDays);
        UpdateWindowLayout();
        _calcDaysInput.Focus();
        _calcDaysInput.SelectAll();
    }

    public void HideCalculationPanel()
    {
        _calcCard.Visibility = Visibility.Collapsed;
        UpdateWindowLayout();
    }

    private void PerformCalculation(int daysOffset)
    {
        var result = DateCalculatorHelper.CalculateDetailed(_calcBaseDate, daysOffset);

        string baseStr = _calcBaseDate.Date == DateTime.Today ? "今天" : $"{_calcBaseDate:M月d日}";
        _calcBaseDateLabel.Text = $"基准: {baseStr} ({_calcBaseDate:yyyy-MM-dd})";

        _calcResultDateText.Text = result.TargetDateString;
        _calcResultLunarText.Text = result.LunarString;

        if (!string.IsNullOrEmpty(result.FestivalString))
        {
            _calcResultFestText.Text = result.FestivalString;
            _calcResultFestText.Visibility = Visibility.Visible;
        }
        else
        {
            _calcResultFestText.Visibility = Visibility.Collapsed;
        }

        _calcResultSpanText.Text = result.SpanString;
    }

    public void ShowBanner(string message, Brush? fgBrush = null, Brush? bgBrush = null, int autoRestoreSeconds = 6)
    {
    }

    public void ShowInlineInput(string title, string defaultValue, Action<string> onConfirm)
    {
        _inlineTitle.Text = title;
        _inlineInput.Text = defaultValue;
        _inlineAction = onConfirm;
        _inlineCard.Visibility = Visibility.Visible;
        UpdateWindowLayout();
        _inlineInput.Focus();
        _inlineInput.SelectAll();
    }

    public void HideInlineInput()
    {
        _inlineCard.Visibility = Visibility.Collapsed;
        _inlineAction = null;
        UpdateWindowLayout();
    }

    private void ConfirmInlineInput()
    {
        string text = _inlineInput.Text?.Trim() ?? string.Empty;
        var action = _inlineAction;
        HideInlineInput();
        if (!string.IsNullOrEmpty(text))
        {
            action?.Invoke(text);
        }
    }

    public void ExecuteDateCalculation(DateTime baseDate, int days)
    {
        OpenCalculationPanel(baseDate, days);
    }

    public void SetQuickAlarm(DateTime baseDate, TimeSpan offset, string title)
    {
        DateTime triggerTime = DateTime.Now.Add(offset);
        CalendarReminderManager.Instance.AddAlarm(triggerTime, title);
        RenderMonthGrid();
        RefreshRightPanel(_selectedDate);
        RefreshCountdownPanel();
    }

    public void SetExactAlarm(DateTime date, int hour, int minute, string title)
    {
        DateTime triggerTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0);
        if (triggerTime <= DateTime.Now && date.Date == DateTime.Today)
        {
            triggerTime = triggerTime.AddDays(1);
        }
        CalendarReminderManager.Instance.AddAlarm(triggerTime, title);
        RenderMonthGrid();
        RefreshRightPanel(_selectedDate);
        RefreshCountdownPanel();
    }

    public void AddReminderForDate(DateTime date, string content)
    {
        CalendarReminderManager.Instance.AddReminder(date, content);
        RenderMonthGrid();
        RefreshRightPanel(_selectedDate);
        RefreshCountdownPanel();
    }

    public void ClearRemindersForDate(DateTime date)
    {
        CalendarReminderManager.Instance.ClearItemsForDate(date);
        RenderMonthGrid();
        RefreshRightPanel(_selectedDate);
        RefreshCountdownPanel();
    }

    private long _renderedDataVersion = -1;
    private void UpdateClock()
    {
        TaskbarCalendarService.Instance.RefreshPresentationState();
        UpdateNavHeader();
        if (_renderedDataVersion != CalendarReminderManager.Instance.DataVersion)
        {
            _renderedDataVersion = CalendarReminderManager.Instance.DataVersion;
            RenderMonthGrid(); RefreshRightPanel(_selectedDate); RefreshCountdownPanel();
        }

        // 检查待响铃闹钟
        CalendarReminderManager.Instance.CheckPendingAlarms(TaskbarCalendarService.Instance.ActionContext, triggered =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                RenderMonthGrid();
                RefreshRightPanel(_selectedDate);
                RefreshCountdownPanel();
            });
        });
    }

    public void RenderMonthGrid()
    {
        UpdateNavHeader();
        _calendarScrollPanel.Children.Clear();
        _dateCellElements.Clear();
        _dateCellHolders.Clear();
        _monthSectionElements.Clear();

        // 默认生成前后 1 年半范围（共 36 个月），支持超大跨度的无缝文章式垂直连续滚动
        int baseYear = _selectedDate.Year;
        int startYear = baseYear - 1;
        int endYear = baseYear + 1;

        for (int y = startYear; y <= endYear; y++)
        {
            for (int m = 1; m <= 12; m++)
            {
                var block = CreateMonthBlock(y, m);
                _calendarScrollPanel.Children.Add(block);
            }
        }

        _calendarScrollPanel.UpdateLayout();
        _calendarScroll.UpdateLayout();
        ScrollToDate(_selectedDate);
    }

    private FrameworkElement CreateMonthBlock(int year, int month)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        // 1. 月份分节标题条 (文章式分节，高雅醒目，左侧月年，右侧淡灰细线贯穿)
        var headerGrid = new Grid { Margin = new Thickness(6, 12, 6, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titleSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleSp.Children.Add(new TextBlock
        {
            Text = $"{month}月",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 6, 0)
        });
        titleSp.Children.Add(new TextBlock
        {
            Text = $"{year}年",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1)
        });
        headerGrid.Children.Add(titleSp);

        var dividerLine = new Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(dividerLine, 1);
        headerGrid.Children.Add(dividerLine);

        block.Children.Add(headerGrid);

        // 2. 当月 7 列网格 (严格对齐周一到周日)
        var daysGrid = new UniformGrid { Columns = 7 };
        var firstDay = new DateTime(year, month, 1);
        int daysInMonth = DateTime.DaysInMonth(year, month);

        int firstDOW = (int)firstDay.DayOfWeek;
        int leadingBlanks = (firstDOW == 0) ? 6 : firstDOW - 1;

        // 前置空白占位 (严格对齐周一到周日)
        for (int i = 0; i < leadingBlanks; i++)
        {
            daysGrid.Children.Add(new Border
            {
                Height = 46,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            });
        }

        // 当月所有有效日期单元格
        for (int d = 1; d <= daysInMonth; d++)
        {
            var curDate = new DateTime(year, month, d);
            var cell = CreateDateCell(curDate);
            daysGrid.Children.Add(cell);
            _dateCellElements[curDate.Date] = cell;
        }

        // 后置空白占位补齐
        int totalCells = leadingBlanks + daysInMonth;
        int trailingBlanks = (7 - (totalCells % 7)) % 7;
        for (int i = 0; i < trailingBlanks; i++)
        {
            daysGrid.Children.Add(new Border
            {
                Height = 46,
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            });
        }

        block.Children.Add(daysGrid);
        _monthSectionElements[(year, month)] = block;
        return block;
    }

    private Border CreateDateCell(DateTime curDate)
    {
        var detail = ChineseCalendarHelper.GetDetail(curDate);
        var cell = new Border
        {
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(2, 2, 2, 2),
            Height = 46,
            Cursor = Cursors.Hand,
            Tag = curDate.Date
        };

        var holder = new DateCellHolder
        {
            Date = curDate.Date,
            CellBorder = cell,
            Detail = detail
        };
        _dateCellHolders[curDate.Date] = holder;

        // 内部结构：角标 + 闹钟圆点 + 公历日期 + 农历副文本
        var cellGrid = new Grid();

        // 1. 左上角待办/闹钟发光小圆点（●）
        if (detail.HasAlarm || detail.HasReminder)
        {
            var dot = new Ellipse
            {
                Width = 5.5,
                Height = 5.5,
                Fill = detail.HasAlarm
                    ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // 醒目橙黄闹钟点
                    : new SolidColorBrush(Color.FromRgb(96, 165, 250)), // 科技天青待办点
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 4, 0, 0)
            };
            cellGrid.Children.Add(dot);
        }

        // 2. 右上角休/班微型角标
        if (detail.HasBadge)
        {
            var badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(3.5),
                Background = detail.BadgeBackground,
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 1, 0),
                Opacity = 1.0
            };
            var badgeTxt = new TextBlock
            {
                Text = detail.BadgeText,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                LineHeight = 11
            };
            badgeBorder.Child = badgeTxt;
            cellGrid.Children.Add(badgeBorder);
        }

        // 3. 主体日期公历数字与节日上下固定网格
        var dateContentGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        dateContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        dateContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });

        var dayText = new TextBlock
        {
            Text = curDate.Day.ToString(),
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(dayText, 0);
        dateContentGrid.Children.Add(dayText);
        holder.DayText = dayText;

        var subText = new TextBlock
        {
            Text = detail.CellDisplayText,
            FontSize = 11,
            FontWeight = detail.CellFontWeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = string.IsNullOrEmpty(detail.CellDisplayText) ? Visibility.Hidden : Visibility.Visible
        };
        Grid.SetRow(subText, 1);
        dateContentGrid.Children.Add(subText);
        holder.SubText = subText;

        cellGrid.Children.Add(dateContentGrid);
        cell.Child = cellGrid;

        // 应用初始视觉状态
        ApplyCellStyles(holder);

        // 悬浮反馈
        cell.MouseEnter += (s, e) =>
        {
            if (!IsCellSelectedOrRange(curDate))
            {
                cell.Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            }
        };
        cell.MouseLeave += (s, e) =>
        {
            if (!IsCellSelectedOrRange(curDate))
            {
                ApplyCellStyles(holder);
            }
        };

        var cellDateCaptured = curDate;
        cell.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            _highlightDate = null;
            if (e.ClickCount == 2)
            {
                HandleDateCellDoubleClick(cellDateCaptured);
            }
            else if (e.ClickCount == 1)
            {
                HandleDateCellClick(cellDateCaptured);
            }
        };

        // ToolTip 悬停卡片
        var tipStack = new StackPanel { Margin = new Thickness(4) };
        tipStack.Children.Add(new TextBlock
        {
            Text = curDate.ToString("yyyy年M月d日 dddd"),
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = Brushes.White
        });
        tipStack.Children.Add(new TextBlock
        {
            Text = $"农历 {detail.GanZhiYear} {detail.LunarMonthName}{detail.LunarDayName}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        if (!string.IsNullOrEmpty(detail.ToolTipFestivalSummary))
        {
            tipStack.Children.Add(new TextBlock
            {
                Text = detail.ToolTipFestivalSummary,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                Margin = new Thickness(0, 3, 0, 0)
            });
        }
        if (detail.AlarmList.Count > 0)
        {
            foreach (var al in detail.AlarmList)
            {
                tipStack.Children.Add(new TextBlock
                {
                    Text = $"[闹钟] {al}",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
        }
        if (detail.ReminderList.Count > 0)
        {
            foreach (var rem in detail.ReminderList)
            {
                tipStack.Children.Add(new TextBlock
                {
                    Text = $"[待办] {rem}",
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
        }

        cell.ToolTip = new ToolTip
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 30, 30, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9),
            Content = tipStack
        };

        // 右键上下文菜单
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromArgb(248, 28, 28, 32)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HasDropShadow = true,
            MinWidth = 185
        };
        menu.Opened += (_, _) => IsMenuOpen = true;
        menu.Closed += (_, _) => IsMenuOpen = false;

        var titlePanel = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"{cellDateCaptured:M月d日 dddd}",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250))
        });
        string subInfo = $"{detail.LunarMonthName}{detail.LunarDayName}";
        if (!string.IsNullOrEmpty(detail.TraditionalFestival)) subInfo += $" · {detail.TraditionalFestival}";
        else if (!string.IsNullOrEmpty(detail.SolarFestival)) subInfo += $" · {detail.SolarFestival}";
        else if (detail.IsHoliday) subInfo += " (休)";
        else if (detail.IsWorkday) subInfo += " (班)";

        titlePanel.Children.Add(new TextBlock
        {
            Text = subInfo,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            Margin = new Thickness(0, 2, 0, 0)
        });

        menu.Items.Add(new MenuItem
        {
            Header = titlePanel,
            IsEnabled = false,
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 2, 6, 2)
        });
        menu.Items.Add(new Separator { Margin = new Thickness(2, 2, 2, 2) });

        MenuItem CreateCalcMenuItem(string text, Action onClick, Brush fg)
        {
            var mi = new MenuItem
            {
                Header = text,
                Foreground = fg,
                Background = Brushes.Transparent,
                FontSize = 12,
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                Cursor = Cursors.Hand
            };
            mi.Click += (_, _) => { menu.IsOpen = false; onClick(); };
            return mi;
        }

        var greenBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        menu.Items.Add(CreateCalcMenuItem("推算 3 天后", () => OpenCalculationPanel(cellDateCaptured, 3), greenBrush));
        menu.Items.Add(CreateCalcMenuItem("推算 5 天后", () => OpenCalculationPanel(cellDateCaptured, 5), greenBrush));
        menu.Items.Add(CreateCalcMenuItem("推算 7 天后 (1周)", () => OpenCalculationPanel(cellDateCaptured, 7), greenBrush));
        menu.Items.Add(CreateCalcMenuItem("推算 30 天后 (1月)", () => OpenCalculationPanel(cellDateCaptured, 30), greenBrush));
        menu.Items.Add(CreateCalcMenuItem("自定义推算天数...", () => OpenCalculationPanel(cellDateCaptured), greenBrush));

        if (_rangeStartDate.HasValue)
        {
            menu.Items.Add(new Separator { Margin = new Thickness(2, 2, 2, 2) });
            menu.Items.Add(CreateCalcMenuItem("清除日期选区", () => ClearRangeSelection(), new SolidColorBrush(Color.FromRgb(248, 113, 113))));
        }

        cell.ContextMenu = menu;
        return cell;
    }

    private bool IsCellSelectedOrRange(DateTime curDate)
    {
        bool isToday = (curDate.Date == DateTime.Today);
        bool isSelected = (curDate.Date == _selectedDate.Date);
        bool isRangeStart = (_rangeStartDate.HasValue && curDate.Date == _rangeStartDate.Value.Date);
        bool isRangeEnd = (_rangeEndDate.HasValue && curDate.Date == _rangeEndDate.Value.Date);
        bool isInRange = false;
        if (_rangeStartDate.HasValue && _rangeEndDate.HasValue)
        {
            var minDate = _rangeStartDate.Value < _rangeEndDate.Value ? _rangeStartDate.Value.Date : _rangeEndDate.Value.Date;
            var maxDate = _rangeStartDate.Value < _rangeEndDate.Value ? _rangeEndDate.Value.Date : _rangeStartDate.Value.Date;
            isInRange = (curDate.Date > minDate && curDate.Date < maxDate);
        }
        bool isHigh = (_highlightDate.HasValue && curDate.Date == _highlightDate.Value.Date);
        return isToday || isSelected || isRangeStart || isRangeEnd || isInRange || isHigh;
    }

    private void ApplyCellStyles(DateCellHolder holder)
    {
        var curDate = holder.Date;
        var cell = holder.CellBorder;
        var detail = holder.Detail;

        bool isToday = (curDate.Date == DateTime.Today);
        bool isSelected = (curDate.Date == _selectedDate.Date);
        bool isRangeStart = (_rangeStartDate.HasValue && curDate.Date == _rangeStartDate.Value.Date);
        bool isRangeEnd = (_rangeEndDate.HasValue && curDate.Date == _rangeEndDate.Value.Date);
        bool isInRange = false;
        if (_rangeStartDate.HasValue && _rangeEndDate.HasValue)
        {
            var minDate = _rangeStartDate.Value < _rangeEndDate.Value ? _rangeStartDate.Value.Date : _rangeEndDate.Value.Date;
            var maxDate = _rangeStartDate.Value < _rangeEndDate.Value ? _rangeEndDate.Value.Date : _rangeStartDate.Value.Date;
            isInRange = (curDate.Date > minDate && curDate.Date < maxDate);
        }
        bool isHighlighted = (_highlightDate.HasValue && curDate.Date == _highlightDate.Value.Date);
        bool isWeekend = (curDate.DayOfWeek == DayOfWeek.Saturday || curDate.DayOfWeek == DayOfWeek.Sunday);

        Brush cellBg;
        Brush cellBorder;
        double borderWidth;
        Brush dayForeground;
        Brush subForeground;

        if (isRangeStart || isRangeEnd)
        {
            cellBg = new SolidColorBrush(Color.FromArgb(180, 37, 99, 235));
            cellBorder = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            borderWidth = 1.5;
            dayForeground = Brushes.White;
            subForeground = new SolidColorBrush(Color.FromRgb(219, 234, 254));
        }
        else if (isToday)
        {
            cellBg = new SolidColorBrush(Color.FromRgb(10, 138, 92));
            cellBorder = isSelected ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.Transparent;
            borderWidth = isSelected ? 1.5 : 0;
            dayForeground = Brushes.White;
            subForeground = Brushes.White;
        }
        else if (isInRange)
        {
            cellBg = new SolidColorBrush(Color.FromArgb(70, 37, 99, 235));
            cellBorder = new SolidColorBrush(Color.FromArgb(120, 96, 165, 250));
            borderWidth = 1.0;
            dayForeground = new SolidColorBrush(Color.FromRgb(224, 231, 255));
            subForeground = new SolidColorBrush(Color.FromRgb(191, 219, 254));
        }
        else if (isHighlighted)
        {
            cellBg = new SolidColorBrush(Color.FromArgb(80, 16, 185, 129));
            cellBorder = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            borderWidth = 1.5;
            dayForeground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            subForeground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        }
        else if (isSelected)
        {
            cellBg = new SolidColorBrush(Color.FromArgb(40, 10, 138, 92));
            cellBorder = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            borderWidth = 1.5;
            dayForeground = isWeekend ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.White;
            subForeground = new SolidColorBrush(Color.FromRgb(161, 161, 170));
        }
        else
        {
            cellBg = Brushes.Transparent;
            cellBorder = Brushes.Transparent;
            borderWidth = 0;
            dayForeground = isWeekend
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(244, 244, 245));
            subForeground = new SolidColorBrush(Color.FromRgb(161, 161, 170));
        }

        cell.Background = cellBg;
        cell.BorderBrush = cellBorder;
        cell.BorderThickness = new Thickness(borderWidth);
        if (holder.DayText != null)
        {
            holder.DayText.Foreground = dayForeground;
            holder.DayText.FontWeight = (isToday || isHighlighted || isSelected) ? FontWeights.Bold : FontWeights.SemiBold;
        }
        if (holder.SubText != null)
        {
            holder.SubText.Foreground = subForeground;
        }
    }

    private void UpdateCellVisual(DateTime date)
    {
        if (_dateCellHolders.TryGetValue(date.Date, out var holder))
        {
            ApplyCellStyles(holder);
        }
    }

    private void UpdateAllCellVisuals()
    {
        foreach (var holder in _dateCellHolders.Values)
        {
            ApplyCellStyles(holder);
        }
    }

    public void ScrollToDate(DateTime date, bool smooth = false)
    {
        if (_dateCellElements.TryGetValue(date.Date, out var cell))
        {
            try
            {
                var pt = cell.TransformToAncestor(_calendarScrollPanel).Transform(new Point(0, 0));
                double target = Math.Max(0, pt.Y - 120);
                _isProgrammaticScrolling = true;
                _calendarScroll.ScrollToVerticalOffset(target);
                _isProgrammaticScrolling = false;
            }
            catch { }
        }
    }

    public void ScrollToMonth(int year, int month)
    {
        if (_monthSectionElements.TryGetValue((year, month), out var elem))
        {
            try
            {
                var pt = elem.TransformToAncestor(_calendarScrollPanel).Transform(new Point(0, 0));
                _isProgrammaticScrolling = true;
                _calendarScroll.ScrollToVerticalOffset(Math.Max(0, pt.Y - 6));
                _isProgrammaticScrolling = false;
            }
            catch { }
        }
    }

    private void CalendarScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScrolling) return;
        try
        {
            double curOffset = _calendarScroll.VerticalOffset + 50;
            foreach (var kvp in _monthSectionElements)
            {
                var elem = kvp.Value;
                var pt = elem.TransformToAncestor(_calendarScrollPanel).Transform(new Point(0, 0));
                double h = elem.ActualHeight > 0 ? elem.ActualHeight : 260;
                if (curOffset >= pt.Y && curOffset < pt.Y + h)
                {
                    var (y, m) = kvp.Key;
                    if (_displayMonth.Year != y || _displayMonth.Month != m)
                    {
                        _displayMonth = new DateTime(y, m, 1);
                    }
                    break;
                }
            }
        }
        catch { }
    }

    public void ShowFlyout()
    {
        Reposition();
        Show();
        Activate();
        if (WindowHandle != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(WindowHandle);
        }
        TaskbarCalendarService.Instance.EnsureMouseHookForFlyout();
        ScrollToDate(_selectedDate);
    }

    public void HideFlyout()
    {
        Hide();
        _inlineCard.Visibility = Visibility.Collapsed;
        _calcCard.Visibility = Visibility.Collapsed;
        UpdateWindowLayout();
        TaskbarCalendarService.Instance.ReleaseMouseHookIfHoverMode();
    }

    private void Reposition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
    }

}

#region Win32 原生互操作类型定义

public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

public enum USER_NOTIFICATION_STATE
{
    QUNS_NOT_PRESENT = 1,
    QUNS_BUSY = 2,
    QUNS_RUNNING_D3D_FULL_SCREEN = 3,
    QUNS_PRESENTATION_MODE = 4,
    QUNS_ACCEPTS_NOTIFICATIONS = 5,
    QUNS_QUIET_TIME = 6
}

public static class NativeMethods
{
    public delegate bool EnumWindowProc(IntPtr hwnd, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowProc callback, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hook);

    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const uint GA_ROOT = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);


    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out USER_NOTIFICATION_STATE pquns);
}

#endregion
