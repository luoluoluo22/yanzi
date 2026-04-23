using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenQuickHost.Sync;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;

namespace OpenQuickHost;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const int HotKeyId = 0x5301;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const int WmHotKey = 0x0312;

    private readonly List<CommandItem> _allCommands;
    private readonly CloudSyncClient? _cloudSyncClient;
    private readonly SyncOptions _syncOptions;
    private readonly Dictionary<string, CommandItem> _localExtensionIndex;
    private CommandItem? _selectedCommand;
    private CommandItem? _lastActionableCommand;
    private string _activeQueryArgument = string.Empty;
    private string _lastRunMessage = "准备就绪。输入关键字后按 Enter 运行。";
    private string _syncStatus = "云同步未初始化。";
    private HwndSource? _source;
    private bool _authPromptActive;

    public MainWindow()
    {
        InitializeComponent();
        HostAssets.EnsureCreated();
        _syncOptions = SyncConfigLoader.Load();
        LocalExtensionCatalog.EnsureSampleExtension();
        if (_syncOptions.IsConfigured)
        {
            _cloudSyncClient = new CloudSyncClient(_syncOptions);
        }

        _allCommands = CreateSeedCommands();
        _allCommands.AddRange(LocalExtensionCatalog.LoadCommands());
        _localExtensionIndex = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension)
            .GroupBy(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        FilteredCommands = new ObservableCollection<CommandItem>(_allCommands);
        SelectedCommand = FilteredCommands.FirstOrDefault();
        DataContext = this;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
    }

    public ObservableCollection<CommandItem> FilteredCommands { get; }

    public bool AllowClose { get; set; }

    public CommandItem? SelectedCommand
    {
        get => _selectedCommand;
        set
        {
            if (Equals(value, _selectedCommand))
            {
                return;
            }

            _selectedCommand = value;
            if (value != null && !IsInternalCommand(value))
            {
                _lastActionableCommand = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveSelectedCommand));
            OnPropertyChanged(nameof(FooterHint));
        }
    }

    public string LastRunMessage
    {
        get => _lastRunMessage;
        set
        {
            if (value == _lastRunMessage)
            {
                return;
            }

            _lastRunMessage = value;
            OnPropertyChanged();
        }
    }

    public string VisibleCountText => $"{FilteredCommands.Count} 条结果";

    public string FooterHint => SelectedCommand == null
        ? "Up / Down 切换   Esc 收起"
        : SelectedCommand.SupportsQueryArgument && !string.IsNullOrWhiteSpace(_activeQueryArgument)
            ? $"{SelectedCommand.Title}   ·   {_activeQueryArgument}"
            : $"{SelectedCommand.Title}   ·   {SelectedCommand.Category}";

    public CommandItem? EffectiveSelectedCommand =>
        SelectedCommand == null
            ? null
            : IsInternalCommand(SelectedCommand) && _lastActionableCommand != null
                ? ResolveRunnableCommand(_lastActionableCommand)
                : ResolveRunnableCommand(SelectedCommand);

    public string SyncStatus
    {
        get => _syncStatus;
        set
        {
            if (value == _syncStatus)
            {
                return;
            }

            _syncStatus = value;
            OnPropertyChanged();
        }
    }

    public string SyncSummaryText =>
        _cloudSyncClient == null
            ? "云同步未配置"
            : $"用户 {_cloudSyncClient.CurrentUserLabel} · {_allCommands.Count(x => x.Source == CommandSource.Cloud)} 个云扩展";

    public string SyncBaseUrl => _syncOptions.BaseUrl;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();

        if (_cloudSyncClient == null)
        {
            return;
        }

        if (!AppSettingsStore.Load().RefreshCloudOnStartup)
        {
            return;
        }

        await RefreshCloudStateAsync();
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            RunSelectedCommand();
            e.Handled = true;
        }
    }

    private void CommandList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is CommandItem item)
        {
            SelectedCommand = item;
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        RunSelectedCommand();
    }

    private void CommandList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSelectedCommand();
            e.Handled = true;
        }
    }

    private void CommandList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dependencyObject = e.OriginalSource as DependencyObject;
        while (dependencyObject != null && dependencyObject is not ListBoxItem)
        {
            dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
        }

        if (dependencyObject is ListBoxItem item && item.DataContext is CommandItem command)
        {
            SelectedCommand = command;
            CommandList.SelectedItem = command;
        }
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        RunSelectedCommand();
    }

    private async void CreateDesktopShortcutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await CreateDesktopShortcutAsync();
    }

    private async void RenameCommandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RenameSelectedExtensionAsync();
    }

    private async void EditExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedExtensionAsync();
    }

    private async void DeleteExtensionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedExtensionAsync();
    }

    private async void RefreshCloudButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCloudStateAsync();
    }

    private async void SyncSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await SyncSelectedCommandAsync();
    }

    private async void DownloadSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        await DownloadSelectedCommandAsync();
    }

    private async void AddJsonExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await AddJsonExtensionAsync();
    }

    private async void EditJsonExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedExtensionAsync();
    }

    private async void DeleteExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedExtensionAsync();
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        SignOutFromSettings();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppSettingsStore.Load().CloseToTray)
        {
            HideToTray();
            return;
        }

        AllowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideToTray();
            return;
        }

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void CommandList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var current = SelectedCommand;
        var actionable = current != null && !IsInternalCommand(current) ? current : _lastActionableCommand;
        var resolved = actionable == null ? null : ResolveRunnableCommand(actionable);

        CreateDesktopShortcutMenuItem.IsEnabled = resolved?.OpenTarget is { Length: > 0 } && !IsInternalCommand(resolved);
        var canManageLocalExtension = resolved?.Source == CommandSource.LocalExtension;
        RenameCommandMenuItem.IsEnabled = canManageLocalExtension;
        EditExtensionMenuItem.IsEnabled = canManageLocalExtension;
        DeleteExtensionMenuItem.IsEnabled = canManageLocalExtension;
        if (current == null && resolved == null)
        {
            e.Handled = true;
        }
    }

    private void ApplyFilter(string? query)
    {
        var normalized = (query ?? string.Empty).Trim();
        _activeQueryArgument = string.Empty;
        var matches = string.IsNullOrWhiteSpace(normalized)
            ? _allCommands
            : _allCommands
                .Select(command => new
                {
                    Command = command,
                    Match = BuildCommandMatch(command, normalized)
                })
                .Where(x => x.Match.IsMatch)
                .OrderByDescending(x => x.Match.Priority)
                .ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Command)
                .ToList();

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var leadingCommand = matches.FirstOrDefault();
            if (leadingCommand != null)
            {
                _activeQueryArgument = ExtractQueryArgument(leadingCommand, normalized);
            }
        }

        FilteredCommands.Clear();
        foreach (var item in matches)
        {
            FilteredCommands.Add(item);
        }

        SelectedCommand = FilteredCommands.FirstOrDefault();
        CommandList.SelectedItem = SelectedCommand;
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(FooterHint));
    }

    private void MoveSelection(int delta)
    {
        if (FilteredCommands.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedCommand == null ? -1 : FilteredCommands.IndexOf(SelectedCommand);
        var nextIndex = currentIndex + delta;
        if (nextIndex < 0)
        {
            nextIndex = 0;
        }
        else if (nextIndex >= FilteredCommands.Count)
        {
            nextIndex = FilteredCommands.Count - 1;
        }

        SelectedCommand = FilteredCommands[nextIndex];
        CommandList.SelectedItem = SelectedCommand;
        CommandList.ScrollIntoView(SelectedCommand);
    }

    private void RunSelectedCommand()
    {
        if (SelectedCommand == null)
        {
            LastRunMessage = "没有可执行的命令。";
            return;
        }

        var runnable = ResolveRunnableCommand(SelectedCommand);
        if (HandleInternalCommand(runnable))
        {
            return;
        }

        var executionTarget = BuildExecutionTarget(runnable, SearchBox.Text);
        if (executionTarget is { Length: > 0 })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executionTarget,
                    UseShellExecute = true
                });
                HostAssets.AppendRecent(runnable.Title);
                HostAssets.AppendLog($"Executed command: {runnable.Title} -> {executionTarget}");
                LastRunMessage = $"已运行：{runnable.Title} -> {executionTarget}";
                return;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Command failed: {runnable.Title} -> {ex.Message}");
                LastRunMessage = $"运行失败：{runnable.Title}，{ex.Message}";
                return;
            }
        }

        HostAssets.AppendLog($"Command has no executable target: {runnable.Title}");
        LastRunMessage = runnable.Source == CommandSource.Cloud
            ? $"云端记录已存在，但当前机器没有安装对应扩展：{runnable.ExtensionId}。先下载扩展包或放入本地扩展目录。"
            : $"已模拟执行：{runnable.Title}。下一步可以把它接到插件执行器。";
    }

    private async Task RefreshCloudStateAsync()
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        try
        {
            SyncStatus = "正在读取云端扩展和用户同步状态...";
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            var me = await _cloudSyncClient.GetMeAsync();
            var health = await _cloudSyncClient.GetHealthAsync();
            var cloudExtensions = await _cloudSyncClient.GetExtensionsAsync();
            var userExtensions = await _cloudSyncClient.GetUserExtensionsAsync();
            MergeCloudCommands(cloudExtensions, userExtensions);
            var autoSyncedCount = await AutoSyncLocalExtensionsAsync();
            if (autoSyncedCount > 0)
            {
                cloudExtensions = await _cloudSyncClient.GetExtensionsAsync();
                userExtensions = await _cloudSyncClient.GetUserExtensionsAsync();
                MergeCloudCommands(cloudExtensions, userExtensions);
            }

            SyncStatus = $"已登录 {me?.Username ?? _cloudSyncClient.CurrentUserLabel}，服务时间 {health?.Now ?? "unknown"}";
            LastRunMessage = autoSyncedCount > 0
                ? $"已刷新云端扩展：{cloudExtensions.Count} 个，并自动同步了 {autoSyncedCount} 个本地扩展。"
                : $"已刷新云端扩展：{cloudExtensions.Count} 个，全局用户扩展：{userExtensions.Count} 条。";
            OnPropertyChanged(nameof(SyncSummaryText));
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await RefreshCloudStateAsync();
                return;
            }

            SyncStatus = $"云同步读取失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task SyncSelectedCommandAsync()
    {
        if (_cloudSyncClient == null)
        {
            SyncStatus = "云同步未配置，无法写入。";
            return;
        }

        if (SelectedCommand == null)
        {
            SyncStatus = "没有可同步的命令。";
            return;
        }

        try
        {
            if (!await EnsureAuthenticatedAsync())
            {
                return;
            }

            SyncStatus = $"正在同步 {SelectedCommand.Title} ...";
            var version = SelectedCommand.DeclaredVersion;
            await _cloudSyncClient.UpsertExtensionAsync(SelectedCommand);
            var packageBytes = ExtensionPackageService.BuildPackage(SelectedCommand, version);
            await _cloudSyncClient.UploadExtensionArchiveAsync(SelectedCommand, packageBytes, version);
            await _cloudSyncClient.UpsertUserExtensionAsync(SelectedCommand);
            SelectedCommand.MarkAsSynced(version);
            LastRunMessage = $"已把命令和扩展包同步到云端：{SelectedCommand.Title}";
            await RefreshCloudStateAsync();
        }
        catch (Exception ex)
        {
            if (await TryRecoverAuthenticationAsync(ex))
            {
                await SyncSelectedCommandAsync();
                return;
            }

            SyncStatus = $"同步失败：{FormatExceptionMessage(ex)}";
        }
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

    private async Task AddJsonExtensionAsync()
    {
        var command = ShowJsonExtensionEditorAsync(
            LocalExtensionCatalog.CreateTemplateJson(),
            isEditMode: false);
        if (command == null)
        {
            return;
        }

        LastRunMessage = $"已添加本地 JSON 扩展：{command.Title}";
        if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
        {
            await SyncCommandToCloudAsync(command);
            await RefreshCloudStateAsync();
        }
    }

    private async Task EditSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可编辑的扩展。";
            return;
        }

        var editable = ResolveRunnableCommand(sourceCommand);
        if (editable.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地 JSON 扩展，不能直接编辑。";
            return;
        }

        try
        {
            var manifestJson = LocalExtensionCatalog.LoadManifestJson(editable.ExtensionId);
            var updated = ShowJsonExtensionEditorAsync(manifestJson, isEditMode: true);
            if (updated == null)
            {
                return;
            }

            LastRunMessage = $"已更新本地 JSON 扩展：{updated.Title}";
            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(updated);
                await RefreshCloudStateAsync();
            }
        }
        catch (Exception ex)
        {
            SyncStatus = $"编辑失败：{FormatExceptionMessage(ex)}";
        }
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
            $"确认删除扩展“{deletable.Title}”吗？\n这会删除本地扩展目录，并卸载当前用户的云端安装记录。",
            "删除扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            LocalExtensionCatalog.DeleteExtension(deletable.ExtensionId);
            RemoveLocalExtensionCommand(deletable.ExtensionId);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = FilteredCommands.FirstOrDefault();
            CommandList.SelectedItem = SelectedCommand;

            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await _cloudSyncClient.RemoveUserExtensionAsync(deletable.ExtensionId);
                await RefreshCloudStateAsync();
            }

            LastRunMessage = $"已删除本地扩展：{deletable.Title}";
            SyncStatus = $"已删除扩展：{deletable.Title}";
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

    private async Task<bool> EnsureAuthenticatedAsync(bool forcePrompt = false)
    {
        if (_cloudSyncClient == null)
        {
            return false;
        }

        if (forcePrompt || !_cloudSyncClient.HasCredential)
        {
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

        _cloudSyncClient.ClearCredential();
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
            ShowPanel();
            var saved = SecureCredentialStore.Load();
            var dialog = new LoginWindow(saved?.Username);
            dialog.SendRegistrationCodeAsync = (email, username) => _cloudSyncClient.SendRegistrationCodeAsync(email, username);
            dialog.RegisterAsyncHandler = (email, username, password, code) => _cloudSyncClient.RegisterAsync(email, username, password, code);
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

            _cloudSyncClient.SetCredential(dialog.Username, dialog.Password, dialog.RememberCredential);
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

    private async Task<int> AutoSyncLocalExtensionsAsync()
    {
        if (_cloudSyncClient == null)
        {
            return 0;
        }

        var pendingCommands = _allCommands
            .Where(x => x.Source == CommandSource.LocalExtension && (!x.ExistsInCloud || !x.InstalledForUser))
            .ToList();
        if (pendingCommands.Count == 0)
        {
            return 0;
        }

        var syncedCount = 0;
        foreach (var command in pendingCommands)
        {
            await SyncCommandToCloudAsync(command);
            command.MarkAsSynced(command.DeclaredVersion);
            syncedCount++;
        }

        return syncedCount;
    }

    private static List<CommandItem> CreateSeedCommands()
    {
        return
        [
            new("谷", "谷歌", "输入“谷歌 关键词”后直接搜索。", "搜索", "#FF3B82F6", null, ["谷歌", "google", "guge", "gg", "搜索", "网页搜索"], queryPrefixes: ["谷歌", "guge", "google", "gg"], queryTargetTemplate: "https://www.google.com/search?q={query}"),
            new("设", "设置", "打开设置面板。", "命令", "#FF4F46E5", "oqh://settings", ["设置", "preferences", "config"]),
            new("加", "添加扩展", "添加一个单文件 JSON 扩展。", "命令", "#FF16A34A", "oqh://add-extension", ["扩展", "json", "添加"]),
            new("编", "编辑扩展", "编辑当前选中的本地扩展。", "命令", "#FF0284C7", "oqh://edit-extension", ["扩展", "编辑", "manifest"]),
            new("删", "删除扩展", "删除当前选中的本地扩展。", "命令", "#FFDC2626", "oqh://delete-extension", ["扩展", "删除", "remove"]),
            new("库", "应用库", "打开本地扩展目录。", "目录", "#FFE66A32", HostAssets.ExtensionsPath, ["应用", "扩展", "library"]),
            new("市", "插件市场", "打开本地扩展市场说明页。", "文件", "#FF9FE870", HostAssets.MarketplacePath, ["市场", "商店", "publish"]),
            new("工", "工作区", "打开开发工作区目录。", "目录", "#FFFACC15", @"F:\Desktop\kaifa", ["工作区", "folder"]),
            new("Q", "Quicker 动作目录", "打开当前 Quicker 相关开发目录。", "目录", "#FFA78BFA", @"F:\Desktop\kaifa\applanuch", ["quicker", "action"]),
            new("文", "帮助文档", "打开本地帮助文档。", "文件", "#FFFB7185", HostAssets.DocsReadmePath, ["文档", "help"]),
            new("近", "最近运行", "打开最近执行记录。", "文件", "#FF34D399", HostAssets.RecentCommandsPath, ["最近", "history"]),
            new("志", "日志", "打开日志文件。", "文件", "#FF60A5FA", HostAssets.HostLogPath, ["日志", "diagnose"])
        ];
    }

    private async Task SyncCommandToCloudAsync(CommandItem command)
    {
        if (_cloudSyncClient == null)
        {
            return;
        }

        var packageBytes = ExtensionPackageService.BuildPackage(command, command.DeclaredVersion);
        await _cloudSyncClient.UpsertExtensionAsync(command);
        await _cloudSyncClient.UploadExtensionArchiveAsync(command, packageBytes, command.DeclaredVersion);
        await _cloudSyncClient.UpsertUserExtensionAsync(command);
    }

    private void MergeCloudCommands(
        IReadOnlyList<CloudExtensionRecord> cloudExtensions,
        IReadOnlyList<UserExtensionRecord> userExtensions)
    {
        var userMap = userExtensions.ToDictionary(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase);

        foreach (var command in _allCommands)
        {
            command.ClearCloudData();
        }

        _allCommands.RemoveAll(x => x.Source == CommandSource.Cloud);
        var localById = _allCommands
            .Where(x => x.Source != CommandSource.Cloud)
            .ToDictionary(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase);

        foreach (var extension in cloudExtensions)
        {
            var extensionId = extension.ExtensionId;
            if (localById.TryGetValue(extensionId, out var existing))
            {
                existing.ApplyCloudData(
                    extension.DisplayName,
                    extension.LatestVersion,
                    true,
                    userMap.ContainsKey(extensionId),
                    extension.ArchiveKey);
                continue;
            }

            var cloudCommand = new CommandItem(
                glyph: "C",
                title: extension.DisplayName,
                subtitle: $"来自云端扩展库，扩展 ID：{extension.ExtensionId}",
                category: "云扩展",
                accentHex: "#FF4ADE80",
                openTarget: null,
                keywords: [extension.ExtensionId, extension.DisplayName, "cloud", "extension"],
                source: CommandSource.Cloud,
                extensionId: extension.ExtensionId);
            cloudCommand.ApplyCloudData(
                extension.DisplayName,
                extension.LatestVersion,
                true,
                userMap.ContainsKey(extensionId),
                extension.ArchiveKey);
            _allCommands.Add(cloudCommand);
        }

        ApplyFilter(SearchBox.Text);
        OnPropertyChanged(nameof(SyncSummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpsertLocalExtensionCommand(CommandItem command)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
        _allCommands.Add(command);
        _localExtensionIndex[command.ExtensionId] = command;
    }

    private void RemoveLocalExtensionCommand(string extensionId)
    {
        _allCommands.RemoveAll(x =>
            x.Source == CommandSource.LocalExtension &&
            x.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        _localExtensionIndex.Remove(extensionId);
    }

    private CommandItem? ShowJsonExtensionEditorAsync(string initialJson, bool isEditMode)
    {
        var currentJson = initialJson;

        while (true)
        {
            var dialog = new AddJsonExtensionWindow(currentJson, isEditMode)
            {
                Owner = this
            };
            var result = dialog.ShowDialog();
            if (result != true)
            {
                return null;
            }

            try
            {
                var command = LocalExtensionCatalog.SaveJsonExtension(dialog.JsonContent);
                UpsertLocalExtensionCommand(command);
                ApplyFilter(SearchBox.Text);
                SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(command.ExtensionId, StringComparison.OrdinalIgnoreCase));
                CommandList.SelectedItem = SelectedCommand;
                return command;
            }
            catch (Exception ex)
            {
                currentJson = dialog.JsonContent;
                var retryDialog = new AddJsonExtensionWindow(currentJson, isEditMode)
                {
                    Owner = this
                };
                retryDialog.ShowError(ex.Message);
                if (retryDialog.ShowDialog() != true)
                {
                    return null;
                }

                currentJson = retryDialog.JsonContent;
            }
        }
    }

    private CommandItem ResolveRunnableCommand(CommandItem command)
    {
        if (command.Source != CommandSource.Cloud)
        {
            return command;
        }

        return _localExtensionIndex.TryGetValue(command.ExtensionId, out var localExtension)
            ? localExtension
            : command;
    }

    public void ShowPanel()
    {
        ShowInTaskbar = true;
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    public void TogglePanelVisibility()
    {
        if (IsVisible)
        {
            HideToTray();
        }
        else
        {
            ShowPanel();
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source == null)
        {
            return;
        }

        _source.AddHook(WndProc);
        RegisterHotKey(_source.Handle, HotKeyId, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.Space));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            if (_source != null)
            {
                UnregisterHotKey(_source.Handle, HotKeyId);
                _source.RemoveHook(WndProc);
            }

            return;
        }

        if (!AppSettingsStore.Load().CloseToTray)
        {
            AllowClose = true;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            TogglePanelVisibility();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
            return true;
        }
        catch (Exception ex)
        {
            SyncStatus = $"登录失败：{FormatExceptionMessage(ex)}";
            return false;
        }
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

        _cloudSyncClient.ClearCredential();
        SyncStatus = "已退出登录。";
        OnPropertyChanged(nameof(SyncSummaryText));
    }

    public void RefreshAppSettings()
    {
        SyncStatus = AppSettingsStore.Load().RefreshCloudOnStartup
            ? SyncStatus
            : "设置已保存。启动时自动同步已关闭。";
    }

    private async Task CreateDesktopShortcutAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可创建快捷方式的命令。";
            return;
        }

        var command = ResolveRunnableCommand(sourceCommand);
        if (string.IsNullOrWhiteSpace(command.OpenTarget) || IsInternalCommand(command))
        {
            SyncStatus = "当前命令不支持创建桌面快捷方式。";
            return;
        }

        try
        {
            var path = DesktopShortcutService.CreateShortcut(command.Title, command.OpenTarget);
            LastRunMessage = $"已创建桌面快捷方式：{path}";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SyncStatus = $"创建快捷方式失败：{FormatExceptionMessage(ex)}";
        }
    }

    private async Task RenameSelectedExtensionAsync()
    {
        var sourceCommand = SelectedCommand != null && !IsInternalCommand(SelectedCommand)
            ? SelectedCommand
            : _lastActionableCommand;
        if (sourceCommand == null)
        {
            SyncStatus = "没有可重命名的扩展。";
            return;
        }

        var extension = ResolveRunnableCommand(sourceCommand);
        if (extension.Source != CommandSource.LocalExtension)
        {
            SyncStatus = "当前选中项不是本地扩展，不能直接重命名。";
            return;
        }

        var dialog = new SimpleTextInputWindow("重命名扩展", "输入新的扩展名称。", extension.Title)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var renamed = LocalExtensionCatalog.RenameExtension(extension.ExtensionId, dialog.ValueText);
            UpsertLocalExtensionCommand(renamed);
            ApplyFilter(SearchBox.Text);
            SelectedCommand = _allCommands.FirstOrDefault(x => x.ExtensionId.Equals(renamed.ExtensionId, StringComparison.OrdinalIgnoreCase));
            CommandList.SelectedItem = SelectedCommand;
            LastRunMessage = $"已重命名扩展：{renamed.Title}";

            if (_cloudSyncClient != null && await EnsureAuthenticatedAsync())
            {
                await SyncCommandToCloudAsync(renamed);
                await RefreshCloudStateAsync();
            }
        }
        catch (Exception ex)
        {
            SyncStatus = $"重命名失败：{FormatExceptionMessage(ex)}";
        }
    }

    private bool HandleInternalCommand(CommandItem command)
    {
        switch (command.OpenTarget)
        {
            case "oqh://settings":
                if (System.Windows.Application.Current is App app)
                {
                    app.OpenSettingsWindow();
                    LastRunMessage = "已打开设置。";
                }

                return true;
            case "oqh://add-extension":
                _ = AddJsonExtensionAsync();
                return true;
            case "oqh://edit-extension":
                _ = EditSelectedExtensionAsync();
                return true;
            case "oqh://delete-extension":
                _ = DeleteSelectedExtensionAsync();
                return true;
            default:
                return false;
        }
    }

    private static bool IsInternalCommand(CommandItem command)
    {
        return command.OpenTarget?.StartsWith("oqh://", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static CommandMatch BuildCommandMatch(CommandItem command, string query)
    {
        var argument = ExtractQueryArgument(command, query);
        if (command.SupportsQueryArgument && argument.Length > 0)
        {
            return new CommandMatch(true, 300);
        }

        if (command.SupportsQueryArgument && command.QueryPrefixes.Any(prefix =>
                prefix.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandMatch(true, 260);
        }

        if (command.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 220);
        }

        if (command.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 160);
        }

        if (command.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandMatch(true, 120);
        }

        if (command.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandMatch(true, 140);
        }

        return new CommandMatch(false, 0);
    }

    private static string ExtractQueryArgument(CommandItem command, string rawInput)
    {
        if (!command.SupportsQueryArgument || string.IsNullOrWhiteSpace(rawInput))
        {
            return string.Empty;
        }

        var input = rawInput.Trim();
        foreach (var prefix in command.QueryPrefixes)
        {
            if (input.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (input.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase))
            {
                return input[(prefix.Length + 1)..].Trim();
            }

            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && input.Length > prefix.Length)
            {
                return input[prefix.Length..].Trim();
            }
        }

        return string.Empty;
    }

    private static string? BuildExecutionTarget(CommandItem command, string? rawInput)
    {
        if (command.SupportsQueryArgument)
        {
            var argument = ExtractQueryArgument(command, rawInput ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(argument))
            {
                return command.QueryTargetTemplate!.Replace("{query}", Uri.EscapeDataString(argument), StringComparison.Ordinal);
            }
        }

        return command.OpenTarget;
    }
}

