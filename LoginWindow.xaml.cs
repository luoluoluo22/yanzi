using System.Windows;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class LoginWindow : Window
{
    public Func<string, string, Task<SendCodeResponse>>? SendRegistrationCodeAsync { private get; set; }

    public Func<string, string, string, string, Task>? RegisterAsyncHandler { private get; set; }

    public LoginWindow(string? username = null)
    {
        InitializeComponent();
        UsernameBox.Text = username ?? string.Empty;
        UpdateMode();
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(UsernameBox.Text))
            {
                UsernameBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }
        };
    }

    public string Username => UsernameBox.Text.Trim();

    public string Email => EmailBox.Text.Trim();

    public string Password => PasswordBox.Password;

    public string VerificationCode => CodeBox.Text.Trim();

    public bool RememberCredential => RememberCheckBox.IsChecked != false;

    public bool IsRegisterMode { get; private set; }

    public void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        StatusText.Visibility = Visibility.Visible;
    }

    public void ShowInfo(string message)
    {
        StatusText.Text = message;
        StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        StatusText.Visibility = Visibility.Visible;
    }

    private void SignInModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsRegisterMode = false;
        UpdateMode();
    }

    private void RegisterModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsRegisterMode = true;
        UpdateMode();
    }

    private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRegisterMode)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            ShowError("请先输入用户名。");
            return;
        }

        if (Username.Length < 3)
        {
            ShowError("用户名至少 3 位，只能使用字母、数字、下划线或短横线。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            ShowError("请先输入邮箱。");
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(Email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
        {
            ShowError("邮箱格式不正确。");
            return;
        }

        if (SendRegistrationCodeAsync == null)
        {
            ShowError("当前客户端未配置发送验证码能力。");
            return;
        }

        try
        {
            SendCodeButton.IsEnabled = false;
            var result = await SendRegistrationCodeAsync(Email, Username);
            if (!string.IsNullOrWhiteSpace(result.PreviewCode))
            {
                CodeBox.Text = result.PreviewCode;
                ShowInfo("验证码已生成，当前为开发模式，已自动填入。");
            }
            else
            {
                ShowInfo("验证码已发送，请查收邮箱。");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SendCodeButton.IsEnabled = true;
        }
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ShowError("请输入用户名。");
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ShowError("请输入密码。");
            return;
        }

        if (Password.Length < 8)
        {
            ShowError("密码至少 8 位。");
            return;
        }

        if (IsRegisterMode)
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                ShowError("请输入邮箱。");
                return;
            }

            if (string.IsNullOrWhiteSpace(VerificationCode))
            {
                ShowError("请输入邮箱验证码。");
                return;
            }

            if (RegisterAsyncHandler == null)
            {
                ShowError("当前客户端未配置注册能力。");
                return;
            }

            try
            {
                ConfirmButton.IsEnabled = false;
                await RegisterAsyncHandler(Email, Username, Password, VerificationCode);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }
            finally
            {
                ConfirmButton.IsEnabled = true;
            }
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateMode()
    {
        HeaderText.Text = IsRegisterMode ? "注册燕子账号" : "登录燕子云同步";
        DescriptionText.Text = IsRegisterMode
            ? "使用邮箱验证码完成注册，用户名会校验唯一性。"
            : "登录后可在多设备之间同步扩展和设置。";
        EmailPanel.Visibility = IsRegisterMode ? Visibility.Visible : Visibility.Collapsed;
        CodePanel.Visibility = IsRegisterMode ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Content = IsRegisterMode ? "注册并登录" : "登录";
        StatusText.Visibility = Visibility.Collapsed;
        SignInModeButton.Opacity = IsRegisterMode ? 0.7 : 1;
        RegisterModeButton.Opacity = IsRegisterMode ? 1 : 0.7;
    }
}
