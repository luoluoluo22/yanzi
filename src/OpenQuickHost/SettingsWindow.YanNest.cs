using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using OpenQuickHost.Sync;
namespace OpenQuickHost;
public partial class SettingsWindow
{
    private DispatcherTimer? _nestTimer;
    private CloudSyncClient? _nestCloud;
    private string? _nestAccount;
    private bool _nestBusy;
    private bool _nestDeviceMutation;
    private bool _nestSelectionMode;
    public Visibility NestSelectionVisibility => _nestSelectionMode ? Visibility.Visible : Visibility.Collapsed;
    private NestDeviceRow[] NestRows => (NestDeviceList.ItemsSource as NestDeviceRow[]) ?? [];
    private bool _nestOnline;
    private bool _nestMobileOnline;
    private static readonly System.Net.Http.HttpClient NestLocalAgentHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(2.5)
    };
    public bool NestOnline { get => _nestOnline; private set { _nestOnline=value; OnPropertyChanged(); } }
    public bool NestMobileOnline { get => _nestMobileOnline; private set { _nestMobileOnline=value; OnPropertyChanged(); } }
    private void YanNestAccessContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (YanNestAccessContent.Content == null && System.Windows.Application.Current is App app) YanNestAccessContent.Content = app.CreateExternalAccessPanel();
        if (_nestTimer != null) return;
        _nestTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _nestTimer.Tick += async (_, _) => await RefreshNestAsync();
        Closed += (_, _) => _nestTimer.Stop();_nestTimer.Start();_ = RefreshNestAsync();
    }
    private async Task RefreshNestAsync()
    {
        if (_nestBusy || _nestDeviceMutation || !IsVisible || !IsYanwoSelected) return;
        _nestBusy = true;
        NestDeviceRefreshButton.IsEnabled = false;
        NestDeviceBatchButton.IsEnabled = false;
        NestDeviceList.IsEnabled = false;
        try
        {
            await RefreshNestAgentApiAsync();
            var account = SyncSessionStore.Load()?.UserId;
            NestSignOutButton.IsEnabled = !string.IsNullOrEmpty(account);
            if (string.IsNullOrEmpty(account)) { ClearNestDevices(); NestOnline=false;NestMobileOnline=false;NestCloudStatus.Text = "未登录 · 公网消息与数据需要燕子账号"; MobileStatusText.Text = _settings.EnableLanSync ? "局域网发现已开启 · 公网未登录" : "局域网发现关闭 · 公网未登录"; MobileStatusText.Tag = "Disconnected"; return; }
            if (_nestAccount != account) { NestDeviceList.ItemsSource = null; NestDeviceActionStatus.Text = ""; }
            if (_nestCloud == null || _nestAccount != account) { _nestCloud = new CloudSyncClient(SyncConfigLoader.Load()); _nestAccount = account; }
            var devices = await _nestCloud.ListPeerDevicesAsync();
            if (account != SyncSessionStore.Load()?.UserId) return;
            var online = devices.Count(x => x.Platform == "android" && x.Online);
            var own = DeviceIdentityStore.GetOrCreateDesktopDeviceId();
            var selected = NestRows.Where(x => x.IsSelected && x.AccountId == account).Select(x => x.DeviceId).ToHashSet();
            var nameCounts = devices.GroupBy(x => (x.Platform, x.DisplayName)).ToDictionary(x => x.Key, x => x.Count());
            NestDeviceList.ItemsSource = devices.OrderByDescending(x => x.DeviceId == own).ThenBy(x => x.DisplayName).ThenByDescending(x => x.Online)
                .Select(x => new NestDeviceRow(x, account, x.DeviceId == own, nameCounts[(x.Platform, x.DisplayName)]) { IsSelected = x.DeviceId != own && selected.Contains(x.DeviceId) }).ToArray();
            UpdateNestSelection();
            NestDeviceSummary.Text = devices.Count == 0 ? "账号下暂无设备登记记录。" : $"登记记录 {devices.Count} 条 · 在线 {devices.Count(x => x.Online)} 条 · 离线 {devices.Count(x => !x.Online)} 条";
            NestCloudStatus.Text = $"设备登记 {devices.Count} 条 · 在线手机 {online} 台";
            NestOnline=true;NestMobileOnline=online>0;
            MobileStatusText.Text = $"公网在线 {online} 台 · 局域网发现{(_settings.EnableLanSync ? "开" : "关")}";
            MobileStatusText.Tag = online > 0 ? "Connected" : "Disconnected";
            MobileStatusDot.Fill = new SolidColorBrush(online > 0 ? Colors.LimeGreen : Colors.Gray);
            MobileStatusText.Foreground = MobileStatusDot.Fill;
            if (MobileToolTipStatusText != null) MobileToolTipStatusText.Text = MobileStatusText.Text;
        }
        catch { NestDeviceList.ItemsSource = null; NestDeviceSummary.Text = "设备列表暂不可用，请刷新重试。"; NestOnline=false;NestMobileOnline=false;NestCloudStatus.Text = "公网状态暂不可用 · 稍后自动重试"; MobileStatusText.Text = "公网状态未知"; MobileStatusText.Tag = "Disconnected"; MobileStatusDot.Fill = new SolidColorBrush(Colors.Gray); }
        finally { _nestBusy = false; NestDeviceRefreshButton.IsEnabled = !_nestDeviceMutation; NestDeviceBatchButton.IsEnabled = !_nestDeviceMutation && NestRows.Length > 0; NestDeviceList.IsEnabled = !_nestDeviceMutation; UpdateNestSelection(); }
    }

    private async Task RefreshNestAgentApiAsync()
    {
        if (!_settings.EnableAgentApi)
        {
            NestApiStatus.Text = "已禁用";
            AiCapabilityStatusText.Text = "本地 API 已禁用";
            AiCapabilityStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            AiCapabilityStatusDot.Fill = new SolidColorBrush(Colors.Gray);
            AiCapabilitySummaryText.Text = "启用“API接口”后，AI 才能发现并调用燕子能力。";
            AiCapabilityManageButton.IsEnabled = false;
            return;
        }

        var baseUrl = $"http://127.0.0.1:{_settings.AgentApiPort}";
        try
        {
            using var health = await NestLocalAgentHttp.GetAsync(baseUrl + "/health");
            if (!health.IsSuccessStatusCode)
                throw new System.Net.Http.HttpRequestException($"HTTP {(int)health.StatusCode}");

            NestApiStatus.Text = $"已启动 · {_settings.AgentApiPort}";
            AiCapabilityStatusText.Text = "可用";
            AiCapabilityStatusText.Foreground = new SolidColorBrush(Colors.LimeGreen);
            AiCapabilityStatusDot.Fill = new SolidColorBrush(Colors.LimeGreen);
            AiCapabilityManageButton.IsEnabled = true;
        }
        catch
        {
            NestApiStatus.Text = $"未连接 · {_settings.AgentApiPort}";
            AiCapabilityStatusText.Text = "本地 API 未连接";
            AiCapabilityStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            AiCapabilityStatusDot.Fill = new SolidColorBrush(Colors.Gray);
            AiCapabilitySummaryText.Text = "本地 Agent API 当前不可达，能力目录暂不可用。";
            AiCapabilityManageButton.IsEnabled = false;
            return;
        }

        try
        {
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Get, baseUrl + "/v1/agent/catalog");
            request.Headers.TryAddWithoutValidation("X-Yanzi-Token", _settings.AgentApiToken);
            using var response = await NestLocalAgentHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                AiCapabilitySummaryText.Text = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "本地 API 已启动，但能力目录认证失败。"
                    : $"能力目录暂不可用 · HTTP {(int)response.StatusCode}";
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            var extensionCount = 0;
            var totalCount = 0;
            var availableCount = 0;

            if (root.TryGetProperty("extensions", out var extensions) &&
                extensions.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                extensionCount = extensions.GetArrayLength();
                foreach (var extension in extensions.EnumerateArray())
                {
                    if (!extension.TryGetProperty("capabilities", out var capabilities) ||
                        capabilities.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                    foreach (var capability in capabilities.EnumerateArray())
                    {
                        totalCount++;
                        var availableNow = capability.TryGetProperty("available", out var available) &&
                            available.ValueKind == System.Text.Json.JsonValueKind.True;
                        var onDemand = capability.TryGetProperty("onDemand", out var onDemandValue) &&
                            onDemandValue.ValueKind == System.Text.Json.JsonValueKind.True;
                        if (availableNow || onDemand) availableCount++;
                    }
                }
            }

            if (root.TryGetProperty("hostCapabilities", out var hostCapabilities) &&
                hostCapabilities.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var capability in hostCapabilities.EnumerateArray())
                {
                    totalCount++;
                    if (capability.TryGetProperty("available", out var available) &&
                        available.ValueKind == System.Text.Json.JsonValueKind.True) availableCount++;
                }
            }

            AiCapabilitySummaryText.Text =
                $"已发现 {totalCount} 项能力 · 当前可调用 {availableCount} 项 · {extensionCount} 个已安装小程序";
        }
        catch (Exception error)
        {
            AiCapabilitySummaryText.Text = "能力目录读取失败：" + error.Message;
        }
    }

    private async void RefreshAiCapabilities_Click(object sender, RoutedEventArgs e)
    {
        await RefreshNestAgentApiAsync();
    }

    private void ClearNestDevices()
    {
        NestDeviceList.ItemsSource = null;
        NestDeviceSummary.Text = "登录燕子账号后查看历史登记设备。";
        NestSignOutButton.IsEnabled = false;
        SetNestSelectionMode(false);
        _nestAccount = null;
        _nestCloud = null;
    }

    private async void NestDeviceManagement_Click(object sender, RoutedEventArgs e)
    {
        var expanded = NestDevicePanel.Visibility != Visibility.Visible;
        NestDevicePanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        NestDeviceManagementButton.Content = expanded ? "收起设备管理 ▴" : "设备管理 ▾";
        if (expanded) await RefreshNestAsync();
    }

    private async void NestDeviceRefresh_Click(object sender, RoutedEventArgs e) => await RefreshNestAsync();

    private void SetNestSelectionMode(bool enabled)
    {
        _nestSelectionMode = enabled;
        if (!enabled) foreach (var row in NestRows) row.IsSelected = false;
        NestDeviceSelectionBar.Visibility = NestSelectionVisibility;
        NestDeviceBatchButton.Content = enabled ? "取消多选" : "一键删除";
        OnPropertyChanged(nameof(NestSelectionVisibility));
        UpdateNestSelection();
    }
    private void NestDeviceBatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_nestDeviceMutation && !_nestBusy) SetNestSelectionMode(!_nestSelectionMode);
    }
    private void UpdateNestSelection()
    {
        var count = NestRows.Count(x => x.CanSelect && x.IsSelected);
        NestDeviceDeleteSelectedButton.Content = $"删除选中 ({count})";
        NestDeviceDeleteSelectedButton.IsEnabled = count > 0 && !_nestBusy && !_nestDeviceMutation;
    }
    private void NestDeviceSelectionChanged(object sender, RoutedEventArgs e)
    {
        // Checked fires before a two-way binding can commit its source value.
        if (sender is System.Windows.Controls.CheckBox { DataContext: NestDeviceRow row } check) row.IsSelected = row.CanSelect && check.IsChecked == true;
        UpdateNestSelection();
    }
    private void NestDeviceSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in NestRows) row.IsSelected = row.CanSelect;
        UpdateNestSelection();
    }
    private void NestDeviceSelectOffline_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in NestRows) row.IsSelected = row.CanSelect && !row.Online;
        UpdateNestSelection();
    }
    private async void NestDeviceDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_nestBusy || _nestDeviceMutation) return;
        var account = SyncSessionStore.Load()?.UserId;
        var rows = NestRows.Where(x => x.CanSelect && x.IsSelected && x.AccountId == account).ToArray();
        if (rows.Length == 0) return;
        var names = string.Join("\n", rows.Take(8).Select(x => $"• {x.Name} · {x.DeviceId[Math.Max(0, x.DeviceId.Length - 8)..]}"));
        if (rows.Length > 8) names += $"\n以及其他 {rows.Length - 8} 条记录";
        if (System.Windows.MessageBox.Show(this, $"删除选中的 {rows.Length} 条设备登记？其中 {rows.Count(x => x.Online)} 条在线。\n\n{names}\n\n将停用对应消息和直连授权，设备文件会保留。", "批量删除设备", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var cloud = _nestCloud;
        if (cloud == null || account != SyncSessionStore.Load()?.UserId) return;
        _nestDeviceMutation = true;
        NestDeviceList.IsEnabled = false;
        NestDeviceSelectionBar.IsEnabled = false;
        NestDeviceBatchButton.IsEnabled = false;
        NestDeviceRefreshButton.IsEnabled = false;
        NestSignOutButton.IsEnabled = false;
        var removed = 0;
        var failures = new List<string>();
        try
        {
            foreach (var row in rows)
            {
                if (account != SyncSessionStore.Load()?.UserId) break;
                NestDeviceActionStatus.Text = $"正在删除 {removed + failures.Count + 1}/{rows.Length}…";
                try
                {
                    await cloud.RemovePeerDeviceAsync(row.DeviceId);
                    foreach (var pair in YanziLanPairing.List().Where(x => x.DeviceId == row.DeviceId && x.OwnerAccount == account)) YanziLanPairing.Revoke(pair.PairId);
                    YanziPeerRegistry.Remove(row.DeviceId);
                    row.IsSelected = false;
                    removed++;
                }
                catch (Exception error) { failures.Add(row.Name + "：" + error.Message); }
            }
            if (account == SyncSessionStore.Load()?.UserId)
                NestDeviceActionStatus.Text = $"已删除 {removed} 条登记。" + (failures.Count == 0 ? "" : $" {failures.Count} 条未完成，可重试。\n" + string.Join("\n", failures));
            if (failures.Count == 0) SetNestSelectionMode(false);
        }
        finally
        {
            _nestDeviceMutation = false;
            NestDeviceList.IsEnabled = true;
            NestDeviceSelectionBar.IsEnabled = true;
            NestDeviceBatchButton.IsEnabled = true;
            NestDeviceRefreshButton.IsEnabled = true;
            NestSignOutButton.IsEnabled = SyncSessionStore.Load() != null;
            UpdateNestSelection();
        }
        await RefreshNestAsync();
    }

    private async void NestSignOut_Click(object sender, RoutedEventArgs e)
    {
        if (_nestDeviceMutation || SyncSessionStore.Load() == null) return;
        if (System.Windows.MessageBox.Show(this, "退出这台电脑的燕子账号？\n本地小程序和文件会保留，账号同步与公网连接将停止。", "退出本机登录", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await SignOutAsync();
        ClearNestDevices();
        NestDeviceActionStatus.Text = "已退出本机登录。";
        await RefreshNestAsync();
    }

    private async void NestDeviceAction_Click(object sender, RoutedEventArgs e)
    {
        if (_nestDeviceMutation || sender is not System.Windows.Controls.Button { Tag: NestDeviceRow row }) return;
        if (row.AccountId != SyncSessionStore.Load()?.UserId) { await RefreshNestAsync(); return; }
        if (row.IsCurrent) { NestSignOut_Click(sender, e); return; }
        if (System.Windows.MessageBox.Show(this, $"删除设备“{row.Name}”的登记并停用消息和直连授权？\n设备上的文件不会被删除。", "删除设备", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var cloud = _nestCloud;
        if (cloud == null || row.AccountId != SyncSessionStore.Load()?.UserId) return;
        _nestDeviceMutation = true;
        NestDeviceList.IsEnabled = false;
        NestDeviceRefreshButton.IsEnabled = false;
        NestSignOutButton.IsEnabled = false;
        NestDeviceActionStatus.Text = "正在删除设备…";
        try
        {
            await cloud.RemovePeerDeviceAsync(row.DeviceId);
            foreach (var pair in YanziLanPairing.List().Where(x => x.DeviceId == row.DeviceId && x.OwnerAccount == row.AccountId)) YanziLanPairing.Revoke(pair.PairId);
            YanziPeerRegistry.Remove(row.DeviceId);
            if (row.AccountId == SyncSessionStore.Load()?.UserId) NestDeviceActionStatus.Text = $"已删除设备“{row.Name}”。";
        }
        catch (Exception error)
        {
            if (row.AccountId == SyncSessionStore.Load()?.UserId) NestDeviceActionStatus.Text = "删除未完成：" + error.Message;
        }
        finally
        {
            _nestDeviceMutation = false;
            NestDeviceList.IsEnabled = true;
            NestDeviceRefreshButton.IsEnabled = true;
            NestSignOutButton.IsEnabled = SyncSessionStore.Load() != null;
        }
        await RefreshNestAsync();
    }

    public sealed class NestDeviceRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { var selected = CanSelect && value; if (_isSelected == selected) return; _isSelected = selected; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
        public bool CanSelect => !IsCurrent;
        public bool Online { get; }
        public string DeviceId { get; }
        public string AccountId { get; }
        public bool IsCurrent { get; }
        public string Name { get; }
        public string Status { get; }
        public System.Windows.Media.Brush StatusBrush { get; }
        public string Detail { get; }
        public string ActionLabel => IsCurrent ? "退出本机登录" : "删除";
        public NestDeviceRow(PeerDeviceInfo device, string account, bool current, int sameNameCount = 1)
        {
            DeviceId = device.DeviceId; AccountId = account; IsCurrent = current;
            Online = device.Online;
            Name = (string.IsNullOrWhiteSpace(device.DisplayName) ? "未命名设备" : device.DisplayName) + (current ? " · 本机" : "") + (sameNameCount > 1 ? $" · 同名登记 {sameNameCount} 条" : "");
            var platform = device.Platform switch { "desktop" => "电脑", "android" => "Android 手机", "ios" => "iPhone / iPad", "web" => "浏览器", _ => device.Platform };
            Status = $"{(device.Online ? "在线" : "离线")} · {platform}";
            StatusBrush = new SolidColorBrush(device.Online ? Colors.LimeGreen : Colors.Gray);
            Detail = $"最近活动：{FormatTime(device.LastSeenAt)} · 首次登记：{FormatTime(device.CreatedAt)}\n网络地区：{(string.IsNullOrWhiteSpace(device.LastLocation) ? "未记录" : device.LastLocation)} · 标识：{DeviceId[Math.Max(0, DeviceId.Length - 8)..]}";
        }
        private static string FormatTime(string? value) => DateTimeOffset.TryParse(value, out var time) ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未记录";
    }
}
