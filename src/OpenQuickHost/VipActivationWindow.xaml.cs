using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

    // 链动小铺赞助维护商品购买直达链接
    public static string PurchaseUrl { get; set; } = "https://wzyp.cn/shop/4AOUCE2B";

    public bool IsActivatedSuccessfully { get; private set; }

    public VipActivationWindow(CloudSyncClient syncClient, Action? onVipStatusChanged = null, string? currentVipStatus = null)
    {
        HostAssets.AppendLog($"[VipActivationWindow] Ctor called. syncClient is {(syncClient != null ? "Ready" : "Null")}, currentVip={currentVipStatus}");
        InitializeComponent();
        _syncClient = syncClient;
        _onVipStatusChanged = onVipStatusChanged;

        if (!string.IsNullOrWhiteSpace(currentVipStatus) && currentVipStatus != "激活VIP")
        {
            CurrentVipBadgeText.Text = currentVipStatus;
            CurrentVipBadge.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            HostAssets.AppendLog($"[VipActivationWindow] Loaded event. UserLabel={_syncClient?.CurrentUserLabel}, HasCredential={_syncClient?.HasCredential}");
            CheckClipboardForLicense();
        };

        Activated += (_, _) =>
        {
            CheckClipboardForLicense();
        };
    }

    private void LicenseCodeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ActivateButton.IsEnabled = !string.IsNullOrWhiteSpace(LicenseCodeTextBox.Text);
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
        HostAssets.AppendLog($"[VipActivationWindow] ActivateButton_Click: codePrefix={(rawCode?.Length >= 6 ? rawCode[..6] : "empty")}***");
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            ShowMessage("请输入激活码", isError: true);
            LicenseCodeTextBox.Focus();
            return;
        }

        if (!_syncClient.HasCredential)
        {
            HostAssets.AppendLog("[VipActivationWindow] ActivateButton_Click aborted: client has no credential.");
            ShowMessage("请先在设置中登录燕子云端账号，以便将维护权益与您的账号绑定。", isError: true);
            return;
        }

        ActivateButton.IsEnabled = false;
        BuyButton.IsEnabled = false;
        ShowMessage("正在验证激活码并开通权益...", isError: false);

        try
        {
            HostAssets.AppendLog($"[VipActivationWindow] Calling RedeemLicenseAsync for {rawCode}...");
            var response = await _syncClient.RedeemLicenseAsync(rawCode);
            HostAssets.AppendLog($"[VipActivationWindow] RedeemLicenseAsync response: ok={response?.Ok}, msg={response?.Message}");
            if (response != null && response.Ok)
            {
                IsActivatedSuccessfully = true;
                ShowMessage(response.Message ?? "激活成功！感谢您对燕子开发维护的支持。", isError: false);
                ClipboardBanner.Visibility = Visibility.Collapsed;
                LicenseCodeTextBox.Text = string.Empty;
                if (!string.IsNullOrWhiteSpace(response.VipType))
                {
                    CurrentVipBadgeText.Text = response.VipType == "lifetime" ? "终身VIP" : $"VIP {response.DaysRemaining}天";
                    CurrentVipBadge.Visibility = Visibility.Visible;
                }
                _onVipStatusChanged?.Invoke();
            }
            else
            {
                ShowMessage("激活失败，请检查激活码是否输入正确。", isError: true);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[VipActivationWindow] RedeemLicenseAsync exception: {ex}");
            ShowMessage(ex.Message, isError: true);
        }
        finally
        {
            ActivateButton.IsEnabled = !string.IsNullOrWhiteSpace(LicenseCodeTextBox.Text);
            BuyButton.IsEnabled = true;
        }
    }

    private void BuyButton_Click(object sender, RoutedEventArgs e)
    {
        HostAssets.AppendLog($"[VipActivationWindow] BuyButton_Click: opening {PurchaseUrl}");
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
            HostAssets.AppendLog($"[VipActivationWindow] BuyButton_Click failed: {ex.Message}");
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
