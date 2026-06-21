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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    private const byte VK_LWIN = 0x5B;
    private const byte VK_H = 0x48;
    private const byte KEYEVENTF_KEYUP = 0x0002;

    private readonly StringBuilder _conversationText = new();
    private string? _lastUrl;
    private DateTimeOffset? _lastMessageTime;

    public MobileMessageToastWindow()
    {
        InitializeComponent();
        LoadInboxHistory();

        Loaded += (_, _) =>
        {
            PositionBottomRight();
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
        _lastMessageTime = null;

        var entries = ReadInboxHistory();
        var lastMobile = entries.FindLast(e => e.SourceDeviceName != "\u6211(\u7535\u8111)" && e.SourceDeviceName != "desktop");
        TitleText.Text = lastMobile != null ? lastMobile.SourceDeviceName : "\u624b\u673a\u804a\u5929"; // "手机聊天"
        
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
            if (sourceLabel != "\u6211(\u7535\u8111)" && sourceLabel != "desktop")
            {
                TitleText.Text = sourceLabel;
            }
        }

        if (_lastMessageTime == null || (receivedAt - _lastMessageTime.Value).Duration() > TimeSpan.FromMinutes(3))
        {
            _lastMessageTime = receivedAt;
            AddChatTimeDivider(receivedAt);
        }

        AddMessageBubble(messageText, sourceLabel, receivedAt, screenshotDataUrl, screenshotFilePath);
        UpdateUrlActions();
        Dispatcher.InvokeAsync(() => MessageScrollViewer.ScrollToEnd());
    }

    private void AddMessageBubble(string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl, string? screenshotFilePath)
    {
        bool isSelf = sourceDeviceId == "\u6211(\u7535\u8111)" || sourceDeviceId == "desktop";
        var container = new Border
        {
            Margin = isSelf ? new Thickness(50, 0, 0, 10) : new Thickness(0, 0, 50, 10),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = isSelf ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left,
            BorderThickness = isSelf ? new Thickness(0) : new Thickness(1),
            BorderBrush = isSelf ? null : new SolidColorBrush(System.Windows.Media.Color.FromArgb(36, 56, 189, 248))
        };

        if (isSelf)
        {
            container.SetResourceReference(Border.BackgroundProperty, "BrushAccent");
        }
        else
        {
            container.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 31, 41, 55));
        }

        var panel = new StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBox
        {
            Text = messageText,
            Margin = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = isSelf ? System.Windows.Media.Brushes.White : new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)),
            FontSize = 14,
            Padding = new Thickness(0),
            HorizontalAlignment = isSelf ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left
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
                Text = "\u622a\u56fe\u9884\u89c8\u52a0\u8f7d\u5931\u8d25\uff0c\u8be6\u60c5\u8bf7\u67e5\u770b host.log\u3002",
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
                Text = $"\u5df2\u4fdd\u5b58\uff1a{screenshot.FilePath}",
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
            pathBox.ToolTip = "\u53cc\u51fb\u6253\u5f00\u6587\u4ef6";
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
            HostAssets.AppendLog("Mobile inbox history cleared by user.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox history clear failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void VoiceButton_Click(object sender, RoutedEventArgs e)
    {
        InputTextBox.Focus();
        keybd_event(VK_LWIN, 0, 0, 0);
        keybd_event(VK_H, 0, 0, 0);
        keybd_event(VK_H, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var photoItem = new System.Windows.Controls.MenuItem { Header = "选择照片" };
        photoItem.Click += PhotoItem_Click;
        var fileItem = new System.Windows.Controls.MenuItem { Header = "选择文件" };
        fileItem.Click += FileItem_Click;
        menu.Items.Add(photoItem);
        menu.Items.Add(fileItem);
        
        menu.PlacementTarget = sender as System.Windows.Controls.Button;
        menu.IsOpen = true;
    }

    private async void PhotoItem_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new SaveFileDialog // 虽然叫SaveFileDialog，但是在WinForms中实际我们更常用OpenFileDialog。等等！上面的 code 里 11 行导入了 using System.Windows.Forms; 我们得用 OpenFileDialog。
        {
            // 在 WPF 里由于导入了 System.Windows.Forms，为了避免和 WPF 自己的 OpenFileDialog 冲突，
            // 既然 direct using System.Windows.Forms; 存在，且 LoadInboxHistory 等里面在另存为时用了 SaveFileDialog，
            // 我们可以直接使用 System.Windows.Forms.OpenFileDialog。
        };
        
        using var ofd = new System.Windows.Forms.OpenFileDialog
        {
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.gif;*.bmp)|*.jpg;*.jpeg;*.png;*.gif;*.bmp|所有文件 (*.*)|*.*",
            Title = "选择要发送的照片"
        };
        if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await SendFileOrPhotoToMobileAsync(ofd.FileName, isPhoto: true);
        }
    }

    private async void FileItem_Click(object sender, RoutedEventArgs e)
    {
        using var ofd = new System.Windows.Forms.OpenFileDialog
        {
            Filter = "所有文件 (*.*)|*.*",
            Title = "选择要发送的文件"
        };
        if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await SendFileOrPhotoToMobileAsync(ofd.FileName, isPhoto: false);
        }
    }

    private async Task SendFileOrPhotoToMobileAsync(string filePath, bool isPhoto)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        try
        {
            var fileName = Path.GetFileName(filePath);
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            var base64Data = Convert.ToBase64String(fileBytes);
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var mimeType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
            var dataUrl = $"data:{mimeType};base64,{base64Data}";

            var kind = isPhoto ? "photo" : "file";
            var sent = await SendMessageToMobileAsync(message: fileName, kind: kind, dataUrl: dataUrl);
            if (sent)
            {
                var receivedAt = DateTimeOffset.Now;
                if (isPhoto)
                {
                    AddMessageBubble($"[照片] {fileName}", "\u6211(\u7535\u8111)", receivedAt, dataUrl, filePath);
                }
                else
                {
                    AddMessageBubble($"[文件] {fileName}", "\u6211(\u7535\u8111)", receivedAt, null, filePath);
                }

                try
                {
                    var record = new
                    {
                        messageId = Guid.NewGuid().ToString(),
                        sourceDeviceId = "desktop",
                        sourceDeviceName = "\u6211(\u7535\u8111)",
                        kind = kind,
                        title = "\u7535\u8111\u53d1\u5f80\u624b\u673a\u7684" + (isPhoto ? "\u7167\u7247" : "\u6587\u4ef6"),
                        text = fileName,
                        payload = "",
                        screenshotDataUrl = isPhoto ? dataUrl : (string?)null,
                        localFilePath = filePath,
                        receivedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                        createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    File.AppendAllText(
                        HostAssets.MobileInboxPath,
                        JsonSerializer.Serialize(record) + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Failed to append sent chat photo/file history: {ex.Message}");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("\u53d1\u9001\u5931\u8d25\uff0c\u624b\u673a\u53ef\u80fd\u672a\u5904\u4e8e\u5c40\u57df\u7f51\u76f4\u8fde\u72b6\u6001\u3002", "\u53d1\u9001\u5931\u8d25", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to process file send: {ex.Message}");
            System.Windows.MessageBox.Show($"发送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await TriggerSendMessageAsync();
    }

    private async void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            await TriggerSendMessageAsync();
        }
    }

    private async Task TriggerSendMessageAsync()
    {
        var text = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        InputTextBox.Text = string.Empty;
        var sent = await SendMessageToMobileAsync(text);
        if (sent)
        {
            var receivedAt = DateTimeOffset.Now;
            AddMessageBubble(text, "\u6211(\u7535\u8111)", receivedAt, null, null);
            
            try
            {
                var record = new
                {
                    messageId = Guid.NewGuid().ToString(),
                    sourceDeviceId = "desktop",
                    sourceDeviceName = "\u6211(\u7535\u8111)",
                    kind = "text",
                    title = "\u7535\u8111\u53d1\u5f80\u624b\u673a\u7684\u6d88\u606f",
                    text = text,
                    payload = "",
                    screenshotDataUrl = (string?)null,
                    localFilePath = (string?)null,
                    receivedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                File.AppendAllText(
                    HostAssets.MobileInboxPath,
                    JsonSerializer.Serialize(record) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Failed to append sent chat history: {ex.Message}");
            }
        }
        else
        {
            System.Windows.MessageBox.Show("\u6d88\u606f\u53d1\u9001\u5931\u8d25\uff0c\u624b\u673a\u53ef\u80fd\u672a\u5904\u4e8e\u5c40\u57df\u7f51\u76f4\u8fde\u72b6\u6001\u3002", "\u53d1\u9001\u5931\u8d25", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task<bool> SendMessageToMobileAsync(string message, string kind = "text", string? dataUrl = null)
    {
        var mobileIp = LanDiscoveryService.LastKnownMobileIp;
        if (mobileIp == null)
        {
            HostAssets.AppendLog("Send message skipped: mobile IP is not discovered.");
            return false;
        }

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            
            object payloadObj;
            if (kind == "photo")
            {
                payloadObj = new
                {
                    title = "YanziChat",
                    message = message,
                    kind = kind,
                    screenshotDataUrl = dataUrl
                };
            }
            else if (kind == "file")
            {
                payloadObj = new
                {
                    title = "YanziChat",
                    message = message,
                    kind = kind,
                    fileDataUrl = dataUrl,
                    fileName = message
                };
            }
            else
            {
                payloadObj = new
                {
                    title = "YanziChat",
                    message = message,
                    kind = kind
                };
            }

            var payload = JsonSerializer.Serialize(payloadObj);
            var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"http://{mobileIp}:42981/", content);
            response.EnsureSuccessStatusCode();
            HostAssets.AppendLog($"Message sent directly to mobile via LAN: IP={mobileIp}, content={message}, kind={kind}");
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to send message to mobile: {ex.Message}");
            return false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatChatTime(DateTimeOffset time)
    {
        var localTime = time.ToLocalTime();
        var now = DateTimeOffset.Now.ToLocalTime();
        if (localTime.Date == now.Date)
        {
            return localTime.ToString("HH:mm");
        }
        if (localTime.Date == now.Date.AddDays(-1))
        {
            return "昨天 " + localTime.ToString("HH:mm");
        }
        if (localTime.Year == now.Year)
        {
            return localTime.ToString("MM-dd HH:mm");
        }
        return localTime.ToString("yyyy-MM-dd HH:mm");
    }

    private void AddChatTimeDivider(DateTimeOffset receivedAt)
    {
        var border = new Border
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 10),
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 255, 255, 255))
        };

        var textBlock = new TextBlock
        {
            Text = FormatChatTime(receivedAt),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(156, 163, 175)),
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };

        border.Child = textBlock;
        MessageStack.Children.Add(border);
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
