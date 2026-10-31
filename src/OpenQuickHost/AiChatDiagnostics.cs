using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using Clipboard = System.Windows.Clipboard;
using FontFamily = System.Windows.Media.FontFamily;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenQuickHost;

// Requests are logged without prompts, response bodies, API keys, or authorization headers.
internal static class AiChatDiagnostics
{
    private static readonly object Gate = new();
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
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2_000_000)
                {
                    File.Move(LogPath, LogPath + ".old", true);
                }
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
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

    internal static string ReadRecent(string? topicId, bool allTopics)
    {
        if (!File.Exists(LogPath))
            return "尚无 AI 对话日志。发送消息后会记录请求阶段、耗时、HTTP 状态和错误类别。\n不会记录密钥、消息正文或模型回复。";
        try
        {
            string[] lines;
            lock (Gate) { lines = File.ReadAllLines(LogPath, Encoding.UTF8); }
            var selected = lines.Where(line => allTopics || string.IsNullOrEmpty(topicId) ||
                    line.Contains("|topic=" + topicId + "|", StringComparison.Ordinal))
                    .TakeLast(250).ToArray();
            return selected.Length > 0 ? string.Join(Environment.NewLine, selected)
                : "当前对话尚无请求日志，可勾选「显示全部对话」查看其他记录。";
        }
        catch (IOException e)
        {
            return "日志暂时无法读取：" + e.GetType().Name;
        }
    }
}

internal sealed class AiChatLogWindow : Window
{
    private readonly TextBox _text;
    private readonly CheckBox _all;
    private readonly Func<string?> _currentTopic;
    private readonly DispatcherTimer _refreshTimer;

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
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
        Foreground = Brushes.White;
        ShowInTaskbar = false;

        var layout = new Grid { Margin = new Thickness(16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        _all = new CheckBox { Content = "显示全部对话", VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 12, 0) };
        _all.Checked += (_, _) => Reload();
        _all.Unchecked += (_, _) => Reload();
        actions.Children.Add(_all);
        var refresh = new Button { Content = "刷新", MinWidth = 65, Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) => Reload();
        actions.Children.Add(refresh);
        var copy = new Button { Content = "复制日志", MinWidth = 80 };
        copy.Click += (_, _) => CopyLog();
        actions.Children.Add(copy);
        Grid.SetRow(actions, 0);
        layout.Children.Add(actions);

        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(16, 16, 16)),
            Foreground = new SolidColorBrush(Color.FromRgb(222, 222, 222)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(66, 66, 66))
        };
        Grid.SetRow(_text, 1);
        layout.Children.Add(_text);

        var location = new TextBlock
        {
            Text = "本机日志：" + AiChatDiagnostics.LogPath + "    ·    不包含 API Key、请求正文或回复内容",
            Foreground = Brushes.Gray,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(location, 2);
        layout.Children.Add(location);
        Content = layout;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => Reload();
        Loaded += (_, _) => { Reload(); _refreshTimer.Start(); };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void CopyLog()
    {
        if (!string.IsNullOrEmpty(_text.Text)) Clipboard.SetText(_text.Text);
    }

    private void Reload()
    {
        var updated = AiChatDiagnostics.ReadRecent(_currentTopic(), _all.IsChecked == true);
        if (_text.Text != updated) _text.Text = updated;
    }
}
