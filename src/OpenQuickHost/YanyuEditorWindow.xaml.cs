using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using Yanzi.UI.Wpf;

namespace OpenQuickHost;

public partial class YanyuEditorWindow : Window
{
    public sealed record ActionTypeOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record SuffixOption(string Value, string Label, string KbdLabel)
    {
        public override string ToString() => Label;
    }

    public sealed record ExtensionOption(
        string ExtensionId,
        string Label,
        string Detail,
        ImageSource? IconSource,
        Geometry? VectorIcon,
        MediaBrush AccentBrush,
        string DisplayGlyph)
    {
        public bool HasImageIcon => IconSource != null;

        public bool HasVectorIcon => VectorIcon != null && !HasImageIcon;

        public bool UseGlyphIcon => !HasImageIcon && !HasVectorIcon;

        public override string ToString() => Label;
    }

    private readonly List<ExtensionOption> _extensionOptions;
    private string _selectedExtensionId = string.Empty;
    private bool _initializingExtensionSelection;
    private bool _committingExtensionSelection;

    public YanyuEditorWindow(
        string title,
        string subtitle,
        YanyuRuleSettings initialRule,
        IReadOnlyList<CommandItem> extensions,
        bool isEditMode)
    {
        InitializeComponent();
        // Apply the shared design system only to this window, without altering the host's resources.
        var themeMode = AppSettingsStore.Load().ThemeMode;
        var useLightTheme = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(themeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                useLightTheme = key?.GetValue("AppsUseLightTheme") is int light && light == 1;
            }
            catch { /* Use the dark theme when Windows personalization is unavailable. */ }
        }
        YanziUi.ApplyTo(this, useLightTheme ? YanziTheme.Light : YanziTheme.Dark);
        Title = title;
         DeleteButton.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;

        SuffixComboBox.ItemsSource = new[]
        {
            new SuffixOption(YanyuTriggerSuffix.Space, "空格", YanyuTriggerSuffix.ToKbdText(YanyuTriggerSuffix.Space)),
            new SuffixOption(YanyuTriggerSuffix.Tab, "制表", YanyuTriggerSuffix.ToKbdText(YanyuTriggerSuffix.Tab)),
            new SuffixOption(YanyuTriggerSuffix.Enter, "回车", YanyuTriggerSuffix.ToKbdText(YanyuTriggerSuffix.Enter)),
            new SuffixOption(";", "分号", YanyuTriggerSuffix.ToKbdText(";"))
        };

        ActionTypeComboBox.ItemsSource = new[]
        {
            new ActionTypeOption(YanyuActionTypes.PasteText, "粘贴文本"),
            new ActionTypeOption(YanyuActionTypes.RunExtension, $"运行{BrandTerms.Current.MiniApp}")
        };

