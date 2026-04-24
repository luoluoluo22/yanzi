using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace OpenQuickHost;

public partial class HotkeyCaptureWindow : Window
{
    private readonly bool _allowEmpty;

    public HotkeyCaptureWindow(string title, string description, string? initialValue = null, bool allowEmpty = false)
    {
        InitializeComponent();
        _allowEmpty = allowEmpty;
        Title = title;
        TitleText.Text = title;
        DescriptionText.Text = description;
        ClearButton.Visibility = allowEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(initialValue))
        {
            ShortcutText = initialValue.Trim();
            CapturedHotkeyText.Text = ShortcutText;
            ConfirmButton.IsEnabled = true;
        }

        Loaded += (_, _) => Focus();
    }

    public string ShortcutText { get; private set; } = string.Empty;

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        if (e.Key is Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            ErrorText.Text = "请至少包含 Ctrl、Alt、Shift 或 Win 中的一个修饰键。";
            ErrorText.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
            e.Handled = true;
            return;
        }

        ShortcutText = BuildShortcutText(modifiers, key);
        CapturedHotkeyText.Text = ShortcutText;
        ConfirmButton.IsEnabled = true;
        e.Handled = true;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        ShortcutText = string.Empty;
        CapturedHotkeyText.Text = "请直接按下新的组合键";
        ConfirmButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty)
        {
            return;
        }

        ShortcutText = string.Empty;
        CapturedHotkeyText.Text = "当前将清空快捷键";
        ConfirmButton.IsEnabled = true;
        ErrorText.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(ShortcutText))
        {
            ErrorText.Text = "请先录制一个快捷键。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }

    private static string BuildShortcutText(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            Key.Capital => "CapsLock",
            _ => key.ToString()
        };
    }
}
