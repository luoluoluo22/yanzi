using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        string input = context.InputText?.Trim() ?? string.Empty;

        // 1. 检查是否已有常驻运行的服务实例
        var existingService = GetExistingRunningService(context);
        if (existingService != null)
        {
            try
            {
                var showMethod = existingService.GetType().GetMethod("Show") ??
                                 existingService.GetType().GetMethod("Toggle");
                if (showMethod != null)
                {
                    showMethod.Invoke(existingService, null);
                    return "已呼出镜子数据窗口。";
                }
            }
            catch (Exception ex)
            {
                context.Log($"唤起已存在服务窗口失败: {ex.Message}");
            }
            return "镜子服务已在后台运行中。";
        }

        // 2. 首次启动常驻服务
        var service = MirrorService.Instance;
        service.Initialize(context);

        // 注册到宿主 HostObjectRegistry，供宿主感知与二次呼出
        try
        {
            context.RegisterObject?.Invoke("mirror-service", service);
            context.RegisterObject?.Invoke($"{context.ExtensionId}-window", service);
        }
        catch { }

        // 3. 区分触发场景：开机自启（静默常驻，不弹窗） vs 用户主动点击（唤起数据查看窗口）
        bool isStartup = string.Equals(context.LaunchSource, "app-startup", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(context.LaunchSource, "startup", StringComparison.OrdinalIgnoreCase);

        if (isStartup)
        {
            context.Log("【镜子】随应用自启动完成，已在后台静默建立 Win32 事件驱动监听。");
        }
        else
        {
            service.Show();
        }

        // 4. 异步常驻挂起，保持后台运行
        await service.WaitForStopAsync();

        // 5. 退出时清理全局对象
        try
        {
            context.RegisterObject?.Invoke("mirror-service", null!);
            context.RegisterObject?.Invoke($"{context.ExtensionId}-window", null!);
        }
        catch { }

        return "镜子服务已停止。";
    }

    private static object? GetExistingRunningService(YanziActionContext context)
    {
        try
        {
            var obj = context.GetRegisteredObject?.Invoke("mirror-service");
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
}

/// <summary>
/// 镜子服务：纯事件驱动的前台窗口注意力轨迹监听与管理
/// </summary>
public sealed class MirrorService
{
    private static readonly Lazy<MirrorService> _lazy = new(() => new MirrorService());
    public static MirrorService Instance => _lazy.Value;

    public bool IsRunning { get; private set; }
    public int ProcessId => Process.GetCurrentProcess().Id;

    private YanziActionContext? _context;
    private readonly TaskCompletionSource<bool> _stopTcs = new();

    // Win32 事件钩子相关：必须在 UI 线程（有消息循环）上安装，并保持委托强引用防止 GC
    private IntPtr _hookHandle = IntPtr.Zero;
    private WinEventDelegate? _winEventDelegate;

    // 状态记录
    private readonly object _lock = new();
    private IntPtr _lastHwnd = IntPtr.Zero;
    private uint _lastPid = 0;
    private string _lastTitle = string.Empty;
    private string _lastProcessName = string.Empty;
    private DateTime _lastEnterTime = DateTime.MinValue;
    private FocusRecord? _currentActiveRecord;

    // 历史数据与存储
    private readonly List<FocusRecord> _records = new();
    private string _storageFilePath = string.Empty;
    private MirrorWindow? _activeWindow;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private MirrorService() { }

    public void Initialize(YanziActionContext context)
    {
        if (IsRunning) return;
        _context = context;

        // 1. 确定存储路径并加载历史数据
        try
        {
            string dataDir = context.ExtensionDataDirectory;
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _storageFilePath = System.IO.Path.Combine(dataDir, "mirror_records.json");
            LoadRecordsFromDisk();
        }
        catch (Exception ex)
        {
            _context.Log($"初始化存储路径失败: {ex.Message}");
        }

        // 2. 关键：调度到 WPF 主 Dispatcher 线程安装钩子，确保其依托 UI 消息泵正常派发事件
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                _winEventDelegate = new WinEventDelegate(OnForegroundChangedEvent);
                _hookHandle = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _winEventDelegate,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT);

                if (_hookHandle != IntPtr.Zero)
                {
                    IsRunning = true;
                    _context?.Log("【镜子】Win32 EVENT_SYSTEM_FOREGROUND 事件钩子已依托主消息泵就绪（0 轮询）。");

                    // 初始记录当前前台窗口
                    RecordCurrentForegroundWindow();
                }
                else
                {
                    _context?.Log("【镜子】SetWinEventHook 安装失败，错误码: " + Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                _context?.Log($"安装事件钩子异常: {ex.Message}");
            }
        });
    }

    public Task WaitForStopAsync() => _stopTcs.Task;

    public void Quit()
    {
        if (!IsRunning) return;
        IsRunning = false;

        // 结算当前窗口
        SettleCurrentRecord();
        SaveRecordsToDisk();

        if (_hookHandle != IntPtr.Zero)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            });
        }

        CloseWindow();
        _stopTcs.TrySetResult(true);
    }

    public void Show()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                if (_activeWindow != null && _activeWindow.IsLoaded)
                {
                    if (_activeWindow.WindowState == WindowState.Minimized)
                    {
                        _activeWindow.WindowState = WindowState.Normal;
                    }
                    _activeWindow.Activate();
                    _activeWindow.Topmost = true;
                    _activeWindow.Topmost = false;
                    _activeWindow.Focus();
                    _activeWindow.RefreshData();
                    return;
                }

                _activeWindow = new MirrorWindow(this);
                _activeWindow.Closed += (s, e) => { _activeWindow = null; };
                _activeWindow.Show();
                _activeWindow.Activate();
            }
            catch (Exception ex)
            {
                _context?.Log($"唤起镜子窗口异常: {ex.Message}");
            }
        });
    }

    public void Toggle()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_activeWindow != null && _activeWindow.IsLoaded && _activeWindow.IsVisible)
            {
                _activeWindow.Close();
            }
            else
            {
                Show();
            }
        });
    }

    private void CloseWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                _activeWindow?.Close();
                _activeWindow = null;
            }
            catch { }
        });
    }

    private void OnForegroundChangedEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;

        // 异步派发，绝不阻塞 UI 消息泵
        Task.Run(() =>
        {
            ProcessWindowTransition(hwnd);
        });
    }

    private void RecordCurrentForegroundWindow()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            Task.Run(() => ProcessWindowTransition(hwnd));
        }
    }

    private void ProcessWindowTransition(IntPtr hwnd)
    {
        try
        {
            var sb = new StringBuilder(512);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString().Trim();

            // 过滤无标题窗口或镜子自身的展示窗口
            if (string.IsNullOrWhiteSpace(title) || title.StartsWith("镜子 · 注意力足迹") || title.StartsWith("镜子 - 注意力足迹"))
            {
                return;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string processName = "Unknown";
            try
            {
                using var p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;
            }
            catch { }

            // 过滤桌面底板
            if (processName.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("WorkerW", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DateTime now = DateTime.Now;

            lock (_lock)
            {
                // 去重同一焦点
                if (pid == _lastPid && string.Equals(title, _lastTitle, StringComparison.Ordinal))
                {
                    return;
                }

                // 结算上一窗口
                SettleCurrentRecord();

                // 开启新窗口追踪
                _lastHwnd = hwnd;
                _lastPid = pid;
                _lastTitle = title;
                _lastProcessName = processName;
                _lastEnterTime = now;

                _currentActiveRecord = new FocusRecord
                {
                    Time = now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Process = processName,
                    Title = TruncateString(title, 100),
                    DurationSeconds = 0
                };
            }

            // 界面正在显示时，轻量刷新通知
            if (_activeWindow != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _activeWindow?.RefreshData();
                });
            }
        }
        catch (Exception ex)
        {
            _context?.Log($"处理窗口转换异常: {ex.Message}");
        }
    }

    private void SettleCurrentRecord()
    {
        lock (_lock)
        {
            if (_currentActiveRecord != null && _lastEnterTime != DateTime.MinValue)
            {
                int duration = (int)(DateTime.Now - _lastEnterTime).TotalSeconds;
                if (duration >= 1)
                {
                    _currentActiveRecord.DurationSeconds = duration;
                    _records.Add(_currentActiveRecord);

                    if (_records.Count > 500)
                    {
                        _records.RemoveRange(0, _records.Count - 500);
                    }

                    SaveRecordsToDiskAsync();
                }
                _currentActiveRecord = null;
            }
        }
    }

    public IReadOnlyList<FocusRecord> GetRecentRecords()
    {
        lock (_lock)
        {
            var list = new List<FocusRecord>(_records);
            if (_currentActiveRecord != null && _lastEnterTime != DateTime.MinValue)
            {
                int currentDuration = (int)(DateTime.Now - _lastEnterTime).TotalSeconds;
                var currentCopy = new FocusRecord
                {
                    Time = _currentActiveRecord.Time,
                    Process = _currentActiveRecord.Process,
                    Title = _currentActiveRecord.Title,
                    DurationSeconds = currentDuration
                };
                list.Add(currentCopy);
            }
            return list;
        }
    }

    public (string Process, string Title, int DurationSeconds) GetCurrentActiveInfo()
    {
        lock (_lock)
        {
            if (_currentActiveRecord != null && _lastEnterTime != DateTime.MinValue)
            {
                int currentDuration = (int)(DateTime.Now - _lastEnterTime).TotalSeconds;
                return (_currentActiveRecord.Process, _currentActiveRecord.Title, currentDuration);
            }
            return (string.Empty, string.Empty, 0);
        }
    }

    public void ClearRecords()
    {
        lock (_lock)
        {
            _records.Clear();
            SaveRecordsToDisk();
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            _activeWindow?.RefreshData();
        });
    }

    private void LoadRecordsFromDisk()
    {
        try
        {
            if (File.Exists(_storageFilePath))
            {
                string json = File.ReadAllText(_storageFilePath, Encoding.UTF8);
                var items = JsonSerializer.Deserialize<List<FocusRecord>>(json);
                if (items != null)
                {
                    lock (_lock)
                    {
                        _records.Clear();
                        _records.AddRange(items);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _context?.Log($"读取历史记录失败: {ex.Message}");
        }
    }

    private void SaveRecordsToDisk()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_storageFilePath)) return;
            List<FocusRecord> snapshot;
            lock (_lock)
            {
                snapshot = new List<FocusRecord>(_records);
            }
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(_storageFilePath, json, Encoding.UTF8);
        }
        catch { }
    }

    private void SaveRecordsToDiskAsync()
    {
        Task.Run(() => SaveRecordsToDisk());
    }

    private static string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length <= maxLength ? input : input.Substring(0, maxLength) + "...";
    }
}

