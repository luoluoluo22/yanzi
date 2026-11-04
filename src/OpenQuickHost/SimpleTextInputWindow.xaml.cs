using System.Windows;

namespace OpenQuickHost;

public partial class SimpleTextInputWindow : Window
{
    private readonly bool _allowEmpty;

    public SimpleTextInputWindow(string title, string description, string initialValue, bool allowEmpty = false)
    {
        InitializeComponent();
        // Use shared window-scoped theme resources; leave the host theme untouched.
        var theme = AppSettingsStore.Load().ThemeMode;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(this,
            string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark);
        _allowEmpty = allowEmpty;
        Title = title;
        DescriptionText.Text = description;
        ValueBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string ValueText => ValueBox.Text.Trim();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(ValueText))
        {
            ErrorText.Text = "内容不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        DialogResult = true;
    }

    private void ValueBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OkButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
