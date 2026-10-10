using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var host = new IdleTaskServiceHost(context);
        context.RegisterObject?.Invoke($"{context.ExtensionId}-window", host);
        context.Capabilities.Register(
            "idle.tasks.list",
            payload => Task.FromResult<object?>(IdleTaskCapability.List(context.ExtensionDataDirectory, payload)));

        if (string.Equals(context.LaunchSource, "idle", StringComparison.OrdinalIgnoreCase))
        {
            _ = host.HandleTrigger("idle");
        }
        else if (!string.Equals(context.LaunchSource, "app-startup", StringComparison.OrdinalIgnoreCase))
        {
            host.Show();
        }

        if (string.Equals(context.InputText?.Trim(), "--run-next", StringComparison.OrdinalIgnoreCase))
        {
            _ = host.Run("--run-next");
        }

        return await host.Lifetime.ConfigureAwait(false);
    }
}

public sealed class IdleTaskServiceHost
{
    private readonly IdleTaskRuntime _runtime;
    private readonly TaskCompletionSource<string> _lifetime =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IdleTaskWindow? _window;
    private int _quitting;

    public IdleTaskServiceHost(YanziActionContext context)
    {
        Directory.CreateDirectory(context.ExtensionDataDirectory);
        _runtime = new IdleTaskRuntime(
            context.ExtensionDataDirectory,
            context.ExtensionId,
            string.IsNullOrWhiteSpace(context.AgentApiBaseUrl) ? "http://127.0.0.1:53919" : context.AgentApiBaseUrl.TrimEnd('/'),
            context.AgentApiToken ?? string.Empty);
    }

    public Task<string> Lifetime => _lifetime.Task;
    public IdleTaskRuntime Runtime => _runtime;

    public void Show() => Activate();

    public void Activate()
    {
        if (Volatile.Read(ref _quitting) != 0) return;
        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_window != null && _window.IsLoaded)
            {
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;
                _window.Show();
                _window.Activate();
                return;
            }

            _window = new IdleTaskWindow(this);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _window.Activate();
        }));
    }

    public async Task HandleTrigger(string launchSource)
    {
        if (Volatile.Read(ref _quitting) != 0) return;
        if (string.Equals(launchSource, "idle", StringComparison.OrdinalIgnoreCase)
            || string.Equals(launchSource, "scheduler", StringComparison.OrdinalIgnoreCase))
        {
            await ProcessNextAsync().ConfigureAwait(false);
        }
    }

    public async Task Run(string input)
    {
        if (string.Equals(input?.Trim(), "--run-next", StringComparison.OrdinalIgnoreCase))
            await ProcessNextAsync().ConfigureAwait(false);
        else
            Activate();
    }

    public Task<string> ProcessNextAsync() => IdleTaskWorker.ProcessNextAsync(_runtime);

    public void Quit()
    {
        if (Interlocked.Exchange(ref _quitting, 1) != 0) return;
        _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { _window?.Close(); } catch { }
            _window = null;
        }));
        _lifetime.TrySetResult("闲置任务后台服务已停止");
    }
}

public sealed record IdleTaskRuntime(
    string DataDirectory,
    string ExtensionId,
    string AgentApiBaseUrl,
    string AgentApiToken);

public sealed class IdleTaskWindow : Window
{
    private readonly IdleTaskServiceHost _host;
    private readonly string _dataDirectory;
    private readonly StackPanel _taskList = new();
    private readonly StackPanel _statusFilters = new();
    private readonly StackPanel _typeFilters = new();
    private readonly TextBlock _summary = new();
    private readonly TextBlock _bridgeStatus = new();
    private readonly TextBlock _idleCountdown = new();
    private readonly TextBox _searchBox = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;

    private readonly TextBox _titleBox = new();
    private readonly ComboBox _kindBox = new();
    private readonly TextBox _promptBox = new();
    private readonly TextBox _extensionIdBox = new();
    private readonly TextBox _extensionInputBox = new();
    private readonly TextBox _tagsBox = new();
    private readonly CheckBox _repeatBox = new();
    private readonly CheckBox _enabledBox = new();
    private readonly StackPanel _chatGptFields = new();
    private readonly StackPanel _extensionFields = new();
    private readonly TextBlock _editorTitle = new();

    private IdleTriggerRuntimeSnapshot? _lastIdleStatus;
    private string? _selectedTaskId;
    private string _statusFilter = "all";
    private string _typeFilter = "all";
    private bool _refreshing;
    private bool _editorLoading;

    private static readonly Brush BackgroundBrush = BrushFrom("#0F1318");
    private static readonly Brush PanelBrush = BrushFrom("#171C22");
    private static readonly Brush CardBrush = BrushFrom("#1C2229");
    private static readonly Brush CardSelectedBrush = BrushFrom("#202B36");
    private static readonly Brush BorderLineBrush = BrushFrom("#2C343D");
    private static readonly Brush MainTextBrush = BrushFrom("#F2F5F8");
    private static readonly Brush MutedTextBrush = BrushFrom("#93A0AD");
    private static readonly Brush AccentBrush = BrushFrom("#22D3EE");
    private static readonly Brush BlueBrush = BrushFrom("#3B82F6");
    private static readonly Brush GreenBrush = BrushFrom("#34D399");
    private static readonly Brush AmberBrush = BrushFrom("#FBBF24");
    private static readonly Brush RedBrush = BrushFrom("#F87171");

    public IdleTaskWindow(IdleTaskServiceHost host)
    {
        _host = host;
        _dataDirectory = host.Runtime.DataDirectory;
        Title = "闲置任务";
        Width = 1180;
        Height = 760;
        MinWidth = 920;
        MinHeight = 620;
        Background = BackgroundBrush;
        Foreground = MainTextBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildUi();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => UpdateIdleCountdownText();

        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            _countdownTimer.Start();
            await RefreshAsync();
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _countdownTimer.Stop();
        };
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader();
        root.Children.Add(header);

        var body = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        Grid.SetRow(body, 1);

        var sidebar = BuildSidebar();
        body.Children.Add(sidebar);

        var board = BuildBoard();
        Grid.SetColumn(board, 1);
        body.Children.Add(board);

        var editor = BuildEditor();
        Grid.SetColumn(editor, 2);
        body.Children.Add(editor);

