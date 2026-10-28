using System.Windows;

namespace OpenQuickHost;

public partial class SystemPromptEditorWindow : Window
{
    public string PromptText { get; private set; } = string.Empty;

    public SystemPromptEditorWindow(string initialText)
    {
        InitializeComponent();
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
