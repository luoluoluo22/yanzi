using System.Windows;

namespace OpenQuickHost;

public partial class WebDavCredentialWindow : Window
{
    private readonly bool _requireUsername;

    public WebDavCredentialWindow(string username, bool requireUsername, string password = "")
    {
        InitializeComponent();
        _requireUsername = requireUsername;
        UsernameBox.Text = username;
        PasswordBox.Password = password;
        CurrentUsernameText.Text = username;
        UsernamePanel.Visibility = requireUsername ? Visibility.Visible : Visibility.Collapsed;
        CurrentUsernamePanel.Visibility = requireUsername ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            if (requireUsername)
            {
                UsernameBox.Focus();
                UsernameBox.SelectAll();
            }
            else
            {
                PasswordBox.Focus();
            }
        };
    }

    public string Username => _requireUsername ? UsernameBox.Text.Trim() : CurrentUsernameText.Text.Trim();

    public string Password => PasswordBox.Password;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_requireUsername && string.IsNullOrWhiteSpace(Username))
        {
            ErrorText.Text = "用户名不能为空。";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorText.Text = "密码不能为空。";
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
