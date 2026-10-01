using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenQuickHost.Sync;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;

namespace OpenQuickHost;

/// <summary>Account-scoped consent prompts; no extension-specific synchronization logic.</summary>
internal sealed class ExternalAccessApprovalService : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly HashSet<string> shown = new();
    private CloudSyncClient? client;
    private string? account;
    private bool busy;
    private bool disposed;
    private Window? prompt;
    public ExternalAccessApprovalService() { timer.Tick += async (_, _) => await PollAsync(); timer.Start(); }
    private CloudSyncClient GetClient()
    {
        var user = SyncSessionStore.Load()?.UserId;
        if (string.IsNullOrEmpty(user)) throw new InvalidOperationException("请先登录燕子账号。");
        if (user != account || client == null) { prompt?.Close(); shown.Clear(); account = user; client = new CloudSyncClient(SyncConfigLoader.Load()); }
        return client;
    }
    private async Task PollAsync()
    {
        if (busy || disposed) return;
        busy = true;
        try
        {
            var current = GetClient();
            var result = await current.ExternalAccessAsync("/v1/applications/access-requests");
            if (disposed || account != SyncSessionStore.Load()?.UserId) return;
            var rows = result.GetProperty("requests").EnumerateArray().Select(x => x.Clone()).ToList();
            if (prompt?.Tag is string openId && !rows.Any(x => x.GetProperty("requestId").GetString() == openId)) prompt.Close();
            if (prompt != null) return;
            foreach (var row in rows)
            {
                var id = row.GetProperty("requestId").GetString()!;
                if (!shown.Add(id)) continue;
                ShowPrompt(current, row); break;
            }
        }
        catch (Exception) { /* Offline/unpublished API is retried without exposing invitation secrets. */ }
        finally { busy = false; }
    }
    private void ShowPrompt(CloudSyncClient current, JsonElement row)
    {
        string Text(string key) => row.GetProperty(key).GetString() ?? "";
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"申请方（自行填写）：{Text("clientName")}\n核对码：{Text("userCode")}\n小程序：{Text("extensionId")}\n数据：{Text("dataKey")}\n权限：{(Text("access") == "read-write" ? "读取、新增、修改、删除" : "仅读取")}\n有效期：1 小时。请与 AI 返回的核对码核对。", TextWrapping = TextWrapping.Wrap });
        var window = new Window { Title = "外部应用请求数据授权", Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel, Topmost = true, Tag = Text("requestId") };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,16,0,0) }; panel.Children.Add(actions);
        foreach (var allow in new[] { true, false })
        {
            var button = new Button { Content = allow ? "允许" : "拒绝", Padding = new Thickness(16,6,16,6), Margin = new Thickness(0,0,12,0) }; actions.Children.Add(button);
            button.Click += async (_, _) => { actions.IsEnabled = false; try { await current.ExternalAccessAsync($"/v1/applications/access-requests/{Text("requestId")}/decision", new { approve = allow }); window.Close(); } catch (Exception e) { status.Text = e.Message; actions.IsEnabled = true; } };
        }
        prompt = window; window.Closed += (_, _) => { if (prompt == window) prompt = null; }; window.Show(); window.Activate();
    }
    public void ShowCenter()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "把接入地址交给 AI。每次申请仍需在手机或电脑确认。\n地址有效 7 天；批准的读写令牌有效 1 小时。", TextWrapping = TextWrapping.Wrap });
        var extension = new TextBox { Text = "taskbar-calendar", Margin = new Thickness(0,12,0,0) }; panel.Children.Add(extension);
        var key = new TextBox { Text = "calendar.v1.json", Margin = new Thickness(0,8,0,0) }; panel.Children.Add(key);
        var write = new CheckBox { Content = "允许申请新增、修改、删除", Margin = new Thickness(0,8,0,0) }; panel.Children.Add(write);
        var address = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0) }; panel.Children.Add(address);
        var button = new Button { Content = "生成接入地址", Margin = new Thickness(0,12,0,0) }; panel.Children.Add(button);
        button.Click += async (_, _) => { button.IsEnabled = false; try { var result = await GetClient().ExternalAccessAsync("/v1/applications/access-invites", new { extensionId = extension.Text.Trim(), key = key.Text.Trim(), access = write.IsChecked == true ? "read-write" : "read" }); address.Text = result.GetProperty("address").GetString(); address.SelectAll(); address.Focus(); } catch (Exception e) { address.Text = e.Message; } finally { button.IsEnabled = true; } };
        new Window { Title = "AI / 外部应用接入", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel }.Show();
        shown.Clear(); _ = PollAsync();
    }
    public void Dispose() { disposed = true; timer.Stop(); prompt?.Close(); }
}