        root.Children.Add(body);
        return root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = "闲置任务",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = MainTextBrush
        });
        left.Children.Add(new TextBlock
        {
            Text = "电脑闲置时自动处理队列。支持 ChatGPT 与小程序任务，任务可重复执行。",
            FontSize = 12.5,
            Foreground = MutedTextBrush,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _idleCountdown.FontSize = 13;
        _idleCountdown.FontWeight = FontWeights.SemiBold;
        _idleCountdown.Foreground = AccentBrush;
        _idleCountdown.Margin = new Thickness(0, 8, 0, 0);
        _idleCountdown.Text = "下一次闲置触发：正在读取…";
        left.Children.Add(_idleCountdown);
        grid.Children.Add(left);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        _bridgeStatus.FontSize = 12;
        _bridgeStatus.Foreground = MutedTextBrush;
        _bridgeStatus.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(_bridgeStatus);
        _summary.FontSize = 12;
        _summary.Foreground = MutedTextBrush;
        _summary.Margin = new Thickness(0, 5, 0, 0);
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(_summary);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }

    private UIElement BuildSidebar()
    {
        var border = Panel();
        border.Margin = new Thickness(0, 0, 12, 0);
        var root = new StackPanel();

        root.Children.Add(SectionLabel("任务状态"));
        _statusFilters.Margin = new Thickness(0, 7, 0, 16);
        root.Children.Add(_statusFilters);

        root.Children.Add(SectionLabel("任务类别"));
        _typeFilters.Margin = new Thickness(0, 7, 0, 16);
        root.Children.Add(_typeFilters);

        root.Children.Add(SectionLabel("说明"));
        root.Children.Add(new TextBlock
        {
            Text = "重复任务完成后会自动重新进入“未执行”，等待下一次闲置触发。失败任务不会自动循环，避免持续重复错误。",
            Foreground = MutedTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        });

        border.Child = root;
        return border;
    }

    private UIElement BuildBoard()
    {
        var border = Panel();
        border.Margin = new Thickness(0, 0, 12, 0);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _searchBox.Background = BrushFrom("#10151B");
        _searchBox.Foreground = MainTextBrush;
        _searchBox.BorderBrush = BorderLineBrush;
        _searchBox.BorderThickness = new Thickness(1);
        _searchBox.Padding = new Thickness(10, 7, 10, 7);
        _searchBox.FontSize = 12.5;
        _searchBox.ToolTip = "搜索任务名称、内容、标签";
        _searchBox.TextChanged += async (_, _) => await RefreshAsync();
        toolbar.Children.Add(_searchBox);

        var newButton = Button("＋ 新建任务", primary: true);
        newButton.Margin = new Thickness(10, 0, 0, 0);
        newButton.Click += (_, _) => NewTask();
        Grid.SetColumn(newButton, 1);
        toolbar.Children.Add(newButton);
        grid.Children.Add(toolbar);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.Content = _taskList;
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        border.Child = grid;
        return border;
    }

    private UIElement BuildEditor()
    {
        var border = Panel();
        var root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stack = new StackPanel();

        _editorTitle.Text = "新建任务";
        _editorTitle.FontSize = 17;
        _editorTitle.FontWeight = FontWeights.SemiBold;
        _editorTitle.Foreground = MainTextBrush;
        stack.Children.Add(_editorTitle);

        stack.Children.Add(FieldLabel("任务名称"));
        StyleEditorTextBox(_titleBox, 36);
        stack.Children.Add(_titleBox);

        stack.Children.Add(FieldLabel("任务类别"));
        _kindBox.Items.Add(new ComboBoxItem { Content = "ChatGPT", Tag = "chatgpt" });
        _kindBox.Items.Add(new ComboBoxItem { Content = "小程序", Tag = "extension" });
        _kindBox.SelectedIndex = 0;
        _kindBox.Background = BrushFrom("#10151B");
        _kindBox.Foreground = MainTextBrush;
        _kindBox.BorderBrush = BorderLineBrush;
        _kindBox.Padding = new Thickness(7, 5, 7, 5);
        _kindBox.SelectionChanged += (_, _) => UpdateKindFields();
        stack.Children.Add(_kindBox);

        _chatGptFields.Children.Add(FieldLabel("ChatGPT 任务内容"));
        StyleEditorTextBox(_promptBox, 120, multiline: true);
        _chatGptFields.Children.Add(_promptBox);
        stack.Children.Add(_chatGptFields);

        _extensionFields.Children.Add(FieldLabel("小程序 ID"));
        StyleEditorTextBox(_extensionIdBox, 36);
        _extensionFields.Children.Add(_extensionIdBox);
        _extensionFields.Children.Add(new TextBlock
        {
            Text = "例如：taskbar-calendar。闲置任务本身不能调用自己。",
            Foreground = MutedTextBrush,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _extensionFields.Children.Add(FieldLabel("传入参数（可选）"));
        StyleEditorTextBox(_extensionInputBox, 80, multiline: true);
        _extensionFields.Children.Add(_extensionInputBox);
        stack.Children.Add(_extensionFields);

        stack.Children.Add(FieldLabel("标签"));
        StyleEditorTextBox(_tagsBox, 36);
        _tagsBox.ToolTip = "多个标签用逗号分隔";
        stack.Children.Add(_tagsBox);

        _repeatBox.Content = "重复执行";
        _repeatBox.Foreground = MainTextBrush;
        _repeatBox.Margin = new Thickness(0, 16, 0, 0);
        stack.Children.Add(_repeatBox);
        stack.Children.Add(new TextBlock
        {
            Text = "开启后：成功完成 → 自动重新排队 → 下一次闲置再次执行。",
            Foreground = MutedTextBrush,
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 3, 0, 0)
        });

        _enabledBox.Content = "启用任务";
        _enabledBox.IsChecked = true;
        _enabledBox.Foreground = MainTextBrush;
        _enabledBox.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(_enabledBox);

        var save = Button("保存", primary: true);
        save.Margin = new Thickness(0, 18, 0, 0);
        save.Click += async (_, _) => await SaveEditorAsync();
        stack.Children.Add(save);

        var runNow = Button("立即执行");
        runNow.Margin = new Thickness(0, 8, 0, 0);
        runNow.Click += async (_, _) =>
        {
            await SaveEditorAsync();
            if (!string.IsNullOrWhiteSpace(_selectedTaskId))
            {
                IdleTaskRepository.MoveToFrontAndReset(_dataDirectory, _selectedTaskId);
                _ = Task.Run(() => _host.ProcessNextAsync());
                await Task.Delay(200);
                await RefreshAsync();
            }
        };
        stack.Children.Add(runNow);

        var delete = Button("删除任务");
        delete.Margin = new Thickness(0, 8, 0, 0);
        delete.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_selectedTaskId)) return;
            IdleTaskRepository.Delete(_dataDirectory, _selectedTaskId);
            NewTask();
            await RefreshAsync();
        };
        stack.Children.Add(delete);

        root.Content = stack;
        border.Child = root;
        UpdateKindFields();
        return border;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var tasks = IdleTaskRepository.Load(_dataDirectory);
            _lastIdleStatus = IdleTriggerRuntimeSnapshot.TryLoad(_dataDirectory);
            UpdateIdleCountdownText();
            RebuildFilters(tasks);

            var waitingQueue = tasks
                .Where(IdleTaskRecord.IsWaitingForIdle)
                .OrderBy(x => x.CreatedAt)
                .ToList();
            var waitingPositions = waitingQueue
                .Select((task, index) => new { task.Id, Index = index })
                .ToDictionary(x => x.Id, x => x.Index, StringComparer.OrdinalIgnoreCase);

            var filtered = ApplyFilters(tasks).OrderByDescending(x => x.UpdatedAt).ToList();
            _taskList.Children.Clear();
            if (filtered.Count == 0)
            {
                _taskList.Children.Add(new TextBlock
                {
                    Text = tasks.Count == 0 ? "暂无任务，点击“新建任务”开始。" : "当前筛选条件下没有任务。",
                    Foreground = MutedTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 36, 0, 0)
                });
            }
            else
            {
                foreach (var task in filtered)
                {
                    waitingPositions.TryGetValue(task.Id, out var waitingIndex);
                    _taskList.Children.Add(BuildTaskCard(
                        task,
                        waitingPositions.ContainsKey(task.Id) ? waitingIndex : (int?)null));
                }
            }

            var notRun = tasks.Count(IdleTaskRecord.IsWaitingForIdle);
            var running = tasks.Count(IdleTaskRecord.IsRunning);
            var done = tasks.Count(x => string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase));
            _summary.Text = $"共 {tasks.Count} · 未执行 {notRun} · 执行中 {running} · 已完成 {done}";

            var bridge = await IdleTaskBridge.CheckHealthAsync().ConfigureAwait(true);
            _bridgeStatus.Text = bridge.Connected ? "● ChatGPT 工作台已连接" : "○ ChatGPT 工作台未连接";
            _bridgeStatus.Foreground = bridge.Connected ? AccentBrush : AmberBrush;

            if (!string.IsNullOrWhiteSpace(_selectedTaskId) && !_editorLoading)
            {
                var current = tasks.FirstOrDefault(x => string.Equals(x.Id, _selectedTaskId, StringComparison.OrdinalIgnoreCase));
                if (current != null && !_titleBox.IsKeyboardFocusWithin && !_promptBox.IsKeyboardFocusWithin
                    && !_extensionIdBox.IsKeyboardFocusWithin && !_extensionInputBox.IsKeyboardFocusWithin
                    && !_tagsBox.IsKeyboardFocusWithin)
                {
                    LoadEditor(current);
                }
            }
        }
        catch (Exception ex)
        {
            _bridgeStatus.Text = "刷新失败：" + ex.Message;
            _bridgeStatus.Foreground = RedBrush;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private IEnumerable<IdleTaskRecord> ApplyFilters(IEnumerable<IdleTaskRecord> tasks)
    {
        var query = tasks;
        query = _statusFilter switch
        {
            "pending" => query.Where(IdleTaskRecord.IsWaitingForIdle),
            "running" => query.Where(IdleTaskRecord.IsRunning),
            "done" => query.Where(x => string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase)),
            "error" => query.Where(IdleTaskRecord.IsError),
            _ => query
        };

        if (_typeFilter != "all")
            query = query.Where(x => string.Equals(x.Kind, _typeFilter, StringComparison.OrdinalIgnoreCase));

        var search = _searchBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                (x.Title ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                || (x.Prompt ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                || (x.ExtensionId ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return query;
    }

    private void RebuildFilters(List<IdleTaskRecord> tasks)
    {
        _statusFilters.Children.Clear();
        AddFilterButton(_statusFilters, "全部", "all", ref _statusFilter, tasks.Count);
        AddFilterButton(_statusFilters, "未执行", "pending", ref _statusFilter, tasks.Count(IdleTaskRecord.IsWaitingForIdle));
        AddFilterButton(_statusFilters, "执行中", "running", ref _statusFilter, tasks.Count(IdleTaskRecord.IsRunning));
        AddFilterButton(_statusFilters, "已完成", "done", ref _statusFilter,
            tasks.Count(x => string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase)));
        AddFilterButton(_statusFilters, "异常", "error", ref _statusFilter, tasks.Count(IdleTaskRecord.IsError));

        _typeFilters.Children.Clear();
        AddFilterButton(_typeFilters, "全部类型", "all", ref _typeFilter, tasks.Count);
        AddFilterButton(_typeFilters, "ChatGPT", "chatgpt", ref _typeFilter,
            tasks.Count(x => string.Equals(x.Kind, "chatgpt", StringComparison.OrdinalIgnoreCase)));
        AddFilterButton(_typeFilters, "小程序", "extension", ref _typeFilter,
            tasks.Count(x => string.Equals(x.Kind, "extension", StringComparison.OrdinalIgnoreCase)));
    }

    private void AddFilterButton(StackPanel panel, string label, string value, ref string current, int count)
    {
        var selected = string.Equals(current, value, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Content = $"{label}  {count}",
            Tag = value,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, 4),
            Background = selected ? BrushFrom("#24384A") : Brushes.Transparent,
            Foreground = selected ? AccentBrush : MainTextBrush,
            BorderThickness = new Thickness(0),
            FontSize = 12
        };
        button.Click += async (_, _) =>
        {
            if (ReferenceEquals(panel, _statusFilters)) _statusFilter = value;
            else _typeFilter = value;
            await RefreshAsync();
        };
        panel.Children.Add(button);
    }

    private UIElement BuildTaskCard(IdleTaskRecord task, int? waitingIndex)
    {
        var selected = string.Equals(task.Id, _selectedTaskId, StringComparison.OrdinalIgnoreCase);
        var card = new Border
        {
            Background = selected ? CardSelectedBrush : CardBrush,
            BorderBrush = selected ? AccentBrush : BorderLineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 9),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(task.Title) ? BuildDefaultTitle(task) : task.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = task.Enabled ? MainTextBrush : MutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        top.Children.Add(title);

        var time = new TextBlock
        {
            Text = task.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            FontSize = 10.5,
            Foreground = MutedTextBrush
        };
        Grid.SetColumn(time, 1);
        top.Children.Add(time);
        stack.Children.Add(top);

        var chips = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        chips.Children.Add(Chip(TaskKindText(task.Kind), BlueBrush));
        chips.Children.Add(Chip(StatusText(task.Status), StatusBrush(task.Status)));
        if (task.RepeatEnabled) chips.Children.Add(Chip("重复", AccentBrush));
        if (!task.Enabled) chips.Children.Add(Chip("已停用", MutedTextBrush));
        foreach (var tag in task.Tags.Take(3))
            chips.Children.Add(Chip(tag, BrushFrom("#A78BFA")));
        stack.Children.Add(chips);

        var preview = string.Equals(task.Kind, "extension", StringComparison.OrdinalIgnoreCase)
            ? $"{task.ExtensionId}  {task.ExtensionInput}".Trim()
            : task.Prompt;
        if (!string.IsNullOrWhiteSpace(preview))
        {
            stack.Children.Add(new TextBlock
            {
                Text = preview,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 42,
                Foreground = MutedTextBrush,
                FontSize = 11.5,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        if (waitingIndex.HasValue && task.Enabled)
        {
            stack.Children.Add(new TextBlock
            {
                Text = BuildTaskEstimate(_lastIdleStatus, waitingIndex.Value),
                Foreground = AccentBrush,
                FontSize = 11,
                Margin = new Thickness(0, 7, 0, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(task.LastRunStatus))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"已执行 {task.RunCount} 次 · 上次：{StatusText(task.LastRunStatus)}"
                    + (task.LastCompletedAt.HasValue ? $" · {task.LastCompletedAt.Value.ToLocalTime():MM-dd HH:mm}" : ""),
                Foreground = MutedTextBrush,
                FontSize = 10.5,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(task.Result))
        {
            stack.Children.Add(new TextBlock
            {
                Text = "结果：" + task.Result,
                Foreground = BrushFrom("#C9D4DE"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 38,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        card.Child = stack;
        card.MouseLeftButtonUp += (_, _) =>
        {
            _selectedTaskId = task.Id;
            LoadEditor(task);
            _ = RefreshAsync();
        };
        return card;
    }

    private void NewTask()
    {
        _selectedTaskId = null;
        _editorLoading = true;
        try
        {
            _editorTitle.Text = "新建任务";
            _titleBox.Text = "";
            _kindBox.SelectedIndex = 0;
            _promptBox.Text = "";
            _extensionIdBox.Text = "";
            _extensionInputBox.Text = "";
            _tagsBox.Text = "";
            _repeatBox.IsChecked = false;
            _enabledBox.IsChecked = true;
            UpdateKindFields();
        }
        finally { _editorLoading = false; }
    }

    private void LoadEditor(IdleTaskRecord task)
    {
        _editorLoading = true;
        try
        {
            _selectedTaskId = task.Id;
            _editorTitle.Text = "编辑任务";
            _titleBox.Text = task.Title ?? "";
            _kindBox.SelectedIndex = string.Equals(task.Kind, "extension", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            _promptBox.Text = task.Prompt ?? "";
            _extensionIdBox.Text = task.ExtensionId ?? "";
            _extensionInputBox.Text = task.ExtensionInput ?? "";
            _tagsBox.Text = string.Join(", ", task.Tags);
            _repeatBox.IsChecked = task.RepeatEnabled;
            _enabledBox.IsChecked = task.Enabled;
            UpdateKindFields();
        }
        finally { _editorLoading = false; }
    }

    private async Task SaveEditorAsync()
    {
        if (_editorLoading) return;
        var kind = SelectedKind();
        var title = _titleBox.Text.Trim();
        var prompt = _promptBox.Text.Trim();
        var extensionId = _extensionIdBox.Text.Trim();
        var extensionInput = _extensionInputBox.Text.Trim();

        if (kind == "chatgpt" && string.IsNullOrWhiteSpace(prompt))
        {
            MessageBox.Show(this, "请输入 ChatGPT 任务内容。", "闲置任务", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind == "extension" && string.IsNullOrWhiteSpace(extensionId))
        {
            MessageBox.Show(this, "请输入要执行的小程序 ID。", "闲置任务", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind == "extension" && string.Equals(extensionId, _host.Runtime.ExtensionId, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "闲置任务不能把自己作为执行目标。", "闲置任务", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var record = string.IsNullOrWhiteSpace(_selectedTaskId)
            ? new IdleTaskRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "pending",
                CreatedAt = DateTimeOffset.Now,
                QueuedAt = DateTimeOffset.Now
            }
            : IdleTaskRepository.Load(_dataDirectory)
                .FirstOrDefault(x => string.Equals(x.Id, _selectedTaskId, StringComparison.OrdinalIgnoreCase))
                ?? new IdleTaskRecord
                {
                    Id = _selectedTaskId!,
                    Status = "pending",
                    CreatedAt = DateTimeOffset.Now,
                    QueuedAt = DateTimeOffset.Now
                };

        record.Title = string.IsNullOrWhiteSpace(title)
            ? (kind == "extension" ? extensionId : BuildDefaultTitle(prompt))
            : title;
        record.Kind = kind;
        record.Prompt = prompt;
        record.ExtensionId = extensionId;
        record.ExtensionInput = extensionInput;
        record.Tags = ParseTags(_tagsBox.Text);
        record.RepeatEnabled = _repeatBox.IsChecked == true;
        record.Enabled = _enabledBox.IsChecked == true;
        record.UpdatedAt = DateTimeOffset.Now;

        if (!record.Enabled && IdleTaskRecord.IsWaitingForIdle(record))
            record.Status = "pending";

        IdleTaskRepository.Upsert(_dataDirectory, record);
        _selectedTaskId = record.Id;
        _editorTitle.Text = "编辑任务";
        await RefreshAsync();
    }

    private void UpdateKindFields()
    {
        var kind = SelectedKind();
        _chatGptFields.Visibility = kind == "chatgpt" ? Visibility.Visible : Visibility.Collapsed;
        _extensionFields.Visibility = kind == "extension" ? Visibility.Visible : Visibility.Collapsed;
    }

    private string SelectedKind()
    {
        return (_kindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "chatgpt";
    }

    private void UpdateIdleCountdownText()
    {
        _idleCountdown.Text = BuildIdleCountdownText(_lastIdleStatus);
        _idleCountdown.Foreground = _lastIdleStatus?.Fullscreen == true ? AmberBrush : AccentBrush;
    }

    private static string BuildIdleCountdownText(IdleTriggerRuntimeSnapshot? status)
    {
        if (status == null) return "下一次闲置触发：等待宿主状态…";
        if (!status.Enabled) return "下一次闲置触发：已关闭";
        if (status.Fullscreen)
            return $"下一次闲置触发：已暂停（前台全屏） · 已闲置 {FormatDuration(status.IdleSeconds)}";
        if (string.Equals(status.Reason, "once-per-idle", StringComparison.OrdinalIgnoreCase))
            return "下一次闲置触发：本轮已执行，等待新的键鼠输入后重新计时";
        if (!status.NextEligibleAt.HasValue)
            return $"下一次闲置触发：等待中 · 已闲置 {FormatDuration(status.IdleSeconds)}";

        var remaining = status.NextEligibleAt.Value - DateTimeOffset.Now;
        var countdown = remaining <= TimeSpan.Zero ? "≤ 5 秒" : FormatCountdown(remaining);
        return $"下一次闲置触发：{countdown} · 已闲置 {FormatDuration(status.IdleSeconds)} / {status.AfterMinutes} 分钟";
    }

    private static string BuildTaskEstimate(IdleTriggerRuntimeSnapshot? status, int waitingIndex)
    {
        if (status == null) return "预计执行：等待闲置状态";
        if (!status.Enabled) return "预计执行：闲置触发已关闭";
        if (status.Fullscreen) return "预计执行：暂停计时（前台全屏）";
        if (string.Equals(status.Reason, "once-per-idle", StringComparison.OrdinalIgnoreCase))
            return "预计执行：等待下一轮闲置";
        if (!status.NextEligibleAt.HasValue) return "预计执行：等待下一次闲置触发";

        var expectedAt = status.NextEligibleAt.Value;
        if (waitingIndex > 0)
        {
            if (status.RepeatMinutes <= 0)
                return $"排队第 {waitingIndex + 1} 项 · 预计执行：等待下一轮闲置";
            expectedAt = expectedAt.AddMinutes(status.RepeatMinutes * waitingIndex);
        }

        var remaining = expectedAt - DateTimeOffset.Now;
        var countdown = remaining <= TimeSpan.Zero ? "≤ 5 秒" : FormatCountdown(remaining);
        return waitingIndex == 0 ? $"预计执行：{countdown}" : $"排队第 {waitingIndex + 1} 项 · 预计执行：{countdown}";
    }

    private static string FormatCountdown(TimeSpan value)
    {
        var totalSeconds = Math.Max(0, (int)Math.Ceiling(value.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours:00}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    private static string FormatDuration(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private static WrapPanel Chip(string text, Brush color)
    {
        var holder = new WrapPanel();
        holder.Children.Add(new Border
        {
            Background = WithAlpha(color, 35),
            BorderBrush = WithAlpha(color, 85),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock { Text = text, Foreground = color, FontSize = 10.5 }
        });
        return holder;
    }

    private static Border Panel()
    {
        return new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderLineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(13)
        };
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        Foreground = MutedTextBrush,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold
    };

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        Foreground = MutedTextBrush,
        FontSize = 11,
        Margin = new Thickness(0, 14, 0, 6)
    };

    private static void StyleEditorTextBox(TextBox box, double minHeight, bool multiline = false)
    {
        box.MinHeight = minHeight;
        box.Background = BrushFrom("#10151B");
        box.Foreground = MainTextBrush;
        box.BorderBrush = BorderLineBrush;
        box.BorderThickness = new Thickness(1);
        box.Padding = new Thickness(9, 7, 9, 7);
        box.FontSize = 12.5;
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
    }

    private static Button Button(string text, bool primary = false) => new()
    {
        Content = text,
        Padding = new Thickness(11, 7, 11, 7),
        Background = primary ? BrushFrom("#2563EB") : BrushFrom("#252C34"),
        Foreground = MainTextBrush,
        BorderBrush = primary ? BrushFrom("#3B82F6") : BorderLineBrush,
        BorderThickness = new Thickness(1),
        FontSize = 12
    };

    private static string TaskKindText(string? kind) =>
        string.Equals(kind, "extension", StringComparison.OrdinalIgnoreCase) ? "小程序" : "ChatGPT";

    private static string StatusText(string? status) => (status ?? "pending").ToLowerInvariant() switch
    {
        "pending" => "未执行",
        "checking" => "检查中",
        "submitting" => "提交中",
        "queued" => "已排队",
        "running" => "执行中",
        "waiting-bridge" => "等待恢复",
        "submission-uncertain" => "待确认",
        "needs-review" => "待人工核对",
        "success" => "已完成",
        "error" => "失败",
        "timeout" => "超时",
        "interrupted" => "中断",
        _ => status ?? "未执行"
    };

    private static Brush StatusBrush(string? status) => (status ?? "").ToLowerInvariant() switch
    {
        "success" => GreenBrush,
        "error" or "timeout" or "interrupted" => RedBrush,
        "running" or "queued" or "submitting" or "checking" => AccentBrush,
        "submission-uncertain" or "waiting-bridge" or "needs-review" => AmberBrush,
        _ => MutedTextBrush
    };

    private static string BuildDefaultTitle(IdleTaskRecord task) =>
        BuildDefaultTitle(string.Equals(task.Kind, "extension", StringComparison.OrdinalIgnoreCase)
            ? task.ExtensionId
            : task.Prompt);

    private static string BuildDefaultTitle(string? source)
    {
        var text = (source ?? "未命名任务").Trim().Replace("\r", " ").Replace("\n", " ");
        return text.Length > 26 ? text[..26] + "…" : text;
    }

    private static List<string> ParseTags(string? raw)
    {
        return (raw ?? "")
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static SolidColorBrush BrushFrom(string value) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    private static SolidColorBrush WithAlpha(Brush brush, byte alpha)
    {
        if (brush is SolidColorBrush solid)
            return new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
        return new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
    }
}

public static class IdleTaskWorker
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public const int MaxInterruptedRetries = 3;
    private static readonly TimeSpan ActiveJobPollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BridgeFailureBackoff = TimeSpan.FromMinutes(5);

    private static TimeSpan RetryDelay(int previousRetries) => previousRetries switch
    {
        0 => TimeSpan.FromMinutes(2),
        1 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromMinutes(15)
    };

    private static DateTimeOffset RetryAt(IdleTaskRecord task) =>
        task.NextRetryAt ?? (task.LastCompletedAt ?? task.UpdatedAt).Add(RetryDelay(task.RetryCount));

    // Chrome can close an asynchronous message channel after the ChatGPT page accepted
    // the prompt. Distinguish this transport failure from a permanent task error.
    public static bool IsMessageChannelFailure(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        (message.Contains("message channel closed before a response", StringComparison.OrdinalIgnoreCase)
         || message.Contains("message port closed before a response", StringComparison.OrdinalIgnoreCase)
         || message.Contains("Extension context invalidated", StringComparison.OrdinalIgnoreCase));

    private static bool IsRecoverableFailure(IdleTaskRecord task) =>
        IdleTaskRecord.IsRetryableFailure(task.Status)
        || (string.Equals(task.Status, "error", StringComparison.OrdinalIgnoreCase)
            && IsMessageChannelFailure(task.Error));

    private static bool RetryReady(IdleTaskRecord task, DateTimeOffset now) =>
        task.Enabled
        && string.Equals(task.Kind, "chatgpt", StringComparison.OrdinalIgnoreCase)
        && IsRecoverableFailure(task)
        && task.RetryCount < MaxInterruptedRetries
        && !string.IsNullOrWhiteSpace(task.BridgeJobId)
        && now >= RetryAt(task);

    public static async Task<string> ProcessNextAsync(IdleTaskRuntime runtime)
    {
        if (!await Gate.WaitAsync(0).ConfigureAwait(false))
            return "已有闲置任务正在执行。";

        try
        {
            var task = SelectNextTask(runtime.DataDirectory);
            if (task == null) return "没有等待处理的闲置任务。";

            if (!task.Enabled) return "任务已停用。";

            if (string.Equals(task.Kind, "extension", StringComparison.OrdinalIgnoreCase))
                return await ExecuteExtensionTaskAsync(runtime, task).ConfigureAwait(false);

            return await ExecuteChatGptTaskAsync(runtime, task).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static IdleTaskRecord? SelectNextTask(string dataDirectory)
    {
        var tasks = IdleTaskRepository.Load(dataDirectory);
        var activeChatGpt = tasks
            .Where(x => x.Enabled
                        && string.Equals(x.Kind, "chatgpt", StringComparison.OrdinalIgnoreCase)
                        && !IdleTaskRecord.IsTerminal(x.Status)
                        && !string.IsNullOrWhiteSpace(x.BridgeJobId))
            .OrderBy(x => x.QueuedAt ?? x.CreatedAt)
            .FirstOrDefault();
        if (activeChatGpt != null)
        {
            // Check a live job every ten minutes. Elapsed time alone does not prove failure.
            // The same Job ID is retained until the bridge confirms its terminal state.
            var nextCheckAt = (activeChatGpt.LastCheckedAt ?? activeChatGpt.StartedAt ?? activeChatGpt.QueuedAt ?? activeChatGpt.CreatedAt)
                .Add(ActiveJobPollInterval);
            return DateTimeOffset.Now >= nextCheckAt ? activeChatGpt : null;
        }

        var now = DateTimeOffset.Now;
        return tasks
            .Where(x => x.Enabled &&
                        ((IdleTaskRecord.IsWaitingForIdle(x) && (!x.NextEligibleAt.HasValue || x.NextEligibleAt <= now))
                         || RetryReady(x, now)))
            .OrderBy(x => IdleTaskRecord.IsRetryableFailure(x.Status)
                ? RetryAt(x) : x.QueuedAt ?? x.CreatedAt)
            .FirstOrDefault();
    }

    private static async Task<string> ExecuteExtensionTaskAsync(IdleTaskRuntime runtime, IdleTaskRecord task)
    {
        task.Status = "running";
        task.StartedAt = DateTimeOffset.Now;
        task.Attempts += 1;
        task.Error = null;
        TouchAndSave(runtime.DataDirectory, task);

        try
        {
            var result = await IdleTaskExtensionAgent.RunAsync(runtime, task.ExtensionId ?? "", task.ExtensionInput).ConfigureAwait(false);
            task.Result = result;
            CompleteSuccess(runtime.DataDirectory, task);
            return "小程序任务已完成。";
        }
        catch (Exception ex)
        {
            task.Status = "error";
            task.Error = ex.Message;
            task.LastRunStatus = "error";
            task.LastCompletedAt = DateTimeOffset.Now;
            task.CompletedAt = task.LastCompletedAt;
            task.RunCount += 1;
            TouchAndSave(runtime.DataDirectory, task);
            return "小程序任务失败：" + ex.Message;
        }
    }

    private static async Task<string> ExecuteChatGptTaskAsync(IdleTaskRuntime runtime, IdleTaskRecord task)
    {
        if (IsRecoverableFailure(task))
        {
            if (task.RetryCount >= MaxInterruptedRetries || DateTimeOffset.Now < RetryAt(task))
                return "中断任务的自动重试尚未到期，或已达到重试上限。";

            // Reconcile the old job FIRST. Do not dispatch a duplicate if the old conversation succeeded
            // or if the bridge cannot be reached to establish its terminal status.
            if (!string.IsNullOrWhiteSpace(task.BridgeJobId))
            {
                IdleBridgeJob previous;
                try
                {
                    previous = await IdleTaskBridge.GetJobAsync(task.BridgeJobId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    task.NextRetryAt = DateTimeOffset.Now.Add(BridgeFailureBackoff);
                    task.Error = "核对上一轮 Job 失败，暂停重新派发以避免重复：" + ex.Message;
                    TouchAndSave(runtime.DataDirectory, task);
                    return task.Error;
                }

                if (string.Equals(previous.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(previous.Result))
                    {
                        task.Status = "needs-review";
                        task.Error = "原 Job 返回成功但没有回复文本；需要核验，禁止直接重发。";
                        task.NextRetryAt = null;
                        TouchAndSave(runtime.DataDirectory, task);
                        return task.Error;
                    }
                    task.Result = previous.Result;
                    CompleteSuccess(runtime.DataDirectory, task);
                    return "上一轮 Job 实际已成功，已核对结果，未重复派发。";
                }
                if (!IdleTaskRecord.IsTerminal(previous.Status))
                {
                    task.Status = string.Equals(previous.Status, "queued", StringComparison.OrdinalIgnoreCase)
                        ? "queued" : "running";
                    task.LastCheckedAt = DateTimeOffset.Now;
                    task.Error = "上一轮 Job 仍在执行，保持原 Job ID，不重新派发。";
                    TouchAndSave(runtime.DataDirectory, task);
                    return task.Error;
                }
                if (previous.DeliveryUncertain)
                {
                    task.Status = "needs-review";
                    task.Error = "原页面消息可能已发送；等待人工核对，禁止重复发送。";
                    task.NextRetryAt = null;
                    TouchAndSave(runtime.DataDirectory, task);
                    return task.Error;
                }
                if (!IdleTaskRecord.IsRetryableFailure(previous.Status)
                    && !(string.Equals(previous.Status, "error", StringComparison.OrdinalIgnoreCase)
                         && IsMessageChannelFailure(previous.Message)))
                {
                    task.Status = previous.Status;
                    task.LastRunStatus = previous.Status;
                    task.Error = previous.Message;
                    task.NextRetryAt = null;
                    TouchAndSave(runtime.DataDirectory, task);
                    return "上一轮 Job 终态已变更，自动重试取消。";
                }
                if (!string.IsNullOrWhiteSpace(previous.Result)) task.Result = previous.Result;
            }

            task.PreviousBridgeJobId = task.BridgeJobId;
            task.BridgeJobId = null;
            task.RetryCount += 1;
            task.NextRetryAt = null;
            task.Status = "pending";
            task.QueuedAt = DateTimeOffset.Now;
            task.StartedAt = null;
            task.CompletedAt = null;
            task.Error = null;
            TouchAndSave(runtime.DataDirectory, task);
        }

        if (string.Equals(task.Status, "submission-uncertain", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(task.BridgeJobId))
        {
            var reconciled = await IdleTaskBridge.TryReconcileSubmissionAsync(task).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(reconciled))
            {
                task.BridgeJobId = reconciled;
                task.Status = "queued";
                task.Error = null;
                TouchAndSave(runtime.DataDirectory, task);
            }
            else
            {
                task.Error = "上次提交是否已到达 ChatGPT 无法确认。为避免重复执行，已停止自动重发；可手动重新排队。";
                TouchAndSave(runtime.DataDirectory, task);
                return task.Error;
            }
        }

        if (string.IsNullOrWhiteSpace(task.BridgeJobId))
        {
            task.Status = "checking";
            task.Error = null;
            TouchAndSave(runtime.DataDirectory, task);

            var health = await IdleTaskBridge.EnsureConnectedAsync().ConfigureAwait(false);
            if (!health.Connected)
            {
                task.Status = "pending";
                task.QueuedAt = DateTimeOffset.Now;
                task.Error = health.Message ?? "ChatGPT 工作台未连接，保留任务等待下次闲置。";
                TouchAndSave(runtime.DataDirectory, task);
                return task.Error;
            }

            task.Status = "submitting";
            task.StartedAt = DateTimeOffset.Now;
            task.Attempts += 1;
            TouchAndSave(runtime.DataDirectory, task);

            try
            {
                task.BridgeJobId = await IdleTaskBridge.SubmitAsync(
                    IdleTaskBridge.ComposePrompt(task)).ConfigureAwait(false);
                task.Status = "queued";
                task.Error = null;
                TouchAndSave(runtime.DataDirectory, task);
            }
            catch (Exception ex)
            {
                task.Status = "submission-uncertain";
                task.Error = "提交发生异常，正在保守处理以避免重复任务：" + ex.Message;
                TouchAndSave(runtime.DataDirectory, task);

                var reconciled = await IdleTaskBridge.TryReconcileSubmissionAsync(task).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(reconciled))
                    return task.Error;

                task.BridgeJobId = reconciled;
                task.Status = "queued";
                task.Error = null;
                TouchAndSave(runtime.DataDirectory, task);
            }
        }

        return await PollExistingJobAsync(runtime.DataDirectory, task).ConfigureAwait(false);
    }

    private static async Task<string> PollExistingJobAsync(string dataDirectory, IdleTaskRecord task)
    {
        IdleBridgeJob job;
        task.LastCheckedAt = DateTimeOffset.Now;
        try
        {
            job = await IdleTaskBridge.GetJobAsync(task.BridgeJobId!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            task.Status = "waiting-bridge";
            task.Error = "已保存 ChatGPT Job ID；本次巡检查询失败，稍后继续查询同一任务，不会重复提交：" + ex.Message;
            TouchAndSave(dataDirectory, task);
            return task.Error;
        }

        // A lost extension response is an interrupted transport, not a proven task
        // failure. Retain the original Job ID and reconcile it before any new send.
        var channelInterrupted = string.Equals(job.Status, "error", StringComparison.OrdinalIgnoreCase)
                                 && IsMessageChannelFailure(job.Message);
        task.Status = job.DeliveryUncertain ? "needs-review" : channelInterrupted ? "interrupted" : job.Status;
        task.Error = job.DeliveryUncertain
            ? "原 ChatGPT 页面可能仍在运行，已保留 Job ID；必须核对原页面及成果后才能重新派发：" + (job.Message ?? "")
            : job.Message;
        if (!string.IsNullOrWhiteSpace(job.Result))
            task.Result = job.Result;
        TouchAndSave(dataDirectory, task);

        if (job.DeliveryUncertain)
        {
            task.NextRetryAt = null;
            task.LastBusinessStatus = "unverified";
            TouchAndSave(dataDirectory, task);
            return "页面回传不确定，已暂停自动重派以避免重复操作。";
        }
        if (IdleTaskRecord.IsTerminal(job.Status))
        {
            if (string.Equals(job.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(job.Result))
                {
                    task.Status = "needs-review";
                    task.Error = "ChatGPT Job 返回 success，但缺少可验收的回复文本；保留 Job ID，暂停自动重发。";
                    task.NextRetryAt = null;
                    task.LastBusinessStatus = "unverified";
                    TouchAndSave(dataDirectory, task);
                    return task.Error;
                }
                CompleteSuccess(dataDirectory, task);
                return "闲置任务已完成。";
            }

            if (IsTransientBusy(job.Status, job.Message))
            {
                task.Status = "pending";
                task.BridgeJobId = null;
                task.Error = job.Message;
                task.StartedAt = null;
                task.CompletedAt = null;
                task.QueuedAt = DateTimeOffset.Now;
                TouchAndSave(dataDirectory, task);
                return "ChatGPT 工作台繁忙，本任务已自动重新排队，不计为一次失败。";
            }

            task.LastRunStatus = task.Status;
            task.LastCompletedAt = DateTimeOffset.Now;
            task.CompletedAt = task.LastCompletedAt;
            task.RunCount += 1;
            if (IdleTaskRecord.IsRetryableFailure(task.Status)
                && task.RetryCount < MaxInterruptedRetries && task.Enabled)
            {
                task.NextRetryAt = DateTimeOffset.Now.Add(RetryDelay(task.RetryCount));
                task.Error = (job.Message ?? "ChatGPT 会话中断或超时") +
                    "；将于 " + task.NextRetryAt.Value.ToString("MM-dd HH:mm") +
                    " 后自动重派（" + (task.RetryCount + 1) + "/" + MaxInterruptedRetries + "）。";
            }
            else
            {
                task.NextRetryAt = null;
                if (IdleTaskRecord.IsRetryableFailure(task.Status))
                    task.Error = (job.Message ?? "ChatGPT 会话失败") + "；自动重试已达到 " +
                        MaxInterruptedRetries + " 次上限，需要人工排查。";
            }
            TouchAndSave(dataDirectory, task);
            return "闲置任务结束：" + job.Status + (string.IsNullOrWhiteSpace(task.Error) ? "" : " · " + task.Error);
        }

        task.Status = string.Equals(job.Status, "queued", StringComparison.OrdinalIgnoreCase) ? "queued" : "running";
        task.Error = "ChatGPT 尚未返回终态；保留同一 Job ID，十分钟后再次巡检。";
        TouchAndSave(dataDirectory, task);
        return task.Error;
    }

    private static bool IsTransientBusy(string? status, string? message)
    {
        if (!string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            return false;

        var text = message ?? string.Empty;
        return text.Contains("正在执行其他任务", StringComparison.OrdinalIgnoreCase)
               || text.Contains("请稍后重试", StringComparison.OrdinalIgnoreCase)
               || text.Contains("busy", StringComparison.OrdinalIgnoreCase)
               || text.Contains("another task", StringComparison.OrdinalIgnoreCase);
    }

    private static void CompleteSuccess(string dataDirectory, IdleTaskRecord task)
    {
        var completedAt = DateTimeOffset.Now;
        task.LastRunStatus = "success";
        task.LastDeploymentStatus = IdleTaskRecord.ReadReportedStatus(task.Result, "deploymentStatus");
        task.LastReleaseStatus = IdleTaskRecord.ReadReportedStatus(task.Result, "releaseStatus");
        task.LastBusinessStatus = task.LastReleaseStatus == "published"
            ? "published"
            : task.LastDeploymentStatus == "installed"
                ? "awaiting-production-feedback"
                : task.LastReleaseStatus == "blocked" || task.LastDeploymentStatus == "blocked"
                    ? "blocked"
                    : "unverified";
        task.LastCompletedAt = completedAt;
        task.CompletedAt = completedAt;
        task.RunCount += 1;
        task.Error = null;
        task.RetryCount = 0;
        task.NextRetryAt = null;

        if (task.RepeatEnabled && task.Enabled)
        {
            task.Status = "pending";
            task.BridgeJobId = null;
            task.StartedAt = null;
            task.QueuedAt = completedAt;
            task.NextEligibleAt = task.MinimumRepeatMinutes > 0
                ? completedAt.AddMinutes(task.MinimumRepeatMinutes)
                : null;
        }
        else
        {
            task.Status = "success";
        }

        TouchAndSave(dataDirectory, task);
    }

    private static void TouchAndSave(string dataDirectory, IdleTaskRecord task)
    {
        task.UpdatedAt = DateTimeOffset.Now;
        IdleTaskRepository.Upsert(dataDirectory, task);
    }
}

public static class IdleTaskExtensionAgent
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<string> RunAsync(IdleTaskRuntime runtime, string extensionId, string? input)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
            throw new InvalidOperationException("小程序 ID 为空。");
        if (string.Equals(extensionId, runtime.ExtensionId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("闲置任务不能调用自己。");

        var payload = JsonSerializer.Serialize(new { input = input ?? "" });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            runtime.AgentApiBaseUrl.TrimEnd('/') + "/v1/extensions/" + Uri.EscapeDataString(extensionId) + "/run");
        if (!string.IsNullOrWhiteSpace(runtime.AgentApiToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", runtime.AgentApiToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"执行小程序失败：{(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
            throw new InvalidOperationException(error ?? "小程序执行失败。");
        }

        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.String)
            return output.GetString() ?? "执行完成";
        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            return message.GetString() ?? "执行完成";
        return "执行完成";
    }
}

public static class IdleTaskBridge
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private const string BaseUrl = "http://127.0.0.1:53921";

    public static async Task<IdleBridgeHealth> CheckHealthAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/health");
            AddAuthorization(request);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new IdleBridgeHealth(false, "ChatGPT 工作台返回 " + (int)response.StatusCode);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;
            var connected = root.TryGetProperty("connected", out var value) && value.ValueKind == JsonValueKind.True;
            return new IdleBridgeHealth(connected, connected ? null : "浏览器助手未连接到 ChatGPT 工作台。");
        }
        catch (Exception ex)
        {
            return new IdleBridgeHealth(false, ex.Message);
        }
    }

    public static async Task<IdleBridgeHealth> EnsureConnectedAsync()
    {
        var health = await CheckHealthAsync().ConfigureAwait(false);
        if (health.Connected) return health;

        // Auto-launch Edge only when the bridge service is reachable and explicitly reports
        // that its browser helper is absent. Keep bridge/service failures separate.
        if (!string.Equals(health.Message, "浏览器助手未连接到 ChatGPT 工作台。", StringComparison.Ordinal))
            return health;

        if (IsEdgeRunning())
            return new IdleBridgeHealth(false, "Edge 已运行，但浏览器助手仍未连接到 ChatGPT 工作台。请检查扩展是否启用。");

        var edgePath = FindEdgeExecutable();
        if (string.IsNullOrWhiteSpace(edgePath))
            return new IdleBridgeHealth(false, "浏览器助手未连接，且没有找到 Microsoft Edge 可执行文件。");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = "https://chatgpt.com/",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized
            });
        }
        catch (Exception ex)
        {
            return new IdleBridgeHealth(false, "自动启动 Edge 失败：" + ex.Message);
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            health = await CheckHealthAsync().ConfigureAwait(false);
            if (health.Connected) return health;

            if (!string.Equals(health.Message, "浏览器助手未连接到 ChatGPT 工作台。", StringComparison.Ordinal))
                return health;
        }

        return new IdleBridgeHealth(false, "已自动启动 Edge，但浏览器助手在 25 秒内仍未连接到 ChatGPT 工作台。请检查扩展是否启用。");
    }

    private static bool IsEdgeRunning()
    {
        try { return Process.GetProcessesByName("msedge").Length > 0; }
        catch { return false; }
    }

    private static string? FindEdgeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // Must match the payload used by TryReconcileSubmissionAsync after a lost submit acknowledgement.
    public static string ComposePrompt(IdleTaskRecord task) =>
        task.RetryCount == 0 ? task.Prompt :
        task.Prompt + "\n\n【闲置任务中断后自动恢复 " + task.RetryCount + "/" + IdleTaskWorker.MaxInterruptedRetries + "】" +
        "\n上次 ChatGPT 会话可能已完成部分工作。请先检查项目文件、日志及记录，" +
        "识别已完成的部分，不要重复操作或覆盖已有改动；从未完成的最小步骤继续。" +
        "\n每轮优先完成可验证的阶段性成果，并记录下一步和剩余事项。";

    public static async Task<string> SubmitAsync(string prompt)
    {
        var payload = JsonSerializer.Serialize(new
        {
            action = "chatgpt_send",
            prompt,
            temporary = true,
            tabPolicy = "new",
            closeAfter = true,
            // 0 means no reply deadline. The idle task board owns lifecycle polling.
            timeoutSeconds = 0
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/jobs");
        AddAuthorization(request);
        request.Headers.TryAddWithoutValidation("X-Bridge-Request", "1");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("ChatGPT 工作台拒绝任务：" + (int)response.StatusCode + " " + body);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("id", out var idNode))
            throw new InvalidOperationException("ChatGPT 工作台未返回 Job ID。");
        return idNode.GetString() ?? throw new InvalidOperationException("ChatGPT Job ID 为空。");
    }

    public static async Task<IdleBridgeJob> GetJobAsync(string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/api/jobs/" + Uri.EscapeDataString(id));
        AddAuthorization(request);
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("查询 ChatGPT Job 失败：" + (int)response.StatusCode + " " + body);

        using var doc = JsonDocument.Parse(body);
        return ParseJob(doc.RootElement);
    }

    public static async Task<string?> TryReconcileSubmissionAsync(IdleTaskRecord task)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/api/jobs");
            AddAuthorization(request);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            foreach (var node in doc.RootElement.EnumerateArray())
            {
                if (!node.TryGetProperty("task", out var taskNode)
                    || !taskNode.TryGetProperty("prompt", out var promptNode)
                    || !string.Equals(promptNode.GetString(), ComposePrompt(task), StringComparison.Ordinal))
                    continue;

                if (node.TryGetProperty("createdAt", out var createdNode)
                    && TryReadBridgeTime(createdNode, out var createdAt)
                    && createdAt < task.StartedAt.GetValueOrDefault(task.CreatedAt).AddMinutes(-2))
                    continue;

                if (node.TryGetProperty("id", out var idNode))
                {
                    var id = idNode.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
            }
        }
        catch { }

        return null;
    }

    private static IdleBridgeJob ParseJob(JsonElement root)
    {
        var status = root.TryGetProperty("status", out var statusNode) ? statusNode.GetString() ?? "unknown" : "unknown";
        var message = root.TryGetProperty("message", out var messageNode) && messageNode.ValueKind == JsonValueKind.String
            ? messageNode.GetString()
            : null;
        string? result = null;
        if (root.TryGetProperty("data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Object)
        {
            if (dataNode.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
                result = textNode.GetString();
            else if (dataNode.TryGetProperty("markdown", out var markdownNode) && markdownNode.ValueKind == JsonValueKind.String)
                result = markdownNode.GetString();
        }
        var uncertain = root.TryGetProperty("data", out var delivered)
                        && delivered.ValueKind == JsonValueKind.Object
                        && delivered.TryGetProperty("deliveryUncertain", out var flag)
                        && flag.ValueKind == JsonValueKind.True;
        return new IdleBridgeJob(status, result, message, uncertain);
    }

    private static bool TryReadBridgeTime(JsonElement node, out DateTimeOffset time)
    {
        time = default;
        if (node.ValueKind == JsonValueKind.String)
            return DateTimeOffset.TryParse(node.GetString(), out time);
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var millis))
        {
            try
            {
                time = DateTimeOffset.FromUnixTimeMilliseconds(millis);
                return true;
            }
            catch { }
        }
        return false;
    }

    private static void AddAuthorization(HttpRequestMessage request)
    {
        var tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "ExtensionStorage", "chatgpt-bridge", "api-token.txt");
        if (!File.Exists(tokenPath))
            throw new FileNotFoundException("没有找到 ChatGPT 工作台令牌。请先启动“ChatGPT 后台工作台”。", tokenPath);

        var token = File.ReadAllText(tokenPath).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("ChatGPT 工作台令牌为空。");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

public static class IdleTaskCapability
{
    public static object List(string dataDirectory, JsonElement payload)
    {
        var status = ReadFilter(payload, "status");
        var kind = ReadFilter(payload, "kind");
        var limit = payload.TryGetProperty("limit", out var limitNode) ? limitNode.GetInt32() : 50;
        if (limit < 1 || limit > 200)
            throw new ArgumentException("limit 必须在 1 到 200 之间。");

        var items = IdleTaskRepository.LoadReadOnly(dataDirectory)
            .Where(task => status == null || string.Equals(task.Status, status, StringComparison.OrdinalIgnoreCase))
            .Where(task => kind == null || string.Equals(task.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(task => task.UpdatedAt)
            .Take(limit)
            .Select(task => new
            {
                id = task.Id,
                title = task.Title,
                kind = task.Kind,
                status = task.Status,
                enabled = task.Enabled,
                repeatEnabled = task.RepeatEnabled,
                tags = task.Tags.ToArray(),
                attempts = task.Attempts,
                runCount = task.RunCount,
                minimumRepeatMinutes = task.MinimumRepeatMinutes,
                nextEligibleAt = task.NextEligibleAt?.ToString("O"),
                lastRunStatus = task.LastRunStatus,
                businessStatus = task.LastBusinessStatus,
                deploymentStatus = task.LastDeploymentStatus,
                releaseStatus = task.LastReleaseStatus,
                error = Preview(task.Error),
                resultPreview = Preview(task.Result),
                createdAt = task.CreatedAt.ToString("O"),
                updatedAt = task.UpdatedAt.ToString("O"),
                lastCompletedAt = task.LastCompletedAt?.ToString("O")
            })
            .ToArray();

        return new { items, count = items.Length };
    }

    private static string? ReadFilter(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var node) || node.ValueKind == JsonValueKind.Null)
            return null;
        var value = node.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? Preview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Replace("\r", " ").Replace("\n", " ");
        return normalized.Length <= 800 ? normalized : normalized[..800] + "…";
    }
}

public static class IdleTaskRepository
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static List<IdleTaskRecord> Load(string dataDirectory)
    {
        lock (Gate) return LoadUnsafe(dataDirectory);
    }

    public static List<IdleTaskRecord> LoadReadOnly(string dataDirectory)
    {
        lock (Gate)
        {
            var path = Path.Combine(dataDirectory, "tasks.json");
            if (!File.Exists(path)) return new List<IdleTaskRecord>();
            try
            {
                var tasks = JsonSerializer.Deserialize<List<IdleTaskRecord>>(File.ReadAllText(path), Options)
                            ?? new List<IdleTaskRecord>();
                foreach (var task in tasks) Normalize(task);
                return tasks;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("闲置任务数据格式损坏，无法读取。", ex);
            }
        }
    }

    public static void Upsert(string dataDirectory, IdleTaskRecord record)
    {
        lock (Gate)
        {
            var tasks = LoadUnsafe(dataDirectory);
            Normalize(record);
            var index = tasks.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) tasks[index] = record;
            else tasks.Add(record);
            SaveUnsafe(dataDirectory, tasks);
        }
    }

    public static void Delete(string dataDirectory, string id)
    {
        lock (Gate)
        {
            var tasks = LoadUnsafe(dataDirectory);
            tasks.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveUnsafe(dataDirectory, tasks);
        }
    }

    public static void MoveToFrontAndReset(string dataDirectory, string id)
    {
        lock (Gate)
        {
            var tasks = LoadUnsafe(dataDirectory);
            var record = tasks.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (record == null) return;
            record.Status = "pending";
            record.BridgeJobId = null;
            record.Error = null;
            record.StartedAt = null;
            record.CompletedAt = null;
            record.Enabled = true;
            record.QueuedAt = DateTimeOffset.MinValue;
            record.NextEligibleAt = null; // Explicit manual requeue bypasses a scheduled observation gate.
            record.UpdatedAt = DateTimeOffset.Now;
            SaveUnsafe(dataDirectory, tasks);
        }
    }

    private static List<IdleTaskRecord> LoadUnsafe(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "tasks.json");
        if (!File.Exists(path)) return new List<IdleTaskRecord>();

        try
        {
            var tasks = JsonSerializer.Deserialize<List<IdleTaskRecord>>(File.ReadAllText(path), Options)
                        ?? new List<IdleTaskRecord>();
            foreach (var task in tasks) Normalize(task);
            return tasks;
        }
        catch
        {
            var corrupt = Path.Combine(dataDirectory, "tasks.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            try { File.Copy(path, corrupt, overwrite: false); } catch { }
            return new List<IdleTaskRecord>();
        }
    }

    private static void Normalize(IdleTaskRecord task)
    {
        task.Id = string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
        task.Kind = string.IsNullOrWhiteSpace(task.Kind) ? "chatgpt" : task.Kind.Trim().ToLowerInvariant();
        task.Status = string.IsNullOrWhiteSpace(task.Status) ? "pending" : task.Status;
        task.Tags ??= new List<string>();
        if (task.CreatedAt == default) task.CreatedAt = DateTimeOffset.Now;
        if (task.UpdatedAt == default) task.UpdatedAt = task.CreatedAt;
        if (!task.QueuedAt.HasValue && IsQueueState(task.Status)) task.QueuedAt = task.CreatedAt;
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            var source = task.Kind == "extension" ? task.ExtensionId : task.Prompt;
            var text = (source ?? "未命名任务").Trim().Replace("\r", " ").Replace("\n", " ");
            task.Title = text.Length > 26 ? text[..26] + "…" : text;
        }
    }

    private static bool IsQueueState(string? status) =>
        string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "submission-uncertain", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "waiting-bridge", StringComparison.OrdinalIgnoreCase);

    private static void SaveUnsafe(string dataDirectory, List<IdleTaskRecord> tasks)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "tasks.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tasks, Options), new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class IdleTaskRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "chatgpt";
    public string Prompt { get; set; } = "";
    public string? ExtensionId { get; set; }
    public string? ExtensionInput { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool RepeatEnabled { get; set; }
    public bool Enabled { get; set; } = true;

    public string Status { get; set; } = "pending";
    public string? BridgeJobId { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public int RunCount { get; set; }
    public int MinimumRepeatMinutes { get; set; }
    public DateTimeOffset? NextEligibleAt { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? PreviousBridgeJobId { get; set; }
    public string? LastRunStatus { get; set; }
    public string? LastDeploymentStatus { get; set; }
    public string? LastReleaseStatus { get; set; }
    public string? LastBusinessStatus { get; set; }

    // A successful ChatGPT HTTP job is not proof that its deployment/release succeeded.
    // Extract only structured outcome keys, not optimistic words in the prose summary.
    public static string? ReadReportedStatus(string? result, string field)
    {
        if (string.IsNullOrWhiteSpace(result) ||
            field is not ("deploymentStatus" or "releaseStatus"))
            return null;
        var pattern = "\"" + field + "\"\\s*:\\s*\"([A-Za-z0-9_-]{2,40})\"";
        var match = System.Text.RegularExpressions.Regex.Match(
            result, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }

    public static bool IsWaitingForIdle(IdleTaskRecord task) =>
        task.Enabled && (
            string.Equals(task.Status, "pending", StringComparison.OrdinalIgnoreCase)
            || string.Equals(task.Status, "submission-uncertain", StringComparison.OrdinalIgnoreCase));

    public static bool IsRunning(IdleTaskRecord task) =>
        string.Equals(task.Status, "checking", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "submitting", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "queued", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "running", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "waiting-bridge", StringComparison.OrdinalIgnoreCase);

    public static bool IsError(IdleTaskRecord task) =>
        string.Equals(task.Status, "error", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "timeout", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "interrupted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "submission-uncertain", StringComparison.OrdinalIgnoreCase)
        || string.Equals(task.Status, "needs-review", StringComparison.OrdinalIgnoreCase);

    public static bool IsRetryableFailure(string? status) =>
        string.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "timeout", StringComparison.OrdinalIgnoreCase);

    public static bool IsTerminal(string? status) =>
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "timeout", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "needs-review", StringComparison.OrdinalIgnoreCase);
}

public sealed class IdleTriggerRuntimeSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public DateTimeOffset ObservedAt { get; set; }
    public int IdleSeconds { get; set; }
    public bool Fullscreen { get; set; }
    public bool Enabled { get; set; }
    public int AfterMinutes { get; set; }
    public int RepeatMinutes { get; set; }
    public bool TriggeredInCurrentIdlePeriod { get; set; }
    public DateTimeOffset? LastTriggeredAt { get; set; }
    public DateTimeOffset? NextEligibleAt { get; set; }
    public string Reason { get; set; } = "";

    public static IdleTriggerRuntimeSnapshot? TryLoad(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(dataDirectory, "idle-trigger-status.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<IdleTriggerRuntimeSnapshot>(File.ReadAllText(path), Options);
        }
        catch { return null; }
    }
}

public sealed record IdleBridgeHealth(bool Connected, string? Message);
public sealed record IdleBridgeJob(string Status, string? Result, string? Message, bool DeliveryUncertain = false);
