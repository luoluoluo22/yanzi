using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;

public static class YanziAction
{
    private static Window current;
    private static readonly object gate = new object();
    private static System.Threading.Timer recoveryTimer;
    private static readonly SemaphoreSlim recoveryGate = new SemaphoreSlim(1,1);
    private static TaskCompletionSource<string> supervisorLifetime = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void RegisterController(string directory)
    {
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var registry=assembly.GetType("OpenQuickHost.HostObjectRegistry");
            if(registry==null)continue;
            registry.GetMethod("Register").Invoke(null,new object[]{"raccoon-manager-window",new RaccoonController(directory)});
            return;
        }
        throw new InvalidOperationException("燕子窗口唤醒接口不可用，保留原启动管理。");
    }
    public static void Quit()
    {
        lock(gate) {
            recoveryTimer?.Dispose(); recoveryTimer=null;
            current?.Dispatcher.BeginInvoke(new Action(()=>current?.Close()));
            supervisorLifetime.TrySetResult("浣熊后台管理已停止；MCP 保持独立运行");
        }
    }
    private static async Task<string> StartBackground()
    {
        _ = Recover();
        return await supervisorLifetime.Task;
    }
    private static readonly string recoveryDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "McpRuntime", "raccoon");
    private static void EnsureRecovery()
    {
        lock(gate) {
            if(recoveryTimer != null)return;
            if(supervisorLifetime.Task.IsCompleted)supervisorLifetime=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old=AppDomain.CurrentDomain.GetData("raccoon-manager.recovery.timer") as IDisposable;
            old?.Dispose();
            (AppDomain.CurrentDomain.GetData("raccoon-manager.recovery.lifetime") as TaskCompletionSource<string>)?.TrySetResult("后台管理已交接给新版本");
            recoveryTimer=new System.Threading.Timer(async _=>await Recover(),null,TimeSpan.FromMinutes(1),TimeSpan.FromMinutes(1));
            AppDomain.CurrentDomain.SetData("raccoon-manager.recovery.timer",recoveryTimer);
            AppDomain.CurrentDomain.SetData("raccoon-manager.recovery.lifetime",supervisorLifetime);
        }
    }
    private static async Task<string> Recover()
    {
        if(!await recoveryGate.WaitAsync(0))return "后台恢复检查正在执行";
        try {
            // Detached service children can inherit redirected pipe handles and keep
            // ReadToEndAsync pending after PowerShell exits. Startup writes its own log.
            var start=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "raccoon-manager", "service")};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-WindowStyle","Hidden","-File",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "raccoon-manager", "managed-start.ps1"),"-Recover"})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)) {
                using(var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(6))) {
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch(OperationCanceledException) { process.Kill(); throw new TimeoutException("后台检查超过 90 秒，已停止检查进程；MCP 保持运行。"); }
                }
                Directory.CreateDirectory(recoveryDirectory);
                File.WriteAllText(Path.Combine(recoveryDirectory,"yanzi-supervisor.json"),JsonSerializer.Serialize(new{owner="raccoon-manager",hostPid=Environment.ProcessId,checkedAt=DateTimeOffset.Now,exitCode=process.ExitCode,interfaceOpened=false}));
                if(process.ExitCode!=0)File.AppendAllText(Path.Combine(recoveryDirectory,"startup.log"),DateTimeOffset.Now.ToString("o")+" 燕子后台恢复失败，退出码："+process.ExitCode+Environment.NewLine);
                return process.ExitCode==0?"浣熊后台恢复检查完成；未打开界面":"后台恢复失败，请查看启动日志";
            }
        } catch(Exception e) {
            Directory.CreateDirectory(recoveryDirectory);
            File.AppendAllText(Path.Combine(recoveryDirectory,"startup.log"),DateTimeOffset.Now.ToString("o")+" "+e.Message+Environment.NewLine);
            return "后台恢复失败，请查看启动日志";
        } finally {recoveryGate.Release();}
    }
    private delegate bool EnumWindow(IntPtr handle,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback,IntPtr state);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle,StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
    private static void CloseOldPanels()
    {
        var ownPid=(uint)Environment.ProcessId;
        EnumWindows((handle,state)=> {
            GetWindowThreadProcessId(handle,out var pid);
            if(pid!=ownPid)return true;
            var text=new StringBuilder(256); GetWindowText(handle,text,text.Capacity);
            if(text.ToString()=="浣熊 MCP · 燕子管理")PostMessage(handle,0x0010,IntPtr.Zero,IntPtr.Zero);
            return true;
        },IntPtr.Zero);
    }
    public static Task<string> RunAsync(YanziActionContext context)
    {
        EnsureRecovery();
        RegisterController(context.ExtensionDirectory);
        if(context.LaunchSource=="app-startup" || context.LaunchSource=="agent-api-reload" || context.InputText=="--background-start")return StartBackground();
        OpenPanel(context.ExtensionDirectory);
        return supervisorLifetime.Task;
    }
    public static void OpenPanel(string directory)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate) {
            if (current != null) {
                current.Dispatcher.BeginInvoke(new Action(() => { current.Show(); current.Activate(); }));
                return;
            }
            CloseOldPanels();
            var thread = new Thread(() => {
                try {
                    var panel = new RaccoonPanel(directory);
                    lock (gate) current = panel;
                    panel.Closed += (_, _) => { lock (gate) current = null; completion.TrySetResult("管理面板已关闭；MCP 继续独立运行"); };
                    panel.ShowDialog();
                } catch (Exception e) {completion.TrySetException(e);}
            });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
}

