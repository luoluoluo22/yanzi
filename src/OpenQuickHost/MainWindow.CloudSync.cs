using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using OpenQuickHost.Sync;
using Forms = System.Windows.Forms;

namespace OpenQuickHost;

public partial class MainWindow
{
    private const string PublicStoreOrigin = "https://yanzi.luoluoluo.cc.cd";

    public static string BuildExtensionStoreUrl(string extensionId)
    {
        return $"{PublicStoreOrigin}/store.html?id={Uri.EscapeDataString(extensionId ?? string.Empty)}";
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
            $"确认将扩展“{deletable.Title}”移入回收站吗？\n如果已启用坚果云/WebDAV，同步器会在后台把这次删除同步到其他设备。",
            "移入扩展回收站",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            WebDavSyncService.MarkExtensionDeletedLocally(deletable.ExtensionId, deletable.DeclaredVersion);
            ExtensionRecycleBinService.MoveToRecycleBin(deletable.ExtensionId);
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
            if (IsVisible)
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
        if (_mobileMessageBridgeTask is not { IsCompleted: false })
        {
            _mobileMessageBridgeCts?.Cancel();
            _mobileMessageBridgeCts = new CancellationTokenSource();
            _mobileMessageBridgeTask = Task.Run(() => MobileMessageBridgeLoopAsync(_mobileMessageBridgeCts.Token));
        }

        HostAssets.AppendLog($"Mobile bridge started: reason={reason}, deviceId={_desktopDeviceId}.");
        _ = PollMobileMessagesSafeAsync($"start-{reason}");
    }

