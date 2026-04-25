using System.Windows;

namespace OpenQuickHost;

public partial class SimpleTextInputWindow : Window
{
    private readonly bool _allowEmpty;

    public SimpleTextInputWindow(string title, string description, string initialValue, bool allowEmpty = false)
    {
        InitializeComponent();
        _allowEmpty = allowEmpty;
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
        if (!_allowEmpty && string.IsNullOrWhiteSpace(ValueText))
        {
            ErrorText.Text = "内容不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
