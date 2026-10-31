using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Linq;
using OpenQuickHost.Sync;

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
    private const string ChatGptPlanProviderId = "chatgpt-plan-gateway";

    private enum AiConversationMode
    {
        Chat,
        Agent
    }

    // ==================== 字段 ====================
    private readonly ObservableCollection<AiChatMessage> _aiChatMessages = [];
    private readonly ObservableCollection<AiChatTopic> _aiChatTopics = [];
    private readonly ObservableCollection<AiChatAttachment> _aiChatAttachments = [];
    private Yanzi.UI.Wpf.YanziDropdownMenu? _activeAiChatMenu;
    private AiChatTopic? _selectedAiChatTopic;
    private readonly HttpClient _aiHttpClient = new() { Timeout = TimeSpan.FromSeconds(300) };
    private static readonly TimeSpan AiModelDiscoveryInterval = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, DateTimeOffset> _aiModelDiscoveryAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _aiModelDiscoveryInFlight = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _aiModelDiscoveryTimer;
    private string _aiChatInputText = string.Empty;
    private string _aiChatStatusText = "选择或新建话题开始对话";
    private string _aiChatSendErrorText = string.Empty;
    // A request belongs to a conversation, not to the window. All mutations occur on the WPF dispatcher.
    private readonly AiChatRequestTracker _aiRequestTracker = new();
    private readonly Dictionary<string, string> _aiTopicErrors = new(StringComparer.Ordinal);
    private sealed record AiRequestProfile(string BaseUrl, string ApiKey, string Model, bool IsChatGptPlan);

    public bool IsAiChatRequestInFlight => _selectedAiChatTopic != null &&
        (_aiRequestTracker.IsActive(_selectedAiChatTopic.Id) ||
         _aiRequestTracker.IsPreparing(_selectedAiChatTopic.Id));
    public Visibility AiChatCancelVisibility =>
        _selectedAiChatTopic != null && _aiRequestTracker.IsActive(_selectedAiChatTopic.Id)
            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AiChatSendVisibility => AiChatCancelVisibility == Visibility.Visible
        ? Visibility.Collapsed : Visibility.Visible;

    private void RefreshAiTopicRequestState()
    {
        OnPropertyChanged(nameof(IsAiChatRequestInFlight));
        OnPropertyChanged(nameof(AiChatCancelVisibility));
        OnPropertyChanged(nameof(AiChatSendVisibility));
    }

    private void AiChatStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAiChatTopic != null &&
            _aiRequestTracker.TryGetActive(_selectedAiChatTopic.Id, out var request))
            request.Cancel();
    }

    private void SetAiTopicStatus(AiChatTopic topic, string status, string? error = null)
    {
        if (error != null) _aiTopicErrors[topic.Id] = error;
        if (!ReferenceEquals(_selectedAiChatTopic, topic)) return;
        AiChatStatusText = status;
        if (error != null) AiChatSendErrorText = error;
    }

    private void AppendAiTopicMessage(AiChatTopic topic, AiChatMessage message)
    {
        topic.Messages.Add(message);
        if (ReferenceEquals(_selectedAiChatTopic, topic)) _aiChatMessages.Add(message);
        topic.NotifyMessagesChanged();
        ReorderTopics();
    }

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
                foreach (var t in _aiChatTopics)
                {
                    t.IsDeleteConfirming = false;
                }
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTopic));
            OnPropertyChanged(nameof(NoTopicVisibility));
            LoadTopicMessages();
            RefreshAiTopicRequestState();
            AiChatSendErrorText = _selectedAiChatTopic != null &&
                _aiTopicErrors.TryGetValue(_selectedAiChatTopic.Id, out var error) ? error : string.Empty;
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

    public string AiChatSendErrorText
    {
        get => _aiChatSendErrorText;
        private set
        {
            if (_aiChatSendErrorText == value) return;
            _aiChatSendErrorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AiChatSendErrorVisibility));
        }
    }

    public Visibility AiChatSendErrorVisibility => string.IsNullOrWhiteSpace(_aiChatSendErrorText)
        ? Visibility.Collapsed : Visibility.Visible;

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

    private void AiChatTopicItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AiChatTopic topic })
        {
            SelectTopic(topic);
        }
    }

    private void AiChatTopicEditButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // Editing must never select the conversation behind the button.
        if (sender is System.Windows.Controls.Button { DataContext: AiChatTopic topic } button)
        {
            if (_activeAiChatMenu?.IsOpen == true) _activeAiChatMenu.IsOpen = false;
            var menu = CreateAiChatMenu();
            menu.PreferAbove = false;
            menu.AddAction("重命名", () => RenameTopic(topic));
            menu.AddSeparator();
            menu.AddAction("删除", () => DeleteTopic(topic), destructive: true);
            _activeAiChatMenu = menu;
            menu.ShowFrom(button);
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
        UpdateAiChatTimeDividers();
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
        ReorderTopics();
        SaveTopicsToStorage();
    }

    private void EscReturnButton_Click(object sender, MouseButtonEventArgs e)
    {
        ExitAiChatMode();
    }

    private void PinTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatTopic topic })
        {
            topic.IsPinned = !topic.IsPinned;
            ReorderTopics();
            SaveTopicsToStorage();
        }
    }

    private void RenameTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatTopic topic })
        {
            RenameTopic(topic);
        }
    }

    private void DeleteTopicButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatTopic topic })
        {
            if (topic.IsDeleteConfirming)
            {
                DeleteTopic(topic);
            }
            else
            {
                foreach (var t in _aiChatTopics)
                {
                    t.IsDeleteConfirming = false;
                }
                topic.IsDeleteConfirming = true;
            }
        }
    }

    private void ReorderTopics()
    {
        var originalSelected = _selectedAiChatTopic;
        var sorted = _aiChatTopics
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();

        bool changed = false;
        if (_aiChatTopics.Count == sorted.Count)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                if (_aiChatTopics[i] != sorted[i])
                {
                    changed = true;
                    break;
                }
            }
        }
        else
        {
            changed = true;
        }

        if (changed)
        {
            _selectedAiChatTopic = null;
            _aiChatTopics.Clear();
            foreach (var t in sorted)
            {
                _aiChatTopics.Add(t);
            }
            _selectedAiChatTopic = originalSelected;
            if (_selectedAiChatTopic != null)
            {
                _selectedAiChatTopic.IsSelected = true;
            }
            OnPropertyChanged(nameof(SelectedAiChatTopic));
        }
    }

    private void SelectTopic(AiChatTopic topic)
    {
        SelectedAiChatTopic = topic;
        AiChatStatusText = IsAiChatRequestInFlight ? "此对话正在生成…" :
            IsAiConfigured(_appSettings) ? "继续对话" : "请先配置 AI";
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
        UpdateAiChatTimeDividers();
    }

    private void UpdateAiChatTimeDividers()
    {
        AiChatMessage? previous = null;
        foreach (var message in _aiChatMessages)
        {
            // Show the first timestamp, then only after >= 3 minutes.
            // This is visual metadata; no extra message is added to the chat history.
            message.ShowTimeDivider = AiChatMessage.NeedsTimeDivider(previous, message);
            previous = message;
        }
    }

    private void DeleteTopic(AiChatTopic topic)
    {
        if (_aiRequestTracker.TryGetActive(topic.Id, out var pending)) pending.Cancel();
        _aiTopicErrors.Remove(topic.Id);
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
            Width = 420,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        Yanzi.UI.Wpf.YanziUi.ApplyTo(dialog, Yanzi.UI.Wpf.YanziUi.GetTheme(this));
        dialog.SetResourceReference(Window.BackgroundProperty, "Yanzi.Color.Popover");
        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = topic.Title,
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 15)
        };
        textBox.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Yanzi.Input");
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
        okButton.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Yanzi.Button.Default");
        okButton.IsDefault = true;
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
        cancelButton.SetResourceReference(System.Windows.Controls.Control.StyleProperty, "Yanzi.Button.Outline");
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) => dialog.Close();

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stack.Children.Add(textBox);
        stack.Children.Add(buttonPanel);
        dialog.Content = stack;

        textBox.Focus();
        dialog.ShowDialog();
    }

    // Shared menu chrome and edge positioning are owned by Yanzi.UI.Wpf.
    // Keep business actions (rename/delete) in the host.
    private void ShowTopicContextMenu(AiChatTopic topic, FrameworkElement placementTarget)
    {
        var menu = CreateAiChatMenu();
        menu.AddAction("重命名", () => RenameTopic(topic));
        menu.AddSeparator();
        menu.AddAction("删除", () => DeleteTopic(topic));
        OpenAiChatContextMenu(menu, placementTarget);
    }

    private Yanzi.UI.Wpf.YanziDropdownMenu CreateAiChatMenu()
    {
        var menu = new Yanzi.UI.Wpf.YanziDropdownMenu();
        menu.UseStandaloneTheme(Yanzi.UI.Wpf.YanziUi.GetTheme(this));
        menu.UseContentWidth(168);
        return menu;
    }

    private void OpenAiChatContextMenu(
        Yanzi.UI.Wpf.YanziDropdownMenu menu, FrameworkElement target)
    {
        var cursor = System.Windows.Input.Mouse.GetPosition(target);
        var physical = target.PointToScreen(cursor);
        var source = PresentationSource.FromVisual(target);
        if (source?.CompositionTarget is null) return;
        var matrix = source.CompositionTarget.TransformFromDevice;
        var area = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)Math.Round(physical.X), (int)Math.Round(physical.Y))).WorkingArea;
        var topLeft = matrix.Transform(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = matrix.Transform(new System.Windows.Point(area.Right, area.Bottom));
        var logicalClick = matrix.Transform(physical);
        if (_activeAiChatMenu is not null) _activeAiChatMenu.IsOpen = false;
        _activeAiChatMenu = menu;
        menu.ShowAtScreenPoint(logicalClick, new Rect(topLeft, bottomRight));
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
        var menu = CreateAiChatMenu();
        menu.AddAction("复制内容", () => CopyMessage(message));
        if (message.IsUser)
            menu.AddAction("重新发送", () => ResendMessage(message));
        OpenAiChatContextMenu(menu, placementTarget);
    }

    private void AiChatCopyMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatMessage message })
        {
            CopyMessage(message);
        }
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
        if (IsAiChatRequestInFlight)
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
        RefreshAiModelSelector();
        StartAiModelDiscovery();
        
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

        _aiModelDiscoveryTimer?.Stop();
        if (_activeAiChatMenu is not null) _activeAiChatMenu.IsOpen = false;
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

    // Model options are sourced directly from the saved service providers.
    // The bottom-right menu is the only selector; no hidden duplicated ComboBoxes.
    public void RefreshAiModelSelector()
    {
        OnPropertyChanged(nameof(AiChatModelDisplayText));
    }

    private void AiChatModelMenuButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.Button anchor) return;
        if (_activeAiChatMenu?.IsOpen == true)
        {
            _activeAiChatMenu.IsOpen = false;
            return;
        }

        // Display cached models while asynchronously refreshing the catalog.
        _appSettings = AppSettingsStore.Load();
        QueueAiModelCatalogRefreshes();
        OnPropertyChanged(nameof(AiChatModelDisplayText));

        var menu = CreateAiChatMenu();
        menu.PreferAbove = true;
        menu.AlignStart = false;
        menu.UseContentWidth(190);

        var providers = (_appSettings.AiServiceProviders ?? [])
            .Where(provider => provider.IsEnabled).ToArray();
        if (providers.Length == 0)
        {
            menu.AddLabel("尚未启用模型供应商");
            menu.AddSeparator();
            menu.AddAction("配置供应商…", () => AiChatSettingsButton_Click(anchor, new RoutedEventArgs()));
        }
        else
        {
            var widestModel = providers.SelectMany(provider => provider.Models ?? [])
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .DefaultIfEmpty("").MaxBy(model => model.Length) ?? "";
            // Give full model IDs enough room, including provider-specific suffixes.
            menu.SubmenuWidth = Math.Clamp(widestModel.Length * 9 + 58, 194, 420);
            foreach (var provider in providers)
            {
                var providerId = provider.Id;
                var models = (provider.Models ?? [])
                    .Where(model => !string.IsNullOrWhiteSpace(model))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var isActive = string.Equals(providerId, _appSettings.ActiveServiceProviderId,
                    StringComparison.OrdinalIgnoreCase);
                var item = menu.AddSubmenu(provider.Name, submenu =>
                {
                    if (models.Length == 0)
                    {
                        submenu.AddLabel("暂无模型，请先在设置中添加");
                        return;
                    }
                    foreach (var model in models)
                    {
                        var modelId = model;
                        var selected = isActive && string.Equals(modelId, _appSettings.AiModel,
                            StringComparison.OrdinalIgnoreCase);
                        submenu.AddAction(modelId, () => SelectAiChatProviderModel(providerId, modelId),
                            selected ? Yanzi.UI.Wpf.YanziIcons.Check(15) : null);
                    }
                }, isActive ? Yanzi.UI.Wpf.YanziIcons.Check(15) : null);
                item.ToolTip = "选择 " + provider.Name + " 提供的模型";
            }
        }

        // Use the public menu's fixed-size screen placement. It reserves submenu
        // space before opening, chooses right/left from the monitor boundaries
        // and does not resize the popup while moving between providers.
        OpenAiChatContextMenu(menu, anchor);
    }

    private void SelectAiChatProviderModel(string providerId, string model)
    {
        var settings = AppSettingsStore.Load();
        var provider = settings.AiServiceProviders?.FirstOrDefault(item =>
            item.IsEnabled && string.Equals(item.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (provider == null || !(provider.Models ?? []).Any(candidate =>
                string.Equals(candidate, model, StringComparison.OrdinalIgnoreCase)))
            return;

        settings.ActiveServiceProviderId = provider.Id;
        settings.AiBaseUrl = provider.BaseUrl;
        settings.AiApiKey = provider.ApiKey;
        settings.AiModel = model;
        provider.SelectedModel = model;
        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        RefreshAiModelSelector();
    }

    public void OnAiSettingsChanged()
    {
        _appSettings = AppSettingsStore.Load();
        RefreshAiModelSelector();
        OnPropertyChanged(nameof(AiChatModelDisplayText));
        _aiModelDiscoveryAttempts.Clear();
        if (IsAiChatMode) QueueAiModelCatalogRefreshes();
    }



    private void StartAiModelDiscovery()
    {
        if (_aiModelDiscoveryTimer == null)
        {
            _aiModelDiscoveryTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = AiModelDiscoveryInterval
            };
            _aiModelDiscoveryTimer.Tick += (_, _) =>
            {
                if (IsAiChatMode) QueueAiModelCatalogRefreshes();
            };
        }

        _aiModelDiscoveryTimer.Start();
        QueueAiModelCatalogRefreshes();
    }

    private void QueueAiModelCatalogRefreshes()
    {
        foreach (var provider in (_appSettings.AiServiceProviders ?? []).Where(p => p.IsEnabled))
        {
            _ = RefreshAiModelCatalogAsync(provider.Id);
        }
    }

    // Fetch asynchronously, retain the last successful list on failure, and
    // never change the selected model implicitly.
    private async Task RefreshAiModelCatalogAsync(string? providerId = null)
    {
        providerId ??= _appSettings.ActiveServiceProviderId;
        var provider = (_appSettings.AiServiceProviders ?? []).FirstOrDefault(p =>
            p.IsEnabled && string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (provider is null || string.IsNullOrWhiteSpace(provider.Id) ||
            string.IsNullOrWhiteSpace(provider.ApiKey) || string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            return;
        }

        var id = provider.Id;
        if (_aiModelDiscoveryInFlight.Contains(id) ||
            (_aiModelDiscoveryAttempts.TryGetValue(id, out var lastAttempt) &&
             DateTimeOffset.UtcNow - lastAttempt < AiModelDiscoveryInterval))
        {
            return;
        }

        _aiModelDiscoveryAttempts[id] = DateTimeOffset.UtcNow;
        _aiModelDiscoveryInFlight.Add(id);
        var snapshot = new AiProviderSnapshot(provider.ProviderType, provider.BaseUrl, provider.ApiKey);
        try
        {
            var discovered = await SettingsAiController.FetchAvailableModelsAsync(snapshot);
            if (discovered.Count == 0)
            {
                HostAssets.AppendLog($"AI model discovery returned an empty list: provider={id}; keeping cache.");
                return;
            }

            // A settings transaction reloads the latest selected model and account
            // state, avoiding overwrites while the network request is in flight.
            var current = AppSettingsStore.Load();
            var target = (current.AiServiceProviders ?? []).FirstOrDefault(p =>
                p.IsEnabled && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.BaseUrl, snapshot.BaseUrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ApiKey, snapshot.ApiKey, StringComparison.Ordinal) &&
                string.Equals(p.ProviderType, snapshot.ProviderType, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;
            var pinnedModels = SettingsAiController.IncludeSelectedModel(discovered,
                string.Equals(current.ActiveServiceProviderId, id, StringComparison.OrdinalIgnoreCase) ? current.AiModel : null,
                target.SelectedModel);
            if (!SettingsAiController.SameModelIds(target.Models, pinnedModels))
            {
                current = AppSettingsStore.Update(settings =>
                {
                    var latest = (settings.AiServiceProviders ?? []).FirstOrDefault(p =>
                        p.IsEnabled && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.BaseUrl, snapshot.BaseUrl, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.ApiKey, snapshot.ApiKey, StringComparison.Ordinal) &&
                        string.Equals(p.ProviderType, snapshot.ProviderType, StringComparison.OrdinalIgnoreCase));
                    if (latest != null)
                    {
                        var updated = SettingsAiController.IncludeSelectedModel(discovered,
                            string.Equals(settings.ActiveServiceProviderId, id, StringComparison.OrdinalIgnoreCase) ? settings.AiModel : null,
                            latest.SelectedModel);
                        if (!SettingsAiController.SameModelIds(latest.Models, updated)) latest.Models = updated;
                    }
                    return settings;
                });
                HostAssets.AppendLog($"AI model catalog refreshed: provider={id}, count={discovered.Count}.");
            }

            if (IsAiChatMode &&
                string.Equals(_appSettings.ActiveServiceProviderId, id, StringComparison.OrdinalIgnoreCase))
            {
                _appSettings = current;
                RefreshAiModelSelector();
                OnPropertyChanged(nameof(AiChatModelDisplayText));
                if (!IsAiChatRequestInFlight && !string.IsNullOrWhiteSpace(current.AiModel) &&
                    !discovered.Contains(current.AiModel, StringComparer.OrdinalIgnoreCase))
                {
                    AiChatStatusText = "当前模型已不在服务商最新列表中，请选择可用模型。";
                }
            }
        }
        catch (Exception ex)
        {
            // Do not log key material, query strings, or raw provider errors.
            HostAssets.AppendLog($"AI model discovery unavailable: provider={id}, type={ex.GetType().Name}; using cache.");
        }
        finally
        {
            _aiModelDiscoveryInFlight.Remove(id);
        }
    }


    private async void SubmitAiChatMessage()
    {
        if (_selectedAiChatTopic == null) CreateNewTopic();
        var topic = _selectedAiChatTopic!;
        // Two fast Enter presses in the same topic are rejected, but other topics remain interactive.
        if (!_aiRequestTracker.TryPrepare(topic.Id))
            return;
        RefreshAiTopicRequestState();
        try
        {
            await SubmitAiChatMessageCore(topic);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SubmitAiChatMessage failed: {ex}");
            SetAiTopicStatus(topic, "发送失败", $"发送失败：{TrimForLog(FormatExceptionMessage(ex), 160)}");
        }
        finally
        {
            _aiRequestTracker.EndPreparing(topic.Id);
            RefreshAiTopicRequestState();
        }
    }

    private async Task SubmitAiChatMessageCore(AiChatTopic topic)
    {
        // Snapshot editor, attachments, mode, and provider before the first await: topic switching
        // or model changes cannot redirect a request already in progress.
        var userInput = (AiChatInputBox?.Text ?? AiChatInputText).Trim();
        var pendingAttachments = _aiChatAttachments.ToArray();
        if (string.IsNullOrEmpty(userInput) && pendingAttachments.Length == 0) return;
        var mode = DetermineAiConversationMode(userInput);
        var settings = _appSettings;
        if (!IsAiConfigured(settings))
        {
            var msg = string.IsNullOrWhiteSpace(settings.AiApiKey)
                ? "无法发送：当前服务商缺少 API Key。请打开「模型服务」重新配置。"
                : "无法发送：当前服务商的模型或接口地址尚未配置。";
            SetAiTopicStatus(topic, msg, msg);
            return;
        }
        var profile = new AiRequestProfile(
            settings.AiBaseUrl.Trim(), settings.AiApiKey.Trim(), settings.AiModel.Trim(),
            string.Equals(settings.ActiveServiceProviderId, ChatGptPlanProviderId,
                StringComparison.OrdinalIgnoreCase));
        _aiTopicErrors.Remove(topic.Id);
        if (ReferenceEquals(_selectedAiChatTopic, topic)) AiChatSendErrorText = string.Empty;
        var messageAttachments = await BuildMessageAttachmentsAsync(pendingAttachments);
        var userMessage = new AiChatMessage(true, userInput, messageAttachments);
        AppendAiTopicMessage(topic, userMessage);
        if (topic.Messages.Count == 1)
            topic.Title = userInput.Length > 30 ? userInput[..30] + "..." : userInput;
        SaveTopicsToStorage();
        if (ReferenceEquals(_selectedAiChatTopic, topic))
        {
            // Don't erase a second conversation's input if the user switched while attachments loaded.
            AiChatInputText = string.Empty;
            _aiChatAttachments.Clear();
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _aiRequestTracker.Start(topic.Id, cancellation);
        topic.IsGenerating = true;
        RefreshAiTopicRequestState();
        SetAiTopicStatus(topic, "AI 正在生成…");
        HostAssets.AppendLog($"AI conversation route: topic={topic.Id}, mode={mode}, model={profile.Model}, streaming=true");
        try
        {
            if (mode == AiConversationMode.Chat)
            {
                // The placeholder belongs exclusively to its originating topic.
                var requestMessages = BuildAiRequestMessages(mode, topic);
                var reply = new AiChatMessage(false, string.Empty) { IsStreaming = true };
                AppendAiTopicMessage(topic, reply);
                var buffer = new StringBuilder();
                var lastVisualUpdate = DateTime.UtcNow;
                try
                {
                    var answer = await RequestAiChatCompletionStreamingAsync(
                        requestMessages, profile, mode,
                        delta =>
                        {
                            buffer.Append(delta);
                            var now = DateTime.UtcNow;
                            if ((now - lastVisualUpdate).TotalMilliseconds >= 160 || buffer.Length < 64)
                            {
                                reply.Text = buffer.ToString();
                                lastVisualUpdate = now;
                            }
                        }, cancellation.Token);
                    reply.Text = answer;
                    reply.IsStreaming = false;
                    SetAiTopicStatus(topic, "继续对话");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    reply.Text = buffer.Length == 0 ? "生成已停止，原始问题已保留。"
                        : buffer + Environment.NewLine + Environment.NewLine + "> 已停止生成，保留部分回复。";
                    reply.IsStreaming = false;
                    SetAiTopicStatus(topic, "生成已停止");
                }
                catch
                {
                    reply.Text = buffer.Length == 0 ? "回复中断，请重试。当前问题已保留。"
                        : buffer + Environment.NewLine + Environment.NewLine + "> 回复中断，已保留部分内容。";
                    reply.IsStreaming = false;
                    throw;
                }
            }
            else
            {
                // Agent tool calls are parsed only after a complete SSE response, but the HTTP
                // transport itself streams so upstream gateways don't wait for the full answer.
                for (int round = 0; round < 5; round++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var reply = await RequestAiChatCompletionStreamingAsync(
                        BuildAiRequestMessages(mode, topic), profile, mode, _ => { }, cancellation.Token);
                    var toolCall = ParseToolCall(reply);
                    if (toolCall == null)
                    {
                        AppendAiTopicMessage(topic, new AiChatMessage(false, reply));
                        break;
                    }
                    var call = new AiChatMessage(false, reply)
                    {
                        IsToolCall = true, ToolName = toolCall.ToolName
                    };
                    AppendAiTopicMessage(topic, call);
                    SetAiTopicStatus(topic, $"AI 正在调用工具：{toolCall.ToolName}…");
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        call.ToolFeedback = await ExecuteToolAsync(toolCall);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        call.ToolFeedback = $"【系统反馈】执行工具失败：{ex.Message}";
                    }
                    topic.NotifyMessagesChanged();
                    SetAiTopicStatus(topic, "AI 正在分析工具结果…");
                }
                SetAiTopicStatus(topic, "继续对话");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetAiTopicStatus(topic, "生成已停止");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AI request failed: topic={topic.Id}, model={profile.Model}, error={FormatExceptionMessage(ex)}");
            var error = $"AI 请求失败：{TrimForLog(FormatExceptionMessage(ex), 160)}";
            SetAiTopicStatus(topic, error, error);
        }
        finally
        {
            SaveTopicsToStorage();
            _aiRequestTracker.Finish(topic.Id);
            topic.IsGenerating = false;
            RefreshAiTopicRequestState();
            // Do not steal keyboard focus from a different conversation.
            if (ReferenceEquals(_selectedAiChatTopic, topic))
                _ = Dispatcher.BeginInvoke(() => FocusAiInput(), DispatcherPriority.Background);
        }
    }

    private class ToolCallInfo
    {
        public string ToolName { get; set; } = string.Empty;
        public JsonElement RawPayload { get; set; }
    }

    private ToolCallInfo? ParseToolCall(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        string jsonContent = content.Trim();
        int startIdx = content.IndexOf("```json");
        int endIdx;
        if (startIdx != -1 && (endIdx = content.IndexOf("```", startIdx + 7)) != -1)
        {
            jsonContent = content.Substring(startIdx + 7, endIdx - startIdx - 7).Trim();
        }

        if (jsonContent.StartsWith("{") && jsonContent.EndsWith("}"))
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement.Clone();
                if (root.TryGetProperty("tool", out var toolProp))
                {
                    var toolName = toolProp.GetString();
                    if (!string.IsNullOrWhiteSpace(toolName) && IsKnownAiTool(toolName))
                    {
                        return new ToolCallInfo
                        {
                            ToolName = toolName,
                            RawPayload = root
                        };
                    }
                }
            }
            catch
            {
                // Ignore and fallback
            }
        }

        // 正则备用
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(jsonContent, @"""tool""\s*:\s*""([^""]+)""");
            if (match.Success)
            {
                var toolName = match.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(toolName) && IsKnownAiTool(toolName))
                {
                    var minimalJson = $"{{\"tool\":\"{toolName}\"}}";
                    using var doc = JsonDocument.Parse(minimalJson);
                    return new ToolCallInfo
                    {
                        ToolName = toolName,
                        RawPayload = doc.RootElement.Clone()
                    };
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    private bool IsKnownAiTool(string toolName)
    {
        return toolName is "query_extensions" or "execute_extension" or "execute_command" or "create_extension" or "delete_extension" or "run_extension" or "stop_extension";
    }

    private async Task<string> ExecuteToolAsync(ToolCallInfo toolCall)
    {
        switch (toolCall.ToolName)
        {
            case "query_extensions":
                {
                    var extensions = GetExtensionsForSettings();
                    var list = new List<object>();
                    foreach (var ext in extensions)
                    {
                        list.Add(new
                        {
                            id = ext.ExtensionId,
                            name = ext.Title,
                            desc = ext.Subtitle
                        });
                    }
                    var jsonStr = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = false });
                    return $"【系统反馈】这是查询到的扩展列表：\n{jsonStr}";
                }

            case "execute_extension":
                {
                    string id = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("id", out var idProp))
                    {
                        id = idProp.GetString() ?? string.Empty;
                    }
                    else if (toolCall.RawPayload.TryGetProperty("extensionId", out var extIdProp))
                    {
                        id = extIdProp.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        return "【系统反馈】执行失败：未指定扩展 id 参数。";
                    }

                    var extId = id;
                    var success = await Dispatcher.InvokeAsync(() =>
                    {
                        if (TryResolveExtensionCommand(extId, out var command))
                        {
                            MarkExtensionAsSeen(command);
                            _ = ExecuteCommandAsync(ResolveRunnableCommand(command), explicitInput: null, launchSource: "ai-agent");
                            return true;
                        }
                        return false;
                    });

                    if (success)
                    {
                        return $"【系统反馈】已成功在后台触发扩展 {extId} 的执行。";
                    }
                    else
                    {
                        return $"【系统反馈】执行失败：未能找到启用状态的小程序 ID \"{extId}\"。";
                    }
                }

            case "execute_command":
                {
                    string command = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("command", out var cmdProp))
                    {
                        command = cmdProp.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        return "【系统反馈】执行失败：未指定 command 参数。";
                    }

                    var result = await RunPowerShellCommandAsync(command);
                    return $"【系统反馈】命令行执行结果(退出码:{result.ExitCode})：\n{result.Output}\n请根据结果直接使用自然语言回复用户，绝对不要再次调用本工具！";
                }

            case "create_extension":
                {
                    string manifestStr = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("manifest", out var manifestProp))
                    {
                        manifestStr = manifestProp.GetString() ?? string.Empty;
                    }
                    if (string.IsNullOrWhiteSpace(manifestStr))
                    {
                        if (toolCall.RawPayload.TryGetProperty("manifest", out var manifestRaw) && manifestRaw.ValueKind == JsonValueKind.Object)
                        {
                            manifestStr = JsonSerializer.Serialize(manifestRaw);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(manifestStr))
                    {
                        return "【系统反馈】新建失败：未指定 manifest 参数。";
                    }

                    try
                    {
                        var result = await Dispatcher.InvokeAsync(() =>
                        {
                            var command = LocalExtensionCatalog.SaveJsonExtension(manifestStr, forceNewSystemId: true);
                            if (command != null)
                            {
                                TrackRecentlyAddedExtension(command.ExtensionId);
                                ReloadLocalExtensionsFromExternal();
                                QueueCSharpPrebuild(command, "api-add");
                                return (true, command.ExtensionId);
                            }
                            return (false, string.Empty);
                        });

                        if (result.Item1)
                        {
                            return $"【系统反馈】新建小程序成功，新生成的小程序 ID 为：{result.Item2}，且已自动编译并刷新列表。";
                        }
                        else
                        {
                            return "【系统反馈】新建小程序失败。";
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"【系统反馈】新建小程序失败：{ex.Message}";
                    }
                }

            case "delete_extension":
                {
                    string id = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("id", out var idProp))
                    {
                        id = idProp.GetString() ?? string.Empty;
                    }
                    else if (toolCall.RawPayload.TryGetProperty("extensionId", out var extIdProp))
                    {
                        id = extIdProp.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        return "【系统反馈】删除失败：未指定 id 参数。";
                    }

                    var extIdToDelete = id;
                    try
                    {
                        var result = await Dispatcher.InvokeAsync(() =>
                        {
                            var commands = LocalExtensionCatalog.LoadCommands();
                            var command = commands.FirstOrDefault(c => string.Equals(c.ExtensionId, extIdToDelete, StringComparison.OrdinalIgnoreCase));
                            if (command != null)
                            {
                                WebDavSyncService.MarkExtensionDeletedLocally(extIdToDelete, command.DeclaredVersion);
                                ExtensionRecycleBinService.MoveToRecycleBin(extIdToDelete, command.ExtensionDirectoryPath);
                                ReloadLocalExtensionsFromExternal();
                                QueuePrivateExtensionRemovalFromAccount(extIdToDelete);
                                QueueBackgroundWebDavSync("extension-ai-delete", forceImmediate: true);
                                return true;
                            }
                            return false;
                        });

                        if (result)
                        {
                            return $"【系统反馈】已成功删除小程序 ID: {extIdToDelete} 并刷新列表。";
                        }
                        else
                        {
                            return $"【系统反馈】删除失败：未能找到 ID 为 \"{extIdToDelete}\" 的扩展。";
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"【系统反馈】删除小程序失败：{ex.Message}";
                    }
                }

            case "run_extension":
                {
                    string id = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("id", out var idProp))
                    {
                        id = idProp.GetString() ?? string.Empty;
                    }
                    else if (toolCall.RawPayload.TryGetProperty("extensionId", out var extIdProp))
                    {
                        id = extIdProp.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        return "【系统反馈】运行失败：未指定 id 参数。";
                    }

                    string inputStr = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("input", out var inputProp))
                    {
                        inputStr = inputProp.GetString() ?? string.Empty;
                    }

                    var extIdToRun = id;
                    try
                    {
                        var (success, output, error) = await await Dispatcher.InvokeAsync(async () =>
                        {
                            if (TryResolveExtensionCommand(extIdToRun, out var command))
                            {
                                MarkExtensionAsSeen(command);
                                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                                try
                                {
                                    var result = await ScriptExtensionRunner.ExecuteAsync(command, inputStr, "ai-agent", cts.Token);
                                    return (true, result.Output ?? string.Empty, result.Error ?? string.Empty);
                                }
                                catch (Exception ex)
                                {
                                    return (false, string.Empty, ex.Message);
                                }
                            }
                            return (false, string.Empty, "未找到该扩展");
                        });

                        if (success)
                        {
                            return $"【系统反馈】扩展 {extIdToRun} 运行成功。\n【标准输出】\n{output}\n【标准错误】\n{error}";
                        }
                        else
                        {
                            return $"【系统反馈】扩展 {extIdToRun} 运行失败：{error}";
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"【系统反馈】运行小程序失败：{ex.Message}";
                    }
                }

            case "stop_extension":
                {
                    string id = string.Empty;
                    if (toolCall.RawPayload.TryGetProperty("id", out var idProp))
                    {
                        id = idProp.GetString() ?? string.Empty;
                    }
                    else if (toolCall.RawPayload.TryGetProperty("extensionId", out var extIdProp))
                    {
                        id = extIdProp.GetString() ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        return "【系统反馈】停止失败：未指定 id 参数。";
                    }

                    var extIdToStop = id;
                    try
                    {
                        var success = await Dispatcher.InvokeAsync(() =>
                        {
                            var runningInstances = RunningExtensionRegistry.GetSnapshot()
                                .Where(x => string.Equals(x.ExtensionId, extIdToStop, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (runningInstances.Count == 0)
                            {
                                return false;
                            }

                            foreach (var instance in runningInstances)
                            {
                                RunningExtensionRegistry.TryTerminate(instance.InstanceId, out _);
                            }
                            return true;
                        });

                        if (success)
                        {
                            return $"【系统反馈】已成功停止小程序 ID: {extIdToStop} 的所有运行实例。";
                        }
                        else
                        {
                            return $"【系统反馈】停止失败：未能找到处于运行状态下的小程序 ID \"{extIdToStop}\" 实例。";
                        }
                    }
                    catch (Exception ex)
                    {
                        return $"【系统反馈】停止小程序失败：{ex.Message}";
                    }
                }

            default:
                return $"【系统反馈】未知的工具名称：{toolCall.ToolName}";
        }
    }

    private async Task<(int ExitCode, string Output)> RunPowerShellCommandAsync(string command)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            var prependedCommand = "$ProgressPreference = 'SilentlyContinue';\r\n" + command;
            var bytes = System.Text.Encoding.Unicode.GetBytes(prependedCommand);
            var base64 = Convert.ToBase64String(bytes);
            
            process.StartInfo.Arguments = $"-NoProfile -NonInteractive -EncodedCommand {base64}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                return (-1, outputBuilder.ToString() + "\r\n[错误] 命令执行超时 (15秒)\r\n" + errorBuilder.ToString());
            }

            var fullOutput = outputBuilder.ToString() + errorBuilder.ToString();
            return (process.ExitCode, fullOutput);
        }
        catch (Exception ex)
        {
            return (-1, $"[执行异常] {ex.Message}");
        }
    }

    private AiConversationMode DetermineAiConversationMode(string userInput)
    {
        var text = (userInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return AiConversationMode.Chat;
        }

        var lower = text.ToLowerInvariant();

        string[] chatOnlyMarkers =
        [
            "不要调用工具", "不用调用工具", "不要操作电脑", "不要执行", "只回答", "只解释",
            "只分析", "先讨论", "先不要操作", "不需要操作"
        ];

        if (chatOnlyMarkers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return AiConversationMode.Chat;
        }

        if (lower.Contains("调用工具", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("使用工具", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("直接执行", StringComparison.OrdinalIgnoreCase))
        {
            return AiConversationMode.Agent;
        }

        string[] actionMarkers =
        [
            "打开", "关闭", "启动", "运行", "执行", "安装", "卸载", "删除", "新建", "创建",
            "修改", "修复", "保存", "复制", "移动", "重命名", "下载", "上传", "发送", "截图",
            "关机", "重启", "停止", "暂停", "继续执行", "切换", "设置", "调整", "清理",
            "查看", "检查", "读取", "找到", "定位", "搜索本地"
        ];

        string[] environmentTargets =
        [
            "电脑", "本机", "windows", "文件", "文件夹", "目录", "桌面", "进程", "服务",
            "注册表", "剪贴板", "小程序", "扩展", "燕子", "微信", "qq", "浏览器", "硬盘",
            "磁盘", "cpu", "内存", "网络", "服务器", "程序", "应用", "界面"
        ];

        var hasAction = actionMarkers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var hasEnvironmentTarget = environmentTargets.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var recentContext = string.Join(
            "\n",
            _aiChatMessages
                .TakeLast(8)
                .Select(static message => message.Text)
                .Where(static messageText => !string.IsNullOrWhiteSpace(messageText)));
        var recentContextHasEnvironmentTarget = environmentTargets.Any(marker =>
            recentContext.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (hasAction && (hasEnvironmentTarget || (text.Length <= 40 && recentContextHasEnvironmentTarget)))
        {
            return AiConversationMode.Agent;
        }

        if ((lower.Contains(@"c:\", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains(@"d:\", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains(@"f:\", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains(".exe", StringComparison.OrdinalIgnoreCase)) &&
            hasAction)
        {
            return AiConversationMode.Agent;
        }

        string[] continuationMarkers =
        [
            "继续", "继续吧", "接着做", "再试", "重试", "开始吧", "执行吧", "完成它", "修复它"
        ];

        if (text.Length <= 16 &&
            continuationMarkers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase)) &&
            (_aiChatMessages.TakeLast(6).Any(static message => message.IsToolCall) || recentContextHasEnvironmentTarget))
        {
            return AiConversationMode.Agent;
        }

        return AiConversationMode.Chat;
    }


    private async Task<string> RequestAiChatCompletionStreamingAsync(
        IReadOnlyList<object> messages, AiRequestProfile profile, AiConversationMode mode,
        Action<string> onDelta, CancellationToken cancellationToken)
    {
        var endpoint = BuildAiChatEndpoint(profile.BaseUrl);
        object payloadObject = profile.IsChatGptPlan
            ? new { model = profile.Model, messages, stream = true,
                    yanzi_mode = mode == AiConversationMode.Agent ? "agent" : "chat",
                    verbosity = mode == AiConversationMode.Agent ? "medium" : "high",
                    reasoning_effort = mode == AiConversationMode.Agent ? "medium" : "high" }
            : new { model = profile.Model, messages, stream = true };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(JsonSerializer.Serialize(payloadObject), Encoding.UTF8, "application/json");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        HostAssets.AppendLog($"AI SSE request start: model={profile.Model}, messages={messages.Count}, agent={mode == AiConversationMode.Agent}");
        using var response = await _aiHttpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            HostAssets.AppendLog($"AI SSE failed: model={profile.Model}, status={(int)response.StatusCode}, ms={timer.ElapsedMilliseconds}, body={TrimForLog(body, 280)}");
            // Retry as a non-streaming request ONLY when the provider explicitly rejected
            // the stream option. Never retry 5xx/504: the upstream may still be executing.
            var streamRejected = (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
                                  response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity) &&
                                 body.Contains("stream", StringComparison.OrdinalIgnoreCase) &&
                                 (body.Contains("not support", StringComparison.OrdinalIgnoreCase) ||
                                  body.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                                  body.Contains("不支持", StringComparison.OrdinalIgnoreCase));
            if (streamRejected)
            {
                HostAssets.AppendLog($"AI provider does not support SSE: model={profile.Model}; falling back once.");
                return await RequestAiChatCompletionCompatibilityAsync(messages, profile, mode, onDelta, cancellationToken);
            }
            throw new InvalidOperationException($"供应商返回 {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        // A few compatible gateways ignore stream=true and return one regular JSON response.
        // Use that answer instead of silently displaying an empty stream.
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<AiChatCompletionResponse>(json, JsonDefaults.CaseInsensitive);
            var completed = parsed?.Choices?.Select(c => c.Message?.Content)
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
            if (string.IsNullOrWhiteSpace(completed)) throw new IOException("供应商忽略流式请求且未返回内容");
            onDelta(completed);
            return completed;
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await AiChatSseParser.ReadAsync(stream, onDelta, cancellationToken);
        HostAssets.AppendLog($"AI SSE success: model={profile.Model}, bytes={result.Length}, ms={timer.ElapsedMilliseconds}");
        return result;
    }

    private async Task<string> RequestAiChatCompletionCompatibilityAsync(
        IReadOnlyList<object> messages, AiRequestProfile profile, AiConversationMode mode,
        Action<string> onDelta, CancellationToken cancellationToken)
    {
        var endpoint = BuildAiChatEndpoint(profile.BaseUrl);
        object payload = profile.IsChatGptPlan
            ? new { model = profile.Model, messages,
                    yanzi_mode = mode == AiConversationMode.Agent ? "agent" : "chat",
                    verbosity = mode == AiConversationMode.Agent ? "medium" : "high",
                    reasoning_effort = mode == AiConversationMode.Agent ? "medium" : "high" }
            : new { model = profile.Model, messages };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _aiHttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"供应商兼容请求失败：{(int)response.StatusCode} {response.ReasonPhrase}");
        var parsed = JsonSerializer.Deserialize<AiChatCompletionResponse>(body, JsonDefaults.CaseInsensitive);
        var text = parsed?.Choices?.Select(choice => choice.Message?.Content)
            .FirstOrDefault(content => !string.IsNullOrWhiteSpace(content))?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new IOException("供应商兼容请求返回空内容");
        onDelta(text);
        return text;
    }

    private const string DEEP_CHAT_SYSTEM_PROMPT =
        "你是“燕子”的深度对话助手。当前是 Chat 模式：只进行理解、分析、解释和讨论，不调用本地电脑工具，也不要输出工具调用 JSON。\n" +
        "目标不是给出一个勉强够用的摘要，而是准确理解用户真正想解决的问题，并在问题值得展开时给出充分、具体、有因果关系的回答。\n\n" +
        "回答原则：\n" +
        "1. 简单事实问题可以简洁；开放性、哲学性、研究性、机制性问题默认充分展开，不要因为模型偏好简洁而过早收尾。\n" +
        "2. 对复杂问题，优先按“核心问题 → 贴近现实的具体案例 → 因果推演 → 核心规律 → 迁移应用”组织，避免一开始堆砌术语和孤立知识点。\n" +
        "3. 重要结论要解释为什么成立，指出条件、边界、反例或不确定性；不要只罗列若干因素就结束。\n" +
        "4. 当用户在建立长期理论或世界模型时，要主动连接前后逻辑，帮助形成可复述、可检验、可迁移的结构。\n" +
        "5. 不要为了显得详细而重复同一句话；详细意味着增加有效机制、例子、条件和推演。\n" +
        "6. 除非用户明确要求短答，否则优先完整回答其隐含问题，而不仅回答字面问题。";

    private const string DEFAULT_SYSTEM_PROMPT = 
        "你是燕子电脑端 AI 助手。你可以解答问题，也可以调用本地电脑端工具。\n" +
        "你可以自主判断是否需要调用工具。如果需要调用工具，请输出一段包裹在 ```json 内部的 JSON 代码块：\n" +
        "```json\n" +
        "{\"tool\": \"工具名\", \"参数名\": \"参数值\"}\n" +
        "```\n\n" +
        "【工具调用示例】\n" +
        "用户：查看插件列表\n" +
        "AI回复：\n" +
        "```json\n" +
        "{\"tool\": \"query_extensions\"}\n" +
        "```\n" +
        "系统反馈：\n" +
        "[{\"id\": \"ext_calculator\", \"name\": \"计算器\"}, {\"id\": \"ext_weather\", \"name\": \"天气助手\"}]\n" +
        "AI回复：\n" +
        "目前已安装的插件列表如下：\n" +
        "1. 计算器 (ID: ext_calculator)\n" +
        "2. 天气助手 (ID: ext_weather)\n" +
        "你可以告诉我你想执行哪一个。\n\n" +
        "【可用工具列表】\n" +
        "1. query_extensions: 获取可用小程序列表。无参数。\n" +
        "2. execute_extension: 执行某个扩展. 参数: id (扩展ID)。\n" +
        "3. execute_command: 在电脑端执行命令行命令。参数: command (要执行的命令文本)。【重要】电脑端已默认在 PowerShell 5.1 环境中执行命令，请直接输入 PowerShell 的 Cmdlet 或表达式，严禁外层嵌套调用 powershell、powershell.exe -Command 或 cmd /c，避免转义错误和执行超时。\n\n" +
        "【注意】如果你调用了工具，系统会在后台真实执行，并在执行完成后将真实的结果反馈给你，之后你再根据执行结果来决定是继续调用工具还是输出最终的自然语言回复。";

    private IReadOnlyList<object> BuildAiRequestMessages(AiConversationMode conversationMode, AiChatTopic requestTopic)
    {
        var settings = AppSettingsStore.Load();

        if (conversationMode == AiConversationMode.Chat)
        {
            var chatMessages = new List<object>
            {
                new
                {
                    role = "system",
                    content = DEEP_CHAT_SYSTEM_PROMPT
                }
            };

            foreach (var message in requestTopic.Messages)
            {
                if (message.IsToolCall)
                {
                    continue;
                }

                chatMessages.Add(new
                {
                    role = message.IsUser ? "user" : "assistant",
                    content = message.BuildRequestContent()
                });
            }

            return chatMessages;
        }

        var basePrompt = string.IsNullOrWhiteSpace(settings.AiSystemPrompt)
            ? DEFAULT_SYSTEM_PROMPT
            : settings.AiSystemPrompt;

        // 获取可用小程序列表
        var extList = new List<object>();
        foreach (var cmd in GetExtensionsForSettings())
        {
            extList.Add(new
            {
                id = cmd.ExtensionId,
                name = cmd.Title,
                desc = cmd.Subtitle
            });
        }
        var extListJson = JsonSerializer.Serialize(extList);

        var finalPrompt = "【系统指令（严格遵守）】\n" + basePrompt +
                          "\n\n" + YanziCapabilityRequirementResolver.BuildAuthoringPrompt() +
                          "\n当前可用小程序有:\n" + extListJson;

        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = finalPrompt
            }
        };

        foreach (var message in requestTopic.Messages)
        {
            messages.Add(new
            {
                role = message.IsUser ? "user" : "assistant",
                content = message.BuildRequestContent()
            });

            if (message.IsToolCall && !string.IsNullOrWhiteSpace(message.ToolFeedback))
            {
                messages.Add(new
                {
                    role = "user",
                    content = message.ToolFeedback
                });
            }
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
        if (trimmed.Equals("https://generativelanguage.googleapis.com/v1beta", StringComparison.OrdinalIgnoreCase))
            return $"{trimmed}/openai/chat/completions";
        if (trimmed.Equals("https://generativelanguage.googleapis.com/v1beta/openai", StringComparison.OrdinalIgnoreCase))
            return $"{trimmed}/chat/completions";
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
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
                    isPinned = t.IsPinned,
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

                if (topicElement.TryGetProperty("isPinned", out var isPinnedElement))
                {
                    topic.IsPinned = isPinnedElement.GetBoolean();
                }

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

                        DateTimeOffset? restoredTime = null;
                        if (msgElement.TryGetProperty("timestamp", out var timeElement) &&
                            timeElement.ValueKind == JsonValueKind.String &&
                            timeElement.TryGetDateTimeOffset(out var originalTime))
                            restoredTime = originalTime;
                        topic.Messages.Add(new AiChatMessage(isUser, text, attachments, restoredTime));
                    }
                }

                _aiChatTopics.Add(topic);
            }

            ReorderTopics();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Load topics failed: {ex.Message}");
        }
    }

    // ==================== 公共接口 ====================
    
    public void SaveAiSettings(string baseUrl, string apiKey, string model, string systemPrompt)
    {
        var settings = AppSettingsStore.Load();
        settings.AiBaseUrl = baseUrl;
        settings.AiApiKey = apiKey;
        settings.AiModel = model;
        settings.AiSystemPrompt = systemPrompt;
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        NotifyQuickPanelSettingsChanged("ai-settings-saved", refreshYanmOverlay: false);
        OnPropertyChanged(nameof(AiChatModelDisplayText));
    }
}

