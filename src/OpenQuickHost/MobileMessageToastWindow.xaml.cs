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
using OpenQuickHost.Sync;

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
    private string? _replyDeviceId;
    private string? _preferredTargetDeviceId;
    private bool _sending;
    private string _sendStatus = "";
    private string _sendError = "";
    private string? _lastCloudMessageId;
    private MainWindow? _receiptOwner;
    private sealed record ChatTarget(string? Id, string Label, string Detail = "") { public override string ToString() => Label; }
    private static string DeviceLabel(string name, string platform)
    {
        var label = MobileDeviceNameNormalizer.Normalize(name, platform == "desktop" ? "电脑" : "手机");
        if (label.StartsWith("Windows · ", StringComparison.OrdinalIgnoreCase)) label = label[10..].Trim();
        return label.Length > 28 ? label[..28] + "…" : label;
    }
    private static string DeviceTime(string? value) => DateTimeOffset.TryParse(value, out var time)
        ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未记录";
    private async Task LoadTargetsAsync()
    {
        var selected = _preferredTargetDeviceId ?? (TargetDevicePicker.SelectedItem as ChatTarget)?.Id;
        var targets = new List<ChatTarget> { new(null, "所有设备", "发送账号消息，所有设备分别接收") };
        var own = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        try {
            var cloud = (System.Windows.Application.Current.MainWindow as MainWindow)?.CloudSyncClient;
            if (cloud?.HasCredential != true) throw new InvalidOperationException("请登录账号查看设备");
            foreach (var peer in await cloud.ListPeerDevicesAsync())
                if (peer.DeviceId != own)
                    targets.Add(new(peer.DeviceId, DeviceLabel(peer.DisplayName, peer.Platform),
                        (peer.Online ? "在线" : "离线") + " · " + (peer.Platform == "desktop" ? "电脑" : peer.Platform == "android" ? "手机" : peer.Platform)
                        + "\n最近活动：" + DeviceTime(peer.LastSeenAt) + "\n首次登记：" + DeviceTime(peer.CreatedAt)
                        + "\n网络地区：" + (string.IsNullOrWhiteSpace(peer.LastLocation) ? "未记录" : peer.LastLocation)));
        } catch (Exception error) {
            HostAssets.AppendLog("Device target list unavailable: " + error.Message);
            foreach (var peer in YanziPeerRegistry.List().Where(x => x.DeviceId != own))
                targets.Add(new(peer.DeviceId, DeviceLabel(peer.DisplayName, peer.Platform), "本地记录 · 云端状态未确认\n最近直连：" + DeviceTime(peer.VerifiedAt.ToString("O"))));
            _deviceListWarning = "云端设备列表暂不可用，当前仅显示本地记录。";
        }
        foreach (var group in targets.Where(x => x.Id != null).GroupBy(x => x.Label).Where(x => x.Count() > 1).ToArray())
            foreach (var item in group.ToArray()) targets[targets.IndexOf(item)] = item with { Label = item.Label + " · " + item.Id![Math.Max(0, item.Id!.Length - 4)..] };
        targets = targets.Take(1).Concat(targets.Skip(1).OrderByDescending(x => x.Detail.StartsWith("在线")).ThenBy(x => x.Label)).ToList();
        TargetDevicePicker.Items.Clear(); foreach (var target in targets) TargetDevicePicker.Items.Add(target);
        TargetDevicePicker.SelectedItem = targets.FirstOrDefault(x => x.Id == selected) ?? targets[0];
        TitleText.Text = ((ChatTarget)TargetDevicePicker.SelectedItem).Label;
        _preferredTargetDeviceId = null;
    }
    private string? _deviceListWarning;
    private async void DeviceSwitcher_Click(object sender, RoutedEventArgs e)
    {
        _deviceListWarning = null;
        await LoadTargetsAsync();
        var menu = new System.Windows.Controls.ContextMenu { MaxHeight = 520, MinWidth = 330,
            PlacementTarget = (System.Windows.Controls.Button)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        if (_deviceListWarning != null) menu.Items.Add(new System.Windows.Controls.MenuItem { Header = _deviceListWarning, IsEnabled = false });
        foreach (var target in TargetDevicePicker.Items.Cast<ChatTarget>())
        {
            var row = new Grid { Width = 340, Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = target.Label, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = target.Detail, FontSize = 11, Opacity = .72, Margin = new Thickness(0, 4, 8, 0), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            var item = new System.Windows.Controls.MenuItem { Header = row,
                ToolTip = target.Id == null ? target.Detail : target.Label + "\n设备标识：" + target.Id,
                IsCheckable = true, IsChecked = (TargetDevicePicker.SelectedItem as ChatTarget)?.Id == target.Id };
            item.Click += (_, _) => { TargetDevicePicker.SelectedItem = target; TitleText.Text = target.Label; };
            if (target.Id != null)
            {
                var remove = new System.Windows.Controls.Button { Content = "删除", Padding = new Thickness(8, 4, 8, 4),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(remove, 1); row.Children.Add(remove);
                remove.Click += async (_, args) => {
                    args.Handled = true; menu.IsOpen = false;
                    if (System.Windows.MessageBox.Show(this, "删除“" + target.Label + "”的设备登记并停用该设备的消息和直连授权？\n设备上的文件不会被删除。", "删除设备", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    try {
                        var cloud = (System.Windows.Application.Current.MainWindow as MainWindow)?.CloudSyncClient ?? throw new InvalidOperationException("请先登录账号");
                        await cloud.RemovePeerDeviceAsync(target.Id);
                        foreach (var pair in YanziLanPairing.List().Where(x => x.DeviceId == target.Id)) YanziLanPairing.Revoke(pair.PairId);
                        YanziPeerRegistry.Remove(target.Id);
                        await LoadTargetsAsync(); SendStatusText.Text = "已删除设备“" + target.Label + "”。";
                    } catch (Exception error) { SendStatusText.Text = "删除未完成：" + error.Message; }
                };
            }
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private async void UpdateReceipt(string id, string status)
    {
        if (id != _lastCloudMessageId) return;
        SendStatusText.Text = status == "unknown" ? "目标设备结果待确认，请先在该设备检查，避免重复发送。" :
            status is "cancelled" or "expired" ? "请求已取消或过期。" : status is "acked" or "completed" ? "已有设备接收，其余设备会继续同步" : status == "failed" ? "有设备接收失败，请检查该设备" : "已交云端，等待各设备接收";
        try {
            var cloud = _receiptOwner?.CloudSyncClient;
            if (cloud == null) return;
            var detail = await cloud.GetDeviceMessageAsync(id);
            if (id != _lastCloudMessageId || detail?.Receipts.Count is not > 0) return;
            string Label(string state) => state switch {"acked" or "completed" => "已接收", "failed" => "失败", "unknown" => "结果待确认", _ => state};
            SendStatusText.Text = string.Join("；", detail.Receipts.Take(4).Select(x => (string.IsNullOrEmpty(x.DisplayName) ? x.DeviceId : x.DisplayName) + "：" + Label(x.Status))) +
                (detail.Receipts.Count > 4 ? $"；另有 {detail.Receipts.Count - 4} 台回执" : "");
        } catch (Exception error) { HostAssets.AppendLog("Device receipts refresh deferred: " + error.GetType().Name); }
    }

    public MobileMessageToastWindow()
    {
        InitializeComponent();
        LoadInboxHistory();
        _receiptOwner = System.Windows.Application.Current.MainWindow as MainWindow;
        if (_receiptOwner != null) _receiptOwner.MobileReceiptReceived += UpdateReceipt;
        Closed += (_, _) => { if (_receiptOwner != null) _receiptOwner.MobileReceiptReceived -= UpdateReceipt; };

        Loaded += async (_, _) =>
        {
            PositionBottomRight();
            await LoadTargetsAsync();
        };
    }

    public MobileMessageToastWindow(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl = null, string? screenshotFilePath = null)
    {
        InitializeComponent();
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "手机发来消息" : title.Trim();
        AppendMessageCore(title, messageText, sourceDeviceId, receivedAt, screenshotDataUrl, screenshotFilePath, updateHeader: true);

        Loaded += async (_, _) =>
        {
            PositionBottomRight();
            await LoadTargetsAsync();
        };
    }

    public void PreferTargetDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        _preferredTargetDeviceId = deviceId;
        var target = TargetDevicePicker.Items.Cast<ChatTarget>()
            .FirstOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.Ordinal));
        if (target != null)
        {
            TargetDevicePicker.SelectedItem = target;
            TitleText.Text = target.Label;
            _preferredTargetDeviceId = null;
        }
    }
    public void AppendMessage(string title, string messageText, string sourceDeviceId, DateTimeOffset receivedAt, string? screenshotDataUrl = null, string? screenshotFilePath = null, string? replyDeviceId = null)
    {
        if (!string.IsNullOrWhiteSpace(replyDeviceId)) _replyDeviceId = replyDeviceId;
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
        _replyDeviceId = lastMobile?.SourceDeviceId;
        TitleText.Text = (TargetDevicePicker.SelectedItem as ChatTarget)?.Label ?? "所有设备";
        
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
                TitleText.Text = (TargetDevicePicker.SelectedItem as ChatTarget)?.Label ?? "所有设备";
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

        var isFileAttachment = !string.IsNullOrWhiteSpace(screenshotFilePath) && File.Exists(screenshotFilePath)
            && !new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico" }.Contains(Path.GetExtension(screenshotFilePath).ToLowerInvariant());
        var screenshot = isFileAttachment ? (Image: (System.Windows.Controls.Image?)null, FilePath: screenshotFilePath)
            : TryCreateScreenshotImage(screenshotDataUrl, screenshotFilePath, receivedAt);
        if (screenshot.Image != null)
        {
            panel.Children.Add(screenshot.Image);
        }
        else if (!isFileAttachment && (!string.IsNullOrWhiteSpace(screenshotDataUrl) || !string.IsNullOrWhiteSpace(screenshotFilePath)))
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
                string.IsNullOrWhiteSpace(localFilePath) ? null : localFilePath,
                ReadString(root, "sourceDeviceId"));
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
        if (_sending) return;

        try
        {
            var fileName = Path.GetFileName(filePath);
            _sending = true; SendButton.IsEnabled = false; AttachButton.IsEnabled = false;
            SendStatusText.Text = "正在上传附件…";
            if (new FileInfo(filePath).Length > 30L * 1024 * 1024) throw new IOException("附件不能超过 30 MB。");
            string? dataUrl = null;

            var kind = isPhoto ? "photo" : "file";
            var sent = await SendMessageToMobileAsync(message: fileName, kind: kind, dataUrl: dataUrl, filePath: filePath);
            SendStatusText.Text = sent ? _sendStatus : _sendError;
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
            SendStatusText.Text = _sendError;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to process file send: {ex.Message}");
            System.Windows.MessageBox.Show($"发送失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _sending = false; SendButton.IsEnabled = true; AttachButton.IsEnabled = true; }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await TriggerSendMessageAsync();
    }

    private async void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter sends, while Shift+Enter inserts a newline into the multiline editor.
        if (e.Key == System.Windows.Input.Key.Enter &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await TriggerSendMessageAsync();
        }
    }

    private async Task TriggerSendMessageAsync()
    {
        var text = InputTextBox.Text;
        if (string.IsNullOrWhiteSpace(text) || _sending) return;

        _sending = true;
        SendButton.IsEnabled = false;
        AttachButton.IsEnabled = false;
        SendStatusText.Text = "正在发送…";
        bool sent;
        try { sent = await SendMessageToMobileAsync(text); }
        finally { _sending = false; SendButton.IsEnabled = true; AttachButton.IsEnabled = true; }
        SendStatusText.Text = sent ? _sendStatus : _sendError;
        if (sent)
        {
            if (InputTextBox.Text == text) InputTextBox.Clear();
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
            SendStatusText.Text = _sendError;
        }
    }

    private async Task<bool> SendMessageToMobileAsync(string message, string kind = "text", string? dataUrl = null, string? filePath = null)
    {
        _sendError = "";
        var transferId = Guid.NewGuid().ToString("N");
        bool lanReceived = false;
        var syncClient = (System.Windows.Application.Current.MainWindow as MainWindow)?.CloudSyncClient;
        DesktopChatJob? queued = null;
        if (syncClient?.CurrentUserId is { } account)
        {
            try {
                queued = DesktopChatOutbox.Enqueue(account, DeviceIdentityStore.GetOrCreateDesktopDeviceId(), transferId, kind, message, filePath, (TargetDevicePicker.SelectedItem as ChatTarget)?.Id);
                filePath = queued.FilePath;
            } catch (Exception error) { _sendError = "保存发件消息失败：" + error.Message; return false; }
        }
        var selectedTarget = queued?.TargetDeviceId ?? (TargetDevicePicker.SelectedItem as ChatTarget)?.Id;
        var peer = YanziPeerRegistry.Resolve(selectedTarget);
        var mobileIp = peer != null ? System.Net.IPAddress.Parse(peer.Address) :
            YanziPeerRegistry.List().Count == 0 ? LanDiscoveryService.LastKnownMobileIp : null;
        var mobilePort = peer?.Port ?? LanDiscoveryService.LastKnownMobileNotificationPort;
        if (mobileIp != null)
        {
        try
        {
            if (kind is "file" or "photo")
            {
                if (string.IsNullOrEmpty(filePath)) throw new IOException("未选择附件。");
                await YanziLanMobileTransfer.SendAsync(mobileIp, mobilePort,
                    AppSettingsStore.Load().AgentApiToken, filePath, kind, transferId);
                _sendStatus = "已通过局域网发送，手机已确认接收";
                lanReceived = true;
            }
            if (!lanReceived)
            {
            using var handler = new SecureLanHttpHandler();
            using var client = new System.Net.Http.HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(3);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AppSettingsStore.Load().AgentApiToken);
            
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
                    clientMessageId = transferId,
                    kind = kind
                };
            }

            var payload = JsonSerializer.Serialize(payloadObj);
            var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"http://{mobileIp}:{mobilePort}/", content);
            response.EnsureSuccessStatusCode();
            HostAssets.AppendLog($"Message sent directly to mobile via LAN: IP={mobileIp}, content={message}, kind={kind}");
            _sendStatus = "已通过直连发送";
            lanReceived = true;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Chat LAN unavailable; trying cloud: {ex.Message}");
        }
        }
        try
        {
            var cloud = (System.Windows.Application.Current.MainWindow as MainWindow)?.CloudSyncClient;
            if (cloud == null || !cloud.HasCredential)
            {
                _sendError = "请先在电脑端登录同步账号，再通过公网发送。";
                return lanReceived;
            }
            queued ??= DesktopChatOutbox.Enqueue(cloud.CurrentUserId!, DeviceIdentityStore.GetOrCreateDesktopDeviceId(), transferId, kind, message, filePath, selectedTarget);
            var id = await cloud.DeliverChatJobAsync(queued);
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("云端未返回消息编号。");
            _sendStatus = "已交云端，等待手机接收";
            _lastCloudMessageId = id;
            var receipt = _receiptOwner?.GetMobileReceipt(id);
            if (receipt == "acked" || receipt == "completed") _sendStatus = "已有设备接收，其余设备会继续同步";
            HostAssets.AppendLog($"Chat message queued by cloud: messageId={id}.");
            return true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Chat cloud send failed: {ex.GetType().Name}: {ex.Message}");
            _sendError = "公网发送失败，请检查网络和同步账号后重试；消息内容已保留。";
            if (lanReceived || queued != null) { _sendStatus = lanReceived ? "局域网已接收，账号同步待重试" : "已保存到发件队列，联网后自动发送"; return true; }
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
        string? LocalFilePath,
        string? SourceDeviceId);
}
