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
using Clipboard = System.Windows.Clipboard;
using Application = System.Windows.Application;

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
        var resourceSummary = row.TryGetProperty("requiresScopeConfirmation", out var multi) && multi.ValueKind == JsonValueKind.True ? $"请求 {row.GetProperty("scopes").GetArrayLength()} 项数据；勾选本次允许的项目。" : $"小程序：{Text("extensionId")}\n数据：{Text("dataKey")}";
        panel.Children.Add(new TextBlock { Text = $"申请方（自行填写）：{Text("clientName")}\n核对码：{Text("userCode")}\n{resourceSummary}\n有效期：1 小时。请与 AI 返回的核对码核对。", TextWrapping = TextWrapping.Wrap });
        var choices = new List<(CheckBox Check, JsonElement Scope)>();
        if (row.TryGetProperty("scopes", out var scopes))
        {
            var scopePanel = new StackPanel();
            foreach (var scope in scopes.EnumerateArray())
            {
                var check = new CheckBox { IsChecked = true, Content = $"{scope.GetProperty("extensionId").GetString()} / {scope.GetProperty("key").GetString()} · {(scope.GetProperty("access").GetString() == "read-write" ? "增删改查" : "只读")}", Margin = new Thickness(0,6,0,0) };
                choices.Add((check,scope.Clone())); scopePanel.Children.Add(check);
            }
            panel.Children.Add(new ScrollViewer { Content = scopePanel, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }
        var window = new Window { Title = "外部应用请求数据授权", Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel, Topmost = true, Tag = Text("requestId") };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };panel.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,16,0,0) }; panel.Children.Add(actions);
        foreach (var allow in new[] { true, false })
        {
            var button = new Button { Content = allow ? "允许" : "拒绝", Padding = new Thickness(16,6,16,6), Margin = new Thickness(0,0,12,0) }; actions.Children.Add(button);
            button.Click += async (_, _) => { actions.IsEnabled = false; try { var selected = choices.Where(x => x.Check.IsChecked == true).Select(x => x.Scope).ToArray(); if (allow && choices.Count > 0 && selected.Length == 0) throw new InvalidOperationException("请至少选择一项数据，或拒绝申请。"); await current.ExternalAccessAsync($"/v1/applications/access-requests/{Text("requestId")}/decision", new { approve = allow, scopes = selected }); window.Close(); } catch (Exception e) { status.Text = e.Message; actions.IsEnabled = true; } };
        }
        prompt = window; window.Closed += (_, _) => { if (prompt == window) prompt = null; }; window.Show(); window.Activate();
    }
    public void ShowCenter()
    {
        new Window { Title = "AI / 外部应用接入", Width = 640, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = new ScrollViewer { Content = CreateCenterPanel(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Background = (System.Windows.Media.Brush?)Application.Current.TryFindResource("BrushWindowBG") ?? System.Windows.Media.Brushes.White }.Show();
        shown.Clear(); _ = PollAsync();
    }
    public FrameworkElement CreateCenterPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "先选择可供 AI 申请的数据，再生成提示词。AI 可申请部分或全部清单，每次仍需你确认。", TextWrapping = TextWrapping.Wrap });
        var message = new TextBlock { Text = "正在获取可授权数据…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) }; panel.Children.Add(message);
        var selectAll = new CheckBox { Content = "全选当前清单", IsChecked = true, Margin = new Thickness(0,8,0,0) };panel.Children.Add(selectAll);
        var list = new StackPanel();panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var choices = new List<(CheckBox Check, JsonElement Scope)>();
        selectAll.Click += (_, _) => { foreach (var choice in choices) choice.Check.IsChecked = selectAll.IsChecked == true; };
        var write = new CheckBox { Content = "允许申请增删改查（默认只读）", Margin = new Thickness(0,8,0,0) };panel.Children.Add(write);
        var promptText = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 80, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,8,0,0) };panel.Children.Add(promptText);
        var actions = new WrapPanel { Margin = new Thickness(0,8,0,0) };panel.Children.Add(actions);
        Button Add(string label) { var button = new Button { Content = label, Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,0,8,8) }; if(Application.Current.TryFindResource("SecondaryBtn") is Style style)button.Style=style;actions.Children.Add(button);return button; }
        var refresh=Add("刷新列表");var generate=Add("生成接入提示词");generate.IsEnabled=false;var copy=Add("复制提示词");copy.IsEnabled=false;var send=Add("复制并打开 AI");send.IsEnabled=false;var revoke=Add("撤销接入地址");revoke.IsEnabled=false;
        string? address=null;CloudSyncClient? owner=null;
        async Task Load(){try{owner=GetClient();var result=await owner.ExternalAccessAsync("/v1/applications/access-resources");choices.Clear();list.Children.Clear();foreach(var scope in result.GetProperty("resources").EnumerateArray()){var check=new CheckBox { IsChecked=true, Content=$"{scope.GetProperty("name").GetString()} · {scope.GetProperty("key").GetString()}", Margin=new Thickness(0,6,0,0) };choices.Add((check,scope.Clone()));list.Children.Add(check);}selectAll.IsChecked=true;generate.IsEnabled=choices.Count>0;message.Text=$"可授权数据 {choices.Count} 项 · 不包含账号密码、电脑文件和未列出的未来数据。";}catch(Exception e){generate.IsEnabled=false;message.Text=e.Message;}}
        refresh.Click+=async(_,_)=>await Load();
        generate.Click+=async(_,_)=>{generate.IsEnabled=false;try{var selected=choices.Where(x=>x.Check.IsChecked==true).Select(x=>x.Scope).ToArray();if(selected.Length==0)throw new InvalidOperationException("请至少选择一项数据。");owner=GetClient();var result=await owner.ExternalAccessAsync("/v1/applications/access-invites",new { resources=selected,access=write.IsChecked==true?"read-write":"read" });address=result.GetProperty("address").GetString();promptText.Text=$"请访问这个燕子数据接入地址：{address}\n先 GET 地址和 resourceList 获取可申请的数据清单。根据我的任务只申请必要的 scopes；只有我明确要求全部时才申请 scopes=all。提交你的 clientName，告诉我返回的核对码，等待我在电脑或手机确认。按 poll 说明领取限时授权，再使用返回的 resources 数据接口。修改前读取最新版本，遇到 409 重新读取；未经明确要求不要删除数据。\n地址有效 7 天，批准的权限有效 1 小时。";copy.IsEnabled=send.IsEnabled=revoke.IsEnabled=true;message.Text=$"已生成接入地址，最多可申请 {selected.Length} 项；确认前无法读取数据。";}catch(Exception e){message.Text=e.Message;}finally{generate.IsEnabled=choices.Count>0;}};
        copy.Click+=(_,_)=>{Clipboard.SetText(promptText.Text);message.Text="提示词已复制，粘贴给能发 HTTP 请求的 AI。";};
        send.Click+=(_,_)=>{Clipboard.SetText(promptText.Text);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://chatgpt.com/"){UseShellExecute=true});message.Text="已复制并打开 AI，请粘贴提示词发送。";};
        revoke.Click+=async(_,_)=>{try{if(owner==null||address==null)return;await owner.ExternalAccessAsync("/v1/applications/access-invites/"+address.Split('/').Last(),delete:true);copy.IsEnabled=send.IsEnabled=revoke.IsEnabled=false;promptText.Clear();message.Text="地址已撤销；已批准的授权可在下方单独撤销。";}catch(Exception e){message.Text=e.Message;}};
        var grantsPanel=new StackPanel();panel.Children.Add(grantsPanel);var grantsButton=Add("查看 / 撤销已授权应用");
        grantsButton.Click+=async(_,_)=>{try{var current=GetClient();var result=await current.ExternalAccessAsync("/v1/applications/access-grants");grantsPanel.Children.Clear();foreach(var grant in result.GetProperty("grants").EnumerateArray()){var id=grant.GetProperty("requestId").GetString();var item=new Button { Content=$"撤销 {grant.GetProperty("clientName").GetString()} · {grant.GetProperty("scopes").GetArrayLength()} 项数据",Margin=new Thickness(0,6,0,0) };grantsPanel.Children.Add(item);item.Click+=async(_,_)=>{try{await current.ExternalAccessAsync("/v1/applications/access-grants/"+id,delete:true);grantsPanel.Children.Remove(item);message.Text="授权已撤销，已发出的令牌不能再访问数据。";}catch(Exception e){message.Text=e.Message;}};}if(grantsPanel.Children.Count==0)message.Text="当前没有有效的外部应用授权。";}catch(Exception e){message.Text=e.Message;}};
        panel.Loaded+=async(_,_)=>await Load();return panel;
    }
    public void Dispose() { disposed = true; timer.Stop(); prompt?.Close(); }
}
