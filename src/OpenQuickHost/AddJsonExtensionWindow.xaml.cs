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
using System.Windows.Media;
using System.Windows.Threading;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace OpenQuickHost;

public partial class AddJsonExtensionWindow : Window
{
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
    private AppSettings _settings;
    private string _aiGuidePrompt = string.Empty;
    private WizardStep _currentStep = WizardStep.Describe;
    private LocalExtensionHostedViewManifest? _manualHostedView;
    private bool _lastJsonValid;
    private bool _testCompleted;
    private bool _testSucceeded;
    private bool _manualMode;
    private bool _aiPromptCopied;
    private bool _isInitializing = true;

    public bool WasAccepted { get; private set; }

    public AddJsonExtensionWindow(string initialJson, bool isEditMode = false)
    {
        InitializeComponent();
        BuiltInIconsList.ItemsSource = _builtInIcons;
        _isEditMode = isEditMode;
        _settings = AppSettingsStore.Load();
        _manualMode = isEditMode || _settings.PreferManualExtensionEditor;

        ConfigureMode(initialJson);

        Loaded += (_, _) =>
        {
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
            
            // 初始化完成，允许同步
            _isInitializing = false;
        };
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
        ManualModeButton.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
        ManualModeButton.Content = _manualMode ? "AI 生成" : "手动编辑";

        if (_isEditMode)
        {
            PageHeaderPrefix.Text = "编辑";
            PageHeaderAccent.Text = "扩展";
            HeaderDescription.Text = "直接修改 JSON，验证通过后可以测试并保存。";
            Step1Label.Text = "编辑 JSON";
            Step2Label.Text = "测试扩展";
            Step3Label.Text = "保存";
            SaveButton.Content = "保存修改 →";
            _currentStep = WizardStep.Test;
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
        RefreshIconPreview();
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
        _manualMode = !_manualMode;
        _settings = _settings with { PreferManualExtensionEditor = _manualMode };
        AppSettingsStore.Save(_settings);
        ManualModeButton.Content = _manualMode ? "AI 生成" : "手动编辑";
        SyncJsonEditors(fromManual: _manualMode);
        RefreshAllState();
        
        // 强制更新窗口大小和布局
        UpdateLayout();
        InvalidateVisual();
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
        if (!_manualMode)
        {
            return;
        }

        // 初始化期间不同步编辑器
        if (!_isInitializing)
        {
            SyncJsonEditors(fromManual: true);
        }
        
        _testCompleted = false;
        _testSucceeded = false;
        ManualTestResultPanel.Visibility = Visibility.Collapsed;
        ManualTestLogTextBox.Clear();
        ManualTestSummaryText.Text = string.Empty;
        UpdateManualJsonValidationState();
        RefreshAllState();
    }

    private async void ManualTestExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTestAndRenderAsync(
            ManualTestExtensionButton,
            ManualTestResultPanel,
            ManualTestSummaryText,
            ManualTestLogTextBox,
            useManualJson: true);
    }

    private void ManualTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        ManualJsonInputBox.Text = tag switch
        {
            "open" => CreateOpenTargetTemplateJson(),
            "search" => CreateSearchTemplateJson(),
            "script" => CreateInlineScriptTemplateJson(),
            "foreground" => CreateForegroundWindowTemplateJson(),
            "clipboard" => CreateClipboardTemplateJson(),
            "selection" => CreateSelectionContextTemplateJson(),
            "csharp" => CreateCSharpContextTemplateJson(),
            "timestamp" => CreateTimestampTemplateJson(),
            "translate" => CreateTranslateWorkbenchTemplateJson(),
            _ => LocalExtensionCatalog.CreateTemplateJson()
        };