    private async Task MobileMessageBridgeLoopAsync(CancellationToken cancellationToken)
    {
        HostAssets.AppendLog("Mobile bridge background loop started.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await PollMobileMessagesSafeAsync("background-loop");
            }
        }
        catch (OperationCanceledException)
        {
            HostAssets.AppendLog("Mobile bridge background loop stopped.");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile bridge background loop crashed: {FormatExceptionMessage(ex)}");
        }
    }

    private async void MobileMessagePollTimer_Tick(object? sender, EventArgs e)
    {
        await PollMobileMessagesSafeAsync("timer");
    }

    private async Task PollMobileMessagesSafeAsync(string reason)
    {
        if (_mobileMessagePollRunning || _cloudSyncClient == null || !_cloudSyncClient.HasCredential)
        {
            if (_mobileMessagePollRunning)
            {
                HostAssets.AppendLog($"Mobile bridge poll skipped: reason={reason}, previous poll still running.");
            }
            return;
        }

        _mobileMessagePollRunning = true;
        try
        {
            using var pollTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _desktopDeviceId ??= DeviceIdentityStore.GetOrCreateDesktopDeviceId();
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
                await HandleMobileDeviceMessageAsync(message);
                await _cloudSyncClient.AckDeviceMessageAsync(message.MessageId, _desktopDeviceId, pollTimeout.Token);
                HostAssets.AppendLog($"Mobile bridge acked message: id={message.MessageId}, deviceId={_desktopDeviceId}.");
            }
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
    }

    private async Task HandleMobileDeviceMessageAsync(DeviceMessageRecord message)
    {
        var title = string.IsNullOrWhiteSpace(message.Title) ? "手机发来消息" : message.Title.Trim();
        var text = string.IsNullOrWhiteSpace(message.Text) ? $"消息类型：{message.Kind}" : message.Text.Trim();
        var sourceLabel = GetMobileSourceLabel(message);
        var screenshotDataUrl = GetPayloadString(message, "screenshotDataUrl");
        var screenshotFilePath = await TryDownloadMobileScreenshotFromWebDavAsync(message);
        if (string.Equals(message.Kind, "screenshot", StringComparison.OrdinalIgnoreCase))
        {
            var payloadKeys = message.Payload.Count == 0
                ? "(empty)"
                : string.Join(",", message.Payload.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
            HostAssets.AppendLog(
                $"Mobile screenshot payload: id={message.MessageId}, keys={payloadKeys}, hasDataUrl={!string.IsNullOrWhiteSpace(screenshotDataUrl)}, webDavPath={GetPayloadString(message, "webDavPath") ?? "(none)"}, localFile={screenshotFilePath ?? "(none)"}.");
        }
        HostAssets.AppendLog(
            $"Mobile bridge message: id={message.MessageId}, source={sourceLabel}, kind={message.Kind}, text={trimForLog(text)}");

        await Dispatcher.InvokeAsync(() =>
        {
            if (TryHandleMobileRunExtensionMessage(message, text))
            {
                return;
            }

            LastRunMessage = $"{title}：{text}";
            SyncStatus = "已收到手机端消息。";
            SaveMobileInboxMessage(message, title, text, sourceLabel, screenshotDataUrl, screenshotFilePath);
            ShowMobileMessageToast(title, text, sourceLabel, screenshotDataUrl, screenshotFilePath);
        });
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
            if (!service.IsConfigured)
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

            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            var filePath = Path.Combine(downloads, $"yanzi-mobile-screenshot-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.jpg");
            await File.WriteAllBytesAsync(filePath, bytes, timeout.Token);
            HostAssets.AppendLog($"Mobile screenshot WebDAV downloaded: path={remotePath}, local={filePath}, bytes={bytes.Length}.");
            return filePath;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Mobile screenshot WebDAV download failed: path={remotePath}, {FormatExceptionMessage(ex)}");
            return null;
        }
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

    private bool TryHandleMobileRunExtensionMessage(DeviceMessageRecord message, string inputText)
    {
        if (!string.Equals(message.Kind, "run-extension", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!message.Payload.TryGetValue("extensionId", out var extensionElement))
        {
            return false;
        }

        var extensionId = extensionElement.ValueKind == JsonValueKind.String
            ? extensionElement.GetString()
            : extensionElement.ToString();
        if (string.IsNullOrWhiteSpace(extensionId) ||
            !_localExtensionIndex.TryGetValue(extensionId, out var command))
        {
            LastRunMessage = $"手机请求的扩展不存在：{extensionId}";
            return true;
        }

        ExecuteCommandExternally(command, inputText, "mobile");
        LastRunMessage = $"已执行手机端请求：{command.Title}";
        return true;
    }

    private static string trimForLog(string value)
    {
        var text = value.ReplaceLineEndings(" ").Trim();
        return text.Length <= 160 ? text : $"{text[..160]}...";
    }

    private async Task SyncPersonalWebDavAsync(bool showDisabledMessage)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWebDavSync)
        {
            if (showDisabledMessage)
            {
                SyncStatus = "未启用个人 WebDAV 扩展同步。";
            }

            return;
        }

        try
        {
            var service = new WebDavSyncService(settings);
            var result = await service.SyncExtensionsAsync();
            ApplyWebDavSyncResult(result);
            LastRunMessage = $"个人扩展同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。";
        }
        catch (Exception ex)
        {
            SyncStatus = $"个人扩展同步失败：{FormatExceptionMessage(ex)}";
        }
    }

    private void StartBackgroundWebDavSync()
    {
        if (AppSettingsStore.Load().EnableWebDavSync && !_backgroundWebDavSyncTimer.IsEnabled)
        {
            _backgroundWebDavSyncTimer.Start();
        }
    }

    private void QueueBackgroundWebDavSync(string reason)
    {
        var settings = AppSettingsStore.Load();
        if (!settings.EnableWebDavSync)
        {
            return;
        }

        StartBackgroundWebDavSync();
        if (_backgroundWebDavSyncRunning)
        {
            _backgroundWebDavSyncRequested = true;
            HostAssets.AppendLog($"WebDAV background sync queued while running: {reason}");
            return;
        }

        _ = RunBackgroundWebDavSyncAsync(reason);
    }

    private async Task RunBackgroundWebDavSyncAsync(string reason)
    {
        _backgroundWebDavSyncRunning = true;
        try
        {
            HostAssets.AppendLog($"WebDAV background sync started: {reason}");
            var settings = AppSettingsStore.Load();
            var result = await Task.Run(async () =>
            {
                var service = new WebDavSyncService(settings);
                return await service.SyncExtensionsAsync();
            });
            ApplyWebDavSyncResult(result);
            SyncStatus = $"个人扩展后台同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个。";
            HostAssets.AppendLog($"WebDAV background sync completed: {reason}, uploaded={result.UploadedCount}, pulled={result.PulledCount}, configUploaded={result.ConfigUploaded}, configPulled={result.ConfigPulled}");
        }
        catch (Exception ex)
        {
            var message = FormatExceptionMessage(ex);
            SyncStatus = $"个人扩展后台同步失败：{message}";
            HostAssets.AppendLog($"WebDAV background sync failed: {reason} -> {message}");
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

    private async Task<bool> PullWebDavConfigFromCloudAsync()
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        var snapshot = await _cloudSyncClient.GetUserConfigAsync<CloudWebDavConfigSnapshot>(CloudWebDavConfigId);
        if (snapshot == null)
        {
            HostAssets.AppendLog("WebDAV cloud pull: no user config found.");
            if (ShouldSyncLocalWebDavConfigToCloud())
            {
                await PushWebDavConfigToCloudAsync("cloud-refresh-bootstrap");
            }
            return false;
        }

        HostAssets.AppendLog(
            $"WebDAV cloud pull: enabled={snapshot.EnableWebDavSync}, serverUrl={snapshot.WebDavServerUrl}, rootPath={snapshot.WebDavRootPath}, username={snapshot.WebDavUsername}, hasPassword={!string.IsNullOrWhiteSpace(snapshot.WebDavPassword)}");

        var settings = AppSettingsStore.Load();
        var shouldDefaultEnable = snapshot.EnableWebDavSync || HasWebDavConfigValues(snapshot.WebDavServerUrl, snapshot.WebDavRootPath, snapshot.WebDavUsername, snapshot.WebDavPassword);
        var resolvedEnabled = settings.WebDavSyncManuallyDisabled ? false : shouldDefaultEnable;
        var changed =
            settings.EnableWebDavSync != resolvedEnabled ||
            !string.Equals(settings.WebDavServerUrl, snapshot.WebDavServerUrl, StringComparison.Ordinal) ||
            !string.Equals(settings.WebDavRootPath, snapshot.WebDavRootPath, StringComparison.Ordinal) ||
            !string.Equals(settings.WebDavUsername, snapshot.WebDavUsername, StringComparison.Ordinal);
        var credential = WebDavCredentialStore.Load();
        var passwordChanged = !string.Equals(credential?.Password, snapshot.WebDavPassword, StringComparison.Ordinal);
        if (!changed)
        {
            if (passwordChanged && !string.IsNullOrWhiteSpace(snapshot.WebDavPassword))
            {
                HostAssets.AppendLog("WebDAV cloud pull: applying password-only update.");
                SaveWebDavCredential(snapshot.WebDavUsername ?? string.Empty, snapshot.WebDavPassword);
                NotifySettingsWindowWebDavConfigChanged();
                return true;
            }

            HostAssets.AppendLog("WebDAV cloud pull: no local changes detected.");
            return false;
        }

        settings.EnableWebDavSync = resolvedEnabled;
        settings.WebDavServerUrl = string.IsNullOrWhiteSpace(snapshot.WebDavServerUrl)
            ? settings.WebDavServerUrl
            : snapshot.WebDavServerUrl.Trim();
        settings.WebDavRootPath = string.IsNullOrWhiteSpace(snapshot.WebDavRootPath)
            ? "/yanzi"
            : snapshot.WebDavRootPath.Trim();
        settings.WebDavUsername = snapshot.WebDavUsername?.Trim() ?? string.Empty;
        if (resolvedEnabled)
        {
            settings.WebDavSyncManuallyDisabled = false;
        }
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        if (!string.IsNullOrWhiteSpace(snapshot.WebDavPassword))
        {
            SaveWebDavCredential(snapshot.WebDavUsername ?? string.Empty, snapshot.WebDavPassword);
        }
        if (settings.EnableWebDavSync)
        {
            StartBackgroundWebDavSync();
        }
        else
        {
            _backgroundWebDavSyncTimer.Stop();
        }

        HostAssets.AppendLog(
            $"WebDAV cloud pull applied: enabled={settings.EnableWebDavSync}, serverUrl={settings.WebDavServerUrl}, rootPath={settings.WebDavRootPath}, username={settings.WebDavUsername}, passwordSaved={!string.IsNullOrWhiteSpace(snapshot.WebDavPassword)}");
        NotifySettingsWindowWebDavConfigChanged();
        return true;
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
            HostAssets.AppendLog($"Cloud WebDAV config synced: {reason}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Cloud WebDAV config sync skipped: {reason} -> {FormatExceptionMessage(ex)}");
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
            HostAssets.AppendLog($"WebDAV cloud push skipped: {reason}");
            return;
        }

        await _cloudSyncClient.EnsureAuthenticatedAsync();
        var settings = AppSettingsStore.Load();
        var credential = WebDavCredentialStore.Load();
        HostAssets.AppendLog(
            $"WebDAV cloud push: reason={reason}, enabled={settings.EnableWebDavSync}, serverUrl={settings.WebDavServerUrl}, rootPath={settings.WebDavRootPath}, username={settings.WebDavUsername}, hasPassword={!string.IsNullOrWhiteSpace(credential?.Password)}");
        await _cloudSyncClient.UpsertUserConfigAsync(CloudWebDavConfigId, new CloudWebDavConfigSnapshot
        {
            EnableWebDavSync = settings.EnableWebDavSync,
            WebDavServerUrl = settings.WebDavServerUrl,
            WebDavRootPath = settings.WebDavRootPath,
            WebDavUsername = settings.WebDavUsername,
            WebDavPassword = credential?.Password
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
        return !string.IsNullOrWhiteSpace(settings.WebDavServerUrl) &&
               !string.IsNullOrWhiteSpace(settings.WebDavRootPath) &&
               !string.IsNullOrWhiteSpace(settings.WebDavUsername);
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
        WebDavCredentialStore.Save(new SavedWebDavCredential
        {
            Username = username.Trim(),
            Password = password
        });

        var settings = AppSettingsStore.Load();
        settings.WebDavUsername = username.Trim();
        AppSettingsStore.Save(settings);
        _appSettings = settings;
        _windowBoundExtensionsService.Reload(_appSettings.WindowBindings);
        StartBackgroundWebDavSync();
        QueueBackgroundWebDavSync("credential-saved");
        QueueCloudWebDavConfigSync("credential-saved");
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
            var service = new WebDavSyncService(AppSettingsStore.Load());
            await service.ProbeAsync();
            return (true, $"WebDAV 连接正常：{service.SyncRootDisplay}");
        }
        catch (Exception ex)
        {
            return (false, $"WebDAV 测试失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message)> SyncWebDavNowAsync()
    {
        try
        {
            var service = new WebDavSyncService(AppSettingsStore.Load());
            var result = await service.SyncExtensionsAsync();
            ApplyWebDavSyncResult(result);
            var configSummary = result.ConfigPulled
                ? "，配置已拉取"
                : result.ConfigUploaded
                    ? "，配置已上传"
                    : string.Empty;
            return (true, $"个人扩展同步完成：上传 {result.UploadedCount} 个，拉取 {result.PulledCount} 个{configSummary}。");
        }
        catch (Exception ex)
        {
            return (false, $"个人扩展同步失败：{FormatExceptionMessage(ex)}");
        }
    }

    public async Task<(bool ok, string message, bool uploaded, bool pulled, int payloadBytes)> SyncYanmStateNowAsync()
    {
        try
        {
            var service = new WebDavSyncService(AppSettingsStore.Load());
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
