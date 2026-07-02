using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpenQuickHost.Sync;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace OpenQuickHost;

public partial class AddJsonExtensionWindow : Window
{
    private enum EditSource
    {
        Unknown,
        Form,
        Json
    }

    private static readonly MediaBrush AccentBrush = CreateBrush("#FF3B82F6");
    private static readonly MediaBrush AccentGlowBrush = CreateBrush("#223B82F6");
    private static readonly MediaBrush BorderSoftBrush = CreateBrush("#12FFFFFF");
    private static readonly MediaBrush BorderStrongBrush = CreateBrush("#1FFFFFFF");
    private static readonly MediaBrush GreenBrush = CreateBrush("#FF34D399");
    private static readonly MediaBrush RedBrush = CreateBrush("#FFF87171");
    private static readonly MediaBrush Text2Brush = CreateBrush("#FF9090A8");
    private static readonly MediaBrush Text3Brush = CreateBrush("#FF5A5A72");

    private readonly IReadOnlyList<ExtensionIconOption> _builtInIcons = ExtensionIconLibrary.GetBuiltInOptions();
    private readonly bool _isEditMode;
    private readonly string _initialJson;
    private AppSettings _settings;
    private string _aiGuidePrompt = string.Empty;
    private WizardStep _currentStep = WizardStep.Describe;
    private LocalExtensionHostedViewManifest? _manualHostedView;
    private LocalExtensionSearchProviderManifest? _manualSearchProvider;
    private LocalExtensionMouseGestureManifest? _manualMouseGesture;
    private string? _manualUiMode;
    private bool _lastJsonValid;
    private bool _testCompleted;
    private bool _testSucceeded;
    private bool _manualMode;
    private bool _aiPromptCopied;
    private bool _isInitializing = true;
    private bool _suppressEditTracking;
    private EditSource _lastEditedSource = EditSource.Unknown;

    public bool WasAccepted { get; private set; }
    public CommandItem? PersistedCommand { get; private set; }