public class FocusRecord
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [JsonPropertyName("process")]
    public string Process { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public int DurationSeconds { get; set; }
}

/// <summary>
/// 镜子数据展示窗口（极简现代暗黑设计，支持查看注意力足迹）
/// </summary>
public sealed class MirrorWindow : Window
{
    private readonly MirrorService _service;
    private readonly StackPanel _recordsPanel;
    private readonly TextBlock _statTotalCount;
    private readonly TextBlock _statActiveFocus;
    private readonly TextBlock _statActiveDuration;

    public MirrorWindow(MirrorService service)
    {
        _service = service;

        Title = "镜子 · 注意力足迹";
        Width = 720;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

        var rootBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)), // #18181B
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)), // #27272A
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true
        };
        rootBorder.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Stat Bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Records List
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // 1. 顶部 Header
        var header = new Grid { Margin = new Thickness(20, 16, 20, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
        var iconText = new TextBlock
        {
            Text = "🪞",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var titleGroup = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var mainTitle = new TextBlock
        {
            Text = "镜子 · 注意力足迹",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245))
        };
        var subTitle = new TextBlock
        {
            Text = "系统级 Win32 事件驱动 · 0 轮询 · 客观行为镜像",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        titleGroup.Children.Add(mainTitle);
        titleGroup.Children.Add(subTitle);
        titleStack.Children.Add(iconText);
        titleStack.Children.Add(titleGroup);
        Grid.SetColumn(titleStack, 0);

        var actionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var statusDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusLabel = new TextBlock
        {
            Text = "监听中",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var clearBtn = CreateActionButton("清空", () =>
        {
            if (MessageBox.Show("确定要清空当前的注意力轨迹记录吗？", "清空确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _service.ClearRecords();
            }
        });

        var refreshBtn = CreateActionButton("刷新", () => RefreshData());

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0)
        };
        closeBtn.Click += (s, e) => Close();

        actionStack.Children.Add(statusDot);
        actionStack.Children.Add(statusLabel);
        actionStack.Children.Add(refreshBtn);
        actionStack.Children.Add(clearBtn);
        actionStack.Children.Add(closeBtn);
        Grid.SetColumn(actionStack, 1);

        header.Children.Add(titleStack);
        header.Children.Add(actionStack);
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // 2. 统计卡片栏
        var statGrid = new Grid { Margin = new Thickness(20, 0, 20, 12) };
        statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _statTotalCount = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(6, 182, 212)) };
        _statActiveFocus = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)), TextTrimming = TextTrimming.CharacterEllipsis };
        _statActiveDuration = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(168, 85, 247)) };

        statGrid.Children.Add(CreateStatCard(0, "累计记录", _statTotalCount));
        statGrid.Children.Add(CreateStatCard(1, "当前活跃应用", _statActiveFocus));
        statGrid.Children.Add(CreateStatCard(2, "当前驻留时长", _statActiveDuration));

        Grid.SetRow(statGrid, 1);
        mainGrid.Children.Add(statGrid);

        // 3. 记录流列表
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(20, 0, 20, 0)
        };
        _recordsPanel = new StackPanel();
        scrollViewer.Content = _recordsPanel;
        Grid.SetRow(scrollViewer, 2);
        mainGrid.Children.Add(scrollViewer);

        // 4. 底部 Footer
        var footer = new Border
        {
            Padding = new Thickness(20, 10, 20, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 20))
        };
        var footerText = new TextBlock
        {
            Text = "💡 数据全量保存在本地，无任何上传 · 按 ESC 键快速关闭窗口 · 后台继续保持静默监听",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        footer.Child = footerText;
        Grid.SetRow(footer, 3);
        mainGrid.Children.Add(footer);

        rootBorder.Child = mainGrid;
        Content = rootBorder;

        RefreshData();
    }

    private Border CreateStatCard(int colIndex, string label, TextBlock valueControl)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 31, 35)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(colIndex == 0 ? 0 : 4, 0, colIndex == 2 ? 0 : 4, 0)
        };
        var stack = new StackPanel();
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            Margin = new Thickness(0, 0, 0, 2)
        };
        stack.Children.Add(labelText);
        stack.Children.Add(valueControl);
        card.Child = stack;
        Grid.SetColumn(card, colIndex);
        return card;
    }

    private Button CreateActionButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(4, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 12
        };
        btn.Click += (s, e) => onClick();
        return btn;
    }

    public void RefreshData()
    {
        var records = _service.GetRecentRecords();
        var (curProc, curTitle, curDuration) = _service.GetCurrentActiveInfo();

        _statTotalCount.Text = $"{records.Count} 次切换";
        _statActiveFocus.Text = string.IsNullOrEmpty(curProc) ? "等待切换" : $"{curProc} - {curTitle}";
        _statActiveFocus.ToolTip = curTitle;
        _statActiveDuration.Text = FormatDuration(curDuration);

        _recordsPanel.Children.Clear();

        if (records.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "暂无窗口切换记录。去切换使用其他软件，数据会自动在此呈现实时足迹。",
                Foreground = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            _recordsPanel.Children.Add(emptyText);
            return;
        }

        for (int i = records.Count - 1; i >= 0; i--)
        {
            var rec = records[i];
            _recordsPanel.Children.Add(CreateRecordRow(rec, i == records.Count - 1));
        }
    }

    private UIElement CreateRecordRow(FocusRecord rec, bool isCurrent)
    {
        var rowBorder = new Border
        {
            Background = isCurrent
                ? new SolidColorBrush(Color.FromArgb(30, 6, 182, 212))
                : new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            BorderBrush = isCurrent
                ? new SolidColorBrush(Color.FromArgb(80, 6, 182, 212))
                : new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string shortTime = rec.Time.Length >= 11 ? rec.Time.Substring(11) : rec.Time;
        var timeBlock = new TextBlock
        {
            Text = shortTime,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(timeBlock, 0);

        var pill = new Border
        {
            Background = GetColorForProcess(rec.Process),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var pillText = new TextBlock
        {
            Text = rec.Process,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White
        };
        pill.Child = pillText;
        Grid.SetColumn(pill, 1);

        var titleBlock = new TextBlock
        {
            Text = rec.Title,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = rec.Title
        };
        Grid.SetColumn(titleBlock, 2);

        var durationBlock = new TextBlock
        {
            Text = isCurrent ? $"{FormatDuration(rec.DurationSeconds)} (进行中)" : FormatDuration(rec.DurationSeconds),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = isCurrent
                ? new SolidColorBrush(Color.FromRgb(6, 182, 212))
                : new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(durationBlock, 3);

        grid.Children.Add(timeBlock);
        grid.Children.Add(pill);
        grid.Children.Add(titleBlock);
        grid.Children.Add(durationBlock);

        rowBorder.Child = grid;
        return rowBorder;
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "< 1秒";
        if (seconds < 60) return $"{seconds}秒";
        int minutes = seconds / 60;
        int remSeconds = seconds % 60;
        if (minutes < 60)
        {
            return remSeconds > 0 ? $"{minutes}分{remSeconds}秒" : $"{minutes}分钟";
        }
        int hours = minutes / 60;
        int remMinutes = minutes % 60;
        return $"{hours}时{remMinutes}分";
    }

    private static SolidColorBrush GetColorForProcess(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return new SolidColorBrush(Color.FromRgb(82, 82, 91));

        int hash = Math.Abs(processName.GetHashCode());
        Color[] colors =
        {
            Color.FromRgb(59, 130, 246),
            Color.FromRgb(16, 185, 129),
            Color.FromRgb(139, 92, 246),
            Color.FromRgb(245, 158, 11),
            Color.FromRgb(236, 72, 153),
            Color.FromRgb(6, 182, 212),
            Color.FromRgb(249, 115, 22),
            Color.FromRgb(99, 102, 241)
        };
        return new SolidColorBrush(colors[hash % colors.Length]);
    }
}
