using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenQuickHost;
using OpenQuickHost.CSharpRuntime;
using Yanzi.UI.Wpf;

public static class YanziAction
{
    private static readonly object Gate = new();
    private static Window? Active;

    public static async Task<string> RunAsync(YanziActionContext context)
    {
        lock (Gate)
        {
            if (Active != null)
            {
                Active.Dispatcher.BeginInvoke(new Action(() => { Active.Show(); Active.Activate(); }));
                return "Cookie API 窗口已打开";
            }
        }
        var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => ShowWindow(context, completed)) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completed.Task;
    }

    private static void ShowWindow(YanziActionContext context, TaskCompletionSource<string> completed)
    {
        try
        {
            var window = new Window
            {
                Title = "Cookie API",
                Width = 570, Height = 485, MinWidth = 470, MinHeight = 410,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            YanziUi.ApplyTo(window, YanziTheme.Dark);
            window.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Background");
            window.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.Foreground");
            window.SetResourceReference(Control.FontFamilyProperty, "Yanzi.Font.Geist");
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "Cookie API", FontSize = 21, FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Ego 浏览器桥接 · ChatGPT / Gemini",
                Margin = new Thickness(0, 6, 0, 14), Foreground = Brushes.Gray
            });
            var status = new TextBlock
            {
                Text = "正在检查…", TextWrapping = TextWrapping.Wrap,
                FontSize = 13, Margin = new Thickness(0, 0, 0, 15)
            };
            panel.Children.Add(status);
            panel.Children.Add(new TextBlock
            {
                Text = "已安装的 Cookie Suite 后台目录",
                Margin = new Thickness(0, 0, 0, 6)
            });
            var directory = new TextBox
            {
                Text = YanziCookieApiBridge.GetRuntimeFolder() ?? "",
                MinHeight = 33, Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(directory);
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            Button MakeButton(string text, string style)
            {
                var button = new Button { Content = text, MinWidth = 82, Margin = new Thickness(0, 0, 8, 8) };
                return YanziUi.WithStyle(button, style);
            }
            var browse = MakeButton("选择目录", YanziUi.Styles.OutlineButton);
            var bind = MakeButton("绑定后台", YanziUi.Styles.OutlineButton);
            var start = MakeButton("启动服务", YanziUi.Styles.DefaultButton);
            var stop = MakeButton("停止适配器", YanziUi.Styles.OutlineButton);
            var refresh = MakeButton("刷新状态", YanziUi.Styles.GhostButton);
            var test = MakeButton("测试 ChatGPT", YanziUi.Styles.OutlineButton);
            var help = MakeButton("使用说明", YanziUi.Styles.GhostButton);
            foreach (var button in new[] { browse, bind, start, stop, refresh, test, help })
                row.Children.Add(button);
            panel.Children.Add(row);
            panel.Children.Add(new TextBlock
            {
                Text = "燕子模型服务 → Cookie API (Ego)\n" +
                       "chatgpt-web / gemini-3.5-flash-lite\n" +
                       "仅在本机使用凭证，接口需本机随机密钥授权。",
                Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap
            });

            async Task Refresh()
            {
                var s = await YanziCookieApiBridge.GetStatusAsync();
                status.Text = "燕子模型适配器：" + (YanziCookieApiBridge.IsRunning ? "已启动" : "未启动") +
                              "\n后台服务：" + (s.Backend ? "在线" : "离线") +
                              "\nEgo 扩展：" + (s.Extension ? "已连接" : "未连接") +
                              "\n本地路径：" + (YanziCookieApiBridge.GetRuntimeFolder() ?? "未绑定");
            }

            browse.Click += (_, _) =>
            {
                var picker = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择已安装的 Cookie Suite 后台文件夹"
                };
                if (picker.ShowDialog(window) == true) directory.Text = picker.FolderName;
            };
            bind.Click += async (_, _) =>
            {
                try { YanziCookieApiBridge.ConfigureRuntimeFolder(directory.Text); await Refresh(); }
                catch (Exception error) { status.Text = "绑定失败：" + error.Message; }
            };
            start.Click += async (_, _) =>
            {
                start.IsEnabled = false;
                try
                {
                    YanziCookieApiBridge.EnsureStarted();
                    status.Text = "正在连接后台…";
                    var ok = await YanziCookieApiBridge.EnsureBackendStartedAsync();
                    await Refresh();
                    if (!ok) status.Text += "\n后台启动失败，请检查路径。";
                }
                catch (Exception error)
                {
                    context.Log("cookie-api start: " + error.GetType().Name);
                    status.Text = "启动失败：" + error.GetType().Name;
                }
                finally { start.IsEnabled = true; }
            };
            stop.Click += async (_, _) =>
            {
                YanziCookieApiBridge.Stop(stopOwnedBackend: true);
                await Refresh();
            };
            refresh.Click += async (_, _) => await Refresh();
            test.Click += async (_, _) =>
            {
                test.IsEnabled = false;
                try
                {
                    YanziCookieApiBridge.EnsureStarted();
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
                    using var req = new HttpRequestMessage(HttpMethod.Post,
                        YanziCookieApiBridge.BaseUrl + "/chat/completions");
                    req.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer", YanziCookieApiBridge.GetLocalKey());
                    req.Content = new StringContent(
                        "{\"model\":\"chatgpt-web\",\"messages\":[{\"role\":\"user\",\"content\":\"Reply with exactly OK.\"}],\"stream\":false}",
                        Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(req);
                    if (!response.IsSuccessStatusCode)
                        status.Text = "模型测试未通过：HTTP " + (int)response.StatusCode;
                    else
                    {
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                        var answer = json.RootElement.GetProperty("choices")[0]
                            .GetProperty("message").GetProperty("content").GetString();
                        status.Text = answer?.Trim() == "OK"
                            ? "ChatGPT 模型端到端测试通过（OK）"
                            : "模型返回了内容，但测试校验未通过。";
                    }
                }
                catch (Exception error) { status.Text = "模型测试失败：" + error.GetType().Name; }
                finally { test.IsEnabled = true; }
            };
            help.Click += (_, _) => MessageBox.Show(window,
                "选择已安装 Cookie Suite 目录并绑定，Ego 浏览器需启用 Cookie Hub 扩展。\n" +
                "在燕子「模型服务」选择 Cookie API (Ego) 即可调用。\n" +
                "未配置的设备需要先安装后台。已有其他程序启动的服务不会被本窗口强制关闭。",
                "Cookie API 使用说明", MessageBoxButton.OK, MessageBoxImage.Information);
            window.Content = new ScrollViewer
            {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            lock (Gate) Active = window;
            window.Closed += (_, _) => { lock (Gate) Active = null; };
            window.Loaded += async (_, _) =>
            {
                try { YanziCookieApiBridge.EnsureStarted(); await Refresh(); }
                catch (Exception error) { status.Text = "启动失败：" + error.GetType().Name; }
            };
            window.ShowDialog();
            completed.TrySetResult("Cookie API 窗口已关闭");
        }
        catch (Exception error)
        {
            context.Log("cookie-api window: " + error.GetType().Name);
            lock (Gate) Active = null;
            completed.TrySetResult("Cookie API 窗口启动失败");
        }
    }
}
