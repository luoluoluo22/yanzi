using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OpenQuickHost;
using OpenQuickHost.CSharpRuntime;

public sealed class VpnSyncSettings
{
    public string SubscriptionVariable { get; set; } = "YANZI_VPN_SUB_URL";
    public string PreferredMode { get; set; } = "rule";
    public Dictionary<string, string> Selections { get; set; } = new();
}

public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        if (context.LaunchSource is "app-startup" or "startup")
        {
            VpnManagerWindow.RecoverStaleLease();
            return "VPN 本机代理恢复检查完成";
        }
        VpnSyncSettings settings;
        try { settings = await context.Storage.ReadJsonAsync<VpnSyncSettings>("vpn/settings.json", "both") ?? new(); }
        catch { settings = new(); } // Never write default cloud state when read fails.
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { new VpnManagerWindow(context, settings).ShowDialog(); completion.TrySetResult("VPN 管理器已关闭"); }
            catch (Exception ex) { completion.TrySetResult("VPN 管理器失败：" + ex.GetType().Name); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task;
    }
}

public sealed class VpnManagerWindow : Window
{
    private const int ApiPort = 18990;
    private const int MixedPort = 17990;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly YanziActionContext _ctx;
    private readonly VpnSyncSettings _sync;
    private readonly string _workdir;
    private readonly string _config;
    private readonly string _encryptedCache;
    private readonly TextBox _env = new();
    private readonly ComboBox _mode = new();
    private readonly TextBlock _status = new();
    private readonly ListBox _groups = new();
    private readonly ListBox _nodes = new();
    private readonly Dictionary<string, List<string>> _nodeMap = new(StringComparer.Ordinal);
    private readonly HashSet<string> _manualGroups = new(StringComparer.Ordinal);
    private Process? _core;
    private string? _apiSecret;
    private (int Enable, string Server)? _proxySnapshot;
    private Mutex? _proxyLock;
    private const string ProxyMutexName = @"Local\YanziVpnManagerSystemProxy";
    private static string LeasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "VpnManager", "proxy-lease.json");
    private bool _busy;

    public VpnManagerWindow(YanziActionContext ctx, VpnSyncSettings sync)
    {
        _ctx = ctx;
        _sync = sync;
        // Keep all generated core files outside extension sync and account backup roots.
        _workdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "VpnManager", "runtime");
        _config = Path.Combine(_workdir, "active-config.yaml");
        _encryptedCache = Path.Combine(_workdir, "subscription-cache.dat");
        _sync.Selections ??= new Dictionary<string, string>();
        if (!Process.GetProcessesByName("mihomo").Any() && File.Exists(_config))
            try { File.Delete(_config); } catch { } // Recover sensitive plaintext left by a crashed instance.
        Title = "燕子 · VPN 管理";
        Width = 860; Height = 660; MinWidth = 710; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Foreground = Brushes.White;
        Background = new SolidColorBrush(Color.FromRgb(25, 28, 37));
        _env.Text = sync.SubscriptionVariable;
        _mode.ItemsSource = new[] { "rule", "global", "direct" };
        _mode.SelectedItem = new[] { "rule", "global", "direct" }.Contains(sync.PreferredMode) ? sync.PreferredMode : "rule";
        _groups.SelectionChanged += (_, _) => ShowNodes();
        RecoverStaleLease();
        Content = Layout();
        Status("就绪：不会自动修改系统代理。订阅密钥从燕子的环境变量管理器读取。");
        Closed += (_, _) =>
        {
            try { RestoreProxy(); } catch { }
            try { StopCore(); } catch { }
        };
    }

    private UIElement Layout()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "VPN 管理", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        top.Children.Add(new TextBlock { Text = "同步小程序和个人设置 · 核心按需安装 · 网络接管只发生在本机",
            Foreground = Brushes.LightGray, Margin = new Thickness(0, 7, 0, 11) });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _status.TextWrapping = TextWrapping.Wrap; _status.FontSize = 13;
        _status.Foreground = Brushes.LightGreen; _status.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "订阅环境变量名称（密钥和订阅 URL 不写入小程序配置）", Foreground = Brushes.WhiteSmoke });
        _env.Height = 34; _env.Padding = new Thickness(7); _env.Margin = new Thickness(0, 7, 0, 0);
        body.Children.Add(_env);
        var row = NewRow();
        row.Children.Add(Btn("导入 Clash Verge 订阅", ImportAsync));
        row.Children.Add(Btn("保存同步配置", SaveAsync));
        row.Children.Add(Btn("下载订阅到本机", PrepareAsync));
        body.Children.Add(row);
        body.Children.Add(new TextBlock { Text = "独立运行环境", FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Margin = new Thickness(0, 18, 0, 2) });
        row = NewRow();
        row.Children.Add(Btn("启动核心", StartAsync));
        row.Children.Add(Btn("停止核心", () => { RestoreProxy(); StopCore(); Status("核心已停止。"); return Task.CompletedTask; }));
        row.Children.Add(Btn("接管系统代理", () => { TakeProxy(); return Task.CompletedTask; }));
        row.Children.Add(Btn("恢复系统代理", () => { RestoreProxy(); Status("系统代理已恢复。"); return Task.CompletedTask; }));
        body.Children.Add(row);
        row = NewRow();
        row.Children.Add(new TextBlock { Text = "模式", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        _mode.Width = 125; _mode.Height = 30; _mode.Margin = new Thickness(8, 0, 8, 0);
        row.Children.Add(_mode);
        row.Children.Add(Btn("应用模式", SetModeAsync));
        row.Children.Add(Btn("刷新节点", RefreshAsync));
        row.Children.Add(Btn("测节点延迟", DelayAsync));
        row.Children.Add(Btn("选择节点", SelectAsync));
        body.Children.Add(row);
        var grid = new Grid { Margin = new Thickness(0, 15, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        AddList(grid, 0, "策略组", _groups); AddList(grid, 1, "节点", _nodes);
        body.Children.Add(grid);
        body.Children.Add(new TextBlock { Text = "安全说明：TUN 模式暂不自动启用。与 Clash Verge 并行时不接管系统代理。",
            Foreground = Brushes.LightGray, Margin = new Thickness(0, 15, 0, 0), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return root;
    }

    private static StackPanel NewRow() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private Button Btn(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Height = 34, Margin = new Thickness(0, 0, 7, 0),
            Padding = new Thickness(13, 3, 13, 3), Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(49, 56, 74)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 82, 99)) };
        button.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true;
            try { await action(); }
            catch (Exception ex) { Status("操作失败（" + ex.GetType().Name + "）。为了保护订阅凭据，不展示 URL 或异常原文。"); }
            finally { _busy = false; }
        };
        return button;
    }
    private static void AddList(Grid grid, int column, string title, ListBox list)
    {
        var panel = new DockPanel { Margin = new Thickness(column == 0 ? 0 : 10, 0, 0, 0) };
        var label = new TextBlock { Text = title, Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
        list.MinHeight = 165; list.MaxHeight = 235;
        list.Background = new SolidColorBrush(Color.FromRgb(35, 40, 52)); list.Foreground = Brushes.White;
        panel.Children.Add(list); Grid.SetColumn(panel, column); grid.Children.Add(panel);
    }
    private void Status(string value) => _status.Text = value;
    private string Variable()
    {
        var name = _env.Text.Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z_0-9]{0,120}$"))
            throw new InvalidDataException("Invalid environment variable name");
        return name;
    }
    private async Task SaveAsync()
    {
        _sync.SubscriptionVariable = Variable();
        _sync.PreferredMode = _mode.SelectedItem?.ToString() ?? "rule";
        // Metadata only; the environment-secret vault handles credentials separately with E2EE.
        var cloudAvailable = !string.IsNullOrWhiteSpace(_ctx.AgentApiBaseUrl);
        await _ctx.Storage.WriteJsonAsync("vpn/settings.json", _sync, cloudAvailable ? "both" : "local");
        Status(cloudAvailable ? "已提交配置同步（只包含环境变量名称和节点偏好）。" : "已本机保存；燕子本地 API 未启用，尚未确认云同步。");
    }
    private async Task ImportAsync()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "io.github.clash-verge-rev.clash-verge-rev", "profiles.yaml");
        if (!File.Exists(file)) { Status("没有发现 Clash Verge 订阅索引。"); return; }
        var text = await File.ReadAllTextAsync(file);
        var hits = Regex.Matches(text, @"(?m)^\s+url:\s*['""]?(https://[^\r\n'""]+)", RegexOptions.IgnoreCase);
        if (hits.Count == 0) { Status("没有可导入的 HTTPS 订阅 URL。"); return; }
        var url = hits[0].Groups[1].Value.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        { Status("仅支持 HTTPS 订阅。"); return; }
        var name = Variable();
        var variables = AppEnvironmentVariableStore.Load().ToList();
        if (variables.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v.Value))
            && MessageBox.Show("是否用首个 Clash Verge 订阅覆盖该环境变量的现有值？", "确认导入",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        variables.RemoveAll(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        variables.Add(new AppEnvironmentVariableSettings { Name = name, Value = url, Description = "VPN 订阅地址（已从 Clash Verge 导入）" });
        AppEnvironmentVariableStore.Save(variables);
        await SaveAsync();
        Status("订阅地址已保存到燕子环境变量的受保护存储；原 VPN 未被修改。");
    }
    private string SubscriptionUrl()
    {
        var value = AppEnvironmentVariableStore.GetValue(Variable());
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || url.IsLoopback || url.UserInfo.Length > 0)
            throw new InvalidDataException("Missing HTTPS subscription URL in Yanzi environment variables");
        return url.AbsoluteUri;
    }
    private async Task PrepareAsync()
    {
        var url = SubscriptionUrl();
        Directory.CreateDirectory(_workdir);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Yanzi-VPN/0.1");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) { Status("下载订阅失败：HTTP " + (int)response.StatusCode); return; }
        if (response.Content.Headers.ContentLength is > 4000000) { Status("订阅文件过大。"); return; }
        var raw = await response.Content.ReadAsStringAsync();
        if (raw.Length > 4000000 || !Regex.IsMatch(raw, @"(?m)^(proxies|proxy-providers|proxy-groups):"))
        { Status("仅支持完整 Mihomo YAML 订阅；不支持 Base64 节点订阅。"); return; }
        _apiSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var content = SafeConfig(raw, _apiSecret, _sync.PreferredMode);
        var tmp = _encryptedCache + ".new";
        var protectedContent = ProtectedData.Protect(Encoding.UTF8.GetBytes(content), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(tmp, protectedContent);
        File.Move(tmp, _encryptedCache, true);
        Status("订阅已写入本机 DPAPI 加密缓存；明文配置仅在核心运行期间临时生成。");
    }
    public static string SafeConfig(string input, string secret, string mode)
    {
        if (!Regex.IsMatch(secret, "^[a-f0-9]{64}$")) throw new ArgumentException("Invalid API secret");
        if (mode is not ("rule" or "global" or "direct")) mode = "rule";
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "external-controller", "external-ui",
            "external-ui-url", "external-controller-pipe", "external-controller-unix", "external-controller-tls",
            "secret", "port", "socks-port", "mixed-port", "redir-port", "tproxy-port", "allow-lan", "listeners", "iptables", "tunnels", "script",
            "bind-address", "tun", "mode" };
        // Reject alternate YAML root-key syntax that could bypass the hard-coded security overrides.
        if (Regex.IsMatch(input, @"(?m)^(?:['""\{\[]|<<\s*:)"))
            throw new InvalidDataException("Ambiguous YAML root keys are not supported");
        var sb = new StringBuilder();
        bool skip = false;
        foreach (var line in input.Replace("\r\n", "\n").Split('\n'))
        {
            var key = Regex.Match(line, @"^([A-Za-z][A-Za-z0-9_-]*):(?:\s|$)");
            if (key.Success) skip = blocked.Contains(key.Groups[1].Value);
            if (!skip) sb.AppendLine(line);
        }
        return $"mixed-port: {MixedPort}\nallow-lan: false\nbind-address: 127.0.0.1\nexternal-controller: 127.0.0.1:{ApiPort}\nsecret: '{secret}'\ntun:\n  enable: false\nmode: {mode}\n" + sb;
    }
    private string CorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "Runtimes", "mihomo", "1.19.31", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(), "mihomo.exe");
    private static bool ClashRunning() => Process.GetProcessesByName("clash-verge").Any();
    private static bool PortBusy(int port) => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);

    private async Task StartAsync()
    {
        if (ClashRunning()) { Status("Clash Verge 仍在运行，禁止两个 VPN 核心同时管理网络。先在原软件中手动退出。"); return; }
        if (_core is { HasExited: false }) { Status("核心已启动。"); return; }
        if (!File.Exists(CorePath)) { Status("Mihomo 尚未就绪；请使用燕子能力管理器安装 mihomo>=1.19.31。"); return; }
        if (!File.Exists(_encryptedCache)) await PrepareAsync();
        if (!File.Exists(_encryptedCache)) return;
        // The required plaintext config exists only during test/run, never in the synchronized extension data.
        var configText = Encoding.UTF8.GetString(ProtectedData.Unprotect(
            await File.ReadAllBytesAsync(_encryptedCache), null, DataProtectionScope.CurrentUser));
        var keyMatch = Regex.Match(configText, @"(?m)^secret: '([a-f0-9]{64})'$", RegexOptions.IgnoreCase);
        if (!keyMatch.Success) { Status("加密缓存缺少合法控制密钥，请重新下载订阅。"); return; }
        _apiSecret = keyMatch.Groups[1].Value;
        await File.WriteAllTextAsync(_config, configText, new UTF8Encoding(false));
        if (PortBusy(MixedPort) || PortBusy(ApiPort)) { Status("运行端口被其他程序占用，已拒绝启动。"); return; }
        var checkInfo = NewProcess();
        checkInfo.ArgumentList.Add("-t");
        checkInfo.RedirectStandardOutput = true; checkInfo.RedirectStandardError = true;
        using (var test = Process.Start(checkInfo) ?? throw new IOException("Mihomo validation failed to start"))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await test.WaitForExitAsync(timeout.Token);
            if (test.ExitCode != 0) { Status("Mihomo 配置校验失败，未启动代理。"); return; }
        }
        var startInfo = NewProcess();
        _core = Process.Start(startInfo) ?? throw new IOException("Mihomo failed to start");
        await Task.Delay(1000);
        if (_core.HasExited) { StopCore(); Status("代理核心启动失败，系统代理未被修改。"); return; }
        Status("Mihomo 已独立启动（仅本机），系统代理尚未接管。");
        await RefreshAsync();
        foreach (var pair in _sync.Selections)
        {
            if (!_nodeMap.TryGetValue(pair.Key, out var valid) || !valid.Contains(pair.Value)) continue;
            try { using var ignored = await ApiAsync(HttpMethod.Put, "proxies/" + Uri.EscapeDataString(pair.Key), new { name = pair.Value }); }
            catch { /* Node may have disappeared from the updated provider; never overwrite cloud selection. */ }
        }
    }
    private ProcessStartInfo NewProcess()
    {
        var psi = new ProcessStartInfo(CorePath) { WorkingDirectory = _workdir, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(_workdir);
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(_config);
        return psi;
    }
    private void StopCore()
    {
        if (_core is { HasExited: false } proc)
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(3000);
        }
        _core?.Dispose(); _core = null; _apiSecret = null;
        try { if (File.Exists(_config)) File.Delete(_config); } catch { }
        _groups.Items.Clear(); _nodes.Items.Clear();
    }
    private async Task<JsonDocument> ApiAsync(HttpMethod method, string endpoint, object? payload = null)
    {
        if (_core is not { HasExited: false } || _apiSecret == null)
            throw new InvalidOperationException("VPN core not running");
        using var req = new HttpRequestMessage(method, "http://127.0.0.1:" + ApiPort + "/" + endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiSecret);
        if (payload != null) req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(req);
        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
    }
    private async Task RefreshAsync()
    {
        _groups.Items.Clear(); _nodes.Items.Clear(); _nodeMap.Clear(); _manualGroups.Clear();
        using var doc = await ApiAsync(HttpMethod.Get, "proxies");
        if (!doc.RootElement.TryGetProperty("proxies", out var proxies)) return;
        foreach (var entry in proxies.EnumerateObject())
        {
            var v = entry.Value;
            var type = v.TryGetProperty("type", out var t) ? t.GetString() : "";
            if (type is not ("Selector" or "URLTest" or "Fallback" or "LoadBalance")) continue;
            if (type == "Selector") _manualGroups.Add(entry.Name);
            if (!v.TryGetProperty("all", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            _nodeMap[entry.Name] = arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
            _groups.Items.Add(entry.Name);
        }
        if (_groups.Items.Count > 0) _groups.SelectedIndex = 0;
        Status("已读取 " + _nodeMap.Count + " 个策略组。");
    }
    private void ShowNodes()
    {
        _nodes.Items.Clear();
        var group = _groups.SelectedItem?.ToString();
        if (group == null || !_nodeMap.TryGetValue(group, out var nodes)) return;
        foreach (var item in nodes) _nodes.Items.Add(item);
        if (_sync.Selections.TryGetValue(group, out var previous)) _nodes.SelectedItem = previous;
        if (_nodes.SelectedIndex < 0 && _nodes.Items.Count > 0) _nodes.SelectedIndex = 0;
    }
    private async Task SelectAsync()
    {
        var group = _groups.SelectedItem?.ToString();
        var node = _nodes.SelectedItem?.ToString();
        if (group == null || node == null) return;
        if (!_manualGroups.Contains(group)) { Status("此策略组采用自动选择，不能手动指定节点。"); return; }
        using var _ = await ApiAsync(HttpMethod.Put, "proxies/" + Uri.EscapeDataString(group), new { name = node });
        _sync.Selections[group] = node;
        await SaveAsync();
        Status("已切换节点：" + group + " → " + node);
    }
    private async Task DelayAsync()
    {
        var node = _nodes.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(node)) { Status("请先选择一个节点。"); return; }
        var endpoint = "proxies/" + Uri.EscapeDataString(node) +
            "/delay?timeout=5000&url=" + Uri.EscapeDataString("https://www.gstatic.com/generate_204");
        using var response = await ApiAsync(HttpMethod.Get, endpoint);
        if (response.RootElement.TryGetProperty("delay", out var delay))
            Status(node + " 延迟 " + delay.GetInt32() + " ms");
        else Status("节点延迟测试未返回结果。");
    }
    private async Task SetModeAsync()
    {
        var mode = _mode.SelectedItem?.ToString() ?? "rule";
        using var _ = await ApiAsync(new HttpMethod("PATCH"), "configs", new { mode });
        await SaveAsync();
        Status("已应用代理模式：" + mode);
    }
    private static RegistryKey ProxyKey() => Registry.CurrentUser.OpenSubKey(
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true)
        ?? throw new InvalidOperationException("Cannot open Windows proxy settings");

    private sealed record ProxyLease(int PreviousEnable, string PreviousServer, int OwnerPid, DateTimeOffset CreatedUtc);

    public static bool RecoverStaleLease()
    {
        if (!File.Exists(LeasePath)) return false;
        using var mutex = new Mutex(false, ProxyMutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return false; // An active window owns the Windows proxy.
        try
        {
            // Do not mutate the registry if lease data was damaged.
            var lease = JsonSerializer.Deserialize<ProxyLease>(File.ReadAllText(LeasePath));
            if (lease == null || lease.PreviousEnable is not (0 or 1)) return false;
            using var key = ProxyKey();
            if (key.GetValue("ProxyServer")?.ToString() == "127.0.0.1:" + MixedPort)
            {
                key.SetValue("ProxyServer", lease.PreviousServer, RegistryValueKind.String);
                key.SetValue("ProxyEnable", lease.PreviousEnable, RegistryValueKind.DWord);
                RefreshWindowsProxy();
            }
            File.Delete(LeasePath);
            return true;
        }
        catch { return false; }
        finally { mutex.ReleaseMutex(); }
    }

    private void ReleaseProxyLock()
    {
        if (_proxyLock == null) return;
        try { _proxyLock.ReleaseMutex(); } catch (ApplicationException) { }
        _proxyLock.Dispose();
        _proxyLock = null;
    }

    private void TakeProxy()
    {
        if (_core is not { HasExited: false } || ClashRunning())
        { Status("核心未运行或 Clash Verge 仍在运行，未接管代理。"); return; }
        if (_proxySnapshot == null)
        {
            _proxyLock = new Mutex(false, ProxyMutexName);
            bool acquired;
            try { acquired = _proxyLock.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { _proxyLock.Dispose(); _proxyLock = null; Status("已有一个燕子 VPN 实例正在接管代理。"); return; }
        }
        using var key = ProxyKey();
        var currentEnabled = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
        var currentServer = key.GetValue("ProxyServer")?.ToString() ?? "";
        if (_proxySnapshot == null && currentEnabled != 0 && currentServer != "127.0.0.1:" + MixedPort)
        {
            ReleaseProxyLock();
            Status("系统代理已被其他程序使用，拒绝覆盖。"); return;
        }
        if (_proxySnapshot == null)
        {
            _proxySnapshot = (currentEnabled, currentServer);
            Directory.CreateDirectory(Path.GetDirectoryName(LeasePath)!);
            // Durable state must exist before any registry modification.
            File.WriteAllText(LeasePath, JsonSerializer.Serialize(
                new ProxyLease(currentEnabled, currentServer, Environment.ProcessId, DateTimeOffset.UtcNow)));
        }
        key.SetValue("ProxyServer", "127.0.0.1:" + MixedPort, RegistryValueKind.String);
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        RefreshWindowsProxy();
        Status("已接管系统代理。异常退出时，下次燕子启动会恢复原代理设置。");
    }

    private void RestoreProxy()
    {
        if (_proxySnapshot == null) { ReleaseProxyLock(); return; }
        try
        {
            using var key = ProxyKey();
            if (key.GetValue("ProxyServer")?.ToString() == "127.0.0.1:" + MixedPort)
            {
                key.SetValue("ProxyServer", _proxySnapshot.Value.Server, RegistryValueKind.String);
                key.SetValue("ProxyEnable", _proxySnapshot.Value.Enable, RegistryValueKind.DWord);
                RefreshWindowsProxy();
            }
            if (File.Exists(LeasePath)) File.Delete(LeasePath);
            _proxySnapshot = null;
        }
        finally { ReleaseProxyLock(); }
    }
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);
    private static void RefreshWindowsProxy()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }
}
