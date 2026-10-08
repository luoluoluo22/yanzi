using System.Windows;
using System.Windows.Controls;
using Yanzi.UI.Wpf;

namespace OpenQuickHost;

/// <summary>
/// First, reversible in-place adoption of the shared WPF design library in
/// the real General settings page. All original bindings and event handlers
/// stay attached to the same controls; the preview switch never writes settings.
/// </summary>
public partial class SettingsWindow
{
    private bool _uiPilotReady;
    private YanziCard? _uiPilotCard;
    private StackPanel? _uiPilotRows;
    private readonly List<UIElement> _uiPilotOriginalRows = [];
    private readonly List<(FrameworkElement Element, object OldStyle)> _uiPilotStyled = [];

    private void InitializeSettingsUiPilot()
    {
        if (_uiPilotReady) return;
        if (HostRuntimeProfile.IsDevelopment && Environment.GetCommandLineArgs().Any(static arg =>
                arg.Equals("--settings-preview", StringComparison.OrdinalIgnoreCase)))
            Title = "燕子设置 · 统一 UI 试用（开发版）";
        YanziUi.ApplyTo(this, PilotTheme());
        // The pilot toggle is deliberately not persisted in AppSettings.
        YanziUi.WithStyle(UnifiedUiPreviewToggle, YanziUi.Styles.SwitchShadcn);
        _uiPilotReady = true;
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
                if (key?.GetValue("AppsUseLightTheme") is int { } light && light == 1)
                    return YanziTheme.Light;
            }
            catch (Exception) { /* No OS personalization key: use dark. */ }
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
        if (!_uiPilotReady) return;
        SetSettingsUiPilot(UnifiedUiPreviewToggle.IsChecked == true);
    }

    private void SetSettingsUiPilot(bool enabled)
    {
        // First ten original children are the real Theme + four startup/system
        // settings and their separators. We never duplicate a bound control.
        if (enabled)
        {
            if (_uiPilotCard is not null) return;
            var source = GeneralSectionRoot.Children
                .Cast<UIElement>()
                .Skip(1) // immutable preview header
                .Take(10)
                .ToArray();
            if (source.Length != 10 || source[0] is not Grid ||
                source[2] is not Grid || source[4] is not Grid ||
                source[6] is not Grid || source[8] is not Grid)
            {
                HostAssets.AppendLog("Settings UI pilot aborted: original general layout changed.");
                return;
            }

            _uiPilotOriginalRows.Clear();
            _uiPilotOriginalRows.AddRange(source);
            _uiPilotRows = new StackPanel();
            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Text = "基础设置", FontSize = 16, FontWeight = FontWeights.SemiBold
            });
            var hint = new TextBlock
            {
                Text = "主题、启动和托盘行为 · 实际设置",
                FontSize = 12, Margin = new Thickness(0, 5, 0, 0)
            };
            ((TextBlock)header.Children[0]).SetResourceReference(
                TextBlock.ForegroundProperty, "Yanzi.Color.Foreground");
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Yanzi.Color.MutedForeground");
            header.Children.Add(hint);
            _uiPilotCard = new YanziCard { Margin = new Thickness(0, 0, 0, 16) };
            _uiPilotCard.SetHeader(header);
            _uiPilotCard.SetBody(_uiPilotRows);

            foreach (var child in source)
            {
                GeneralSectionRoot.Children.Remove(child);
                _uiPilotRows.Children.Add(child);
            }
            GeneralSectionRoot.Children.Insert(1, _uiPilotCard);

            // Preserve both saved values and existing two-way bindings/events.
            // Only Style is swapped; all controls stay the same WPF instances.
            foreach (var index in new[] { 0, 2, 4, 6, 8 })
            {
                if (source[index] is not Grid row) continue;
                FrameworkElement? control = row.Children.OfType<System.Windows.Controls.ComboBox>().FirstOrDefault()
                    ?? (FrameworkElement?)row.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault();
                if (control is null) continue;
                _uiPilotStyled.Add((control,
                    control.ReadLocalValue(FrameworkElement.StyleProperty)));
                control.SetResourceReference(FrameworkElement.StyleProperty,
                    control is System.Windows.Controls.ComboBox ? YanziUi.Styles.Select : YanziUi.Styles.SwitchShadcn);
            }
            HostAssets.AppendLog("Settings UI pilot enabled: five real settings controls, no state mutation.");
        }
        else
        {
            if (_uiPilotCard is null || _uiPilotRows is null) return;
            foreach (var (control, original) in _uiPilotStyled)
            {
                if (original == DependencyProperty.UnsetValue)
                    control.ClearValue(FrameworkElement.StyleProperty);
                else
                    control.SetValue(FrameworkElement.StyleProperty, original);
            }
            _uiPilotStyled.Clear();

            GeneralSectionRoot.Children.Remove(_uiPilotCard);
            int insertAt = 1;
            foreach (var child in _uiPilotOriginalRows)
            {
                _uiPilotRows.Children.Remove(child);
                GeneralSectionRoot.Children.Insert(insertAt++, child);
            }
            _uiPilotOriginalRows.Clear();
            _uiPilotRows = null;
            _uiPilotCard = null;
            HostAssets.AppendLog("Settings UI pilot disabled; original setting controls and styles restored.");
        }
    }
}
