using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using Clipboard = System.Windows.Clipboard;
using System.Windows.Threading;

namespace OpenQuickHost;

// Requests are logged without prompts, response bodies, API keys, or authorization headers.
internal static class AiChatDiagnostics
{
    private static readonly object Gate = new();
    private static readonly AiChatLogSnapshotCache ReadCache = new();
    private static long _logRevision;
    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "logs", "ai-chat.log");

    internal static void Record(string topicId, string model, string stage, string details = "")
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} |topic={Safe(topicId)}| model={Safe(model)} stage={Safe(stage)} {Safe(details)}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > AiChatLogLimits.MaxBytes)
                {
                    File.Move(LogPath, LogPath + ".old", true);
                }
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                Interlocked.Increment(ref _logRevision);
            }
        }
        catch
        {
            // Diagnostics must never break chat requests.
        }
    }

    private static string Safe(string? value)
    {
        var sanitized = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
        return sanitized.Length > 250 ? sanitized[..250] : sanitized;
    }

    internal static string ReadRecent(string? topicId, bool allTopics) =>
        ReadCache.ReadRecent(LogPath, topicId, allTopics,
            () => Interlocked.Read(ref _logRevision),
            path =>
            {
                lock (Gate) return File.ReadAllLines(path, Encoding.UTF8);
            });

    // Manual refresh bypasses cached snapshots even when an external writer preserves metadata.
    internal static void ForceReload() => Interlocked.Increment(ref _logRevision);

    internal static void Clear()
    {
        try
        {
            lock (Gate)
            {
                TryDelete(LogPath);
                TryDelete(LogPath + ".old");
                Interlocked.Increment(ref _logRevision);
            }
        }
        catch
        {
            // Diagnostics must never break the log window.
        }
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

internal sealed class AiChatLogWindow : Window
{
    private readonly TextBox _text;
    private readonly CheckBox _all;
    private readonly Func<string?> _currentTopic;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isClosed;
    private bool _refreshRunning;
    private bool _refreshPending;
    private readonly AiChatLogRefreshVersion _refreshVersion = new();

    internal AiChatLogWindow(Window owner, Func<string?> currentTopic)
    {
        Owner = owner;
        _currentTopic = currentTopic;
        Title = "模型对话日志";
        Width = 860;
        Height = 570;
        MinWidth = 560;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(this,
            string.Equals(AppSettingsStore.Load().ThemeMode, "Light", StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark);
        SetResourceReference(BackgroundProperty, "Yanzi.Color.Background");
        SetResourceReference(ForegroundProperty, "Yanzi.Color.Foreground");
        SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "Yanzi.Font.Geist");
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Yanzi.Color.Foreground");
        void UseSharedScrollbar(FrameworkElement element)
        {
            if (TryFindResource(Yanzi.UI.Wpf.YanziUi.Styles.Scrollbar) is Style scrollbar)
                element.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollbar;
        }

        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        _all = Yanzi.UI.Wpf.YanziUi.WithStyle(new CheckBox
        {
            Content = "显示全部对话", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.CheckBoxPreview);
        _all.Checked += (_, _) => Reload();
        _all.Unchecked += (_, _) => Reload();
        actions.Children.Add(_all);
        var refresh = Yanzi.UI.Wpf.YanziUi.WithStyle(new Button
        {
            Content = "刷新", MinWidth = 72, Margin = new Thickness(0, 0, 8, 0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.OutlineButton);
        refresh.Click += (_, _) => { AiChatDiagnostics.ForceReload(); Reload(force: true); };
        actions.Children.Add(refresh);
        var copy = Yanzi.UI.Wpf.YanziUi.WithStyle(new Button
        {
            Content = "复制日志", MinWidth = 88, Margin = new Thickness(0, 0, 8, 0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.OutlineButton);
        copy.Click += (_, _) => CopyLog();
        actions.Children.Add(copy);
        var clear = Yanzi.UI.Wpf.YanziUi.WithStyle(new Button
        {
            Content = "清除日志", MinWidth = 88
        }, Yanzi.UI.Wpf.YanziUi.Styles.DestructiveButton);
        clear.Click += (_, _) => ClearLog();
        actions.Children.Add(clear);
        Grid.SetRow(actions, 0);
        layout.Children.Add(actions);

        _text = Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBox
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }, Yanzi.UI.Wpf.YanziUi.Styles.Textarea);
        UseSharedScrollbar(_text);
        Grid.SetRow(_text, 1);
        layout.Children.Add(_text);

        var location = Yanzi.UI.Wpf.YanziUi.WithStyle(new TextBlock
        {
            Text = "本机日志：" + AiChatDiagnostics.LogPath +
                "    ·    不包含 API Key、请求正文或回复内容" +
                "    ·    单文件超过 " + (AiChatLogLimits.MaxBytes / 1_000_000) +
                " MB 自动轮转，界面显示最近 " + AiChatLogLimits.RecentLines + " 条",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 10, 0, 0)
        }, Yanzi.UI.Wpf.YanziUi.Styles.TextSecondary);
        Grid.SetRow(location, 2);
        layout.Children.Add(location);
        Content = layout;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => Reload();
        Loaded += (_, _) => { Reload(); _refreshTimer.Start(); };
        Closed += (_, _) =>
        {
            _isClosed = true;
            _refreshVersion.Cancel();
            _refreshTimer.Stop();
        };
    }

    private void CopyLog()
    {
        if (!string.IsNullOrEmpty(_text.Text)) Clipboard.SetText(_text.Text);
    }

    private void ClearLog()
    {
        AiChatDiagnostics.Clear();
        Reload(force: true);
    }

    private void Reload(bool force = false)
    {
        if (_isClosed) return;
        // Repeated timer ticks must not invalidate an in-flight read when the
        // selected conversation is unchanged, even on a slow disk.
        if (force) _refreshVersion.Cancel();
        _refreshVersion.Observe(_currentTopic(), _all.IsChecked == true);
        _refreshPending = true;
        if (!_refreshRunning) _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _refreshRunning = true;
        try
        {
            while (_refreshPending && !_isClosed)
            {
                _refreshPending = false;
                var generation = _refreshVersion.Generation;
                var topic = _currentTopic();
                var allTopics = _all.IsChecked == true;
                // Filesystem probes and full-file reads run outside the UI dispatcher.
                var updated = await Task.Run(() => AiChatDiagnostics.ReadRecent(topic, allTopics));
                if (_isClosed) return;
                if (generation != _refreshVersion.Generation ||
                    !string.Equals(topic, _currentTopic(), StringComparison.Ordinal) ||
                    allTopics != (_all.IsChecked == true))
                {
                    _refreshPending = true;
                    continue;
                }
                if (_text.Text != updated) _text.Text = updated;
            }
        }
        catch (Exception ex)
        {
            if (!_isClosed) _text.Text = "日志暂时无法读取：" + ex.GetType().Name;
        }
        finally
        {
            _refreshRunning = false;
            if (_refreshPending && !_isClosed) Reload();
        }
    }
}
