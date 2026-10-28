using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using OpenQuickHost.Sync;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;

namespace OpenQuickHost;

public partial class SettingsWindow
{
    private void AccountButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HostAssets.AppendLog($"[SettingsWindow.Account] AccountButton_Click triggered. CurrentUser={AccountTitle}, IsLoggedIn={IsAccountLoggedIn}");
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
            HostAssets.AppendLog("[SettingsWindow.Account] AccountMenu opened via element.ContextMenu.");
        }
        else if (AccountMenu != null)
        {
            AccountMenu.PlacementTarget = sender as UIElement ?? this;
            AccountMenu.IsOpen = true;
            HostAssets.AppendLog("[SettingsWindow.Account] AccountMenu opened via fallback field.");
        }
        else
        {
            HostAssets.AppendLog("[SettingsWindow.Account] WARNING: Could not find AccountMenu to open.");
        }
    }

    private void AccountCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        AccountButton_Click(sender, e);
    }

    private void ActivateVipButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HostAssets.AppendLog($"[SettingsWindow.Account] ActivateVipButton_Click triggered. IsVipActive={IsVipActive}, BadgeText={VipBadgeText}");
        OpenVipActivationDialog();
    }

    private async void SignInMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void SignOutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await SignOutAsync();
    }

    private async void RefreshAccountMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudAsync();
    }

    private void VipActivationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenVipActivationDialog();
    }

    public void OpenVipActivationDialog()
    {
        HostAssets.AppendLog($"[SettingsWindow.Account] OpenVipActivationDialog called. HasSyncClient={_mainWindow.CloudSyncClient != null}");
        if (_mainWindow.CloudSyncClient == null)
        {
            HostAssets.AppendLog("[SettingsWindow.Account] OpenVipActivationDialog aborted: CloudSyncClient is null.");
            System.Windows.MessageBox.Show("云端服务组件未就绪，请稍后重试。", "燕子", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            HostAssets.AppendLog("[SettingsWindow.Account] Creating VipActivationWindow instance...");
            var currentVip = IsVipActive ? VipBadgeText : null;
            var vipWindow = new VipActivationWindow(_mainWindow.CloudSyncClient, () =>
            {
                HostAssets.AppendLog("[SettingsWindow.Account] Vip status changed callback received. Refreshing account summary...");
                RefreshAccountSummary();
            }, currentVip);

            if (IsLoaded && IsVisible)
            {
                vipWindow.Owner = this;
            }

            HostAssets.AppendLog("[SettingsWindow.Account] Displaying VipActivationWindow modal dialog...");
            var dialogResult = vipWindow.ShowDialog();
            HostAssets.AppendLog($"[SettingsWindow.Account] VipActivationWindow closed. Result={dialogResult}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[SettingsWindow.Account] CRITICAL: Failed to show VipActivationWindow: {ex}");
            System.Windows.MessageBox.Show($"打开卡密激活窗口时发生异常：{ex.Message}", "燕子", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        await SignOutAsync();
    }

    private async void RefreshSyncStatusButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudAsync();
    }

    private void RefreshSyncLogButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSyncActivityLog();
    }

    private DispatcherTimer? _webDavSaveTimer;
    private DispatcherTimer? _webDavStatusHideTimer;
    private bool _isWebDavSaveStatusVisible;

    public bool IsWebDavSaveStatusVisible
    {
        get => _isWebDavSaveStatusVisible;
        private set
        {
            if (_isWebDavSaveStatusVisible == value) return;
            _isWebDavSaveStatusVisible = value;
            OnPropertyChanged();
        }
    }

    private void QueueWebDavSettingsSave(int delayMs = 500)
    {
        if (_webDavSaveTimer == null)
        {
            _webDavSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _webDavSaveTimer.Tick += (s, e) => { _webDavSaveTimer.Stop(); SaveWebDavSettings(); };
        }
        else
        {
            _webDavSaveTimer.Stop();
            _webDavSaveTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        }
        _webDavSaveTimer.Start();
    }

    private void FlushWebDavSettingsSave()
    {
        if (_webDavSaveTimer != null && _webDavSaveTimer.IsEnabled)
        {
            _webDavSaveTimer.Stop();
            SaveWebDavSettings();
        }
    }

    private void SaveWebDavSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveWebDavSettings();
    }

    private void SaveWebDavSettings()
    {
        _personalSyncSecrets.GitHubToken = GitHubTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GiteeToken = GiteeTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GitLabToken = GitLabTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.GiteaToken = GiteaTokenBox?.Password ?? string.Empty;
        _personalSyncSecrets.S3SecretAccessKey = S3SecretAccessKeyBox?.Password ?? string.Empty;
        _personalSyncSecrets.WebDavPassword = WebDavPasswordBox?.Password ?? string.Empty;
        CloudSyncDiagnostics.Log(
            "SettingsWindow.PersonalSync",
            "Save personal sync button clicked",
            ("selectedProvider", SelectedPersonalSyncProvider),
            ("summary", CloudSyncDiagnostics.DescribePersonalSync(_personalSyncSettings, _personalSyncSecrets)));
        _mainWindow.SavePersonalSyncSettings(ClonePersonalSyncSettings(_personalSyncSettings), ClonePersonalSyncSecrets(_personalSyncSecrets));
        _settings = AppSettingsStore.Load();
        _settingsPersistence.Reset(_settings);
        RefreshWebDavSummary();
        SyncStatusText = "个人同步配置已保存。";
        _webDavStatusHideTimer = ShowSaveStatusTemporarily(_webDavStatusHideTimer, visible => IsWebDavSaveStatusVisible = visible);
        RefreshSyncActivityLog();
    }

}
