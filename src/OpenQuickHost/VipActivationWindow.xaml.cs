using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OpenQuickHost.Sync;
using MediaColor = System.Windows.Media.Color;
using WpfClipboard = System.Windows.Clipboard;

namespace OpenQuickHost;

public partial class VipActivationWindow : Window
{
    private static readonly Regex LicenseRegex = new(
        @"^YZ-(1M|1Y|LIFE)-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private readonly CloudSyncClient _syncClient;
    private readonly Action? _onVipStatusChanged;
    private string? _lastAutoFilledCode;

    // 可配置的链动小铺或发卡商品链接，默认官方首页/商品页
    public static string PurchaseUrl { get; set; } = "https://wzyp.cn";

    public VipActivationWindow(CloudSyncClient syncClient, Action? onVipStatusChanged = null)
    {
        InitializeComponent();
        _syncClient = syncClient;
        _onVipStatusChanged = onVipStatusChanged;

        Loaded += async (_, _) =>
        {
            await RefreshStatusAsync();
            CheckClipboardForLicense();
        };

        Activated += (_, _) =>
        {
            CheckClipboardForLicense();
        };
    }

    private async Task RefreshStatusAsync()
    {
        AccountText.Text = _syncClient.CurrentUserLabel;

        if (!_syncClient.HasCredential)
        {
            VipStatusText.Text = "请先登录账号以绑定维护权益";
            VipStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 152, 0));
            return;
        }

        try
        {
            var status = await _syncClient.GetVipStatusAsync();
            if (status != null && status.IsVip)
            {
                if (status.VipType == "lifetime")
                {
                    VipStatusText.Text = "永久赞助者（全版本终身维护更新）";
                }
                else
                {
                    var expireStr = !string.IsNullOrWhiteSpace(status.VipExpireAt) && DateTime.TryParse(status.VipExpireAt, out var dt)
                        ? dt.ToLocalTime().ToString("yyyy-MM-dd")
                        : "长期有效";
                    VipStatusText.Text = $"赞助维护版（有效期至 {expireStr}，剩余 {status.DaysRemaining} 天）";
                }
                VipStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(76, 175, 80));
            }
            else
            {
                VipStatusText.Text = "社区免费版（基础功能永久免费）";
                VipStatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(176, 190, 197));
            }
        }
        catch (Exception ex)
        {
            VipStatusText.Text = "状态获取中...";
            Debug.WriteLine($"[VipActivation] Refresh status failed: {ex.Message}");
        }
    }

    private void CheckClipboardForLicense()
    {
        try
        {
            if (!WpfClipboard.ContainsText()) return;

            var text = WpfClipboard.GetText()?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var match = LicenseRegex.Match(text);
            if (match.Success)
            {
                var code = match.Value.ToUpperInvariant();
                if (code != _lastAutoFilledCode && string.IsNullOrWhiteSpace(LicenseCodeTextBox.Text))
                {
                    _lastAutoFilledCode = code;
                    LicenseCodeTextBox.Text = code;
                    ClipboardBanner.Visibility = Visibility.Visible;
                }
            }
        }
        catch
        {
            // 避免剪贴板被其他应用锁定导致异常
        }
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        var rawCode = LicenseCodeTextBox.Text?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            ShowMessage("请输入激活码", isError: true);
            LicenseCodeTextBox.Focus();
            return;
        }

        if (!_syncClient.HasCredential)
        {
            ShowMessage("请先在设置中登录燕子云端账号，以便将维护权益与您的账号绑定。", isError: true);
            return;
        }

        ActivateButton.IsEnabled = false;
        BuyButton.IsEnabled = false;
        ShowMessage("正在验证激活码并开通权益...", isError: false);

        try
        {
            var response = await _syncClient.RedeemLicenseAsync(rawCode);
            if (response != null && response.Ok)
            {
                ShowMessage(response.Message ?? "激活成功！感谢您对燕子开发维护的支持。", isError: false);
                ClipboardBanner.Visibility = Visibility.Collapsed;
                await RefreshStatusAsync();
                _onVipStatusChanged?.Invoke();
            }
            else
            {
                ShowMessage("激活失败，请检查激活码是否输入正确。", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
        finally
        {
            ActivateButton.IsEnabled = true;
            BuyButton.IsEnabled = true;
        }
    }

    private void BuyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PurchaseUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowMessage($"打开购买链接失败: {ex.Message}", isError: true);
        }
    }

    private void ShowMessage(string message, bool isError)
    {
        StatusMessageText.Text = message;
        StatusMessageText.Foreground = isError
            ? new SolidColorBrush(MediaColor.FromRgb(244, 67, 54))
            : new SolidColorBrush(MediaColor.FromRgb(76, 175, 80));
    }
}