// ==================== 数据模型 ====================

public sealed class AiChatMessage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _text;
    private bool _isToolCall;
    private string? _toolName;
    private string? _toolFeedback;
    private bool _isExpanded = true; // 默认展开，让用户能够直观看到执行状态
    private bool _isStreaming;
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (_isStreaming == value) return;
            _isStreaming = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedText));
            OnPropertyChanged(nameof(StreamingTextVisibility));
        }
    }
    public string FormattedText => IsStreaming ? string.Empty : Text;
    public Visibility StreamingTextVisibility => IsStreaming ? Visibility.Visible : Visibility.Collapsed;
    private bool _showTimeDivider;

    public bool IsToolCall
    {
        get => _isToolCall;
        set { _isToolCall = value; OnPropertyChanged(); }
    }

    public string? ToolName
    {
        get => _toolName;
        set { _toolName = value; OnPropertyChanged(); }
    }

    public string? ToolFeedback
    {
        get => _toolFeedback;
        set { _toolFeedback = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public AiChatMessage(bool isUser, string text,
        IEnumerable<AiChatMessageAttachment>? attachments = null,
        DateTimeOffset? timestamp = null)
    {
        Id = Guid.NewGuid().ToString();
        IsUser = isUser;
        _text = text;
        Timestamp = timestamp ?? DateTimeOffset.Now;
        Attachments = attachments?.ToList() ?? [];
    }

    public string Id { get; }
    public bool IsUser { get; }
    public string Text
    {
        get => _text;
        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
            {
                return;
            }

            _text = value;
            OnPropertyChanged();
            if (!IsStreaming) OnPropertyChanged(nameof(FormattedText));
        }
    }
    public DateTimeOffset Timestamp { get; }
    public bool ShowTimeDivider
    {
        get => _showTimeDivider;
        set
        {
            if (_showTimeDivider == value) return;
            _showTimeDivider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeDividerVisibility));
        }
    }
    public static bool NeedsTimeDivider(AiChatMessage? previous, AiChatMessage current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return previous is null ||
               current.Timestamp.ToLocalTime().Date != previous.Timestamp.ToLocalTime().Date ||
               current.Timestamp - previous.Timestamp >= TimeSpan.FromMinutes(3) ||
               current.Timestamp < previous.Timestamp;
    }

    public Visibility TimeDividerVisibility => ShowTimeDivider ? Visibility.Visible : Visibility.Collapsed;
    public string TimeDividerText =>
        Timestamp.ToLocalTime().Date == DateTime.Now.Date
            ? Timestamp.ToLocalTime().ToString("HH:mm")
            : Timestamp.ToLocalTime().ToString("yyyy年M月d日 HH:mm");
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

    private bool _isGenerating;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            if (_isGenerating == value) return;
            _isGenerating = value;
            OnPropertyChanged();
        }
    }

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

    private bool _isPinned;
    private bool _isDeleteConfirming;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDeleteConfirming
    {
        get => _isDeleteConfirming;
        set
        {
            if (_isDeleteConfirming != value)
            {
                _isDeleteConfirming = value;
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
