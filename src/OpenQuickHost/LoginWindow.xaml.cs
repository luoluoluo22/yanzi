using System.Text.RegularExpressions;
using System.Windows;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class LoginWindow : Window
{
    public Func<string, string, Task<SendCodeResponse>>? SendRegistrationCodeAsync { private get; set; }

    public Func<string, Task<SendCodeResponse>>? SendPasswordResetCodeAsync { private get; set; }

    public Func<string, string, string, string, Task>? RegisterAsyncHandler { private get; set; }

    public Func<string, string, string, Task>? ResetPasswordAsyncHandler { private get; set; }

    public LoginWindow(string? email = null)
    {
        InitializeComponent();
        PrimaryInputBox.Text = email ?? string.Empty;
        UpdateMode(AuthDialogMode.SignIn);
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(PrimaryInputBox.Text))
            {
                PrimaryInputBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }
        };
    }

    public string LoginEmail => PrimaryInputBox.Text.Trim();

    public string Username => PrimaryInputBox.Text.Trim();

    public string Email => EmailBox.Text.Trim();

    public string Password => PasswordBox.Password;

    public string VerificationCode => CodeBox.Text.Trim();

    public bool RememberCredential => RememberCheckBox.IsChecked != false;

    public bool IsRegisterMode => Mode == AuthDialogMode.Register;

    public bool IsResetPasswordMode => Mode == AuthDialogMode.ResetPassword;

    private AuthDialogMode Mode { get; set; }

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
        UpdateMode(AuthDialogMode.SignIn);
    }

    private void RegisterModeButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateMode(AuthDialogMode.Register);
    }

    private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateMode(AuthDialogMode.ResetPassword);
    }

    private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SendCodeButton.IsEnabled = false;

            if (Mode == AuthDialogMode.Register)
            {
                ValidateUsername(Username);
                ValidateEmail(Email, emptyMessage: "请先输入邮箱。");

                if (SendRegistrationCodeAsync == null)
                {
                    ShowError("当前客户端未配置发送验证码能力。");
                    return;
                }

                var result = await SendRegistrationCodeAsync(Email, Username);
                PopulateVerificationCode(result, "注册");
                return;
            }

            if (Mode == AuthDialogMode.ResetPassword)
            {
                ValidateEmail(LoginEmail, emptyMessage: "请先输入注册邮箱。");

                if (SendPasswordResetCodeAsync == null)
                {
                    ShowError("当前客户端未配置找回密码能力。");
                    return;
                }

                var result = await SendPasswordResetCodeAsync(LoginEmail);
                PopulateVerificationCode(result, "重置密码");
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
        try
        {
            ConfirmButton.IsEnabled = false;

            if (Mode == AuthDialogMode.SignIn)
            {
                ValidateEmail(LoginEmail, emptyMessage: "请输入邮箱。");
                ValidatePassword(Password);
                DialogResult = true;
                return;
            }

            if (Mode == AuthDialogMode.Register)
            {
                ValidateUsername(Username);
                ValidateEmail(Email, emptyMessage: "请输入邮箱。");
                ValidatePassword(Password);

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

                await RegisterAsyncHandler(Email, Username, Password, VerificationCode);
                DialogResult = true;
                return;
            }

            ValidateEmail(LoginEmail, emptyMessage: "请输入注册邮箱。");
            ValidatePassword(Password);

            if (string.IsNullOrWhiteSpace(VerificationCode))
            {
                ShowError("请输入重置验证码。");
                return;
            }

            if (ResetPasswordAsyncHandler == null)
            {
                ShowError("当前客户端未配置找回密码能力。");
                return;
            }

            await ResetPasswordAsyncHandler(LoginEmail, Password, VerificationCode);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateMode(AuthDialogMode mode)
    {
        Mode = mode;
        HeaderText.Text = mode switch
        {
            AuthDialogMode.Register => "注册燕子账号",
            AuthDialogMode.ResetPassword => "找回燕子账号密码",
            _ => "登录燕子云同步"
        };
        DescriptionText.Text = mode switch
        {
            AuthDialogMode.Register => "使用邮箱验证码完成注册，用户名会校验唯一性。",
            AuthDialogMode.ResetPassword => "使用注册邮箱接收验证码，重置成功后会自动登录。",
            _ => "使用邮箱和密码登录后，可在多设备之间同步扩展和设置。"
        };
        PrimaryFieldLabel.Text = mode == AuthDialogMode.Register ? "用户名" : "邮箱";
        EmailPanel.Visibility = mode == AuthDialogMode.Register ? Visibility.Visible : Visibility.Collapsed;
        CodePanel.Visibility = mode == AuthDialogMode.SignIn ? Visibility.Collapsed : Visibility.Visible;
        ForgotPasswordButton.Visibility = mode == AuthDialogMode.SignIn ? Visibility.Visible : Visibility.Collapsed;
        RememberCheckBox.Visibility = Visibility.Visible;
        ConfirmButton.Content = mode switch
        {
            AuthDialogMode.Register => "注册并登录",
            AuthDialogMode.ResetPassword => "重置并登录",
            _ => "登录"
        };
        SendCodeButton.Content = mode == AuthDialogMode.Register ? "发送验证码" : "发送重置码";
        StatusText.Visibility = Visibility.Collapsed;
        SignInModeButton.Opacity = mode == AuthDialogMode.SignIn ? 1 : 0.7;
        RegisterModeButton.Opacity = mode == AuthDialogMode.Register ? 1 : 0.7;

        if (mode == AuthDialogMode.Register)
        {
            EmailBox.Focus();
        }
        else
        {
            PrimaryInputBox.Focus();
        }
    }

    private void PopulateVerificationCode(SendCodeResponse result, string purpose)
    {
        if (!string.IsNullOrWhiteSpace(result.PreviewCode))
        {
            CodeBox.Text = result.PreviewCode;
            ShowInfo($"{purpose}验证码已生成，当前为开发模式，已自动填入。");
            return;
        }

        ShowInfo($"{purpose}验证码已发送，请查收邮箱。");
    }

    private void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("请输入用户名。");
        }

        if (username.Length < 3 || !Regex.IsMatch(username, "^[a-zA-Z0-9_-]{3,32}$"))
        {
            throw new InvalidOperationException("用户名至少 3 位，只能使用字母、数字、下划线或短横线。");
        }
    }

    private void ValidateEmail(string email, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(emptyMessage);
        }

        if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
        {
            throw new InvalidOperationException("邮箱格式不正确。");
        }
    }

    private void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("请输入密码。");
        }

        if (password.Length < 8)
        {
            throw new InvalidOperationException("密码至少 8 位。");
        }
    }

    private enum AuthDialogMode
    {
        SignIn,
        Register,
        ResetPassword
    }
}