public readonly record struct CommandMatch(bool IsMatch, int Priority);

public sealed class CommandItem : INotifyPropertyChanged
{
    public CommandItem(
        string glyph,
        string title,
        string subtitle,
        string category,
        string accentHex,
        string? openTarget,
        IEnumerable<string> keywords,
        CommandSource source = CommandSource.Local,
        string? extensionId = null,
        string? declaredVersion = null,
        string? extensionDirectoryPath = null,
        IEnumerable<string>? queryPrefixes = null,
        string? queryTargetTemplate = null)
    {
        Glyph = glyph;
        Title = title;
        Subtitle = subtitle;
        Category = category;
        OpenTarget = openTarget;
        Keywords = keywords.ToArray();
        AccentBrush = (SolidColorBrush)new BrushConverter().ConvertFromString(accentHex)!;
        Source = source;
        ExtensionId = string.IsNullOrWhiteSpace(extensionId)
            ? CloudSyncClient.CreateExtensionId(this)
            : extensionId;
        DeclaredVersion = string.IsNullOrWhiteSpace(declaredVersion) ? "0.1.0" : declaredVersion;
        ExtensionDirectoryPath = extensionDirectoryPath;
        QueryPrefixes = queryPrefixes?.ToArray() ?? [];
        QueryTargetTemplate = queryTargetTemplate;
    }

