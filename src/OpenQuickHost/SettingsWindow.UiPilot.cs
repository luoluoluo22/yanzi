using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yanzi.UI.Wpf;

namespace OpenQuickHost;

/// <summary>
/// Progressive adoption of Yanzi.UI.Wpf in the actual SettingsWindow.
/// Original controls, bindings, command handlers and settings remain intact.
/// The non-persistent preview toggle reversibly changes only visual Styles.
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
    private readonly List<UIElement> _originalGeneralRows = [];
    private readonly Dictionary<FrameworkElement, object> _originalStyles = new();

    private void InitializeSettingsUiPilot()
    {
        if (_uiPilotReady) return;
        if (HostRuntimeProfile.IsDevelopment && Environment.GetCommandLineArgs().Any(static arg =>
                arg.Equals("--settings-preview", StringComparison.OrdinalIgnoreCase)))
            Title = "燕子设置 · 统一 UI 试用（开发版）";

        YanziUi.ApplyTo(this, PilotTheme());
        YanziUi.WithStyle(UnifiedUiPreviewToggle, YanziUi.Styles.SwitchShadcn);
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
        SetSettingsUiPilot(UnifiedUiPreviewToggle.IsChecked == true);
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

    private void UnifiedUiPreviewToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_uiPilotReady)
            SetSettingsUiPilot(UnifiedUiPreviewToggle.IsChecked == true);
    }

    private void SetSettingsUiPilot(bool enabled)
    {
        if (enabled == _unifiedSettingsOn) return;
        _unifiedSettingsOn = enabled;
        if (enabled)
        {
            BuildGeneralSettingsCard();
            foreach (var name in UnifiedSections)
                if (FindName(name) is FrameworkElement section)
                    StyleSettingsSubtree(section);
            HostAssets.AppendLog("Unified UI settings enabled across all sections (styles only).");
        }
        else
        {
            RestoreStyles();
            RestoreGeneralSettingsRows();
            HostAssets.AppendLog("Unified UI settings disabled: original controls and styles restored.");
        }
    }

    private void BuildGeneralSettingsCard()
    {
        if (_uiPilotCard is not null) return;
        // Ten real controls: 5 setting rows followed by their legacy dividers.
        var source = GeneralSectionRoot.Children.Cast<UIElement>().Skip(1).Take(10).ToArray();
        if (source.Length != 10 || Enumerable.Range(0, 5)
            .Any(i => source[2 * i] is not Grid || source[2 * i + 1] is not System.Windows.Shapes.Rectangle))
            throw new InvalidOperationException("General settings rows changed unexpectedly.");

        _originalGeneralRows.Clear();
        _originalGeneralRows.AddRange(source);
        var section = new YanziSettingsSectionCard("基础设置", "主题、启动和托盘行为 · 实际设置")
        {
            Margin = new Thickness(0, 0, 0, 16)
        };
        // Remove the original trailing divider. The rounded card border now
        // provides the final visual edge; there is no detached bottom line.
        foreach (var child in source)
            GeneralSectionRoot.Children.Remove(child);
        for (int i = 0; i < 5; i++)
            section.AddRow(source[i * 2], i == 0 ? null : source[i * 2 - 1]);

        GeneralSectionRoot.Children.Insert(1, section);
        _uiPilotCard = section;
    }

    private void RestoreGeneralSettingsRows()
    {
        if (_uiPilotCard is null) return;
        GeneralSectionRoot.Children.Remove(_uiPilotCard);
        int index = 1;
        foreach (var child in _originalGeneralRows)
        {
            if (_uiPilotCard.Rows.Children.Contains(child))
                _uiPilotCard.Rows.Children.Remove(child);
            GeneralSectionRoot.Children.Insert(index++, child);
        }
        _originalGeneralRows.Clear();
        _uiPilotCard = null;
    }

    private void StyleSettingsSubtree(DependencyObject root)
    {
        int previous = _originalStyles.Count;
        var legacySwitch = TryFindResource("RaycastSwitchStyle") as Style;
        var legacySurface = TryFindResource("SurfaceCard") as Style;
        Walk(root, current =>
        {
            switch (current)
            {
                case System.Windows.Controls.ComboBox combo when
                    combo.ItemContainerStyle is null && combo.Tag?.ToString() != "KeepNativeStyle":
                    SetStyleIfNeeded(combo, YanziUi.Styles.Select);
                    break;
                case PasswordBox password:
                    SetStyleIfNeeded(password, YanziUi.Styles.Password);
                    break;
                case System.Windows.Controls.TextBox input when !input.AcceptsReturn && !input.IsReadOnly
                    && input.Width is not (>= 0 and < 78):
                    SetStyleIfNeeded(input, YanziUi.Styles.Input);
                    break;
                case System.Windows.Controls.CheckBox check when ReferenceEquals(check.Style, legacySwitch):
                    SetStyleIfNeeded(check, YanziUi.Styles.SwitchShadcn);
                    break;
                case Border border when ReferenceEquals(border.Style, legacySurface):
                    SetStyleIfNeeded(border, "Yanzi.Settings.Surface");
                    break;
                case System.Windows.Controls.Button button when button.Content is string label
                    && label.Length >= 2 && button.Width is >= 100
                    && button.Template is null:
                    SetStyleIfNeeded(button, YanziUi.Styles.OutlineButton);
                    break;
            }
        });
        int styled = _originalStyles.Count - previous;
        if (styled > 0 && root is FrameworkElement section)
            HostAssets.AppendLog($"Unified UI section={section.Name}, newStyledControls={styled}.");
    }

    private void SetStyleIfNeeded(FrameworkElement element, string resourceKey)
    {
        if (_originalStyles.ContainsKey(element)) return;
        _originalStyles.Add(element, element.ReadLocalValue(FrameworkElement.StyleProperty));
        element.SetResourceReference(FrameworkElement.StyleProperty, resourceKey);
    }

    private void RestoreStyles()
    {
        foreach (var entry in _originalStyles)
        {
            if (entry.Value == DependencyProperty.UnsetValue)
                entry.Key.ClearValue(FrameworkElement.StyleProperty);
            else
                entry.Key.SetValue(FrameworkElement.StyleProperty, entry.Value);
        }
        _originalStyles.Clear();
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