    public AddJsonExtensionWindow(string initialJson, bool isEditMode = false)
    {
        InitializeComponent();
        App.EnableSilentLoading(this);
        Topmost = false;
        ShowInTaskbar = true;
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(AnyTextBox_TextChanged));
        AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(TextBoxClipboard_PreviewKeyDown), true);
        BuiltInIconsList.ItemsSource = _builtInIcons;
        _isEditMode = isEditMode;
        _initialJson = initialJson ?? string.Empty;
        _settings = AppSettingsStore.Load();
        _manualMode = true;

        ConfigureMode(_initialJson);

        Loaded += (_, _) =>
        {
            // 依据编辑状态动态展示窗口标题
            if (_isEditMode)
            {
                HeaderTitleText.Text = "编辑扩展";
                this.Title = "编辑扩展";
            }
            else
            {
                HeaderTitleText.Text = "新建扩展";
                this.Title = "新建扩展";
            }

            // 初始化加载持久化记住的测试输入参数，若为空则显示默认值
            TestArgumentBox.Text = _settings.LastTestArgument ?? "示例参数";
            UpdateTestArgumentPlaceholderVisibility();

            if (_isEditMode && ShouldOpenAdvancedEditorForExistingJson(_initialJson))
            {
                AdvancedModeTab.IsChecked = true;
                SimpleModePanel.Visibility = Visibility.Collapsed;
                AdvancedModePanel.Visibility = Visibility.Visible;
                _manualMode = true;
            }
            else
            {
                SimpleModeTab.IsChecked = true;
                SimpleModePanel.Visibility = Visibility.Visible;
                AdvancedModePanel.Visibility = Visibility.Collapsed;
                _manualMode = false;
            }

            // 确保 AI 编辑模式下 JSON 输入框为空（新增模式）
            if (!_isEditMode && !_manualMode)
            {
                AiJsonInputBox.Text = string.Empty;
                AiJsonPlaceholder.Visibility = Visibility.Visible;
            }

            if (_manualMode)
            {
                ManualJsonInputBox.Focus();
                ManualJsonInputBox.CaretIndex = 0;
            }
            else if (_isEditMode)
            {
                AiJsonInputBox.Focus();
                AiJsonInputBox.SelectAll();
            }
            else
            {
                AiRequestBox.Focus();
            }

            RefreshPromptText();
            RefreshAllState();
            
            // 初始化简单模式：根据已加载的 manifest 推断类型并把字段同步到简单控件
            InitializeSimpleMode();
            if (_isEditMode && ShouldOpenAdvancedEditorForExistingJson(_initialJson))
            {
                AdvancedModeTab.IsChecked = true;
                SimpleModePanel.Visibility = Visibility.Collapsed;
                AdvancedModePanel.Visibility = Visibility.Visible;
            }
            // 初始化完成，允许同步
            _isInitializing = false;

            // 异步初始化高级编辑器与内联脚本编辑器，支持 4 秒超时无缝降级
            _ = InitializeWebViewEditorsAsync();
        };
    }

    private static bool ShouldOpenAdvancedEditorForExistingJson(string initialJson)
    {
        if (string.IsNullOrWhiteSpace(initialJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(initialJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("app", out _);
        }
        catch
        {
            return false;
        }
    }

    public string JsonContent => ExtractJsonPayload(GetCurrentJsonText());

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ConfigureMode(string initialJson)
    {
        Title = _isEditMode ? "编辑扩展" : "添加新扩展";
        ManualModeButton.Visibility = Visibility.Collapsed;
        ManualModeButton.Content = "手动编辑";

        if (_isEditMode)
        {
            PageHeaderPrefix.Text = "编辑";
            PageHeaderAccent.Text = "扩展";
            HeaderDescription.Text = "直接修改 JSON，验证通过后可以测试并保存。";
            SaveButton.Content = "保存修改";
            _currentStep = WizardStep.Test;
        }
        else
        {
            PageHeaderPrefix.Text = "添加";
            PageHeaderAccent.Text = "新扩展";
            HeaderDescription.Text = "通过表单和手动编写 JSON 来创建扩展。";
            SaveButton.Content = "保存并添加";
            _currentStep = WizardStep.Describe;
        }

        // 先清空两个编辑器，避免残留内容
        ManualJsonInputBox.Text = string.Empty;
        AiJsonInputBox.Text = string.Empty;

        if (!string.IsNullOrWhiteSpace(initialJson))
        {
            // 编辑模式：设置初始 JSON 内容
            ManualJsonInputBox.Text = initialJson;
            AiJsonInputBox.Text = initialJson;
            TryPopulateManualFormFromJson(initialJson, showError: false);
        }
        // 新增模式下两个 JSON 编辑器保持为空，等待用户粘贴或手动输入

        UpdateJsonValidationState();
        UpdateManualJsonValidationState();
        SafeRefreshIconPreview();
        UpdateWindowHeightForStep();
    }

    private void AiRequestBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AiRequestPlaceholder.Visibility = string.IsNullOrWhiteSpace(AiRequestBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _aiPromptCopied = false;
        _testCompleted = false;
        _testSucceeded = false;
        RefreshPromptText();
        RefreshAllState();
    }

    private void AiExampleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string example })
        {
            return;
        }

        AiRequestBox.Text = example;
        AiRequestBox.Focus();
        AiRequestBox.CaretIndex = AiRequestBox.Text.Length;
    }

    private void ManualModeButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void AiPromptPreviewBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AiPromptPreviewBox.Text))
        {
            AiPromptPlaceholder.Visibility = Visibility.Visible;
            _aiGuidePrompt = string.Empty;
            _aiPromptCopied = false;
            return;
        }

        AiPromptPlaceholder.Visibility = Visibility.Collapsed;
        _aiGuidePrompt = AiPromptPreviewBox.Text;
        _aiPromptCopied = false;
        RefreshButtons();
    }

    private void ManualJsonInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingWebView) return;

        if (!_isInitializing && !_suppressEditTracking)
        {
            _lastEditedSource = EditSource.Json;
        }

        // 初始化期间不同步编辑器
        if (!_isInitializing)
        {
            SyncJsonEditors(fromManual: true);
        }
        
        _testCompleted = false;
        _testSucceeded = false;
        ManualTestResultPanel.Visibility = Visibility.Collapsed;
        ManualCopyTestFailureButton.Visibility = Visibility.Collapsed;
        ManualTestLogTextBox.Clear();
        ManualTestSummaryText.Text = string.Empty;
        UpdateManualJsonValidationState();

        // 动态检测 JSON 并同步更新下方的内联脚本独立编辑器状态与高度分栏
        UpdateInlineScriptPanelState();

        RefreshAllState();
    }

    private async void ManualTestExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAndRenderAsync(
            ManualTestExtensionButton,
            ManualTestResultPanel,
            ManualTestSummaryText,
            ManualTestLogTextBox,
            ManualCopyTestFailureButton,
            useManualJson: true);
    }

    private async void ManualCopyTestFailureButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyTestLogToClipboardAsync(
            ManualCopyTestFailureButton,
            ManualTestLogTextBox.Text);
    }

    private void ManualTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        ManualJsonInputBox.Text = tag switch
        {
            "base" => CreateDesktopTemplateJson(),
            "open" => CreateOpenTargetTemplateJson(),
            "search" => CreateSearchTemplateJson(),
            "script" => CreateInlineScriptTemplateJson(),
            "foreground" => CreateForegroundWindowTemplateJson(),
            "clipboard" => CreateClipboardTemplateJson(),
            "selection" => CreateSelectionContextTemplateJson(),
            "csharp" => CreateCSharpContextTemplateJson(),
            "native" => CreateNativeWindowTemplateJson(),
            "native-note" => CreateNativeNoteTemplateJson(),
            "timestamp" => CreateTimestampTemplateJson(),
            "translate" => CreateTranslateWorkbenchTemplateJson(),
            _ => CreateDesktopTemplateJson()
        };

        TryPopulateManualFormFromJson(ManualJsonInputBox.Text, showError: false);
    }

    private void ParseManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryPopulateManualFormFromJson(ManualJsonInputBox.Text, showError: true))
        {
            FormatManualJsonEditor(showError: true);
        }
    }

    private void GenerateManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ManualJsonInputBox.Text = JsonSerializer.Serialize(BuildManifestFromForm(), CreateJsonOptions());
            UpdateManualJsonValidationState();
            RefreshAllState();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void FormatManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        FormatManualJsonEditor(showError: true);
    }

    private void FindManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        FindNextManualJsonMatch();
    }

    private void ReplaceManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        ReplaceCurrentManualJsonMatch();
    }

    private void ReplaceAllManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        ReplaceAllManualJsonMatches();
    }

    private void IconBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SafeRefreshIconPreview();
    }

    private void IconPreviewContext_TextChanged(object sender, TextChangedEventArgs e)
    {
        SafeRefreshIconPreview();
    }

    private void BuiltInIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string iconReference })
        {
            return;
        }

        IconBox.Text = iconReference;
        SafeRefreshIconPreview();
    }

    private void PickIconPresetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IconPickerDialog(this, IconSimpleBox.Text);
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedIconReference))
        {
            IconSimpleBox.Text = dialog.SelectedIconReference;
        }
    }

    private void PickIconForegroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        MediaColor initColor = Colors.White;
        try
        {
            var currentIcon = IconSimpleBox.Text?.Trim() ?? string.Empty;
            if (currentIcon.LastIndexOf('#') is var hashIdx && hashIdx > 0)
            {
                initColor = (MediaColor)MediaColorConverter.ConvertFromString(currentIcon[hashIdx..])!;
            }
        }
        catch {}

        var dialog = new ColorPickerDialog(this, initColor);
        if (dialog.ShowDialog() == true)
        {
            var color = dialog.SelectedColor;
            var hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            
            var currentIcon = IconSimpleBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(currentIcon))
            {
                var baseIcon = currentIcon;
                if (currentIcon.LastIndexOf('#') is var hashIdx && hashIdx > 0)
                {
                    baseIcon = currentIcon[..hashIdx].TrimEnd(':');
                }
                IconSimpleBox.Text = $"{baseIcon}:{hex}";
            }
            else
            {
                IconSimpleBox.Text = $"mdi:search:{hex}";
            }
        }
    }

    private void PickAccentColorButton_Click(object sender, RoutedEventArgs e)
    {
        MediaColor initColor = (MediaColor)MediaColorConverter.ConvertFromString("#FF3B82F6")!;
        try
        {
            var current = AccentHexSimpleBox.Text?.Trim();
            if (!string.IsNullOrEmpty(current))
            {
                initColor = (MediaColor)MediaColorConverter.ConvertFromString(current)!;
            }
        }
        catch {}

        var dialog = new ColorPickerDialog(this, initColor);
        if (dialog.ShowDialog() == true)
        {
            var color = dialog.SelectedColor;
            var hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            AccentHexSimpleBox.Text = hex;
        }
    }

    private void AccentColorLivePreview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            PickAccentColorButton_Click(sender, e);
        }
    }

    private void PickIconImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择扩展图标",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.ico|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        IconBox.Text = dialog.FileName;
        SafeRefreshIconPreview();
    }

    private void ClearIconButton_Click(object sender, RoutedEventArgs e)
    {
        IconBox.Clear();
        SafeRefreshIconPreview();
    }

    private void GoStep2Button_Click(object sender, RoutedEventArgs e)
    {
        if (AiRequestBox.Text.Trim().Length <= 3)
        {
            ShowError("先写清楚你想做什么扩展，再进入下一步。");
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        _currentStep = WizardStep.Prompt;
        RefreshAllState();
        UpdateLayout();
    }

    private void BackToStep1Button_Click(object sender, RoutedEventArgs e)
    {
        _currentStep = WizardStep.Describe;
        RefreshAllState();
    }

    private void GoStep3Button_Click(object sender, RoutedEventArgs e)
    {
        if (!_aiPromptCopied)
        {
            ShowError("先复制提示词，再进入粘贴 JSON 的下一步。");
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        _currentStep = WizardStep.Test;
        RefreshAllState();
        UpdateLayout();
    }

    private void BackToStep2Button_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditMode)
        {
            return;
        }

        _currentStep = WizardStep.Prompt;
        RefreshAllState();
    }

    private async void CopyAiGuidePromptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _aiGuidePrompt = AiPromptPreviewBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(_aiGuidePrompt))
            {
                ShowError("当前没有可复制的提示词。");
                return;
            }

            await Task.Run(() => CopyTextToClipboard(_aiGuidePrompt));
            _aiPromptCopied = true;
            ErrorText.Visibility = Visibility.Collapsed;
            CopyAiGuidePromptButton.Content = "已复制，去问 AI";
            CopyAiGuidePromptButton.Background = GreenBrush;
            CopyAiGuidePromptButton.BorderBrush = GreenBrush;
            GoStep3Button.Content = "去粘贴 JSON";
            RefreshButtons();

            await Task.Delay(1800);
            if (!IsLoaded)
            {
                return;
            }

            CopyAiGuidePromptButton.Content = "再次复制";
            CopyAiGuidePromptButton.Background = AccentBrush;
            CopyAiGuidePromptButton.BorderBrush = AccentBrush;
        }
        catch (Exception ex)
        {
            ShowError($"复制提示词失败：{ex.Message}");
        }
    }

    private async void ManualCopyPromptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            var prompt = TryBuildManualCopyPrompt();
            await Task.Run(() => CopyTextToClipboard(prompt));

            ManualCopyPromptButton.Content = "已复制";
            ManualCopyPromptButton.Background = GreenBrush;
            ManualCopyPromptButton.BorderBrush = GreenBrush;

            await Task.Delay(1800);
            if (!IsLoaded)
            {
                return;
            }

            ManualCopyPromptButton.Content = "复制提示词";
            ManualCopyPromptButton.Background = MediaBrushes.Transparent;
            ManualCopyPromptButton.BorderBrush = BorderStrongBrush;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AddJson manual copy prompt failed: {ex}");
            ShowError($"复制提示词失败：{ex.Message}");
        }
    }

    private async void ManualCopyJsonButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            await Task.Run(() => CopyTextToClipboard(ManualJsonInputBox.Text ?? string.Empty));

            ManualCopyJsonButton.Content = "已复制";
            ManualCopyJsonButton.Background = GreenBrush;
            ManualCopyJsonButton.BorderBrush = GreenBrush;

            await Task.Delay(1800);
            if (!IsLoaded)
            {
                return;
            }

            ManualCopyJsonButton.Content = "复制 JSON";
            ManualCopyJsonButton.Background = MediaBrushes.Transparent;
            ManualCopyJsonButton.BorderBrush = BorderStrongBrush;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AddJson manual copy json failed: {ex}");
            ShowError($"复制 JSON 失败：{ex.Message}");
        }
    }

    private string TryBuildManualCopyPrompt()
    {
        return BuildDetailedPrompt(BuildManualRequestSummary());
    }

    private string BuildManualRequestSummary()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(NameBox.Text))
        {
            parts.Add($"名称是“{NameBox.Text.Trim()}”");
        }

        if (!string.IsNullOrWhiteSpace(CategoryBox.Text))
        {
            parts.Add($"分类是“{CategoryBox.Text.Trim()}”");
        }

        if (!string.IsNullOrWhiteSpace(DescriptionBox.Text))
        {
            parts.Add($"用途是：{DescriptionBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(OpenTargetBox.Text))
        {
            parts.Add($"点击后打开目标：{OpenTargetBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(QueryTargetTemplateBox.Text))
        {
            parts.Add($"这是一个搜索扩展，搜索模板是：{QueryTargetTemplateBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(QueryPrefixesBox.Text))
        {
            parts.Add($"搜索前缀有：{QueryPrefixesBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(RuntimeBox.Text))
        {
            parts.Add($"运行时希望使用：{RuntimeBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(EntryModeBox.Text))
        {
            parts.Add($"入口模式是：{EntryModeBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(EntryBox.Text))
        {
            parts.Add($"入口文件是：{EntryBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(ScriptSourceBox.Text))
        {
            parts.Add("需要包含内联脚本逻辑");
        }

        if (!string.IsNullOrWhiteSpace(IconBox.Text))
        {
            parts.Add($"图标希望使用：{IconBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(KeywordsBox.Text))
        {
            parts.Add($"关键词包括：{KeywordsBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(PermissionsBox.Text))
        {
            parts.Add($"权限包括：{PermissionsBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(GlobalShortcutBox.Text))
        {
            parts.Add($"全局快捷键是：{GlobalShortcutBox.Text.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(HotkeyBehaviorBox.Text))
        {
            parts.Add($"热键行为是：{HotkeyBehaviorBox.Text.Trim()}");
        }

        return parts.Count == 0
            ? "创建一个新的 Yanzi 扩展。"
            : $"创建一个新的 Yanzi 扩展，要求如下：{string.Join("；", parts)}。";
    }

    private void AiLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"打开链接失败：{ex.Message}");
        }
    }

    private void AiJsonInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_manualMode)
        {
            return;
        }

        AiJsonPlaceholder.Visibility = string.IsNullOrWhiteSpace(AiJsonInputBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_isInitializing && !_suppressEditTracking)
        {
            _lastEditedSource = EditSource.Json;
        }

        // 初始化期间不同步编辑器
        if (!_isInitializing)
        {
            SyncJsonEditors(fromManual: false);
        }
        
        _testCompleted = false;
        _testSucceeded = false;
        TestResultPanel.Visibility = Visibility.Collapsed;
        CopyTestFailureButton.Visibility = Visibility.Collapsed;
        TestLogTextBox.Clear();
        TestSummaryText.Text = string.Empty;

        UpdateJsonValidationState();
        RefreshAllState();
    }

    private async void TestExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAndRenderAsync(
            TestExtensionButton,
            TestResultPanel,
            TestSummaryText,
            TestLogTextBox,
            CopyTestFailureButton,
            useManualJson: false);
    }

    private void Border_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasAccepted = false;
        if (IsLoaded)
        {
            try
            {
                DialogResult = false;
                return;
            }
            catch
            {
                // Fall back to Close for non-modal usage.
            }
        }

        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            var normalizedJson = ResolveJsonForSave();
            _ = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
                ?? throw new InvalidOperationException("JSON 解析失败。");

            if (Owner is MainWindow mainWindow)
            {
                PersistedCommand = mainWindow.PersistJsonExtensionFromDialog(normalizedJson, _isEditMode);
            }

            WasAccepted = true;
            HostAssets.AppendLog("AddJson save accepted: dialog validation passed.");

            if (IsLoaded)
            {
                try
                {
                    DialogResult = true;
                    return;
                }
                catch
                {
                    // Fall back to Close for non-modal usage.
                }
            }

            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void RefreshAllState()
    {
        RefreshPanels();
        RefreshSteps();
        RefreshButtons();
        UpdateWindowHeightForStep();
        UpdateLayout();
    }

    private void RefreshPanels()
    {
        // 新布局下通过控制各容器内容的可见性来实现模式切换
        // 这里目前简化处理，主要优化编辑体验
    }

    private void RefreshSteps()
    {
        if (_manualMode)
        {
            return;
        }

        if (_isEditMode)
        {
            SetStepVisual(Step1Dot, Step1DotText, Step1Label, StepVisualState.Done, "1");
            SetStepVisual(Step2Dot, Step2DotText, Step2Label, StepVisualState.Done, "2");
            SetStepVisual(Step3Dot, Step3DotText, Step3Label, _lastJsonValid ? StepVisualState.Active : StepVisualState.Active, "3");
            StepLine1.Background = AccentBrush;
            StepLine2.Background = AccentBrush;
            return;
        }

        SetStepVisual(
            Step1Dot,
            Step1DotText,
            Step1Label,
            _currentStep == WizardStep.Describe ? StepVisualState.Active : StepVisualState.Done,
            "1");

        SetStepVisual(
            Step2Dot,
            Step2DotText,
            Step2Label,
            _currentStep == WizardStep.Prompt
                ? StepVisualState.Active
                : _currentStep == WizardStep.Test ? StepVisualState.Done : StepVisualState.Inactive,
            "2");

        SetStepVisual(
            Step3Dot,
            Step3DotText,
            Step3Label,
            _currentStep == WizardStep.Test ? StepVisualState.Active : StepVisualState.Inactive,
            "3");

        StepLine1.Background = _currentStep != WizardStep.Describe ? AccentBrush : BorderSoftBrush;
        StepLine2.Background = _currentStep == WizardStep.Test ? AccentBrush : BorderSoftBrush;
    }

    private void RefreshButtons()
    {
        var canContinueToStep2 = AiRequestBox.Text.Trim().Length > 3;
        GoStep2Button.IsEnabled = canContinueToStep2;
        CopyAiGuidePromptButton.IsEnabled = !string.IsNullOrWhiteSpace(_aiGuidePrompt);
        GoStep3Button.IsEnabled = _aiPromptCopied;
        TestExtensionButton.Visibility = _lastJsonValid ? Visibility.Visible : Visibility.Collapsed;
        TestExtensionButton.IsEnabled = _lastJsonValid;
        ManualTestExtensionButton.Visibility = _lastJsonValid ? Visibility.Visible : Visibility.Collapsed;
        ManualTestExtensionButton.IsEnabled = _lastJsonValid;
        SaveButton.IsEnabled = _lastJsonValid;
        // 新设计：保存按钮始终可见。无效时通过 IsEnabled=false 给出视觉反馈，
        // 不再因为 JSON 暂时无效就把按钮藏起来（这会误导用户以为保存功能消失了）。
        SaveButton.Visibility = Visibility.Visible;

        if (_testCompleted && !_testSucceeded)
        {
            SaveButton.IsEnabled = _lastJsonValid;
        }

        if (_aiPromptCopied)
        {
            AiPromptStatusText.Text = "提示词已复制。去 AI 对话生成 JSON，然后回到这里继续。";
            AiPromptStatusText.Foreground = GreenBrush;
            AiPromptStatusDot.Fill = GreenBrush;
        }
        else
        {
            AiPromptStatusText.Text = string.IsNullOrWhiteSpace(_aiGuidePrompt)
                ? "先填写需求，系统会自动生成可复制的提示词。"
                : "先复制提示词，再去任意 AI 对话里提问。";
            AiPromptStatusText.Foreground = Text3Brush;
            AiPromptStatusDot.Fill = Text3Brush;
        }
    }

    private void UpdateWindowHeightForStep()
    {
        // 优化后的编辑器固定大尺寸，提供沉浸式编辑体验
        Width = 1200;
        MinWidth = 1080;
        MaxWidth = double.PositiveInfinity;
        ApplyWindowHeight(preferredHeight: 880, minimumHeight: 760);
    }

    private void ApplyWindowHeight(double preferredHeight, double minimumHeight)
    {
        var maxHeight = GetMaxUsableWindowHeight();
        MinHeight = Math.Min(minimumHeight, maxHeight);
        Height = Math.Clamp(preferredHeight, MinHeight, maxHeight);
        MaxHeight = maxHeight;
    }

    private void UpdatePromptEditorHeight()
    {
        if (_isEditMode)
        {
            return;
        }

        var promptHeight = _currentStep == WizardStep.Prompt
            ? Math.Clamp(Height - 520, 220, 340)
            : 220;

        AiPromptPreviewBox.Height = promptHeight;
    }

    private static double GetMaxUsableWindowHeight()
    {
        var workAreaHeight = SystemParameters.WorkArea.Height;
        return Math.Max(560, workAreaHeight - 48);
    }

    private void RefreshPromptText()
    {
        if (_isEditMode)
        {
            return;
        }

        var request = AiRequestBox.Text.Trim();
        if (request.Length <= 3)
        {
            _aiGuidePrompt = string.Empty;
            _aiPromptCopied = false;
            AiPromptPreviewBox.Text = string.Empty;
            AiPromptPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        _aiPromptCopied = false;
        _aiGuidePrompt = BuildDetailedPrompt(request);
        AiPromptPreviewBox.Text = _aiGuidePrompt;
        Dispatcher.BeginInvoke(() =>
        {
            AiPromptPreviewBox.CaretIndex = 0;
            AiPromptPreviewBox.ScrollToHome();
        }, DispatcherPriority.Background);
        AiPromptPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void UpdateJsonValidationState()
    {
        if (string.IsNullOrWhiteSpace(AiJsonInputBox.Text))
        {
            _lastJsonValid = false;
            AiJsonInputBox.BorderBrush = BorderStrongBrush;
            AiJsonStatusText.Text = "等待粘贴 AI 生成的 JSON…";
            AiJsonStatusText.Foreground = Text3Brush;
            JsonStatusDot.Fill = Text3Brush;
            AiJsonPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            _ = ParseManifestFromJson(AiJsonInputBox.Text, "ai-json-validation");

            _lastJsonValid = true;
            AiJsonInputBox.BorderBrush = CreateBrush("#8034D399");
            AiJsonStatusText.Text = "JSON 格式正确，可以开始测试。";
            AiJsonStatusText.Foreground = GreenBrush;
            JsonStatusDot.Fill = GreenBrush;
            AiJsonPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _lastJsonValid = false;
            AiJsonInputBox.BorderBrush = CreateBrush("#80F87171");
            AiJsonStatusText.Text = $"格式有误，请检查 JSON 是否完整（{CompactError(ex.Message)}）";
            AiJsonStatusText.Foreground = RedBrush;
            JsonStatusDot.Fill = RedBrush;
            AiJsonPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateManualJsonValidationState()
    {
        if (string.IsNullOrWhiteSpace(ManualJsonInputBox.Text))
        {
            _lastJsonValid = false;
            ManualJsonInputBox.BorderBrush = BorderStrongBrush;
            ManualJsonStatusText.Text = "等待输入 JSON…";
            ManualJsonStatusText.Foreground = Text3Brush;
            ManualJsonStatusDot.Fill = Text3Brush;
            return;
        }

        try
        {
            _ = ParseManifestFromJson(ManualJsonInputBox.Text, "manual-json-validation");

            _lastJsonValid = true;
            ManualJsonInputBox.BorderBrush = CreateBrush("#8034D399");
            ManualJsonStatusText.Text = "JSON 格式正确，可以开始测试。";
            ManualJsonStatusText.Foreground = GreenBrush;
            ManualJsonStatusDot.Fill = GreenBrush;
        }
        catch (Exception ex)
        {
            _lastJsonValid = false;
            ManualJsonInputBox.BorderBrush = CreateBrush("#80F87171");
            ManualJsonStatusText.Text = $"格式有误，请检查 JSON 是否完整（{CompactError(ex.Message)}）";
            ManualJsonStatusText.Foreground = RedBrush;
            ManualJsonStatusDot.Fill = RedBrush;

            // 高精度解析并捕获异常中的 LineNumber 与 BytePositionInLine，便于双击状态栏快速飞渡
            try
            {
                var matchLine = System.Text.RegularExpressions.Regex.Match(ex.Message, @"LineNumber:\s*(\d+)");
                var matchCol = System.Text.RegularExpressions.Regex.Match(ex.Message, @"BytePositionInLine:\s*(\d+)");

                _lastErrorLine = matchLine.Success ? int.Parse(matchLine.Groups[1].Value) : 1;
                _lastErrorCol = matchCol.Success ? int.Parse(matchCol.Groups[1].Value) : 1;
            }
            catch
            {
                _lastErrorLine = 1;
                _lastErrorCol = 1;
            }
        }
    }

    private string GetCurrentJsonText()
    {
        if (!string.IsNullOrWhiteSpace(ManualJsonInputBox.Text))
        {
            return ManualJsonInputBox.Text;
        }

        return _manualMode ? ManualJsonInputBox.Text : AiJsonInputBox.Text;
    }

    private string ResolveJsonForSave()
    {
        if (_lastEditedSource == EditSource.Form)
        {
            try
            {
                var manifestJson = JsonSerializer.Serialize(BuildManifestFromForm(), CreateJsonOptions());
                if (!string.Equals(ManualJsonInputBox.Text, manifestJson, StringComparison.Ordinal))
                {
                    _suppressEditTracking = true;
                    try
                    {
                        ManualJsonInputBox.Text = manifestJson;
                    }
                    finally
                    {
                        _suppressEditTracking = false;
                    }
                }

                return ExtractJsonPayload(manifestJson);
            }
            catch
            {
                // Fall back to the current JSON editor contents if form serialization fails.
            }
        }

        var currentJson = GetCurrentJsonText();
        if (!string.IsNullOrWhiteSpace(currentJson))
        {
            return ExtractJsonPayload(currentJson);
        }

        var fallbackManifestJson = JsonSerializer.Serialize(BuildManifestFromForm(), CreateJsonOptions());
        return ExtractJsonPayload(fallbackManifestJson);
    }

    private void SyncJsonEditors(bool fromManual)
    {
        if (fromManual)
        {
            if (!_isEditMode)
            {
                return;
            }

            if (!string.Equals(AiJsonInputBox.Text, ManualJsonInputBox.Text, StringComparison.Ordinal))
            {
                _suppressEditTracking = true;
                try
                {
                    AiJsonInputBox.Text = ManualJsonInputBox.Text;
                }
                finally
                {
                    _suppressEditTracking = false;
                }
            }

            return;
        }

        if (!string.Equals(ManualJsonInputBox.Text, AiJsonInputBox.Text, StringComparison.Ordinal))
        {
            _suppressEditTracking = true;
            try
            {
                ManualJsonInputBox.Text = AiJsonInputBox.Text;
            }
            finally
            {
                _suppressEditTracking = false;
            }
        }
    }

    private void AnyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressEditTracking || e.OriginalSource is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        if (ReferenceEquals(textBox, ManualJsonInputBox) ||
            ReferenceEquals(textBox, AiJsonInputBox) ||
            ReferenceEquals(textBox, AiRequestBox) ||
            ReferenceEquals(textBox, AiPromptPreviewBox) ||
            ReferenceEquals(textBox, TestLogTextBox) ||
            ReferenceEquals(textBox, ManualTestLogTextBox) ||
            ReferenceEquals(textBox, ManualJsonFindBox) ||
            ReferenceEquals(textBox, ManualJsonReplaceBox))
        {
            return;
        }

        _lastEditedSource = EditSource.Form;
    }

    private void TextBoxClipboard_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (IsFindShortcut(e))
        {
            ManualJsonFindBox.Focus();
            ManualJsonFindBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (!IsCopyShortcut(e) || e.OriginalSource is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        var selectedText = textBox.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        try
        {
            ClipboardService.SetText(selectedText);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AddJson text box copy failed: {ex}");
            ShowError($"复制选中内容失败：{ex.Message}");
            e.Handled = true;
        }
    }

    private static bool IsCopyShortcut(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
               ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt) &&
               (key == Key.C || key == Key.Insert);
    }

    private static bool IsFindShortcut(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
               ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.Alt) &&
               key == Key.F;
    }

    private bool TryPopulateManualFormFromJson(string json, bool showError)
    {
        try
        {
            var manifest = ParseManifestFromJson(json, "manual-form-populate");
            ApplyManifestToForm(manifest);
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception ex)
        {
            if (showError)
            {
                ShowError($"解析 JSON 失败：{ex.Message}");
            }

            return false;
        }
    }

    private void FormatManualJsonEditor(bool showError)
    {
        try
        {
            var formatted = FormatJsonText(ManualJsonInputBox.Text);
            if (!string.Equals(ManualJsonInputBox.Text, formatted, StringComparison.Ordinal))
            {
                ManualJsonInputBox.Text = formatted;
            }

            ManualJsonInputBox.CaretIndex = 0;
            ManualJsonInputBox.ScrollToHome();
            ErrorText.Visibility = Visibility.Collapsed;
            UpdateManualJsonValidationState();
            RefreshAllState();
        }
        catch (Exception ex)
        {
            if (showError)
            {
                ShowError($"格式化 JSON 失败：{ex.Message}");
            }
        }
    }

    private void FindNextManualJsonMatch()
    {
        var query = ManualJsonFindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            ManualJsonStatusText.Text = "请输入查找内容。";
            ManualJsonStatusText.Foreground = Text3Brush;
            return;
        }

        if (JsonWebViewEditor.Visibility == Visibility.Visible && _isJsonEditorReady)
        {
            // 通过 JavaScript 在 Monaco 视图中触发搜索高亮并滚动
            _ = JsonWebViewEditor.ExecuteScriptAsync($"findNext({JsonSerializer.Serialize(query)})");
            ManualJsonStatusText.Text = "已定位匹配内容。";
            ManualJsonStatusText.Foreground = GreenBrush;
        }
        else
        {
            var text = ManualJsonInputBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var comparison = StringComparison.OrdinalIgnoreCase;
            var start = ManualJsonInputBox.SelectionStart + Math.Max(ManualJsonInputBox.SelectionLength, 0);
            var index = text.IndexOf(query, Math.Min(start, text.Length), comparison);
            if (index < 0 && start > 0)
            {
                index = text.IndexOf(query, 0, comparison);
            }

            if (index < 0)
            {
                ManualJsonStatusText.Text = "未找到匹配内容。";
                ManualJsonStatusText.Foreground = RedBrush;
                return;
            }

            SelectManualJsonRange(index, query.Length);
            ManualJsonStatusText.Text = "已定位匹配内容。";
            ManualJsonStatusText.Foreground = GreenBrush;
        }
    }

    private void ReplaceCurrentManualJsonMatch()
    {
        var query = ManualJsonFindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            ManualJsonStatusText.Text = "请输入查找内容。";
            ManualJsonStatusText.Foreground = Text3Brush;
            return;
        }

        var replacement = ManualJsonReplaceBox.Text ?? string.Empty;

        if (JsonWebViewEditor.Visibility == Visibility.Visible && _isJsonEditorReady)
        {
            // 在 Monaco 中替换当前匹配
            _ = JsonWebViewEditor.ExecuteScriptAsync($"replaceCurrent({JsonSerializer.Serialize(query)}, {JsonSerializer.Serialize(replacement)})");
            ManualJsonStatusText.Text = "已替换当前匹配。";
            ManualJsonStatusText.Foreground = GreenBrush;
        }
        else
        {
            var selected = ManualJsonInputBox.SelectedText;
            if (!string.Equals(selected, query, StringComparison.OrdinalIgnoreCase))
            {
                FindNextManualJsonMatch();
                selected = ManualJsonInputBox.SelectedText;
                if (!string.Equals(selected, query, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var start = ManualJsonInputBox.SelectionStart;
            ManualJsonInputBox.SelectedText = replacement;
            SelectManualJsonRange(start, replacement.Length);
            ManualJsonStatusText.Text = "已替换当前匹配。";
            ManualJsonStatusText.Foreground = GreenBrush;
        }
    }

    private void ReplaceAllManualJsonMatches()
    {
        var query = ManualJsonFindBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            ManualJsonStatusText.Text = "请输入查找内容。";
            ManualJsonStatusText.Foreground = Text3Brush;
            return;
        }

        var replacement = ManualJsonReplaceBox.Text ?? string.Empty;

        if (JsonWebViewEditor.Visibility == Visibility.Visible && _isJsonEditorReady)
        {
            // 在 Monaco 中批量替换所有匹配
            _ = JsonWebViewEditor.ExecuteScriptAsync($"replaceAll({JsonSerializer.Serialize(query)}, {JsonSerializer.Serialize(replacement)})");
            ManualJsonStatusText.Text = "已替换全部匹配。";
            ManualJsonStatusText.Foreground = GreenBrush;
        }
        else
        {
            var text = ManualJsonInputBox.Text;
            var comparison = StringComparison.OrdinalIgnoreCase;
            var builder = new StringBuilder(text.Length);
            var count = 0;
            var index = 0;

            while (index < text.Length)
            {
                var match = text.IndexOf(query, index, comparison);
                if (match < 0)
                {
                    builder.Append(text, index, text.Length - index);
                    break;
                }

                builder.Append(text, index, match - index);
                builder.Append(replacement);
                index = match + query.Length;
                count++;
            }

            if (count == 0)
            {
                ManualJsonStatusText.Text = "未找到匹配内容。";
                ManualJsonStatusText.Foreground = RedBrush;
                return;
            }

            ManualJsonInputBox.Text = builder.ToString();
            ManualJsonInputBox.Focus();
            ManualJsonInputBox.CaretIndex = 0;
            ManualJsonStatusText.Text = $"已替换 {count} 处。";
            ManualJsonStatusText.Foreground = GreenBrush;
        }
    }

    private void SelectManualJsonRange(int start, int length)
    {
        ManualJsonInputBox.Focus();
        ManualJsonInputBox.Select(start, length);
        var line = ManualJsonInputBox.GetLineIndexFromCharacterIndex(start);
        if (line >= 0)
        {
            ManualJsonInputBox.ScrollToLine(line);
        }
    }

    private LocalExtensionManifest BuildManifestFromForm()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            throw new InvalidOperationException("扩展名称不能为空。");
        }

        var runtime = NullIfEmpty(RuntimeBox.Text);
        var entryMode = NullIfEmpty(EntryModeBox.Text);
        var scriptSource = NullIfEmpty(ScriptSourceBox.Text);
        var preservedManifest = TryParsePreservedManifest();

        return new LocalExtensionManifest
        {
            Id = GetOrCreateFormExtensionId(),
            Name = NameBox.Text.Trim(),
            Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? "0.1.0" : VersionBox.Text.Trim(),
            Category = NullIfEmpty(CategoryBox.Text),
            Description = NullIfEmpty(DescriptionBox.Text),
            Keywords = SplitCsv(KeywordsBox.Text),
            OpenTarget = NullIfEmpty(OpenTargetBox.Text),
            QueryPrefixes = SplitCsv(QueryPrefixesBox.Text),
            QueryTargetTemplate = NullIfEmpty(QueryTargetTemplateBox.Text),
            Icon = NullIfEmpty(IconBox.Text),
            AccentHex = NormalizeAccentHexOrNull(AccentHexBox.Text),
            HostedView = _manualHostedView ?? preservedManifest?.HostedView,
            HostedViewV2 = preservedManifest?.HostedViewV2,
            HostedViewXaml = preservedManifest?.HostedViewXaml,
            App = preservedManifest?.App,
            GlobalShortcut = NullIfEmpty(GlobalShortcutBox.Text),
            HotkeyBehavior = NullIfEmpty(HotkeyBehaviorBox.Text),
            Runtime = runtime,
            UiMode = string.Equals(runtime, "csharp", StringComparison.OrdinalIgnoreCase) ? NullIfEmpty(_manualUiMode) : null,
            EntryMode = entryMode,
            Entry = NullIfEmpty(EntryBox.Text),
            Permissions = SplitCsv(PermissionsBox.Text),
            Script = string.IsNullOrWhiteSpace(scriptSource) ? null : new LocalExtensionInlineScriptManifest
            {
                Source = ScriptSourceBox.Text.ReplaceLineEndings("\r\n")
            },
            Startup = (string.IsNullOrWhiteSpace(StartupModeBox.Text) && string.IsNullOrWhiteSpace(StartupScheduleBox.Text))
                ? null
                : new LocalExtensionStartupManifest
                {
                    Mode = NullIfEmpty(StartupModeBox.Text),
                    Schedule = NullIfEmpty(StartupScheduleBox.Text)
                },
            SearchProvider = _manualSearchProvider,
            MouseGesture = NormalizeMouseGestureForManifest(_manualMouseGesture)
        };
    }

    private string GetOrCreateFormExtensionId()
    {
        if (!string.IsNullOrWhiteSpace(IdBox.Text))
        {
            return IdBox.Text.Trim();
        }

        var id = LocalExtensionCatalog.CreateSystemExtensionId();
        IdBox.Text = id;
        return id;
    }

    private LocalExtensionManifest? TryParsePreservedManifest()
    {
        if (!_isEditMode)
        {
            return null;
        }

        foreach (var candidate in new[] { ManualJsonInputBox.Text, _initialJson })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(
                    ExtractJsonPayload(candidate),
                    CreateJsonOptions());
                if (manifest?.App != null ||
                    manifest?.HostedView != null ||
                    manifest?.HostedViewV2 != null ||
                    manifest?.HostedViewXaml != null)
                {
                    return manifest;
                }
            }
            catch
            {
                // Preserve is a best-effort guard for custom protocols; normal validation reports malformed JSON elsewhere.
            }
        }

        return null;
    }

    private LocalExtensionMouseGestureManifest? NormalizeMouseGestureForManifest(LocalExtensionMouseGestureManifest? gesture)
    {
        if (gesture == null)
        {
            return null;
        }

        return new LocalExtensionMouseGestureManifest
        {
            Trigger = GetSelectedGestureTrigger(),
            Sequence = gesture.Sequence,
            Sign = gesture.Sign,
            Data = gesture.Data,
            Tolerance = gesture.Tolerance,
            MinDistance = gesture.MinDistance
        };
    }

    private void ApplyManifestToForm(LocalExtensionManifest manifest)
    {
        IdBox.Text = string.IsNullOrWhiteSpace(manifest.Id)
            ? LocalExtensionCatalog.CreateSystemExtensionId()
            : manifest.Id;
        NameBox.Text = manifest.Name;
        VersionBox.Text = manifest.Version;
        CategoryBox.Text = manifest.Category ?? string.Empty;
        DescriptionBox.Text = manifest.Description ?? string.Empty;
        KeywordsBox.Text = manifest.Keywords == null ? string.Empty : string.Join(", ", manifest.Keywords);
        OpenTargetBox.Text = manifest.OpenTarget ?? string.Empty;
        QueryPrefixesBox.Text = manifest.QueryPrefixes == null ? string.Empty : string.Join(", ", manifest.QueryPrefixes);
        QueryTargetTemplateBox.Text = manifest.QueryTargetTemplate ?? string.Empty;
        IconBox.Text = manifest.Icon ?? string.Empty;
        AccentHexBox.Text = manifest.AccentHex ?? string.Empty;
        GlobalShortcutBox.Text = manifest.GlobalShortcut ?? string.Empty;
        HotkeyBehaviorBox.Text = manifest.HotkeyBehavior ?? string.Empty;
        RuntimeBox.Text = manifest.Runtime ?? string.Empty;
        EntryModeBox.Text = manifest.EntryMode ?? string.Empty;
        EntryBox.Text = manifest.Entry ?? string.Empty;
        PermissionsBox.Text = manifest.Permissions == null ? string.Empty : string.Join(", ", manifest.Permissions);
        ScriptSourceBox.Text = manifest.Script?.Source ?? string.Empty;
        StartupModeBox.Text = manifest.Startup?.Mode ?? string.Empty;
        StartupScheduleBox.Text = manifest.Startup?.Schedule ?? string.Empty;
        _manualHostedView = manifest.HostedView;
        _manualSearchProvider = manifest.SearchProvider;
        _manualMouseGesture = manifest.MouseGesture;
        _manualUiMode = manifest.UiMode;
        SafeRefreshIconPreview();
    }

    private void RefreshIconPreview()
    {
        IconPreviewImage.Visibility = Visibility.Collapsed;
        IconPreviewImage.Source = null;
        IconPreviewVectorHost.Visibility = Visibility.Collapsed;
        IconPreviewVector.Data = null;
        IconPreviewGlyph.Visibility = Visibility.Collapsed;

        var iconReference = NullIfEmpty(IconBox.Text);
        var previewDirectory = ResolvePreviewDirectory();

        var imageSource = ExtensionIconLibrary.ResolveImageSource(iconReference, previewDirectory);
        if (imageSource != null)
        {
            IconPreviewImage.Source = imageSource;
            IconPreviewImage.Visibility = Visibility.Visible;
            IconPreviewHostBackgroundToImage();
            IconPreviewHintText.Text = "当前使用图片图标或本地图标路径。";
            HighlightSelectedBuiltInButton(null);
            return;
        }

        var vectorIcon = ExtensionIconLibrary.ResolveVectorIcon(iconReference);
        if (vectorIcon != null)
        {
            IconPreviewVector.Data = vectorIcon;
            IconPreviewVectorHost.Visibility = Visibility.Visible;
            
            var vectorColorHex = "#FFFFFFFF";
            if (iconReference != null && iconReference.LastIndexOf('#') is var hashIdx && hashIdx > 0)
            {
                vectorColorHex = iconReference[hashIdx..];
            }
            try
            {
                IconPreviewVector.Fill = CreateBrush(vectorColorHex);
            }
            catch
            {
                IconPreviewVector.ClearValue(System.Windows.Shapes.Shape.FillProperty);
            }

            IconPreviewHostBackgroundToAccent();
            IconPreviewHintText.Text = $"当前使用内置图标：{iconReference}";
            HighlightSelectedBuiltInButton(iconReference);
            return;
        }

        IconPreviewGlyph.Text = InferFallbackGlyph();
        IconPreviewGlyph.Visibility = Visibility.Visible;
        IconPreviewHostBackgroundToAccent();
        IconPreviewHintText.Text = string.IsNullOrWhiteSpace(iconReference)
            ? "未设置图标时会回退为字母标识。"
            : $"当前 icon 值未解析成功：{iconReference}";
        HighlightSelectedBuiltInButton(null);
    }

    private void IconPreviewHostBackgroundToImage()
    {
        PreviewIconHost.Background = MediaBrushes.Transparent;
    }

    private void IconPreviewHostBackgroundToAccent()
    {
        var hex = AccentHexBox.Text?.Trim();
        try
        {
            PreviewIconHost.Background = CreateBrush(NormalizeAccentHexOrDefault(hex));
        }
        catch
        {
            PreviewIconHost.Background = AccentBrush;
        }
    }

    private void SafeRefreshIconPreview()
    {
        try
        {
            RefreshIconPreview();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AddJson icon preview failed: {ex}");
            IconPreviewImage.Visibility = Visibility.Collapsed;
            IconPreviewImage.Source = null;
            IconPreviewVectorHost.Visibility = Visibility.Collapsed;
            IconPreviewVector.Data = null;
            IconPreviewGlyph.Text = InferFallbackGlyph();
            IconPreviewGlyph.Visibility = Visibility.Visible;
            IconPreviewHintText.Text = "图标预览失败，已回退为字母标识。";
            HighlightSelectedBuiltInButton(null);
        }
    }

    private string ResolvePreviewDirectory()
    {
        if (string.IsNullOrWhiteSpace(IdBox.Text))
        {
            return HostAssets.ExtensionsPath;
        }

        var trimmedId = IdBox.Text.Trim();
        if (trimmedId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return HostAssets.ExtensionsPath;
        }

        return Path.Combine(HostAssets.ExtensionsPath, trimmedId);
    }

    private void HighlightSelectedBuiltInButton(string? selectedReference)
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(BuiltInIconsList))
        {
            var isSelected = !string.IsNullOrWhiteSpace(selectedReference) &&
                             string.Equals(button.Tag as string, selectedReference, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = isSelected ? AccentBrush : BorderSoftBrush;
            button.Background = isSelected ? AccentGlowBrush : CreateBrush("#FF131316");
        }
    }

    private string InferFallbackGlyph()
    {
        if (!string.IsNullOrWhiteSpace(NameBox.Text))
        {
            return NameBox.Text.Trim()[0].ToString().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(IdBox.Text))
        {
            return IdBox.Text.Trim()[0].ToString().ToUpperInvariant();
        }

        return "E";
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private async Task<TestExecutionResult> RunExtensionTestAsync(bool useManualJson)
    {
        var normalizedJson = ExtractJsonPayload(useManualJson ? ManualJsonInputBox.Text : AiJsonInputBox.Text);
        var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
            ?? throw new InvalidOperationException("JSON 解析失败。");

        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"扩展：{manifest.Name} ({manifest.Id})");
        logBuilder.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        logBuilder.AppendLine();

        if (manifest.HostedViewXaml != null || manifest.HostedViewV2 != null || manifest.HostedView != null)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yanzi-extension-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var command = BuildTestCommand(manifest, tempDirectory);
                var mainWindow = Owner as MainWindow
                    ?? System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow == null)
                {
                    logBuilder.AppendLine("未找到主窗口实例，无法预览 hostedView。");
                    return new TestExecutionResult(false, "没有可用的主窗口来预览该扩展。", logBuilder.ToString());
                }

                Hide();
                await mainWindow.Dispatcher.InvokeAsync(() => mainWindow.PreviewHostedViewForTestAsync(command, editorWindowToRestore: this)).Task.Unwrap();
                var hostedViewType = manifest.HostedViewXaml?.Type ?? manifest.HostedViewV2?.Type ?? manifest.HostedView?.Type ?? "unknown";
                logBuilder.AppendLine("类型：宿主内置界面扩展");
                logBuilder.AppendLine($"视图类型：{hostedViewType}");
                logBuilder.AppendLine($"窗口宽度：{manifest.HostedViewXaml?.Window?.Width?.ToString("0") ?? manifest.HostedViewV2?.Window?.Width?.ToString("0") ?? manifest.HostedView?.WindowWidth?.ToString("0") ?? "默认"}");
                logBuilder.AppendLine($"窗口高度：{manifest.HostedViewXaml?.Window?.Height?.ToString("0") ?? manifest.HostedViewV2?.Window?.Height?.ToString("0") ?? manifest.HostedView?.WindowHeight?.ToString("0") ?? "默认"}");
                logBuilder.AppendLine("已拉起主窗口并打开扩展视图。");
                logBuilder.AppendLine(manifest.HostedViewXaml != null
                    ? "当前 hostedViewXaml 使用动态 XAML 宿主能力。"
                    : "当前 hostedViewV2 使用受限组件协议，不支持直接声明任意 WPF 控件树。");
                return new TestExecutionResult(true, "测试通过，已在主窗口中打开该扩展界面。", logBuilder.ToString());
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Runtime))
        {
            if (!string.Equals(manifest.EntryMode, "inline", StringComparison.OrdinalIgnoreCase))
            {
                return new TestExecutionResult(
                    false,
                    "当前 JSON 使用的是外部脚本入口，测试前需要先保存脚本文件到扩展目录。",
                    logBuilder.AppendLine("当前只支持直接测试内联脚本扩展。").ToString());
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "yanzi-extension-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var retainTempDirectory = false;
            try
            {
                var command = BuildTestCommand(manifest, tempDirectory);
                var result = await Task.Run(
                    () => ScriptExtensionRunner.ExecuteAsync(command, "测试输入", "extension-editor-test"),
                    CancellationToken.None);
                logBuilder.AppendLine($"执行结果：{(result.Success ? "成功" : "失败")}");
                logBuilder.AppendLine($"退出码：{result.ExitCode}");
                logBuilder.AppendLine();
                logBuilder.AppendLine("标准输出：");
                logBuilder.AppendLine(string.IsNullOrWhiteSpace(result.Output) ? "无输出。" : result.Output.Trim());
                logBuilder.AppendLine();
                logBuilder.AppendLine("错误输出：");
                logBuilder.AppendLine(string.IsNullOrWhiteSpace(result.Error) ? "无错误输出。" : result.Error.Trim());

                var nativeWindowStarted = string.Equals(result.Output, "native-window-started", StringComparison.Ordinal);
                retainTempDirectory = nativeWindowStarted;
                if (retainTempDirectory)
                {
                    HostAssets.AppendLog($"AddJson native-window test retained temp directory: {tempDirectory}");
                }

                return new TestExecutionResult(
                    result.Success,
                    result.Success
                        ? (nativeWindowStarted
                            ? "测试通过，原生窗口已启动，编辑器不会等待窗口关闭。"
                            : "测试通过，脚本已经成功执行。")
                        : "测试未通过，请根据下方日志检查脚本。",
                    logBuilder.ToString());
            }
            finally
            {
                if (!retainTempDirectory)
                {
                    TryDeleteDirectory(tempDirectory);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.QueryTargetTemplate))
        {
            var sampleQuery = "测试关键词";
            var preview = manifest.QueryTargetTemplate.Replace("{query}", Uri.EscapeDataString(sampleQuery), StringComparison.Ordinal);
            Process.Start(new ProcessStartInfo
            {
                FileName = preview,
                UseShellExecute = true
            });
            logBuilder.AppendLine("类型：网页搜索扩展");
            logBuilder.AppendLine($"示例关键词：{sampleQuery}");
            logBuilder.AppendLine($"预览地址：{preview}");
            logBuilder.AppendLine("已实际打开搜索地址。");
            return new TestExecutionResult(true, "测试通过，已实际打开搜索结果地址。", logBuilder.ToString());
        }

        if (manifest.SearchProvider != null)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "yanzi-extension-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var command = BuildTestCommand(manifest, tempDirectory);
                var mainWindow = Owner as MainWindow
                    ?? System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow == null)
                {
                    logBuilder.AppendLine("未找到主窗口实例，无法预览搜索提供器。");
                    return new TestExecutionResult(false, "没有可用的主窗口来预览文件夹搜索。", logBuilder.ToString());
                }

                await mainWindow.Dispatcher.InvokeAsync(() => mainWindow.OpenSearchProviderInLauncher(command, string.Empty));
                logBuilder.AppendLine("类型：扩展搜索提供器");
                logBuilder.AppendLine($"提供器：{manifest.SearchProvider.Type}");
                logBuilder.AppendLine($"搜索目录：{manifest.SearchProvider.Path ?? manifest.OpenTarget ?? "未设置"}");
                logBuilder.AppendLine("已拉起主窗口并进入该扩展的搜索输入。");
                return new TestExecutionResult(true, "测试通过，已在主窗口中打开搜索入口。", logBuilder.ToString());
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.OpenTarget))
        {
            var target = manifest.OpenTarget.Trim();
            var exists = File.Exists(target) || Directory.Exists(target);
            var isUri = Uri.TryCreate(target, UriKind.Absolute, out _);
            // 系统协议（shell:、ms-settings:、ms-photos: 之类）和 PATH 查得到的可执行文件也算合法目标
            var isShellProtocol = target.Contains(':') && !Path.IsPathFullyQualified(target);
            var resolvedFromPath = !exists && TryResolveExecutableOnPath(target);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            logBuilder.AppendLine("类型：打开目标扩展");
            logBuilder.AppendLine($"目标：{target}");
            logBuilder.AppendLine($"本地存在：{exists}");
            logBuilder.AppendLine($"绝对地址：{isUri}");
            if (resolvedFromPath)
            {
                logBuilder.AppendLine("PATH 解析：找到对应可执行文件。");
            }
            if (isShellProtocol)
            {
                logBuilder.AppendLine("协议地址：识别为 shell 或系统协议。");
            }
            logBuilder.AppendLine("已实际执行打开动作。");
            var success = exists || isUri || isShellProtocol || resolvedFromPath;
            return new TestExecutionResult(
                success,
                success ? "测试通过，已实际执行打开动作。" : "测试未通过，目标既不是可访问地址，也不是现有文件/目录。",
                logBuilder.ToString());
        }

        logBuilder.AppendLine("未检测到 runtime、queryTargetTemplate 或 openTarget。");
        return new TestExecutionResult(false, "当前扩展缺少可测试的执行入口。", logBuilder.ToString());
    }

    private static bool TryResolveExecutableOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(new[] { '\\', '/' }) >= 0)
        {
            return false;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var hasExt = Path.HasExtension(fileName);

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (hasExt)
                {
                    if (File.Exists(Path.Combine(dir, fileName))) return true;
                }
                else
                {
                    foreach (var ext in pathExt)
                    {
                        if (File.Exists(Path.Combine(dir, fileName + ext))) return true;
                    }
                }
            }
            catch
            {
                // ignore unparseable PATH entries
            }
        }
        return false;
    }

    private async Task RunTestAndRenderAsync(
        System.Windows.Controls.Button triggerButton,
        Border resultPanel,
        TextBlock summaryText,
        System.Windows.Controls.TextBox logTextBox,
        System.Windows.Controls.Button copyFailureButton,
        bool useManualJson)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            triggerButton.IsEnabled = false;
            triggerButton.Content = "测试中...";
            resultPanel.Visibility = Visibility.Visible;
            copyFailureButton.Visibility = Visibility.Collapsed;
            copyFailureButton.Content = "复制日志";
            copyFailureButton.Background = MediaBrushes.Transparent;
            copyFailureButton.BorderBrush = BorderStrongBrush;
            summaryText.Text = "正在执行测试，请稍等。";
            logTextBox.Text = string.Empty;
            await Dispatcher.Yield(DispatcherPriority.Background);

            var result = await RunExtensionTestAsync(useManualJson);
            _testCompleted = true;
            _testSucceeded = result.Success;

            summaryText.Foreground = result.Success ? GreenBrush : RedBrush;
            summaryText.Text = result.Summary;
            logTextBox.Text = result.Log;
            copyFailureButton.Visibility = string.IsNullOrWhiteSpace(result.Log) ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            _testCompleted = true;
            _testSucceeded = false;
            resultPanel.Visibility = Visibility.Visible;
            summaryText.Foreground = RedBrush;
            summaryText.Text = "测试执行失败。";
            logTextBox.Text = ex.ToString();
            copyFailureButton.Visibility = Visibility.Visible;
        }
        finally
        {
            triggerButton.IsEnabled = _lastJsonValid;
            triggerButton.Content = "测试扩展";
            RefreshAllState();
        }
    }

    private async Task CopyTestLogToClipboardAsync(
        System.Windows.Controls.Button button,
        string log)
    {
        try
        {
            await Task.Run(() => CopyTextToClipboard(log));
            button.Content = "已复制";
            button.Background = GreenBrush;
            button.BorderBrush = GreenBrush;
        }
        catch (Exception ex)
        {
            ShowError($"复制日志失败：{ex.Message}");
        }
    }

    private static CommandItem BuildTestCommand(LocalExtensionManifest manifest, string tempDirectory)
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, CreateJsonOptions()));

        return new CommandItem(
            glyph: "E",
            title: manifest.Name,
            subtitle: manifest.Description ?? "临时测试扩展",
            category: manifest.Category ?? "扩展",
            accentHex: NormalizeAccentHexOrDefault(manifest.AccentHex),
            openTarget: manifest.OpenTarget,
            keywords: manifest.Keywords ?? [],
            source: CommandSource.LocalExtension,
            extensionId: manifest.Id,
            declaredVersion: manifest.Version,
            extensionDirectoryPath: tempDirectory,
            queryPrefixes: manifest.QueryPrefixes,
            queryTargetTemplate: manifest.QueryTargetTemplate,
            hostedView: manifest.HostedViewXaml?.ToDefinition() ?? manifest.HostedViewV2?.ToDefinition() ?? manifest.HostedView?.ToDefinition(),
            globalShortcut: manifest.GlobalShortcut,
            hotkeyBehavior: manifest.HotkeyBehavior,
            runtime: manifest.Runtime,
            uiMode: manifest.UiMode,
            entryPoint: manifest.Entry,
            permissions: manifest.Permissions ?? [],
            entryMode: manifest.EntryMode,
            inlineScriptSource: manifest.Script?.Source,
            iconReference: manifest.Icon,
            searchProvider: manifest.SearchProvider?.ToDefinition(manifest.OpenTarget));
    }

    private static string BuildGenerationPrompt(string request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请生成一个 Yanzi 扩展 manifest JSON。");
        builder.AppendLine();
        builder.AppendLine("需求：");
        builder.AppendLine(request);
        builder.AppendLine();
        builder.AppendLine("能力概览：");
        builder.AppendLine("- openTarget：直接打开网页、程序、文件、文件夹或系统协议，例如 ms-settings:bluetooth。");
        builder.AppendLine("- queryPrefixes + queryTargetTemplate：网页搜索或带前缀输入。脚本扩展也可用 queryPrefixes 接收输入，前缀后的内容会进入 context.InputText。");
        builder.AppendLine("- runtime 只支持 csharp / powershell。entryMode=inline 时源码放在 script.source；外部脚本用 entry。");
        builder.AppendLine("- PowerShell 适合 Windows 自动化、注册表、服务、进程、计划任务、系统命令、cmd/bat 命令包装和已有 cmdlet。不要写 runtime=cmd/bat；需要 cmd/bat 时用 powershell 调 cmd /c 或 Start-Process。");
        builder.AppendLine("- C# 适合复杂逻辑、JSON/HTTP/文件处理、P/Invoke、强类型 .NET API、System.Drawing/System.Management、原生 WPF 窗口。");
        builder.AppendLine("- hostedViewXaml 可做宿主内工作区；C# 加 uiMode=native-window 可做独立 WPF 窗口。");
        builder.AppendLine("- 宿主 context 只提供管家能力：InputText、LaunchSource、ExtensionDirectory、ExtensionDataDirectory、Now、Permissions、State、SetStateAsync、Storage、ViewState、UpdateView。其它功能请用 C# / PowerShell / Windows 原生能力实现。");
        builder.AppendLine("- 如果需要向绑定的手机端发送横幅通知，请向本机的燕子服务发送 HTTP POST 请求到 http://127.0.0.1:{端口}/v1/notify（默认端口 53919，Header 需携带 Authorization: Bearer yanzi-local-dev-token），Body 格式为 {\"title\":\"标题\",\"body\":\"内容\"}，且必须使用绝对标准的 UTF-8 编码字节流发送，否则手机上会显示 ??? 乱码。");
        builder.AppendLine();
        builder.AppendLine("选择策略：");
        builder.AppendLine("- 能用 openTarget 或 queryTargetTemplate 完成就不要写脚本。");
        builder.AppendLine("- 系统配置和命令行自动化优先 PowerShell；复杂应用逻辑和窗口工具优先 C#。");
        builder.AppendLine("- 需要界面时，简单宿主工作区用 hostedViewXaml，独立窗口工具用 C# native-window。");
        builder.AppendLine();
        builder.AppendLine("脚本约定：");
        builder.AppendLine("- C# 内联入口：public static class YanziAction，并实现 public static Task<string> RunAsync(YanziActionContext context)。System、System.Threading.Tasks、System.IO、System.Linq 等常用命名空间和宿主运行时会自动导入。");
        builder.AppendLine("- PowerShell 内联脚本建议第一行写 param([string]$InputText = \"\", [string]$ContextPath = \"\")，成功信息写 stdout，失败请 throw 或写 stderr。");
        builder.AppendLine("- PowerShell 调 .NET 静态方法时类型名必须完整包在方括号里，例如 [System.Drawing.ColorTranslator]::ToWin32([System.Drawing.Color]::Gold)，不要写成 [System.Drawing.ColorTranslator::ToWin32(...)。");
        builder.AppendLine("- 如果脚本修改 Windows 个性化、壁纸、设备、网络等外部状态，请调用真正生效的系统 API / cmdlet，并检查返回值；不要只写注册表就宣称成功。");
        builder.AppendLine("- 不要默认用 Disable-PnpDevice / Enable-PnpDevice 做蓝牙、Wi-Fi、网卡、USB 开关；这会禁用硬件设备且需要管理员权限，除非需求明确是禁用硬件。");
        builder.AppendLine("- 如果需要管理员权限，请明确提示；C# 不要同时设置 Verb=\"runas\" 和 UseShellExecute=false。");
        builder.AppendLine();
        builder.AppendLine("输出要求：");
        builder.AppendLine("- 只返回一个 ```json 代码块，不要解释，不要额外文字。");
        builder.AppendLine("- JSON 必须能被 System.Text.Json 解析；不要写注释、尾随逗号或 null 字段。");
        builder.AppendLine("- 必填字段：id、name、version、category、description、keywords。");
        builder.AppendLine("- 常用字段：icon、accentHex、openTarget、queryPrefixes、queryTargetTemplate、runtime、entryMode、entry、permissions、script.source、hostedViewXaml、uiMode。");
        builder.AppendLine("- id 用英文小写、数字、短横线；accentHex 支持 #RRGGBB 或 #AARRGGBB。");
        builder.AppendLine();
        builder.AppendLine("最小示例：");
        builder.AppendLine("打开类：{\"id\":\"open-settings\",\"name\":\"打开设置\",\"version\":\"0.1.0\",\"category\":\"系统\",\"description\":\"打开 Windows 设置。\",\"keywords\":[\"设置\"],\"icon\":\"mdi:cog\",\"openTarget\":\"ms-settings:\"}");
        builder.AppendLine("PowerShell：{\"id\":\"ps-demo\",\"name\":\"PowerShell 示例\",\"version\":\"0.1.0\",\"category\":\"脚本\",\"description\":\"执行 PowerShell。\",\"keywords\":[\"ps\"],\"runtime\":\"powershell\",\"entryMode\":\"inline\",\"permissions\":[],\"script\":{\"source\":\"param([string]$InputText = \\\"\\\", [string]$ContextPath = \\\"\\\")\\nWrite-Output $InputText\"}}");
        builder.AppendLine("C#：{\"id\":\"csharp-demo\",\"name\":\"C# 示例\",\"version\":\"0.1.0\",\"category\":\"脚本\",\"description\":\"执行 C#。\",\"keywords\":[\"csharp\"],\"runtime\":\"csharp\",\"entryMode\":\"inline\",\"permissions\":[],\"script\":{\"source\":\"public static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(context.InputText ?? string.Empty);\\n    }\\n}\"}}");
        return builder.ToString();
    }

    // Legacy verbose prompt kept temporarily for comparison; UI copy actions use BuildGenerationPrompt.
    private static string BuildDetailedPrompt(string request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请帮我生成一个 Yanzi 扩展的完整 JSON 配置。");
        builder.AppendLine();
        builder.AppendLine("一、背景说明");
        builder.AppendLine("这个产品的设计理念是“万物皆扩展”。用户会在桌面启动器、快捷面板、鼠标呼出面板里运行扩展。");
        builder.AppendLine("扩展可以是：");
        builder.AppendLine("1. 直接打开网页、程序、文件、文件夹");
        builder.AppendLine("2. 做网页搜索");
        builder.AppendLine("3. 运行脚本处理输入内容");
        builder.AppendLine("4. 用 C#/.NET/WPF/Windows 原生能力完成系统操作或独立工具");
        builder.AppendLine("5. 在宿主界面里展示一个简单工作区");
        builder.AppendLine("宿主的角色是管家：负责搜索框入口、输入传递、扩展状态、本地/云端存储和少量受控宿主视图动作；除这些已声明 API 外，功能实现应优先写原生 C#，不要臆造宿主封装方法。");
        builder.AppendLine();
        builder.AppendLine("我的需求是：");
        builder.AppendLine(request);
        builder.AppendLine();
        builder.AppendLine("二、输出要求");
        builder.AppendLine("1. 只返回一个 ```json 代码块，不要解释，不要额外文字");
        builder.AppendLine("2. JSON 必须能直接被 System.Text.Json 解析");
        builder.AppendLine("3. 如果最简单的配置就能实现，不要过度设计");
        builder.AppendLine("4. 优先选择最贴近需求的方案：");
        builder.AppendLine("   - 打开类：优先用 openTarget");
        builder.AppendLine("   - 搜索类：优先用 queryPrefixes + queryTargetTemplate");
        builder.AppendLine("   - 脚本类：按任务选择 runtime，不要机械固定用 C#");
        builder.AppendLine("   - 复杂业务逻辑、JSON/HTTP/文件处理、原生 WPF 窗口、P/Invoke、需要强类型 .NET API 时，优先用 runtime = csharp");
        builder.AppendLine("   - Windows 自动化、注册表/服务/进程/计划任务/系统命令、已有 PowerShell cmdlet 能直接完成的任务，优先用 runtime = powershell");
        builder.AppendLine("   - 如果需求本质上是一串 cmd/bat 命令，优先用 powershell 包装执行或输出外部 .bat 入口，不要为了简单命令硬套 C#");
        builder.AppendLine("   - 内联脚本：使用 entryMode = inline 和 script.source");
        builder.AppendLine("   - 需要窗口、复杂交互、文件/进程/剪贴板/注册表/Win32 调用时，优先使用语言/系统原生能力，而不是要求宿主提供新的专用 API");
        builder.AppendLine("   - 如果 C# 里必须启动 PowerShell，请优先用 ProcessStartInfo.ArgumentList 或 -EncodedCommand，避免手拼带嵌套双引号的 Arguments 字符串");
        builder.AppendLine("   - 如果进程需要管理员权限，不要同时设置 Verb = \"runas\" 和 UseShellExecute = false；要么 UseShellExecute = true 触发 UAC 且不重定向输出，要么明确提示用户以管理员身份运行");
        builder.AppendLine("   - 蓝牙、Wi-Fi、网卡、USB 等系统开关不要默认使用 Disable-PnpDevice / Enable-PnpDevice；这会禁用硬件设备、让设置页开关消失，且需要管理员权限。除非需求明确是禁用硬件设备，否则优先打开系统设置或给出可恢复的用户操作");
        builder.AppendLine("   - 修改 Windows 个性化、壁纸、系统颜色等设置时，不能只写注册表后直接返回成功；必须调用实际生效 API、检查返回值，必要时生成壁纸文件并调用 SystemParametersInfo(SPI_SETDESKWALLPAPER) 或明确提示需要用户手动刷新/注销");
        builder.AppendLine("   - 脚本测试只能判断代码是否成功执行，不能自动证明桌面背景、系统颜色、网络状态等外部副作用真的生效；这类脚本应自行读取/验证结果后再返回成功");
        builder.AppendLine("   - 宿主会自动引用随应用发布的常用托管 DLL，可直接使用 System.Drawing.Common、System.Management、System.IO.Ports、System.ServiceProcess、System.Diagnostics.EventLog、System.DirectoryServices、System.Security.Cryptography.ProtectedData、System.Text.Encoding.CodePages 等基础库");
        builder.AppendLine("4.1 如果需要用户在主界面输入“前缀 + 内容”后触发扩展，必须提供 queryPrefixes；脚本或工作区扩展会通过 context.InputText 收到去掉前缀后的内容");
        builder.AppendLine("5. 如果是 C# 内联脚本，必须严格遵守宿主约定：");
        builder.AppendLine("   - 必须包含 \"runtime\": \"csharp\"");
        builder.AppendLine("   - 必须包含 \"entryMode\": \"inline\"");
        builder.AppendLine("   - 不要默认包含 \"context.read\"；只有脚本必须读取快捷面板触发前的选中文本/文件时才声明它");
        builder.AppendLine("   - 如果通过 queryPrefixes 或 hostedViewXaml 输入框传入内容，context.InputText 不需要 \"context.read\"");
        builder.AppendLine("   - script.source 不需要写任何宿主运行时 using；编译器已自动导入 YanziActionContext 所在命名空间");
        builder.AppendLine("   - script.source 里声明 public static class YanziAction");
        builder.AppendLine("   - script.source 里实现 public static Task<string> RunAsync(YanziActionContext context)");
        builder.AppendLine("   - 输入内容从 context.InputText 读取");
        builder.AppendLine("   - YanziActionContext 只提供宿主管家能力：InputText、LaunchSource、ExtensionDirectory、ExtensionDataDirectory、Now、Permissions、State、SetStateAsync、Storage、ViewState、UpdateView");
        builder.AppendLine("   - 不要发明 context.SetTheme、context.GetTheme、context.OpenFilePicker、context.ShowMessage、context.GetStateAsync<T>() 等不存在的宿主 API；这些需求应优先用原生 C#/.NET/WPF/Windows API 自己实现");
        builder.AppendLine("   - 不要根据旧命名空间推断 pack URI、程序集名或资源路径；当前应用程序集名是 Yanzi，且没有内置主题资源字典");
        builder.AppendLine("5.1 只有脚本真正创建 WPF 原生窗口时才输出 \"uiMode\": \"native-window\"，典型特征是 new Window、ShowDialog、WindowStartupLocation 或 WindowStyle。仅使用 System.Windows.Clipboard 不属于原生窗口扩展。");
        builder.AppendLine("5.2 只要是 native-window 扩展，就不要再同时输出 hostedViewXaml 或 hostedViewV2");
        builder.AppendLine("5.3 如果需求是独立弹窗小工具、原生窗口小应用、独立编辑器，而不是寄生在宿主里的工作区，优先输出 native-window，而不是 hostedViewXaml");
        builder.AppendLine();
        builder.AppendLine("三、字段说明");
        builder.AppendLine("- id：扩展唯一标识，只能英文小写、数字、短横线，例如 \"open-project-folder\"");
        builder.AppendLine("- name：扩展显示名称");
        builder.AppendLine("- version：版本号，默认 \"0.1.0\"");
        builder.AppendLine("- category：分类，例如 \"扩展\"、\"网页搜索\"、\"效率工具\"");
        builder.AppendLine("- description：一句话描述扩展用途");
        builder.AppendLine("- keywords：搜索关键词数组");
        builder.AppendLine("- icon：图标，可用 mdi:图标名 或图片地址");
        builder.AppendLine("- accentHex：可选，扩展按钮 / 卡片底色，支持 #RRGGBB 或 #AARRGGBB，例如 #10B981、#FFF97316；不要所有扩展都用默认蓝色");
        builder.AppendLine("- openTarget：点击后直接打开的目标");
        builder.AppendLine("- queryPrefixes：前缀数组，例如 [\"百度\", \"baidu\"]；搜索扩展会把后面的内容替换进 {query}，脚本 / 工作区扩展会把后面的内容传给 context.InputText");
        builder.AppendLine("- queryTargetTemplate：搜索模板，必须包含 {query}");
        builder.AppendLine("- searchProvider：可选；如果希望某个扩展被固定到顶部后，在主界面继续输入关键词就返回一组列表结果，可输出 searchProvider");
        builder.AppendLine("- searchProvider.type：当前支持 \"folder\"，表示在指定目录下搜索文件/文件夹");
        builder.AppendLine("- searchProvider.type 也支持 \"script\"；这时扩展自己的脚本需要返回 JSON 结果数组");
        builder.AppendLine("- searchProvider.path：搜索根目录；如果省略且 openTarget 本身是目录，会自动拿 openTarget 当根目录");
        builder.AppendLine("- searchProvider.aliases：可选；固定到顶部后支持 @别名 关键词，例如 @项目 需求文档");
        builder.AppendLine("- searchProvider.includeSubdirectories / includeFiles / includeDirectories / maxResults：可选，控制搜索范围");
        builder.AppendLine("- script provider 的脚本返回格式建议是 JSON 数组，每项包含 title、subtitle、kind、openTarget、keywords、accentHex；kind 可用 file、folder、record、url、script、api");
        builder.AppendLine("- runtime：脚本运行时，例如 \"csharp\" 或 \"powershell\"");
        builder.AppendLine("- uiMode：可选；如果希望 C# 扩展自己弹原生窗口而不是寄生在宿主界面中，可写 \"native-window\"");
        builder.AppendLine("- entryMode：如果是内联脚本请写 \"inline\"");
        builder.AppendLine("- entry：如果是外部脚本文件，写入口文件名");
        builder.AppendLine("- permissions：权限数组，例如 [\"clipboard\", \"network\"]");
        builder.AppendLine("- 宿主 API 边界：context 不是万能能力对象，只能使用本文明确列出的成员；其它能力请在 script.source 中直接使用 C# 原生库、WPF、P/Invoke、Process、File、HttpClient 等实现");
        builder.AppendLine("- 命名边界：产品名和应用名是 Yanzi；不要在 C# 脚本里写旧产品名相关命名空间、程序集引用、pack URI、资源路径或品牌文案。hostedViewXaml 的 oqh:HostedViewBridge 命名空间使用模板给出的 Yanzi 命名空间");
        builder.AppendLine("- 扩展脚本现在支持 context.Storage 本地/云端存储 helper：ReadTextAsync、WriteTextAsync、ReadJsonAsync<T>、WriteJsonAsync<T>");
        builder.AppendLine("- context.Storage 默认支持 scope = local、cloud、both；local 写入本地扩展数据目录，cloud / both 会通过宿主 API 写入坚果云 / WebDAV");
        builder.AppendLine("- context.Storage.ReadTextAsync 的可用写法是：await context.Storage.ReadTextAsync(\"note.txt\", scope: \"both\")；不要传 defaultValue 参数");
        builder.AppendLine("- context.Storage.WriteTextAsync 的可用写法是：await context.Storage.WriteTextAsync(\"note.txt\", content, scope: \"both\")");
        builder.AppendLine("- 如果需要默认值，请自己写：var text = await context.Storage.ReadTextAsync(\"note.txt\", scope: \"both\") ?? string.Empty; 或用 try/catch，不要发明 defaultValue 参数");
        builder.AppendLine("- script.source：内联脚本源码");
        builder.AppendLine("- hostedViewXaml：如果要让宿主直接加载自定义 XAML 界面，请输出 hostedViewXaml");
        builder.AppendLine("- hostedViewXaml.xaml：填写可直接解析的 WPF XAML 字符串，根元素建议用 Grid、UserControl 或 Window");
        builder.AppendLine("- hostedViewXaml.xaml 必须放在合法 JSON 字符串里，内部所有双引号都必须正确转义为 \\\"");
        builder.AppendLine("- hostedViewXaml 是标准 WPF XAML，不是 WinUI / MAUI / UWP / Web 风格标记");
        builder.AppendLine("- hostedViewXaml 中 Grid 没有 Padding 属性；如果要留内边距，请用 Margin、在 Grid 外包一层 Border 并把 Padding 写在 Border 上，或在 StackPanel / Border 上设置间距");
        builder.AppendLine("- hostedViewXaml 中不要使用宿主没有声明的 StaticResource；除非我明确给出资源名，否则不要写 Converter={StaticResource ...}、Style={StaticResource ...} 这类引用");
        builder.AppendLine("- hostedViewXaml 中不要假设存在 InverseBoolConverter、BooleanToVisibilityConverter 或任何自定义 Converter，除非我明确给出");
        builder.AppendLine("- hostedViewXaml.state：初始化状态对象，值可用字符串、数字、布尔；XAML 中可通过 {Binding [key]} 绑定");
        builder.AppendLine("- hostedViewXaml.window.width / height / minWidth / minHeight：可选，控制窗口尺寸");
        builder.AppendLine("- hostedViewXaml 中按钮可用 xmlns:oqh=\"clr-namespace:Yanzi\"，再用 oqh:HostedViewBridge.Action 声明动作");
        builder.AppendLine("- 所有 URL、xmlns、图片地址都必须是纯文本，不要写成 [text](url) 这种 Markdown 链接");
        builder.AppendLine("- oqh:HostedViewBridge.Action 当前支持 close、setState、runScript、loadStorage、saveStorage；多个动作可用 | 分隔，参数用 ;key=value");
        builder.AppendLine("- 根元素还支持 oqh:HostedViewBridge.LoadedAction，可在窗口打开时自动执行 loadStorage");
        builder.AppendLine("- 视图脚本如果要读写界面状态，优先使用 context.State 和 await context.SetStateAsync(...)；兼容写法 context.ViewState / await context.UpdateView() 也支持");
        builder.AppendLine("- 不要使用 context.GetStateAsync<T>()；当前宿主没有这个 API。读取状态请用 context.State[\"key\"]，写状态请用 await context.SetStateAsync(...)");
        builder.AppendLine("- hostedViewXaml 当前更适合工作区、设置页、仪表盘、面板、轻量编辑器；如果需求是独立多窗口工具、复杂原生拖拽、系统级悬浮窗，请改用 native-window");
        builder.AppendLine("- hostedViewXaml 当前没有代码隐藏，不要输出 Click=、TextChanged= 这类事件处理函数名；宿主只会识别 oqh:HostedViewBridge.Action / LoadedAction");
        builder.AppendLine("- hostedViewXaml 当前状态模型偏扁平，优先使用 note、preview、status、path、result、query 这类简单键名，不要假设存在复杂对象树绑定");
        builder.AppendLine("- 如果需求里需要列表、表格、树、拖拽排序、复杂选择器，请先收敛成静态布局 + 按钮动作；当前宿主还没有成熟的列表模板和通用事件桥");
        builder.AppendLine("- 如果需求里需要打开文件、选择目录、消息确认、颜色选择、进度条、取消任务，不要发明宿主 action；请改用 native-window 或 C# 原生 WPF 对话框/控件自己实现");
        builder.AppendLine("- hostedViewV2：如果要在宿主里显示内置界面，也可以输出 hostedViewV2，不要返回 @view: 之类的协议字符串");
        builder.AppendLine("- hostedViewV2.type：当前支持 \"single-pane\"、\"split-horizontal\"");
        builder.AppendLine("- hostedViewV2.window.width / height / minWidth / minHeight：可选，控制窗口尺寸");
        builder.AppendLine("- hostedViewV2.state：初始化状态对象，例如 { \"note\": \"\", \"preview\": \"先输入内容\", \"count\": 0 }");
        builder.AppendLine("- hostedViewV2.components：当前支持 text、textarea、button、markdown");
        builder.AppendLine("- 组件的 bind 字段用于绑定到 state 路径");
        builder.AppendLine("- button.actions：当前支持 setState、runScript、loadStorage、saveStorage");
        builder.AppendLine("- 如果只是旧版简单双栏工作区，也可以输出 hostedView，但新方案优先用 hostedViewXaml 或 hostedViewV2");
        builder.AppendLine("- 如果不想寄生在宿主界面中，而是希望扩展自己弹原生 WPF 窗口，可使用 C# 扩展并设置 uiMode = native-window；这类扩展仍然需要用 YanziActionContext 读取输入、状态和存储");
        builder.AppendLine("- native-window 扩展中的 WPF 窗口代码必须在 STA 线程中创建和显示；如果手动 new Window / TextBox / Button，必须显式创建 STA 线程再 ShowDialog，不要直接在 RunAsync 当前线程里 new Window");
        builder.AppendLine("- 如果需求是笔记、便签、编辑器、独立小应用，并且不寄生在宿主界面中，请优先参考模板 5.1 的原生笔记窗口，不要自己改写窗口启动结构");
        builder.AppendLine("- 如果需求是修改宿主自身界面资源，可使用 System.Windows.Application.Current.Dispatcher 和 Application.Current.Resources 等 WPF 原生对象尝试实现，但不要写 context.SetTheme 这类未声明方法");
        builder.AppendLine("- 不要输出 x:Class，也不要假设宿主会自动解析你自定义的事件处理函数");
        builder.AppendLine();
        builder.AppendLine("四、请优先参考这些模板思路");
        builder.AppendLine();
        builder.AppendLine("模板 1：打开类扩展");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"open-project-folder\",");
        builder.AppendLine("  \"name\": \"打开项目文件夹\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"扩展\",");
        builder.AppendLine("  \"description\": \"打开指定项目目录。\",");
        builder.AppendLine("  \"keywords\": [\"项目\", \"folder\", \"vscode\"],");
        builder.AppendLine("  \"openTarget\": \"C:\\\\Projects\\\\Demo\",");
        builder.AppendLine("  \"icon\": \"mdi:folder\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 2：网页搜索扩展");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"search-baidu\",");
        builder.AppendLine("  \"name\": \"百度搜索\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"网页搜索\",");
        builder.AppendLine("  \"description\": \"用百度搜索关键词。\",");
        builder.AppendLine("  \"keywords\": [\"百度\", \"搜索\", \"网页\"],");
        builder.AppendLine("  \"queryPrefixes\": [\"百度\", \"baidu\"],");
        builder.AppendLine("  \"queryTargetTemplate\": \"https://www.baidu.com/s?wd={query}\",");
        builder.AppendLine("  \"icon\": \"https://www.baidu.com/favicon.ico\"");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 2.1：目录搜索扩展（支持 @别名 和 扩展名+空格 激活）");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"download-folder-search\",");
        builder.AppendLine("  \"name\": \"下载\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"目录搜索\",");
        builder.AppendLine("  \"description\": \"固定到顶部后，在下载目录里继续搜索文件。\",");
        builder.AppendLine("  \"keywords\": [\"下载\", \"文件夹\", \"folder\", \"search\"],");
        builder.AppendLine("  \"icon\": \"mdi:folder-search-outline\",");
        builder.AppendLine("  \"openTarget\": \"C:\\\\Users\\\\你的用户名\\\\Downloads\",");
        builder.AppendLine("  \"searchProvider\": {");
        builder.AppendLine("    \"type\": \"folder\",");
        builder.AppendLine("    \"aliases\": [\"下载\", \"downloads\"],");
        builder.AppendLine("    \"includeSubdirectories\": true,");
        builder.AppendLine("    \"includeFiles\": true,");
        builder.AppendLine("    \"includeDirectories\": false,");
        builder.AppendLine("    \"maxResults\": 120");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine("使用方式：固定到顶部后，可以直接输入 @下载 关键词；也可以在“扩展”标签里输入“下载 空格”显示全部结果，输入“下载 空格 关键词”显示过滤结果。");
        builder.AppendLine();
        builder.AppendLine("模板 2.2：脚本结果列表扩展（script provider）");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"demo-script-search\",");
        builder.AppendLine("  \"name\": \"脚本结果\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"动态结果\",");
        builder.AppendLine("  \"description\": \"通过脚本返回一组动态结果项。\",");
        builder.AppendLine("  \"keywords\": [\"脚本\", \"搜索\", \"结果\"],");
        builder.AppendLine("  \"icon\": \"mdi:code-json\",");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"searchProvider\": {");
        builder.AppendLine("    \"type\": \"script\",");
        builder.AppendLine("    \"aliases\": [\"脚本结果\", \"script\"],");
        builder.AppendLine("    \"maxResults\": 50");
        builder.AppendLine("  },");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System.Text.Json;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var q = (context.InputText ?? string.Empty).Trim();\\n        var items = new object[]\\n        {\\n            new { id = \\\"doc-1\\\", title = \\\"接口文档\\\", subtitle = \\\"脚本生成的示例结果\\\", kind = \\\"record\\\", openTarget = \\\"https://example.com/docs\\\", keywords = new[] { \\\"文档\\\", q }, accentHex = \\\"#FF10B981\\\" },\\n            new { id = \\\"tool-1\\\", title = \\\"打开工具页\\\", subtitle = \\\"支持 URL / 文件 / 普通记录\\\", kind = \\\"url\\\", openTarget = \\\"https://example.com/tools?q=\\\" + System.Uri.EscapeDataString(q), keywords = new[] { \\\"工具\\\", q }, accentHex = \\\"#FF06B6D4\\\" }\\n        };\\n        return Task.FromResult(JsonSerializer.Serialize(items));\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 3：内联脚本扩展");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"inline-text-demo\",");
        builder.AppendLine("  \"name\": \"处理输入文本\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"脚本\",");
        builder.AppendLine("  \"description\": \"读取输入内容并返回结果。\",");
        builder.AppendLine("  \"keywords\": [\"脚本\", \"文本\", \"inline\"],");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [],");
        builder.AppendLine("  \"icon\": \"mdi:code-tags\",");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var input = context.InputText ?? string.Empty;\\n        return Task.FromResult(\\\"收到输入：\\\" + input);\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 3.1：带前缀输入的内联脚本扩展");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"text-length-counter\",");
        builder.AppendLine("  \"name\": \"文本长度统计\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"脚本\",");
        builder.AppendLine("  \"description\": \"在主界面输入前缀后，把后面的文本传给脚本并返回长度。\",");
        builder.AppendLine("  \"keywords\": [\"文本\", \"长度\", \"统计\", \"脚本\"],");
        builder.AppendLine("  \"queryPrefixes\": [\"统计\", \"count\"],");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [],");
        builder.AppendLine("  \"icon\": \"mdi:counter\",");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var input = context.InputText ?? string.Empty;\\n        return Task.FromResult(\\\"原文：\\\" + input + \\\"\\\\n长度：\\\" + input.Length);\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4：宿主自定义 XAML 视图扩展（hostedViewXaml）");
        builder.AppendLine("适用：双栏编辑器、便签工作区、预览器、轻量设置页。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"sticky-note-workbench\",");
        builder.AppendLine("  \"name\": \"简易便签\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"在宿主窗口中打开一个便签工作区。\",");
        builder.AppendLine("  \"keywords\": [\"便签\", \"记事本\", \"note\"],");
        builder.AppendLine("  \"icon\": \"mdi:note-text-outline\",");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"简易便签\",");
        builder.AppendLine("    \"description\": \"使用自定义 XAML 渲染便签窗口，并在本地 / 坚果云持久化。\",");
        builder.AppendLine("    \"window\": {");
        builder.AppendLine("      \"width\": 960,");
        builder.AppendLine("      \"height\": 720,");
        builder.AppendLine("      \"minWidth\": 760,");
        builder.AppendLine("      \"minHeight\": 520");
        builder.AppendLine("    },");
        builder.AppendLine("    \"state\": {");
        builder.AppendLine("      \"note\": \"\",");
        builder.AppendLine("      \"preview\": \"先在左侧输入内容，这里会显示便签结果。\",");
        builder.AppendLine("      \"saved\": true");
        builder.AppendLine("    },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\" oqh:HostedViewBridge.PreferredFocus=\\\"NoteBox\\\" oqh:HostedViewBridge.LoadedAction=\\\"loadStorage;path=note;key=note.txt;scope=both;defaultValue=\\\"><Grid.ColumnDefinitions><ColumnDefinition Width=\\\"*\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\\\"0\\\"><TextBlock Text=\\\"便签内容\\\" Foreground=\\\"White\\\" FontSize=\\\"14\\\" FontWeight=\\\"SemiBold\\\" Margin=\\\"0,0,0,10\\\"/><TextBox x:Name=\\\"NoteBox\\\" Text=\\\"{Binding [note], UpdateSourceTrigger=PropertyChanged}\\\" AcceptsReturn=\\\"True\\\" VerticalScrollBarVisibility=\\\"Auto\\\" TextWrapping=\\\"Wrap\\\" MinHeight=\\\"320\\\" Padding=\\\"12\\\"/><Button Content=\\\"保存便签\\\" Margin=\\\"0,12,0,0\\\" oqh:HostedViewBridge.Action=\\\"saveStorage;path=note;key=note.txt;scope=both;successMessage=便签已保存。|setState;path=preview;valueFrom=note\\\"/></StackPanel><Border Grid.Column=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"10\\\" Padding=\\\"12\\\"><TextBlock Text=\\\"{Binding [preview]}\\\" TextWrapping=\\\"Wrap\\\" Foreground=\\\"White\\\"/></Border></Grid>\"");
        builder.AppendLine("  },");
        builder.AppendLine("  \"startup\": {");
        builder.AppendLine("    \"mode\": \"on_app_launch\",");
        builder.AppendLine("    \"schedule\": \"0 9 * * *\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.1：设置页 / 表单型 hostedViewXaml");
        builder.AppendLine("适用：配置保存、账号信息、路径输入、开关集合。重点是表单布局和 loadStorage / saveStorage。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"workspace-settings-demo\",");
        builder.AppendLine("  \"name\": \"工作区设置\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"在宿主里编辑并保存设置项。\",");
        builder.AppendLine("  \"keywords\": [\"设置\", \"配置\", \"workspace\"],");
        builder.AppendLine("  \"icon\": \"mdi:cog-outline\",");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"工作区设置\",");
        builder.AppendLine("    \"window\": { \"width\": 900, \"height\": 680, \"minWidth\": 720, \"minHeight\": 520 },");
        builder.AppendLine("    \"state\": {");
        builder.AppendLine("      \"workspaceName\": \"默认工作区\",");
        builder.AppendLine("      \"defaultFolder\": \"F:\\\\Desktop\",");
        builder.AppendLine("      \"status\": \"修改后点击保存\""); 
        builder.AppendLine("    },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\" oqh:HostedViewBridge.LoadedAction=\\\"loadStorage;path=workspaceName;key=settings/workspace-name.txt;scope=local|loadStorage;path=defaultFolder;key=settings/default-folder.txt;scope=local\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><TextBlock Text=\\\"工作区设置\\\" FontSize=\\\"22\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><Border Grid.Row=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"18\\\"><StackPanel><TextBlock Text=\\\"工作区名称\\\" Foreground=\\\"White\\\" Margin=\\\"0,0,0,8\\\"/><TextBox Text=\\\"{Binding [workspaceName], UpdateSourceTrigger=PropertyChanged}\\\" Padding=\\\"10\\\"/><TextBlock Text=\\\"默认目录\\\" Foreground=\\\"White\\\" Margin=\\\"0,18,0,8\\\"/><TextBox Text=\\\"{Binding [defaultFolder], UpdateSourceTrigger=PropertyChanged}\\\" Padding=\\\"10\\\"/><TextBlock Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" Margin=\\\"0,18,0,0\\\"/></StackPanel></Border><StackPanel Grid.Row=\\\"3\\\" Orientation=\\\"Horizontal\\\" HorizontalAlignment=\\\"Right\\\" Margin=\\\"0,14,0,0\\\"><Button Content=\\\"保存\\\" oqh:HostedViewBridge.Action=\\\"saveStorage;path=workspaceName;key=settings/workspace-name.txt;scope=local|saveStorage;path=defaultFolder;key=settings/default-folder.txt;scope=local|setState;path=status;value=设置已保存\\\"/></StackPanel></Grid>\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.2：脚本工具台 hostedViewXaml");
        builder.AppendLine("适用：本机脚本执行、文本处理、网络请求入口、结果回显。重点是 textarea + runScript + output。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"script-console-demo\",");
        builder.AppendLine("  \"name\": \"脚本工具台\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"开发工具\",");
        builder.AppendLine("  \"description\": \"在宿主窗口中输入内容并执行 C# 脚本。\",");
        builder.AppendLine("  \"keywords\": [\"脚本\", \"控制台\", \"console\"],");
        builder.AppendLine("  \"icon\": \"mdi:console\",");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [\"network\"],");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"脚本工具台\",");
        builder.AppendLine("    \"window\": { \"width\": 1020, \"height\": 720, \"minWidth\": 760, \"minHeight\": 520 },");
        builder.AppendLine("    \"state\": { \"input\": \"\", \"output\": \"执行结果会显示在这里。\", \"status\": \"准备就绪\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><TextBlock Text=\\\"脚本工具台\\\" FontSize=\\\"22\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><Grid Grid.Row=\\\"2\\\"><Grid.ColumnDefinitions><ColumnDefinition Width=\\\"*\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\\\"0\\\"><TextBlock Text=\\\"输入\\\" Foreground=\\\"White\\\" Margin=\\\"0,0,0,8\\\"/><TextBox Text=\\\"{Binding [input], UpdateSourceTrigger=PropertyChanged}\\\" AcceptsReturn=\\\"True\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\" MinHeight=\\\"360\\\" Padding=\\\"12\\\"/></StackPanel><Border Grid.Column=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"12\\\"><TextBox Text=\\\"{Binding [output]}\\\" IsReadOnly=\\\"True\\\" AcceptsReturn=\\\"True\\\" Background=\\\"Transparent\\\" BorderThickness=\\\"0\\\" Foreground=\\\"#FFE5E7EB\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\"/></Border></Grid><DockPanel Grid.Row=\\\"3\\\" Margin=\\\"0,14,0,0\\\"><TextBlock Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" VerticalAlignment=\\\"Center\\\"/><Button Content=\\\"执行脚本\\\" DockPanel.Dock=\\\"Right\\\" oqh:HostedViewBridge.Action=\\\"runScript;inputFrom=input;outputTo=output;successMessage=脚本执行完成\\\"/></DockPanel></Grid>\"");
        builder.AppendLine("  },");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var input = context.InputText ?? string.Empty;\\n        return Task.FromResult(\\\"输入长度：\\\" + input.Length + \\\"\\\\n\\\\n\\\" + input.ToUpperInvariant());\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.3：仪表盘 / 状态面板 hostedViewXaml");
        builder.AppendLine("适用：展示关键数据、状态摘要、日志片段、快速动作。重点是多卡片布局，而不是双栏。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"ops-dashboard-demo\",");
        builder.AppendLine("  \"name\": \"状态仪表盘\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"在宿主里展示关键状态卡片和快速操作。\",");
        builder.AppendLine("  \"keywords\": [\"仪表盘\", \"状态\", \"dashboard\"],");
        builder.AppendLine("  \"icon\": \"mdi:view-dashboard-outline\",");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"状态仪表盘\",");
        builder.AppendLine("    \"window\": { \"width\": 1100, \"height\": 760, \"minWidth\": 820, \"minHeight\": 560 },");
        builder.AppendLine("    \"state\": { \"summary\": \"今日任务 5 项\", \"health\": \"运行正常\", \"recentLog\": \"暂无新日志\", \"status\": \"准备就绪\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"16\\\"/><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"16\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><StackPanel><TextBlock Text=\\\"状态仪表盘\\\" FontSize=\\\"24\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><TextBlock Text=\\\"用多卡片布局展示关键指标和最近状态\\\" Foreground=\\\"#FF9CA3AF\\\" Margin=\\\"0,6,0,0\\\"/></StackPanel><Grid Grid.Row=\\\"2\\\"><Grid.ColumnDefinitions><ColumnDefinition Width=\\\"*\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/></Grid.ColumnDefinitions><Border Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"14\\\" Padding=\\\"16\\\"><StackPanel><TextBlock Text=\\\"今日摘要\\\" Foreground=\\\"#FF9CA3AF\\\"/><TextBlock Text=\\\"{Binding [summary]}\\\" Foreground=\\\"White\\\" FontSize=\\\"20\\\" FontWeight=\\\"SemiBold\\\" Margin=\\\"0,10,0,0\\\"/></StackPanel></Border><Border Grid.Column=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"14\\\" Padding=\\\"16\\\"><StackPanel><TextBlock Text=\\\"运行状态\\\" Foreground=\\\"#FF9CA3AF\\\"/><TextBlock Text=\\\"{Binding [health]}\\\" Foreground=\\\"#FF34D399\\\" FontSize=\\\"20\\\" FontWeight=\\\"SemiBold\\\" Margin=\\\"0,10,0,0\\\"/></StackPanel></Border><Border Grid.Column=\\\"4\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"14\\\" Padding=\\\"16\\\"><StackPanel><TextBlock Text=\\\"快速动作\\\" Foreground=\\\"#FF9CA3AF\\\"/><Button Content=\\\"刷新摘要\\\" Margin=\\\"0,12,0,0\\\" oqh:HostedViewBridge.Action=\\\"setState;path=status;value=已刷新摘要\\\"/></StackPanel></Border></Grid><Border Grid.Row=\\\"4\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"14\\\" Padding=\\\"16\\\"><StackPanel><TextBlock Text=\\\"最近日志\\\" Foreground=\\\"White\\\" FontWeight=\\\"SemiBold\\\" Margin=\\\"0,0,0,10\\\"/><TextBox Text=\\\"{Binding [recentLog]}\\\" IsReadOnly=\\\"True\\\" AcceptsReturn=\\\"True\\\" Background=\\\"Transparent\\\" BorderThickness=\\\"0\\\" Foreground=\\\"#FFE5E7EB\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\" MinHeight=\\\"220\\\"/></StackPanel></Border><DockPanel Grid.Row=\\\"5\\\" Margin=\\\"0,14,0,0\\\"><TextBlock Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" VerticalAlignment=\\\"Center\\\"/></DockPanel></Grid>\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.4：路径与文件工具 hostedViewXaml");
        builder.AppendLine("适用：文件整理、批量重命名入口、命令封装。重点是路径输入、脚本执行、结果日志。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"path-tool-demo\",");
        builder.AppendLine("  \"name\": \"路径工具台\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"开发工具\",");
        builder.AppendLine("  \"description\": \"输入目录和规则后执行本地处理脚本。\",");
        builder.AppendLine("  \"keywords\": [\"路径\", \"文件\", \"folder\"],");
        builder.AppendLine("  \"icon\": \"mdi:folder-cog-outline\",");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [\"storage\"],");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"路径工具台\",");
        builder.AppendLine("    \"window\": { \"width\": 980, \"height\": 720, \"minWidth\": 760, \"minHeight\": 520 },");
        builder.AppendLine("    \"state\": { \"path\": \"F:\\\\Desktop\", \"rule\": \"*.txt\", \"result\": \"等待执行\", \"status\": \"准备就绪\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><TextBlock Text=\\\"路径工具台\\\" FontSize=\\\"22\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><Border Grid.Row=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"18\\\"><StackPanel><TextBlock Text=\\\"目标目录\\\" Foreground=\\\"White\\\" Margin=\\\"0,0,0,8\\\"/><TextBox Text=\\\"{Binding [path], UpdateSourceTrigger=PropertyChanged}\\\" Padding=\\\"10\\\"/><TextBlock Text=\\\"匹配规则\\\" Foreground=\\\"White\\\" Margin=\\\"0,16,0,8\\\"/><TextBox Text=\\\"{Binding [rule], UpdateSourceTrigger=PropertyChanged}\\\" Padding=\\\"10\\\"/></StackPanel></Border><Border Grid.Row=\\\"4\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"12\\\"><TextBox Text=\\\"{Binding [result]}\\\" IsReadOnly=\\\"True\\\" AcceptsReturn=\\\"True\\\" Background=\\\"Transparent\\\" BorderThickness=\\\"0\\\" Foreground=\\\"#FFE5E7EB\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\"/></Border><DockPanel Grid.Row=\\\"5\\\" Margin=\\\"0,14,0,0\\\"><TextBlock Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" VerticalAlignment=\\\"Center\\\"/><Button Content=\\\"执行检查\\\" DockPanel.Dock=\\\"Right\\\" oqh:HostedViewBridge.Action=\\\"runScript;inputFrom=path;outputTo=result;successMessage=检查完成\\\"/></DockPanel></Grid>\"");
        builder.AppendLine("  },");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System.IO;\\nusing System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var path = context.InputText ?? string.Empty;\\n        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))\\n        {\\n            return Task.FromResult(\\\"目录不存在：\\\" + path);\\n        }\\n\\n        var files = Directory.GetFiles(path);\\n        return Task.FromResult(\\\"目录：\\\" + path + \\\"\\\\n文件数：\\\" + files.Length);\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.5：搜索与预览工作区 hostedViewXaml");
        builder.AppendLine("适用：左侧查询、右侧结果预览、历史记录。重点是输入区、结果区、状态区。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"search-preview-demo\",");
        builder.AppendLine("  \"name\": \"搜索预览台\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"在宿主中输入查询并展示结果预览。\",");
        builder.AppendLine("  \"keywords\": [\"搜索\", \"预览\", \"preview\"],");
        builder.AppendLine("  \"icon\": \"mdi:file-search-outline\",");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [\"network\"],");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"搜索预览台\",");
        builder.AppendLine("    \"window\": { \"width\": 1040, \"height\": 730, \"minWidth\": 780, \"minHeight\": 540 },");
        builder.AppendLine("    \"state\": { \"query\": \"\", \"preview\": \"输入关键词后点击搜索\", \"status\": \"等待查询\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><DockPanel><TextBlock Text=\\\"搜索预览台\\\" FontSize=\\\"22\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><Button Content=\\\"搜索\\\" DockPanel.Dock=\\\"Right\\\" oqh:HostedViewBridge.Action=\\\"runScript;inputFrom=query;outputTo=preview;successMessage=搜索完成\\\"/></DockPanel><Grid Grid.Row=\\\"2\\\"><Grid.ColumnDefinitions><ColumnDefinition Width=\\\"340\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\\\"0\\\"><TextBlock Text=\\\"关键词\\\" Foreground=\\\"White\\\" Margin=\\\"0,0,0,8\\\"/><TextBox Text=\\\"{Binding [query], UpdateSourceTrigger=PropertyChanged}\\\" Padding=\\\"10\\\"/><TextBlock Text=\\\"说明\\\" Foreground=\\\"White\\\" Margin=\\\"0,18,0,8\\\"/><Border Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"12\\\"><TextBlock Text=\\\"可用于搜索文件、接口说明、知识片段等。\\\" Foreground=\\\"#FFCBD5E1\\\" TextWrapping=\\\"Wrap\\\"/></Border></StackPanel><Border Grid.Column=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"12\\\"><TextBox Text=\\\"{Binding [preview]}\\\" IsReadOnly=\\\"True\\\" AcceptsReturn=\\\"True\\\" Background=\\\"Transparent\\\" BorderThickness=\\\"0\\\" Foreground=\\\"#FFE5E7EB\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\"/></Border></Grid><TextBlock Grid.Row=\\\"3\\\" Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" Margin=\\\"0,14,0,0\\\"/></Grid>\"");
        builder.AppendLine("  },");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System.Threading.Tasks;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var q = context.InputText ?? string.Empty;\\n        return Task.FromResult(string.IsNullOrWhiteSpace(q) ? \\\"请输入关键词。\\\" : \\\"查询：\\\" + q + \\\"\\\\n\\\\n这里是搜索结果预览占位内容。\\\");\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.6：多分区编辑器 hostedViewXaml");
        builder.AppendLine("适用：文案编辑、Prompt 编辑、模板拼装。重点是头部工具栏 + 主编辑区 + 底部状态栏。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"prompt-editor-demo\",");
        builder.AppendLine("  \"name\": \"多分区编辑器\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"创作工具\",");
        builder.AppendLine("  \"description\": \"在宿主里编辑主内容、补充说明和结果草稿。\",");
        builder.AppendLine("  \"keywords\": [\"编辑器\", \"prompt\", \"writer\"],");
        builder.AppendLine("  \"icon\": \"mdi:text-box-edit-outline\",");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"多分区编辑器\",");
        builder.AppendLine("    \"window\": { \"width\": 1120, \"height\": 780, \"minWidth\": 860, \"minHeight\": 580 },");
        builder.AppendLine("    \"state\": { \"title\": \"新草稿\", \"main\": \"\", \"notes\": \"\", \"status\": \"准备就绪\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><DockPanel><TextBlock Text=\\\"多分区编辑器\\\" FontSize=\\\"22\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><Button Content=\\\"同步预览\\\" DockPanel.Dock=\\\"Right\\\" oqh:HostedViewBridge.Action=\\\"setState;path=status;value=已同步当前内容\\\"/></DockPanel><Grid Grid.Row=\\\"2\\\"><Grid.ColumnDefinitions><ColumnDefinition Width=\\\"2*\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\\\"0\\\"><TextBlock Text=\\\"标题\\\" Foreground=\\\"White\\\" Margin=\\\"0,0,0,8\\\"/><TextBox Text=\\\"{Binding [title], UpdateSourceTrigger=PropertyChanged}\\\" Padding=\\\"10\\\"/><TextBlock Text=\\\"正文\\\" Foreground=\\\"White\\\" Margin=\\\"0,16,0,8\\\"/><TextBox Text=\\\"{Binding [main], UpdateSourceTrigger=PropertyChanged}\\\" AcceptsReturn=\\\"True\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\" MinHeight=\\\"360\\\" Padding=\\\"12\\\"/></StackPanel><StackPanel Grid.Column=\\\"2\\\"><TextBlock Text=\\\"补充说明\\\" Foreground=\\\"White\\\" Margin=\\\"0,0,0,8\\\"/><TextBox Text=\\\"{Binding [notes], UpdateSourceTrigger=PropertyChanged}\\\" AcceptsReturn=\\\"True\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\" MinHeight=\\\"220\\\" Padding=\\\"12\\\"/><Border Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"12\\\" Margin=\\\"0,16,0,0\\\"><TextBlock Text=\\\"这里可以放预览、说明、模板提示等辅助内容。\\\" Foreground=\\\"#FFCBD5E1\\\" TextWrapping=\\\"Wrap\\\"/></Border></StackPanel></Grid><TextBlock Grid.Row=\\\"3\\\" Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" Margin=\\\"0,14,0,0\\\"/></Grid>\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.7：日志与输出查看器 hostedViewXaml");
        builder.AppendLine("适用：脚本日志、任务记录、审计面板。重点是只读输出区和清空/刷新动作。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"log-viewer-demo\",");
        builder.AppendLine("  \"name\": \"日志查看器\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"开发工具\",");
        builder.AppendLine("  \"description\": \"在宿主里查看、清空和刷新日志内容。\",");
        builder.AppendLine("  \"keywords\": [\"日志\", \"log\", \"viewer\"],");
        builder.AppendLine("  \"icon\": \"mdi:text-box-search-outline\",");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"日志查看器\",");
        builder.AppendLine("    \"window\": { \"width\": 980, \"height\": 700, \"minWidth\": 760, \"minHeight\": 520 },");
        builder.AppendLine("    \"state\": { \"logText\": \"暂无日志内容\", \"status\": \"准备就绪\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"12\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><DockPanel><TextBlock Text=\\\"日志查看器\\\" FontSize=\\\"22\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><StackPanel DockPanel.Dock=\\\"Right\\\" Orientation=\\\"Horizontal\\\"><Button Content=\\\"清空\\\" Margin=\\\"0,0,10,0\\\" oqh:HostedViewBridge.Action=\\\"setState;path=logText;value=日志已清空|setState;path=status;value=已清空\\\"/><Button Content=\\\"刷新\\\" oqh:HostedViewBridge.Action=\\\"setState;path=status;value=已刷新\\\"/></StackPanel></DockPanel><Border Grid.Row=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"12\\\" Padding=\\\"12\\\"><TextBox Text=\\\"{Binding [logText]}\\\" IsReadOnly=\\\"True\\\" AcceptsReturn=\\\"True\\\" Background=\\\"Transparent\\\" BorderThickness=\\\"0\\\" Foreground=\\\"#FFE5E7EB\\\" TextWrapping=\\\"Wrap\\\" VerticalScrollBarVisibility=\\\"Auto\\\"/></Border><TextBlock Grid.Row=\\\"3\\\" Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" Margin=\\\"0,14,0,0\\\"/></Grid>\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4.8：欢迎页 / 向导型 hostedViewXaml");
        builder.AppendLine("适用：首次启动引导、模板选择、配置引导。重点是说明区、步骤按钮和持久化。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"welcome-guide-demo\",");
        builder.AppendLine("  \"name\": \"欢迎向导\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"首次启动时展示说明、步骤和完成状态。\",");
        builder.AppendLine("  \"keywords\": [\"欢迎\", \"向导\", \"guide\"],");
        builder.AppendLine("  \"icon\": \"mdi:compass-outline\",");
        builder.AppendLine("  \"hostedViewXaml\": {");
        builder.AppendLine("    \"type\": \"xaml\",");
        builder.AppendLine("    \"title\": \"欢迎向导\",");
        builder.AppendLine("    \"window\": { \"width\": 920, \"height\": 680, \"minWidth\": 720, \"minHeight\": 500 },");
        builder.AppendLine("    \"state\": { \"step\": \"步骤 1 / 3\", \"status\": \"欢迎使用燕子工作区\", \"summary\": \"完成快捷键设置、创建第一个扩展、尝试鼠标面板。\" },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:Yanzi\\\"><Grid.RowDefinitions><RowDefinition Height=\\\"Auto\\\"/><RowDefinition Height=\\\"16\\\"/><RowDefinition Height=\\\"*\\\"/><RowDefinition Height=\\\"Auto\\\"/></Grid.RowDefinitions><StackPanel><TextBlock Text=\\\"欢迎向导\\\" FontSize=\\\"26\\\" FontWeight=\\\"SemiBold\\\" Foreground=\\\"White\\\"/><TextBlock Text=\\\"{Binding [step]}\\\" Foreground=\\\"#FF60A5FA\\\" Margin=\\\"0,8,0,0\\\"/></StackPanel><Border Grid.Row=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"16\\\" Padding=\\\"20\\\"><StackPanel><TextBlock Text=\\\"开始之前\\\" Foreground=\\\"White\\\" FontSize=\\\"18\\\" FontWeight=\\\"SemiBold\\\"/><TextBlock Text=\\\"{Binding [summary]}\\\" Foreground=\\\"#FFCBD5E1\\\" TextWrapping=\\\"Wrap\\\" Margin=\\\"0,12,0,0\\\"/><Border Background=\\\"#FF111827\\\" CornerRadius=\\\"12\\\" Padding=\\\"14\\\" Margin=\\\"0,18,0,0\\\"><TextBlock Text=\\\"建议顺序：设置呼出方式 -> 导入模板 -> 测试第一个扩展。\\\" Foreground=\\\"#FFE5E7EB\\\" TextWrapping=\\\"Wrap\\\"/></Border></StackPanel></Border><DockPanel Grid.Row=\\\"3\\\" Margin=\\\"0,14,0,0\\\"><TextBlock Text=\\\"{Binding [status]}\\\" Foreground=\\\"#FF9CA3AF\\\" VerticalAlignment=\\\"Center\\\"/><StackPanel DockPanel.Dock=\\\"Right\\\" Orientation=\\\"Horizontal\\\"><Button Content=\\\"下一步\\\" Margin=\\\"0,0,10,0\\\" oqh:HostedViewBridge.Action=\\\"setState;path=step;value=步骤 2 / 3|setState;path=status;value=继续查看下一步\\\"/><Button Content=\\\"完成\\\" oqh:HostedViewBridge.Action=\\\"setState;path=status;value=向导已完成|close\\\"/></StackPanel></DockPanel></Grid>\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("模板 5：原生窗口扩展（uiMode = native-window）");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"native-window-demo\",");
        builder.AppendLine("  \"name\": \"原生窗口示例\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"在独立 WPF 窗口中显示输入内容。\",");
        builder.AppendLine("  \"keywords\": [\"native\", \"window\", \"wpf\"],");
        builder.AppendLine("  \"icon\": \"mdi:application-outline\",");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"uiMode\": \"native-window\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [],");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System;\\nusing System.Threading;\\nusing System.Threading.Tasks;\\nusing System.Windows;\\nusing System.Windows.Controls;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var input = context.InputText ?? string.Empty;\\n        Exception? error = null;\\n\\n        var thread = new Thread(() =>\\n        {\\n            try\\n            {\\n                var textBlock = new TextBlock\\n                {\\n                    Text = string.IsNullOrWhiteSpace(input) ? \\\"这是一个独立原生窗口示例。\\\" : \\\"输入内容：\\\" + input,\\n                    Margin = new Thickness(24),\\n                    TextWrapping = TextWrapping.Wrap,\\n                    FontSize = 16\\n                };\\n\\n                var closeButton = new Button\\n                {\\n                    Content = \\\"关闭\\\",\\n                    Width = 88,\\n                    Height = 32,\\n                    Margin = new Thickness(24, 0, 24, 24),\\n                    HorizontalAlignment = HorizontalAlignment.Right\\n                };\\n\\n                var panel = new DockPanel();\\n                DockPanel.SetDock(closeButton, Dock.Bottom);\\n                panel.Children.Add(closeButton);\\n                panel.Children.Add(textBlock);\\n\\n                var window = new Window\\n                {\\n                    Title = \\\"原生窗口示例\\\",\\n                    Width = 520,\\n                    Height = 320,\\n                    MinWidth = 420,\\n                    MinHeight = 240,\\n                    Content = panel,\\n                    WindowStartupLocation = WindowStartupLocation.CenterScreen\\n                };\\n\\n                closeButton.Click += (_, _) => window.Close();\\n                window.ShowDialog();\\n            }\\n            catch (Exception ex)\\n            {\\n                error = ex;\\n            }\\n        });\\n\\n        thread.SetApartmentState(ApartmentState.STA);\\n        thread.IsBackground = false;\\n        thread.Start();\\n        thread.Join();\\n\\n        if (error != null)\\n        {\\n            throw error;\\n        }\\n\\n        return Task.FromResult(\\\"窗口已关闭\\\");\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 5.1：原生笔记窗口（带存储）");
        builder.AppendLine("注意：这类扩展必须直接沿用下面的 STA 线程结构和 storage 调用方式；不要改成在 RunAsync 当前线程里直接 new Window，也不要给 ReadTextAsync 传 defaultValue。");
        builder.AppendLine("{");
        builder.AppendLine("  \"id\": \"note-native-app\",");
        builder.AppendLine("  \"name\": \"独立笔记\",");
        builder.AppendLine("  \"version\": \"0.1.0\",");
        builder.AppendLine("  \"category\": \"效率工具\",");
        builder.AppendLine("  \"description\": \"在独立窗口中创建和保存笔记。\",");
        builder.AppendLine("  \"keywords\": [\"笔记\", \"便签\", \"native\"],");
        builder.AppendLine("  \"icon\": \"mdi:notebook-edit-outline\",");
        builder.AppendLine("  \"runtime\": \"csharp\",");
        builder.AppendLine("  \"uiMode\": \"native-window\",");
        builder.AppendLine("  \"entryMode\": \"inline\",");
        builder.AppendLine("  \"permissions\": [\"storage\"],");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using System;\\nusing System.Threading;\\nusing System.Threading.Tasks;\\nusing System.Windows;\\nusing System.Windows.Controls;\\nusing System.Windows.Media;\\npublic static class YanziAction\\n{\\n    public static async Task<string> RunAsync(YanziActionContext context)\\n    {\\n        var storage = context.Storage;\\n        string noteContent;\\n        try\\n        {\\n            noteContent = await storage.ReadTextAsync(\\\"note.txt\\\", scope: \\\"local\\\") ?? string.Empty;\\n        }\\n        catch\\n        {\\n            noteContent = string.Empty;\\n        }\\n\\n        Exception? error = null;\\n        var thread = new Thread(() =>\\n        {\\n            try\\n            {\\n                var window = new Window\\n                {\\n                    Title = \\\"独立笔记\\\",\\n                    Width = 600,\\n                    Height = 500,\\n                    MinWidth = 400,\\n                    MinHeight = 300,\\n                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),\\n                    WindowStartupLocation = WindowStartupLocation.CenterScreen\\n                };\\n\\n                var textBox = new TextBox\\n                {\\n                    Text = noteContent,\\n                    AcceptsReturn = true,\\n                    TextWrapping = TextWrapping.Wrap,\\n                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,\\n                    Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),\\n                    Foreground = Brushes.White,\\n                    FontSize = 14,\\n                    Padding = new Thickness(12),\\n                    Margin = new Thickness(10),\\n                    MinHeight = 360\\n                };\\n\\n                var saveButton = new Button\\n                {\\n                    Content = \\\"保存笔记\\\",\\n                    Margin = new Thickness(10, 0, 10, 10),\\n                    Height = 32,\\n                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),\\n                    Foreground = Brushes.White,\\n                    BorderThickness = new Thickness(0)\\n                };\\n\\n                var statusText = new TextBlock\\n                {\\n                    Text = \\\"就绪\\\",\\n                    Margin = new Thickness(10),\\n                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),\\n                    FontSize = 12\\n                };\\n\\n                saveButton.Click += async (_, _) =>\\n                {\\n                    await storage.WriteTextAsync(\\\"note.txt\\\", textBox.Text, scope: \\\"both\\\");\\n                    statusText.Text = \\\"已保存到本地，云端同步在后台进行\\\";\\n                };\\n\\n                var panel = new StackPanel();\\n                panel.Children.Add(textBox);\\n                panel.Children.Add(saveButton);\\n                panel.Children.Add(statusText);\\n                window.Content = panel;\\n                window.ShowDialog();\\n            }\\n            catch (Exception ex)\\n            {\\n                error = ex;\\n            }\\n        });\\n\\n        thread.SetApartmentState(ApartmentState.STA);\\n        thread.IsBackground = false;\\n        thread.Start();\\n        thread.Join();\\n\\n        if (error != null)\\n        {\\n            throw error;\\n        }\\n\\n        return \\\"笔记窗口已关闭\\\";\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("五、最终要求");
        builder.AppendLine("请结合我的需求，只返回一个包含最终 JSON 的 ```json 代码块，不要返回多个方案，不要附加说明。");
        builder.AppendLine("如果需求里提到便签、面板、编辑器、工作区、内置界面，请优先使用 hostedViewXaml；如果只是简单表单，再考虑 hostedViewV2。");
        builder.AppendLine("如果需求里明确是独立弹窗小工具或原生小应用，并且脚本需要直接 new Window / TextBox / Button，就必须输出 native-window，不要改成 hostedViewXaml。");
        return builder.ToString();
    }

    private static string CreateOpenTargetTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"open-target-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "打开记事本",
            Version = "0.1.0",
            Category = "扩展",
            Description = "点击后打开记事本。",
            Keywords = ["打开", "记事本", "notepad"],
            OpenTarget = "notepad.exe",
            Icon = "mdi:notebook-outline",
            AccentHex = "#FF3B82F6"
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateDesktopTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"open-desktop-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "打开桌面",
            Version = "0.1.0",
            Category = "扩展",
            Description = "点击后打开当前用户桌面目录。",
            Keywords = ["桌面", "desktop", "打开"],
            OpenTarget = "shell:Desktop",
            Icon = "mdi:monitor-dashboard",
            AccentHex = "#FF06B6D4"
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateSearchTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"search-template-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "网页搜索",
            Version = "0.1.0",
            Category = "网页搜索",
            Description = "用指定网站搜索关键词。",
            Keywords = ["搜索", "网页"],
            QueryPrefixes = ["搜索", "web"],
            QueryTargetTemplate = "https://www.baidu.com/s?wd={query}",
            Icon = "https://www.baidu.com/favicon.ico",
            AccentHex = "#FF10B981"
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateInlineScriptTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"inline-script-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "内联脚本示例",
            Version = "0.1.0",
            Category = "脚本",
            Description = "读取输入内容并返回结果。",
            Keywords = ["脚本", "inline"],
            Runtime = "csharp",
            EntryMode = "inline",
            Permissions = ["clipboard"],
            Icon = "mdi:code-tags",
            AccentHex = "#FF3B82F6",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source = "public static class YanziAction\n{\n    public static Task<string> RunAsync(YanziActionContext context)\n    {\n        return Task.FromResult(\"收到输入：\" + context.InputText);\n    }\n}"
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateForegroundWindowTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"foreground-window-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "前台窗口信息",
            Version = "0.1.0",
            Category = "脚本",
            Description = "获取当前前台窗口标题和进程信息。",
            Keywords = ["window", "foreground", "前台窗口", "powershell", "script"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["window.foreground"],
            Icon = "mdi:window",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Window {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
"@

$handle = [Win32Window]::GetForegroundWindow()
$titleBuilder = New-Object System.Text.StringBuilder 512
[void][Win32Window]::GetWindowText($handle, $titleBuilder, $titleBuilder.Capacity)
[uint32]$processId = 0
[void][Win32Window]::GetWindowThreadProcessId($handle, [ref]$processId)
$process = Get-Process -Id $processId -ErrorAction SilentlyContinue

Write-Output ("窗口标题: " + $titleBuilder.ToString().Trim())
Write-Output ("进程名: " + $(if ($process) { $process.ProcessName } else { "unknown" }))
Write-Output ("进程 ID: " + $processId)
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateClipboardTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"clipboard-paste-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "模拟粘贴",
            Version = "0.1.0",
            Category = "脚本",
            Description = "向当前窗口发送 Ctrl+V。",
            Keywords = ["clipboard", "剪贴板", "粘贴", "powershell", "script"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.write"],
            Icon = "mdi:clipboard",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait("^v")
Write-Output "已发送 Ctrl+V。"
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateSelectionContextTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"selection-context-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "选中内容示例",
            Version = "0.1.0",
            Category = "脚本",
            Description = "优先读取宿主传入的 InputText，没有时回退到剪贴板文本或文件列表。",
            Keywords = ["selection", "context", "clipboard", "选中", "右键", "面板"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.read"],
            Icon = "app:selection",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms

$source = "HostInput"
$normalized = $InputText
$fileList = @()

if ([string]::IsNullOrWhiteSpace($normalized)) {
    if ([System.Windows.Forms.Clipboard]::ContainsFileDropList()) {
        $fileList = [System.Windows.Forms.Clipboard]::GetFileDropList()
        $normalized = ($fileList | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        $source = "ClipboardFileDropList"
    }
    elseif ([System.Windows.Forms.Clipboard]::ContainsText()) {
        $normalized = [System.Windows.Forms.Clipboard]::GetText()
        $source = "ClipboardText"
    }
}

if ([string]::IsNullOrWhiteSpace($normalized)) {
    Write-Output "没有检测到宿主输入，也没有检测到剪贴板里的文本/文件。"
    exit 0
}

Write-Output "来源: $source"
Write-Output ""

if ($fileList.Count -gt 0) {
    Write-Output "识别为文件选择，共 $($fileList.Count) 个："
    Write-Output ""
    foreach ($file in $fileList) {
        Write-Output $file
    }
    exit 0
}

Write-Output "识别为文本输入："
Write-Output ""
Write-Output $normalized.Trim()
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateCSharpContextTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"csharp-context-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "C# 动作示例",
            Version = "0.1.0",
            Category = "C#",
            Description = "使用 C# 打开一个原生示例窗口。",
            Keywords = ["csharp", "dotnet", "context", "示例"],
            Runtime = "csharp",
            UiMode = "native-window",
            EntryMode = "inline",
            Permissions = ["context.read"],
            Icon = "mdi:code",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source = CreateCSharpWindowScript()
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateNativeWindowTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"native-window-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "原生窗口示例",
            Version = "0.1.0",
            Category = "效率工具",
            Description = "在独立原生窗口中显示输入内容。",
            Keywords = ["native", "window", "wpf", "窗口"],
            Runtime = "csharp",
            UiMode = "native-window",
            EntryMode = "inline",
            Permissions = [],
            Icon = "mdi:application-outline",
            QueryPrefixes = ["窗口", "window"],
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = context.InputText ?? string.Empty;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var textBlock = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(input) ? "这是一个独立原生窗口示例。" : "输入内容：" + input,
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 16
                };

                var closeButton = new Button
                {
                    Content = "关闭",
                    Width = 88,
                    Height = 32,
                    Margin = new Thickness(24, 0, 24, 24),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var panel = new DockPanel();
                DockPanel.SetDock(closeButton, Dock.Bottom);
                panel.Children.Add(closeButton);
                panel.Children.Add(textBlock);

                var window = new Window
                {
                    Title = "原生窗口示例",
                    Width = 520,
                    Height = 320,
                    MinWidth = 420,
                    MinHeight = 240,
                    Content = panel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                closeButton.Click += (_, _) => window.Close();
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw error;
        }

        return Task.FromResult("窗口已关闭");
    }
}
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateNativeNoteTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"native-note-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "独立笔记",
            Version = "0.1.0",
            Category = "效率工具",
            Description = "在独立窗口中创建和保存笔记。",
            Keywords = ["笔记", "便签", "native", "编辑器"],
            Runtime = "csharp",
            UiMode = "native-window",
            EntryMode = "inline",
            Permissions = ["storage"],
            Icon = "mdi:notebook-edit-outline",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var storage = context.Storage;
        string noteContent;
        try
        {
            noteContent = await storage.ReadTextAsync("note.txt", scope: "local") ?? string.Empty;
        }
        catch
        {
            noteContent = string.Empty;
        }

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    Title = "独立笔记",
                    Width = 600,
                    Height = 500,
                    MinWidth = 400,
                    MinHeight = 300,
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var textBox = new TextBox
                {
                    Text = noteContent,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    Padding = new Thickness(12),
                    Margin = new Thickness(10),
                    MinHeight = 360
                };

                var saveButton = new Button
                {
                    Content = "保存笔记",
                    Margin = new Thickness(10, 0, 10, 10),
                    Height = 32,
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0)
                };

                var statusText = new TextBlock
                {
                    Text = "就绪",
                    Margin = new Thickness(10),
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 12
                };

                saveButton.Click += async (_, _) =>
                {
                    await storage.WriteTextAsync("note.txt", textBox.Text, scope: "both");
                    statusText.Text = "已保存到本地，云端同步在后台进行";
                };

                var panel = new StackPanel();
                panel.Children.Add(textBox);
                panel.Children.Add(saveButton);
                panel.Children.Add(statusText);
                window.Content = panel;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw error;
        }

        return "笔记窗口已关闭";
    }
}
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateTimestampTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"inline-timestamp-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "内联时间戳",
            Version = "0.1.0",
            Category = "脚本",
            Description = "返回当前时间和输入内容。",
            Keywords = ["time", "timestamp", "时间戳", "inline", "powershell"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.read"],
            Icon = "mdi:clock",
            HostedView = new LocalExtensionHostedViewManifest
            {
                Type = "split-workbench",
                Title = "内联时间戳",
                Description = "左侧输入任意文本，右侧显示时间戳和输入内容。",
                InputLabel = "输入",
                InputPlaceholder = "输入任意内容...",
                OutputLabel = "结果",
                ActionButtonText = "执行脚本",
                ActionType = "script",
                EmptyState = "脚本输出会显示在这里。"
            },
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$now = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
if ([string]::IsNullOrWhiteSpace($InputText)) {
    Write-Output "当前时间: $now"
} else {
    Write-Output "当前时间: $now"
    Write-Output "输入内容: $InputText"
}
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static string CreateTranslateWorkbenchTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"translate-workbench-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "双栏翻译",
            Version = "0.1.0",
            Category = "扩展",
            Description = "在当前窗口中打开双栏翻译工作区。",
            Keywords = ["translate", "translator", "翻译", "双栏", "script"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard", "network"],
            Icon = "mdi:translate",
            HostedView = new LocalExtensionHostedViewManifest
            {
                Type = "split-workbench",
                Title = "双栏翻译",
                Description = "左侧输入待翻译内容，右侧显示脚本输出。",
                InputLabel = "原文",
                InputPlaceholder = "输入要翻译的中文、英文或任意文本...",
                OutputLabel = "译文",
                ActionButtonText = "开始翻译",
                ActionType = "script",
                EmptyState = "这里会显示脚本的执行结果。"
            },
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($InputText)) {
    Write-Output "请输入要翻译的文本。"
    exit 0
}

$trimmed = $InputText.Trim()
Write-Output "译文：$trimmed"
Write-Output ""
Write-Output "说明：这是模板输出，后续可以替换为真实翻译 API 调用。"
"""
            }
        };

        return JsonSerializer.Serialize(manifest, CreateJsonOptions());
    }

    private static void SetStepVisual(Border dot, TextBlock dotText, TextBlock label, StepVisualState state, string fallbackNumber)
    {
        switch (state)
        {
            case StepVisualState.Inactive:
                dot.BorderBrush = BorderStrongBrush;
                dot.Background = MediaBrushes.Transparent;
                dotText.Text = fallbackNumber;
                dotText.Foreground = Text3Brush;
                label.Foreground = Text3Brush;
                break;
            case StepVisualState.Active:
                dot.BorderBrush = AccentBrush;
                dot.Background = AccentGlowBrush;
                dotText.Text = fallbackNumber;
                dotText.Foreground = AccentBrush;
                label.Foreground = AccentBrush;
                break;
            case StepVisualState.Done:
                dot.BorderBrush = GreenBrush;
                dot.Background = CreateBrush("#1A34D399");
                dotText.Text = "✓";
                dotText.Foreground = GreenBrush;
                label.Foreground = GreenBrush;
                break;
        }
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string[]? SplitCsv(string? value)
    {
        var items = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return items.Length == 0 ? null : items;
    }

    private static string? NormalizeAccentHexOrNull(string? accentHex)
    {
        return string.IsNullOrWhiteSpace(accentHex)
            ? null
            : NormalizeAccentHexOrDefault(accentHex);
    }

    private static string NormalizeAccentHexOrDefault(string? accentHex)
    {
        var value = accentHex?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#FF3B82F6";
        }

        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (value.Length == 7)
        {
            value = "#FF" + value[1..];
        }

        if (value.Length != 9 ||
            !value[1..].All(static ch => Uri.IsHexDigit(ch)))
        {
            return "#FF3B82F6";
        }

        return value.ToUpperInvariant();
    }

    private static string CompactError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "未知错误";
        }

        var compact = message.Replace(Environment.NewLine, " ");
        return compact.Length <= 36 ? compact : compact[..36];
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    private static string FormatJsonText(string rawText)
    {
        var normalizedJson = ExtractJsonPayload(rawText);
        var documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        using var document = JsonDocument.Parse(normalizedJson, documentOptions);
        var root = document.RootElement.Clone();
        return JsonSerializer.Serialize(root, CreateJsonOptions());
    }

    private static LocalExtensionManifest ParseManifestFromJson(string json, string source)
    {
        var normalizedJson = ExtractJsonPayload(json);

        try
        {
            return JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
                ?? throw new InvalidOperationException("JSON 解析失败。");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"AddJson parse failed: source={source}, detail={BuildJsonErrorDetail(ex)}, payloadPreview={BuildJsonPreview(normalizedJson)}");
            throw;
        }
    }

    private static string BuildJsonErrorDetail(Exception ex)
    {
        if (ex is JsonException jsonEx)
        {
            return $"message={jsonEx.Message}, path={jsonEx.Path ?? "$"}, line={(jsonEx.LineNumber?.ToString() ?? "?")}, byte={(jsonEx.BytePositionInLine?.ToString() ?? "?")}";
        }

        return ex.Message;
    }

    private static string BuildJsonPreview(string json)
    {
        var compact = json.ReplaceLineEndings(" ").Trim();
        return compact.Length <= 160 ? compact : compact[..160];
    }

    private static string ExtractJsonPayload(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new InvalidOperationException("没有检测到可解析的 JSON 内容。");
        }

        var trimmed = rawText.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split(["\r\n", "\n"], StringSplitOptions.None);
            if (lines.Length >= 3)
            {
                trimmed = string.Join(Environment.NewLine, lines[1..^1]).Trim();
            }
        }

        if (TrySliceJsonObject(trimmed, out var directJson))
        {
            return directJson;
        }

        throw new InvalidOperationException("没有在当前内容中找到合法的 JSON 对象，请确认 AI 返回的是 JSON。");
    }

    private static bool TrySliceJsonObject(string text, out string json)
    {
        json = string.Empty;
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaping = false;
        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = text[start..(index + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    private static void CopyTextToClipboard(string text)
    {
        ClipboardService.SetText(text);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    private static SolidColorBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(colorHex));
    }

    private TResult ExecuteWithListenerServicesPaused<TResult>(Func<TResult> action)
    {
        var mainWindow = System.Windows.Application.Current.Windows
            .OfType<MainWindow>()
            .FirstOrDefault(static window => window.IsLoaded);

        if (mainWindow == null)
        {
            return action();
        }

        mainWindow.PauseListenerServices();
        try
        {
            return action();
        }
        finally
        {
            mainWindow.ResumeListenerServices();
        }
    }

    private enum WizardStep
    {
        Describe,
        Prompt,
        Test
    }

    private enum StepVisualState
    {
        Inactive,
        Active,
        Done
    }

    private sealed record TestExecutionResult(bool Success, string Summary, string Log);

    #region Advanced Editor & Inline Script Upgrades

    private bool _isJsonEditorReady = false;
    private bool _isScriptEditorReady = false;
    private bool _isJsonEditorInitializing = false;
    private bool _isSyncingScript = false;
    private bool _isUpdatingWebView = false;
    private TaskCompletionSource<bool>? _jsonEditorReadyTcs;
    private TaskCompletionSource<bool>? _scriptEditorReadyTcs;

    private int _lastErrorLine = 1;
    private int _lastErrorCol = 1;

    private async Task InitializeWebViewEditorsAsync()
    {
        if (_isJsonEditorInitializing) return;
        _isJsonEditorInitializing = true;

        if (!CheckWebView2RuntimeAvailable(logSuccess: false))
        {
            JsonWebViewEditor.Visibility = Visibility.Collapsed;
            ManualJsonInputBox.Visibility = Visibility.Visible;
            ScriptWebViewEditor.Visibility = Visibility.Collapsed;
            InlineScriptInputBox.Visibility = Visibility.Visible;
            return;
        }

        // 初始化 JSON 编辑器
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost",
                "JsonEditorWebView2");
            Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            await JsonWebViewEditor.EnsureCoreWebView2Async(env);
            JsonWebViewEditor.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            JsonWebViewEditor.CoreWebView2.Settings.AreDevToolsEnabled = true;

            JsonWebViewEditor.CoreWebView2.WebMessageReceived += JsonWebViewEditor_WebMessageReceived;
            JsonWebViewEditor.CoreWebView2.NavigateToString(GetJsonMonacoHtml());

            var timeoutTask = Task.Delay(4000);
            var readyTcs = new TaskCompletionSource<bool>();
            _jsonEditorReadyTcs = readyTcs;

            var completedTask = await Task.WhenAny(readyTcs.Task, timeoutTask);
            if (completedTask == readyTcs.Task && readyTcs.Task.Result)
            {
                _isJsonEditorReady = true;
                JsonWebViewEditor.Visibility = Visibility.Visible;
                ManualJsonInputBox.Visibility = Visibility.Collapsed;

                var text = ManualJsonInputBox.Text;
                _ = JsonWebViewEditor.ExecuteScriptAsync($"setValue({JsonSerializer.Serialize(text)})");
            }
            else
            {
                JsonWebViewEditor.Visibility = Visibility.Collapsed;
                ManualJsonInputBox.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to initialize JsonWebViewEditor: {ex.Message}");
            JsonWebViewEditor.Visibility = Visibility.Collapsed;
            ManualJsonInputBox.Visibility = Visibility.Visible;
        }

        // 初始化 Script 编辑器
        try
        {
            var userDataFolder2 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenQuickHost",
                "ScriptEditorWebView2");
            Directory.CreateDirectory(userDataFolder2);
            var env2 = await CoreWebView2Environment.CreateAsync(null, userDataFolder2);

            await ScriptWebViewEditor.EnsureCoreWebView2Async(env2);
            ScriptWebViewEditor.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            ScriptWebViewEditor.CoreWebView2.Settings.AreDevToolsEnabled = true;

            ScriptWebViewEditor.CoreWebView2.WebMessageReceived += ScriptWebViewEditor_WebMessageReceived;
            ScriptWebViewEditor.CoreWebView2.NavigateToString(GetScriptMonacoHtml());

            var timeoutTask2 = Task.Delay(4000);
            var readyTcs2 = new TaskCompletionSource<bool>();
            _scriptEditorReadyTcs = readyTcs2;

            var completedTask2 = await Task.WhenAny(readyTcs2.Task, timeoutTask2);
            if (completedTask2 == readyTcs2.Task && readyTcs2.Task.Result)
            {
                _isScriptEditorReady = true;
                ScriptWebViewEditor.Visibility = Visibility.Visible;
                InlineScriptInputBox.Visibility = Visibility.Collapsed;

                var text = InlineScriptInputBox.Text;
                _ = ScriptWebViewEditor.ExecuteScriptAsync($"setValue({JsonSerializer.Serialize(text)})");
            }
            else
            {
                ScriptWebViewEditor.Visibility = Visibility.Collapsed;
                InlineScriptInputBox.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Failed to initialize ScriptWebViewEditor: {ex.Message}");
            ScriptWebViewEditor.Visibility = Visibility.Collapsed;
            InlineScriptInputBox.Visibility = Visibility.Visible;
        }
    }

    private void JsonWebViewEditor_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var rawJson = e.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "ready")
            {
                _jsonEditorReadyTcs?.TrySetResult(true);
            }
            else if (type == "change")
            {
                var val = root.GetProperty("value").GetString() ?? string.Empty;
                if (!_isUpdatingWebView)
                {
                    _isUpdatingWebView = true;
                    ManualJsonInputBox.Text = val;
                    _isUpdatingWebView = false;
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"JsonWebViewEditor_WebMessageReceived failed: {ex.Message}");
        }
    }

    private void ScriptWebViewEditor_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var rawJson = e.TryGetWebMessageAsString();
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            if (type == "ready")
            {
                _scriptEditorReadyTcs?.TrySetResult(true);
            }
            else if (type == "change")
            {
                var val = root.GetProperty("value").GetString() ?? string.Empty;
                if (!_isSyncingScript)
                {
                    SyncScriptSourceToManifest(val);
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ScriptWebViewEditor_WebMessageReceived failed: {ex.Message}");
        }
    }

    private void UpdateInlineScriptPanelState()
    {
        if (string.IsNullOrWhiteSpace(ManualJsonInputBox.Text))
        {
            HideInlineScriptPanel();
            return;
        }

        try
        {
            var node = JsonNode.Parse(ManualJsonInputBox.Text);
            if (node != null && node["script"] is JsonObject scriptObj && scriptObj["source"] != null)
            {
                var sourceCode = scriptObj["source"]?.ToString() ?? string.Empty;
                var runtime = node["runtime"]?.ToString() ?? "csharp";

                ShowInlineScriptPanel(sourceCode, runtime);
            }
            else
            {
                HideInlineScriptPanel();
            }
        }
        catch
        {
            // 忽略 JSON 解析错误期间的状态更新
        }
    }

    private void ShowInlineScriptPanel(string sourceCode, string runtime)
    {
        var displayLang = "C#";
        var monacoLang = "csharp";
        if (runtime.Contains("powershell", StringComparison.OrdinalIgnoreCase) || runtime.Contains("ps", StringComparison.OrdinalIgnoreCase))
        {
            displayLang = "PowerShell";
            monacoLang = "powershell";
            InlineScriptLanguageBadge.Text = displayLang;
            InlineScriptLanguageBadgeHost.Background = CreateBrush("#1AF59E0B");
            InlineScriptLanguageBadge.Foreground = CreateBrush("#FFF59E0B");
        }
        else if (runtime.Contains("javascript", StringComparison.OrdinalIgnoreCase) || runtime.Contains("js", StringComparison.OrdinalIgnoreCase))
        {
            displayLang = "JavaScript";
            monacoLang = "javascript";
            InlineScriptLanguageBadge.Text = displayLang;
            InlineScriptLanguageBadgeHost.Background = CreateBrush("#1AFBBF24");
            InlineScriptLanguageBadge.Foreground = CreateBrush("#FFFBBF24");
        }
        else
        {
            InlineScriptLanguageBadge.Text = displayLang;
            InlineScriptLanguageBadgeHost.Background = CreateBrush("#1A3B82F6");
            InlineScriptLanguageBadge.Foreground = CreateBrush("#FF3B82F6");
        }

        if (!_isSyncingScript)
        {
            _isSyncingScript = true;
            InlineScriptInputBox.Text = sourceCode;
            if (_isScriptEditorReady && ScriptWebViewEditor.Visibility == Visibility.Visible)
            {
                _ = ScriptWebViewEditor.ExecuteScriptAsync($"setValue({JsonSerializer.Serialize(sourceCode)})");
                _ = ScriptWebViewEditor.ExecuteScriptAsync($"setLanguage({JsonSerializer.Serialize(monacoLang)})");
            }
            _isSyncingScript = false;
        }

        if (InlineScriptPanel.Visibility != Visibility.Visible)
        {
            // 上下分栏比例分配
            JsonEditorRow.Height = new GridLength(1.5, GridUnitType.Star);
            InlineScriptSplitterRow.Height = new GridLength(4);
            InlineScriptRow.Height = new GridLength(1.2, GridUnitType.Star);

            InlineScriptPanel.Visibility = Visibility.Visible;
            InlineScriptSplitter.Visibility = Visibility.Visible;
        }
    }

    private void HideInlineScriptPanel()
    {
        if (InlineScriptPanel.Visibility == Visibility.Visible)
        {
            JsonEditorRow.Height = new GridLength(1, GridUnitType.Star);
            InlineScriptSplitterRow.Height = new GridLength(0);
            InlineScriptRow.Height = new GridLength(0);

            InlineScriptPanel.Visibility = Visibility.Collapsed;
            InlineScriptSplitter.Visibility = Visibility.Collapsed;
        }
    }

    private void InlineScriptInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingScript) return;
        SyncScriptSourceToManifest(InlineScriptInputBox.Text);
    }

    private void SyncScriptSourceToManifest(string sourceCode)
    {
        if (_isSyncingScript) return;
        try
        {
            var jsonText = ManualJsonInputBox.Text;
            if (string.IsNullOrWhiteSpace(jsonText)) return;

            var node = JsonNode.Parse(jsonText);
            if (node is JsonObject jsonObject)
            {
                if (jsonObject["script"] == null)
                {
                    jsonObject["script"] = new JsonObject();
                }

                if (jsonObject["script"] is JsonObject scriptObj)
                {
                    scriptObj["source"] = sourceCode;
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var updatedJson = jsonObject.ToJsonString(options);

                _isSyncingScript = true;

                // 同步更新 TextBox 隐藏字段
                ManualJsonInputBox.Text = updatedJson;

                // 同步更新 Monaco
                if (_isJsonEditorReady && JsonWebViewEditor.Visibility == Visibility.Visible)
                {
                    _isUpdatingWebView = true;
                    _ = JsonWebViewEditor.ExecuteScriptAsync($"setValue({JsonSerializer.Serialize(updatedJson)})");
                    _isUpdatingWebView = false;
                }

                _isSyncingScript = false;
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"SyncScriptSourceToManifest failed: {ex.Message}");
        }
    }

    private void ManualJsonStatusText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

        e.Handled = true;

        if (_lastJsonValid) return;

        if (JsonWebViewEditor.Visibility == Visibility.Visible && _isJsonEditorReady)
        {
            _ = JsonWebViewEditor.ExecuteScriptAsync($"revealPosition({_lastErrorLine}, {_lastErrorCol})");
        }
        else
        {
            NavigateToLineAndColumn(ManualJsonInputBox, _lastErrorLine, _lastErrorCol);
        }
    }

    private void NavigateToLineAndColumn(System.Windows.Controls.TextBox textBox, int lineNumber, int columnNumber)
    {
        try
        {
            var text = textBox.Text;
            var currentLine = 1;
            var charIndex = 0;

            while (currentLine < lineNumber && charIndex < text.Length)
            {
                if (text[charIndex] == '\n')
                {
                    currentLine++;
                }
                charIndex++;
            }

            if (columnNumber > 0)
            {
                charIndex += (columnNumber - 1);
            }

            if (charIndex >= 0 && charIndex <= text.Length)
            {
                textBox.Focus();
                textBox.Select(charIndex, 0);

                var lineIndex = textBox.GetLineIndexFromCharacterIndex(charIndex);
                if (lineIndex >= 0)
                {
                    textBox.ScrollToLine(lineIndex);
                }
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"NavigateToLineAndColumn failed: {ex.Message}");
        }
    }

    private static bool CheckWebView2RuntimeAvailable(bool logSuccess = true)
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var ok = !string.IsNullOrWhiteSpace(version);
            if (ok && logSuccess)
            {
                HostAssets.AppendLog($"JsonEditor: WebView2 runtime available, version={version}.");
            }
            return ok;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"JsonEditor: WebView2 runtime unavailable, error={ex.Message}");
            return false;
        }
    }

    private string GetJsonMonacoHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html;charset=utf-8"" />
<style>
    html, body {
        width: 100%;
        height: 100%;
        margin: 0;
        padding: 0;
        overflow: hidden;
        background-color: #1e1e1e;
    }
    #container {
        width: 100%;
        height: 100%;
    }
</style>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.39.0/min/vs/loader.min.js""></script>
</head>
<body>
<div id=""container""></div>
<script>
    var editor;
    var currentMatchIndex = -1;
    var lastFindQuery = '';
    require.config({ paths: { 'vs': 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.39.0/min/vs' } });
    
    require(['vs/editor/editor.main'], function() {
        editor = monaco.editor.create(document.getElementById('container'), {
            value: '',
            language: 'json',
            theme: 'vs-dark',
            automaticLayout: true,
            tabSize: 2,
            formatOnPaste: true,
            formatOnType: true,
            folding: true,
            minimap: { enabled: false }
        });

        editor.onDidChangeModelContent(function() {
            var val = editor.getValue();
            try {
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'change',
                    value: val
                }));
            } catch(e) {}
        });

        try {
            window.chrome.webview.postMessage(JSON.stringify({
                type: 'ready'
            }));
        } catch(e) {}
    });

    function setValue(val) {
        if (editor && editor.getValue() !== val) {
            editor.setValue(val);
        }
    }
    function getValue() {
        return editor ? editor.getValue() : '';
    }
    function revealPosition(line, col) {
        if (editor) {
            editor.revealLineInCenter(line);
            editor.setPosition({ lineNumber: line, column: col });
            editor.focus();
        }
    }
    function findNext(query) {
        if (!editor) return;
        var model = editor.getModel();
        var matches = model.findMatches(query, true, false, false, null, true);
        if (matches && matches.length > 0) {
            if (query !== lastFindQuery) {
                currentMatchIndex = 0;
                lastFindQuery = query;
            } else {
                currentMatchIndex = (currentMatchIndex + 1) % matches.length;
            }
            var match = matches[currentMatchIndex];
            editor.setSelection(match.range);
            editor.revealRangeInCenter(match.range);
            editor.focus();
            return true;
        }
        return false;
    }
    function replaceCurrent(query, replacement) {
        if (!editor) return;
        var selection = editor.getSelection();
        var model = editor.getModel();
        var selectedText = model.getValueInRange(selection);
        if (selectedText.toLowerCase() === query.toLowerCase()) {
            editor.executeEdits('find-replace', [{
                range: selection,
                text: replacement,
                forceMoveMarkers: true
            }]);
            findNext(query);
            return true;
        } else {
            return findNext(query);
        }
    }
    function replaceAll(query, replacement) {
        if (!editor) return;
        var model = editor.getModel();
        var matches = model.findMatches(query, true, false, false, null, true);
        if (matches && matches.length > 0) {
            var edits = matches.map(function(m) {
                return {
                    range: m.range,
                    text: replacement,
                    forceMoveMarkers: true
                };
            });
            editor.executeEdits('find-replace', edits);
            return matches.length;
        }
        return 0;
    }
</script>
</body>
</html>";
    }

    private string GetScriptMonacoHtml()
    {
        return @"<!DOCTYPE html>
<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html;charset=utf-8"" />
<style>
    html, body {
        width: 100%;
        height: 100%;
        margin: 0;
        padding: 0;
        overflow: hidden;
        background-color: #1e1e1e;
    }
    #container {
        width: 100%;
        height: 100%;
    }
</style>
<script src=""https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.39.0/min/vs/loader.min.js""></script>
</head>
<body>
<div id=""container""></div>
<script>
    var editor;
    require.config({ paths: { 'vs': 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.39.0/min/vs' } });
    
    require(['vs/editor/editor.main'], function() {
        editor = monaco.editor.create(document.getElementById('container'), {
            value: '',
            language: 'csharp',
            theme: 'vs-dark',
            automaticLayout: true,
            tabSize: 4,
            folding: true,
            minimap: { enabled: false }
        });

        editor.onDidChangeModelContent(function() {
            var val = editor.getValue();
            try {
                window.chrome.webview.postMessage(JSON.stringify({
                    type: 'change',
                    value: val
                }));
            } catch(e) {}
        });

        try {
            window.chrome.webview.postMessage(JSON.stringify({
                type: 'ready'
            }));
        } catch(e) {}
    });

    function setValue(val) {
        if (editor && editor.getValue() !== val) {
            editor.setValue(val);
        }
    }
    function setLanguage(lang) {
        if (editor) {
            var model = editor.getModel();
            monaco.editor.setModelLanguage(model, lang);
        }
    }
    function revealPosition(line, col) {
        if (editor) {
            editor.revealLineInCenter(line);
            editor.setPosition({ lineNumber: line, column: col });
            editor.focus();
        }
    }
</script>
</body>
</html>";
    }

    private void UpdateTestArgumentPlaceholderVisibility()
    {
        if (TestArgumentPlaceholder == null || TestArgumentBox == null) return;
        
        TestArgumentPlaceholder.Visibility = 
            (string.IsNullOrEmpty(TestArgumentBox.Text) && !TestArgumentBox.IsFocused) 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private void TestArgumentBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateTestArgumentPlaceholderVisibility();
    }

    private void TestArgumentBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateTestArgumentPlaceholderVisibility();
    }

    private void TestArgumentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTestArgumentPlaceholderVisibility();
        
        // 当用户输入时，持久化记住它
        if (_settings != null && TestArgumentBox != null)
        {
            var text = TestArgumentBox.Text;
            if (string.Equals(_settings.LastTestArgument, text, StringComparison.Ordinal) == false)
            {
                _settings.LastTestArgument = text;
                try
                {
                    AppSettingsStore.Save(_settings);
                }
                catch
                {
                    // 忽略保存临时错误，保证编辑无阻碍
                }
            }
        }
    }

    #endregion
}
