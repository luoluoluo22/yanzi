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

    public override string ToString() =>
        (Enabled ? "● " : "○ ") + Name + " · " +
        (RepeatDaily ? "每天 " : "单次 ") +
        NextRunAt.ToLocalTime().ToString("MM-dd HH:mm") +
        "\n    " + LastStatus;
}

// Hosted by MainWindow (the resident tray process), never by the chat window.
public partial class MainWindow
{
    private Window? _detachedAiChatWindow;
    private Grid? _aiChatOriginalParent;
    private int _aiChatOriginalIndex;
    private Window? _aiScheduleManagementWindow;
    private ListBox? _aiScheduleList;
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
        window.Show();
        window.Activate();
        ExitAiChatMode(); // Leave the launcher usable independently of the taskbar window.
    }

    private void ActivateDetachedAiChat()
    {
        if (_detachedAiChatWindow == null) return;
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
        _aiScheduleTimer.Tick += async (_, _) => await CheckAiSchedulesAsync();
        _aiScheduleTimer.Start();
        Closed += (_, _) => _aiScheduleTimer.Stop();
        Dispatcher.BeginInvoke(async () => await CheckAiSchedulesAsync(),
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

    private async Task CheckAiSchedulesAsync()
    {
        if (_aiScheduleTickRunning) return;
        _aiScheduleTickRunning = true;
        try
        {
            foreach (var task in _aiSchedules
                .Where(t => t.Enabled && t.NextRunAt <= DateTimeOffset.Now).ToArray())
                await RunAiScheduleAsync(task, manual: false);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("AI scheduler error: " + ex.GetType().Name);
        }
        finally { _aiScheduleTickRunning = false; }
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
            Width = 690, Height = 710,
            MinWidth = 560, MinHeight = 580,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Icon = CreateAiChatIcon(),
            Background = new SolidColorBrush(Color.FromRgb(28, 30, 39)),
            Foreground = Brushes.White
        };
        Yanzi.UI.Wpf.YanziUi.ApplyTo(window,
            string.Equals(AppSettingsStore.Load().ThemeMode, "Light",
                StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new StackPanel { Margin = new Thickness(20) };
        scroll.Content = form;
        window.Content = scroll;

        form.Children.Add(new TextBlock
        {
            Text = "宿主在后台运行时仍会触发定时会话；退出程序或关机后不会运行。重启时检查已到期任务。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        var list = new ListBox
        {
            ItemsSource = _aiSchedules, Height = 160,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _aiScheduleList = list;
        form.Children.Add(list);

        void Label(string text) => form.Children.Add(
            new TextBlock { Text = text, Margin = new Thickness(0, 7, 0, 3) });
        Label("任务名称");
        var name = new TextBox { Height = 32, Padding = new Thickness(6) };
        form.Children.Add(name);
        Label("定时发送的消息");
        var prompt = new TextBox
        {
            Height = 78, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6)
        };
        form.Children.Add(prompt);
        Label("选择目标会话");
        var topics = new ComboBox { Height = 33 };
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
        var date = new DatePicker
        {
            Width = 160, Height = 33, SelectedDate = DateTime.Today.AddDays(1)
        };
        var time = new TextBox
        {
            Width = 80, Height = 33, Text = "09:00",
            Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(6)
        };
        var repeat = new CheckBox
        {
            Content = "每天重复", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(15, 0, 0, 0)
        };
        timeLine.Children.Add(date);
        timeLine.Children.Add(time);
        timeLine.Children.Add(repeat);
        form.Children.Add(timeLine);
        var status = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        form.Children.Add(status);

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            name.Text = task.Name;
            prompt.Text = task.Prompt;
            date.SelectedDate = task.NextRunAt.LocalDateTime.Date;
            time.Text = task.NextRunAt.LocalDateTime.ToString("HH:mm");
            repeat.IsChecked = task.RepeatDaily;
            RefreshTopics();
            foreach (ComboBoxItem option in topics.Items)
                if (string.Equals(option.Tag as string, task.TopicId,
                    StringComparison.Ordinal))
                {
                    topics.SelectedItem = option;
                    break;
                }
            status.Text = task.LastRunAt == null ? task.LastStatus :
                task.LastRunAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") +
                " · " + task.LastStatus;
        };

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        form.Children.Add(actions);
        void Action(string title, Action callback)
        {
            var button = new Button
            {
                Content = title, Height = 35, MinWidth = 90,
                Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(9, 0, 9, 0)
            };
            button.Click += (_, _) => callback();
            actions.Children.Add(button);
        }

        Action("新增 / 保存", () =>
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
            var task = list.SelectedItem as AiConversationSchedule
                ?? new AiConversationSchedule();
            task.Name = name.Text.Trim();
            task.Prompt = prompt.Text.Trim();
            task.TopicId = (topics.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            task.NextRunAt = next;
            task.RepeatDaily = repeat.IsChecked == true;
            task.Enabled = true;
            task.LastStatus = "等待执行";
            if (!_aiSchedules.Contains(task)) _aiSchedules.Add(task);
            SaveAiConversationSchedules();
            list.Items.Refresh();
            list.SelectedItem = task;
            status.Text = "任务已保存，后台调度已生效";
        });
        Action("暂停 / 恢复", () =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            task.Enabled = !task.Enabled;
            SaveAiConversationSchedules();
            list.Items.Refresh();
            status.Text = task.Enabled ? "已恢复" : "已暂停";
        });
        Action("立即执行", () =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            _ = RunAiScheduleAsync(task, manual: true);
            status.Text = "已发起，不会修改下次定时执行时间";
        });
        Action("删除", () =>
        {
            if (list.SelectedItem is not AiConversationSchedule task) return;
            _aiSchedules.Remove(task);
            SaveAiConversationSchedules();
            list.Items.Refresh();
            status.Text = "已删除";
        });
        Action("清空表单", () =>
        {
            list.SelectedItem = null; name.Clear(); prompt.Clear();
            date.SelectedDate = DateTime.Today.AddDays(1);
            time.Text = "09:00"; repeat.IsChecked = false;
            RefreshTopics(); status.Text = "";
        });

        _aiScheduleManagementWindow = window;
        window.Closed += (_, _) =>
        {
            _aiScheduleManagementWindow = null;
            _aiScheduleList = null;
        };
        window.Show();
        window.Activate();
    }
}
