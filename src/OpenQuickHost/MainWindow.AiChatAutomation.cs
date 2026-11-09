using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Binding = System.Windows.Data.Binding;

namespace OpenQuickHost;

public sealed class AiConversationSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string TopicId { get; set; } = "";
    public DateTimeOffset NextRunAt { get; set; }
    public bool RepeatDaily { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastRunAt { get; set; }
    public string LastStatus { get; set; } = "未运行";

    public string ListSummary
    {
        get
        {
            var when = RepeatDaily
                ? "每天 " + NextRunAt.ToLocalTime().ToString("HH:mm")
                : "单次 " + NextRunAt.ToLocalTime().ToString("MM-dd HH:mm");
            return when + " · " + (Enabled ? LastStatus : "已暂停");
        }
    }

    public override string ToString() => Name + " · " + ListSummary;
}

// Hosted by MainWindow (the resident tray process), never by the chat window.
public partial class MainWindow
{
    private Window? _detachedAiChatWindow;
    private Grid? _aiChatOriginalParent;
    private int _aiChatOriginalIndex;
    private Window? _aiScheduleManagementWindow;
    private ListBox? _aiScheduleList;
    private Action<AiConversationSchedule>? _refreshAiScheduleDetail;
    private readonly List<AiConversationSchedule> _aiSchedules = [];
    private readonly DispatcherTimer _aiScheduleTimer =
        new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _aiScheduleTickRunning;

