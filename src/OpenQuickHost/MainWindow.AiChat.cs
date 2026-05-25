using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenQuickHost;

/// <summary>
/// AI 对话功能模块 - 完整重构版本
/// 支持话题管理、附件上传、消息操作等功能
/// </summary>
public partial class MainWindow
{
    // ==================== 常量 ====================
    private const string SearchScopeAi = "ai";
    private const long MaxAttachmentSize = 10 * 1024 * 1024; // 10MB
    private const int MaxAttachmentInlineChars = 60000;
    private const int MaxAttachmentPreviewChars = 12000;

    // ==================== 字段 ====================
    private readonly ObservableCollection<AiChatMessage> _aiChatMessages = [];
    private readonly ObservableCollection<AiChatTopic> _aiChatTopics = [];
    private readonly ObservableCollection<AiChatAttachment> _aiChatAttachments = [];
    private AiChatTopic? _selectedAiChatTopic;
    private readonly HttpClient _aiHttpClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private string _aiChatInputText = string.Empty;
    private string _aiChatStatusText = "选择或新建话题开始对话";
    private bool _isAiChatRequestInFlight;
    private string _lastNonAiSearchScopeKey = SearchScopeAll;

    // ==================== 属性 ====================
    public ObservableCollection<AiChatMessage> AiChatMessages => _aiChatMessages;
    public ObservableCollection<AiChatTopic> AiChatTopics => _aiChatTopics;
    public ObservableCollection<AiChatAttachment> AiChatAttachments => _aiChatAttachments;

