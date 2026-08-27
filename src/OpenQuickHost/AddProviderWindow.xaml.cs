using System.Windows;

namespace OpenQuickHost;

public partial class AddProviderWindow : Window
{
    public string ProviderName { get; private set; } = string.Empty;
    public string ProviderType { get; private set; } = "OpenAI";

    public AddProviderWindow()
    {
        InitializeComponent();
        ProviderTypeBox.ItemsSource = new string[]
        {
            "OpenAI",
            "OpenAI-Response",
            "Gemini",
            "Anthropic",
            "Azure OpenAI",
            "New API",
            "CherryIN",
            "Ollama"
        };
        ProviderTypeBox.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            ProviderNameBox.Focus();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ProviderNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "提供商名称不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        ProviderName = name;
        ProviderType = ProviderTypeBox.SelectedItem as string ?? "OpenAI";
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ProviderNameBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OkButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
