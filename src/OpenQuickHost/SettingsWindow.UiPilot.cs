using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yanzi.UI.Wpf;

namespace OpenQuickHost;

/// <summary>
/// Permanent use of Yanzi.UI.Wpf components in the actual SettingsWindow.
/// Original controls, bindings, command handlers and settings remain intact.
/// Shared UI styles are applied permanently to the real settings controls.
/// </summary>
public partial class SettingsWindow
{
    private static readonly string[] UnifiedSections =
    [
        nameof(GeneralSectionRoot),
        nameof(EnvironmentSectionRoot),
        nameof(SyncSectionRoot),
        nameof(YanmSectionRoot),
        nameof(YanwoSectionRoot),
        nameof(AboutSectionRoot),
        nameof(QuickPanelSectionRoot),
        nameof(MouseGesturesSectionRoot),
        nameof(RadialSectionRoot),
        nameof(YarnSelectSectionRoot),
        nameof(ExtensionsSectionRoot),
        nameof(AiSectionRoot)
    ];

    private bool _uiPilotReady;
    private bool _unifiedSettingsOn;
    private YanziSettingsSectionCard? _uiPilotCard;
    private readonly HashSet<FrameworkElement> _styledControls = [];

    private void InitializeSettingsUiPilot()
    {
        if (_uiPilotReady) return;
        if (HostRuntimeProfile.IsDevelopment && Environment.GetCommandLineArgs().Any(static arg =>
                arg.Equals("--settings-preview", StringComparison.OrdinalIgnoreCase)))
            Title = "燕子设置 · 统一 UI（开发版）";

        YanziUi.ApplyTo(this, PilotTheme());

        _uiPilotReady = true;

        foreach (var name in UnifiedSections)
        {
            if (FindName(name) is not FrameworkElement section)
                throw new InvalidOperationException($"Settings UI missing section: {name}");
            // Dynamic panels such as models/extensions can populate after initial load.
            section.IsVisibleChanged += (_, _) =>
            {
                if (!_unifiedSettingsOn || !section.IsVisible) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    () => StyleSettingsSubtree(section));
            };
        }
        ApplyUnifiedSettingsUi();
    }

    private YanziTheme PilotTheme()
    {
        if (string.Equals(_settings.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase))
            return YanziTheme.Light;
        if (string.Equals(_settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int light && light == 1)
                    return YanziTheme.Light;
            }
            catch (Exception) { /* Missing Windows personalization key: default dark. */ }
        }
        return YanziTheme.Dark;
    }

    private void UpdateSettingsUiPilotTheme()
    {
        if (_uiPilotReady)
            YanziUi.ApplyTo(this, PilotTheme());
    }

    private void ApplyUnifiedSettingsUi()
    {
        if (_unifiedSettingsOn) return;
        _unifiedSettingsOn = true;
        BuildGeneralSettingsCard();
        foreach (var name in UnifiedSections)
            if (FindName(name) is FrameworkElement section)
                StyleSettingsSubtree(section);
        HostAssets.AppendLog("Unified UI components applied permanently across all settings sections.");
    }
    private void BuildGeneralSettingsCard()
    {
        if (_uiPilotCard is not null) return;
        // Five real settings rows followed by their legacy dividers.
        var source = GeneralSectionRoot.Children.Cast<UIElement>().Take(10).ToArray();
        if (source.Length != 10 || Enumerable.Range(0, 5)
            .Any(i => source[2 * i] is not Grid || source[2 * i + 1] is not System.Windows.Shapes.Rectangle))
            throw new InvalidOperationException("General settings rows changed unexpectedly.");

        var section = new YanziSettingsSectionCard("基础设置", "主题、启动和托盘行为 · 实际设置")
        {
            Margin = new Thickness(0, 0, 0, 16)
        };
        // The card supplies the final edge; discard the original trailing divider.
        foreach (var child in source)
            GeneralSectionRoot.Children.Remove(child);
        for (int i = 0; i < 5; i++)
            section.AddRow(source[i * 2], i == 0 ? null : source[i * 2 - 1]);

        GeneralSectionRoot.Children.Insert(0, section);
        _uiPilotCard = section;
    }
    private void StyleSettingsSubtree(DependencyObject root)
    {
        int previous = _styledControls.Count;
        var legacySwitch = TryFindResource("RaycastSwitchStyle") as Style;
        var legacySurface = TryFindResource("SurfaceCard") as Style;
        var legacyPrimaryButton = TryFindResource("PrimaryBtn") as Style;
        var legacySecondaryButton = TryFindResource("SecondaryBtn") as Style;
        var legacyGhostButton = TryFindResource("GhostButtonStyle") as Style;
        var legacyInlineLinkButton = TryFindResource("InlineLinkButtonStyle") as Style;
        var legacyDarkComboBox = TryFindResource("DarkComboBoxStyle") as Style;

        Walk(root, current =>
        {
            switch (current)
            {
                case System.Windows.Controls.ComboBox combo when combo.Tag?.ToString() != "KeepNativeStyle"
                    && combo.ItemTemplate is null && combo.ItemTemplateSelector is null
                    && (legacyDarkComboBox is null || !ReferenceEquals(combo.Style, legacyDarkComboBox)):
                    SetStyleIfNeeded(combo, YanziUi.Styles.Select);
                    break;
                case System.Windows.Controls.PasswordBox password:
                    SetStyleIfNeeded(password, YanziUi.Styles.Password);
                    break;
                case System.Windows.Controls.TextBox input when input.AcceptsReturn && !input.IsReadOnly
                    && input.BorderThickness != new Thickness(0)
                    && input.Tag?.ToString() != "KeepNativeStyle":
                    SetStyleIfNeeded(input, YanziUi.Styles.TextareaSoft);
                    break;
                case System.Windows.Controls.TextBox input when !input.AcceptsReturn && !input.IsReadOnly
                    && input.BorderThickness != new Thickness(0)
                    && input.Width is not (>= 0 and < 78)
                    && input.Tag?.ToString() != "KeepNativeStyle":
                    SetStyleIfNeeded(input, YanziUi.Styles.Input);
                    break;
                case System.Windows.Controls.CheckBox check when legacySwitch is not null && ReferenceEquals(check.Style, legacySwitch):
                    SetStyleIfNeeded(check, YanziUi.Styles.SwitchShadcn);
                    break;
                case System.Windows.Controls.CheckBox check when check.ReadLocalValue(FrameworkElement.StyleProperty)
                    == DependencyProperty.UnsetValue:
                    var isRightAlignedToggle = check.HorizontalAlignment == System.Windows.HorizontalAlignment.Right;
                    SetStyleIfNeeded(check, isRightAlignedToggle
                        ? YanziUi.Styles.SwitchShadcn
                        : YanziUi.Styles.CheckBox);
                    break;
                case System.Windows.Controls.Border border when legacySurface is not null && ReferenceEquals(border.Style, legacySurface):
                    SetStyleIfNeeded(border, "Yanzi.Settings.Surface");
                    break;
                case System.Windows.Controls.Button button when button.Content is string buttonLabel && buttonLabel.Trim().Length >= 2
                    && button.Tag?.ToString() != "KeepNativeStyle"
                    && button.ReadLocalValue(System.Windows.Controls.Control.TemplateProperty) == DependencyProperty.UnsetValue:
                    if (legacyPrimaryButton is not null && ReferenceEquals(button.Style, legacyPrimaryButton))
                        SetStyleIfNeeded(button, YanziUi.Styles.DefaultButton);
                    else if (legacySecondaryButton is not null && ReferenceEquals(button.Style, legacySecondaryButton))
                        SetStyleIfNeeded(button, YanziUi.Styles.OutlineButton);
                    else if (legacyGhostButton is not null && ReferenceEquals(button.Style, legacyGhostButton))
                        SetStyleIfNeeded(button, YanziUi.Styles.GhostButton);
                    else if (legacyInlineLinkButton is not null && ReferenceEquals(button.Style, legacyInlineLinkButton))
                        SetStyleIfNeeded(button, YanziUi.Styles.LinkButton);
                    else if (button.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
                        SetStyleIfNeeded(button, YanziUi.Styles.OutlineButton);
                    break;
            }
        });

        int styled = _styledControls.Count - previous;
        if (styled > 0 && root is FrameworkElement section)
            HostAssets.AppendLog($"Unified UI section={section.Name}, newStyledControls={styled}.");
    }
    private void SetStyleIfNeeded(FrameworkElement element, string resourceKey)
    {
        if (_styledControls.Contains(element)) return;
        element.SetResourceReference(FrameworkElement.StyleProperty, resourceKey);
        _styledControls.Add(element);
    }
    private static void Walk(DependencyObject root, Action<DependencyObject> visit)
    {
        var seen = new HashSet<DependencyObject>();
        void Visit(DependencyObject current)
        {
            if (!seen.Add(current)) return;
            visit(current);
            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                Visit(child);
        }
        Visit(root);
    }
}
