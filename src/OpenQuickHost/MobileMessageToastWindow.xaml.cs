using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Forms;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class MobileMessageToastWindow : Window
{
    private const int MaxHistoryMessages = 120;
    private const int TailReadChunkBytes = 64 * 1024;

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""]+|www\.[^\s<>""]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly StringBuilder _conversationText = new();
    private string? _lastUrl;
    private System.Windows.Threading.DispatcherTimer? _autoCloseTimer;

    public MobileMessageToastWindow()
    {
        InitializeComponent();
        LoadInboxHistory();

        Loaded += (_, _) =>
        {
            PositionBottomRight();
            InitializeAutoCloseTimer();
        };
    }

    public MobileMessageToastWindow(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl = null, string? screenshotFilePath = null)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "手机发来消息" : title.Trim();
        AppendMessageCore(title, messageText, sourceDeviceId, receivedAt, screenshotDataUrl, screenshotFilePath, updateHeader: true);

        Loaded += (_, _) =>
        {
            PositionBottomRight();
            InitializeAutoCloseTimer();
        };
    }

    public void AppendMessage(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl = null, string? screenshotFilePath = null)
    {
        AppendMessageCore(title, messageText, sourceDeviceId, receivedAt, screenshotDataUrl, screenshotFilePath, updateHeader: true);
    }

    public void LoadInboxHistory()
    {
        MessageStack.Children.Clear();
        _conversationText.Clear();
        _lastUrl = null;

        TitleText.Text = "手机聊天记录";
        MetaText.Text = "手机与电脑对话";

        var entries = ReadInboxHistory();
        if (entries.Count == 0)
        {
            MessageStack.Children.Add(new TextBlock
            {
                Text = "暂无手机消息记录。",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 4, 2, 4)
            });
            UpdateUrlActions();
            return;
        }

        foreach (var entry in entries)
        {
            AppendMessageCore(
                entry.Title,
                entry.Text,
                entry.SourceDeviceName,
                entry.ReceivedAt,
                entry.ScreenshotDataUrl,
                entry.LocalFilePath,
                updateHeader: false);
        }

        var latest = entries[^1];
        MetaText.Text = $"共 {entries.Count} 条 · 最近来自 {latest.SourceDeviceName} · {latest.ReceivedAt:MM-dd HH:mm}";
        UpdateUrlActions();
        Dispatcher.InvokeAsync(() => MessageScrollViewer.ScrollToEnd());
    }

    private void AppendMessageCore(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl, string? screenshotFilePath, bool updateHeader)
    {
        var sourceLabel = MobileDeviceNameNormalizer.Normalize(sourceDeviceId);
        _lastUrl = ExtractUrl(messageText) ?? _lastUrl;
        if (_conversationText.Length > 0)
        {
            _conversationText.AppendLine();
        }

        _conversationText.Append('[').Append(receivedAt.ToString("HH:mm:ss")).Append("] ")
            .Append(sourceLabel).Append(": ").Append(messageText);

        if (updateHeader)
        {
            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "手机发来消息" : title.Trim();
            MetaText.Text = $"最近来自 {sourceLabel} · {receivedAt:HH:mm:ss}";
        }

        AddMessageBubble(messageText, sourceLabel, receivedAt, screenshotDataUrl, screenshotFilePath);
        UpdateUrlActions();
        if (AutoCloseCheckBox != null && AutoCloseCheckBox.IsChecked == true)
        {
            ResetAutoCloseTimer();
        }
        Dispatcher.InvokeAsync(() => MessageScrollViewer.ScrollToEnd());
    }

    private void AddMessageBubble(string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl, string? screenshotFilePath)
    {
        var container = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 17, 24, 39)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(36, 56, 189, 248)),
            BorderThickness = new Thickness(1)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{sourceDeviceId} · {receivedAt:HH:mm:ss}",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
            FontSize = 11
        });
        panel.Children.Add(new System.Windows.Controls.TextBox
        {
            Text = messageText,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)),
            FontSize = 14,
            Padding = new Thickness(0)
        });

        var screenshot = TryCreateScreenshotImage(screenshotDataUrl, screenshotFilePath, receivedAt);
        if (screenshot.Image != null)
        {
            panel.Children.Add(screenshot.Image);
        }
        else if (!string.IsNullOrWhiteSpace(screenshotDataUrl) || !string.IsNullOrWhiteSpace(screenshotFilePath))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "截图预览加载失败，详情请查看 host.log。",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
        if (!string.IsNullOrWhiteSpace(screenshot.FilePath))
        {
            var pathBox = new System.Windows.Controls.TextBox
            {
                Text = $"已保存：{screenshot.FilePath}",
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(103, 232, 249)),
                FontSize = 11,
                Padding = new Thickness(0)
            };
            pathBox.Cursor = System.Windows.Input.Cursors.Hand;
            pathBox.ToolTip = "双击打开文件";
            pathBox.MouseDoubleClick += (_, e) =>
            {
                TryOpenFilePath(screenshot.FilePath);
                e.Handled = true;
            };
            panel.Children.Add(pathBox);
        }

        container.Child = panel;
        MessageStack.Children.Add(container);
    }

    private static (System.Windows.Controls.Image? Image, string? FilePath) TryCreateScreenshotImage(string? dataUrl, string? existingFilePath, DateTimeOffset receivedAt)
    {
        try
        {
            byte[] bytes;
            string filePath;
            if (!string.IsNullOrWhiteSpace(existingFilePath) && File.Exists(existingFilePath))
            {
                bytes = File.ReadAllBytes(existingFilePath);
                filePath = existingFilePath;
            }
            else if (!string.IsNullOrWhiteSpace(dataUrl))
            {
                const string marker = "base64,";
                var index = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return (null, null);
                }

                bytes = Convert.FromBase64String(dataUrl[(index + marker.Length)..]);
                filePath = SaveScreenshotToDownloads(bytes, receivedAt);
            }
            else
            {
                return (null, null);
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.DecodePixelWidth = 300;
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new System.Windows.Controls.Image
            {
                Source = bitmap,
                Margin = new Thickness(0, 10, 0, 0),
                MaxWidth = 300,
                MaxHeight = 180,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "双击打开文件，右键更多操作"
            };
            image.ContextMenu = BuildScreenshotContextMenu(bitmap, bytes, filePath);
            image.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    TryOpenFilePath(filePath);
                    e.Handled = true;
                }
            };
            image.MouseRightButtonUp += (_, e) =>
            {
                image.ContextMenu.IsOpen = true;
                e.Handled = true;
            };
            HostAssets.AppendLog($"Mobile screenshot preview loaded: local={filePath}, bytes={bytes.Length}.");
            return (image, filePath);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile screenshot preview failed: {ex.GetType().Name}: {ex.Message}");
            return (null, null);
        }
    }

    private static string SaveScreenshotToDownloads(byte[] bytes, DateTimeOffset receivedAt)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        Directory.CreateDirectory(downloads);
        var path = Path.Combine(downloads, $"yanzi-mobile-screenshot-{receivedAt:yyyyMMdd-HHmmss-fff}.jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static System.Windows.Controls.ContextMenu BuildScreenshotContextMenu(BitmapSource bitmap, byte[] bytes, string filePath)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var copy = new System.Windows.Controls.MenuItem { Header = "复制图片" };
        copy.Click += (_, _) => System.Windows.Clipboard.SetImage(bitmap);
        var copyPath = new System.Windows.Controls.MenuItem { Header = "复制文件路径" };
        copyPath.Click += (_, _) => ClipboardService.SetText(filePath);
        var open = new System.Windows.Controls.MenuItem { Header = "打开图片" };
        open.Click += (_, _) => TryOpenFilePath(filePath);
        var saveAs = new System.Windows.Controls.MenuItem { Header = "另存为..." };
        saveAs.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "JPEG 图片 (*.jpg)|*.jpg|所有文件 (*.*)|*.*",
                FileName = Path.GetFileName(filePath),
                InitialDirectory = Path.GetDirectoryName(filePath)
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                File.WriteAllBytes(dialog.FileName, bytes);
            }
        };
        menu.Items.Add(copy);
        menu.Items.Add(copyPath);
        menu.Items.Add(open);
        menu.Items.Add(saveAs);
        return menu;
    }

    private static bool TryOpenFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            HostAssets.AppendLog($"Mobile inbox open file skipped: path={filePath ?? "(empty)"}.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox open file failed: path={filePath}, {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static List<MobileInboxEntry> ReadInboxHistory()
    {
        var entries = new List<MobileInboxEntry>();
        try
        {
            if (!File.Exists(HostAssets.MobileInboxPath))
            {
                return entries;
            }

            foreach (var line in ReadRecentLines(HostAssets.MobileInboxPath, MaxHistoryMessages))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = TryReadInboxEntry(line);
                if (entry == null)
                {
                    continue;
                }

                entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox history load failed: {ex.GetType().Name}: {ex.Message}");
        }

        return entries;
    }

    private static IReadOnlyList<string> ReadRecentLines(string path, int maxLines)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
        {
            return [];
        }

        var chunks = new List<byte[]>();
        var position = stream.Length;
        var newlineCount = 0;

        while (position > 0 && newlineCount <= maxLines)
        {
            var bytesToRead = (int)Math.Min(TailReadChunkBytes, position);
            position -= bytesToRead;
            var buffer = new byte[bytesToRead];
            stream.Seek(position, SeekOrigin.Begin);
            var read = stream.Read(buffer, 0, bytesToRead);
            if (read <= 0)
            {
                break;
            }

            if (read != bytesToRead)
            {
                Array.Resize(ref buffer, read);
            }

            newlineCount += buffer.Count(static value => value == (byte)'\n');
            chunks.Add(buffer);
        }

        if (chunks.Count == 0)
        {
            return [];
        }

        var totalLength = chunks.Sum(static chunk => chunk.Length);
        var data = new byte[totalLength];
        var offset = 0;
        for (var index = chunks.Count - 1; index >= 0; index--)
        {
            var chunk = chunks[index];
            Buffer.BlockCopy(chunk, 0, data, offset, chunk.Length);
            offset += chunk.Length;
        }

        var text = Encoding.UTF8.GetString(data).TrimEnd('\r', '\n');
        return text
            .Split(['\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd('\r'))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(maxLines)
            .ToList();
    }

    private static MobileInboxEntry? TryReadInboxEntry(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var payload = root.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
                ? payloadElement
                : default;

            var source = FirstNonEmpty(
                ReadString(root, "sourceDeviceName"),
                ReadString(payload, "sourceDeviceName"),
                ReadString(root, "sourceDeviceId"),
                "手机");
            var title = FirstNonEmpty(ReadString(root, "title"), "手机发来消息");
            var text = FirstNonEmpty(ReadString(root, "text"), ReadString(root, "kind"), "手机消息");
            var localFilePath = FirstNonEmpty(
                ReadString(root, "localFilePath"),
                ReadString(root, "screenshotFilePath"),
                ReadString(payload, "localFilePath"),
                ReadString(payload, "screenshotFilePath"),
                ReadString(payload, "filePath"));
            var screenshotDataUrl = FirstNonEmpty(
                ReadString(root, "screenshotDataUrl"),
                ReadString(payload, "screenshotDataUrl"));

            return new MobileInboxEntry(
                title,
                text,
                MobileDeviceNameNormalizer.Normalize(source, ReadString(root, "sourceDeviceId")),
                ReadReceivedAt(root),
                string.IsNullOrWhiteSpace(screenshotDataUrl) ? null : screenshotDataUrl,
                string.IsNullOrWhiteSpace(localFilePath) ? null : localFilePath);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox history entry skipped: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static DateTimeOffset ReadReceivedAt(JsonElement root)
    {
        var receivedAtText = FirstNonEmpty(ReadString(root, "receivedAtUtc"), ReadString(root, "createdAt"));
        if (DateTimeOffset.TryParse(receivedAtText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var receivedAt))
        {
            return receivedAt.ToLocalTime();
        }

        if (root.TryGetProperty("createdAt", out var createdAt) &&
            createdAt.ValueKind == JsonValueKind.Number &&
            createdAt.TryGetInt64(out var createdAtMillis))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(createdAtMillis).ToLocalTime();
        }

        return DateTimeOffset.Now;
    }

    private static string? ReadString(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private void UpdateUrlActions()
    {
        if (!string.IsNullOrWhiteSpace(_lastUrl))
        {
            OpenLinkButton.Visibility = Visibility.Visible;
            UrlHintText.Visibility = Visibility.Visible;
            UrlHintText.Text = $"最近链接：{_lastUrl}";
            return;
        }

        OpenLinkButton.Visibility = Visibility.Collapsed;
        UrlHintText.Visibility = Visibility.Collapsed;
    }

    private void PositionBottomRight()
    {
        var area = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Left = area.Right / GetDpiScaleX() - ActualWidth - 18;
        Top = area.Bottom / GetDpiScaleY() - ActualHeight - 18;
    }

    private double GetDpiScaleX()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
    }

    private double GetDpiScaleY()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastUrl))
        {
            return;
        }

        var url = _lastUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? _lastUrl : $"https://{_lastUrl}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        ClipboardService.SetText(_conversationText.ToString());
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HostAssets.MobileInboxPath)!);
            File.WriteAllText(HostAssets.MobileInboxPath, string.Empty);
            LoadInboxHistory();
            MetaText.Text = "手机聊天记录已清理";
            HostAssets.AppendLog("Mobile inbox history cleared by user.");
        }
        catch (Exception ex)
        {
            MetaText.Text = "清理失败，详情见日志";
            HostAssets.AppendLog($"Mobile inbox history clear failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void InitializeAutoCloseTimer()
    {
        if (AutoCloseCheckBox == null) return;
        var settings = AppSettingsStore.Load();
        AutoCloseCheckBox.IsChecked = settings.AutoCloseToastEnabled;

        if (settings.AutoCloseToastEnabled)
        {
            ResetAutoCloseTimer();
        }
    }

    private void ResetAutoCloseTimer()
    {
        if (_autoCloseTimer == null)
        {
            _autoCloseTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _autoCloseTimer.Tick += (s, e) => Close();
        }
        else
        {
            _autoCloseTimer.Stop();
        }
        _autoCloseTimer.Start();
    }

    private void StopAutoCloseTimer()
    {
        _autoCloseTimer?.Stop();
    }

    private void AutoCloseCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        settings.AutoCloseToastEnabled = true;
        AppSettingsStore.Save(settings);

        ResetAutoCloseTimer();
    }

    private void AutoCloseCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsStore.Load();
        settings.AutoCloseToastEnabled = false;
        AppSettingsStore.Save(settings);

        StopAutoCloseTimer();
    }

    private static string? ExtractUrl(string text)
    {
        var match = UrlRegex.Match(text ?? string.Empty);
        return match.Success ? match.Value.TrimEnd('.', ',', ';', ')', ']', '}') : null;
    }

    private sealed record MobileInboxEntry(
        string Title,
        string Text,
        string SourceDeviceName,
        DateTimeOffset ReceivedAt,
        string? ScreenshotDataUrl,
        string? LocalFilePath);
}