public sealed class RaccoonController
{
    private readonly string directory;
    public RaccoonController(string directory) {this.directory=directory;}
    public void Toggle() {YanziAction.OpenPanel(directory);}
    public void Quit() {YanziAction.Quit();}
    public void Run(string input) {if(input!="--background-start")Toggle();}
}

public sealed class RaccoonPanel : Window
{
    private static readonly string Project = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "Extensions", "raccoon-manager", "service");
    private static readonly string Runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenQuickHost", "McpRuntime", "raccoon");
    private static int GetConfiguredPort()
    {
        var envFile = Path.Combine(Runtime, ".env");
        try {
            if(File.Exists(envFile)) {
                var line=File.ReadLines(envFile).FirstOrDefault(x=>x.StartsWith("RACCOON_PORT=",StringComparison.Ordinal));
                if(line!=null && int.TryParse(line.Substring("RACCOON_PORT=".Length).Trim(),out var port) &&
                    port>=1 && port<=65535) return port;
            }
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
        return 3766;
    }
    private readonly string extensionDir;
    private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    private readonly TextBlock summary = new TextBlock { FontSize=19, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,12,0,12) };
    private readonly TextBox processes = new TextBox { IsReadOnly=true, FontFamily=new FontFamily("Consolas"), TextWrapping=TextWrapping.Wrap, Height=90, Margin=new Thickness(0,0,0,12) };
    private readonly TextBox logs = new TextBox { IsReadOnly=true, FontFamily=new FontFamily("Consolas"), TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, MinHeight=200 };
    private readonly ComboBox logChoice = new ComboBox { Width=210, Margin=new Thickness(0,0,10,8) };
    private readonly StackPanel actions = new StackPanel { Orientation=Orientation.Horizontal };
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(10) };
    private bool refreshing, operating, closed;

    public RaccoonPanel(string directory)
    {
        extensionDir=directory; Title="浣熊 MCP · 燕子管理"; Width=880; Height=650; MinWidth=680; MinHeight=520;
        Background=Brushes.White; Foreground=Brushes.Black;
        var root=new DockPanel { Margin=new Thickness(24) }; Content=root;
        var top=new StackPanel(); DockPanel.SetDock(top,Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text="浣熊 MCP", FontSize=28, FontWeight=FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text="燕子启动时在后台恢复服务，每分钟检查一次；点击才打开此面板，关闭面板不会停止 MCP。", Margin=new Thickness(0,8,0,0), TextWrapping=TextWrapping.Wrap });
        top.Children.Add(summary); top.Children.Add(processes); top.Children.Add(actions);
        var remoteFlag=Path.Combine(Runtime,"remote-control.enabled");
        var remoteControl=new CheckBox {
            Content="允许同账号网页远程调用本机浣熊 MCP（包含文件修改和命令执行）",
            IsChecked=File.Exists(remoteFlag), Margin=new Thickness(0,12,0,0)
        };
        remoteControl.Checked+=(_,_)=>{
            if(MessageBox.Show(this,"允许同账号已授权的网页连接通过燕子设备中继调用本机 MCP，可能执行修改文件或命令。确定开启？","远程控制授权",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) {
                remoteControl.IsChecked=false; return;
            }
            Directory.CreateDirectory(Runtime);
            File.WriteAllText(remoteFlag,"enabled by local user");
        };
        remoteControl.Unchecked+=(_,_)=>{if(File.Exists(remoteFlag))File.Delete(remoteFlag);};
        top.Children.Add(remoteControl);
        AddButton("刷新状态",async()=>await Refresh());
        AddButton("启动 / 恢复连接",async()=>await Control("start"));
        AddButton("安全重启 MCP",async()=>await Control("restart"));
        AddButton("打开日志目录",()=> { Process.Start(new ProcessStartInfo("explorer.exe",Runtime) { UseShellExecute=true }); return Task.CompletedTask; });
        var choice=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,16,0,0) };
        choice.Children.Add(logChoice); top.Children.Add(choice);
        foreach(var name in new[]{"服务日志","隧道日志","启动日志","工具审计","连接状态变化"}) logChoice.Items.Add(name);
        logChoice.SelectedIndex=0; logChoice.SelectionChanged+=(_,_)=>ReadLog(); root.Children.Add(logs);
        timer.Tick+=async(_,_)=>await Refresh();
        Loaded+=async(_,_)=> { timer.Start(); await Refresh(); };
        Closed+=(_,_)=> { closed=true; timer.Stop(); http.Dispose(); };
    }
    private void AddButton(string text,Func<Task> action)
    {
        var b=new Button { Content=text, Padding=new Thickness(12,7,12,7), Margin=new Thickness(0,0,10,0) };
        b.Click+=async(_,_)=> { try { await action(); } catch(Exception e) { if(!closed) MessageBox.Show(this,e.Message,"浣熊 MCP"); } }; actions.Children.Add(b);
    }
    private async Task<bool> Health(string url)
    {
        try { using(var r=await http.GetAsync(url)) { if(!r.IsSuccessStatusCode)return false; using(var doc=JsonDocument.Parse(await r.Content.ReadAsStringAsync()))return doc.RootElement.TryGetProperty("ok",out var ok) && ok.ValueKind==JsonValueKind.True && doc.RootElement.TryGetProperty("name",out var name) && name.GetString()=="raccoon-mcp"; } }
        catch {return false;}
    }
    private string ProcessInfo(string label,string file,string expected)
    {
        try {
            using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Runtime,file)))) {
                var pid=doc.RootElement.GetProperty("pid").GetInt32();
                using(var p=Process.GetProcessById(pid)) {
                    if(p.HasExited || !p.ProcessName.Equals(expected,StringComparison.OrdinalIgnoreCase)) return label+"：未运行（PID 已失效）";
                    return $"{label}：PID {pid} · {p.ProcessName} · 内存 {p.WorkingSet64/1048576} MB · 启动 {p.StartTime:MM-dd HH:mm:ss}";
                }
            }
        } catch { return label+"：未运行 / 无进程记录"; }
    }
    private async Task Refresh()
    {
        if(refreshing || operating || closed)return; refreshing=true;
        try {
            var local=Health("http://127.0.0.1:"+GetConfiguredPort()+"/health");
            var publicUrl=File.Exists(Path.Combine(Runtime,".env"))
                ? File.ReadLines(Path.Combine(Runtime,".env")).FirstOrDefault(x=>x.StartsWith("RACCOON_PUBLIC_URL=",StringComparison.Ordinal))
                : null;
            var address=publicUrl?.Substring("RACCOON_PUBLIC_URL=".Length).Trim();
            var validPublic=Uri.TryCreate(address,UriKind.Absolute,out var remoteUri) && remoteUri!.Scheme=="https";
            var remote=validPublic ? Health(new Uri(remoteUri!, "/health").ToString()) : Task.FromResult(false);
            await Task.WhenAll(local,remote); if(closed)return;
            summary.Text=local.Result
                ? (remote.Result ? "● 本机在线 · 公网在线" : validPublic ? "● 本机在线 · 公网未连接，可使用燕子账号设备中继" : "● 本机在线 · 无独立公网隧道，使用燕子账号设备中继")
                : "● 本机不可达：检查服务进程和启动日志";
            summary.Foreground=local.Result && remote.Result?Brushes.ForestGreen:Brushes.DarkOrange;
            processes.Text=ProcessInfo("MCP","service.json","node")+Environment.NewLine+ProcessInfo("Cloudflare","cloudflare-process.json","cloudflared")+Environment.NewLine+"检查时间："+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); ReadLog();
        } finally {refreshing=false;}
    }
    private void ReadLog()
    {
        var names=new[]{"stderr.log","cloudflare-stderr.log","startup.log","audit.jsonl","connection-history.jsonl"};
        try {
            var file=Path.Combine(Runtime,names[Math.Max(0,logChoice.SelectedIndex)]);
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete)) {
                var offset=Math.Max(0,stream.Length-16384); stream.Seek(offset,SeekOrigin.Begin);
                using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                    if(offset>0)reader.ReadLine(); logs.Text=reader.ReadToEnd(); logs.ScrollToEnd();
                }
            }
        } catch(FileNotFoundException) {logs.Text="暂无此类日志。";} catch(Exception e) {logs.Text="读取日志失败："+e.Message;}
    }
    private async Task Control(string action)
    {
        if(operating || closed)return;
        if(action=="restart" && MessageBox.Show(this,"仅在无活动请求、后台任务和保留会话时重启；忙碌时会拒绝操作。是否执行安全重启？","浣熊 MCP",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        operating=true; actions.IsEnabled=false;
        try {
            var start=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Project };
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-WindowStyle","Hidden","-File",Path.Combine(extensionDir,"control.ps1"),"-Action",action})start.ArgumentList.Add(arg);
            using(var p=Process.Start(start)) {
                var output=p.StandardOutput.ReadToEndAsync(); var error=p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync(); await Task.WhenAll(output,error);
                if(!closed) MessageBox.Show(this,output.Result+Environment.NewLine+error.Result,p.ExitCode==0?"操作完成":"操作未完成；服务未强制停止");
            }
        } finally {operating=false; if(!closed) {actions.IsEnabled=true; await Refresh();} }
    }
}
