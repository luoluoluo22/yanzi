using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private AiChatLogWindow? _aiChatLogWindow;
    private AiChatTopic? _selectedAiChatTopic;
    private readonly HttpClient _aiHttpClient = new() { Timeout = TimeSpan.FromSeconds(300) };
    private static readonly TimeSpan AiModelDiscoveryInterval = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, DateTimeOffset> _aiModelDiscoveryAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _aiModelDiscoveryInFlight = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _aiModelDiscoveryTimer;
    private DispatcherTimer? _aiTopicAgeTimer;
    private string _aiChatInputText = string.Empty;
    private bool _aiChatInputCompositionHooked;
    private bool _aiChatInputComposing;
    private int _aiChatInputCompositionGeneration;
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
                if (IsAiChatMode)
                    MarkTopicRead(_selectedAiChatTopic);
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

    public Visibility AiChatInputPlaceholderVisibility =>
        !_aiChatInputComposing && string.IsNullOrWhiteSpace(AiChatInputText)
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

    public string AiChatModelDisplayText => ResolveAiRequestProfile(_appSettings)?.Model ?? "AI 未配置";

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
    
    // Chat body text is 22px per line. A pixel-virtualized list otherwise
    // treats one wheel notch as a single 16px step, about one line.
    public static double AiChatWheelScrollDistance(int delta) => delta / 120d * 5d * 22d;

    private void AiChatMessagesList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (TryScrollAiChatTranscript(e.Delta))
        {
            e.Handled = true;
        }
    }

    private void AiChatMarkdown_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || AiChatMessagesList == null)
        {
            return;
        }

        // The markdown viewer is a document scroller. It consumes the wheel even
        // when its own bars are disabled, so the conversation list never moves.
        if (TryScrollAiChatTranscript(e.Delta))
        {
            e.Handled = true;
        }
    }

    private bool TryScrollAiChatTranscript(int delta)
    {
        if (AiChatMessagesList == null)
        {
            return false;
        }

        var distance = AiChatWheelScrollDistance(delta);
        if (Math.Abs(distance) < 0.5)
        {
            return false;
        }

        var scrollViewer = FindVisualChildren<System.Windows.Controls.ScrollViewer>(AiChatMessagesList).FirstOrDefault();
        if (scrollViewer == null)
        {
            return false;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - distance);
        return true;
    }

    private void AiChatMarkdown_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Selecting text asks every caret position to scroll into view. That request
        // bubbles into the conversation list and remeasures it on each mouse move.
        if (AiChatMessagesList == null)
        {
            e.Handled = true;
            return;
        }

        var position = Mouse.GetPosition(AiChatMessagesList);
        var nearEdge = position.Y < 28 || position.Y > AiChatMessagesList.ActualHeight - 28;
        if (!nearEdge)
        {
            e.Handled = true;
        }
    }

    private void AiChatMarkdown_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (TryCopyMarkdownSelection(sender, "key"))
        {
            e.Handled = true;
        }
    }

    private void AiChatMarkdown_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy)
        {
            return;
        }

        if (TryCopyMarkdownSelection(sender, "command"))
        {
            e.Handled = true;
        }
    }

    private static bool TryCopyMarkdownSelection(object sender, string source)
    {
        if (sender is not FlowDocumentScrollViewer viewer)
        {
            return false;
        }

        var text = viewer.Selection?.Text;
        if (viewer.Selection is { IsEmpty: false } && !AiChatEmoji.ContainsEmoji(text))
        {
            var withEmoji = AiChatEmoji.ReadPlainText(viewer.Selection.Start, viewer.Selection.End);
            if (AiChatEmoji.ContainsEmoji(withEmoji))
                text = withEmoji;
        }
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // FlowDocument's own Copy builds RTF, XAML and HTML, then flushes the clipboard
        // on this same UI thread. Plain text is written once, with no flush and no retry sleep.
        var started = Stopwatch.GetTimestamp();
        var ok = ClipboardService.TrySetPlainTextFast(text, out var error);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HostAssets.AppendLog($"AI chat selection copy: source={source}, chars={text.Length}, elapsedMs={elapsed:F1}, ok={ok}, error={error}");
        return ok;
    }

    private void AiChatInputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control &&
            TryCaptureClipboardAttachment(out var files, out var bitmap))
        {
            e.Handled = true;
            _ = AddCapturedClipboardAttachmentAsync(files, bitmap);
            return;
        }

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
            // Enter confirms the current IME candidate; don't send until it is committed.
            if (_aiChatInputComposing)
                return;
            SubmitAiChatMessage();
            e.Handled = true;
        }
    }

    private void AiChatInputBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (_aiChatInputCompositionHooked || sender is not System.Windows.Controls.TextBox input) return;
        _aiChatInputCompositionHooked = true;
        TextCompositionManager.AddPreviewTextInputStartHandler(input, AiChatInputBox_CompositionStarted);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(input, AiChatInputBox_CompositionUpdated);
        TextCompositionManager.AddPreviewTextInputHandler(input, AiChatInputBox_CompositionCompleted);
    }

    private void AiChatInputBox_CompositionStarted(object sender, TextCompositionEventArgs e) =>
        BeginAiChatInputComposition();

    private void AiChatInputBox_CompositionUpdated(object sender, TextCompositionEventArgs e)
    {
        if (HasPendingAiChatComposition(e))
            BeginAiChatInputComposition();
        else
            EndAiChatInputComposition();
    }

    private void AiChatInputBox_CompositionCompleted(object sender, TextCompositionEventArgs e) =>
        EndAiChatInputComposition();

    private static bool HasPendingAiChatComposition(TextCompositionEventArgs e)
    {
        var composition = e.TextComposition;
        if (composition == null) return false;
        if (!string.IsNullOrEmpty(composition.CompositionText) || !string.IsNullOrEmpty(composition.Text))
            return true;
        return composition.AutoComplete == TextCompositionAutoComplete.Off;
    }

    private void BeginAiChatInputComposition()
    {
        _aiChatInputCompositionGeneration++;
        if (_aiChatInputComposing) return;
        _aiChatInputComposing = true;
        OnPropertyChanged(nameof(AiChatInputPlaceholderVisibility));
    }

    private void EndAiChatInputComposition()
    {
        if (!_aiChatInputComposing) return;
        var generation = _aiChatInputCompositionGeneration;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _aiChatInputCompositionGeneration || !_aiChatInputComposing) return;
            _aiChatInputComposing = false;
            OnPropertyChanged(nameof(AiChatInputPlaceholderVisibility));
        }, DispatcherPriority.DataBind);
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

    private void AiChatLogButton_Click(object sender, RoutedEventArgs e)
    {
        SuppressAutoHideFor(TimeSpan.FromSeconds(2));
        if (_aiChatLogWindow == null || !_aiChatLogWindow.IsLoaded)
        {
            _aiChatLogWindow = new AiChatLogWindow(this, () => _selectedAiChatTopic?.Id);
            _aiChatLogWindow.Closed += (_, _) => _aiChatLogWindow = null;
            _aiChatLogWindow.Show();
        }
        _aiChatLogWindow.Activate();
    }

    private void AiChatNewTopicButton_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTopic();
    }

    private void AiChatNewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择项目文件",
            Filter = "所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var files = dialog.FileNames
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            return;

        var firstName = Path.GetFileName(files[0]);
        var title = files.Length == 1 ? firstName : $"{firstName} 等 {files.Length} 个文件";
        var topic = new AiChatTopic(string.IsNullOrWhiteSpace(title) ? "新项目" : title);
        topic.SetProjectFiles(files);
        _aiChatTopics.Insert(0, topic);
        SelectTopic(topic);
        ReorderTopics();
        SaveTopicsToStorage();
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

    private static bool TryCaptureClipboardAttachment(out string[] files, out System.Windows.Media.Imaging.BitmapSource? bitmap)
    {
        files = [];
        bitmap = null;
        try
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data == null)
            {
                return false;
            }

            var hasFiles = data.GetDataPresent(System.Windows.DataFormats.FileDrop);
            var hasImage = data.GetDataPresent(System.Windows.DataFormats.Bitmap) ||
                           data.GetDataPresent(System.Windows.DataFormats.Dib) ||
                           data.GetDataPresent("PNG");
            if (!hasFiles && !hasImage)
            {
                return false;
            }

            // A text selection wins over an incidental image format so ordinary Ctrl+V still inserts text.
            if (!hasFiles &&
                data.GetDataPresent(System.Windows.DataFormats.UnicodeText) &&
                data.GetData(System.Windows.DataFormats.UnicodeText) is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!MobileMessageAttachmentInput.TryReadPastedAttachments(data, out bitmap, out files))
            {
                return false;
            }

            if (bitmap is { CanFreeze: true, IsFrozen: false })
            {
                bitmap.Freeze();
            }

            return bitmap != null || files.Length > 0;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog("AI chat clipboard attachment probe failed: " + ex.GetType().Name + " " + ex.Message);
            return false;
        }
    }

    private async Task AddCapturedClipboardAttachmentAsync(string[] files, System.Windows.Media.Imaging.BitmapSource? bitmap)
    {
        try
        {
            if (bitmap != null)
            {
                var path = MobileMessageAttachmentInput.SavePastedBitmap(
                    bitmap,
                    HostAssets.PastedImagesPath,
                    MaxAttachmentSize,
                    "粘贴的图片-");
                await AddAttachmentsFromPathsAsync([path]);
                return;
            }

            if (files.Length > 0)
            {
                await AddAttachmentsFromPathsAsync(files);
            }
        }
        catch (Exception ex)
        {
            AiChatStatusText = "粘贴附件失败：" + ex.Message;
            HostAssets.AppendLog("AI chat paste attachment failed: " + ex.Message);
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

    private bool _loadingAiMessages, _aiScrollPosted;
    private void AiChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_loadingAiMessages) return;
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == _aiChatMessages.Count - 1)
        {
            var last = _aiChatMessages[^1];
            last.ShowTimeDivider = AiChatMessage.NeedsTimeDivider(
                _aiChatMessages.Count > 1 ? _aiChatMessages[^2] : null, last);
        }
        else UpdateAiChatTimeDividers();
        OnPropertyChanged(nameof(VisibleCountText));
        QueueAiScrollToEnd();
    }

    private void QueueAiScrollToEnd()
    {
        if (_aiScrollPosted) return;
        _aiScrollPosted = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _aiScrollPosted = false;
            if (_aiChatMessages.Count > 0) AiChatMessagesList?.ScrollIntoView(_aiChatMessages[^1]);
        }, DispatcherPriority.Background);
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

    private void MarkTopicRead(AiChatTopic topic)
    {
        if (!topic.HasUnreadReply)
            return;
        topic.HasUnreadReply = false;
        SaveTopicsToStorage();
    }

    private void NoteTopicReplyReadState(AiChatTopic topic)
    {
        if (IsAiChatMode && ReferenceEquals(_selectedAiChatTopic, topic))
        {
            topic.HasUnreadReply = false;
            return;
        }

        if (topic.Messages.LastOrDefault() is { IsUser: false })
            topic.HasUnreadReply = true;
    }

    private void StartAiTopicAgeRefresh()
    {
        RefreshAiTopicAges();
        if (_aiTopicAgeTimer == null)
        {
            _aiTopicAgeTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _aiTopicAgeTimer.Tick += (_, _) => RefreshAiTopicAges();
        }

        _aiTopicAgeTimer.Start();
    }

    private void StopAiTopicAgeRefresh() => _aiTopicAgeTimer?.Stop();

    private void RefreshAiTopicAges()
    {
        foreach (var topic in _aiChatTopics)
            topic.RefreshUpdatedAtLabel();
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
        _loadingAiMessages = true;
        try
        {
            _aiChatMessages.Clear();
            if (_selectedAiChatTopic != null)
                foreach (var message in _selectedAiChatTopic.Messages) _aiChatMessages.Add(message);
        }
        finally { _loadingAiMessages = false; }
        UpdateAiChatTimeDividers();
        OnPropertyChanged(nameof(VisibleCountText));
        QueueAiScrollToEnd();
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

    private Task<string[]?> ShowAttachmentPickerAsync()
    {
        // Owned modal picker: stays above its warehouse without forcing the warehouse topmost.
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "所有文件|*.*|文本文件|*.txt;*.md;*.json;*.xml;*.cs;*.js;*.py|图片文件|*.png;*.jpg;*.jpeg;*.gif"
        };

        var result = dialog.ShowDialog(this);
        return Task.FromResult<string[]?>(result == true ? dialog.FileNames : null);
    }

    // ==================== 消息操作 ====================
    
    private void ShowMessageContextMenu(AiChatMessage message, FrameworkElement placementTarget)
    {
        var menu = CreateAiChatMenu();
        menu.AddAction("复制内容", () => CopyMessage(message));
        if (message.HasHtmlContent)
            menu.AddAction("复制网页内容（富文本）", () => CopyHtmlMessage(message));
        if (message.HasHtmlPreview)
            menu.AddAction("预览网页（SVG / CSS 动画）", () => PreviewHtmlMessage(message, placementTarget));
        if (message.IsUser)
        {
            menu.AddAction("编辑", () => EditUserMessage(message));
            menu.AddAction("重新发送", () => ResendMessage(message));
        }
        OpenAiChatContextMenu(menu, placementTarget);
    }

    private void AiChatCopyMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatMessage message })
        {
            CopyMessage(message);
        }
    }

    private void AiChatEditMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatMessage message })
            EditUserMessage(message);
    }

    private void AiChatResendMessageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatMessage message })
            ResendMessage(message);
    }

    private const int UserMessageActionHideDelayMs = 450;
    private DispatcherTimer? _userMessageActionHideTimer;
    private Grid? _userMessageActionRoot;
    private Popup? _userMessageActionPopup;

    private static readonly CustomPopupPlacementCallback UserMessagePopupPlacement = (popupSize, targetSize, _) =>
    {
        var x = targetSize.Width - popupSize.Width;
        return [new CustomPopupPlacement(new System.Windows.Point(x, targetSize.Height + 4), PopupPrimaryAxis.Horizontal)];
    };

    private void UserMessageRoot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Grid root) return;
        _userMessageActionHideTimer?.Stop();
        if (!ReferenceEquals(_userMessageActionRoot, root))
            HideUserMessageActions();
        ShowUserMessageActions(root);
    }

    private void UserMessageRoot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Grid root || !ReferenceEquals(root, _userMessageActionRoot)) return;
        ScheduleUserMessageActionHide();
    }

    private void UserMessageActions_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _userMessageActionHideTimer?.Stop();
    }

    private void UserMessageActions_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ScheduleUserMessageActionHide();
    }

    private void UserMessageRoot_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Grid root)
            ReleaseUserMessageActions(root);
    }

    private void UserMessageRoot_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid root)
            ReleaseUserMessageActions(root);
    }

    private void ScheduleUserMessageActionHide()
    {
        _userMessageActionHideTimer ??= CreateUserMessageActionHideTimer();
        _userMessageActionHideTimer.Stop();
        _userMessageActionHideTimer.Start();
    }

    private DispatcherTimer CreateUserMessageActionHideTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UserMessageActionHideDelayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_userMessageActionRoot?.IsMouseOver == true || _userMessageActionPopup?.IsMouseOver == true)
                return;
            HideUserMessageActions();
        };
        return timer;
    }

    private void ShowUserMessageActions(Grid root)
    {
        if (FindNamedDescendant<Popup>(root, "UserMessageActionPopup") is not { } popup)
            return;
        var bubble = FindNamedDescendant<Border>(root, "UserMessageBubble");
        popup.PlacementTarget = bubble is UIElement target ? target : root;
        popup.Placement = PlacementMode.Custom;
        popup.CustomPopupPlacementCallback = UserMessagePopupPlacement;
        popup.DataContext = root.DataContext;
        popup.IsOpen = true;
        _userMessageActionPopup = popup;
        _userMessageActionRoot = root;
    }

    private void HideUserMessageActions()
    {
        if (_userMessageActionRoot != null)
            ReleaseUserMessageActions(_userMessageActionRoot);
    }

    private void ReleaseUserMessageActions(Grid root)
    {
        var popup = FindNamedDescendant<Popup>(root, "UserMessageActionPopup");
        if (popup != null)
            popup.IsOpen = false;
        if (ReferenceEquals(root, _userMessageActionRoot) || ReferenceEquals(popup, _userMessageActionPopup))
        {
            _userMessageActionHideTimer?.Stop();
            _userMessageActionRoot = null;
            _userMessageActionPopup = null;
        }
    }

    private static T? FindNamedDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && match.Name == name)
                return match;
            var nested = FindNamedDescendant<T>(child, name);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T match)
                return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void AiChatCopyHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatMessage message })
            CopyHtmlMessage(message);
    }

    private void AiChatPreviewHtmlButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AiChatMessage message } target)
            PreviewHtmlMessage(message, target);
    }

    private void PreviewHtmlMessage(AiChatMessage message, FrameworkElement target)
    {
        if (!AiChatHtmlPreviewContent.TryExtractHtml(message.Text, out var html)) return;
        try
        {
            var preview = new AiChatHtmlPreviewWindow(html);
            var owner = Window.GetWindow(target);
            if (owner?.IsVisible == true) preview.Owner = owner;
            preview.Show();
            preview.Activate();
        }
        catch (Exception ex)
        {
            AiChatStatusText = $"网页预览失败：{ex.Message}";
            HostAssets.AppendLog("AI chat HTML preview failed: " + ex);
        }
    }

    private void CopyHtmlMessage(AiChatMessage message)
    {
        if (!message.HasHtmlContent) return;
        try
        {
            var content = AiChatHtmlPreviewContent.TryExtractHtml(message.Text, out var html)
                ? html : message.Text;
            AiChatHtmlContent.CopyRich(content);
            AiChatStatusText = "已复制网页内容（保留富文本格式）";
        }
        catch (Exception ex)
        {
            AiChatStatusText = $"复制网页内容失败：{ex.Message}";
        }
    }

    private void CopyMessage(AiChatMessage message)
    {
        try
        {
            var visible = message.IsUser
                ? message.Text
                : AiChatHtmlPreviewContent.NormalizeCodeBlocks(AiChatGptMarkup.Normalize(message.Text));
            ClipboardService.SetText(visible);
            AiChatStatusText = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            AiChatStatusText = $"复制失败：{ex.Message}";
        }
    }

    private void EditUserMessage(AiChatMessage message)
    {
        if (!TryReturnUserMessageToComposer(message, out _))
            return;
        FocusAiInput();
        AiChatStatusText = "已放回输入框";
    }

    private void ResendMessage(AiChatMessage message)
    {
        if (IsAiChatRequestInFlight)
        {
            AiChatStatusText = "此对话正在生成，请稍后再重发";
            return;
        }

        if (!IsAiConfigured(_appSettings))
        {
            AiChatStatusText = "无法发送：当前服务商尚未配置完成";
            return;
        }

        var text = message.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) && message.Attachments.Count == 0)
            return;

        HideUserMessageActions();
        if (!RemoveMessagesFrom(message))
            return;
        RestorePendingAttachmentsFromMessage(message);
        AiChatInputText = text;
        if (AiChatInputBox != null)
            AiChatInputBox.Text = text;
        // The clicked popup is closing as its message leaves the list. Send on the
        // next turn with the captured text, so a single-message thread still goes out.
        Dispatcher.BeginInvoke(() =>
        {
            AiChatInputText = text;
            if (AiChatInputBox != null)
                AiChatInputBox.Text = text;
            SubmitAiChatMessage(text);
        }, DispatcherPriority.Background);
    }

    private bool TryReturnUserMessageToComposer(AiChatMessage message, out string text)
    {
        text = message.Text ?? string.Empty;
        if (IsAiChatRequestInFlight)
        {
            AiChatStatusText = "此对话正在生成，请稍后再操作";
            return false;
        }

        RemoveMessageForResend(message);
        AiChatInputText = text;
        if (AiChatInputBox != null)
            AiChatInputBox.Text = text;
        RestorePendingAttachmentsFromMessage(message);
        return true;
    }

    // ==================== 核心功能 ====================
    
    private void ActivateAiChatMode()
    {
        if (_detachedAiChatWindow != null)
        {
            ActivateDetachedAiChat();
            return;
        }
        LoadTopicsFromStorage();
        _appSettings = AppSettingsStore.Load();
        RefreshAiModelSelector();
        StartAiModelDiscovery();
        
        // 切换到 AI Chat 模式时，调整窗口大小以获得更好的阅读体验
        if (WindowState == WindowState.Normal)
        {
            // 窗口以中心向两侧扩展；顶部标签栏由弹窗独立补偿，保持屏幕位置不变
            var geometry = AiWindowResizeGeometry.KeepCenterAndScopeTabs(
                Left, Width, 1100, _scopeTabsHorizontalCompensation);
            _scopeTabsHorizontalCompensation = geometry.PopupOffset;
            var anchorTop = Top;  // 使用顶部位置
            
            // 调整窗口大小
            Width = 1100;
            Height = 720;
            MinWidth = 900;
            MinHeight = 600;
            
            // 根据原中心位置计算左边缘，左右各扩展一半
            Left = geometry.Left;
            Top = anchorTop;
            RepositionSearchScopePopup();
        }
        
        if (_aiChatTopics.Count == 0)
        {
            CreateNewTopic();
        }
        else if (_selectedAiChatTopic == null)
        {
            SelectTopic(_aiChatTopics.First());
        }

        if (_selectedAiChatTopic != null)
            MarkTopicRead(_selectedAiChatTopic);
        StartAiTopicAgeRefresh();

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
            .Where(provider => provider.IsEnabled)
            .OrderBy(provider => string.Equals(provider.Id, _appSettings.ActiveServiceProviderId,
                StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToArray();
        if (providers.Length == 0)
        {
            menu.AddLabel("尚未启用模型供应商");
            menu.AddSeparator();
            menu.AddAction("配置供应商…", () => AiChatSettingsButton_Click(anchor, new RoutedEventArgs()));
        }
        else
        {
            var widestModel = providers.SelectMany(provider => VisibleProviderModels(provider))
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .DefaultIfEmpty("").MaxBy(model => model.Length) ?? "";
            // Give full model IDs enough room, including provider-specific suffixes.
            menu.SubmenuWidth = Math.Clamp(widestModel.Length * 9 + 58, 194, 420);
            menu.SubmenuMaxHeight = 440;
            foreach (var provider in providers)
            {
                var providerId = provider.Id;
                var preferredModel = string.Equals(providerId, _appSettings.ActiveServiceProviderId,
                    StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_appSettings.AiModel)
                    ? _appSettings.AiModel : provider.SelectedModel;
                var hidden = new HashSet<string>(provider.HiddenModelIds ?? [], StringComparer.OrdinalIgnoreCase);
                var configured = new HashSet<string>(
                    (provider.Models ?? []).Where(model => !hidden.Contains(model)),
                    StringComparer.OrdinalIgnoreCase);
                var models = AiModelMenuOrdering.Order(
                    configured,
                    (provider.DiscoveredModels ?? []).Where(model => !hidden.Contains(model)),
                    preferredModel);
                var isActive = string.Equals(providerId, _appSettings.ActiveServiceProviderId,
                    StringComparison.OrdinalIgnoreCase);
                var item = menu.AddSubmenu(provider.Name, submenu =>
                {
                    if (models.Length == 0)
                    {
                        submenu.AddLabel("暂无模型，请先在设置中添加");
                        return;
                    }
                    var lastSection = string.Empty;
                    foreach (var model in models)
                    {
                        var modelId = model;
                        var selected = string.Equals(modelId, preferredModel,
                            StringComparison.OrdinalIgnoreCase);
                        var section = selected ? "当前模型" : configured.Contains(modelId)
                            ? "已配置模型" : "其他可用模型";
                        if (!string.Equals(section, lastSection, StringComparison.Ordinal))
                        {
                            if (lastSection.Length > 0) submenu.AddSeparator();
                            submenu.AddLabel(section);
                            lastSection = section;
                        }
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

    private static IEnumerable<string> VisibleProviderModels(AiServiceProviderSettings provider)
    {
        var hidden = new HashSet<string>(provider.HiddenModelIds ?? [], StringComparer.OrdinalIgnoreCase);
        return (provider.Models ?? []).Concat(provider.DiscoveredModels ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model) && !hidden.Contains(model));
    }

    private void SelectAiChatProviderModel(string providerId, string model)
    {
        var settings = AppSettingsStore.Load();
        var provider = settings.AiServiceProviders?.FirstOrDefault(item =>
            item.IsEnabled && string.Equals(item.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (provider == null || !(provider.Models ?? []).Concat(provider.DiscoveredModels ?? []).Any(candidate =>
                string.Equals(candidate, model, StringComparison.OrdinalIgnoreCase)))
            return;

        provider.Models ??= [];
        provider.HiddenModelIds?.RemoveAll(id => string.Equals(id, model, StringComparison.OrdinalIgnoreCase));
        if (!provider.Models.Contains(model, StringComparer.OrdinalIgnoreCase))
            provider.Models.Add(model);
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
            if (!SettingsAiController.SameModelIds(target.DiscoveredModels, pinnedModels))
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
                        if (!SettingsAiController.SameModelIds(latest.DiscoveredModels, updated)) latest.DiscoveredModels = updated;
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


    private async void SubmitAiChatMessage(string? inputOverride = null)
    {
        if (_selectedAiChatTopic == null) CreateNewTopic();
        var topic = _selectedAiChatTopic!;
        // Two fast Enter presses in the same topic are rejected, but other topics remain interactive.
        if (!_aiRequestTracker.TryPrepare(topic.Id))
            return;
        RefreshAiTopicRequestState();
        try
        {
            await SubmitAiChatMessageCore(topic, inputOverride: inputOverride);
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

    private async Task SubmitAiChatMessageCore(AiChatTopic topic, string? scheduledPrompt = null, string? inputOverride = null)
    {
        // Snapshot editor, attachments, mode, and provider before the first await: topic switching
        // or model changes cannot redirect a request already in progress.
        var userInput = (scheduledPrompt ?? inputOverride ?? (AiChatInputBox?.Text ?? AiChatInputText)).Trim();
        var pendingAttachments = scheduledPrompt == null ? _aiChatAttachments.ToArray() : [];
        if (string.IsNullOrEmpty(userInput) && pendingAttachments.Length == 0) return;
        var mode = DetermineAiConversationMode(userInput, topic);
        // Refresh before every request so a live chat observes settings changes.
        // An in-flight request keeps its own immutable profile.
        var settings = AppSettingsStore.Load();
        _appSettings = settings;
        RefreshAiModelSelector();
        var profile = ResolveAiRequestProfile(settings);
        if (profile is null)
        {
            var selected = (settings.AiServiceProviders ?? []).FirstOrDefault(provider =>
                string.Equals(provider.Id, settings.ActiveServiceProviderId, StringComparison.OrdinalIgnoreCase));
            var msg = selected is null || !selected.IsEnabled
                ? "无法发送：没有已启用的当前模型渠道，请在「模型服务」中启用并选择供应商。"
                : string.IsNullOrWhiteSpace(selected.ApiKey)
                    ? "无法发送：当前服务商缺少 API Key。请打开「模型服务」重新配置。"
                    : "无法发送：当前服务商的模型或接口地址尚未配置。";
            SetAiTopicStatus(topic, msg, msg);
            return;
        }
        if (string.Equals(profile.BaseUrl.TrimEnd('/'), YanziAgyModelBridge.BaseUrl, StringComparison.OrdinalIgnoreCase))
            YanziAgyModelBridge.EnsureStarted();
        if (string.Equals(profile.BaseUrl.TrimEnd('/'), YanziCookieApiBridge.BaseUrl, StringComparison.OrdinalIgnoreCase))
            YanziCookieApiBridge.EnsureStarted();
        AiChatDiagnostics.Record(topic.Id, profile.Model, "created", $"mode={mode} transport={(GeminiNativeChatClient.IsGeminiApi(profile.BaseUrl) ? "gemini-native" : "compatible")}");
        _aiTopicErrors.Remove(topic.Id);
        if (ReferenceEquals(_selectedAiChatTopic, topic)) AiChatSendErrorText = string.Empty;
        var messageAttachments = await BuildMessageAttachmentsAsync(pendingAttachments);
        var userMessage = new AiChatMessage(true, userInput, messageAttachments);
        AppendAiTopicMessage(topic, userMessage);
        if (topic.Messages.Count == 1)
            topic.Title = userInput.Length > 30 ? userInput[..30] + "..." : userInput;
        SaveTopicsToStorage();
        if (scheduledPrompt == null && ReferenceEquals(_selectedAiChatTopic, topic))
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
                        requestMessages, profile, mode, topic.Id,
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
                    SetAiTopicStatus(topic, "生成已停止", scheduledPrompt == null ? null : "定时请求被取消或超时");
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
                await RunAiToolTurnAsync(topic, profile, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetAiTopicStatus(topic, "生成已停止", scheduledPrompt == null ? null : "定时请求被取消或超时");
        }
        catch (Exception ex)
        {
            AiChatDiagnostics.Record(topic.Id, profile.Model, "failed",
                $"kind={ex.GetType().Name} inner={ex.InnerException?.GetType().Name ?? "none"} status={(ex is HttpRequestException http ? (int?)http.StatusCode : null)?.ToString() ?? "n/a"}");
            HostAssets.AppendLog($"AI request failed: topic={topic.Id}, model={profile.Model}, error={FormatExceptionMessage(ex)}");
            var error = $"AI 请求失败：{TrimForLog(FormatExceptionMessage(ex), 160)}";
            SetAiTopicStatus(topic, error, error);
        }
        finally
        {
            _aiRequestTracker.Finish(topic.Id);
            AiChatDiagnostics.Record(topic.Id, profile.Model, "finished");
            topic.IsGenerating = false;
            NoteTopicReplyReadState(topic);
            RefreshAiTopicRequestState();
            SaveTopicsToStorage();
            // Scheduled requests must never steal focus from a user editing another prompt.
            if (scheduledPrompt == null && ReferenceEquals(_selectedAiChatTopic, topic))
                _ = Dispatcher.BeginInvoke(() => FocusAiInput(), DispatcherPriority.Background);
        }
    }

    private class ToolCallInfo
    {
        public string ToolName { get; set; } = string.Empty;
        public JsonElement RawPayload { get; set; }
    }

    private async Task<string> ExecuteToolAsync(ToolCallInfo toolCall, CancellationToken cancellationToken)
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
                                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                cts.CancelAfter(TimeSpan.FromSeconds(30));
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

    private AiConversationMode DetermineAiConversationMode(string userInput, AiChatTopic? topic = null)
    {
        var text = (userInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return AiConversationMode.Chat;
        }

        var lower = text.ToLowerInvariant();

        string[] chatOnlyMarkers =
        [
            "不要调用工具", "不用调用工具", "不要使用工具", "不用工具", "禁止调用工具", "纯聊天", "不要操作电脑", "不要执行", "只回答", "只解释",
            "只分析", "先讨论", "先不要操作", "不需要操作"
        ];

        if (chatOnlyMarkers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return AiConversationMode.Chat;
        }

        if (lower.Contains("调用工具", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("使用工具", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("直接执行", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("工具", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("shell", StringComparison.OrdinalIgnoreCase))
        {
            return AiConversationMode.Agent;
        }

        string[] actionMarkers =
        [
            "打开", "关闭", "启动", "运行", "执行", "安装", "卸载", "删除", "新建", "创建",
            "修改", "修复", "保存", "复制", "移动", "重命名", "下载", "上传", "发送", "截图",
            "关机", "重启", "停止", "暂停", "继续执行", "切换", "设置", "调整", "清理",
            "查看", "检查", "读取", "找到", "定位", "搜索本地", "列出", "查询", "诊断", "排查", "核对", "统计", "run", "execute", "check", "list", "read"
        ];

        string[] environmentTargets =
        [
            "电脑", "本机", "windows", "文件", "文件夹", "目录", "桌面", "进程", "服务",
            "注册表", "剪贴板", "小程序", "扩展", "燕子", "微信", "qq", "浏览器", "硬盘",
            "磁盘", "cpu", "内存", "网络", "服务器", "程序", "应用", "界面", "shell", "powershell", "终端", "命令", "命令行", "脚本", "git", "仓库", "项目", "代码", "日志", "file", "directory", "process"
        ];

        var hasAction = actionMarkers.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var hasEnvironmentTarget = environmentTargets.Any(marker => lower.Contains(marker, StringComparison.OrdinalIgnoreCase));
        IEnumerable<AiChatMessage> contextMessages = topic is null ? _aiChatMessages : topic.Messages;
        var recentContext = string.Join(
            "\n",
            contextMessages
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
            (contextMessages.TakeLast(6).Any(static message => message.IsToolCall) || recentContextHasEnvironmentTarget))
        {
            return AiConversationMode.Agent;
        }

        return AiConversationMode.Chat;
    }


    private async Task<string> RequestAiChatCompletionStreamingAsync(
        IReadOnlyList<object> messages, AiRequestProfile profile, AiConversationMode mode,
        string topicId, Action<string> onDelta, CancellationToken cancellationToken)
    {
        if (GeminiNativeChatClient.IsGeminiApi(profile.BaseUrl))
        {
            var native = await GeminiNativeChatClient.SendAsync(_aiHttpClient, profile.BaseUrl,
                profile.ApiKey, profile.Model, messages, cancellationToken,
                stage => AiChatDiagnostics.Record(topicId, profile.Model, stage));
            onDelta(native);
            return native;
        }
        AiChatDiagnostics.Record(topicId, profile.Model, "sse-request-sent", $"messages={messages.Count}");
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
        AiChatDiagnostics.Record(topicId, profile.Model, "sse-headers", $"httpStatus={(int)response.StatusCode} elapsedMs={timer.ElapsedMilliseconds}");
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
        var firstDeltaSeen = false;
        var result = await AiChatSseParser.ReadAsync(stream, delta =>
        {
            if (!firstDeltaSeen)
            {
                firstDeltaSeen = true;
                AiChatDiagnostics.Record(topicId, profile.Model, "sse-first-delta", $"elapsedMs={timer.ElapsedMilliseconds}");
            }
            onDelta(delta);
        }, cancellationToken);
        AiChatDiagnostics.Record(topicId, profile.Model, "sse-completed", $"elapsedMs={timer.ElapsedMilliseconds} chars={result.Length}");
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

        var finalPrompt = AiAgentPrompt.Build(settings.AiSystemPrompt,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

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
        UpdateAiChatTimeDividers();
        SaveTopicsToStorage();
    }

    private bool RemoveMessagesFrom(AiChatMessage message)
    {
        if (_selectedAiChatTopic == null)
            return false;

        var messageIndex = _selectedAiChatTopic.Messages.IndexOf(message);
        if (messageIndex < 0)
        {
            _aiChatMessages.Remove(message);
            return false;
        }

        var removed = _selectedAiChatTopic.Messages.Skip(messageIndex).ToList();
        foreach (var item in removed)
        {
            _selectedAiChatTopic.Messages.Remove(item);
            _aiChatMessages.Remove(item);
        }

        _selectedAiChatTopic.NotifyMessagesChanged();
        UpdateAiChatTimeDividers();
        SaveTopicsToStorage();
        return true;
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

    private static AiRequestProfile? ResolveAiRequestProfile(AppSettings settings)
    {
        var providers = settings.AiServiceProviders ?? [];
        if (providers.Count > 0 || !string.IsNullOrWhiteSpace(settings.ActiveServiceProviderId))
        {
            var provider = providers.FirstOrDefault(item =>
                item.IsEnabled && string.Equals(item.Id, settings.ActiveServiceProviderId,
                    StringComparison.OrdinalIgnoreCase));
            if (provider is null)
                return null;

            // Provider-specific selection must override stale legacy request fields.
            var model = !string.IsNullOrWhiteSpace(provider.SelectedModel)
                ? provider.SelectedModel
                : provider.Models?.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(provider.BaseUrl) ||
                string.IsNullOrWhiteSpace(provider.ApiKey) ||
                string.IsNullOrWhiteSpace(model))
                return null;
            return new AiRequestProfile(provider.BaseUrl.Trim(), provider.ApiKey.Trim(), model.Trim(),
                string.Equals(provider.Id, ChatGptPlanProviderId, StringComparison.OrdinalIgnoreCase));
        }

        // Preserve old single-provider setups, never bypass a disabled provider.
        if (string.IsNullOrWhiteSpace(settings.AiBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.AiApiKey) ||
            string.IsNullOrWhiteSpace(settings.AiModel))
            return null;
        return new AiRequestProfile(settings.AiBaseUrl.Trim(), settings.AiApiKey.Trim(),
            settings.AiModel.Trim(), false);
    }

    private static bool IsAiConfigured(AppSettings settings) => ResolveAiRequestProfile(settings) is not null;

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
                    hasUnreadReply = t.HasUnreadReply,
                    projectFiles = t.ProjectFiles,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt,
                    messages = t.Messages.Select(m => new
                    {
                        id = m.Id,
                        isUser = m.IsUser,
                        text = m.Text,
                        timestamp = m.Timestamp,
                        isToolCall = m.IsToolCall,
                        toolName = m.ToolName,
                        toolFeedback = m.ToolFeedback,
                        toolExecution = m.ToolExecution?.Snapshot(),
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
                var topicId = topicElement.TryGetProperty("id", out var idElement)
                    ? idElement.GetString() : null;
                DateTimeOffset? createdAt = topicElement.TryGetProperty("createdAt", out var createdElement) &&
                    createdElement.ValueKind == JsonValueKind.String && createdElement.TryGetDateTimeOffset(out var createdTime)
                    ? createdTime : null;
                DateTimeOffset? updatedAt = topicElement.TryGetProperty("updatedAt", out var updatedElement) &&
                    updatedElement.ValueKind == JsonValueKind.String && updatedElement.TryGetDateTimeOffset(out var updatedTime)
                    ? updatedTime : null;
                var topic = new AiChatTopic(title, topicId, createdAt, updatedAt);

                if (topicElement.TryGetProperty("isPinned", out var isPinnedElement))
                {
                    topic.IsPinned = isPinnedElement.GetBoolean();
                }

                if (topicElement.TryGetProperty("hasUnreadReply", out var unreadElement) &&
                    unreadElement.ValueKind == JsonValueKind.True)
                {
                    topic.HasUnreadReply = true;
                }

                if (topicElement.TryGetProperty("projectFiles", out var projectFilesElement) &&
                    projectFilesElement.ValueKind == JsonValueKind.Array)
                {
                    var projectFiles = new List<string>();
                    foreach (var fileElement in projectFilesElement.EnumerateArray())
                    {
                        if (fileElement.ValueKind == JsonValueKind.String &&
                            fileElement.GetString() is { Length: > 0 } projectFile)
                            projectFiles.Add(projectFile);
                    }

                    topic.SetProjectFiles(projectFiles);
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
                        var messageId = msgElement.TryGetProperty("id", out var messageIdElement) &&
                            messageIdElement.ValueKind == JsonValueKind.String ? messageIdElement.GetString() : null;
                        var restored = new AiChatMessage(isUser, text, attachments, restoredTime, messageId);
                        if (msgElement.TryGetProperty("isToolCall", out var toolFlag) && toolFlag.ValueKind == JsonValueKind.True)
                        {
                            restored.IsToolCall = true;
                            restored.ToolName = msgElement.TryGetProperty("toolName", out var toolName) && toolName.ValueKind == JsonValueKind.String ? toolName.GetString() : null;
                            restored.ToolFeedback = msgElement.TryGetProperty("toolFeedback", out var feedback) && feedback.ValueKind == JsonValueKind.String ? feedback.GetString() : null;
                            if (msgElement.TryGetProperty("toolExecution", out var execution) && execution.ValueKind == JsonValueKind.Object)
                            {
                                var snapshot = execution.Deserialize<AiToolExecutionSnapshot>();
                                if (snapshot != null)
                                {
                                    restored.ToolExecution = AiToolExecution.Restore(snapshot);
                                    restored.ToolFeedback = restored.ToolExecution.BuildContext();
                                }
                            }
                        }
                        topic.Messages.Add(restored);
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
    private bool _isToolRunning;
    public bool IsToolRunning
    {
        get => _isToolRunning;
        set
        {
            if (_isToolRunning == value) return;
            _isToolRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToolRunningVisibility));
        }
    }
    public Visibility ToolRunningVisibility => IsToolRunning ? Visibility.Visible : Visibility.Collapsed;
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
            OnPropertyChanged(nameof(WaitingIndicatorVisibility));
            OnPropertyChanged(nameof(IsAwaitingFirstToken));
            OnPropertyChanged(nameof(IsReceivingTokens));
            OnPropertyChanged(nameof(HasHtmlContent));
            OnPropertyChanged(nameof(HasHtmlPreview));
            OnPropertyChanged(nameof(HtmlPreviewVisibility));
            OnPropertyChanged(nameof(HtmlVisibility));
            OnPropertyChanged(nameof(MarkdownVisibility));
        }
    }
    public string FormattedText => IsStreaming
        ? string.Empty
        : AiChatHtmlPreviewContent.NormalizeCodeBlocks(AiChatGptMarkup.Normalize(Text));
    public string StreamingText => AiChatGptMarkup.Normalize(Text);
    public bool HasHtmlContent => !IsUser && !IsStreaming && AiChatHtmlContent.IsHtmlMessage(Text);
    public bool HasHtmlPreview => !IsUser && !IsStreaming && AiChatHtmlPreviewContent.TryExtractHtml(Text, out _);
    public Visibility HtmlPreviewVisibility => HasHtmlPreview ? Visibility.Visible : Visibility.Collapsed;
    // A stand-alone HTML document keeps its inert WPF renderer. Inline HTML or
    // HTML examples inside Markdown belong to the Markdown engine instead.
    public bool IsStandaloneHtml => !IsUser && !IsStreaming &&
        AiChatHtmlPreviewContent.IsStandaloneHtml(Text) &&
        AiChatHtmlContent.IsHtmlMessage(Text);
    public Visibility HtmlVisibility => IsStandaloneHtml ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MarkdownVisibility => IsStandaloneHtml ? Visibility.Collapsed : Visibility.Visible;
    // One in-message lifecycle: quiet dots before first token, inline caret while streaming,
    // and no animated element once the reply is complete or canceled.
    public bool IsAwaitingFirstToken => !IsUser && IsStreaming && string.IsNullOrEmpty(Text);
    public bool IsReceivingTokens => !IsUser && IsStreaming && !string.IsNullOrEmpty(Text);
    public Visibility WaitingIndicatorVisibility => IsAwaitingFirstToken ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StreamingTextVisibility => IsReceivingTokens ? Visibility.Visible : Visibility.Collapsed;
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

    private AiToolExecution? _toolExecution;
    public AiToolExecution? ToolExecution
    {
        get => _toolExecution;
        set { _toolExecution = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasToolExecution)); }
    }
    public bool HasToolExecution => ToolExecution != null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public AiChatMessage(bool isUser, string text,
        IEnumerable<AiChatMessageAttachment>? attachments = null,
        DateTimeOffset? timestamp = null, string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
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
            OnPropertyChanged(nameof(StreamingText));
            OnPropertyChanged(nameof(StreamingTextVisibility));
            OnPropertyChanged(nameof(WaitingIndicatorVisibility));
            OnPropertyChanged(nameof(IsAwaitingFirstToken));
            OnPropertyChanged(nameof(IsReceivingTokens));
            if (!IsStreaming)
            {
                OnPropertyChanged(nameof(FormattedText));
                OnPropertyChanged(nameof(HasHtmlContent));
                OnPropertyChanged(nameof(HasHtmlPreview));
                OnPropertyChanged(nameof(HtmlPreviewVisibility));
                OnPropertyChanged(nameof(HtmlVisibility));
                OnPropertyChanged(nameof(MarkdownVisibility));
            }
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
    public ImageSource? PreviewImage => AiChatImagePreview.Get(FilePath, ref _previewAttempted, ref _preview);
    public Visibility ImagePreviewVisibility => PreviewImage != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FileChipVisibility => PreviewImage != null ? Visibility.Collapsed : Visibility.Visible;

    private bool _previewAttempted;
    private ImageSource? _preview;

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
    private bool _hasUnreadReply;
    private DateTimeOffset _updatedAt;

    public AiChatTopic(string title, string? id = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        _title = title;
        CreatedAt = createdAt ?? DateTimeOffset.Now;
        UpdatedAt = updatedAt ?? CreatedAt;
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

    public bool HasUnreadReply
    {
        get => _hasUnreadReply;
        set
        {
            if (_hasUnreadReply == value) return;
            _hasUnreadReply = value;
            OnPropertyChanged();
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
    public DateTimeOffset UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (_updatedAt == value) return;
            _updatedAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdatedAtLabel));
        }
    }

    // Stored for a later model context. These paths are not sent with the request yet.
    private readonly List<string> _projectFiles = [];

    public IReadOnlyList<string> ProjectFiles => _projectFiles;

    public string ProjectFilesLabel => _projectFiles.Count == 0
        ? string.Empty
        : string.Join("、", _projectFiles.Select(Path.GetFileName));

    public Visibility ProjectFilesVisibility =>
        _projectFiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public void SetProjectFiles(IEnumerable<string> paths)
    {
        _projectFiles.Clear();
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            _projectFiles.Add(path);
        OnPropertyChanged(nameof(ProjectFiles));
        OnPropertyChanged(nameof(ProjectFilesLabel));
        OnPropertyChanged(nameof(ProjectFilesVisibility));
    }

    public string UpdatedAtLabel => FormatListAge(UpdatedAt, DateTimeOffset.Now);

    public void RefreshUpdatedAtLabel() => OnPropertyChanged(nameof(UpdatedAtLabel));

    public static string FormatListAge(DateTimeOffset updatedAt, DateTimeOffset now)
    {
        var elapsed = now - updatedAt;
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1))
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m";
        if (elapsed < TimeSpan.FromDays(1))
            return $"{Math.Max(1, (int)elapsed.TotalHours)}h";
        if (elapsed < TimeSpan.FromDays(7))
            return $"{Math.Max(1, (int)elapsed.TotalDays)}d";

        var local = updatedAt.ToLocalTime();
        return local.Year == now.ToLocalTime().Year
            ? local.ToString("M/d")
            : local.ToString("yyyy/M/d");
    }

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
    public ImageSource? PreviewImage => AiChatImagePreview.Get(FilePath, ref _previewAttempted, ref _preview);
    public Visibility ImagePreviewVisibility => PreviewImage != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FileChipVisibility => PreviewImage != null ? Visibility.Collapsed : Visibility.Visible;

    private bool _previewAttempted;
    private ImageSource? _preview;

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

public static class AiChatImagePreview
{
    public static ImageSource? TryCreate(string? path) => Create(path);

    internal static ImageSource? Get(string? path, ref bool attempted, ref ImageSource? preview)
    {
        if (attempted)
        {
            return preview;
        }

        attempted = true;
        preview = Create(path);
        return preview;
    }

    private static ImageSource? Create(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsImage(path))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 480;
            image.StreamSource = stream;
            image.EndInit();
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            return image;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsImage(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[12];
            var read = stream.Read(header);
            if (read >= 8 && header[0] == 0x89 && header[1] == (byte)'P' && header[2] == (byte)'N' && header[3] == (byte)'G')
            {
                return true;
            }

            if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return true;
            }

            if (read >= 6 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
            {
                return true;
            }

            if (read >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
            {
                return true;
            }

            return read >= 12 &&
                   header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
                   header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P';
        }
        catch
        {
            return false;
        }
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
