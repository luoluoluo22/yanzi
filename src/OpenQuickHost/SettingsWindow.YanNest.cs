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
    private bool _nestOnline;
    private bool _nestMobileOnline;
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
        if (_nestBusy || !IsVisible || !IsYanwoSelected) return;
        _nestBusy = true;
        try
        {
            NestApiStatus.Text = _settings.EnableAgentApi && (System.Windows.Application.Current as App)?.AgentApiServer?.IsListening == true ? $"已启动 · {_settings.AgentApiPort}" : "本地 API 未启动";
            var account = SyncSessionStore.Load()?.UserId;
            if (string.IsNullOrEmpty(account)) { NestOnline=false;NestMobileOnline=false;NestCloudStatus.Text = "未登录 · 公网消息与数据需要燕子账号"; MobileStatusText.Text = _settings.EnableLanSync ? "局域网发现已开启 · 公网未登录" : "局域网发现关闭 · 公网未登录"; MobileStatusText.Tag = "Disconnected"; return; }
            if (_nestCloud == null || _nestAccount != account) { _nestCloud = new CloudSyncClient(SyncConfigLoader.Load()); _nestAccount = account; }
            var result = await _nestCloud.ExternalAccessAsync("/v1/me/devices");
            if (account != SyncSessionStore.Load()?.UserId) return;
            var items = result.TryGetProperty("items", out var entries) ? entries : result.TryGetProperty("devices", out entries) ? entries : default;
            var online = items.ValueKind == System.Text.Json.JsonValueKind.Array ? items.EnumerateArray().Count(x => x.TryGetProperty("platform", out var p) && p.GetString() == "android" && x.TryGetProperty("online", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True) : 0;
            NestCloudStatus.Text = $"账号公网已连接 · 在线手机 {online} 台";
            NestOnline=true;NestMobileOnline=online>0;
            MobileStatusText.Text = $"公网在线 {online} 台 · 局域网发现{(_settings.EnableLanSync ? "开" : "关")}";
            MobileStatusText.Tag = online > 0 ? "Connected" : "Disconnected";
            MobileStatusDot.Fill = new SolidColorBrush(online > 0 ? Colors.LimeGreen : Colors.Gray);
            MobileStatusText.Foreground = MobileStatusDot.Fill;
            if (MobileToolTipStatusText != null) MobileToolTipStatusText.Text = MobileStatusText.Text;
        }
        catch { NestOnline=false;NestMobileOnline=false;NestCloudStatus.Text = "公网状态暂不可用 · 稍后自动重试"; MobileStatusText.Text = "公网状态未知"; MobileStatusText.Tag = "Disconnected"; MobileStatusDot.Fill = new SolidColorBrush(Colors.Gray); }
        finally { _nestBusy = false; }
    }
}