        TryPopulateManualFormFromJson(ManualJsonInputBox.Text, showError: false);
    }

    private void ParseManualJsonButton_Click(object sender, RoutedEventArgs e)
    {
        TryPopulateManualFormFromJson(ManualJsonInputBox.Text, showError: true);
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

    private void IconBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshIconPreview();
    }

    private void IconPreviewContext_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshIconPreview();
    }

    private void BuiltInIconButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string iconReference })
        {
            return;
        }

        IconBox.Text = iconReference;
        RefreshIconPreview();
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
        RefreshIconPreview();
    }

    private void ClearIconButton_Click(object sender, RoutedEventArgs e)
    {
        IconBox.Clear();
        RefreshIconPreview();
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

            CopyTextToClipboard(_aiGuidePrompt);
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
            CopyTextToClipboard(prompt);

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
            ShowError($"复制提示词失败：{ex.Message}");
        }
    }

    private string TryBuildManualCopyPrompt()
    {
        if (!string.IsNullOrWhiteSpace(ManualJsonInputBox.Text))
        {
            var manifestJson = ExtractJsonPayload(ManualJsonInputBox.Text);
            return BuildRefinePrompt(manifestJson);
        }

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

        if (!string.IsNullOrWhiteSpace(IdBox.Text))
        {
            parts.Add($"扩展 ID 倾向于使用：{IdBox.Text.Trim()}");
        }

        return parts.Count == 0
            ? "创建一个新的 OpenQuickHost 扩展。"
            : $"创建一个新的 OpenQuickHost 扩展，要求如下：{string.Join("；", parts)}。";
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

        // 初始化期间不同步编辑器
        if (!_isInitializing)
        {
            SyncJsonEditors(fromManual: false);
        }
        
        _testCompleted = false;
        _testSucceeded = false;
        TestResultPanel.Visibility = Visibility.Collapsed;
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
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            var normalizedJson = ExtractJsonPayload(GetCurrentJsonText());
            _ = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
                ?? throw new InvalidOperationException("JSON 解析失败。");

            WasAccepted = true;
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
        if (_manualMode)
        {
            WizardHeaderPanel.Visibility = Visibility.Collapsed;
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Collapsed;
            ManualEditorPanel.Visibility = Visibility.Visible;
            return;
        }

        WizardHeaderPanel.Visibility = Visibility.Visible;
        ManualEditorPanel.Visibility = Visibility.Collapsed;

        if (_isEditMode)
        {
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Collapsed;
            Step3Panel.Visibility = Visibility.Visible;
            return;
        }

        Step1Panel.Visibility = _currentStep == WizardStep.Describe ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == WizardStep.Prompt ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == WizardStep.Test ? Visibility.Visible : Visibility.Collapsed;
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
        SaveButton.Visibility = _lastJsonValid && (_manualMode || _currentStep == WizardStep.Test || _isEditMode)
            ? Visibility.Visible
            : Visibility.Collapsed;

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
        if (_manualMode)
        {
            Width = 1180;
            MinWidth = 1040;
            MaxWidth = double.PositiveInfinity;
            ContentRoot.MaxWidth = 1120;
            ApplyWindowHeight(preferredHeight: 820, minimumHeight: 720);
            return;
        }

        if (_isEditMode)
        {
            Width = 760;
            MinWidth = 680;
            MaxWidth = 760;
            ContentRoot.MaxWidth = 720;
            ApplyWindowHeight(preferredHeight: 820, minimumHeight: 680);
            return;
        }

        Width = 760;
        MinWidth = 680;
        MaxWidth = 760;
        ContentRoot.MaxWidth = 720;
        switch (_currentStep)
        {
            case WizardStep.Describe:
                ApplyWindowHeight(preferredHeight: 560, minimumHeight: 520);
                break;
            case WizardStep.Prompt:
                ApplyWindowHeight(preferredHeight: 900, minimumHeight: 880);
                break;
            case WizardStep.Test:
                ApplyWindowHeight(preferredHeight: 900, minimumHeight: 780);
                break;
        }

        UpdatePromptEditorHeight();
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
            var normalized = ExtractJsonPayload(AiJsonInputBox.Text);
            _ = JsonSerializer.Deserialize<LocalExtensionManifest>(normalized, CreateJsonOptions())
                ?? throw new InvalidOperationException("JSON 解析失败。");

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
            var normalized = ExtractJsonPayload(ManualJsonInputBox.Text);
            _ = JsonSerializer.Deserialize<LocalExtensionManifest>(normalized, CreateJsonOptions())
                ?? throw new InvalidOperationException("JSON 解析失败。");

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
        }
    }

    private string GetCurrentJsonText() => _manualMode ? ManualJsonInputBox.Text : AiJsonInputBox.Text;

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
                AiJsonInputBox.Text = ManualJsonInputBox.Text;
            }

            return;
        }

        if (!string.Equals(ManualJsonInputBox.Text, AiJsonInputBox.Text, StringComparison.Ordinal))
        {
            ManualJsonInputBox.Text = AiJsonInputBox.Text;
        }
    }

    private void TryPopulateManualFormFromJson(string json, bool showError)
    {
        try
        {
            var normalizedJson = ExtractJsonPayload(json);
            var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(normalizedJson, CreateJsonOptions())
                ?? throw new InvalidOperationException("JSON 解析失败。");
            ApplyManifestToForm(manifest);
            ErrorText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            if (showError)
            {
                ShowError($"解析 JSON 失败：{ex.Message}");
            }
        }
    }

    private LocalExtensionManifest BuildManifestFromForm()
    {
        if (string.IsNullOrWhiteSpace(IdBox.Text))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            throw new InvalidOperationException("扩展名称不能为空。");
        }

        var runtime = NullIfEmpty(RuntimeBox.Text);
        var entryMode = NullIfEmpty(EntryModeBox.Text);
        var scriptSource = NullIfEmpty(ScriptSourceBox.Text);

        return new LocalExtensionManifest
        {
            Id = IdBox.Text.Trim(),
            Name = NameBox.Text.Trim(),
            Version = string.IsNullOrWhiteSpace(VersionBox.Text) ? "0.1.0" : VersionBox.Text.Trim(),
            Category = NullIfEmpty(CategoryBox.Text),
            Description = NullIfEmpty(DescriptionBox.Text),
            Keywords = SplitCsv(KeywordsBox.Text),
            OpenTarget = NullIfEmpty(OpenTargetBox.Text),
            QueryPrefixes = SplitCsv(QueryPrefixesBox.Text),
            QueryTargetTemplate = NullIfEmpty(QueryTargetTemplateBox.Text),
            Icon = NullIfEmpty(IconBox.Text),
            HostedView = _manualHostedView,
            GlobalShortcut = NullIfEmpty(GlobalShortcutBox.Text),
            HotkeyBehavior = NullIfEmpty(HotkeyBehaviorBox.Text),
            Runtime = runtime,
            EntryMode = entryMode,
            Entry = NullIfEmpty(EntryBox.Text),
            Permissions = SplitCsv(PermissionsBox.Text),
            Script = string.IsNullOrWhiteSpace(scriptSource) ? null : new LocalExtensionInlineScriptManifest
            {
                Source = ScriptSourceBox.Text.ReplaceLineEndings("\r\n")
            }
        };
    }

    private void ApplyManifestToForm(LocalExtensionManifest manifest)
    {
        IdBox.Text = manifest.Id;
        NameBox.Text = manifest.Name;
        VersionBox.Text = manifest.Version;
        CategoryBox.Text = manifest.Category ?? string.Empty;
        DescriptionBox.Text = manifest.Description ?? string.Empty;
        KeywordsBox.Text = manifest.Keywords == null ? string.Empty : string.Join(", ", manifest.Keywords);
        OpenTargetBox.Text = manifest.OpenTarget ?? string.Empty;
        QueryPrefixesBox.Text = manifest.QueryPrefixes == null ? string.Empty : string.Join(", ", manifest.QueryPrefixes);
        QueryTargetTemplateBox.Text = manifest.QueryTargetTemplate ?? string.Empty;
        IconBox.Text = manifest.Icon ?? string.Empty;
        GlobalShortcutBox.Text = manifest.GlobalShortcut ?? string.Empty;
        HotkeyBehaviorBox.Text = manifest.HotkeyBehavior ?? string.Empty;
        RuntimeBox.Text = manifest.Runtime ?? string.Empty;
        EntryModeBox.Text = manifest.EntryMode ?? string.Empty;
        EntryBox.Text = manifest.Entry ?? string.Empty;
        PermissionsBox.Text = manifest.Permissions == null ? string.Empty : string.Join(", ", manifest.Permissions);
        ScriptSourceBox.Text = manifest.Script?.Source ?? string.Empty;
        _manualHostedView = manifest.HostedView;
        RefreshIconPreview();
    }

    private void RefreshIconPreview()
    {
        IconPreviewImage.Visibility = Visibility.Collapsed;
        IconPreviewImage.Source = null;
        IconPreviewVectorHost.Visibility = Visibility.Collapsed;
        IconPreviewVector.Data = null;
        IconPreviewGlyph.Visibility = Visibility.Collapsed;

        var iconReference = NullIfEmpty(IconBox.Text);
        var previewDirectory = string.IsNullOrWhiteSpace(IdBox.Text)
            ? HostAssets.ExtensionsPath
            : Path.Combine(HostAssets.ExtensionsPath, IdBox.Text.Trim());

        var imageSource = ExtensionIconLibrary.ResolveImageSource(iconReference, previewDirectory);
        if (imageSource != null)
        {
            IconPreviewImage.Source = imageSource;
            IconPreviewImage.Visibility = Visibility.Visible;
            IconPreviewHintText.Text = "当前使用图片图标或本地图标路径。";
            HighlightSelectedBuiltInButton(null);
            return;
        }

        var vectorIcon = ExtensionIconLibrary.ResolveVectorIcon(iconReference);
        if (vectorIcon != null)
        {
            IconPreviewVector.Data = vectorIcon;
            IconPreviewVectorHost.Visibility = Visibility.Visible;
            IconPreviewHintText.Text = $"当前使用内置图标：{iconReference}";
            HighlightSelectedBuiltInButton(iconReference);
            return;
        }

        IconPreviewGlyph.Text = InferFallbackGlyph();
        IconPreviewGlyph.Visibility = Visibility.Visible;
        IconPreviewHintText.Text = string.IsNullOrWhiteSpace(iconReference)
            ? "未设置图标时会回退为字母标识。"
            : $"当前 icon 值未解析成功：{iconReference}";
        HighlightSelectedBuiltInButton(null);
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
            try
            {
                var command = BuildTestCommand(manifest, tempDirectory);
                var result = await ScriptExtensionRunner.ExecuteAsync(command, "测试输入", "extension-editor-test");
                logBuilder.AppendLine($"执行结果：{(result.Success ? "成功" : "失败")}");
                logBuilder.AppendLine($"退出码：{result.ExitCode}");
                logBuilder.AppendLine();
                logBuilder.AppendLine("标准输出：");
                logBuilder.AppendLine(string.IsNullOrWhiteSpace(result.Output) ? "无输出。" : result.Output.Trim());
                logBuilder.AppendLine();
                logBuilder.AppendLine("错误输出：");
                logBuilder.AppendLine(string.IsNullOrWhiteSpace(result.Error) ? "无错误输出。" : result.Error.Trim());

                var hostLogTail = ReadHostLogTail();
                if (!string.IsNullOrWhiteSpace(hostLogTail))
                {
                    logBuilder.AppendLine();
                    logBuilder.AppendLine("宿主日志（最近几行）：");
                    logBuilder.AppendLine(hostLogTail);
                }

                return new TestExecutionResult(
                    result.Success,
                    result.Success ? "测试通过，脚本已经成功执行。" : "测试未通过，请根据下方日志检查脚本。",
                    logBuilder.ToString());
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
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

        if (!string.IsNullOrWhiteSpace(manifest.OpenTarget))
        {
            var target = manifest.OpenTarget.Trim();
            var exists = File.Exists(target) || Directory.Exists(target);
            var isUri = Uri.TryCreate(target, UriKind.Absolute, out _);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
            logBuilder.AppendLine("类型：打开目标扩展");
            logBuilder.AppendLine($"目标：{target}");
            logBuilder.AppendLine($"本地存在：{exists}");
            logBuilder.AppendLine($"绝对地址：{isUri}");
            logBuilder.AppendLine("已实际执行打开动作。");
            return new TestExecutionResult(
                exists || isUri,
                exists || isUri ? "测试通过，已实际执行打开动作。" : "测试未通过，目标既不是可访问地址，也不是现有文件/目录。",
                logBuilder.ToString());
        }

        logBuilder.AppendLine("未检测到 runtime、queryTargetTemplate 或 openTarget。");
        return new TestExecutionResult(false, "当前扩展缺少可测试的执行入口。", logBuilder.ToString());
    }

    private async Task RunTestAndRenderAsync(
        System.Windows.Controls.Button triggerButton,
        Border resultPanel,
        TextBlock summaryText,
        System.Windows.Controls.TextBox logTextBox,
        bool useManualJson)
    {
        try
        {
            ErrorText.Visibility = Visibility.Collapsed;
            triggerButton.IsEnabled = false;
            triggerButton.Content = "测试中...";
            resultPanel.Visibility = Visibility.Visible;
            summaryText.Text = "正在执行测试，请稍等。";
            logTextBox.Text = string.Empty;

            var result = await RunExtensionTestAsync(useManualJson);
            _testCompleted = true;
            _testSucceeded = result.Success;

            summaryText.Foreground = result.Success ? GreenBrush : RedBrush;
            summaryText.Text = result.Summary;
            logTextBox.Text = result.Log;
        }
        catch (Exception ex)
        {
            _testCompleted = true;
            _testSucceeded = false;
            resultPanel.Visibility = Visibility.Visible;
            summaryText.Foreground = RedBrush;
            summaryText.Text = "测试执行失败。";
            logTextBox.Text = ex.ToString();
        }
        finally
        {
            triggerButton.IsEnabled = _lastJsonValid;
            triggerButton.Content = "测试扩展";
            RefreshAllState();
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
            accentHex: "#FF3B82F6",
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
            entryPoint: manifest.Entry,
            permissions: manifest.Permissions ?? [],
            entryMode: manifest.EntryMode,
            inlineScriptSource: manifest.Script?.Source,
            iconReference: manifest.Icon);
    }

    private static string ReadHostLogTail()
    {
        try
        {
            if (!File.Exists(HostAssets.HostLogPath))
            {
                return string.Empty;
            }

            var lines = File.ReadAllLines(HostAssets.HostLogPath);
            return string.Join(Environment.NewLine, lines.TakeLast(12));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildDetailedPrompt(string request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请帮我生成一个 Yanzi / OpenQuickHost 扩展的完整 JSON 配置。");
        builder.AppendLine();
        builder.AppendLine("一、背景说明");
        builder.AppendLine("这个产品的设计理念是“万物皆扩展”。用户会在桌面启动器、快捷面板、鼠标呼出面板里运行扩展。");
        builder.AppendLine("扩展可以是：");
        builder.AppendLine("1. 直接打开网页、程序、文件、文件夹");
        builder.AppendLine("2. 做网页搜索");
        builder.AppendLine("3. 运行脚本处理输入内容");
        builder.AppendLine("4. 在宿主界面里展示一个简单工作区");
        builder.AppendLine();
        builder.AppendLine("我的需求是：");
        builder.AppendLine(request);
        builder.AppendLine();
        builder.AppendLine("二、输出要求");
        builder.AppendLine("1. 只返回 JSON，不要解释，不要 Markdown 代码块");
        builder.AppendLine("2. JSON 必须能直接被 System.Text.Json 解析");
        builder.AppendLine("3. 如果最简单的配置就能实现，不要过度设计");
        builder.AppendLine("4. 优先选择最贴近需求的方案：");
        builder.AppendLine("   - 打开类：优先用 openTarget");
        builder.AppendLine("   - 搜索类：优先用 queryPrefixes + queryTargetTemplate");
        builder.AppendLine("   - 脚本类：优先用 runtime = csharp，必要时才用 powershell");
        builder.AppendLine("   - 内联脚本：使用 entryMode = inline 和 script.source");
        builder.AppendLine();
        builder.AppendLine("三、字段说明");
        builder.AppendLine("- id：扩展唯一标识，只能英文小写、数字、短横线，例如 \"open-project-folder\"");
        builder.AppendLine("- name：扩展显示名称");
        builder.AppendLine("- version：版本号，默认 \"0.1.0\"");
        builder.AppendLine("- category：分类，例如 \"扩展\"、\"网页搜索\"、\"效率工具\"");
        builder.AppendLine("- description：一句话描述扩展用途");
        builder.AppendLine("- keywords：搜索关键词数组");
        builder.AppendLine("- icon：图标，可用 mdi:图标名 或图片地址");
        builder.AppendLine("- openTarget：点击后直接打开的目标");
        builder.AppendLine("- queryPrefixes：搜索前缀数组，例如 [\"百度\", \"baidu\"]");
        builder.AppendLine("- queryTargetTemplate：搜索模板，必须包含 {query}");
        builder.AppendLine("- runtime：脚本运行时，例如 \"csharp\" 或 \"powershell\"");
        builder.AppendLine("- entryMode：如果是内联脚本请写 \"inline\"");
        builder.AppendLine("- entry：如果是外部脚本文件，写入口文件名");
        builder.AppendLine("- permissions：权限数组，例如 [\"clipboard\", \"network\"]");
        builder.AppendLine("- 扩展脚本现在支持 context.Storage 本地/云端存储 helper：ReadTextAsync、WriteTextAsync、ReadJsonAsync<T>、WriteJsonAsync<T>");
        builder.AppendLine("- context.Storage 默认支持 scope = local、cloud、both；local 写入本地扩展数据目录，cloud / both 会通过宿主 API 写入坚果云 / WebDAV");
        builder.AppendLine("- script.source：内联脚本源码");
        builder.AppendLine("- hostedViewXaml：如果要让宿主直接加载自定义 XAML 界面，请输出 hostedViewXaml");
        builder.AppendLine("- hostedViewXaml.xaml：填写可直接解析的 WPF XAML 字符串，根元素建议用 Grid、UserControl 或 Window");
        builder.AppendLine("- hostedViewXaml.state：初始化状态对象，XAML 中可通过 {Binding [key]} 绑定");
        builder.AppendLine("- hostedViewXaml.window.width / height / minWidth / minHeight：可选，控制窗口尺寸");
        builder.AppendLine("- hostedViewXaml 中按钮可用 xmlns:oqh=\"clr-namespace:OpenQuickHost\"，再用 oqh:HostedViewBridge.Action 声明动作");
        builder.AppendLine("- oqh:HostedViewBridge.Action 当前支持 close、setState、runScript、loadStorage、saveStorage；多个动作可用 | 分隔，参数用 ;key=value");
        builder.AppendLine("- 根元素还支持 oqh:HostedViewBridge.LoadedAction，可在窗口打开时自动执行 loadStorage");
        builder.AppendLine("- hostedViewV2：如果要在宿主里显示内置界面，也可以输出 hostedViewV2，不要返回 @view: 之类的协议字符串");
        builder.AppendLine("- hostedViewV2.type：当前支持 \"single-pane\"、\"split-horizontal\"");
        builder.AppendLine("- hostedViewV2.window.width / height / minWidth / minHeight：可选，控制窗口尺寸");
        builder.AppendLine("- hostedViewV2.state：初始化状态对象，例如 { \"note\": \"\", \"preview\": \"先输入内容\" }");
        builder.AppendLine("- hostedViewV2.components：当前支持 text、textarea、button、markdown");
        builder.AppendLine("- 组件的 bind 字段用于绑定到 state 路径");
        builder.AppendLine("- button.actions：当前支持 setState、runScript、loadStorage、saveStorage");
        builder.AppendLine("- 如果只是旧版简单双栏工作区，也可以输出 hostedView，但新方案优先用 hostedViewXaml 或 hostedViewV2");
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
        builder.AppendLine("  \"permissions\": [\"clipboard\"],");
        builder.AppendLine("  \"icon\": \"mdi:code-tags\",");
        builder.AppendLine("  \"script\": {");
        builder.AppendLine("    \"source\": \"using OpenQuickHost.CSharpRuntime;\\npublic static class YanziAction\\n{\\n    public static Task<string> RunAsync(YanziActionContext context)\\n    {\\n        return Task.FromResult(\\\"收到输入：\\\" + context.InputText);\\n    }\\n}\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("模板 4：宿主自定义 XAML 视图扩展（hostedViewXaml）");
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
        builder.AppendLine("      \"preview\": \"先在左侧输入内容，这里会显示便签结果。\"");
        builder.AppendLine("    },");
        builder.AppendLine("    \"xaml\": \"<Grid xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\" xmlns:x=\\\"http://schemas.microsoft.com/winfx/2006/xaml\\\" xmlns:oqh=\\\"clr-namespace:OpenQuickHost\\\" oqh:HostedViewBridge.PreferredFocus=\\\"NoteBox\\\" oqh:HostedViewBridge.LoadedAction=\\\"loadStorage;path=note;key=note.txt;scope=both;defaultValue=\\\"><Grid.ColumnDefinitions><ColumnDefinition Width=\\\"*\\\"/><ColumnDefinition Width=\\\"16\\\"/><ColumnDefinition Width=\\\"*\\\"/></Grid.ColumnDefinitions><StackPanel Grid.Column=\\\"0\\\"><TextBlock Text=\\\"便签内容\\\" Foreground=\\\"White\\\" FontSize=\\\"14\\\" FontWeight=\\\"SemiBold\\\" Margin=\\\"0,0,0,10\\\"/><TextBox x:Name=\\\"NoteBox\\\" Text=\\\"{Binding [note], UpdateSourceTrigger=PropertyChanged}\\\" AcceptsReturn=\\\"True\\\" VerticalScrollBarVisibility=\\\"Auto\\\" TextWrapping=\\\"Wrap\\\" MinHeight=\\\"320\\\" Padding=\\\"12\\\"/><Button Content=\\\"保存便签\\\" Margin=\\\"0,12,0,0\\\" oqh:HostedViewBridge.Action=\\\"saveStorage;path=note;key=note.txt;scope=both;successMessage=便签已保存。|setState;path=preview;valueFrom=note\\\"/></StackPanel><Border Grid.Column=\\\"2\\\" Background=\\\"#FF171717\\\" BorderBrush=\\\"#FF2E2E2E\\\" BorderThickness=\\\"1\\\" CornerRadius=\\\"10\\\" Padding=\\\"12\\\"><TextBlock Text=\\\"{Binding [preview]}\\\" TextWrapping=\\\"Wrap\\\" Foreground=\\\"White\\\"/></Border></Grid>\"");
        builder.AppendLine("  }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("五、最终要求");
        builder.AppendLine("请结合我的需求，返回一份最终可用的完整 JSON，不要返回多个方案，不要附加说明。");
        builder.AppendLine("如果需求里提到便签、面板、编辑器、工作区、内置界面，请优先使用 hostedViewXaml；如果只是简单表单，再考虑 hostedViewV2。");
        return builder.ToString();
    }

    private static string BuildRefinePrompt(string manifestJson)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请基于下面这份 Yanzi / OpenQuickHost 扩展 JSON，帮我修改并输出最终可用版本。");
        builder.AppendLine();
        builder.AppendLine("一、背景说明");
        builder.AppendLine("这个产品的设计理念是“万物皆扩展”。用户会在桌面启动器、快捷面板、鼠标呼出面板里运行扩展。");
        builder.AppendLine("扩展可以是：");
        builder.AppendLine("1. 直接打开网页、程序、文件、文件夹");
        builder.AppendLine("2. 做网页搜索");
        builder.AppendLine("3. 运行脚本处理输入内容");
        builder.AppendLine("4. 在宿主界面里展示一个简单工作区或自定义 XAML 界面");
        builder.AppendLine();
        builder.AppendLine("二、修改要求");
        builder.AppendLine("1. 只返回 JSON，不要解释，不要 Markdown 代码块");
        builder.AppendLine("2. 保留原有结构和意图，按需要修正字段，不要无意义改名");
        builder.AppendLine("3. 不要输出 null 字段，能省略就省略");
        builder.AppendLine("4. icon 支持 mdi:图标名、app:图标名、本地相对路径、绝对路径、图片 URL");
        builder.AppendLine("5. 如果是搜索扩展，确保 queryTargetTemplate 包含 {query}");
        builder.AppendLine("6. 如果是脚本扩展，优先使用 csharp；内联脚本用 entryMode = inline 和 script.source");
        builder.AppendLine("7. 如果需要宿主内置界面，请优先使用 hostedViewXaml，不要输出 @view:textarea 或其他未定义协议");
        builder.AppendLine("8. hostedViewXaml 里不要输出 x:Class，不要假设宿主会自动解析你手写的事件处理函数；按钮动作请通过 oqh:HostedViewBridge.Action 声明");
        builder.AppendLine("9. hostedViewXaml.xaml 必须是可直接解析的 WPF XAML 字符串，不要把 xmlns 写成 Markdown 链接，不要出现 [http://...](http://...) 这种格式");
        builder.AppendLine("10. XAML 根元素建议使用 Grid、UserControl 或 Window；如果用自定义命名空间，请确保 xmlns 正确");
        builder.AppendLine("11. oqh:HostedViewBridge.Action 当前支持 close、setState、runScript、loadStorage、saveStorage；setState 支持 value、valueFrom、append、separator");
        builder.AppendLine("12. 如需在窗口打开时自动加载本地或坚果云数据，可在根元素上使用 oqh:HostedViewBridge.LoadedAction");
        builder.AppendLine("13. 脚本扩展可使用 context.Storage.ReadTextAsync / WriteTextAsync / ReadJsonAsync / WriteJsonAsync，scope 支持 local、cloud、both");
        builder.AppendLine("14. 如果只是简单表单或双栏工作区，也可以使用 hostedViewV2，但自定义界面优先用 hostedViewXaml");
        builder.AppendLine();
        builder.AppendLine("三、字段和能力提醒");
        builder.AppendLine("- id：扩展唯一标识，只能英文小写、数字、短横线");
        builder.AppendLine("- name：扩展显示名称");
        builder.AppendLine("- version：版本号，默认 \"0.1.0\"");
        builder.AppendLine("- category：分类");
        builder.AppendLine("- description：一句话描述扩展用途");
        builder.AppendLine("- keywords：搜索关键词数组");
        builder.AppendLine("- hostedViewXaml.window.width / height / minWidth / minHeight：可选，控制窗口尺寸");
        builder.AppendLine("- hostedViewXaml.state：初始化状态对象，XAML 中可通过 {Binding [key]} 绑定");
        builder.AppendLine("- hostedViewXaml.xaml：完整 XAML 字符串");
        builder.AppendLine("- 如果按钮要把输入追加到现有文本，可使用 setState;path=xxx;valueFrom=yyy;append=true;separator=\\n");
        builder.AppendLine("- 如果要持久化待办、便签、历史记录，可使用 loadStorage / saveStorage，或在脚本里使用 context.Storage");
        builder.AppendLine();
        builder.AppendLine("四、当前 JSON");
        builder.AppendLine(manifestJson);
        builder.AppendLine();
        builder.AppendLine("五、输出要求");
        builder.AppendLine("请直接返回修正后的完整 JSON。");
        builder.AppendLine("如果当前 JSON 已经是 hostedViewXaml，请保留 hostedViewXaml 方案，除非它明显不适合需求。");
        builder.AppendLine("如果当前 JSON 里是待办、便签、工作区、面板这类界面扩展，请优先修正现有 XAML，而不是退回成普通 openTarget 或脚本扩展。");
        return builder.ToString();
    }

    private static string CreateOpenTargetTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = $"open-target-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "打开目标",
            Version = "0.1.0",
            Category = "扩展",
            Description = "点击后打开指定目标。",
            Keywords = ["打开", "target"],
            OpenTarget = "shell:Desktop",
            Icon = "mdi:folder-open"
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
            Icon = "https://www.baidu.com/favicon.ico"
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
            Script = new LocalExtensionInlineScriptManifest
            {
                Source = "using OpenQuickHost.CSharpRuntime;\npublic static class YanziAction\n{\n    public static Task<string> RunAsync(YanziActionContext context)\n    {\n        return Task.FromResult(\"收到输入：\" + context.InputText);\n    }\n}"
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
            Id = $"clipboard-reader-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Name = "读取剪贴板",
            Version = "0.1.0",
            Category = "脚本",
            Description = "读取当前剪贴板文本。",
            Keywords = ["clipboard", "剪贴板", "powershell", "script"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.read"],
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

$text = Get-Clipboard -Raw
if ([string]::IsNullOrWhiteSpace($text)) {
    Write-Output "当前剪贴板为空。"
} else {
    Write-Output $text.Trim()
}
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
            Description = "使用 C# 读取宿主传入的上下文并返回结果。",
            Keywords = ["csharp", "dotnet", "context", "示例"],
            Runtime = "csharp",
            EntryMode = "inline",
            Permissions = ["context.read"],
            Icon = "mdi:code",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = string.IsNullOrWhiteSpace(context.InputText)
            ? "没有收到选中内容。"
            : context.InputText.Trim();

        return Task.FromResult(
            $"来源: {context.LaunchSource}" + Environment.NewLine +
            $"扩展目录: {context.ExtensionDirectory}" + Environment.NewLine +
            Environment.NewLine +
            "输入:" + Environment.NewLine +
            input);
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
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
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
        if (TryCopyViaStaClipboard(text, out var staError))
        {
            return;
        }

        if (TryCopyViaClipExe(text, out var fallbackError))
        {
            return;
        }

        throw new InvalidOperationException($"复制到剪贴板失败：{fallbackError ?? staError}");
    }

    private static bool TryCopyViaStaClipboard(string text, out string? error)
    {
        error = null;
        Exception? threadError = null;
        var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                Forms.Clipboard.SetText(text, Forms.TextDataFormat.UnicodeText);
            }
            catch (Exception ex)
            {
                threadError = ex;
            }
            finally
            {
                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        done.Wait(TimeSpan.FromSeconds(5));

        if (!done.IsSet)
        {
            error = "STA 剪贴板线程超时。";
            return false;
        }

        if (threadError == null)
        {
            return true;
        }

        error = threadError.Message;
        return false;
    }

    private static bool TryCopyViaClipExe(string text, out string? error)
    {
        error = null;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = Encoding.Unicode,
                CreateNoWindow = true
            };

            process.Start();
            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                return true;
            }

            error = $"clip.exe 返回了退出码 {process.ExitCode}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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
}
