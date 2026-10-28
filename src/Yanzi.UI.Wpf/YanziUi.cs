using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Yanzi.UI.Wpf;

public enum YanziTheme { Dark, Light }

/// <summary>
/// Opt-in per-window design system. Never edits Application.Current.Resources:
/// legacy extensions retain their own styles and theme behavior.
/// </summary>
public static class YanziUi
{
    private sealed class InstalledResources
    {
        public required ResourceDictionary Tokens { get; init; }
        public required ResourceDictionary Controls { get; init; }
        public YanziTheme Theme { get; set; }
    }

    private static readonly ConditionalWeakTable<Window, InstalledResources> Installed = new();

    public static class Styles
    {
        public const string DefaultButton = "Yanzi.Button.Default";
        public const string OutlineButton = "Yanzi.Button.Outline";
        public const string GhostButton = "Yanzi.Button.Ghost";
        public const string LinkButton = "Yanzi.Button.Link";
        public const string DestructiveButton = "Yanzi.Button.Destructive";
        public const string PillDefaultButton = "Yanzi.Button.Pill.Default";
        public const string PillSecondaryButton = "Yanzi.Button.Pill.Secondary";
        public const string PillOutlineButton = "Yanzi.Button.Pill.Outline";
        public const string PillGhostButton = "Yanzi.Button.Pill.Ghost";
        public const string PillDestructiveButton = "Yanzi.Button.Pill.Destructive";
        public const string ChipDefaultButton = "Yanzi.Button.Chip.Default";
        public const string ChipSecondaryButton = "Yanzi.Button.Chip.Secondary";
        public const string ChipOutlineButton = "Yanzi.Button.Chip.Outline";
        public const string SegmentedFirstButton = "Yanzi.Button.Segmented.First";
        public const string SegmentedBaseButton = "Yanzi.Button.Segmented.Base";
        public const string SegmentedLastButton = "Yanzi.Button.Segmented.Last";
        public const string Badge = "Yanzi.Badge";
        public const string BadgeGeistPreview = "Yanzi.Badge.PreviewGeist";
        public const string Textarea = "Yanzi.Textarea";
        public const string Password = "Yanzi.Password";
        public const string Select = "Yanzi.Select";
        public const string Slider = "Yanzi.Slider";
        public const string Progress = "Yanzi.Progress";
        public const string Radio = "Yanzi.Radio"; // legacy WPF RadioButton; new callers use RadioCustom
        public const string RadioCustom = "Yanzi.Radio.Custom";
        public const string CheckBoxPreview = "Yanzi.CheckBox.Preview";
        public const string Tabs = "Yanzi.Tabs";
        public const string TabItem = "Yanzi.TabItem";
        public const string Expander = "Yanzi.Expander";
        // Legacy native WPF calendar/date-picker skins, retained for existing consumers.
        // New components should use YanziCalendarMonth and YanziDatePicker (includes custom popup).
        public const string Calendar = "Yanzi.Calendar";
        public const string DatePicker = "Yanzi.DatePicker";
        public const string Tooltip = "Yanzi.Tooltip";
        public const string DataGrid = "Yanzi.DataGrid";
        // Compatibility aliases for Yanzi UI 0.1 clients.
        public const string PrimaryButton = "Yanzi.Button.Primary";
        public const string SecondaryButton = "Yanzi.Button.Secondary";
        public const string DangerButton = "Yanzi.Button.Danger";
        public const string Kbd = "Yanzi.Kbd";
        public const string KbdGroup = "Yanzi.KbdGroup";
        public const string ResizableHandle = "Yanzi.ResizableHandle";
        public const string ResizableHandleVertical = "Yanzi.ResizableHandle.Vertical";
        public const string Input = "Yanzi.Input";
        public const string InputSoft = "Yanzi.Input.Soft";
        public const string TextareaSoft = "Yanzi.Textarea.Soft";
        public const string Toggle = "Yanzi.Toggle";
        public const string SwitchShadcn = "Yanzi.Switch.Shadcn";
        public const string ToggleButton = "Yanzi.ToggleButton";
        public const string CheckBox = "Yanzi.CheckBox";
        public const string Menu = "Yanzi.Menu";
        public const string DropdownAction = "Yanzi.Dropdown.Action";
        public const string Menubar = "Yanzi.Menubar";
        public const string MenuItem = "Yanzi.MenuItem";
        public const string List = "Yanzi.List";
        public const string Scrollbar = "Yanzi.Scrollbar";
        public const string ListItem = "Yanzi.ListItem";
        public const string Loading = "Yanzi.Loading";
        public const string Card = "Yanzi.Card";
        public const string TextSecondary = "Yanzi.Text.Secondary";
    }

    public static YanziTheme GetTheme(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.VerifyAccess();
        return Installed.TryGetValue(window, out var resources) ? resources.Theme : YanziTheme.Dark;
    }

    public static void ApplyTo(Window window, YanziTheme theme = YanziTheme.Dark)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.VerifyAccess();

        if (Installed.TryGetValue(window, out var existing))
        {
            if (existing.Theme != theme)
            {
                existing.Tokens.Source = ThemeUri(theme);
                existing.Theme = theme;
            }
            return;
        }

        var tokens = new ResourceDictionary { Source = ThemeUri(theme) };
        var controls = new ResourceDictionary
        {
            Source = new Uri("/Yanzi.UI.Wpf;component/Themes/Controls.xaml", UriKind.Relative)
        };

        window.Resources.MergedDictionaries.Add(tokens);
        window.Resources.MergedDictionaries.Add(controls);
        Installed.Add(window, new InstalledResources { Tokens = tokens, Controls = controls, Theme = theme });
    }

    /// <summary>Use after ApplyTo to keep a local control bound to an updatable resource key.</summary>
    public static T WithStyle<T>(T control, string styleKey) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(styleKey);
        control.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return control;
    }

    private static Uri ThemeUri(YanziTheme theme) =>
        new($"/Yanzi.UI.Wpf;component/Themes/Tokens.{theme}.xaml", UriKind.Relative);
}
