using System.Windows;

namespace OpenQuickHost;

public partial class SimpleTextInputWindow : Window
{
    public SimpleTextInputWindow(string title, string description, string initialValue)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
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
        if (string.IsNullOrWhiteSpace(ValueText))
        {
            ErrorText.Text = "内容不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
