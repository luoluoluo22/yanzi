using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenQuickHost.CSharpRuntime;
using Yanzi.UI.Wpf;

public static class YanziAction
{
    private static readonly object Gate = new();
    private static Window? Active;
    private const int MaxBody = 24000;
    private static readonly Regex VerifyWords = new(@"验证码|校验码|动态码|安全码|确认码|verification|security\s+code|one[\s-]*time|login\s+code|sign[\s-]*in\s+code", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NearCode = new(@"(?:验证码|校验码|动态码|安全码|确认码|verification\s+code|security\s+code|one[\s-]*time\s+(?:code|password)|login\s+code|code\s+is)[^\d]{0,75}((?:\d[\s-]?){3,7}\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        lock (Gate)
        {
            if (Active != null)
            {
                Active.Dispatcher.BeginInvoke(new Action(() => { Active.Show(); Active.Activate(); }));
                return "窗口已存在";
            }
        }
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { ShowPanel(context, done); }
            catch (Exception ex) { context.Log("mail-verification window: " + ex.GetType().Name); done.TrySetResult("窗口打开失败"); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await done.Task;
    }
    // Shared design tokens and controls are window-scoped and never mutate the host's resources.
    private static void ApplySharedUi(Window window)
    {
        YanziUi.ApplyTo(window, YanziTheme.Dark);
        window.SetResourceReference(Control.BackgroundProperty, "Yanzi.Color.Background");
        window.SetResourceReference(Control.ForegroundProperty, "Yanzi.Color.Foreground");
        window.SetResourceReference(Control.FontFamilyProperty, "Yanzi.Font.Geist");
    }

    private static void ShowPanel(YanziActionContext context, TaskCompletionSource<string> done)
    {
        var layout = new StackPanel { Margin = new Thickness(18) };
        var title = new TextBlock { Text = "验证码", FontSize = 20, FontWeight = FontWeights.SemiBold };
        var settingsButton = new Button { Content = "管理", Width = 58, Height = 29, Margin = new Thickness(6, 0, 0, 0) };
        var addButton = new Button { Content = "+ 添加", Width = 69, Height = 29, Margin = new Thickness(6, 0, 0, 0) };
        var helpButton = new Button { Content = "?", Width = 29, Height = 29, Margin = new Thickness(6, 0, 0, 0), ToolTip = "邮箱授权帮助" };
        var header = new DockPanel();
        DockPanel.SetDock(helpButton, Dock.Right);
        DockPanel.SetDock(settingsButton, Dock.Right);
        DockPanel.SetDock(addButton, Dock.Right);
        YanziUi.WithStyle(helpButton, YanziUi.Styles.GhostButton);
        YanziUi.WithStyle(settingsButton, YanziUi.Styles.OutlineButton);
        YanziUi.WithStyle(addButton, YanziUi.Styles.DefaultButton);
        header.Children.Add(helpButton);
        header.Children.Add(settingsButton);
        header.Children.Add(addButton);
        header.Children.Add(title);

        var selector = YanziUi.WithStyle(new ComboBox
        {
            Height = 35, Margin = new Thickness(0, 12, 0, 5),
            ToolTip = "选择「全部」或单个邮箱账户"
        }, YanziUi.Styles.Select);
        var hint = new TextBlock { Text = "所有已添加邮箱 · 最近60分钟 · 按收到时间排序", Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 5, 0, 10) };
        var code = new TextBlock { Text = "— — — — — —", FontSize = 28, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        var from = new TextBlock { Text = "正在准备检索", Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 12) };
        var status = new TextBlock { Text = "按需访问邮箱，凭证保存在燕子加密库", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 9, 0, 0) };
        var refresh = YanziUi.WithStyle(new Button
        {
            Content = "刷新", Width = 105, Height = 35, Margin = new Thickness(0, 0, 8, 0)
        }, YanziUi.Styles.OutlineButton);
        var copy = YanziUi.WithStyle(new Button
        {
            Content = "复制", Width = 95, Height = 35, IsEnabled = false
        }, YanziUi.Styles.DefaultButton);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(refresh); buttons.Children.Add(copy);
        var otpBody = new StackPanel();
        otpBody.Children.Add(code);
        otpBody.Children.Add(from);
        var otpCard = new YanziCard { Margin = new Thickness(0, 3, 0, 13) };
        otpCard.SetBody(otpBody);
        layout.Children.Add(header);
        layout.Children.Add(YanziPrimitives.Separator());
        layout.Children.Add(selector);
        layout.Children.Add(hint);
        layout.Children.Add(otpCard);
        layout.Children.Add(buttons);
        layout.Children.Add(status);
        var panel = new Window
        {
            Title = "燕子 · 验证码", Width = 475, Height = 460,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false,
            Topmost = false, Content = layout
        };
        ApplySharedUi(panel);
        lock (Gate) Active = panel;
        string? current = null;
        CancellationTokenSource? pending = null;
        List<MailAccount> accounts = new();

        void ReloadChoices(string? retainId = null)
        {
            retainId ??= selector.SelectedIndex > 0 && selector.SelectedIndex - 1 < accounts.Count
                ? accounts[selector.SelectedIndex - 1].Id : null;
            accounts = AccountStore.Load();
            selector.Items.Clear();
            selector.Items.Add("全部 · " + accounts.Count + " 个邮箱");
            foreach (var account in accounts) selector.Items.Add(account.Display);
            var selected = retainId == null ? -1 : accounts.FindIndex(a => a.Id == retainId);
            selector.SelectedIndex = selected < 0 ? 0 : selected + 1;
        }

        async Task RefreshAsync()
        {
            if (pending != null) return;
            pending = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token = pending.Token;
            refresh.IsEnabled = false; copy.IsEnabled = false; current = null;
            code.Text = "正在检索"; from.Text = "";
            status.Foreground = Brushes.Gray; status.Text = "正在并行检查已添加邮箱…";
            try
            {
                ReloadChoices();
                var selectedIndex = selector.SelectedIndex;
                var targets = selectedIndex <= 0 ? accounts.ToArray()
                    : new[] { accounts[selectedIndex - 1] };
                if (targets.Length == 0)
                {
                    code.Text = "尚未添加邮箱";
                    status.Text = "点击「+ 添加」绑定 QQ 或 Gmail，可同时添加多个账户。";
                    return;
                }

                // Isolate failures: a broken mailbox must not suppress a newer OTP in another mailbox.
                var tasks = targets.Select(async account =>
                {
                    try
                    {
                        var result = await QQ.FindAsync(account.Host, account.Address, account.Secret, token);
                        return (Account: account, Result: result, Error: (string?)null);
                    }
                    catch (QQ.LoginRejected)
                    {
                        return (Account: account, Result: ((string Code, string Sender, DateTimeOffset Date)?)null,
                                Error: (string?)"认证失败");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return (Account: account, Result: ((string Code, string Sender, DateTimeOffset Date)?)null,
                                Error: (string?)"连接超时");
                    }
                    catch (Exception ex)
                    {
                        context.Log("mail-verification read-failed provider=" + account.Provider
                                    + " exception=" + ex.GetType().Name);
                        return (Account: account, Result: ((string Code, string Sender, DateTimeOffset Date)?)null,
                                Error: (string?)"读取异常");
                    }
                }).ToArray();
                var scanned = await Task.WhenAll(tasks);
                var winner = scanned.Where(x => x.Result.HasValue)
                    .OrderByDescending(x => x.Result!.Value.Date).FirstOrDefault();
                var failed = scanned.Where(x => x.Error != null).ToArray();
                if (winner.Result.HasValue)
                {
                    current = winner.Result.Value.Code;
                    code.Text = current;
                    from.Text = winner.Account.Display + " · " + winner.Result.Value.Sender
                        + " · " + winner.Result.Value.Date.ToLocalTime().ToString("MM-dd HH:mm");
                    status.Text = "已检查 " + scanned.Length + " 个邮箱；"
                        + (failed.Length > 0 ? failed.Length + " 个邮箱未成功读取，已显示其余账户的最新验证码。" : "显示收到时间最近的验证码。");
                    copy.IsEnabled = true;
                }
                else if (failed.Length > 0)
                {
                    code.Text = failed.Length == scanned.Length ? "邮箱读取失败" : "暂无验证码";
                    status.Foreground = Brushes.IndianRed;
                    var summaries = string.Join("；", failed.Take(2).Select(x =>
                        x.Account.Display + "：" + x.Error));
                    status.Text = summaries + (failed.Length > 2 ? " 等" + failed.Length + " 个账户" : "")
                        + "。认证失败请确认对应邮箱使用自己的授权码；可点击「管理」修改。";
                }
                else
                {
                    code.Text = "暂无验证码";
                    status.Text = "已成功登录 " + scanned.Length + " 个邮箱，近60分钟没有匹配的验证码邮件。";
                }
            }
            catch (OperationCanceledException) { code.Text = "连接超时"; status.Text = "邮箱检索超时，请检查网络后刷新。"; }
            catch (Exception ex)
            {
                code.Text = "读取失败"; status.Foreground = Brushes.IndianRed;
                status.Text = "账户配置或邮箱读取失败；请检查「管理」。";
                context.Log("mail-verification refresh-failed exception=" + ex.GetType().Name);
            }
            finally { pending?.Dispose(); pending = null; refresh.IsEnabled = true; }
        }

        selector.SelectionChanged += (_, __) =>
        {
            code.Text = "— — — — — —"; from.Text = ""; current = null; copy.IsEnabled = false;
            status.Text = "点击刷新检索所选邮箱；选择「全部」同时检索所有账户。";
        };
        refresh.Click += async (_, __) => await RefreshAsync();
        copy.Click += (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(current)) return;
            try { System.Windows.Clipboard.SetText(current); status.Text = "已复制"; }
            catch { status.Text = "剪贴板暂时不可用"; }
        };
        settingsButton.Click += (_, __) => ShowAccounts(panel, () =>
        {
            ReloadChoices();
            _ = RefreshAsync();
        });
        addButton.Click += (_, __) =>
        {
            var created = ShowAccountEditor(panel, null);
            if (created == null) return;
            try
            {
                var all = AccountStore.Load();
                if (all.Any(a => SameAccount(a, created)))
                {
                    MessageBox.Show(panel, "此邮箱账户已添加。请到「管理」中修改授权码。", "燕子 · 验证码");
                    return;
                }
                all.Add(created);
                AccountStore.Save(all);
                ReloadChoices(created.Id);
                _ = RefreshAsync();
            }
            catch { MessageBox.Show(panel, "保存失败：请确认宿主已支持多账户加密存储。", "燕子 · 验证码"); }
        };
        helpButton.Click += (_, __) => ShowGuide(panel, selector.SelectedIndex > 0
            && selector.SelectedIndex - 1 < accounts.Count && accounts[selector.SelectedIndex - 1].Provider == "gmail");
        panel.Loaded += (_, __) =>
        {
            try { ReloadChoices(); _ = RefreshAsync(); }
            catch (Exception ex)
            {
                code.Text = "配置异常"; status.Text = "无法读取加密账户列表。";
                context.Log("mail-verification account-load exception=" + ex.GetType().Name);
            }
        };
        panel.Closed += (_, __) =>
        {
            pending?.Cancel();
            lock (Gate) { if (ReferenceEquals(Active, panel)) Active = null; }
            done.TrySetResult("窗口已关闭");
        };
        panel.ShowDialog();
    }

    private static bool SameAccount(MailAccount a, MailAccount b) =>
        a.Provider == b.Provider && string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);

    private static void ShowAccounts(Window owner, Action onChanged)
    {
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "已添加的邮箱", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(YanziPrimitives.Separator());
        root.Children.Add(new TextBlock
        {
            Text = "可添加多个 QQ/Gmail。所有授权码仅写入燕子的加密账户库。",
            Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap
        });
        var entries = YanziUi.WithStyle(new ListBox
        {
            Height = 206,
            DisplayMemberPath = nameof(MailAccount.Display),
            BorderThickness = new Thickness(0)
        }, YanziUi.Styles.List);
        var listCard = new YanziCard { Margin = new Thickness(0, 5, 0, 0) };
        listCard.BodyPadding = new Thickness(8);
        listCard.SetBody(entries);
        root.Children.Add(listCard);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var add = new Button { Content = "+ 添加", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var edit = new Button { Content = "修改", Width = 75, Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "移除", Width = 75 };
        YanziUi.WithStyle(add, YanziUi.Styles.DefaultButton);
        YanziUi.WithStyle(edit, YanziUi.Styles.OutlineButton);
        YanziUi.WithStyle(remove, YanziUi.Styles.DestructiveButton);
        controls.Children.Add(add); controls.Children.Add(edit); controls.Children.Add(remove);
        root.Children.Add(controls);
        var window = new Window
        {
            Title = "燕子 · 邮箱管理", Owner = owner, Width = 490, Height = 465,
            Content = root, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        ApplySharedUi(window);
        List<MailAccount> list = AccountStore.Load();
        void Rebind(string? selectedId = null)
        {
            list = AccountStore.Load();
            entries.ItemsSource = null; entries.ItemsSource = list;
            var idx = selectedId == null ? -1 : list.FindIndex(a => a.Id == selectedId);
            entries.SelectedIndex = idx >= 0 ? idx : list.Count > 0 ? 0 : -1;
        }
        Rebind();
        add.Click += (_, __) =>
        {
            var account = ShowAccountEditor(window, null);
            if (account == null) return;
            if (list.Any(a => SameAccount(a, account)))
            {
                MessageBox.Show(window, "这个邮箱已存在，可以选择该账户进行修改。", "燕子 · 验证码"); return;
            }
            try { list.Add(account); AccountStore.Save(list); Rebind(account.Id); onChanged(); }
            catch { MessageBox.Show(window, "保存失败，已有账户未被覆盖。", "燕子 · 验证码"); Rebind(); }
        };
        edit.Click += (_, __) =>
        {
            if (entries.SelectedItem is not MailAccount existing) return;
            var updated = ShowAccountEditor(window, existing);
            if (updated == null) return;
            if (list.Any(a => a.Id != existing.Id && SameAccount(a, updated)))
            {
                MessageBox.Show(window, "这个邮箱已添加，请避免重复账户。", "燕子 · 验证码"); return;
            }
            try
            {
                list[list.FindIndex(a => a.Id == existing.Id)] = updated;
                AccountStore.Save(list); Rebind(updated.Id); onChanged();
            }
            catch { MessageBox.Show(window, "修改未能保存，原账户仍保留。", "燕子 · 验证码"); Rebind(existing.Id); }
        };
        remove.Click += (_, __) =>
        {
            if (entries.SelectedItem is not MailAccount existing) return;
            if (!YanziDialog.Confirm(window, "移除邮箱",
                "确定移除 " + existing.Display + " 吗？凭证将从加密账户列表中移除。",
                "确认移除", dangerous: true)) return;
            try
            {
                list.RemoveAll(a => a.Id == existing.Id);
                AccountStore.Save(list); Rebind(); onChanged();
            }
            catch { MessageBox.Show(window, "移除失败，账户没有被删除。", "燕子 · 验证码"); Rebind(existing.Id); }
        };
        window.ShowDialog();
    }

    private static MailAccount? ShowAccountEditor(Window owner, MailAccount? previous)
    {
        var container = new StackPanel { Margin = new Thickness(21) };
        container.Children.Add(new TextBlock
        {
            Text = previous == null ? "添加邮箱账户" : "修改邮箱账户",
            FontSize = 20, FontWeight = FontWeights.SemiBold
        });
        var kind = new YanziSelect("选择邮箱类型") { Height = 36, Width = double.NaN };
        kind.Add("QQ 邮箱");
        kind.Add("Gmail");
        kind.Select(previous?.Provider == "gmail" ? "Gmail" : "QQ 邮箱");
        container.Children.Add(new YanziLabel { Caption = "邮箱类型", Target = kind, Margin = new Thickness(0, 14, 0, 5) });
        container.Children.Add(kind);
        var email = YanziUi.WithStyle(new TextBox
        {
            Height = 36, Text = previous?.Address ?? ""
        }, YanziUi.Styles.Input);
        container.Children.Add(new YanziLabel { Caption = "邮箱地址", Required = true,
            Target = email, Margin = new Thickness(0, 15, 0, 5) });
        container.Children.Add(email);
        var secret = YanziUi.WithStyle(new PasswordBox { Height = 36 }, YanziUi.Styles.Password);
        container.Children.Add(new YanziLabel { Caption = "授权码 / 应用专用密码",
            Target = secret, Margin = new Thickness(0, 15, 0, 5) });
        container.Children.Add(secret);
        var hint = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0) };
        void UpdateHint() => hint.Text = kind.SelectedValue == "Gmail"
            ? "请填该 Google 账户生成的16位应用专用密码，不能填 QQ 授权码。修改时留空保留原密码。"
            : "请填该 QQ 邮箱的 IMAP 授权码，不能填 Gmail 密码。修改时留空保留原授权码。";
        kind.SelectionChanged += (_, __) => UpdateHint();
        UpdateHint();
        container.Children.Add(hint);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
        container.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var help = new Button { Content = "? 获取指引", Width = 95, Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = "保存", Width = 80, Height = 32 };
        YanziUi.WithStyle(help, YanziUi.Styles.OutlineButton);
        YanziUi.WithStyle(save, YanziUi.Styles.DefaultButton);
        buttons.Children.Add(help); buttons.Children.Add(save); container.Children.Add(buttons);
        var window = new Window
        {
            Title = "燕子 · 账户设置", Owner = owner, Width = 490, Height = 495,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Content = container
        };
        ApplySharedUi(window);
        MailAccount? result = null;
        help.Click += (_, __) => ShowGuide(window, kind.SelectedValue == "Gmail");
        save.Click += (_, __) =>
        {
            var provider = kind.SelectedValue == "Gmail" ? "gmail" : "qq";
            var address = email.Text.Trim();
            var password = secret.Password;
            if (provider == "gmail") password = password.Replace(" ", "");
            if (!Regex.IsMatch(address, @"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant))
            {
                error.Text = "请输入有效的完整邮箱地址。"; return;
            }
            if (password.Length == 0 && previous != null && previous.Provider == provider)
                password = previous.Secret;
            if (password.Length == 0)
            {
                error.Text = provider == "gmail"
                    ? "请输入此 Google 账户的应用专用密码，不能使用普通登录密码或 QQ 授权码。"
                    : "请输入此 QQ 邮箱对应的 IMAP 授权码。";
                return;
            }
            result = new MailAccount
            {
                Id = previous?.Id ?? Guid.NewGuid().ToString("N"),
                Provider = provider, Address = address, Secret = password
            };
            secret.Clear();
            window.Close();
        };
        window.ShowDialog();
        return result;
    }
    private static void ShowGuide(Window owner, bool gmail)
    {
        var content = gmail
            ? "1. 登录你要接收验证码的 Google 账户并开启两步验证。\n\n2. 在该账户的「应用专用密码」页面生成供燕子使用的密码（16位字母）。\n\n3. 将同一 Google 账户的完整 Gmail 地址和密码填入设置。\n\n不能填 QQ 授权码，也不能填普通 Google 登录密码。某些 Workspace/企业账户不支持应用专用密码。"
            : "1. 登录 QQ 邮箱网页版，进入设置中的账户或安全选项。\n\n2. 启用 IMAP/SMTP 邮件客户端服务（如账户支持），按提示验证身份并生成授权码。\n\n3. 在燕子的设置中输入 QQ 邮箱地址和授权码。\n\n这里需要的是邮箱客户端授权码，不是 QQ 登录密码。";
        var url = gmail ? "https://myaccount.google.com/apppasswords" : "https://mail.qq.com/";
        var stack = new StackPanel { Margin = new Thickness(22) };
        stack.Children.Add(new TextBlock { Text = gmail ? "Gmail 授权指南" : "QQ 邮箱授权指南", FontSize = 19, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 14, 0, 18) });
        var open = YanziUi.WithStyle(new Button
        {
            Content = "打开官方页面", Height = 35,
            HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 135
        }, YanziUi.Styles.DefaultButton);
        open.Click += (_, __) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { MessageBox.Show("打开浏览器失败，请手动访问：" + url, "燕子 · 操作指南"); }
        };
        stack.Children.Add(open);
        var window = new Window
        {
            Title = "燕子 · 获取帮助", Owner = owner, Width = 475, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Content = stack
        };
        ApplySharedUi(window);
        window.ShowDialog();
    }

    // One protected E2EE-synchronized entry keeps the account list and credentials atomic.
    // Legacy single-account keys are read once for migration but never published to Windows.
    private sealed class MailAccount
    {
        public string Id { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Address { get; set; } = "";
        public string Secret { get; set; } = "";
        [System.Text.Json.Serialization.JsonIgnore]
        public string Host => Provider == "gmail" ? "imap.gmail.com" : "imap.qq.com";
        [System.Text.Json.Serialization.JsonIgnore]
        public string Display => (Provider == "gmail" ? "Gmail" : "QQ 邮箱") + " · " + Address;
    }

    private static class AccountStore
    {
        internal const string AccountsKey = "YANZI_MAIL_ACCOUNTS_V2";
        internal static List<MailAccount> Load()
        {
            var payload = Vault.Read(AccountsKey);
            if (!string.IsNullOrEmpty(payload))
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<List<MailAccount>>(payload)
                    ?? throw new InvalidDataException("Mail account list is invalid");
                if (data.Count > 100) throw new InvalidDataException("Mail account list exceeds limit");
                if (data.Any(a => (a.Provider != "gmail" && a.Provider != "qq")
                                  || string.IsNullOrWhiteSpace(a.Address)
                                  || string.IsNullOrWhiteSpace(a.Secret)
                                  || string.IsNullOrWhiteSpace(a.Id)))
                    throw new InvalidDataException("Mail account record is incomplete");
                return data;
            }
            // Migration is non-destructive. Persist [] for an empty result so removals
            // do not cause deleted legacy accounts to reappear on subsequent launches.
            var result = new List<MailAccount>();
            foreach (var item in new[]
            {
                (Provider:"qq", Id:"legacy-qq", Email:"YANZI_MAIL_QQ_EMAIL", Secret:"YANZI_MAIL_QQ_AUTH_CODE"),
                (Provider:"gmail", Id:"legacy-gmail", Email:"YANZI_MAIL_GMAIL_EMAIL", Secret:"YANZI_MAIL_GMAIL_APP_PASSWORD")
            })
            {
                var email = Vault.Read(item.Email).Trim();
                var password = Vault.Read(item.Secret);
                if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
                    result.Add(new MailAccount { Id=item.Id, Provider=item.Provider, Address=email, Secret=password });
            }
            return result;
        }
        internal static void Save(IEnumerable<MailAccount> accounts)
        {
            var items = accounts.ToArray();
            if (items.Length > 100) throw new InvalidOperationException("Too many mail accounts");
            if (items.Any(a => (a.Provider != "gmail" && a.Provider != "qq")
                                || string.IsNullOrWhiteSpace(a.Secret)
                                || string.IsNullOrWhiteSpace(a.Address)))
                throw new InvalidOperationException("Missing account data");
            var json = System.Text.Json.JsonSerializer.Serialize(items);
            // Use the host's existing atomic metadata+DPAPI/E2EE path, not extension storage.
            Vault.Save(AccountsKey, json, AccountsKey, json);
        }
    }
    // The host is the only credential owner: never write credentials to extension storage,
    // a regular settings JSON file, or a separately synchronized document.
    private static class Vault
    {
        private static readonly object SaveGate = new();
        private static Type Store => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("OpenQuickHost.AppEnvironmentVariableStore", throwOnError: false))
            .FirstOrDefault(type => type != null)
            ?? throw new InvalidOperationException("Yanzi protected environment vault is unavailable");

        internal static bool SupportsPrivateMailCredentials()
        {
            var field = Store.GetField("ProviderOnlySecrets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field?.GetValue(null) is not System.Collections.IEnumerable values) return false;
            var names = values.Cast<object>().OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new[] { "YANZI_MAIL_QQ_EMAIL", "YANZI_MAIL_QQ_AUTH_CODE",
                           "YANZI_MAIL_GMAIL_EMAIL", "YANZI_MAIL_GMAIL_APP_PASSWORD",
                           "YANZI_MAIL_ACCOUNTS_V2" }
                .All(names.Contains);
        }

        internal static string Read(string key)
        {
            var method = Store.GetMethod("GetValue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new MissingMethodException("GetValue");
            return (method.Invoke(null, new object?[] { key }) as string) ?? "";
        }

        internal static void Save(string addressKey, string address, string secretKey, string secret)
        {
            lock (SaveGate)
            {
                var store = Store;
                if (!SupportsPrivateMailCredentials()) throw new InvalidOperationException("Mail secret isolation is not installed");
                var type = store.Assembly.GetType("OpenQuickHost.AppEnvironmentVariableSettings")
                    ?? throw new MissingMemberException("AppEnvironmentVariableSettings");
                var load = store.GetMethod("Load", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?? throw new MissingMethodException("Load");
                var save = store.GetMethod("Save", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?? throw new MissingMethodException("Save");
                var nameProp = type.GetProperty("Name") ?? throw new MissingMemberException("Name");
                var valueProp = type.GetProperty("Value") ?? throw new MissingMemberException("Value");
                var descriptionProp = type.GetProperty("Description") ?? throw new MissingMemberException("Description");
                var originals = (System.Collections.IEnumerable)(load.Invoke(null, null)
                    ?? throw new InvalidOperationException("Vault read failed"));
                var items = originals.Cast<object>().ToList();
                void Upsert(string key, string value, string description)
                {
                    var entry = items.FirstOrDefault(item =>
                        string.Equals(nameProp.GetValue(item) as string, key, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        entry = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Vault entry allocation failed");
                        items.Add(entry);
                    }
                    nameProp.SetValue(entry, key);
                    valueProp.SetValue(entry, value);
                    descriptionProp.SetValue(entry, description);
                }
                Upsert(addressKey, address, "验证码小程序邮箱地址");
                // A blank field preserves an existing key, never overwrites it with an empty string.
                if (!string.IsNullOrWhiteSpace(secret))
                    Upsert(secretKey, secret, "验证码小程序邮箱授权凭证");
                var array = Array.CreateInstance(type, items.Count);
                for (var i = 0; i < items.Count; i++) array.SetValue(items[i], i);
                save.Invoke(null, new object[] { array });
            }
        }
    }

    private static class QQ
    {
        internal sealed class LoginRejected : Exception { }
        internal static async Task<(string Code, string Sender, DateTimeOffset Date)?> FindAsync(string host, string address, string authCode, CancellationToken ct)
        {
            if (host != "imap.qq.com" && host != "imap.gmail.com") throw new ArgumentOutOfRangeException(nameof(host));
            using var client = new TcpClient();
            await client.ConnectAsync(host, 993, ct);
            await using var ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct);
            var io = new Wire(ssl);
            var greeting = await io.Line(ct);
            if (!greeting.StartsWith("* OK", StringComparison.OrdinalIgnoreCase)) throw new IOException("IMAP greeting failed");
            await io.Cmd("LOGIN " + Quote(address) + " " + Quote(authCode), ct, true);
            await io.Cmd("SELECT INBOX", ct);
            var day = DateTime.UtcNow.AddDays(-1).ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
            var result = await io.Cmd("UID SEARCH SINCE " + day, ct);
            var ids = result.Lines.Where(s => s.StartsWith("* SEARCH ", StringComparison.OrdinalIgnoreCase))
                .SelectMany(s => s.Substring(9).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(s => uint.TryParse(s, out _)).TakeLast(32).Reverse().ToArray();
            (string Code, string Sender, DateTimeOffset Date)? latest = null;
            foreach (var id in ids)
            {
                var msg = await io.Cmd("UID FETCH " + id + " (INTERNALDATE BODY.PEEK[HEADER.FIELDS (DATE FROM SUBJECT)] BODY.PEEK[TEXT]<0." + MaxBody + ">)", ct);
                if (msg.Literals.Count < 2) continue;
                var headers = Encoding.UTF8.GetString(msg.Literals[0]);
                var dateValue = Header(headers, "Date");
                if (!DateTimeOffset.TryParse(dateValue, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var sent)) continue;
                // Prefer mailbox INTERNALDATE (server receipt timestamp) to sender-provided Date header.
                var internalDate = Regex.Match(string.Join(" ", msg.Lines), @"INTERNALDATE\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (internalDate.Success)
                {
                    var normalized = Regex.Replace(internalDate.Groups[1].Value,
                        @"([+-]\d{2})(\d{2})$", "$1:$2");
                    if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var received))
                        sent = received;
                }
                if (sent < DateTimeOffset.UtcNow.AddMinutes(-60) || sent > DateTimeOffset.UtcNow.AddMinutes(3)) continue;
                var subject = DecodeHeader(Header(headers, "Subject"));
                var text = Encoding.UTF8.GetString(msg.Literals[1]);
                var body = DecodeBody(text);
                var simple = WebUtility.HtmlDecode(Regex.Replace(body, @"<[^>]+>", " "));
                simple = Regex.Replace(simple, @"\s+", " ");
                var content = subject + " " + simple;
                if (!VerifyWords.IsMatch(content)) continue;
                var near = NearCode.Match(content);
                var candidate = near.Success ? near.Groups[1].Value : Regex.Matches(content, @"(?<!\d)\d{6}(?!\d)").Cast<Match>().Select(m => m.Value).FirstOrDefault();
                if (candidate == null) continue;
                var digits = Regex.Replace(candidate, @"\D", "");
                if (digits.Length < 4 || digits.Length > 8) continue;
                var senderMatch = Regex.Match(Header(headers, "From"), @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}");
                var found = (Code: digits, Sender: senderMatch.Success ? senderMatch.Value : "未知发件人", Date: sent);
                if (!latest.HasValue || found.Date > latest.Value.Date) latest = found;
            }
            return latest;
        }
        private static string Header(string headers, string field)
        {
            var unfolded = Regex.Replace(headers, @"\r?\n[ \t]+", " ");
            var m = Regex.Match(unfolded, "^" + field + @":\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }
        private static string DecodeHeader(string raw) =>
            Regex.Replace(raw, @"=\?([^?]+)\?([BbQq])\?([^?]+)\?=", m =>
            {
                try
                {
                    var bytes = m.Groups[2].Value.Equals("B", StringComparison.OrdinalIgnoreCase)
                        ? Convert.FromBase64String(m.Groups[3].Value)
                        : QuotedPrintable(m.Groups[3].Value.Replace('_', ' '));
                    return Encoding.GetEncoding(m.Groups[1].Value).GetString(bytes);
                }
                catch { return m.Value; }
            });
        private static string DecodeBody(string body)
        {
            if (body.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(body, @"Content-Transfer-Encoding:\s*base64\s*\r?\n(?:[^\r\n]*\r?\n)*?\r?\n([A-Za-z0-9+/\r\n=]{20,})", RegexOptions.IgnoreCase);
                if (m.Success) try { return body + " " + Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(m.Groups[1].Value, @"\s", ""))); } catch { }
            }
            if (body.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase))
                try { return body + " " + Encoding.UTF8.GetString(QuotedPrintable(body)); } catch { }
            return body;
        }
        private static byte[] QuotedPrintable(string value)
        {
            var source = Regex.Replace(value, @"=\r?\n", "");
            using var buf = new MemoryStream();
            for (int i = 0; i < source.Length; i++)
                if (source[i] == '=' && i + 2 < source.Length && byte.TryParse(source.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                { buf.WriteByte(b); i += 2; }
                else foreach (var c in Encoding.UTF8.GetBytes(source.Substring(i, 1))) buf.WriteByte(c);
            return buf.ToArray();
        }
        private static string Quote(string text)
        {
            if (text.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new ArgumentException("Invalid IMAP credential");
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private sealed class Reply
        {
            public List<string> Lines { get; } = new();
            public List<byte[]> Literals { get; } = new();
        }
        private sealed class Wire
        {
            private readonly Stream stream;
            private readonly byte[] buffer = new byte[8192];
            private int offset, length, tag;
            public Wire(Stream s) { stream = s; }
            private async Task<byte> Next(CancellationToken ct)
            {
                if (offset == length)
                {
                    length = await stream.ReadAsync(buffer.AsMemory(), ct);
                    offset = 0;
                    if (length == 0) throw new EndOfStreamException();
                }
                return buffer[offset++];
            }
            public async Task<string> Line(CancellationToken ct)
            {
                using var b = new MemoryStream();
                while (b.Length < 262144)
                {
                    var x = await Next(ct);
                    if (x == '\n') return Encoding.UTF8.GetString(b.ToArray()).TrimEnd('\r');
                    b.WriteByte(x);
                }
                throw new InvalidDataException("IMAP line too long");
            }
            public async Task<Reply> Cmd(string command, CancellationToken ct, bool login = false)
            {
                var id = "Y" + Interlocked.Increment(ref tag).ToString("D4");
                await stream.WriteAsync(Encoding.UTF8.GetBytes(id + " " + command + "\r\n"), ct);
                await stream.FlushAsync(ct);
                var result = new Reply();
                for (int n = 0; n < 2000; n++)
                {
                    var line = await Line(ct);
                    if (line.StartsWith(id + " ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (line.StartsWith(id + " OK", StringComparison.OrdinalIgnoreCase)) return result;
                        if (login) throw new LoginRejected();
                        throw new IOException("IMAP request failed");
                    }
                    result.Lines.Add(line);
                    var sizeMatch = Regex.Match(line, @"\{(\d+)\}$");
                    if (sizeMatch.Success)
                    {
                        var count = int.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (count > 4 * 1024 * 1024) throw new InvalidDataException("IMAP literal exceeds safe read limit");
                        // Some IMAP providers return more bytes than the requested partial FETCH.
                        // Consume the entire literal to preserve stream framing, but retain only
                        // a bounded prefix needed to recognize a short verification message.
                        var keep = Math.Min(count, MaxBody + 8192);
                        var bytes = new byte[keep];
                        for (var i = 0; i < count; i++)
                        {
                            var next = await Next(ct);
                            if (i < keep) bytes[i] = next;
                        }
                        result.Literals.Add(bytes);
                    }
                }
                throw new IOException("IMAP response timeout");
            }
        }
    }
}