    public AiChatTopic? SelectedAiChatTopic
    {
        get => _selectedAiChatTopic;
        set
        {
            if (_selectedAiChatTopic == value)
            {
                return;
            }

            if (_selectedAiChatTopic != null)
            {
                _selectedAiChatTopic.IsSelected = false;
            }

            _selectedAiChatTopic = value;
            if (_selectedAiChatTopic != null)
            {
                _selectedAiChatTopic.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTopic));
            OnPropertyChanged(nameof(NoTopicVisibility));
            LoadTopicMessages();
        }
    }

    public bool HasSelectedTopic => _selectedAiChatTopic != null;
    public Visibility NoTopicVisibility => _selectedAiChatTopic == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AttachmentsVisibility => _aiChatAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsAiChatMode => string.Equals(SelectedSearchScope?.Key, SearchScopeAi, StringComparison.OrdinalIgnoreCase);
    public Visibility AiChatVisibility => IsAiChatMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NormalLauncherVisibility => IsAiChatMode ? Visibility.Collapsed : Visibility.Visible;

    public string AiChatInputText
    {
        get => _aiChatInputText;
        set
        {
            if (value == _aiChatInputText)
            {
                return;
            }

            _aiChatInputText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AiChatInputPlaceholderVisibility));
        }
    }

    public Visibility AiChatInputPlaceholderVisibility => string.IsNullOrWhiteSpace(AiChatInputText)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string AiChatStatusText
    {
        get => _aiChatStatusText;
        set
        {
            if (value == _aiChatStatusText)
            {
                return;
            }

            _aiChatStatusText = value;
            OnPropertyChanged();
        }
    }

    public string AiChatModelDisplayText => string.IsNullOrWhiteSpace(_appSettings.AiModel)
        ? "AI 未配置"
        : _appSettings.AiModel;

    // ==================== 事件处理 ====================

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsAiChatMode && e.Key == Key.Escape)
        {
            ExitAiChatMode();
            e.Handled = true;
            return;
        }

        if (TryHandleSearchScopeTabNavigation(e))
        {
            return;
        }

        Window_KeyDown(sender, e);
    }
    
    private void AiChatInputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitAiChatMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            TryHandleSearchScopeTabNavigation(e);
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            SubmitAiChatMessage();
            e.Handled = true;
        }
    }

    private void AiChatSendButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitAiChatMessage();
    }

    private void AiChatSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SuppressAutoHideFor(TimeSpan.FromSeconds(2));

        if (System.Windows.Application.Current is App app)
        {
            app.OpenSettingsWindow("ai");
        }
    }

    private void AiChatNewTopicButton_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTopic();
    }

    private void AiChatRenameTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAiChatTopic != null)
        {
            RenameTopic(SelectedAiChatTopic);
        }
    }

    private void AiChatDeleteTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAiChatTopic != null)
        {
            DeleteTopic(SelectedAiChatTopic);
        }
    }

    private void AiChatTopicItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatTopic topic })
        {
            SelectTopic(topic);
        }
    }

    private void AiChatTopicItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatTopic topic } element)
        {
            ShowTopicContextMenu(topic, element);
            e.Handled = true;
        }
    }

    private void AiChatMessageBubble_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatMessage message } element)
        {
            ShowMessageContextMenu(message, element);
            e.Handled = true;
        }
    }

    private async void AiChatAddAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        await AddAttachmentsAsync();
    }

    private void AiChatRemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatAttachment attachment })
        {
            _aiChatAttachments.Remove(attachment);
        }
    }

    private async void AiChatInputArea_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            await AddAttachmentsFromPathsAsync(files);
        }
    }

    private void AiChatInputArea_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void AiChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(VisibleCountText));
        _ = Dispatcher.BeginInvoke(() => AiChatScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void AiChatAttachments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AttachmentsVisibility));
    }

    // ==================== 话题管理 ====================
    
    private void CreateNewTopic()
    {
        var topic = new AiChatTopic("新对话");  // 临时标题，会在第一条消息后更新
        _aiChatTopics.Insert(0, topic);
        SelectTopic(topic);
        SaveTopicsToStorage();
    }

    private void SelectTopic(AiChatTopic topic)
    {
        SelectedAiChatTopic = topic;
        AiChatStatusText = IsAiConfigured(_appSettings) ? "继续对话" : "请先配置 AI";
        FocusAiInput();
    }

    private void LoadTopicMessages()
    {
        _aiChatMessages.Clear();
        if (_selectedAiChatTopic != null)
        {
            foreach (var message in _selectedAiChatTopic.Messages)
            {
                _aiChatMessages.Add(message);
            }
        }
    }

    private void DeleteTopic(AiChatTopic topic)
    {
        _aiChatTopics.Remove(topic);
        if (_selectedAiChatTopic == topic)
        {
            SelectedAiChatTopic = _aiChatTopics.FirstOrDefault();
        }
        SaveTopicsToStorage();
    }

    private void RenameTopic(AiChatTopic topic)
    {
        var dialog = new Window
        {
            Title = "重命名话题",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = topic.Title,
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 15)
        };
        textBox.SelectAll();

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var okButton = new System.Windows.Controls.Button
        {
            Content = "确定",
            Width = 80,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0)
        };
        okButton.Click += (_, _) =>
        {
            var newTitle = textBox.Text.Trim();
            if (!string.IsNullOrEmpty(newTitle))
            {
                topic.Title = newTitle;
                SaveTopicsToStorage();
            }
            dialog.Close();
        };

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "取消",
            Width = 80,
            Height = 32
        };
        cancelButton.Click += (_, _) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stack.Children.Add(textBox);
        stack.Children.Add(buttonPanel);
        dialog.Content = stack;

        textBox.Focus();
        dialog.ShowDialog();
    }

    private void ShowTopicContextMenu(AiChatTopic topic, FrameworkElement placementTarget)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        var renameItem = new System.Windows.Controls.MenuItem { Header = "重命名" };
        renameItem.Click += (_, _) => RenameTopic(topic);
        menu.Items.Add(renameItem);

        var deleteItem = new System.Windows.Controls.MenuItem { Header = "删除" };
        deleteItem.Click += (_, _) => DeleteTopic(topic);
        menu.Items.Add(deleteItem);

        menu.IsOpen = true;
    }

    // ==================== 附件管理 ====================
    
    private async Task AddAttachmentsAsync()
    {
        SuppressAutoHideFor(TimeSpan.FromSeconds(5));

        var filePaths = await ShowAttachmentPickerAsync();
        if (filePaths is { Length: > 0 })
        {
            await AddAttachmentsFromPathsAsync(filePaths);
        }

        await Dispatcher.InvokeAsync(() => FocusAiInput(), DispatcherPriority.Background);
    }

    private async Task AddAttachmentsFromPathsAsync(IEnumerable<string> filePaths)
    {
        var existingPaths = _aiChatAttachments
            .Select(static attachment => attachment.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preparation = await Task.Run(() =>
        {
            var attachments = new List<AiChatAttachment>();
            var oversizeFiles = new List<string>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    if (existingPaths.Contains(filePath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > MaxAttachmentSize)
                    {
                        oversizeFiles.Add(fileInfo.Name);
                        continue;
                    }

                    attachments.Add(new AiChatAttachment(filePath));
                    existingPaths.Add(filePath);
                }
                catch (Exception ex)
                {
                    HostAssets.AppendLog($"Add attachment failed: {ex.Message}");
                }
            }

            return (attachments, oversizeFiles);
        });

        foreach (var attachment in preparation.attachments)
        {
            _aiChatAttachments.Add(attachment);
        }

        if (preparation.oversizeFiles.Count > 0)
        {
            AiChatStatusText = preparation.oversizeFiles.Count == 1
                ? $"文件 {preparation.oversizeFiles[0]} 超过 10MB 限制"
                : $"{preparation.oversizeFiles.Count} 个文件超过 10MB 限制";
        }
    }

    private static Task<string[]?> ShowAttachmentPickerAsync()
    {
        var tcs = new TaskCompletionSource<string[]?>();
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Multiselect = true,
                    Filter = "所有文件|*.*|文本文件|*.txt;*.md;*.json;*.xml;*.cs;*.js;*.py|图片文件|*.png;*.jpg;*.jpeg;*.gif"
                };

                var result = dialog.ShowDialog();
                tcs.SetResult(result == true ? dialog.FileNames : null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    // ==================== 消息操作 ====================
    
    private void ShowMessageContextMenu(AiChatMessage message, FrameworkElement placementTarget)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        var copyItem = new System.Windows.Controls.MenuItem { Header = "复制内容" };
        copyItem.Click += (_, _) => CopyMessage(message);
        menu.Items.Add(copyItem);

        if (message.IsUser)
        {
            var resendItem = new System.Windows.Controls.MenuItem { Header = "重新发送" };
            resendItem.Click += (_, _) => ResendMessage(message);
            menu.Items.Add(resendItem);
        }

        menu.IsOpen = true;
    }

    private void CopyMessage(AiChatMessage message)
    {
        try
        {
            ClipboardService.SetText(message.Text);
            AiChatStatusText = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            AiChatStatusText = $"复制失败：{ex.Message}";
        }
    }

    private void ResendMessage(AiChatMessage message)
    {
        if (_isAiChatRequestInFlight)
        {
            return;
        }

        RestorePendingAttachmentsFromMessage(message);
        RemoveMessageForResend(message);
        AiChatInputText = message.Text;
        FocusAiInput();
        AiChatStatusText = "已恢复到输入框，可重新编辑后发送";
    }

    // ==================== 核心功能 ====================
    
    private void ActivateAiChatMode()
    {
        LoadTopicsFromStorage();
        
        // 切换到 AI Chat 模式时，调整窗口大小以获得更好的阅读体验
        if (WindowState == WindowState.Normal)
        {
            // 保存当前窗口中心点
            var centerX = Left + Width / 2;
            var centerY = Top;  // 使用顶部位置
            
            // 调整窗口大小
            Width = 1100;
            Height = 720;
            MinWidth = 900;
            MinHeight = 600;
            
            // 根据新尺寸重新计算位置，保持顶部中心不变
            Left = centerX - Width / 2;
            Top = centerY;
        }
        
        if (_aiChatTopics.Count == 0)
        {
            CreateNewTopic();
        }
        else if (_selectedAiChatTopic == null)
        {
            SelectTopic(_aiChatTopics.First());
        }

        _aiChatAttachments.CollectionChanged += AiChatAttachments_CollectionChanged;
        _aiChatMessages.CollectionChanged += AiChatMessages_CollectionChanged;

        Dispatcher.BeginInvoke(() => FocusAiInput(), DispatcherPriority.Background);
    }

    private void ExitAiChatMode()
    {
        if (!IsAiChatMode)
        {
            return;
        }

        SaveTopicsToStorage();

        // 退出 AI Chat 模式时，恢复默认窗口大小
        RestoreDefaultWindowSize();

        var targetScope = SearchScopes.FirstOrDefault(scope => 
            scope.Key.Equals(_lastNonAiSearchScopeKey, StringComparison.OrdinalIgnoreCase))
            ?? SearchScopes.FirstOrDefault();
        
        if (targetScope != null)
        {
            SelectedSearchScope = targetScope;
        }

        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private async void SubmitAiChatMessage()
    {
        var userInput = AiChatInputText.Trim();
        if (string.IsNullOrEmpty(userInput) && _aiChatAttachments.Count == 0)
        {
            return;
        }

        if (_isAiChatRequestInFlight)
        {
            return;
        }

        if (!IsAiConfigured(_appSettings))
        {
            AiChatStatusText = "请先配置 AI";
            return;
        }

        // 确保有话题
        if (_selectedAiChatTopic == null)
        {
            CreateNewTopic();
        }

        var messageAttachments = await BuildMessageAttachmentsAsync(_aiChatAttachments);

        // 添加用户消息
        var userMessage = new AiChatMessage(true, userInput, messageAttachments);
        _aiChatMessages.Add(userMessage);
        _selectedAiChatTopic!.Messages.Add(userMessage);
        
        // 如果是第一条消息，使用它作为话题标题
        if (_selectedAiChatTopic.Messages.Count == 1)
        {
            var title = userInput.Length > 30 ? userInput.Substring(0, 30) + "..." : userInput;
            _selectedAiChatTopic.Title = title;
        }
        
        _selectedAiChatTopic.NotifyMessagesChanged();

        // 清空输入
        AiChatInputText = string.Empty;
        _aiChatAttachments.Clear();

        // 请求 AI
        _isAiChatRequestInFlight = true;
        AiChatStatusText = "AI 正在输入…";

        try
        {
            var reply = await RequestAiChatCompletionAsync();
            var aiMessage = new AiChatMessage(false, reply);
            _aiChatMessages.Add(aiMessage);
            _selectedAiChatTopic.Messages.Add(aiMessage);
            _selectedAiChatTopic.NotifyMessagesChanged();
            AiChatStatusText = "继续对话";
            SaveTopicsToStorage();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AI request failed: {FormatExceptionMessage(ex)}");
            AiChatStatusText = "AI 请求失败";
        }
        finally
        {
            _isAiChatRequestInFlight = false;
            _ = Dispatcher.BeginInvoke(() => FocusAiInput(), DispatcherPriority.Background);
        }
    }

    private async Task<string> RequestAiChatCompletionAsync()
    {
        if (!IsAiConfigured(_appSettings))
        {
            throw new InvalidOperationException("AI 未配置");
        }

        var endpoint = BuildAiChatEndpoint(_appSettings.AiBaseUrl.Trim());
        var messages = BuildAiRequestMessages();
        var payload = JsonSerializer.Serialize(new
        {
            model = _appSettings.AiModel.Trim(),
            messages,
            temperature = 0.7
        });

        HostAssets.AppendLog(
            $"AI request start: model={_appSettings.AiModel.Trim()}, messages={messages.Count}, lastUserAttachments={_aiChatMessages.LastOrDefault(static m => m.IsUser)?.Attachments.Count ?? 0}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _appSettings.AiApiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using var response = await _aiHttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        
        if (!response.IsSuccessStatusCode)
        {
            HostAssets.AppendLog(
                $"AI request failed response: status={(int)response.StatusCode} {response.ReasonPhrase}, body={TrimForLog(body, 800)}");
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var parsed = JsonSerializer.Deserialize<AiChatCompletionResponse>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var content = parsed?.Choices?
            .Select(c => c.Message?.Content)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            HostAssets.AppendLog($"AI response empty: body={TrimForLog(body, 1200)}");
            throw new InvalidOperationException("AI 返回为空");
        }

        var finishReason = parsed?.Choices?.FirstOrDefault()?.FinishReason;
        HostAssets.AppendLog(
            $"AI response success: finishReason={finishReason ?? "unknown"}, contentLength={content.Length}, body={TrimForLog(body, 1200)}");

        return content;
    }

    private IReadOnlyList<object> BuildAiRequestMessages()
    {
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = "你是燕子启动器中的 AI 助手。回答简洁、直接，优先帮助用户完成桌面效率任务。"
            }
        };

        foreach (var message in _aiChatMessages)
        {
            messages.Add(new
            {
                role = message.IsUser ? "user" : "assistant",
                content = message.BuildRequestContent()
            });
        }

        return messages;
    }

    private async Task<List<AiChatMessageAttachment>> BuildMessageAttachmentsAsync(IEnumerable<AiChatAttachment> attachments)
    {
        var snapshots = new List<AiChatMessageAttachment>();
        foreach (var attachment in attachments)
        {
            try
            {
                snapshots.Add(await CreateMessageAttachmentAsync(attachment));
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Build message attachment failed: {ex.Message}");
            }
        }

        return snapshots;
    }

    private async Task<AiChatMessageAttachment> CreateMessageAttachmentAsync(AiChatAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        if (!IsTextAttachmentExtension(extension))
        {
            return new AiChatMessageAttachment(
                attachment.FileName,
                attachment.FileSize,
                attachment.FilePath,
                null,
                "二进制或非文本附件，发送时仅附带文件名和大小。");
        }

        var content = await File.ReadAllTextAsync(attachment.FilePath);
        var isLikelyMinified = IsLikelyMinifiedContent(content);
        if (content.Length > MaxAttachmentInlineChars)
        {
            content = content[..MaxAttachmentInlineChars] + $"{Environment.NewLine}[内容已截断]";
        }

        if (isLikelyMinified)
        {
            var previewLength = Math.Min(content.Length, MaxAttachmentPreviewChars);
            var preview = content[..previewLength];
            var note =
                $"该文件疑似压缩或混淆后的代码，原始长度约 {AiChatAttachment.FormatFileSize(attachment.FileSize)}。" +
                $"{Environment.NewLine}请先判断它的用途、所属技术栈和大致功能，不要逐行解释。" +
                $"{Environment.NewLine}以下仅提供文件开头片段：";

            return new AiChatMessageAttachment(
                attachment.FileName,
                attachment.FileSize,
                attachment.FilePath,
                preview,
                note);
        }

        return new AiChatMessageAttachment(
            attachment.FileName,
            attachment.FileSize,
            attachment.FilePath,
            content,
            null);
    }

    private static bool IsTextAttachmentExtension(string? extension)
    {
        return extension != null && extension.ToLowerInvariant() is
            ".txt" or ".md" or ".json" or ".xml" or ".cs" or ".js" or ".ts" or ".tsx" or ".jsx" or
            ".py" or ".java" or ".go" or ".rs" or ".html" or ".css" or ".scss" or ".less" or
            ".yml" or ".yaml" or ".ini" or ".config" or ".log" or ".csv" or ".sql" or ".ps1" or
            ".bat" or ".cmd" or ".sh";
    }

    private static bool IsLikelyMinifiedContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 4000)
        {
            return false;
        }

        var newlineCount = content.Count(static character => character is '\r' or '\n');
        var averageLineLength = newlineCount == 0 ? content.Length : content.Length / Math.Max(1, newlineCount);
        return averageLineLength > 300 || (newlineCount < 20 && content.Length > 8000);
    }

    private void RestorePendingAttachmentsFromMessage(AiChatMessage message)
    {
        _aiChatAttachments.Clear();

        foreach (var attachment in message.Attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FilePath) || !File.Exists(attachment.FilePath))
            {
                continue;
            }

            try
            {
                _aiChatAttachments.Add(new AiChatAttachment(attachment.FilePath));
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Restore attachment for resend failed: {ex.Message}");
            }
        }
    }

    private void RemoveMessageForResend(AiChatMessage message)
    {
        if (_selectedAiChatTopic == null)
        {
            return;
        }

        var messageIndex = _selectedAiChatTopic.Messages.IndexOf(message);
        if (messageIndex < 0)
        {
            _aiChatMessages.Remove(message);
            return;
        }

        _selectedAiChatTopic.Messages.RemoveAt(messageIndex);
        _aiChatMessages.Remove(message);

        if (messageIndex < _selectedAiChatTopic.Messages.Count && !_selectedAiChatTopic.Messages[messageIndex].IsUser)
        {
            var assistantReply = _selectedAiChatTopic.Messages[messageIndex];
            _selectedAiChatTopic.Messages.RemoveAt(messageIndex);
            _aiChatMessages.Remove(assistantReply);
        }

        _selectedAiChatTopic.NotifyMessagesChanged();
        SaveTopicsToStorage();
    }

    private static string TrimForLog(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string BuildAiChatEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}/chat/completions";
        }

        return $"{trimmed}/v1/chat/completions";
    }

    private static bool IsAiConfigured(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.AiBaseUrl) &&
               !string.IsNullOrWhiteSpace(settings.AiApiKey) &&
               !string.IsNullOrWhiteSpace(settings.AiModel);
    }

    private void FocusAiInput(bool selectAll = false)
    {
        AiChatInputBox?.Focus();
        if (AiChatInputBox != null)
        {
            AiChatInputBox.CaretIndex = AiChatInputText.Length;
            if (selectAll)
            {
                AiChatInputBox.SelectAll();
            }
        }
    }

    // ==================== 持久化存储 ====================
    
    private void SaveTopicsToStorage()
    {
        try
        {
            var data = new
            {
                topics = _aiChatTopics.Select(t => new
                {
                    id = t.Id,
                    title = t.Title,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt,
                    messages = t.Messages.Select(m => new
                    {
                        id = m.Id,
                        isUser = m.IsUser,
                        text = m.Text,
                        timestamp = m.Timestamp,
                        attachments = m.Attachments.Select(a => new
                        {
                            fileName = a.FileName,
                            fileSize = a.FileSize,
                            filePath = a.FilePath,
                            content = a.Content,
                            note = a.Note
                        })
                    })
                })
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var path = HostAssets.ResolveDataFilePath("ai-chat-topics.json");
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Save topics failed: {ex.Message}");
        }
    }

    private void LoadTopicsFromStorage()
    {
        if (_aiChatTopics.Count > 0)
        {
            return;
        }

        try
        {
            var path = HostAssets.ResolveDataFilePath("ai-chat-topics.json");
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            
            if (!data.TryGetProperty("topics", out var topicsArray))
            {
                return;
            }

            foreach (var topicElement in topicsArray.EnumerateArray())
            {
                var title = topicElement.GetProperty("title").GetString() ?? "未命名";
                var topic = new AiChatTopic(title);

                if (topicElement.TryGetProperty("messages", out var messagesArray))
                {
                    foreach (var msgElement in messagesArray.EnumerateArray())
                    {
                        var isUser = msgElement.GetProperty("isUser").GetBoolean();
                        var text = msgElement.GetProperty("text").GetString() ?? "";
                        var attachments = new List<AiChatMessageAttachment>();
                        if (msgElement.TryGetProperty("attachments", out var attachmentsArray))
                        {
                            foreach (var attachmentElement in attachmentsArray.EnumerateArray())
                            {
                                attachments.Add(new AiChatMessageAttachment(
                                    attachmentElement.GetProperty("fileName").GetString() ?? "未命名附件",
                                    attachmentElement.TryGetProperty("fileSize", out var fileSizeElement) ? fileSizeElement.GetInt64() : 0,
                                    attachmentElement.TryGetProperty("filePath", out var filePathElement) ? filePathElement.GetString() : null,
                                    attachmentElement.TryGetProperty("content", out var contentElement) ? contentElement.GetString() : null,
                                    attachmentElement.TryGetProperty("note", out var noteElement) ? noteElement.GetString() : null));
                            }
                        }

                        topic.Messages.Add(new AiChatMessage(isUser, text, attachments));
                    }
                }

                _aiChatTopics.Add(topic);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Load topics failed: {ex.Message}");
        }
    }

    // ==================== 公共接口 ====================
    
    public void SaveAiSettings(string baseUrl, string apiKey, string model)
    {
        var settings = AppSettingsStore.Load();
        settings.AiBaseUrl = baseUrl;
        settings.AiApiKey = apiKey;
        settings.AiModel = model;
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        NotifyQuickPanelSettingsChanged("ai-settings-saved", refreshYanmOverlay: false);
        OnPropertyChanged(nameof(AiChatModelDisplayText));
    }
}

// ==================== 数据模型 ====================

public sealed class AiChatMessage
{
    public AiChatMessage(bool isUser, string text, IEnumerable<AiChatMessageAttachment>? attachments = null)
    {
        Id = Guid.NewGuid().ToString();
        IsUser = isUser;
        Text = text;
        Timestamp = DateTimeOffset.Now;
        Attachments = attachments?.ToList() ?? [];
    }

    public string Id { get; }
    public bool IsUser { get; }
    public string Text { get; }
    public DateTimeOffset Timestamp { get; }
    public List<AiChatMessageAttachment> Attachments { get; }
    public bool HasAttachments => Attachments.Count > 0;
    public Visibility AttachmentsVisibility => HasAttachments ? Visibility.Visible : Visibility.Collapsed;

    public string BuildRequestContent()
    {
        if (Attachments.Count == 0)
        {
            return Text;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Text))
        {
            parts.Add(Text);
        }

        foreach (var attachment in Attachments)
        {
            parts.Add(attachment.BuildRequestSection());
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }
}

public sealed class AiChatMessageAttachment
{
    public AiChatMessageAttachment(string fileName, long fileSize, string? filePath, string? content, string? note)
    {
        Id = Guid.NewGuid().ToString();
        FileName = fileName;
        FileSize = fileSize;
        FilePath = filePath;
        Content = content;
        Note = note;
    }

    public string Id { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public string? FilePath { get; }
    public string? Content { get; }
    public string? Note { get; }
    public string FileSizeText => AiChatAttachment.FormatFileSize(FileSize);

    public string BuildRequestSection()
    {
        var header = $"[附件: {FileName}, {FileSizeText}]";
        if (!string.IsNullOrWhiteSpace(Content))
        {
            return $"{header}{Environment.NewLine}{Content}";
        }

        if (!string.IsNullOrWhiteSpace(Note))
        {
            return $"{header}{Environment.NewLine}{Note}";
        }

        return header;
    }
}

public sealed class AiChatTopic : INotifyPropertyChanged
{
    private string _title;
    private bool _isSelected;

    public AiChatTopic(string title)
    {
        Id = Guid.NewGuid().ToString();
        _title = title;
        CreatedAt = DateTimeOffset.Now;
        UpdatedAt = DateTimeOffset.Now;
        Messages = [];
    }

    public string Id { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                UpdatedAt = DateTimeOffset.Now;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<AiChatMessage> Messages { get; }

    public string PreviewText
    {
        get
        {
            var lastMessage = Messages.LastOrDefault();
            if (lastMessage == null)
            {
                return "新对话";
            }

            var text = lastMessage.Text;
            return text.Length > 40 ? text.Substring(0, 40) + "..." : text;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void NotifyMessagesChanged()
    {
        UpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(UpdatedAt));
    }
}

public sealed class AiChatAttachment
{
    public AiChatAttachment(string filePath)
    {
        Id = Guid.NewGuid().ToString();
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        FileSize = new FileInfo(filePath).Length;
    }

    public string Id { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public long FileSize { get; }

    public string FileSizeText => FormatFileSize(FileSize);

    public static string FormatFileSize(long fileSize)
    {
        if (fileSize < 1024)
        {
            return $"{fileSize} B";
        }

        if (fileSize < 1024 * 1024)
        {
            return $"{fileSize / 1024.0:F1} KB";
        }

        return $"{fileSize / (1024.0 * 1024.0):F1} MB";
    }
}

public sealed class AiChatCompletionResponse
{
    public List<AiChatCompletionChoice>? Choices { get; set; }
}

public sealed class AiChatCompletionChoice
{
    public AiChatCompletionMessage? Message { get; set; }
    public string? FinishReason { get; set; }
}

public sealed class AiChatCompletionMessage
{
    public string? Content { get; set; }
}
