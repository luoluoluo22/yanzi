using System.IO;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class MainWindow
{
    private DateTimeOffset _lastNetworkAddressChangedHandledAt = DateTimeOffset.MinValue;
    private static readonly object MobileMessageBridgeLock = new();
    private bool _deviceRegistered;
    private const string PublicStoreOrigin = "https://yanzi.luoluoluo.cc.cd";

    public static string BuildExtensionStoreUrl(string extensionId)
    {
        return $"{PublicStoreOrigin}/store.html?id={Uri.EscapeDataString(extensionId ?? string.Empty)}";
    }

    public CloudSyncClient? CloudSyncClient => _cloudSyncClient;

    public async Task<(bool ok, string message)> InstallStoreExtensionAsync(string extensionId)
    {
        if (_cloudSyncClient == null)
        {
            return (false, "云同步未配置");
        }

        try
        {
            var packageBytes = await _cloudSyncClient.DownloadExtensionArchiveAsync(extensionId);
            var result = await ExtensionInstallService.InstallPackageAsync(packageBytes, extensionId);
            ReloadLocalExtensionsFromExternal();
            return (true, $"已成功安装：{result.Name}");
        }
        catch (Exception ex)
        {
            return (false, $"安装失败：{ex.Message}");
        }
    }

    public (bool ok, string message) CopyExtensionStoreLink(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return (false, "没有可复制的扩展链接。");
        }

        var url = BuildExtensionStoreUrl(extensionId);
        ClipboardService.SetText(url);
        return (true, $"已复制扩展商店链接：{url}");
    }

    public (bool ok, string message) OpenExtensionStoreLink(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return (false, "没有可打开的扩展链接。");
        }

        var url = BuildExtensionStoreUrl(extensionId);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return (true, $"已打开扩展商店链接：{url}");
    }

    private async Task RefreshCloudStateAsync(bool allowLoginPrompt = true)
    {
        if (_cloudSyncClient == null)
        {
            await SyncPersonalWebDavAsync(showDisabledMessage: true);
            return;
        }

        try
        {
            SyncStatus = "正在读取账号状态和云端配置...";
            if (!await EnsureAuthenticatedAsync(allowPrompt: allowLoginPrompt))
            {
                return;
            }

            var me = await _cloudSyncClient.GetMeAsync();
            var pulledConfig = await PullWebDavConfigFromCloudAsync();
            var pulledQuickPanelConfig = await PullQuickPanelConfigFromCloudAsync();
            _allCommands.RemoveAll(x => x.Source == CommandSource.Cloud);
            foreach (var command in _allCommands)
            {
                command.ClearCloudData();
            }
            ApplyFilter(SearchBox.Text);
            SyncStatus = $"已登录 {me?.Username ?? _cloudSyncClient.CurrentUserLabel}";
            ResetSilentCloudReconnect();
            StartMobileMessageBridge("cloud-refresh");
            SyncLocalExtensionsToCloud();
            LastRunMessage = pulledConfig || pulledQuickPanelConfig
                ? "已同步账号状态，并更新了云端配置。"
                : "已同步账号状态。";
            OnPropertyChanged(nameof(SyncSummaryText));
        }
        catch (Exception ex)
        {
            if (!allowLoginPrompt && IsTransientNetworkException(ex))
            {
                ScheduleSilentCloudReconnect("refresh-cloud-failed");
            }

            if (allowLoginPrompt && await TryRecoverAuthenticationAsync(ex))
            {
                await RefreshCloudStateAsync();
                return;
            }

            SyncStatus = $"云同步读取失败：{FormatExceptionMessage(ex)}";
        }
    }

    private Task SyncSelectedCommandAsync()
    {
        SyncStatus = "Cloudflare 当前只同步账号状态和坚果云 / WebDAV 配置，扩展分享稍后接入。";
        return Task.CompletedTask;
    }

    private async Task DownloadSelectedCommandAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法下载。";
            return;
        }

        if (SelectedCommand == null)
        {
            SyncStatus = "没有可下载的命令。";
            return;
        }

        if (!SelectedCommand.HasArchive)
        {
            SyncStatus = "当前命令在云端没有扩展包。";
            return;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            SyncStatus = $"正在下载 {SelectedCommand.Title} 的扩展包 ...";
            var packageBytes = await _cloudSyncClient.DownloadExtensionArchiveAsync(SelectedCommand.ExtensionId);
            var version = SelectedCommand.CloudVersion ?? "0.1.0";
            var path = await ExtensionPackageService.SavePackageAsync(SelectedCommand.ExtensionId, version, packageBytes);
            SelectedCommand.SetLocalPackagePath(path);
            LastRunMessage = $"扩展包已下载到本地：{path}";
            SyncStatus = $"下载完成：{SelectedCommand.Title}";
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await DownloadSelectedCommandAsync();
                return;
            }

            SyncStatus = $"下载失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task<bool> PublishSelectedExtensionAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法发布到商店。";
            return false;
        }

        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可发布的扩展。";
            return false;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (command.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "只有本地扩展才能发布到商店。";
            return false;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                SyncStatus = "发布已取消：未完成登录。";
                return false;
            }

            SyncStatus = $"正在发布扩展：{command.Title} ...";
            var version = string.IsNullOrWhiteSpace(command.DeclaredVersion) ? "0.1.0" : command.DeclaredVersion;
            var publishedIcon = await _cloudSyncClient.PublishIconAsync(command, version);
            var packageBytes = ExtensionPackageService.BuildPackage(command, version, publishedIcon);
            await _cloudSyncClient.UpsertExtensionAsync(command, publishedIcon);
            await _cloudSyncClient.UploadExtensionArchiveAsync(command, packageBytes, version);
            await _cloudSyncClient.UpsertUserExtensionAsync(command);
            command.MarkAsSynced(version);
            var storeUrl = BuildExtensionStoreUrl(command.ExtensionId);
            LastRunMessage = $"已发布到扩展商店：{command.Title} (v{version})";
            SyncStatus = $"发布成功：{command.Title}，商店链接：{storeUrl}";
            return true;
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                return await PublishSelectedExtensionAsync();
            }

            SyncStatus = $"发布失败：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    public async Task<(bool ok, string message)> PublishExtensionFromSettingsAsync(string extensionId)
    {
        try
        {
            if (!_localExtensionIndex.TryGetValue(extensionId, out var command))
            {
                return (false, "没有找到对应扩展。");
            }

            SelectedCommand = command;
            CommandList.SelectedItem = command;
            var ok = await PublishSelectedExtensionAsync();
            return (ok, SyncStatus);
        }
        catch (Exception ex)
        {
            return (false, $"发布失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message)> UnpublishExtensionFromSettingsAsync(string extensionId)
    {
        try
        {
            if (_cloudSyncClient == null)
            {
                return (false, "云同步未配置，无法下线扩展。");
            }

            if (!_localExtensionIndex.TryGetValue(extensionId, out var command))
            {
                return (false, "没有找到对应扩展。");
            }

            if (!await EnsureAuthenticatedAsync())
            {
                return (false, "下线已取消：未完成登录。");
            }

            await _cloudSyncClient.DeleteExtensionAsync(command.ExtensionId);
            SyncStatus = $"已下线扩展：{command.Title}";
            LastRunMessage = $"扩展已从商店下线：{command.Title}";
            return (true, SyncStatus);
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                return await UnpublishExtensionFromSettingsAsync(extensionId);
            }

            return (false, $"下线失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<IReadOnlyDictionary<string, CloudExtensionRecord>> GetOwnedPublishedExtensionsForSettingsAsync()
    {
        if (_cloudSyncClient == null)
        {
            return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            if (!await EnsureAuthenticatedAsync(allowPrompt: false))
            {
                return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
            }

            var me = await _cloudSyncClient.GetMeAsync();
            if (me == null || string.IsNullOrWhiteSpace(me.UserId))
            {
                return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
            }

            var items = await _cloudSyncClient.GetExtensionsAsync();
            var owned = items
                .Where(item =>
                    item.IsPublished != 0 &&
                    item.PublisherUserId.Equals(me.UserId, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.ExtensionId, item => item, StringComparer.OrdinalIgnoreCase);
            HostAssets.AppendLog($"Owned published extensions fetched for settings: userId={me.UserId}, count={owned.Count}");
            return owned;
        }
        catch
        {
            return new Dictionary<string, CloudExtensionRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task HandleProtocolLaunchAsync(string protocolArgument)
    {
        if (!Uri.TryCreate(protocolArgument, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "yanzi", StringComparison.OrdinalIgnoreCase))
        {
            SyncStatus = "收到的本地协议无效。";
            return;
        }

        if (!string.Equals(uri.Host, "install", StringComparison.OrdinalIgnoreCase))
        {
            SyncStatus = $"暂不支持的协议动作：{uri.Host}";
            return;
        }

        var parameters = ParseProtocolQuery(uri.Query);
        var source = GetProtocolValue(parameters, "source");
        var extensionId = GetProtocolValue(parameters, "extensionId") ?? GetProtocolValue(parameters, "id");
        if (string.IsNullOrWhiteSpace(source))
        {
            SyncStatus = "安装协议缺少 source 参数。";
            return;
        }

        try
        {
            SyncStatus = "正在通过本地协议下载安装扩展 ...";
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            var packageBytes = await httpClient.GetByteArrayAsync(source);
            var result = await ExtensionInstallService.InstallPackageAsync(packageBytes, extensionId);
            ReloadLocalExtensionsFromExternal();
            RevealInstalledExtension(result.ExtensionId, result.Name);
            LastRunMessage = $"已安装扩展：{result.Name} ({result.ExtensionId})";
            SyncStatus = $"安装成功：{result.Name}";
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("燕子扩展已安装", $"{result.Name} 已安装到本地扩展目录。");
            }
            ShowPanel();
        }
        catch (Exception ex)
        {
            SyncStatus = $"安装失败：{FormatExceptionMessage(ex)}";
            HostAssets.AppendLog($"Protocol install failed: source={source}, extensionId={extensionId}, error={ex}");
            if (System.Windows.Application.Current is App app)
            {
                app.ShowDesktopNotification("燕子扩展安装失败", FormatExceptionMessage(ex), Forms.ToolTipIcon.Error);
            }
            System.Windows.MessageBox.Show(
                this,
                $"扩展安装失败：{FormatExceptionMessage(ex)}",
                "燕子扩展安装失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ShowPanel();
        }
    }

    private void RevealInstalledExtension(string extensionId, string? extensionName = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        ShowPanel();
        var displayQuery = string.IsNullOrWhiteSpace(extensionName) ? extensionId : extensionName.Trim();
        SearchBox.Text = $"@扩展 {displayQuery}";
        SearchBox.CaretIndex = SearchBox.Text.Length;
        ApplyFilter(SearchBox.Text);

        var installedCommand = FilteredCommands.FirstOrDefault(command =>
            command.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        if (installedCommand == null)
        {
            return;
        }

        var currentIndex = FilteredCommands.IndexOf(installedCommand);
        if (currentIndex > 0)
        {
            FilteredCommands.Move(currentIndex, 0);
        }

        SelectedCommand = installedCommand;
        CommandList.SelectedItem = installedCommand;
        CommandList.ScrollIntoView(installedCommand);
    }

    private Task AddJsonExtensionAsync()
    {
        try
        {
            var command = ShowJsonExtensionEditorAsync(
                string.Empty,
                isEditMode: false);
            if (command == null)
            {
                return Task.CompletedTask;
            }

            LastRunMessage = $"已添加本地 JSON 扩展：{command.Title}";
            QueueBackgroundWebDavSync("extension-add");
        }
        catch (Exception ex)
        {
            HostAssets.AppendDevLog($"AddJsonExtensionAsync failed: {ex}");
            SyncStatus = $"添加扩展失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private Task EditSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可编辑的扩展。";
            return Task.CompletedTask;
        }

        var editable = ResolveRunnableCommand(sourceCommand);
        if (editable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接编辑。";
            return Task.CompletedTask;
        }

        try
        {
            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorAsync(manifestJson, isEditMode: true);
            if (updated == null)
            {
                return Task.CompletedTask;
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            QueueBackgroundWebDavSync("extension-edit");
        }
        catch (Exception ex)
        {
            SyncStatus = $"编辑失败：{FormatExceptionMessage(ex)}";
        }

        return Task.CompletedTask;
    }

    private async Task DeleteSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可删除的扩展。";
            return;
        }

        var deletable = ResolveRunnableCommand(sourceCommand);
        if (deletable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接删除。";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"确认删除扩展“{deletable.Title}”吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            ExtensionRecycleBinService.MoveToRecycleBin(deletable.ExtensionId, deletable.ExtensionDirectoryPath);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            LastRunMessage = $"已将扩展移入回收站：{deletable.Title}";
            SyncStatus = $"已将扩展移入回收站：{deletable.Title}";
            QueueBackgroundWebDavSync("extension-delete");
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await DeleteSelectedExtensionAsync();
                return;
            }

            SyncStatus = $"删除失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync(bool forcePrompt = false, bool allowPrompt = true)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        if (forcePrompt || !_cloudSyncClient.HasCredential)
        {
            if (!allowPrompt)
            {
                SyncStatus = "未登录，已跳过云端账号同步。";
                return false;
            }

            if (!ShowLoginDialog())
            {
                SyncStatus = "未登录，云同步不可用。";
                return false;
            }
        }

        try
        {
            await _cloudSyncClient.EnsureAuthenticatedAsync();
            OnPropertyChanged(nameof(SyncSummaryText));
            return true;
        }
        catch (Exception ex)
        {
            if (!allowPrompt)
            {
                SyncStatus = $"云端账号同步失败，已跳过登录弹窗：{FormatExceptionMessage(ex)}";
                HostAssets.AppendLog($"Cloud silent auth failed: {FormatExceptionMessage(ex)}");
                if (IsTransientNetworkException(ex))
                {
                    ScheduleSilentCloudReconnect("silent-auth-failed");
                }
                return false;
            }

            if (ShowLoginDialog(FormatExceptionMessage(ex)))
            {
                await _cloudSyncClient.EnsureAuthenticatedAsync();
                OnPropertyChanged(nameof(SyncSummaryText));
                return true;
            }

            SyncStatus = "未登录，云同步不可用。";
            return false;
        }
    }

    private async Task<bool> TryRecoverAuthenticationAsync(Exception ex)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var message = ex.Message ?? string.Empty;
        if (!message.Contains("401", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("登录", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("凭据", StringComparison.OrdinalIgnoreCase) &&
            !message.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _cloudSyncClient.ClearSessionOnly();
        if (await EnsureAuthenticatedAsync(allowPrompt: false))
        {
            return true;
        }

        return await EnsureAuthenticatedAsync(forcePrompt: true);
    }

    private bool ShowLoginDialog(string? errorMessage = null)
    {
        if (_cloudSyncClient == null || _authPromptActive)
        {
            return false;
        }

        _authPromptActive = true;
        try
        {
            var saved = SecureCredentialStore.Load();
            var dialog = new LoginWindow(saved?.LoginEmail);
            dialog.SendRegistrationCodeAsync = (email, username) => _cloudSyncClient.SendRegistrationCodeAsync(email, username);
            dialog.SendPasswordResetCodeAsync = (email) => _cloudSyncClient.SendPasswordResetCodeAsync(email);
            dialog.RegisterAsyncHandler = (email, username, password, code) => _cloudSyncClient.RegisterAsync(email, username, password, code);
            dialog.ResetPasswordAsyncHandler = (email, password, code) => _cloudSyncClient.ResetPasswordAsync(email, password, code);
            Window? activeWindow = null;
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win.IsVisible && win != dialog)
                {
                    if (win is SettingsWindow)
                    {
                        activeWindow = win;
                        break;
                    }
                    if (win.IsActive || activeWindow == null)
                    {
                        activeWindow = win;
                    }
                }
            }
            if (activeWindow != null)
            {
                dialog.Owner = activeWindow;
            }
            else if (IsVisible)
            {
                dialog.Owner = this;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                dialog.ShowError(errorMessage);
            }

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return false;
            }

            _cloudSyncClient.SetCredential(dialog.LoginEmail, dialog.Password, dialog.RememberCredential);
            return true;
        }
        finally
        {
            _authPromptActive = false;
        }
    }

    private static string FormatExceptionMessage(Exception ex)
    {
        var rawMessage = ex.ToString();
        if (rawMessage.Contains("The SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("0 bytes from the transport stream", StringComparison.OrdinalIgnoreCase))
        {
            return "网络连接被中断，无法建立 HTTPS 安全连接。请检查网络、代理/VPN、系统时间或稍后重试。";
        }

        if (rawMessage.Contains("NameResolutionFailure", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase))
        {
            return "无法解析服务器地址。请检查网络连接、DNS、代理/VPN 或同步服务地址是否填写正确。";
        }

        if (rawMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            rawMessage.Contains("A task was canceled", StringComparison.OrdinalIgnoreCase))
        {
            return "连接超时。请检查网络是否稳定，或稍后重试。";
        }

        var messages = new List<string>();
        Exception? current = ex;
        while (current != null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message.Trim());
            }

            current = current.InnerException;
        }

        return string.Join(" | ", messages.Distinct(StringComparer.Ordinal));
    }

    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!e.IsAvailable)
            {
                return;
            }

            HostAssets.AppendLog("Network availability restored, scheduling silent cloud reconnect.");
            ScheduleSilentCloudReconnect("network-available", immediate: true);
        });
    }

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastNetworkAddressChangedHandledAt < TimeSpan.FromSeconds(5)) return;
        _lastNetworkAddressChangedHandledAt = now;
        NetworkChange_NetworkAddressChanged_Real(sender, e);
    }

    private void NetworkChange_NetworkAddressChanged_Real(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return;
            }

            HostAssets.AppendLog("Network address changed, scheduling silent cloud reconnect.");
            ScheduleSilentCloudReconnect("network-address-changed", immediate: true);
        });
    }

    private void ScheduleSilentCloudReconnect(string reason, bool immediate = false)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential || !_appSettings.RefreshCloudOnStartup)
        {
            return;
        }

        _cloudReconnectPendingReason = reason;
        if (_cloudReconnectInProgress)
        {
            HostAssets.AppendLog($"Silent cloud reconnect already running, marked pending: {reason}");
            return;
        }

        var delay = immediate ? TimeSpan.FromSeconds(1) : GetSilentCloudReconnectDelay(_cloudReconnectAttemptCount);
        if (_cloudReconnectTimer.IsEnabled && _cloudReconnectTimer.Interval <= delay)
        {
            return;
        }

        _cloudReconnectTimer.Stop();
        _cloudReconnectTimer.Interval = delay;
        _cloudReconnectTimer.Start();
        HostAssets.AppendLog($"Silent cloud reconnect scheduled: reason={reason}, delay={delay}.");
    }

    private async void CloudReconnectTimer_Tick(object? sender, EventArgs e)
    {
        _cloudReconnectTimer.Stop();
        if (_cloudReconnectInProgress || _cloudSyncClient == null || !_cloudSyncClient.HasCredential || !_appSettings.RefreshCloudOnStartup)
        {
            return;
        }

        _cloudReconnectInProgress = true;
        var reason = _cloudReconnectPendingReason ?? "timer";
        try
        {
            HostAssets.AppendLog($"Silent cloud reconnect attempt started: reason={reason}, attempt={_cloudReconnectAttemptCount + 1}.");
            await RefreshCloudStateAsync(allowLoginPrompt: false);
        }
        catch
        {
            // RefreshCloudStateAsync records failures and schedules follow-up retries.
        }
        finally
        {
            _cloudReconnectInProgress = false;
        }
    }

    private void ResetSilentCloudReconnect()
    {
        _cloudReconnectAttemptCount = 0;
        _cloudReconnectPendingReason = null;
        _cloudReconnectTimer.Stop();
    }

    private TimeSpan GetSilentCloudReconnectDelay(int attemptCount)
    {
        var seconds = attemptCount switch
        {
            <= 0 => 5,
            1 => 15,
            2 => 30,
            3 => 60,
            4 => 120,
            _ => 300
        };

        _cloudReconnectAttemptCount = Math.Min(attemptCount + 1, 5);
        return TimeSpan.FromSeconds(seconds);
    }

    private static bool IsTransientNetworkException(Exception ex)
    {
        var message = FormatExceptionMessage(ex);
        return message.Contains("SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("0 bytes from the transport stream", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ResponseEnded", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
    }

    private void StartMobileMessageBridge(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            HostAssets.AppendLog($"Mobile bridge skipped: reason={reason}, hasClient={_cloudSyncClient != null}, hasCredential={_cloudSyncClient?.HasCredential == true}.");
            return;
        }

        _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();
        lock (MobileMessageBridgeLock)
        {
            _deviceRegistered = false;
            if (_mobileMessageBridgeTask is not { IsCompleted: false })
            {
                try
                {
                    _mobileMessageBridgeCts?.Cancel();
                }
                catch
                {
                    // Ignore cancel exceptions
                }
                _mobileMessageBridgeCts = new CancellationTokenSource();
                _mobileMessageBridgeTask = Task.Run(() => MobileMessageBridgeLoopAsync(_mobileMessageBridgeCts.Token));
            }
        }

        HostAssets.AppendLog($"Mobile bridge started: reason={reason}, deviceId={_desktopDeviceId}.");
        _ = PollMobileMessagesSafeAsync($"start-{reason}");
    }

    private async Task MobileMessageBridgeLoopAsync(CancellationToken cancellationToken)
    {
        HostAssets.AppendLog("Mobile bridge background loop started with SSE streaming.");
        _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();

        try
        {
            await PollMobileMessagesSafeAsync("sse-sync");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile bridge SSE sync error during startup: {ex.Message}");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                HostAssets.AppendLog("Mobile bridge establishing SSE connection to cloud...");
                using var response = await _cloudSyncClient!.GetMobileMessagesEventsStreamAsync(_desktopDeviceId, cancellationToken);
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                HostAssets.AppendLog("Mobile bridge SSE connection established successfully.");

                while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        var dataJson = line["data:".Length..].Trim();
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(dataJson);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "messages")
                            {
                                if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    var itemsText = itemsProp.GetRawText();
                                    var messages = System.Text.Json.JsonSerializer.Deserialize<List<DeviceMessageRecord>>(itemsText, new System.Text.Json.JsonSerializerOptions
                                    {
                                        PropertyNameCaseInsensitive = true
                                    });

                                    if (messages != null && messages.Count > 0)
                                    {
                                        HostAssets.AppendLog($"Mobile bridge SSE pushed messages: count={messages.Count}.");
                                        
                                        foreach (var message in messages)
                                        {
                                            var res = await HandleMobileDeviceMessageAsync(message);
                                            if (res.hasResult)
                                            {
                                                await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, res.success, res.output, cancellationToken);
                                            }
                                            else
                                            {
                                                await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, cancellationToken: cancellationToken);
                                            }
                                            HostAssets.AppendLog($"Mobile bridge SSE acked message: id={message.MessageId}, deviceId={_desktopDeviceId}.");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            HostAssets.AppendLog($"Mobile bridge SSE message parse failed: {parseEx.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Mobile bridge SSE connection error: {FormatExceptionMessage(ex)}");
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                HostAssets.AppendLog("Mobile bridge SSE disconnected. Retrying in 5 seconds...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        HostAssets.AppendLog("Mobile bridge background loop stopped.");
    }

    private async void MobileMessagePollTimer_Tick(object? sender, EventArgs e)
    {
        await PollMobileMessagesSafeAsync("timer");
    }

    private async Task<int> PollMobileMessagesSafeAsync(string reason)
    {
        if (_mobileMessagePollRunning || _cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            if (_mobileMessagePollRunning)
            {
                HostAssets.AppendLog($"Mobile bridge poll skipped: reason={reason}, previous poll still running.");
            }
            return 0;
        }

        _mobileMessagePollRunning = true;
        try
        {
            using var pollTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();
            
            if (!_deviceRegistered)
            {
                await _cloudSyncClient.RegisterDeviceAsync(
                    _desktopDeviceId,
                    "desktop",
                    DeviceIdentityStore.GetDesktopDisplayName(),
                    new
                    {
                        app = "yanzi-desktop",
                        os = Environment.OSVersion.VersionString
                    },
                    cancellationToken: pollTimeout.Token);
                _deviceRegistered = true;
            }

            var messages = await _cloudSyncClient.GetPendingDeviceMessagesAsync(_desktopDeviceId, limit: 20, cancellationToken: pollTimeout.Token);
            if (messages.Count > 0)
            {
                HostAssets.AppendLog($"Mobile bridge received messages: reason={reason}, count={messages.Count}.");
            }
            else if (DateTimeOffset.UtcNow - _lastMobileMessageEmptyLogAt > TimeSpan.FromMinutes(1))
            {
                _lastMobileMessageEmptyLogAt = DateTimeOffset.UtcNow;
                HostAssets.AppendLog($"Mobile bridge poll ok: reason={reason}, count=0, deviceId={_desktopDeviceId}.");
            }

            foreach (var message in messages)
            {
                var res = await HandleMobileDeviceMessageAsync(message);
                if (res.hasResult)
                {
                    await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, res.success, res.output, cancellationToken: pollTimeout.Token);
                }
                else
                {
                    await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, cancellationToken: pollTimeout.Token);
                }
                HostAssets.AppendLog($"Mobile bridge acked message: id={message.MessageId}, deviceId={_desktopDeviceId}.");
            }
            return messages.Count;
        }
        catch (OperationCanceledException ex)
        {
            HostAssets.AppendLog($"Mobile bridge poll timed out: reason={reason}, {FormatExceptionMessage(ex)}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile bridge poll failed: reason={reason}, {FormatExceptionMessage(ex)}");
        }
        finally
        {
            _mobileMessagePollRunning = false;
        }
        return 0;
    }

    internal async Task<(bool hasResult, bool success, string output)> HandleMobileDeviceMessageAsync(DeviceMessageRecord message)
    {
        var title = string.IsNullOrWhiteSpace(message.Title) ? "手机发来消息" : message.Title.Trim();
        var text = string.IsNullOrWhiteSpace(message.Text) ? $"消息类型：{message.Kind}" : message.Text.Trim();
        var sourceLabel = GetMobileSourceLabel(message);
        var screenshotDataUrl = GetPayloadString(message, "screenshotDataUrl");
        var mobileAttachmentFilePath = await TryDownloadMobileScreenshotFromWebDavAsync(message);

        var screenshotFilePath = IsMobileScreenshotMessage(message)

            ? mobileAttachmentFilePath

            : null;
        if (string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var payloadKeys = message.Payload.Count == 0
                ? "(empty)"
                : string.Join(",", message.Payload.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
            HostAssets.AppendLog(
                $"Mobile screenshot payload: id={message.MessageId}, keys={payloadKeys}, hasDataUrl={!string.IsNullOrWhiteSpace(screenshotDataUrl)}, webDavPath={GetPayloadString(message, "webDavPath") ?? "(none)"}, localFile={mobileAttachmentFilePath ?? "(none)"}.");
        }
        HostAssets.AppendLog(
            $"Mobile bridge message: id={message.MessageId}, source={sourceLabel}, kind={message.Kind}, text={trimForLog(text)}");

        if (string.Equals(message.Kind, "run-extension", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Payload.TryGetValue("extensionId", out var extensionElement))
            {
                var extensionId = extensionElement.ValueKind == JsonValueKind.String
                    ? extensionElement.GetString()
                    : extensionElement.ToString();
                if (!string.IsNullOrWhiteSpace(extensionId) && _localExtensionIndex.TryGetValue(extensionId, out var command))
                {
                    var execResult = await RunMobileExtensionAsync(command, text);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LastRunMessage = execResult.success ? $"已执行手机端请求：{command.Title}" : $"执行手机端请求失败：{command.Title}";
                        SyncStatus = execResult.success ? "手机端请求执行成功。" : $"手机端请求执行失败：{execResult.output}";
                    });
                    return (true, execResult.success, execResult.output);
                }
                else
                {
                    return (true, false, $"手机请求的扩展不存在：{extensionId}");
                }
            }
            return (true, false, "缺少 extensionId 参数");
        }

        await Dispatcher.InvokeAsync(() =>
        {
            LastRunMessage = $"{title}：{text}";
            var clipboardMessage = CopyMobileMessageToClipboard(message, text, screenshotDataUrl, mobileAttachmentFilePath);

            SyncStatus = string.IsNullOrWhiteSpace(clipboardMessage)

                ? "已收到手机端消息。"

                : $"已收到手机端消息，{clipboardMessage}。";
            SaveMobileInboxMessage(message, title, text, sourceLabel, screenshotDataUrl, screenshotFilePath);
            ShowMobileMessageToast(title, text, sourceLabel, screenshotDataUrl, screenshotFilePath);
        });

        return (false, true, string.Empty);
    }

    private static string CopyMobileMessageToClipboard(DeviceMessageRecord message, string text, string? screenshotDataUrl, string? localFilePath)

    {

        try

        {

            if (IsMobileScreenshotMessage(message))

            {

                var bitmap = TryCreateClipboardBitmap(screenshotDataUrl, localFilePath);

                if (bitmap != null)

                {

                    var dataObject = new System.Windows.DataObject();

                    dataObject.SetImage(bitmap);

                    ClipboardService.SetDataObject(dataObject, true);

                    HostAssets.AppendLog($"Mobile bridge clipboard copied image: id={message.MessageId}.");

                    return "已复制图片到剪贴板";

                }

            }



            var filePaths = ResolveMobileClipboardFilePaths(message, localFilePath);

            if (filePaths.Count > 0)

            {

                CopyFileDropListToClipboard(filePaths);

                HostAssets.AppendLog($"Mobile bridge clipboard copied files: id={message.MessageId}, count={filePaths.Count}.");

                return filePaths.Count == 1 ? "已复制文件到剪贴板" : $"已复制 {filePaths.Count} 个文件到剪贴板";

            }



            if (!string.IsNullOrWhiteSpace(text))

            {

                ClipboardService.SetText(text);

                HostAssets.AppendLog($"Mobile bridge clipboard copied text: id={message.MessageId}, length={text.Length}.");

                return "已复制文本到剪贴板";

            }

        }

        catch (Exception ex)

        {

            HostAssets.AppendLog($"Mobile bridge clipboard copy failed: id={message.MessageId}, {FormatExceptionMessage(ex)}");

            return "剪贴板写入失败";

        }



        return string.Empty;

    }



    private static BitmapSource? TryCreateClipboardBitmap(string? dataUrl, string? localFilePath)

    {

        byte[] bytes;

        if (!string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath))

        {

            bytes = File.ReadAllBytes(localFilePath);

        }

        else if (!TryDecodeDataUrl(dataUrl, out bytes))

        {

            return null;

        }



        var bitmap = new BitmapImage();

        bitmap.BeginInit();

        bitmap.CacheOption = BitmapCacheOption.OnLoad;

        bitmap.StreamSource = new MemoryStream(bytes);

        bitmap.EndInit();

        bitmap.Freeze();

        return bitmap;

    }



    private static bool TryDecodeDataUrl(string? dataUrl, out byte[] bytes)

    {

        bytes = [];

        if (string.IsNullOrWhiteSpace(dataUrl))

        {

            return false;

        }



        const string marker = "base64,";

        var index = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index < 0)

        {

            return false;

        }



        bytes = Convert.FromBase64String(dataUrl[(index + marker.Length)..]);

        return bytes.Length > 0;

    }



    private static IReadOnlyList<string> ResolveMobileClipboardFilePaths(DeviceMessageRecord message, string? localFilePath)

    {

        var paths = new List<string>();

        AddClipboardFilePath(paths, localFilePath);



        foreach (var key in new[] { "localFilePath", "filePath", "path", "downloadedFilePath", "attachmentPath" })

        {

            AddClipboardFilePath(paths, GetPayloadString(message, key));

        }



        foreach (var key in new[] { "localFilePaths", "filePaths", "paths", "attachments", "files" })

        {

            AddPayloadFilePaths(paths, message, key);

        }



        return paths

            .Where(static path => File.Exists(path) || Directory.Exists(path))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToArray();

    }



    private static void AddPayloadFilePaths(List<string> paths, DeviceMessageRecord message, string key)

    {

        if (!message.Payload.TryGetValue(key, out var element))

        {

            return;

        }



        if (element.ValueKind == JsonValueKind.String)

        {

            AddClipboardFilePath(paths, element.GetString());

            return;

        }



        if (element.ValueKind == JsonValueKind.Array)

        {

            foreach (var item in element.EnumerateArray())

            {

                if (item.ValueKind == JsonValueKind.String)

                {

                    AddClipboardFilePath(paths, item.GetString());

                }

                else if (item.ValueKind == JsonValueKind.Object)

                {

                    AddClipboardFilePath(paths, ReadPayloadObjectString(item, "localFilePath"));

                    AddClipboardFilePath(paths, ReadPayloadObjectString(item, "filePath"));

                    AddClipboardFilePath(paths, ReadPayloadObjectString(item, "path"));

                }

            }

            return;

        }



        if (element.ValueKind == JsonValueKind.Object)

        {

            AddClipboardFilePath(paths, ReadPayloadObjectString(element, "localFilePath"));

            AddClipboardFilePath(paths, ReadPayloadObjectString(element, "filePath"));

            AddClipboardFilePath(paths, ReadPayloadObjectString(element, "path"));

        }

    }



    private static string? ReadPayloadObjectString(JsonElement element, string key)

    {

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out var value))

        {

            return null;

        }



        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();

    }



    private static void AddClipboardFilePath(List<string> paths, string? path)

    {

        if (string.IsNullOrWhiteSpace(path))

        {

            return;

        }



        var normalized = path.Trim().Trim('"');

        if (!string.IsNullOrWhiteSpace(normalized))

        {

            paths.Add(normalized);

        }

    }



    private static void CopyFileDropListToClipboard(IReadOnlyList<string> filePaths)

    {

        var files = new StringCollection();

        foreach (var filePath in filePaths)

        {

            files.Add(filePath);

        }



        var dataObject = new System.Windows.DataObject();

        dataObject.SetFileDropList(files);

        using var stream = new MemoryStream(new byte[] { 5, 0, 0, 0 });

        dataObject.SetData("Preferred DropEffect", stream);

        ClipboardService.SetDataObject(dataObject, true);

    }



    private static bool IsMobileScreenshotMessage(DeviceMessageRecord message)

    {

        return string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase);

    }



    private async Task<(bool success, string output)> RunMobileExtensionAsync(CommandItem runnable, string inputText)
    {
        var hasExternalInput = !string.IsNullOrWhiteSpace(inputText);
        var command = ResolveRunnableCommand(runnable);
        
        if (command.App != null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (AppExtensionWindow.TryActivateExisting(command))
                {
                    return;
                }
                var window = new AppExtensionWindow(command, inputText, "mobile")
                {
                    ShowInTaskbar = true
                };
                window.Show();
            });
            return (true, "已成功在电脑端打开应用扩展。");
        }
        
        if (command.HostedView != null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                ShowPanel();
                OpenHostedView(command, inputText);
            });
            return (true, "已成功在电脑端打开视图界面。");
        }
        
        if (command.SupportsQueryArgument == false && IsInternalCommand(command))
        {
            var internalSuccess = false;
            await Dispatcher.InvokeAsync(() =>
            {
                internalSuccess = HandleInternalCommand(command);
            });
            return (internalSuccess, internalSuccess ? "已成功执行内置指令。" : "执行内置指令失败。");
        }
        
        if (ScriptExtensionRunner.CanExecute(command))
        {
            var result = await ScriptExtensionRunner.ExecuteAsync(command, inputText, "mobile");
            if (result.Success)
            {
                return (true, string.IsNullOrWhiteSpace(result.Output) ? "执行成功，无输出。" : result.Output);
            }
            else
            {
                return (false, string.IsNullOrWhiteSpace(result.Error) ? $"执行失败，退出代码: {result.ExitCode}" : result.Error);
            }
        }
        
        var executionTarget = BuildExecutionTarget(command, inputText, allowRawQuery: hasExternalInput);
        if (executionTarget is { Length: > 0 })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = executionTarget,
                    Arguments = command.LaunchArguments ?? string.Empty,
                    WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? string.Empty : command.WorkingDirectory,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return (true, $"已运行命令：{command.Title}");
            }
            catch (Exception ex)
            {
                return (false, $"无法启动程序：{ex.Message}");
            }
        }
        
        return (false, "不支持的扩展执行方式。");
    }

    private async Task<string?> TryDownloadMobileScreenshotFromWebDavAsync(DeviceMessageRecord message)
    {
        var remotePath = GetPayloadString(message, "webDavPath");
        if (string.IsNullOrWhiteSpace(remotePath))
        {
            if (string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase))
            {
                HostAssets.AppendLog(
                    $"Mobile screenshot WebDAV skipped: payload has no webDavPath, hasDataUrl={!string.IsNullOrWhiteSpace(GetPayloadString(message, "screenshotDataUrl"))}.");
            }
            return null;
        }

        try
        {
            var settings = AppSettingsStore.Load();
            var service = new WebDavSyncService(settings);
            var credential = WebDavCredentialStore.Load();
            bool hasCredentials = Uri.TryCreate(settings.WebDavServerUrl, UriKind.Absolute, out _) &&
                                 !string.IsNullOrWhiteSpace(settings.WebDavUsername) &&
                                 !string.IsNullOrWhiteSpace(credential?.Password);
            if (!hasCredentials)
            {
                HostAssets.AppendLog($"Mobile screenshot WebDAV skipped: not configured, path={remotePath}.");
                return null;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var bytes = await service.TryReadTemporaryFileAsync(remotePath, timeout.Token);
            if (bytes is not { Length: > 0 })
            {
                HostAssets.AppendLog($"Mobile screenshot WebDAV missing: path={remotePath}.");
                return null;
            }

            var filePath = BuildMobileAttachmentDownloadPath(message, remotePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllBytesAsync(filePath, bytes, timeout.Token);
            HostAssets.AppendLog($"Mobile attachment WebDAV downloaded: kind={message.Kind}, path={remotePath}, local={filePath}, bytes={bytes.Length}.");
            return filePath;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile screenshot WebDAV download failed: path={remotePath}, {FormatExceptionMessage(ex)}");
            return null;
        }
    }

    private static string BuildMobileAttachmentDownloadPath(DeviceMessageRecord message, string remotePath)

    {

        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        var fileName = FirstNonEmpty(

            GetPayloadString(message, "fileName"),

            GetPayloadString(message, "name"),

            Path.GetFileName(remotePath.Replace('/', Path.DirectorySeparatorChar)));



        if (string.IsNullOrWhiteSpace(fileName))

        {

            fileName = IsMobileScreenshotMessage(message)

                ? $"yanzi-mobile-screenshot-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.jpg"

                : $"yanzi-mobile-file-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}";

        }



        fileName = SanitizeFileName(fileName);

        var path = Path.Combine(downloads, fileName);

        if (!File.Exists(path) && !Directory.Exists(path))

        {

            return path;

        }



        var stem = Path.GetFileNameWithoutExtension(fileName);

        var extension = Path.GetExtension(fileName);

        return Path.Combine(downloads, $"{stem}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}{extension}");

    }



    private static string SanitizeFileName(string fileName)

    {

        var invalidChars = Path.GetInvalidFileNameChars();

        var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? $"yanzi-mobile-file-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}" : sanitized;

    }



    private static string? GetPayloadString(DeviceMessageRecord message, string key)
    {
        if (!message.Payload.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    private static string GetMobileSourceLabel(DeviceMessageRecord message)
    {
        var label = FirstNonEmpty(
            message.SourceDeviceDisplayName,
            message.SourceDeviceName,
            GetPayloadString(message, "sourceDeviceDisplayName"),
            GetPayloadString(message, "sourceDeviceName"),
            GetPayloadString(message, "deviceName"),
            GetPayloadString(message, "displayName"));
        return MobileDeviceNameNormalizer.Normalize(label, message.SourceDeviceId);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static void SaveMobileInboxMessage(DeviceMessageRecord message, string title, string text, string sourceLabel, string? screenshotDataUrl, string? screenshotFilePath)
    {
        try
        {
            var record = new
            {
                messageId = message.MessageId,
                sourceDeviceId = message.SourceDeviceId,
                sourceDeviceName = sourceLabel,
                kind = message.Kind,
                title,
                text,
                payload = message.Payload,
                screenshotDataUrl = string.IsNullOrWhiteSpace(screenshotFilePath) ? screenshotDataUrl : null,
                localFilePath = screenshotFilePath,
                receivedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                createdAt = message.CreatedAt
            };
            File.AppendAllText(
                HostAssets.MobileInboxPath,
                JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox save failed: id={message.MessageId}, {FormatExceptionMessage(ex)}");
        }
    }

    public void ShowMobileInboxWindow()
    {
        try
        {
            if (_mobileMessageToastWindow is { IsVisible: true })
            {
                _mobileMessageToastWindow.LoadInboxHistory();
                _mobileMessageToastWindow.Activate();
                return;
            }

            _mobileMessageToastWindow = new MobileMessageToastWindow();
            _mobileMessageToastWindow.Closed += (_, _) => _mobileMessageToastWindow = null;
            _mobileMessageToastWindow.Show();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile inbox window failed: {FormatExceptionMessage(ex)}");
        }
    }

    private void ShowMobileMessageToast(string title, string text, string sourceDeviceId, string? screenshotDataUrl = null, string? screenshotFilePath = null)
    {
        try
        {
            if (_mobileMessageToastWindow is { IsVisible: true })
            {
                _mobileMessageToastWindow.AppendMessage(title, text, sourceDeviceId, DateTimeOffset.Now, screenshotDataUrl, screenshotFilePath);
                return;
            }

            _mobileMessageToastWindow = new MobileMessageToastWindow(title, text, sourceDeviceId, DateTimeOffset.Now, screenshotDataUrl, screenshotFilePath);
            _mobileMessageToastWindow.Closed += (_, _) => _mobileMessageToastWindow = null;
            _mobileMessageToastWindow.Show();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile message toast failed: {FormatExceptionMessage(ex)}");
        }
    }



    private static string trimForLog(string value)
    {
        var text = value.ReplaceLineEndings(" ").Trim();
        return text.Length <= 160 ? text : $"{text[..160]}...";
    }

    private async Task SyncPersonalWebDavAsync(bool showDisabledMessage)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            if (showDisabledMessage)
            {
                SyncStatus = "未启用个人同步。";
            }

            return;
        }

        try
        {
            var service = new PersonalSyncService(settings);
            var result = await service.SyncExtensionsAsync();
            ApplyWebDavSyncResult(result);
            LastRunMessage = BuildPersonalSyncCompletedMessage(result);
        }
        catch (Exception ex)
        {
            SyncStatus = $"个人扩展同步失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void StartBackgroundWebDavSync()
    {
        if (PersonalSyncBackendFactory.IsConfigured(AppSettingsStore.Load()) && !_backgroundWebDavSyncTimer.IsEnabled)
        {
            _backgroundWebDavSyncTimer.Start();
        }
    }

    internal void QueueBackgroundWebDavSync(string reason, bool forceImmediate = false)
    {
        var settings = AppSettingsStore.Load();
        if (!PersonalSyncBackendFactory.IsConfigured(settings))
        {
            return;
        }

        StartBackgroundWebDavSync();
        if (!forceImmediate && !IsImmediateBackgroundSyncReason(reason))
        {
            var delaySeconds = settings.PersonalSyncAutoSyncDelaySeconds;
            if (delaySeconds <= 0)
            {
                HostAssets.AppendLog($"Personal sync auto sync skipped by setting: {reason}");
                return;
            }

            _pendingBackgroundWebDavSyncReason = reason;
            _backgroundWebDavSyncDelayTimer.Stop();
            _backgroundWebDavSyncDelayTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(delaySeconds, 2, 120));
            _backgroundWebDavSyncDelayTimer.Start();
            HostAssets.AppendLog($"Personal sync auto sync scheduled: reason={reason}, delaySeconds={delaySeconds}");
            return;
        }

        if (_backgroundWebDavSyncRunning)
        {
            _backgroundWebDavSyncRequested = true;
            HostAssets.AppendLog($"Personal sync background sync queued while running: {reason}");
            return;
        }

        _ = RunBackgroundWebDavSyncAsync(reason);
    }

    private static bool IsImmediateBackgroundSyncReason(string reason)
    {
        return reason.StartsWith("timer", StringComparison.OrdinalIgnoreCase) ||
               reason.StartsWith("startup", StringComparison.OrdinalIgnoreCase) ||
               reason.StartsWith("queued", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunBackgroundWebDavSyncAsync(string reason)
    {
        _backgroundWebDavSyncRunning = true;
        try
        {
            var settings = AppSettingsStore.Load();
            HostAssets.AppendLog($"Personal sync background sync started: reason={reason}, provider={settings.PersonalSync.Provider}");
            var result = await Task.Run(async () =>
            {
                var service = new PersonalSyncService(settings);
                return await service.SyncExtensionsAsync();
            });
            ApplyWebDavSyncResult(result);
            SyncStatus = BuildPersonalSyncCompletedMessage(result, includeConfigSummary: false);
            HostAssets.AppendLog($"Personal sync background sync completed: reason={reason}, uploaded={result.UploadedCount}, pulled={result.PulledCount}, configUploaded={result.ConfigUploaded}, configPulled={result.ConfigPulled}");
        }
        catch (Exception ex)
        {
            var message = FormatExceptionMessage(ex);
            SyncStatus = $"个人扩展后台同步失败：{message}";
            HostAssets.AppendLog($"Personal sync background sync failed: reason={reason} -> {message}");
        }
        finally
        {
            _backgroundWebDavSyncRunning = false;
            if (_backgroundWebDavSyncRequested)
            {
                _backgroundWebDavSyncRequested = false;
                QueueBackgroundWebDavSync("queued");
            }
        }
    }

    private void ApplyWebDavSyncResult(WebDavSyncResult result)
    {
        if (result.PulledCount > 0 || result.ConfigPulled)
        {
            ReloadLocalExtensionsFromWebDav();
        }

        SyncLocalExtensionsToCloud();

        if (!result.ConfigPulled)
        {
            return;
        }

        RefreshAppSettings();
        _quickPanel?.RefreshSettingsFromStore();
        NotifySettingsWindowAiConfigChanged();
        OnPropertyChanged(nameof(AiChatModelDisplayText));
        ApplyFilter(SearchBox.Text);
    }

    public void SyncLocalExtensionsToCloud()
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var commands = LocalExtensionCatalog.LoadCommands();
                int successCount = 0;
                foreach (var cmd in commands)
                {
                    if (cmd.Source == CommandSource.LocalExtension && !IsInternalCommand(cmd))
                    {
                        string? publishedIcon = null;
                        try
                        {
                            publishedIcon = await _cloudSyncClient.PublishIconAsync(cmd, cmd.DeclaredVersion);
                        }
                        catch (Exception iconEx)
                        {
                            HostAssets.AppendLog($"Failed to publish icon for {cmd.ExtensionId}: {iconEx.Message}");
                        }

                        await _cloudSyncClient.UpsertExtensionAsync(cmd, publishedIcon);
                        await _cloudSyncClient.UpsertUserExtensionAsync(cmd);
                        successCount++;
                    }
                }
                HostAssets.AppendLog($"Auto synchronized {successCount} local extensions metadata to cloud db successfully.");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Failed to auto register local extensions to cloud db: {ex.Message}");
            }
        });
    }

    private async Task<bool> PullWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var snapshot = await _cloudSyncClient.GetUserConfigAsync<CloudPersonalSyncConfigSnapshot>(CloudPersonalSyncConfigId);
        var legacySnapshot = await _cloudSyncClient.GetUserConfigAsync<CloudPersonalSyncConfigSnapshot>(CloudLegacyWebDavConfigId);
        HostAssets.AppendLog(
            $"Personal sync cloud pull sources: primaryFound={snapshot != null}, legacyFound={legacySnapshot != null}");
        if (snapshot == null)
        {
            snapshot = legacySnapshot;
        }
        if (snapshot == null)
        {
            HostAssets.AppendLog("Personal sync cloud pull: no user config found.");
            if (ShouldSyncLocalWebDavConfigToCloud())
            {
                await PushWebDavConfigToCloudAsync("cloud-refresh-bootstrap");
            }
            return false;
        }

        var incomingSettings = snapshot.Settings ?? new PersonalSyncSettings();
        incomingSettings.Provider = PersonalSyncProviders.Normalize(
            string.IsNullOrWhiteSpace(snapshot.Provider)
                ? incomingSettings.Provider
                : snapshot.Provider);
        incomingSettings.Enabled = incomingSettings.Enabled || snapshot.Enabled;
        var incomingSecrets = snapshot.Secrets ?? new PersonalSyncSecretBag();
        if (!ReferenceEquals(snapshot, legacySnapshot) && legacySnapshot != null)
        {
            MergeLegacyWebDavSnapshot(incomingSettings, incomingSecrets, legacySnapshot);
            HostAssets.AppendLog(
                $"Personal sync cloud pull: merged legacy WebDAV snapshot, hasLegacyPassword={!string.IsNullOrWhiteSpace(legacySnapshot.Secrets?.WebDavPassword)}");
        }
        if (string.IsNullOrWhiteSpace(incomingSecrets.WebDavPassword))
        {
            var legacyDto = await _cloudSyncClient.FetchWebDavConfigAsync();
            HostAssets.AppendLog(
                $"Personal sync cloud pull: checked legacy WebDAV config endpoint, found={legacyDto != null}, hasPassword={!string.IsNullOrWhiteSpace(legacyDto?.Password)}");
            if (!string.IsNullOrWhiteSpace(legacyDto?.Password))
            {
                incomingSettings.Provider = PersonalSyncProviders.WebDav;
                incomingSettings.Enabled = true;
                incomingSettings.WebDav.Url = string.IsNullOrWhiteSpace(legacyDto.ServerUrl)
                    ? incomingSettings.WebDav.Url
                    : legacyDto.ServerUrl;
                incomingSettings.WebDav.PathPrefix = string.IsNullOrWhiteSpace(legacyDto.RootPath)
                    ? incomingSettings.WebDav.PathPrefix
                    : legacyDto.RootPath;
                incomingSettings.WebDav.Username = string.IsNullOrWhiteSpace(legacyDto.Username)
                    ? incomingSettings.WebDav.Username
                    : legacyDto.Username;
                incomingSecrets.WebDavPassword = legacyDto.Password;
                HostAssets.AppendLog("Personal sync cloud pull: recovered WebDAV password from legacy config endpoint.");
            }
        }
        var incomingAutoSyncDelaySeconds = NormalizePersonalSyncAutoSyncDelay(snapshot.AutoSyncDelaySeconds);

        var localSettings = AppSettingsStore.Load();
        var localPersonalSync = localSettings.PersonalSync ?? new PersonalSyncSettings();
        var localSecrets = PersonalSyncSecretStore.Load();

        HostAssets.AppendLog(
            $"Personal sync cloud pull: provider={incomingSettings.Provider}, enabled={incomingSettings.Enabled}, hasGitHubToken={!string.IsNullOrWhiteSpace(incomingSecrets.GitHubToken)}, hasGiteeToken={!string.IsNullOrWhiteSpace(incomingSecrets.GiteeToken)}, hasGitLabToken={!string.IsNullOrWhiteSpace(incomingSecrets.GitLabToken)}, hasGiteaToken={!string.IsNullOrWhiteSpace(incomingSecrets.GiteaToken)}, hasS3Secret={!string.IsNullOrWhiteSpace(incomingSecrets.S3SecretAccessKey)}, hasWebDavPassword={!string.IsNullOrWhiteSpace(incomingSecrets.WebDavPassword)}");

        if (!HasMeaningfulPersonalSyncConfig(incomingSettings, incomingSecrets) &&
            HasMeaningfulPersonalSyncConfig(localPersonalSync, localSecrets))
        {
            HostAssets.AppendLog("Personal sync cloud pull skipped: remote snapshot is empty, pushing local config instead.");
            await PushWebDavConfigToCloudAsync("cloud-refresh-empty-remote");
            return false;
        }

        PreserveMissingPersonalSyncValues(localPersonalSync, incomingSettings, localSecrets, incomingSecrets);

        if (ArePersonalSyncSettingsEqual(localPersonalSync, incomingSettings) &&
            ArePersonalSyncSecretsEqual(localSecrets, incomingSecrets) &&
            localSettings.PersonalSyncAutoSyncDelaySeconds == incomingAutoSyncDelaySeconds)
        {
            HostAssets.AppendLog("Personal sync cloud pull: no local changes detected.");
            return false;
        }

        SavePersonalSyncSettings(incomingSettings, incomingSecrets, queueCloudSync: false);
        SavePersonalSyncAutoSyncDelaySeconds(incomingAutoSyncDelaySeconds, queueCloudSync: false);
        HostAssets.AppendLog(
            $"Personal sync cloud pull applied: provider={incomingSettings.Provider}, enabled={incomingSettings.Enabled}, autoSyncDelaySeconds={incomingAutoSyncDelaySeconds}");
        NotifySettingsWindowWebDavConfigChanged();
        return true;
    }

    private static void MergeLegacyWebDavSnapshot(
        PersonalSyncSettings targetSettings,
        PersonalSyncSecretBag targetSecrets,
        CloudPersonalSyncConfigSnapshot legacySnapshot)
    {
        var legacySettings = legacySnapshot.Settings ?? new PersonalSyncSettings();
        var legacySecrets = legacySnapshot.Secrets ?? new PersonalSyncSecretBag();
        if (!string.IsNullOrWhiteSpace(legacySettings.WebDav.Url))
        {
            targetSettings.WebDav.Url = legacySettings.WebDav.Url;
        }

        if (!string.IsNullOrWhiteSpace(legacySettings.WebDav.PathPrefix))
        {
            targetSettings.WebDav.PathPrefix = legacySettings.WebDav.PathPrefix;
        }

        if (!string.IsNullOrWhiteSpace(legacySettings.WebDav.Username))
        {
            targetSettings.WebDav.Username = legacySettings.WebDav.Username;
        }

        if (!string.IsNullOrWhiteSpace(legacySecrets.WebDavPassword) &&
            string.IsNullOrWhiteSpace(targetSecrets.WebDavPassword))
        {
            targetSecrets.WebDavPassword = legacySecrets.WebDavPassword;
            targetSettings.Provider = PersonalSyncProviders.WebDav;
            targetSettings.Enabled = true;
        }
    }

    private void QueueCloudWebDavConfigSync(string reason)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _ = PushWebDavConfigToCloudSafeAsync(reason);
    }

    private void QueueCloudQuickPanelConfigSync(string reason)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _ = PushQuickPanelConfigToCloudSafeAsync(reason);
    }

    private void QueueCloudYanmStateSync(string reason)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        _ = PushYanmStateToCloudSafeAsync(reason);
    }

    private async Task PushWebDavConfigToCloudSafeAsync(string reason)
    {
        try
        {
            await PushWebDavConfigToCloudAsync(reason);
            HostAssets.AppendLog($"Cloud personal sync config synced: {reason}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Cloud personal sync config sync skipped: {reason} -> {FormatExceptionMessage(ex)}");
        }
    }

    private async Task PushQuickPanelConfigToCloudSafeAsync(string reason)
    {
        try
        {
            await PushQuickPanelConfigToCloudAsync(reason);
            HostAssets.AppendLog($"Cloud quick panel config synced: {reason}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Cloud quick panel config sync skipped: {reason} -> {FormatExceptionMessage(ex)}");
        }
    }

    private async Task PushYanmStateToCloudSafeAsync(string reason)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await PushYanmStateToCloudAsync(reason);
                HostAssets.AppendLog($"Cloud Yanm state synced: {reason}, attempt={attempt}");
                return;
            }
            catch (Exception ex)
            {
                var message = FormatExceptionMessage(ex);
                if (attempt >= maxAttempts || !IsTransientNetworkException(ex))
                {
                    HostAssets.AppendLog($"Cloud Yanm state sync skipped: {reason}, attempt={attempt}/{maxAttempts} -> {message}");
                    return;
                }

                var delay = TimeSpan.FromSeconds(attempt * 2);
                HostAssets.AppendLog($"Cloud Yanm state sync retry scheduled: {reason}, attempt={attempt}/{maxAttempts}, delay={delay.TotalSeconds:0}s -> {message}");
                await Task.Delay(delay);
            }
        }
    }

    private async Task PushWebDavConfigToCloudAsync(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential || !ShouldSyncLocalWebDavConfigToCloud())
        {
            HostAssets.AppendLog($"Personal sync cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        var sync = settings.PersonalSync ?? new PersonalSyncSettings();
        var secrets = PersonalSyncSecretStore.Load();
        var hadLocalWebDavPassword = !string.IsNullOrWhiteSpace(secrets.WebDavPassword);
        var existingSnapshot = await _cloudSyncClient.GetUserConfigAsync<CloudPersonalSyncConfigSnapshot>(CloudPersonalSyncConfigId);
        if (existingSnapshot?.Secrets != null)
        {
            PreserveMissingPersonalSyncValues(
                existingSnapshot.Settings ?? new PersonalSyncSettings(),
                sync,
                existingSnapshot.Secrets,
                secrets);
            HostAssets.AppendLog(
                $"Personal sync cloud push: preserved missing secrets from primary cloud snapshot, hasExistingWebDavPassword={!string.IsNullOrWhiteSpace(existingSnapshot.Secrets.WebDavPassword)}");
        }

        if (string.IsNullOrWhiteSpace(secrets.WebDavPassword))
        {
            var legacySnapshot = await _cloudSyncClient.GetUserConfigAsync<CloudPersonalSyncConfigSnapshot>(CloudLegacyWebDavConfigId);
            if (legacySnapshot != null)
            {
                MergeLegacyWebDavSnapshot(sync, secrets, legacySnapshot);
                HostAssets.AppendLog(
                    $"Personal sync cloud push: checked legacy WebDAV snapshot, hasLegacyPassword={!string.IsNullOrWhiteSpace(legacySnapshot.Secrets?.WebDavPassword)}");
            }
        }

        if (string.IsNullOrWhiteSpace(secrets.WebDavPassword))
        {
            var legacyDto = await _cloudSyncClient.FetchWebDavConfigAsync();
            if (!string.IsNullOrWhiteSpace(legacyDto?.Password))
            {
                sync.Provider = PersonalSyncProviders.WebDav;
                sync.Enabled = true;
                sync.WebDav.Url = string.IsNullOrWhiteSpace(legacyDto.ServerUrl)
                    ? sync.WebDav.Url
                    : legacyDto.ServerUrl;
                sync.WebDav.PathPrefix = string.IsNullOrWhiteSpace(legacyDto.RootPath)
                    ? sync.WebDav.PathPrefix
                    : legacyDto.RootPath;
                sync.WebDav.Username = string.IsNullOrWhiteSpace(legacyDto.Username)
                    ? sync.WebDav.Username
                    : legacyDto.Username;
                secrets.WebDavPassword = legacyDto.Password;
                HostAssets.AppendLog("Personal sync cloud push: recovered WebDAV password from legacy config endpoint before push.");
            }
        }

        if (!hadLocalWebDavPassword && !string.IsNullOrWhiteSpace(secrets.WebDavPassword))
        {
            PersonalSyncSecretStore.Save(secrets);
            WebDavCredentialStore.Save(new SavedWebDavCredential
            {
                Username = sync.WebDav.Username,
                Password = secrets.WebDavPassword
            });
            HostAssets.AppendLog("Personal sync cloud push: backfilled local WebDAV password from cloud before push.");
        }

        HostAssets.AppendLog(
            $"Personal sync cloud push: reason={reason}, provider={sync.Provider}, enabled={sync.Enabled}, hasGitHubToken={!string.IsNullOrWhiteSpace(secrets.GitHubToken)}, hasGiteeToken={!string.IsNullOrWhiteSpace(secrets.GiteeToken)}, hasGitLabToken={!string.IsNullOrWhiteSpace(secrets.GitLabToken)}, hasGiteaToken={!string.IsNullOrWhiteSpace(secrets.GiteaToken)}, hasS3Secret={!string.IsNullOrWhiteSpace(secrets.S3SecretAccessKey)}, hasWebDavPassword={!string.IsNullOrWhiteSpace(secrets.WebDavPassword)}");
        await _cloudSyncClient.UpsertUserConfigAsync(CloudPersonalSyncConfigId, new CloudPersonalSyncConfigSnapshot
        {
            Enabled = sync.Enabled,
            Provider = sync.Provider,
            Settings = sync,
            Secrets = secrets,
            AutoSyncDelaySeconds = settings.PersonalSyncAutoSyncDelaySeconds
        });
    }

    private async Task<bool> PullQuickPanelConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var snapshot = await _cloudSyncClient.GetUserConfigAsync<CloudQuickPanelConfigSnapshot>(CloudQuickPanelConfigId);
        if (snapshot == null)
        {
            HostAssets.AppendLog("Quick panel cloud pull: no user config found.");
            if (ShouldSyncLocalQuickPanelConfigToCloud())
            {
                await PushQuickPanelConfigToCloudAsync("cloud-refresh-bootstrap");
            }

            return false;
        }

        var settings = AppSettingsStore.Load();
        var shouldBackfillAiConfig = !HasAiConfigPayload(snapshot) && HasAiSettings(settings);
        var incoming = snapshot.ToAppSettings();
        var changed =
            !AreStringListsEqual(settings.GlobalFavoriteExtensionIds, incoming.GlobalFavoriteExtensionIds) ||
            !AreStringListsEqual(settings.ContextFavoriteExtensionIds, incoming.ContextFavoriteExtensionIds) ||
            !AreNullableStringListsEqual(settings.QuickPanelSlots, incoming.QuickPanelSlots) ||
            !string.Equals(settings.SelectedQuickPanelGlobalGroupId, incoming.SelectedQuickPanelGlobalGroupId, StringComparison.Ordinal) ||
            !string.Equals(settings.SelectedQuickPanelContextGroupId, incoming.SelectedQuickPanelContextGroupId, StringComparison.Ordinal) ||
            !AreQuickPanelGroupsEqual(settings.QuickPanelGlobalGroups, incoming.QuickPanelGlobalGroups) ||
            !AreQuickPanelGroupsEqual(settings.QuickPanelContextGroups, incoming.QuickPanelContextGroups) ||
            !AreQuickPanelMouseTriggersEqual(settings.QuickPanelMouseTriggers, incoming.QuickPanelMouseTriggers) ||
            !string.Equals(MouseGestureTriggerModes.Normalize(settings.MouseGestureTriggerMode), MouseGestureTriggerModes.Normalize(incoming.MouseGestureTriggerMode), StringComparison.Ordinal) ||
            !string.Equals(MouseTriggerModes.Normalize(settings.WindowSnapAssistMouseTriggerMode), MouseTriggerModes.Normalize(incoming.WindowSnapAssistMouseTriggerMode), StringComparison.Ordinal) ||
            snapshot.YarnSelect != null && !AreJsonPayloadsEqual(settings.YarnSelect, incoming.YarnSelect) ||
            snapshot.RadialMenu != null && !AreJsonPayloadsEqual(settings.RadialMenu, incoming.RadialMenu) ||
            snapshot.YanyuRules != null && !AreJsonPayloadsEqual(settings.YanyuRules, incoming.YanyuRules) ||
            snapshot.Yanm != null && !AreJsonPayloadsEqual(settings.Yanm, incoming.Yanm) ||
            HasAiConfigPayload(snapshot) && !AreAiSettingsEqual(settings, incoming);
        var localUpdatedAtUtc = TryParseCloudTimestamp(settings.LauncherConfigUpdatedAtUtc);
        var remoteUpdatedAtUtc = TryParseCloudTimestamp(snapshot.UpdatedAtUtc);
        if (changed &&
            localUpdatedAtUtc != null &&
            (remoteUpdatedAtUtc == null || remoteUpdatedAtUtc.Value <= localUpdatedAtUtc.Value.AddSeconds(1)))
        {
            HostAssets.AppendLog(
                $"Quick panel cloud pull skipped: local config is newer, localUpdated={localUpdatedAtUtc:O}, remoteUpdated={remoteUpdatedAtUtc?.ToString("O") ?? "missing"}.");
            await PushQuickPanelConfigToCloudAsync("cloud-refresh-local-newer");
            return false;
        }

        if (!changed)
        {
            if (shouldBackfillAiConfig)
            {
                await PushQuickPanelConfigToCloudAsync("cloud-refresh-ai-backfill");
                HostAssets.AppendLog("Quick panel cloud pull: backfilled missing AI config fields.");
                return true;
            }

            HostAssets.AppendLog("Quick panel cloud pull: no local changes detected.");
            return false;
        }

        settings.QuickPanelSlots = incoming.QuickPanelSlots;
        settings.QuickPanelGlobalGroups = incoming.QuickPanelGlobalGroups;
        settings.QuickPanelContextGroups = incoming.QuickPanelContextGroups;
        settings.SelectedQuickPanelGlobalGroupId = incoming.SelectedQuickPanelGlobalGroupId;
        settings.SelectedQuickPanelContextGroupId = incoming.SelectedQuickPanelContextGroupId;
        settings.GlobalFavoriteExtensionIds = incoming.GlobalFavoriteExtensionIds;
        settings.ContextFavoriteExtensionIds = incoming.ContextFavoriteExtensionIds;
        settings.QuickPanelMouseTriggers = incoming.QuickPanelMouseTriggers;
        settings.MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(incoming.MouseGestureTriggerMode);
        settings.WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(incoming.WindowSnapAssistMouseTriggerMode);
        if (snapshot.YarnSelect != null)
        {
            settings.YarnSelect = incoming.YarnSelect;
        }

        if (snapshot.RadialMenu != null)
        {
            settings.RadialMenu = incoming.RadialMenu;
        }

        if (snapshot.YanyuRules != null)
        {
            settings.YanyuRules = incoming.YanyuRules;
        }

        if (snapshot.Yanm != null)
        {
            settings.Yanm = incoming.Yanm;
        }

        if (HasAiConfigPayload(snapshot))
        {
            settings.AiBaseUrl = incoming.AiBaseUrl;
            settings.AiApiKey = incoming.AiApiKey;
            settings.AiModel = incoming.AiModel;
        }

        settings.LauncherConfigUpdatedAtUtc = DateTime.UtcNow.ToString("O");

        AppSettingsStore.Save(settings);
        _appSettings = AppSettingsStore.Load();
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
            ReloadMouseGestureRegistrations();
            RefreshYanyuRules();
        }

        _quickPanel?.RefreshSettingsFromStore();
        NotifySettingsWindowAiConfigChanged();
        OnPropertyChanged(nameof(AiChatModelDisplayText));
        HostAssets.AppendLog(
            $"Quick panel cloud pull applied: globalGroups={settings.QuickPanelGlobalGroups.Count}, contextGroups={settings.QuickPanelContextGroups.Count}, globalFavs={settings.GlobalFavoriteExtensionIds.Count}, contextFavs={settings.ContextFavoriteExtensionIds.Count}, yanyu={settings.YanyuRules.Count}, radialPages={settings.RadialMenu?.Pages?.Count ?? 0}, aiConfig={HasAiConfigPayload(snapshot)}");
        if (shouldBackfillAiConfig)
        {
            await PushQuickPanelConfigToCloudAsync("cloud-pull-ai-backfill");
            HostAssets.AppendLog("Quick panel cloud pull applied: backfilled missing AI config fields.");
        }

        return true;
    }

    private async Task PushQuickPanelConfigToCloudAsync(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            HostAssets.AppendLog($"Quick panel cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        await _cloudSyncClient.UpsertUserConfigAsync(CloudQuickPanelConfigId, CloudQuickPanelConfigSnapshot.FromSettings(settings));
    }

    private async Task PushYanmStateToCloudAsync(string reason)
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            HostAssets.AppendLog($"Yanm cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        settings.Yanm ??= new YanmSettings();
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await _cloudSyncClient.UpsertYanmStateAsync(settings.Yanm, settings.LauncherConfigUpdatedAtUtc);
    }

    public async Task<(bool ok, string message, bool pulled, int payloadBytes)> PullYanmStateFromCloudNowAsync()
    {
        if (_cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            return (false, "未登录燕子账号，无法读取云端燕幕。", false, 0);
        }

        try
        {
            await _cloudSyncClient.EnsureAuthenticatedAsync();
            var response = await _cloudSyncClient.GetYanmStateAsync();
            if (response?.Yanm == null)
            {
                return (true, "云端没有燕幕快照。", false, 0);
            }

            var settings = AppSettingsStore.Load();
            settings.Yanm ??= new YanmSettings();
            var localUpdatedAtUtc = TryParseCloudTimestamp(settings.LauncherConfigUpdatedAtUtc);
            var remoteUpdatedAtUtc = TryParseCloudTimestamp(response.UpdatedAtUtc);
            if (remoteUpdatedAtUtc != null &&
                localUpdatedAtUtc != null &&
                remoteUpdatedAtUtc.Value <= localUpdatedAtUtc.Value.AddSeconds(1))
            {
                if (!AreJsonPayloadsEqual(settings.Yanm, response.Yanm))
                {
                    await _cloudSyncClient.UpsertYanmStateAsync(settings.Yanm, settings.LauncherConfigUpdatedAtUtc);
                    return (true, $"云端燕幕较旧，已保留本地并回写云端，数据 {FormatBytes(response.Bytes)}。", false, response.Bytes);
                }

                return (true, $"云端燕幕无变化，数据 {FormatBytes(response.Bytes)}。", false, response.Bytes);
            }

            if (AreJsonPayloadsEqual(settings.Yanm, response.Yanm))
            {
                return (true, $"云端燕幕无变化，数据 {FormatBytes(response.Bytes)}。", false, response.Bytes);
            }

            settings.Yanm = response.Yanm;
            settings.LauncherConfigUpdatedAtUtc = DateTime.UtcNow.ToString("O");
            AppSettingsStore.Save(settings);
            _appSettings = AppSettingsStore.Load();
            _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
            if (!_listenerServicesPaused)
            {
                InputHookService.ReloadSettings();
                KeyboardDoubleTapService.ApplyYanmSettings(_appSettings.Yanm);
                RefreshYanmHotkeyRegistration();
                RefreshRadialHotkeyRegistration();
            }

            return (true, $"已拉取云端燕幕，数据 {FormatBytes(response.Bytes)}。", true, response.Bytes);
        }
        catch (Exception ex)
        {
            return (false, $"云端燕幕拉取失败：{FormatExceptionMessage(ex)}", false, 0);
        }
    }

    private static DateTime? TryParseCloudTimestamp(string? value)
    {
        return DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool ShouldSyncLocalWebDavConfigToCloud()
    {
        var settings = AppSettingsStore.Load();
        var sync = settings.PersonalSync ?? new PersonalSyncSettings();
        var secrets = PersonalSyncSecretStore.Load();
        return sync.Enabled ||
               HasWebDavConfigValues(sync.WebDav.Url, sync.WebDav.PathPrefix, sync.WebDav.Username, secrets.WebDavPassword) ||
               !string.IsNullOrWhiteSpace(sync.GitHub.Username) ||
               !string.Equals(sync.GitHub.Repo, "yanzi-sync", StringComparison.Ordinal) ||
               !string.Equals(sync.GitHub.Branch, "main", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sync.GitHub.PathPrefix) ||
               !string.IsNullOrWhiteSpace(sync.Gitee.Username) ||
               !string.Equals(sync.Gitee.Repo, "yanzi-sync", StringComparison.Ordinal) ||
               !string.Equals(sync.Gitee.Branch, "master", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sync.Gitee.PathPrefix) ||
               !string.IsNullOrWhiteSpace(sync.GitLab.ProjectPath) ||
               !string.Equals(sync.GitLab.BaseUrl, "https://gitlab.com", StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(sync.GitLab.Branch, "main", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sync.GitLab.PathPrefix) ||
               !string.Equals(sync.Gitea.BaseUrl, "https://gitea.com", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sync.Gitea.Username) ||
               !string.Equals(sync.Gitea.Repo, "yanzi-sync", StringComparison.Ordinal) ||
               !string.Equals(sync.Gitea.Branch, "main", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(sync.Gitea.PathPrefix) ||
               !string.IsNullOrWhiteSpace(sync.S3.AccessKeyId) ||
               !string.IsNullOrWhiteSpace(sync.S3.Region) ||
               !string.IsNullOrWhiteSpace(sync.S3.Bucket) ||
               !string.IsNullOrWhiteSpace(sync.S3.Endpoint) ||
               !string.IsNullOrWhiteSpace(sync.S3.PathPrefix) ||
               !string.IsNullOrWhiteSpace(secrets.GitHubToken) ||
               !string.IsNullOrWhiteSpace(secrets.GiteeToken) ||
               !string.IsNullOrWhiteSpace(secrets.GitLabToken) ||
               !string.IsNullOrWhiteSpace(secrets.GiteaToken) ||
               !string.IsNullOrWhiteSpace(secrets.S3SecretAccessKey);
    }

    private static bool ShouldSyncLocalQuickPanelConfigToCloud()
    {
        var settings = AppSettingsStore.Load();
        return settings.QuickPanelGlobalGroups.Any(group => group.SlotItems.Any(static slot => slot != null)) ||
               settings.QuickPanelContextGroups.Any(group => group.SlotItems.Any(static slot => slot != null)) ||
               settings.GlobalFavoriteExtensionIds.Count > 0 ||
               settings.ContextFavoriteExtensionIds.Count > 0 ||
               settings.YanyuRules.Count > 0 ||
               settings.YarnSelect.Rules.Count > 0 ||
            settings.RadialMenu.Enabled ||
               settings.Yanm.Components.Count > 0 ||
               HasAiSettings(settings) ||
               settings.RadialMenu.Pages.Any(static page =>
                   page.Slots.Any(static slot => !string.IsNullOrWhiteSpace(slot)) ||
                   page.ChildPageIds.Any(static childPageId => !string.IsNullOrWhiteSpace(childPageId)));
    }

    private static bool HasAiConfigPayload(CloudQuickPanelConfigSnapshot snapshot)
    {
        return snapshot.AiBaseUrl != null ||
               snapshot.AiApiKey != null ||
               snapshot.AiModel != null;
    }

    private static bool HasAiSettings(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.AiBaseUrl) ||
               !string.IsNullOrWhiteSpace(settings.AiApiKey) ||
               !string.IsNullOrWhiteSpace(settings.AiModel);
    }

    private static bool AreAiSettingsEqual(AppSettings left, AppSettings right)
    {
        return string.Equals(left.AiBaseUrl, right.AiBaseUrl, StringComparison.Ordinal) &&
               string.Equals(left.AiApiKey, right.AiApiKey, StringComparison.Ordinal) &&
               string.Equals(left.AiModel, right.AiModel, StringComparison.Ordinal);
    }

    private static bool AreJsonPayloadsEqual<T>(T left, T right)
    {
        return string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);
    }

    private static bool AreStringListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count &&
               !left.Where((item, index) => !string.Equals(item, right[index], StringComparison.Ordinal)).Any();
    }

    private static bool AreNullableStringListsEqual(IReadOnlyList<string?> left, IReadOnlyList<string?> right)
    {
        return left.Count == right.Count &&
               !left.Where((item, index) => !string.Equals(item, right[index], StringComparison.Ordinal)).Any();
    }

    private static bool AreQuickPanelGroupsEqual(IReadOnlyList<QuickPanelGroupSettings> left, IReadOnlyList<QuickPanelGroupSettings> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var l = left[index];
            var r = right[index];
            if (!string.Equals(l.Id, r.Id, StringComparison.Ordinal) ||
                !string.Equals(l.Name, r.Name, StringComparison.Ordinal) ||
                !string.Equals(l.ContextProcessName, r.ContextProcessName, StringComparison.Ordinal) ||
                !string.Equals(l.ContextDisplayName, r.ContextDisplayName, StringComparison.Ordinal) ||
                !AreNullableStringListsEqual(l.Slots, r.Slots) ||
                !AreQuickPanelSlotItemsEqual(l.SlotItems, r.SlotItems))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreQuickPanelSlotItemsEqual(IReadOnlyList<QuickPanelSlotItem?> left, IReadOnlyList<QuickPanelSlotItem?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var l = left[index];
            var r = right[index];
            if (l == null || r == null)
            {
                if (l != r)
                {
                    return false;
                }

                continue;
            }

            if (!string.Equals(l.ItemType, r.ItemType, StringComparison.Ordinal) ||
                !string.Equals(l.ExtensionId, r.ExtensionId, StringComparison.Ordinal) ||
                !string.Equals(l.FolderName, r.FolderName, StringComparison.Ordinal) ||
                !AreStringListsEqual(l.FolderExtensionIds, r.FolderExtensionIds) ||
                !AreQuickPanelSlotItemsEqual(l.FolderSlotItems, r.FolderSlotItems))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreQuickPanelMouseTriggersEqual(QuickPanelMouseTriggerSettings left, QuickPanelMouseTriggerSettings right)
    {
        return left.MiddleButtonDown == right.MiddleButtonDown &&
               left.X1ButtonDown == right.X1ButtonDown &&
               left.X2ButtonDown == right.X2ButtonDown &&
               left.CtrlLeftClick == right.CtrlLeftClick &&
               left.CtrlRightClick == right.CtrlRightClick &&
               left.MiddleButtonLongPress == right.MiddleButtonLongPress &&
               left.RightButtonLongPress == right.RightButtonLongPress &&
               left.RightButtonDrag == right.RightButtonDrag &&
               left.MiddleButtonDrag == right.MiddleButtonDrag &&
               left.HorizontalWheel == right.HorizontalWheel &&
               left.ExecuteOnButtonRelease == right.ExecuteOnButtonRelease &&
               left.LongPressMilliseconds == right.LongPressMilliseconds &&
               left.DragThresholdPixels == right.DragThresholdPixels;
    }

    public async Task<bool> PromptLoginFromSettingsAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置。";
            return false;
        }

        try
        {
            var ok = ShowLoginDialog();
            if (!ok)
            {
                return false;
            }

            await _cloudSyncClient.EnsureAuthenticatedAsync();
            OnPropertyChanged(nameof(SyncSummaryText));
            SyncStatus = "已登录，可进行云同步。";
            HostAssets.AppendLog("PromptLoginFromSettingsAsync: authentication succeeded, pulling cloud configs.");
            await PullWebDavConfigFromCloudAsync();
            await PullQuickPanelConfigFromCloudAsync();
            NotifySettingsWindowWebDavConfigChanged();
            
            return true;
        }
        catch (Exception ex)
        {
            SyncStatus = $"登录失败：{FormatExceptionMessage(ex)}";
            return false;
        }
    }

    private async Task SyncWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        try
        {
            var config = await _cloudSyncClient.FetchWebDavConfigAsync();
            if (config != null)
            {
                var localSettings = AppSettingsStore.Load();
                var resolvedEnabled = localSettings.WebDavSyncManuallyDisabled
                    ? false
                    : (config.Enabled || HasWebDavConfigValues(config.ServerUrl, config.RootPath, config.Username, config.Password));
                // Apply configuration to local settings
                SaveWebDavSettings(
                    resolvedEnabled,
                    config.ServerUrl ?? string.Empty,
                    config.RootPath ?? string.Empty,
                    config.Username ?? string.Empty
                );
                
                // Save credential if provided
                if (!string.IsNullOrWhiteSpace(config.Password))
                {
                    SaveWebDavCredential(config.Username ?? string.Empty, config.Password);
                }
                
                // Notify SettingsWindow to refresh UI if open
                NotifySettingsWindowWebDavConfigChanged();
                
                System.Diagnostics.Debug.WriteLine("WebDAV configuration synced from cloud successfully.");
            }
        }
        catch (Exception ex)
        {
            // Log error but don't block login process
            System.Diagnostics.Debug.WriteLine($"Failed to sync WebDAV config from cloud: {ex.Message}");
        }
    }

    private static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username, string? password)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username) ||
               !string.IsNullOrWhiteSpace(password);
    }

    private static bool ArePersonalSyncSettingsEqual(PersonalSyncSettings? left, PersonalSyncSettings? right)
    {
        var leftJson = JsonSerializer.Serialize(left ?? new PersonalSyncSettings());
        var rightJson = JsonSerializer.Serialize(right ?? new PersonalSyncSettings());
        return string.Equals(leftJson, rightJson, StringComparison.Ordinal);
    }

    private static bool ArePersonalSyncSecretsEqual(PersonalSyncSecretBag? left, PersonalSyncSecretBag? right)
    {
        var leftJson = JsonSerializer.Serialize(left ?? new PersonalSyncSecretBag());
        var rightJson = JsonSerializer.Serialize(right ?? new PersonalSyncSecretBag());
        return string.Equals(leftJson, rightJson, StringComparison.Ordinal);
    }

    private static bool HasMeaningfulPersonalSyncConfig(PersonalSyncSettings? settings, PersonalSyncSecretBag? secrets)
    {
        settings ??= new PersonalSyncSettings();
        secrets ??= new PersonalSyncSecretBag();
        return settings.Enabled ||
               !string.IsNullOrWhiteSpace(secrets.GitHubToken) ||
               !string.IsNullOrWhiteSpace(secrets.GiteeToken) ||
               !string.IsNullOrWhiteSpace(secrets.GitLabToken) ||
               !string.IsNullOrWhiteSpace(secrets.GiteaToken) ||
               !string.IsNullOrWhiteSpace(secrets.S3SecretAccessKey) ||
               !string.IsNullOrWhiteSpace(secrets.WebDavPassword) ||
               !string.IsNullOrWhiteSpace(settings.GitHub.Username) ||
               !string.Equals(settings.GitHub.Repo, "yanzi-sync", StringComparison.Ordinal) ||
               !string.IsNullOrWhiteSpace(settings.GitHub.PathPrefix) ||
               !string.IsNullOrWhiteSpace(settings.Gitee.Username) ||
               !string.Equals(settings.Gitee.Repo, "yanzi-sync", StringComparison.Ordinal) ||
               !string.IsNullOrWhiteSpace(settings.Gitee.PathPrefix) ||
               !string.IsNullOrWhiteSpace(settings.GitLab.ProjectPath) ||
               !string.IsNullOrWhiteSpace(settings.GitLab.PathPrefix) ||
               !string.IsNullOrWhiteSpace(settings.Gitea.Username) ||
               !string.Equals(settings.Gitea.Repo, "yanzi-sync", StringComparison.Ordinal) ||
               !string.IsNullOrWhiteSpace(settings.Gitea.PathPrefix) ||
               !string.IsNullOrWhiteSpace(settings.S3.AccessKeyId) ||
               !string.IsNullOrWhiteSpace(settings.S3.Region) ||
               !string.IsNullOrWhiteSpace(settings.S3.Bucket) ||
               !string.IsNullOrWhiteSpace(settings.S3.Endpoint) ||
               !string.IsNullOrWhiteSpace(settings.S3.PathPrefix) ||
               !string.IsNullOrWhiteSpace(settings.WebDav.Username);
    }

    private static void PreserveMissingPersonalSyncValues(
        PersonalSyncSettings localSettings,
        PersonalSyncSettings incomingSettings,
        PersonalSyncSecretBag localSecrets,
        PersonalSyncSecretBag incomingSecrets)
    {
        if (!incomingSettings.Enabled && localSettings.Enabled && !HasMeaningfulPersonalSyncConfig(incomingSettings, incomingSecrets))
        {
            incomingSettings.Enabled = true;
        }

        incomingSettings.GitHub.Username = KeepIncomingOrLocal(incomingSettings.GitHub.Username, localSettings.GitHub.Username);
        incomingSettings.GitHub.Repo = KeepIncomingOrLocal(incomingSettings.GitHub.Repo, localSettings.GitHub.Repo);
        incomingSettings.GitHub.Branch = KeepIncomingOrLocal(incomingSettings.GitHub.Branch, localSettings.GitHub.Branch);
        incomingSettings.GitHub.PathPrefix = KeepIncomingOrLocal(incomingSettings.GitHub.PathPrefix, localSettings.GitHub.PathPrefix);
        incomingSettings.Gitee.Username = KeepIncomingOrLocal(incomingSettings.Gitee.Username, localSettings.Gitee.Username);
        incomingSettings.Gitee.Repo = KeepIncomingOrLocal(incomingSettings.Gitee.Repo, localSettings.Gitee.Repo);
        incomingSettings.Gitee.Branch = KeepIncomingOrLocal(incomingSettings.Gitee.Branch, localSettings.Gitee.Branch);
        incomingSettings.Gitee.PathPrefix = KeepIncomingOrLocal(incomingSettings.Gitee.PathPrefix, localSettings.Gitee.PathPrefix);
        incomingSettings.GitLab.BaseUrl = KeepIncomingOrLocal(incomingSettings.GitLab.BaseUrl, localSettings.GitLab.BaseUrl);
        incomingSettings.GitLab.ProjectPath = KeepIncomingOrLocal(incomingSettings.GitLab.ProjectPath, localSettings.GitLab.ProjectPath);
        incomingSettings.GitLab.Branch = KeepIncomingOrLocal(incomingSettings.GitLab.Branch, localSettings.GitLab.Branch);
        incomingSettings.GitLab.PathPrefix = KeepIncomingOrLocal(incomingSettings.GitLab.PathPrefix, localSettings.GitLab.PathPrefix);
        incomingSettings.Gitea.BaseUrl = KeepIncomingOrLocal(incomingSettings.Gitea.BaseUrl, localSettings.Gitea.BaseUrl);
        incomingSettings.Gitea.Username = KeepIncomingOrLocal(incomingSettings.Gitea.Username, localSettings.Gitea.Username);
        incomingSettings.Gitea.Repo = KeepIncomingOrLocal(incomingSettings.Gitea.Repo, localSettings.Gitea.Repo);
        incomingSettings.Gitea.Branch = KeepIncomingOrLocal(incomingSettings.Gitea.Branch, localSettings.Gitea.Branch);
        incomingSettings.Gitea.PathPrefix = KeepIncomingOrLocal(incomingSettings.Gitea.PathPrefix, localSettings.Gitea.PathPrefix);
        incomingSettings.S3.AccessKeyId = KeepIncomingOrLocal(incomingSettings.S3.AccessKeyId, localSettings.S3.AccessKeyId);
        incomingSettings.S3.Region = KeepIncomingOrLocal(incomingSettings.S3.Region, localSettings.S3.Region);
        incomingSettings.S3.Bucket = KeepIncomingOrLocal(incomingSettings.S3.Bucket, localSettings.S3.Bucket);
        incomingSettings.S3.Endpoint = KeepIncomingOrLocal(incomingSettings.S3.Endpoint, localSettings.S3.Endpoint);
        incomingSettings.S3.PathPrefix = KeepIncomingOrLocal(incomingSettings.S3.PathPrefix, localSettings.S3.PathPrefix);
        incomingSettings.WebDav.Url = KeepIncomingOrLocal(incomingSettings.WebDav.Url, localSettings.WebDav.Url);
        incomingSettings.WebDav.Username = KeepIncomingOrLocal(incomingSettings.WebDav.Username, localSettings.WebDav.Username);
        incomingSettings.WebDav.PathPrefix = KeepIncomingOrLocal(incomingSettings.WebDav.PathPrefix, localSettings.WebDav.PathPrefix);
        incomingSecrets.GitHubToken = KeepIncomingOrLocal(incomingSecrets.GitHubToken, localSecrets.GitHubToken);
        incomingSecrets.GiteeToken = KeepIncomingOrLocal(incomingSecrets.GiteeToken, localSecrets.GiteeToken);
        incomingSecrets.GitLabToken = KeepIncomingOrLocal(incomingSecrets.GitLabToken, localSecrets.GitLabToken);
        incomingSecrets.GiteaToken = KeepIncomingOrLocal(incomingSecrets.GiteaToken, localSecrets.GiteaToken);
        incomingSecrets.S3SecretAccessKey = KeepIncomingOrLocal(incomingSecrets.S3SecretAccessKey, localSecrets.S3SecretAccessKey);
        incomingSecrets.WebDavPassword = KeepIncomingOrLocal(incomingSecrets.WebDavPassword, localSecrets.WebDavPassword);
    }

    private static string KeepIncomingOrLocal(string? incoming, string? local) =>
        string.IsNullOrWhiteSpace(incoming) && !string.IsNullOrWhiteSpace(local)
            ? local.Trim()
            : incoming?.Trim() ?? string.Empty;

    private static int NormalizePersonalSyncAutoSyncDelay(int value)
    {
        return value is 0 or 2 or 3 or 5 or 10 or 20 or 30 or 60 or 120
            ? value
            : 10;
    }

    private void NotifySettingsWindowWebDavConfigChanged()
    {
        // If SettingsWindow is open, refresh its WebDAV UI
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshWebDavConfigFromExternal();
    }

    private void NotifySettingsWindowAiConfigChanged()
    {
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshAiConfigFromExternal();
    }

    public async Task RefreshCloudFromSettingsAsync()
    {
        await RefreshCloudStateAsync();
    }

    public void SignOutFromSettings()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        HostAssets.AppendLog(
            $"SignOutFromSettings: before clear sessionExists={File.Exists(SyncSessionStore.SessionPath)}, credentialExists={File.Exists(SecureCredentialStore.CredentialPath)}");
        _cloudSyncClient.ClearCredential();
        SyncStatus = "已退出登录。";
        OnPropertyChanged(nameof(SyncSummaryText));
        NotifySettingsWindowAccountChanged();
        HostAssets.AppendLog(
            $"SignOutFromSettings: after clear sessionExists={File.Exists(SyncSessionStore.SessionPath)}, credentialExists={File.Exists(SecureCredentialStore.CredentialPath)}");
    }

    private void NotifySettingsWindowAccountChanged()
    {
        var settingsWindow = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        settingsWindow?.RefreshAccountFromExternal();
    }

    public void RefreshAppSettings()
    {
        var settings = AppSettingsStore.Load();
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        OnPropertyChanged(nameof(AiChatModelDisplayText));
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
            ReloadMouseGestureRegistrations();
            YarnSelectService.ReloadSettings();
            KeyboardDoubleTapService.ApplyYanmSettings(settings.Yanm);
            if (!YarnSelectService.IsRunning && settings.YarnSelect?.Enabled == true)
            {
                YarnSelectService.Start(HandleYarnSelectAction);
            }

            RefreshYanyuRules();
            _yanmOverlay?.ReloadSettings();
            _windowSnapAssistService.ReloadCustomLayouts();
            RefreshLauncherHotkeyRegistration();
            RefreshWindowSnapAssistHotkeyRegistration();
            RefreshExtensionHotkeys();
        }
        SyncStatus = settings.LaunchAtStartup
            ? "设置已保存。开机启动已启用。"
            : settings.RefreshCloudOnStartup
                ? "设置已保存。"
                : "设置已保存。启动后自动刷新云状态已关闭。";
    }

    public string GetLauncherHotkey() => AppSettingsStore.Load().LauncherHotkey;

    public AppSettings GetCurrentAppSettings() => AppSettingsStore.Load();

    public bool TryUpdateWindowSnapAssistHotkey(string shortcut, out string message)
    {
        message = string.Empty;
        if (!string.IsNullOrWhiteSpace(shortcut) &&
            (!TryParseHotkey(shortcut, out _, out _) || IsDoubleTapShortcut(shortcut)))
        {
            message = "窗口排列快捷键格式无效。示例：Ctrl+Alt+S";
            return false;
        }

        var settings = AppSettingsStore.Load();
        var previous = settings.WindowSnapAssistHotkey;
        settings.WindowSnapAssistHotkey = shortcut.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;

        if (!RefreshWindowSnapAssistHotkeyRegistration())
        {
            settings.WindowSnapAssistHotkey = previous;
            AppSettingsStore.Save(settings);
            _appSettings = settings;
            RefreshWindowSnapAssistHotkeyRegistration();
            message = "窗口排列快捷键注册失败，可能与系统或其他程序冲突。";
            return false;
        }

        message = string.IsNullOrWhiteSpace(settings.WindowSnapAssistHotkey)
            ? "已清除窗口排列快捷键。"
            : $"窗口排列快捷键已更新为 {settings.WindowSnapAssistHotkey}";
        return true;
    }

    public void SaveWebDavSettings(bool enabled, string serverUrl, string rootPath, string username)
    {
        var settings = AppSettingsStore.Load();
        settings.PersonalSync ??= new PersonalSyncSettings();
        settings.PersonalSync.Provider = PersonalSyncProviders.WebDav;
        settings.PersonalSync.Enabled = enabled;
        settings.PersonalSync.WebDav.Url = serverUrl.Trim();
        settings.PersonalSync.WebDav.PathPrefix = string.IsNullOrWhiteSpace(rootPath) ? "/yanzi" : rootPath.Trim();
        settings.PersonalSync.WebDav.Username = username.Trim();
        settings.EnableWebDavSync = enabled;
        settings.WebDavSyncManuallyDisabled = !enabled && HasWebDavConfigValues(serverUrl, rootPath, username, null);
        settings.WebDavServerUrl = serverUrl.Trim();
        settings.WebDavRootPath = string.IsNullOrWhiteSpace(rootPath) ? "/yanzi" : rootPath.Trim();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        if (enabled)
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        QueueCloudWebDavConfigSync("settings-saved");
    }

    public void SaveWebDavCredential(string username, string password)
    {
        var secrets = PersonalSyncSecretStore.Load();
        secrets.WebDavPassword = password;
        PersonalSyncSecretStore.Save(secrets);
        WebDavCredentialStore.Save(new SavedWebDavCredential
        {
            Username = username.Trim(),
            Password = password
        });

        var settings = AppSettingsStore.Load();
        settings.PersonalSync ??= new PersonalSyncSettings();
        settings.PersonalSync.Provider = PersonalSyncProviders.WebDav;
        settings.PersonalSync.WebDav.Username = username.Trim();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        StartBackgroundWebDavSync();
        QueueBackgroundWebDavSync("credential-saved");
        QueueCloudWebDavConfigSync("credential-saved");
    }

    public PersonalSyncSecretBag GetPersonalSyncSecrets()
    {
        return PersonalSyncSecretStore.Load();
    }

    public async Task<IReadOnlyList<PersonalSyncGitCommitInfo>> GetPersonalSyncGitHubCommitsAsync(CancellationToken cancellationToken = default)
    {
        var settings = AppSettingsStore.Load();
        var sync = settings.PersonalSync ?? new PersonalSyncSettings();
        var secrets = PersonalSyncSecretStore.Load();
        if (!string.Equals(sync.Provider, PersonalSyncProviders.GitHub, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(secrets.GitHubToken))
        {
            return [];
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secrets.GitHubToken.Trim());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Yanzi", "0.1"));

        var owner = sync.GitHub.Username?.Trim();
        if (string.IsNullOrWhiteSpace(owner))
        {
            using var userResponse = await httpClient.GetAsync("https://api.github.com/user", cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"GitHub 账号读取失败：HTTP {(int)userResponse.StatusCode}");
            }

            using var userDocument = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(cancellationToken));
            owner = userDocument.RootElement.TryGetProperty("login", out var loginElement)
                ? loginElement.GetString()
                : null;
        }

        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new InvalidOperationException("GitHub Token 未返回账号名。");
        }

        var repo = string.IsNullOrWhiteSpace(sync.GitHub.Repo) ? "yanzi-sync" : sync.GitHub.Repo.Trim();
        var branch = string.IsNullOrWhiteSpace(sync.GitHub.Branch) ? "main" : sync.GitHub.Branch.Trim();
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits?sha={Uri.EscapeDataString(branch)}&per_page=12";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub 提交记录读取失败：HTTP {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var commits = new List<PersonalSyncGitCommitInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var sha = item.TryGetProperty("sha", out var shaElement) ? shaElement.GetString() ?? string.Empty : string.Empty;
            var htmlUrl = item.TryGetProperty("html_url", out var htmlUrlElement) ? htmlUrlElement.GetString() ?? string.Empty : string.Empty;
            var commit = item.TryGetProperty("commit", out var commitElement) ? commitElement : default;
            var message = commit.ValueKind != JsonValueKind.Undefined && commit.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;
            var authorName = string.Empty;
            var committedAtUtc = DateTimeOffset.MinValue;
            if (commit.ValueKind != JsonValueKind.Undefined &&
                commit.TryGetProperty("author", out var authorElement))
            {
                if (authorElement.TryGetProperty("name", out var nameElement))
                {
                    authorName = nameElement.GetString() ?? string.Empty;
                }

                if (authorElement.TryGetProperty("date", out var dateElement) &&
                    DateTimeOffset.TryParse(dateElement.GetString(), out var parsedDate))
                {
                    committedAtUtc = parsedDate.ToUniversalTime();
                }
            }

            commits.Add(new PersonalSyncGitCommitInfo(
                sha,
                FirstLine(message),
                authorName,
                committedAtUtc == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : committedAtUtc,
                htmlUrl));
        }

        return commits;
    }

    private static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        using var reader = new StringReader(value.Trim());
        return reader.ReadLine() ?? value.Trim();
    }

    public void SavePersonalSyncSettings(PersonalSyncSettings personalSync, PersonalSyncSecretBag secrets, bool queueCloudSync = true)
    {
        var settings = AppSettingsStore.Load();
        settings.PersonalSync = personalSync ?? new PersonalSyncSettings();
        settings.PersonalSync.Provider = PersonalSyncProviders.Normalize(settings.PersonalSync.Provider);

        if (settings.PersonalSync.Provider == PersonalSyncProviders.WebDav)
        {
            settings.EnableWebDavSync = settings.PersonalSync.Enabled;
            settings.WebDavSyncManuallyDisabled = !settings.PersonalSync.Enabled &&
                                                  HasWebDavConfigValues(
                                                      settings.PersonalSync.WebDav.Url,
                                                      settings.PersonalSync.WebDav.PathPrefix,
                                                      settings.PersonalSync.WebDav.Username,
                                                      null);
            settings.WebDavServerUrl = settings.PersonalSync.WebDav.Url;
            settings.WebDavRootPath = settings.PersonalSync.WebDav.PathPrefix;
            settings.WebDavUsername = settings.PersonalSync.WebDav.Username;
            WebDavCredentialStore.Save(new SavedWebDavCredential
            {
                Username = settings.PersonalSync.WebDav.Username,
                Password = secrets?.WebDavPassword ?? string.Empty
            });
        }
        else
        {
            settings.EnableWebDavSync = false;
        }

        AppSettingsStore.Save(settings);
        PersonalSyncSecretStore.Save(secrets ?? new PersonalSyncSecretBag());
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        if (PersonalSyncBackendFactory.IsConfigured(settings))
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        if (queueCloudSync)
        {
            QueueCloudWebDavConfigSync("personal-sync-settings-saved");
        }
    }

    public void SavePersonalSyncAutoSyncDelaySeconds(int delaySeconds, bool queueCloudSync = true)
    {
        var normalized = NormalizePersonalSyncAutoSyncDelay(delaySeconds);
        var settings = AppSettingsStore.Load();
        if (settings.PersonalSyncAutoSyncDelaySeconds == normalized)
        {
            return;
        }

        settings.PersonalSyncAutoSyncDelaySeconds = normalized;
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        if (queueCloudSync)
        {
            QueueCloudWebDavConfigSync("personal-sync-delay-saved");
        }
    }

    public void NotifyQuickPanelSettingsChanged(string reason, bool refreshYanmOverlay = true)
    {
        var settings = AppSettingsStore.Load();
        settings.LauncherConfigUpdatedAtUtc = DateTime.UtcNow.ToString("O");
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        if (!_listenerServicesPaused)
        {
            InputHookService.ReloadSettings();
            ReloadMouseGestureRegistrations();
            YarnSelectService.ReloadSettings();
            KeyboardDoubleTapService.ApplyYanmSettings(_appSettings.Yanm);
            if (!YarnSelectService.IsRunning && _appSettings.YarnSelect?.Enabled == true)
            {
                YarnSelectService.Start(HandleYarnSelectAction);
            }

            if (refreshYanmOverlay)
            {
                _yanmOverlay?.ReloadSettings();
            }

            RefreshYanmHotkeyRegistration();
            RefreshRadialHotkeyRegistration();
        }

        QueueCloudQuickPanelConfigSync(reason);
        if (reason.StartsWith("yanm-", StringComparison.OrdinalIgnoreCase))
        {
            QueueCloudYanmStateSync(reason);
        }
        QueueBackgroundWebDavSync($"config-{reason}");
    }

    public bool HasWebDavCredential()
    {
        var credential = WebDavCredentialStore.Load();
        return !string.IsNullOrWhiteSpace(credential?.Username) &&
               !string.IsNullOrWhiteSpace(credential?.Password);
    }

    public async Task<(bool ok, string message)> ProbeWebDavAsync()
    {
        try
        {
            var root = await Task.Run(async () =>
            {
                var service = new PersonalSyncService(AppSettingsStore.Load());
                await service.ProbeAsync();
                return service.SyncRootDisplay;
            });
            return (true, $"个人同步连接正常：{root}");
        }
        catch (Exception ex)
        {
            return (false, $"个人同步测试失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message)> SyncWebDavNowAsync()
    {
        try
        {
            var result = await Task.Run(async () =>
            {
                var service = new PersonalSyncService(AppSettingsStore.Load());
                return await service.SyncExtensionsAsync();
            });
            ApplyWebDavSyncResult(result);
            return (true, BuildPersonalSyncCompletedMessage(result));
        }
        catch (Exception ex)
        {
            return (false, $"个人扩展同步失败：{FormatExceptionMessage(ex)}");
        }
    }

    private static string BuildPersonalSyncCompletedMessage(WebDavSyncResult result, bool includeConfigSummary = true)
    {
        var packageSummary = result.UploadedCount == 0 && result.PulledCount == 0
            ? "扩展包已是最新"
            : $"扩展包上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个";
        if (!includeConfigSummary)
        {
            return $"个人扩展同步完成：{packageSummary}。";
        }

        var configSummary = result.ConfigPulled
            ? "，配置已拉取"
            : result.ConfigUploaded
                ? "，配置已上传"
                : "，配置无变化";
        return $"个人扩展同步完成：{packageSummary}{configSummary}。";
    }

    public async Task<(bool ok, string message, bool uploaded, bool pulled, int payloadBytes)> SyncYanmStateNowAsync()
    {
        try
        {
            var service = new PersonalSyncService(AppSettingsStore.Load());
            var result = await service.SyncYanmStateAsync();
            if (result.Pulled)
            {
                RefreshAppSettings();
                _quickPanel?.RefreshSettingsFromStore();
            }

            var action = result.Pulled ? "已拉取" : result.Uploaded ? "已上传" : "无变化";
            return (true, $"燕幕同步{action}，数据 {FormatBytes(result.PayloadBytes)}。", result.Uploaded, result.Pulled, result.PayloadBytes);
        }
        catch (Exception ex)
        {
            return (false, $"燕幕同步失败：{FormatExceptionMessage(ex)}", false, false, 0);
        }
    }

    private static string FormatBytes(int bytes)
    {
        return bytes < 1024
            ? $"{bytes} B"
            : $"{bytes / 1024.0:0.#} KB";
    }

    public bool TryUpdateLauncherHotkey(string shortcut, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut) || !TryParseHotkey(shortcut, out _, out _))
        {
            message = "快捷键格式无效。示例：Alt+Space 或 DoubleCtrl";
            return false;
        }

        var settings = AppSettingsStore.Load();
        var previous = settings.LauncherHotkey;
        settings.LauncherHotkey = shortcut.Trim();
        AppSettingsStore.Save(settings);

        if (!RefreshLauncherHotkeyRegistration())
        {
            settings.LauncherHotkey = previous;
            AppSettingsStore.Save(settings);
            RefreshLauncherHotkeyRegistration();
            message = "主程序快捷键注册失败，可能与系统或其他程序冲突。";
            return false;
        }

        message = $"主程序快捷键已更新为 {settings.LauncherHotkey}";
        return true;
    }

    public string GetYanmHotkey()
    {
        var yanm = AppSettingsStore.Load().Yanm;
        return yanm == null ? string.Empty : (yanm.ActivationKey.Equals(YanmActivationKeys.Custom, StringComparison.OrdinalIgnoreCase) ? yanm.CustomShortcut : yanm.ActivationKey);
    }

    public bool TryUpdateYanmHotkey(string shortcut, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut) || !TryParseHotkey(shortcut, out _, out _))
        {
            message = "快捷键格式无效。示例：Ctrl+Alt+Y";
            return false;
        }

        var settings = AppSettingsStore.Load();
        settings.Yanm ??= new YanmSettings();
        var previous = settings.Yanm.CustomShortcut;
        settings.Yanm.CustomShortcut = shortcut.Trim();
        settings.Yanm.ActivationKey = YanmActivationKeys.Custom;
        AppSettingsStore.Save(settings);

        if (!RefreshYanmHotkeyRegistration())
        {
            settings.Yanm.CustomShortcut = previous;
            AppSettingsStore.Save(settings);
            RefreshYanmHotkeyRegistration();
            RefreshRadialHotkeyRegistration();
            message = "燕幕快捷键注册失败，可能与系统或其他程序冲突。";
            return false;
        }

        KeyboardDoubleTapService.ApplyYanmSettings(settings.Yanm);
        message = $"燕幕快捷键已更新为 {settings.Yanm.CustomShortcut}";
        return true;
    }

    public bool TryUpdateRadialHotkey(string shortcut, out string message)
    {
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(shortcut) || !TryParseHotkey(shortcut, out _, out _))
        {
            message = "快捷键格式无效。示例：Ctrl+Alt+R";
            return false;
        }

        var settings = AppSettingsStore.Load();
        settings.RadialMenu ??= new RadialMenuSettings();
        var previous = settings.RadialMenu.CustomShortcut;
        settings.RadialMenu.CustomShortcut = shortcut.Trim();
        settings.RadialMenu.ActivationKey = RadialActivationKeys.Custom;
        AppSettingsStore.Save(settings);

        if (!RefreshRadialHotkeyRegistration())
        {
            settings.RadialMenu.CustomShortcut = previous;
            AppSettingsStore.Save(settings);
            RefreshRadialHotkeyRegistration();
            message = "燕环快捷键注册失败，可能与系统或其他程序冲突。";
            return false;
        }

        InputHookService.ReloadSettings();
        message = $"燕环快捷键已更新为 {settings.RadialMenu.CustomShortcut}";
        return true;
    }

}

public sealed record PersonalSyncGitCommitInfo(
    string Sha,
    string Message,
    string Author,
    DateTimeOffset CommittedAtUtc,
    string Url);