    private void AiChatDetachButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detachedAiChatWindow != null)
        {
            ActivateDetachedAiChat();
            return;
        }
        if (AiChatDockPanel.Parent is not Grid parent) return;
        _aiChatOriginalParent = parent;
        _aiChatOriginalIndex = parent.Children.IndexOf(AiChatDockPanel);
        parent.Children.Remove(AiChatDockPanel);

        BindingOperations.ClearBinding(AiChatDockPanel, UIElement.VisibilityProperty);
        AiChatDockPanel.Visibility = Visibility.Visible;

        var window = new Window
        {
            Title = "燕子 · AI 对话",
            Width = 1100, Height = 740,
            MinWidth = 850, MinHeight = 530,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = true,
            ResizeMode = ResizeMode.CanResize,
            Icon = CreateAiChatIcon(),
            DataContext = this,
            Background = new SolidColorBrush(Color.FromRgb(23, 24, 31)),
            Content = AiChatDockPanel
        };
        // Preserve host-local brushes in addition to global shared UI styles.
        window.Resources.MergedDictionaries.Add(Resources);
        AiChatTaskbarIdentity.Attach(window);
        _detachedAiChatWindow = window;
        window.Closed += (_, _) =>
        {
            window.Content = null;
            if (_aiChatOriginalParent != null &&
                !_aiChatOriginalParent.Dispatcher.HasShutdownStarted)
            {
                _aiChatOriginalParent.Children.Insert(
                    Math.Min(_aiChatOriginalIndex, _aiChatOriginalParent.Children.Count),
                    AiChatDockPanel);
                BindingOperations.SetBinding(
                    AiChatDockPanel, UIElement.VisibilityProperty,
                    new Binding(nameof(AiChatVisibility)) { Source = this });
            }
            _detachedAiChatWindow = null;
        };
        // Exit the launcher BEFORE showing the independent window. Otherwise the
        // launcher search popup can remain topmost and cover the detached chat.
        ExitAiChatMode();
        SearchScopePopup.IsOpen = false;
        HideToTray();
        window.Show();
        window.Activate();
    }

    internal void OpenDetachedAiChatFromTaskbar()
    {
        if (_detachedAiChatWindow != null)
        {
            ActivateDetachedAiChat();
            return;
        }

        // A taskbar relaunch can arrive while the host is hidden in the tray.
        // Initialize the selected AI topic/model just as opening the AI tab does.
        if (!IsAiChatMode)
        {
            var aiScope = SearchScopes.FirstOrDefault(scope =>
                string.Equals(scope.Key, SearchScopeAi, StringComparison.OrdinalIgnoreCase));
            if (aiScope != null)
                SelectedSearchScope = aiScope;
        }

        AiChatDetachButton_Click(this, new RoutedEventArgs());
    }

    private void ActivateDetachedAiChat()
    {
        if (_detachedAiChatWindow == null) return;
        SearchScopePopup.IsOpen = false;
        HideToTray();
        if (_detachedAiChatWindow.WindowState == WindowState.Minimized)
            _detachedAiChatWindow.WindowState = WindowState.Normal;
        _detachedAiChatWindow.Activate();
    }

    private static ImageSource CreateAiChatIcon()
    {
        // Dedicated blue chat icon; the taskbar entry must not use the host glyph.
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(32, 96, 213)),
                null, new Rect(2, 2, 60, 60), 15, 15);
            dc.DrawRoundedRectangle(
                Brushes.White, null, new Rect(13, 16, 38, 29), 8, 8);
            dc.DrawGeometry(Brushes.White, null,
                Geometry.Parse("M 22,40 L 19,52 L 34,43 Z"));
            var accent = new SolidColorBrush(Color.FromRgb(32, 96, 213));
            dc.DrawEllipse(accent, null, new Point(23, 30), 3, 3);
            dc.DrawEllipse(accent, null, new Point(32, 30), 3, 3);
            dc.DrawEllipse(accent, null, new Point(41, 30), 3, 3);
        }
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void InitializeAiConversationScheduler()
    {
        // Load topics even if the user never opens the chat tab.
        LoadTopicsFromStorage();
        try
        {
            var path = HostAssets.ResolveDataFilePath("ai-chat-schedules.json");
            if (File.Exists(path))
                _aiSchedules.AddRange(JsonSerializer.Deserialize<List<AiConversationSchedule>>(
                    File.ReadAllText(path)) ?? []);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("AI schedule load error: " + ex.GetType().Name);
        }
        // A prior process may have been terminated after checkpointing but before
        // receiving the reply. Do not send twice; make the interrupted result explicit.
        var interrupted = _aiSchedules.Where(s => s.LastStatus == "正在执行").ToArray();
        foreach (var item in interrupted)
            item.LastStatus = "上次执行中断，需核对会话结果";
        if (interrupted.Length != 0) SaveAiConversationSchedules();
        _aiScheduleTimer.Tick += (_, _) => CheckAiSchedules();
        _aiScheduleTimer.Start();
        Closed += (_, _) => _aiScheduleTimer.Stop();
        Dispatcher.BeginInvoke((Action)CheckAiSchedules,
            DispatcherPriority.ApplicationIdle);
    }

    private void SaveAiConversationSchedules()
    {
        try
        {
            var file = HostAssets.ResolveDataFilePath("ai-chat-schedules.json");
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_aiSchedules,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, true);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("AI schedule save error: " + ex.GetType().Name);
        }
    }

    private void CheckAiSchedules()
    {
        if (_aiScheduleTickRunning) return;
        _aiScheduleTickRunning = true;
        try
        {
            // Never hold the timer hostage for a full LLM response.
            // Separate topics may run concurrently; request tracker rejects duplicate sends.
            var due = _aiSchedules
                .Where(t => t.Enabled && t.NextRunAt <= DateTimeOffset.Now)
                .Take(8).ToArray();
            foreach (var task in due)
                _ = RunAiScheduleSafeAsync(task, manual: false);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("AI scheduler error: " + ex.GetType().Name);
        }
        finally { _aiScheduleTickRunning = false; }
    }

    private async Task RunAiScheduleSafeAsync(AiConversationSchedule task, bool manual)
    {
        try { await RunAiScheduleAsync(task, manual); }
        catch (Exception ex)
        {
            task.LastStatus = "失败：" + ex.GetType().Name;
            SaveAiConversationSchedules();
            HostAssets.AppendLog("AI scheduler unhandled error: id=" + task.Id +
                ", type=" + ex.GetType().Name);
        }
    }

    private async Task RunAiScheduleAsync(AiConversationSchedule task, bool manual)
    {
        if (string.IsNullOrWhiteSpace(task.Prompt)) return;
        var topic = string.IsNullOrWhiteSpace(task.TopicId)
            ? null
            : _aiChatTopics.FirstOrDefault(t => t.Id == task.TopicId);
        if (!string.IsNullOrWhiteSpace(task.TopicId) && topic == null)
        {
            task.LastStatus = "目标会话已删除";
            if (!manual) task.Enabled = false;
            SaveAiConversationSchedules();
            return;
        }
        // A busy topic is not consumed: retry next minute rather than overlap requests.
        if (topic != null && !_aiRequestTracker.TryPrepare(topic.Id))
        {
            task.LastStatus = "会话忙碌，将重试";
            if (!manual) task.NextRunAt = DateTimeOffset.Now.AddMinutes(1);
            SaveAiConversationSchedules();
            return;
        }
        if (topic == null)
        {
            topic = new AiChatTopic(task.Name);
            _aiChatTopics.Insert(0, topic);
            if (!_aiRequestTracker.TryPrepare(topic.Id)) return;
            SaveTopicsToStorage();
        }
        var now = DateTimeOffset.Now;
        if (!manual)
        {
            if (task.RepeatDaily)
            {
                do
                {
                    var nextLocal = task.NextRunAt.LocalDateTime.AddDays(1);
                    task.NextRunAt = new DateTimeOffset(nextLocal,
                        TimeZoneInfo.Local.GetUtcOffset(nextLocal));
                }
                while (task.NextRunAt <= now);
            }
            else task.Enabled = false;
        }
        // Checkpoint BEFORE dispatch: no duplicate send after host restart.
        task.LastRunAt = now;
        task.LastStatus = "正在执行";
        SaveAiConversationSchedules();
        RefreshAiTopicRequestState();
        try
        {
            _appSettings = AppSettingsStore.Load();
            await SubmitAiChatMessageCore(topic, task.Prompt);
            task.LastStatus = _aiTopicErrors.TryGetValue(topic.Id, out var error)
                ? "失败：" + TrimForLog(error, 65) : "已完成";
            HostAssets.AppendLog("AI schedule run: id=" + task.Id +
                ", ok=" + (task.LastStatus == "已完成"));
        }
        catch (Exception ex)
        {
            task.LastStatus = "失败：" + ex.GetType().Name;
            HostAssets.AppendLog("AI schedule failed: id=" + task.Id +
                ", type=" + ex.GetType().Name);
        }
        finally
        {
            _aiRequestTracker.EndPreparing(topic.Id);
            RefreshAiTopicRequestState();
            SaveAiConversationSchedules();
            _aiScheduleList?.Items.Refresh();
            _refreshAiScheduleDetail?.Invoke(task);
        }
    }

    private void AiChatScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiScheduleManagementWindow != null)
        {
            _aiScheduleManagementWindow.Activate();
            return;
        }

        var window = new Window
        {
            Title = "燕子 · AI 定时任务",
            Width = 960, Height = 640,
            MinWidth = 820, MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Icon = CreateAiChatIcon()
        };
        AiChatTaskbarIdentity.Attach(window);
        Yanzi.UI.Wpf.YanziUi.ApplyTo(window,
            string.Equals(AppSettingsStore.Load().ThemeMode, "Light",
                StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark);
        window.SetResourceReference(Window.BackgroundProperty, "Yanzi.Color.Background");
        window.SetResourceReference(Window.ForegroundProperty, "Yanzi.Color.Foreground");
        window.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "Yanzi.Font.Geist");
        window.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Yanzi.Color.Foreground");
        void UseSharedScrollbar(FrameworkElement element)
        {
            if (window.TryFindResource(Yanzi.UI.Wpf.YanziUi.Styles.Scrollbar) is Style scrollbar)
                element.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollbar;
        }
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBlock
        {
            Text = "宿主在后台运行时仍会触发定时会话；退出程序或关机后不会运行。重启时检查已到期任务。"
        }, Yanzi.UI.Wpf.YanziUi.Styles.TextSecondary));
        var body = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        window.Content = root;

        var sidebar = Yanzi.UI.Wpf.YanziUi.WithStyle(new Border { Padding = new Thickness(12) },
            "Yanzi.Chat.Sidebar");
        var side = new Grid();
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var sideTitle = new TextBlock
        {
            Text = "任务", FontSize = 16, FontWeight = FontWeights.SemiBold
        };
        sideTitle.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        side.Children.Add(sideTitle);
        var create = Yanzi.UI.Wpf.YanziUi.WithStyle(new Button
        {
            Content = "新建任务",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 8)
        }, Yanzi.UI.Wpf.YanziUi.Styles.DefaultButton);
        Grid.SetRow(create, 1);
        side.Children.Add(create);
        var list = Yanzi.UI.Wpf.YanziUi.WithStyle(new ListBox
        {
            ItemsSource = _aiSchedules,
            ItemTemplate = ScheduleItemTemplate(),
            BorderThickness = new Thickness(0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.List);
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Yanzi.Color.Transparent");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        UseSharedScrollbar(list);
        var listEmpty = Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBlock
        {
            Text = "还没有任务",
            Margin = new Thickness(4, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        }, Yanzi.UI.Wpf.YanziUi.Styles.TextSecondary);
        var listHost = new Grid();
        listHost.Children.Add(list);
        listHost.Children.Add(listEmpty);
        Grid.SetRow(listHost, 2);
        side.Children.Add(listHost);
        sidebar.Child = side;
        body.Children.Add(sidebar);
        _aiScheduleList = list;

        var detailCard = Yanzi.UI.Wpf.YanziUi.WithStyle(new Border { Padding = new Thickness(18) },
            "Yanzi.Chat.Transcript");
        var detailGrid = new Grid();
        detailCard.Child = detailGrid;
        var empty = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            MaxWidth = 360
        };
        var emptyTitle = new TextBlock
        {
            Text = "选择左侧任务", FontSize = 18, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        emptyTitle.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        empty.Children.Add(emptyTitle);
        empty.Children.Add(Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBlock
        {
            Text = "点击任务可查看并修改详情。\n点「新建任务」添加一条定时消息。",
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.TextSecondary));
        detailGrid.Children.Add(empty);
        var editor = new Grid();
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var editorScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        UseSharedScrollbar(editorScroll);
        var form = new StackPanel();
        editorScroll.Content = form;
        editor.Children.Add(editorScroll);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(actions, 1);
        editor.Children.Add(actions);
        editor.Visibility = Visibility.Collapsed;
        detailGrid.Children.Add(editor);
        Grid.SetColumn(detailCard, 2);
        body.Children.Add(detailCard);
        var heading = new TextBlock
        {
            Text = "新建任务", FontSize = 18, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
        form.Children.Add(heading);

        void Label(string text)
        {
            var label = new TextBlock { Text = text, Margin = new Thickness(0, 7, 0, 3) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
            form.Children.Add(label);
        }
        Label("任务名称");
        var name = Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBox(), Yanzi.UI.Wpf.YanziUi.Styles.Input);
        form.Children.Add(name);
        Label("定时发送的消息");
        var prompt = Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBox { Height = 96 },
            Yanzi.UI.Wpf.YanziUi.Styles.Textarea);
        UseSharedScrollbar(prompt);
        form.Children.Add(prompt);
        Label("选择目标会话");
        var topics = Yanzi.UI.Wpf.YanziUi.WithStyle(new ComboBox(), Yanzi.UI.Wpf.YanziUi.Styles.Select);
        void RefreshTopics()
        {
            topics.Items.Clear();
            topics.Items.Add(new ComboBoxItem { Content = "每次新建会话", Tag = "" });
            foreach (var t in _aiChatTopics)
                topics.Items.Add(new ComboBoxItem { Content = t.Title, Tag = t.Id });
            topics.SelectedIndex = 0;
        }
        RefreshTopics();
        form.Children.Add(topics);

        Label("执行日期与时间（本机时区）");
        var timeLine = new StackPanel { Orientation = Orientation.Horizontal };
        var date = new Yanzi.UI.Wpf.YanziDatePicker
        {
            Width = 210, SelectedDate = DateTime.Today.AddDays(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        var time = Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBox
        {
            Width = 88, Text = "09:00",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        }, Yanzi.UI.Wpf.YanziUi.Styles.Input);
        var repeat = Yanzi.UI.Wpf.YanziUi.WithStyle(new CheckBox
        {
            Content = "每天重复", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 0, 0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.CheckBoxPreview);
        timeLine.Children.Add(date);
        timeLine.Children.Add(time);
        timeLine.Children.Add(repeat);
        form.Children.Add(timeLine);
        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        status.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
        form.Children.Add(status);

        var drafting = false;
        Button save = null!;
        StackPanel existingActions = null!;
        void UpdateListEmpty() =>
            listEmpty.Visibility = _aiSchedules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        string Describe(AiConversationSchedule task)
        {
            var run = task.LastRunAt == null ? "尚未执行" :
                task.LastRunAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return (task.Enabled ? "启用" : "已暂停") + " · 上次 " + run + " · " + task.LastStatus;
        }
        void ShowEmpty()
        {
            drafting = false;
            editor.Visibility = Visibility.Collapsed;
            empty.Visibility = Visibility.Visible;
        }
        void ShowEditor(AiConversationSchedule? task)
        {
            drafting = task == null;
            empty.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            heading.Text = task == null ? "新建任务" : "任务详情";
            save.Content = task == null ? "创建任务" : "保存修改";
            existingActions.Visibility = task == null ? Visibility.Collapsed : Visibility.Visible;
            if (task == null)
            {
                name.Clear();
                prompt.Clear();
                date.SelectedDate = DateTime.Today.AddDays(1);
                time.Text = "09:00";
                repeat.IsChecked = false;
                RefreshTopics();
                status.Text = "创建后会出现在左侧列表。";
                return;
            }
            name.Text = task.Name;
            prompt.Text = task.Prompt;
            date.SelectedDate = task.NextRunAt.LocalDateTime.Date;
            time.Text = task.NextRunAt.LocalDateTime.ToString("HH:mm");
            repeat.IsChecked = task.RepeatDaily;
            RefreshTopics();
            foreach (ComboBoxItem option in topics.Items)
                if (string.Equals(option.Tag as string, task.TopicId, StringComparison.Ordinal))
                {
                    topics.SelectedItem = option;
                    break;
                }
            status.Text = Describe(task);
        }
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is AiConversationSchedule task) ShowEditor(task);
            else if (!drafting) ShowEmpty();
        };
        create.Click += (_, _) =>
        {
            drafting = true;
            if (list.SelectedItem != null) list.SelectedItem = null;
            ShowEditor(null);
        };

        Button MakeButton(string title, string styleKey, Action callback)
        {
            var button = Yanzi.UI.Wpf.YanziUi.WithStyle(new Button
            {
                Content = title, MinWidth = 96,
                Margin = new Thickness(0, 0, 8, 0)
            }, styleKey);
            button.Click += (_, _) => callback();
            return button;
        }
        save = MakeButton("创建任务", Yanzi.UI.Wpf.YanziUi.Styles.DefaultButton, () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) ||
                string.IsNullOrWhiteSpace(prompt.Text))
            {
                status.Text = "名称和消息不能为空"; return;
            }
            if (date.SelectedDate is not DateTime d ||
                !TimeSpan.TryParseExact(time.Text.Trim(), @"hh\:mm",
                    System.Globalization.CultureInfo.InvariantCulture, out var clock))
            {
                status.Text = "时间格式应为 HH:mm，例如 09:30"; return;
            }
            var local = d.Date + clock;
            var next = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            if (next <= DateTimeOffset.Now)
            {
                status.Text = "下次执行时间必须晚于现在"; return;
            }
            var task = list.SelectedItem as AiConversationSchedule;
            var isNew = task == null;
            task ??= new AiConversationSchedule();
            task.Name = name.Text.Trim();
            task.Prompt = prompt.Text.Trim();
            task.TopicId = (topics.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            task.NextRunAt = next;
            task.RepeatDaily = repeat.IsChecked == true;
            if (isNew) task.Enabled = true;
            if (isNew || task.Enabled) task.LastStatus = "等待执行";
            if (isNew) _aiSchedules.Add(task);
            SaveAiConversationSchedules();
            list.Items.Refresh();
            UpdateListEmpty();
            list.SelectedItem = task;
            status.Text = "任务已保存，后台调度已生效";
        });
        existingActions = new StackPanel { Orientation = Orientation.Horizontal };
        existingActions.Children.Add(MakeButton("暂停 / 恢复", Yanzi.UI.Wpf.YanziUi.Styles.OutlineButton, () =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            task.Enabled = !task.Enabled;
            SaveAiConversationSchedules();
            list.Items.Refresh();
            status.Text = Describe(task);
        }));
        existingActions.Children.Add(MakeButton("立即执行", Yanzi.UI.Wpf.YanziUi.Styles.OutlineButton, () =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            _ = RunAiScheduleSafeAsync(task, manual: true);
            status.Text = "已发起，不会修改下次定时执行时间";
        }));
        existingActions.Children.Add(MakeButton("删除", Yanzi.UI.Wpf.YanziUi.Styles.DestructiveButton, () =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            _aiSchedules.Remove(task);
            SaveAiConversationSchedules();
            list.SelectedItem = null;
            list.Items.Refresh();
            UpdateListEmpty();
            ShowEmpty();
        }));
        actions.Children.Add(save);
        actions.Children.Add(existingActions);

        _refreshAiScheduleDetail = finished =>
        {
            if (ReferenceEquals(list.SelectedItem, finished)) status.Text = Describe(finished);
        };
        UpdateListEmpty();
        ShowEmpty();
        _aiScheduleManagementWindow = window;
        window.Closed += (_, _) =>
        {
            _aiScheduleManagementWindow = null;
            _aiScheduleList = null;
            _refreshAiScheduleDetail = null;
        };
        window.Show();
        window.Activate();
    }

    private static DataTemplate ScheduleItemTemplate()
    {
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(AiConversationSchedule.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var summary = new FrameworkElementFactory(typeof(TextBlock));
        summary.SetBinding(TextBlock.TextProperty, new Binding(nameof(AiConversationSchedule.ListSummary)));
        summary.SetValue(TextBlock.FontSizeProperty, 12.0);
        summary.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
        summary.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.AppendChild(name);
        stack.AppendChild(summary);
        return new DataTemplate { VisualTree = stack };
    }
}