        _extensionOptions = extensions
            .OrderBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new ExtensionOption(
                item.ExtensionId,
                item.Title,
                BuildExtensionOptionDetail(item),
                item.IconSource,
                item.VectorIcon,
                item.AccentBrush,
                item.DisplayGlyph))
            .ToList();
        ExtensionListBox.ItemsSource = _extensionOptions;

        EnabledCheckBox.IsChecked = initialRule.Enabled;
        TriggerTextBox.Text = initialRule.TriggerText;
        UseRegexCheckBox.IsChecked = initialRule.UseRegex;
        BoundProcessBox.ItemsSource = new[] { new ProcessItem { ProcessName = "所有应用" } }
            .Concat(ProcessCatalog.GetRunningProcesses(initialRule.BoundProcessName))
            .ToList();
        BoundProcessBox.Text = string.IsNullOrWhiteSpace(initialRule.BoundProcessName)
            ? "所有应用" : initialRule.BoundProcessName;
        SuffixComboBox.Text = YanyuTriggerSuffix.ToDisplayText(initialRule.TriggerSuffix);
        ActionTypeComboBox.SelectedValue = YanyuActionTypes.Normalize(initialRule.ActionType);
        DescriptionBox.Text = initialRule.Description;
        TextContentBox.Text = initialRule.TextContent;
        _selectedExtensionId = initialRule.ExtensionId ?? string.Empty;
        var selectedExtension = _extensionOptions.FirstOrDefault(item => item.ExtensionId.Equals(_selectedExtensionId, StringComparison.OrdinalIgnoreCase));
        _initializingExtensionSelection = true;
        ExtensionSearchBox.Text = selectedExtension?.Label ?? string.Empty;
        ExtensionListBox.SelectedItem = selectedExtension;
        ExtensionListBox.Visibility = Visibility.Collapsed;
        UpdateSelectedExtensionPreview(selectedExtension);
        _initializingExtensionSelection = false;

        Loaded += (_, _) =>
        {
            TriggerTextBox.Focus();
            TriggerTextBox.SelectAll();
            UpdateActionPanels();
        };
    }

    public bool WasDeleted { get; private set; }

    public YanyuRuleSettings EditedRule { get; private set; } = new();

    private void ActionTypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateActionPanels();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var triggerText = (TriggerTextBox.Text ?? string.Empty).Trim();
        if (triggerText.Length == 0)
        {
            ShowError("缩写词不能为空。");
            return;
        }

        var suffix = YanyuTriggerSuffix.NormalizeDisplayText(SuffixComboBox.Text);
        if (suffix.Length == 0)
        {
            ShowError("触发后缀不能为空。");
            return;
        }

        var actionType = YanyuActionTypes.Normalize(ActionTypeComboBox.SelectedValue as string);
        var useRegex = UseRegexCheckBox.IsChecked == true;
        if (useRegex)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(triggerText, System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80));
            }
            catch (Exception ex)
            {
                ShowError($"正则表达式无效：{ex.Message}");
                return;
            }
        }

        var textContent = TextContentBox.Text ?? string.Empty;
        var extensionId = ResolveSelectedExtensionId();
        if (string.Equals(actionType, YanyuActionTypes.PasteText, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(textContent))
        {
            ShowError("文本内容不能为空。");
            return;
        }

        if (string.Equals(actionType, YanyuActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(extensionId))
        {
            ShowError($"请选择一个{BrandTerms.Current.MiniApp}。");
            return;
        }

        EditedRule = new YanyuRuleSettings
        {
            Enabled = EnabledCheckBox.IsChecked != false,
            TriggerText = triggerText,
            TriggerSuffix = suffix,
            UseRegex = useRegex,
            BoundProcessName = string.Equals(BoundProcessBox.Text?.Trim(), "所有应用", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : (BoundProcessBox.Text ?? string.Empty).Trim(),
            ActionType = actionType,
            TextContent = textContent,
            ExtensionId = extensionId,
            Description = (DescriptionBox.Text ?? string.Empty).Trim()
        };

        ErrorText.Visibility = Visibility.Collapsed;
        DialogResult = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            this,
            "确认删除这条燕语吗？",
            "删除燕语",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        WasDeleted = true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.S &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            SaveButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void UpdateActionPanels()
    {
        var actionType = YanyuActionTypes.Normalize(ActionTypeComboBox.SelectedValue as string ?? ActionTypeComboBox.Text);
        TextContentPanel.Visibility = string.Equals(actionType, YanyuActionTypes.PasteText, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExtensionPanel.Visibility = string.Equals(actionType, YanyuActionTypes.RunExtension, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ExtensionSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_initializingExtensionSelection || _committingExtensionSelection)
        {
            return;
        }

        _selectedExtensionId = string.Empty;
        UpdateSelectedExtensionPreview(null);
        var keyword = (ExtensionSearchBox.Text ?? string.Empty).Trim();
        var filtered = keyword.Length == 0
            ? []
            : _extensionOptions
                .Where(item =>
                    item.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.ExtensionId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ExtensionListBox.ItemsSource = filtered;
        ExtensionListBox.SelectedIndex = filtered.Count > 0 ? 0 : -1;
        ExtensionListBox.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExtensionSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitHighlightedExtensionSelection();
            e.Handled = true;
            return;
        }

        if (e.Key != System.Windows.Input.Key.Down || ExtensionListBox.Visibility != Visibility.Visible || ExtensionListBox.Items.Count == 0)
        {
            return;
        }

        if (ExtensionListBox.SelectedIndex < 0)
        {
            ExtensionListBox.SelectedIndex = 0;
        }

        ExtensionListBox.Focus();
        if (ExtensionListBox.ItemContainerGenerator.ContainerFromIndex(ExtensionListBox.SelectedIndex) is System.Windows.Controls.ListBoxItem item)
        {
            item.Focus();
        }

        e.Handled = true;
    }

    private void ExtensionListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Highlighting a candidate is not a commit. Enter or double click commits.
    }

    private void ExtensionListBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitHighlightedExtensionSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            ExtensionListBox.Visibility = Visibility.Collapsed;
            ExtensionSearchBox.Focus();
            ExtensionSearchBox.CaretIndex = ExtensionSearchBox.Text.Length;
            e.Handled = true;
        }
    }

    private void ExtensionListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CommitHighlightedExtensionSelection();
    }

    private void CommitHighlightedExtensionSelection()
    {
        if (ExtensionListBox.SelectedItem is not ExtensionOption option)
        {
            return;
        }

        _committingExtensionSelection = true;
        _selectedExtensionId = option.ExtensionId;
        ExtensionSearchBox.Text = option.Label;
        ExtensionSearchBox.CaretIndex = ExtensionSearchBox.Text.Length;
        ExtensionListBox.Visibility = Visibility.Collapsed;
        UpdateSelectedExtensionPreview(option);
        ExtensionSearchBox.Focus();
        _committingExtensionSelection = false;
    }

    private string ResolveSelectedExtensionId()
    {
        if (!string.IsNullOrWhiteSpace(_selectedExtensionId))
        {
            var selected = _extensionOptions.FirstOrDefault(item => item.ExtensionId.Equals(_selectedExtensionId, StringComparison.OrdinalIgnoreCase));
            if (selected != null && selected.Label.Equals((ExtensionSearchBox.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return selected.ExtensionId;
            }
        }

        var text = (ExtensionSearchBox.Text ?? string.Empty).Trim();
        var match = _extensionOptions.FirstOrDefault(item =>
            item.Label.Equals(text, StringComparison.OrdinalIgnoreCase) ||
            item.ExtensionId.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            return string.Empty;
        }

        _selectedExtensionId = match.ExtensionId;
        UpdateSelectedExtensionPreview(match);
        return match.ExtensionId;
    }

    private void UpdateSelectedExtensionPreview(ExtensionOption? option)
    {
        SelectedExtensionIcon.DataContext = option;
        SelectedExtensionIcon.Visibility = option == null ? Visibility.Collapsed : Visibility.Visible;
        ExtensionSearchBox.Padding = option == null
            ? new Thickness(12, 8, 12, 8)
            : new Thickness(48, 8, 12, 8);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string BuildExtensionOptionDetail(CommandItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.OpenTarget))
        {
            return item.OpenTarget;
        }

        return item.Category;
    }
}