    public string Glyph { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Category { get; }

    public System.Windows.Media.Brush AccentBrush { get; }

    public string? OpenTarget { get; }

    public IReadOnlyList<string> Keywords { get; }

    public CommandSource Source { get; }

    public string ExtensionId { get; }

    public string DeclaredVersion { get; }

    public string? ExtensionDirectoryPath { get; }

    public IReadOnlyList<string> QueryPrefixes { get; }

    public string? QueryTargetTemplate { get; }

    public bool SupportsQueryArgument => QueryPrefixes.Count > 0 && !string.IsNullOrWhiteSpace(QueryTargetTemplate);

    public string? CloudVersion { get; private set; }

    public bool ExistsInCloud { get; private set; }

    public bool InstalledForUser { get; private set; }

    public bool HasArchive { get; private set; }

    public string? LocalPackagePath { get; private set; }

    public string VersionLabel => string.IsNullOrWhiteSpace(CloudVersion) ? SourceLabel : $"v{CloudVersion}";

    public string ItemKindLabel => Source == CommandSource.Cloud ? "云端" : Category;

    public string CloudSummary =>
        ExistsInCloud
            ? InstalledForUser
                ? $"云端已收录，并已挂到当前用户。{ArchiveSummary} 来源：{SourceLabel}。"
                : $"云端已收录，但当前用户还没安装。{ArchiveSummary} 来源：{SourceLabel}。"
            : $"当前仅存在于本地。来源：{SourceLabel}。";

    private string SourceLabel => Source switch
    {
        CommandSource.Cloud => "云端",
        CommandSource.LocalExtension => "本地扩展",
        _ => "本地"
    };
    private string ArchiveSummary => HasArchive ? "已包含扩展包。" : "当前还没有扩展包。";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyCloudData(string? displayName, string? version, bool existsInCloud, bool installedForUser, string? archiveKey)
    {
        CloudVersion = version;
        ExistsInCloud = existsInCloud;
        InstalledForUser = installedForUser;
        HasArchive = !string.IsNullOrWhiteSpace(archiveKey);
        NotifyCloudChanged();
    }

    public void ClearCloudData()
    {
        CloudVersion = null;
        ExistsInCloud = false;
        InstalledForUser = false;
        HasArchive = false;
        NotifyCloudChanged();
    }

    public void MarkAsSynced(string? version)
    {
        CloudVersion = version ?? "0.1.0";
        ExistsInCloud = true;
        InstalledForUser = true;
        HasArchive = true;
        NotifyCloudChanged();
    }

    public void SetLocalPackagePath(string path)
    {
        LocalPackagePath = $"本地包：{path}";
        NotifyCloudChanged();
    }

    private void NotifyCloudChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CloudVersion)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExistsInCloud)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstalledForUser)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasArchive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VersionLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CloudSummary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalPackagePath)));
    }
}

public enum CommandSource
{
    Local,
    LocalExtension,
    Cloud
}
