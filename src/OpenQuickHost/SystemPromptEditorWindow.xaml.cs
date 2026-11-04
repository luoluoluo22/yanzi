using System.Windows;

namespace OpenQuickHost;

public partial class SystemPromptEditorWindow : Window
{
    public string PromptText { get; private set; } = string.Empty;

    public SystemPromptEditorWindow(string initialText)
    {
        InitializeComponent();
        // Scope shared theme resources to this dialog without overriding the host theme.
        var theme = AppSettingsStore.Load().ThemeMode;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(this,
            string.Equals(theme, "Light", System.StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark);
        PromptTextBox.Text = initialText;
        Loaded += (_, _) =>
        {
            PromptTextBox.Focus();
            PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        PromptText = PromptTextBox.Text;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
