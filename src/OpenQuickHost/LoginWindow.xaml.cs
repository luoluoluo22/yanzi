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

    public Func<string, string, Task>? SignInAsyncHandler { private get; set; }

    private bool _isCountingDown;
    private System.Windows.Threading.DispatcherTimer? _sendCodeTimer;
    private int _sendCodeCountdown;
    private bool _isSyncingPassword;

    private async void StartRateLimitCountdown(int seconds)
    {
        ConfirmButton.IsEnabled = false;
        var remaining = seconds;
        while (remaining > 0)
        {
            ShowError($"请求过于频繁，请在 {remaining} 秒后重试。");
            await Task.Delay(1000);
            remaining--;
        }
        StatusText.Visibility = Visibility.Collapsed;
        _isCountingDown = false;
        ConfirmButton.IsEnabled = true;
    }

    public LoginWindow(string? email = null)
    {
        InitializeComponent();
        App.EnableSilentLoading(this);
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
        StatusText.Text = TranslateErrorMessage(message);
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
        bool isSuccess = false;
        try
        {
            SendCodeButton.IsEnabled = false;
            CloudSyncDiagnostics.Log(
                "LoginWindow",
                "Send code requested",
                ("mode", Mode.ToString()),
                ("username", Username),
                ("email", Mode == AuthDialogMode.Register ? Email : LoginEmail));

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
                isSuccess = true;
                StartSendCodeCountdown();
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
                isSuccess = true;
                StartSendCodeCountdown();
            }
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Log(
                "LoginWindow",
                "Send code failed",
                ("mode", Mode.ToString()),
                ("error", ex.Message));
            ShowError(ex.Message);
        }
        finally
        {
            if (!isSuccess)
            {
                SendCodeButton.IsEnabled = true;
            }
        }
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConfirmButton.IsEnabled = false;
            CloudSyncDiagnostics.Log(
                "LoginWindow",
                "Confirm requested",
                ("mode", Mode.ToString()),
                ("remember", RememberCredential),
                ("loginEmail", LoginEmail),
                ("registerEmail", Email),
                ("hasPassword", !string.IsNullOrWhiteSpace(Password)),
                ("hasCode", !string.IsNullOrWhiteSpace(VerificationCode)));

            if (Mode == AuthDialogMode.SignIn)
            {
                ValidateEmail(LoginEmail, emptyMessage: "请输入邮箱。");
                ValidatePassword(Password);
                if (SignInAsyncHandler != null)
                {
                    ShowInfo("正在登录...");
                    await SignInAsyncHandler(LoginEmail, Password);
                }
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
            CloudSyncDiagnostics.Log(
                "LoginWindow",
                "Confirm failed",
                ("mode", Mode.ToString()),
                ("error", ex.Message));
            var isRateLimit = false;
            var seconds = 60;
            var match = Regex.Match(ex.Message ?? string.Empty, @"请在\s*(\d+)\s*秒后重试");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsedSec))
            {
                isRateLimit = true;
                seconds = parsedSec;
            }
            else if ((ex.Message ?? string.Empty).Contains("429") || (ex.Message ?? string.Empty).Contains("Too Many Requests"))
            {
                isRateLimit = true;
            }

            if (isRateLimit)
            {
                _isCountingDown = true;
                StartRateLimitCountdown(seconds);
            }
            else
            {
                ShowError(ex.Message ?? "未知错误。");
            }
        }
        finally
        {
            if (!_isCountingDown)
            {
                ConfirmButton.IsEnabled = true;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateMode(AuthDialogMode mode)
    {
        Mode = mode;
        CloudSyncDiagnostics.Log("LoginWindow", "Mode changed", ("mode", mode.ToString()));
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
        SignInModeButton.Style = (Style)FindResource(mode == AuthDialogMode.SignIn ? "PrimaryBtn" : "SecondaryBtn");
        RegisterModeButton.Style = (Style)FindResource(mode == AuthDialogMode.Register ? "PrimaryBtn" : "SecondaryBtn");
        SignInModeButton.Opacity = mode == AuthDialogMode.SignIn ? 1 : 0.88;
        RegisterModeButton.Opacity = mode == AuthDialogMode.Register ? 1 : 0.88;

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

        if (!Regex.IsMatch(username, @"^[\p{L}\p{N}_-]{3,32}$"))
        {
            throw new InvalidOperationException("用户名需 3-32 位，可使用中文、字母、数字、下划线或短横线。");
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

    private void StartSendCodeCountdown()
    {
        if (_sendCodeTimer != null)
        {
            _sendCodeTimer.Stop();
        }

        _sendCodeCountdown = 15;
        SendCodeButton.IsEnabled = false;
        SendCodeButton.Content = $"{_sendCodeCountdown}s 后重试";

        _sendCodeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sendCodeTimer.Tick += (s, e) =>
        {
            _sendCodeCountdown--;
            if (_sendCodeCountdown <= 0)
            {
                _sendCodeTimer.Stop();
                SendCodeButton.IsEnabled = true;
                SendCodeButton.Content = "发送验证码";
            }
            else
            {
                SendCodeButton.Content = $"{_sendCodeCountdown}s 后重试";
            }
        };
        _sendCodeTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_sendCodeTimer != null)
        {
            _sendCodeTimer.Stop();
            _sendCodeTimer = null;
        }
        base.OnClosed(e);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPassword) return;
        _isSyncingPassword = true;
        try
        {
            PasswordTextBox.Text = PasswordBox.Password;
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }

    private void PasswordTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isSyncingPassword) return;
        _isSyncingPassword = true;
        try
        {
            PasswordBox.Password = PasswordTextBox.Text;
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }

    private void ShowPasswordToggle_Checked(object sender, RoutedEventArgs e)
    {
        // 睁开眼睛
        EyeIconPath.Data = System.Windows.Media.Geometry.Parse("M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z");

        PasswordTextBox.Text = PasswordBox.Password;
        PasswordBox.Visibility = Visibility.Collapsed;
        PasswordTextBox.Visibility = Visibility.Visible;
        PasswordTextBox.Focus();
        if (PasswordTextBox.Text.Length > 0)
        {
            PasswordTextBox.SelectionStart = PasswordTextBox.Text.Length;
        }
    }

    private void ShowPasswordToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        // 闭合眼睛
        EyeIconPath.Data = System.Windows.Media.Geometry.Parse("M12 6c3.79 0 7.17 2.13 8.82 5.5-.59 1.22-1.42 2.27-2.41 3.12l1.41 1.41c1.39-1.23 2.49-2.77 3.18-4.53C21.27 6.61 17 3.5 12 3.5c-1.25 0-2.45.2-3.57.57L9.9 5.54c.68-.35 1.42-.54 2.1-.54zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-3.11l1.9 1.9c.35.46.59 1 .69 1.59l-2.59-2.59c.59-.1 1.13-.34 1.59-.69z");

        PasswordBox.Password = PasswordTextBox.Text;
        PasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        PasswordBox.Focus();
    }

    private static string TranslateErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未知错误。";

        if (message.Contains("configured HttpClient.Timeout") || message.Contains("The request was canceled") || message.Contains("TaskCanceledException") || message.Contains("OperationCanceledException"))
        {
            return "网络连接超时（15秒限制），请检查网络连接或代理设置并重试。";
        }
        if (message.Contains("SSL connection could not be established") || message.Contains("AuthenticationException") || message.Contains("unexpected EOF") || message.Contains("transport stream"))
        {
            return "网络 SSL 握手失败，请检查网络代理/VPN设置或防火墙是否拦截了加密连接。";
        }
        if (message.Contains("No such host is known") || message.Contains("HttpRequestException") || message.Contains("SocketException") || message.Contains("Cloud request failed"))
        {
            return "网络连接失败，无法连接到同步服务器，请检查网络。";
        }

        var normalized = message.Trim();
        if (normalized.Contains("Username already exists")) return "该用户名已被占用。";
        if (normalized.Contains("Email already exists")) return "该邮箱已被注册。";
        if (normalized.Contains("Email verification is required")) return "需要进行邮箱验证。";
        if (normalized.Contains("Verification code does not match")) return "验证码与该账号不匹配。";
        if (normalized.Contains("Verification code expired")) return "验证码已过期，请重新获取。";
        if (normalized.Contains("Invalid verification code")) return "验证码无效或错误。";
        if (normalized.Contains("User does not exist")) return "该账号用户不存在。";
        if (normalized.Contains("Invalid email or password")) return "邮箱或密码错误，请重新输入。";
        if (normalized.Contains("Email does not exist")) return "该邮箱不存在。";
        if (normalized.Contains("Password reset verification is required")) return "需要密码重置验证。";
        if (normalized.Contains("Invalid credentials or token")) return "无效的登录凭证。";
        if (normalized.Contains("Token expired")) return "登录已失效，请重新登录。";
        if (normalized.Contains("Username must be 3-32 chars")) return "用户名应为3-32位（包含字母、数字、下划线、中划线）。";
        if (normalized.Contains("Email format is invalid")) return "邮箱格式不正确。";
        if (normalized.Contains("Verification code must be 6 digits")) return "验证码必须为6位数字。";
        if (normalized.Contains("Password must be 8-128 characters")) return "密码长度必须在8-128位之间。";
        if (normalized.Contains("Email provider is not configured")) return "服务器发送验证码邮件失败（邮件服务未配置）。";
        if (normalized.Contains("Too Many Requests") || normalized.Contains("429")) return "请求过于频繁，请稍后重试。";

        return message;
    }

    private enum AuthDialogMode
    {
        SignIn,
        Register,
        ResetPassword
    }
}
