using System.Diagnostics;
using System.Text;
using System.Windows;

namespace OpenQuickHost;

public partial class SettingsWindow
{
    private bool _lanDesktopBusy;

    private async void LanScan_Click(object sender, RoutedEventArgs e)
    {
        if (_lanDesktopBusy) return;
        _lanDesktopBusy = true;
        LanScanButton.IsEnabled = false;
        LanExecuteButton.IsEnabled = false;
        LanScanStatus.Text = "正在搜索局域网电脑并验证同账号加密身份…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(16));
            var computers = await YanziDesktopLanService.DiscoverAsync(
                LanManualIpText.Text.Trim(), timeout.Token);
            LanDesktopList.ItemsSource = computers;
            LanScanStatus.Text = computers.Count == 0
                ? "未发现其他燕子 Windows 设备。请检查对方是否已更新、登录相同账号、启用局域网连接及私人网络防火墙。"
                : $"发现 {computers.Count} 台电脑 · 加密认证通过 {computers.Count(x => x.Trusted)} 台";
            if (computers.Count == 1) LanDesktopList.SelectedIndex = 0;
        }
        catch (Exception error)
        {
            LanScanStatus.Text = "扫描失败：" + error.Message;
        }
        finally
        {
            _lanDesktopBusy = false;
            LanScanButton.IsEnabled = true;
            LanExecuteButton.IsEnabled = true;
        }
    }

    private async void LanExecute_Click(object sender, RoutedEventArgs e)
    {
        if (_lanDesktopBusy) return;
        if (LanDesktopList.SelectedItem is not YanziDesktopLanService.Computer target || !target.Trusted)
        {
            LanCommandOutput.Text = "请先扫描并选择一台通过加密身份验证的电脑。";
            return;
        }
        var command = LanCommandText.Text.Trim();
        if (string.IsNullOrEmpty(command)) { LanCommandOutput.Text = "请输入 PowerShell 命令。"; return; }
        if (System.Windows.MessageBox.Show(this, $"确定在 {target.Name} ({target.Address}) 上执行这条命令吗？\n\n{command}",
            "确认远程执行", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _lanDesktopBusy = true;
        LanExecuteButton.IsEnabled = false;
        LanScanButton.IsEnabled = false;
        LanCommandOutput.Text = "已发送加密命令，等待远程电脑执行结果…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(88));
            LanCommandOutput.Text = await YanziDesktopLanService.ExecuteAsync(target, command, timeout.Token);
        }
        catch (Exception error) { LanCommandOutput.Text = "远程执行失败：" + error.Message; }
        finally
        {
            _lanDesktopBusy = false;
            LanExecuteButton.IsEnabled = true;
            LanScanButton.IsEnabled = true;
        }
    }

    private void LanConfigureFirewall_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this,
            "仅为 Windows 私人网络、同一子网开启燕子的 UDP 42980 设备发现和 TCP 本地 Agent 端口。\n" +
            "此操作需要 Windows 管理员确认。是否继续？",
            "配置局域网防火墙", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        if (_settings.AgentApiPort is < 1024 or > 65535)
        {
            LanScanStatus.Text = "本地 Agent 端口无效，请先检查配置。";
            return;
        }
        var port = _settings.AgentApiPort;
        var script = """
$ErrorActionPreference = 'Stop'
foreach ($entry in @(
    @{Name='Yanzi-LAN-Discovery-Private'; Port=42980; Protocol='UDP'},
    @{Name='Yanzi-LAN-Agent-Private'; Port=YANZI_AGENT_PORT; Protocol='TCP'}
)) {
    Get-NetFirewallRule -Name $entry.Name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -Name $entry.Name -DisplayName $entry.Name -Direction Inbound -Profile Private -Action Allow -Protocol $entry.Protocol -LocalPort $entry.Port -RemoteAddress LocalSubnet | Out-Null
}
# The listener uses HttpListener wildcard binding: allow only the current Windows user to reserve its URL.
$url = 'http://*:' + YANZI_AGENT_PORT + '/'
$null = & netsh http show urlacl "url=$url" 2>&1
if ($LASTEXITCODE -ne 0) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $null = & netsh http add urlacl "url=$url" "user=$identity" "listen=yes"
    if ($LASTEXITCODE -ne 0) { throw 'Failed to authorize Yanzi HTTP URL listener' }
}
""";
        script = script.Replace("YANZI_AGENT_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = true,
                Verb = "runas"
            });
            LanScanStatus.Text = "已请求管理员配置私人网络防火墙及 HTTP 监听权限。确认授权后请重启燕子，再重新扫描。";
        }
        catch (Exception error) { LanScanStatus.Text = "防火墙配置未启动：" + error.Message; }
    }
}
