using System.IO;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>
/// Device-local, per-miniapp Git update preferences. No stored credentials,
/// no implicit Git init/clone and no automatic service restart.
/// </summary>
public sealed class GitExtensionUpdateSettingsWindow : Window
{
    private readonly string _extensionId;
    private readonly string _directory;
    private readonly ComboBox _mode = new();
    private readonly ComboBox _frequency = new();
    private readonly TextBox _remote = new();
    private readonly TextBox _branch = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public GitExtensionUpdateSettingsWindow(string extensionId, string title, string directory)
    {
        _extensionId = extensionId;
        _directory = directory;
        Title = $"Git 更新策略 · {title}";
        Width = 490;
        Height = 535;
        MinWidth = 410;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        var existing = GitExtensionUpdatePolicyStore.Load();
        existing.TryGetValue(extensionId, out var current);
        var policy = current ?? new GitExtensionUpdatePolicy { ExtensionId = extensionId };

        var panel = new StackPanel { Margin = new Thickness(22) };
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        Content = scroller;

        panel.Children.Add(new TextBlock
        {
            Text = "每个小程序独立配置 Git 检查；配置仅保存在此设备，默认不自动更新。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });

        AddLabel(panel, "更新方式");
        AddOption(_mode, "关闭自动检查", "off");
        AddOption(_mode, "检查后提醒（推荐）", "notify");
        AddOption(_mode, "仅在工作区干净且可快进时自动更新", "auto");
        Select(_mode, policy.Mode);
        panel.Children.Add(_mode);

        AddLabel(panel, "检查频率");
        AddOption(_frequency, "每次启动燕子时", "startup");
        AddOption(_frequency, "每小时", "hourly");
        AddOption(_frequency, "每天", "daily");
        Select(_frequency, policy.Frequency);
        panel.Children.Add(_frequency);

        AddLabel(panel, "Git 远端名称（需已在仓库配置）");
        _remote.Text = policy.Remote;
        panel.Children.Add(_remote);

        AddLabel(panel, "远端分支名称");
        _branch.Text = policy.Branch;
        panel.Children.Add(_branch);

        var isRepo = Directory.Exists(directory) && ExtensionGitSafety.HasGitWorktree(directory);
        panel.Children.Add(new TextBlock
        {
            Text = isRepo ? $"仓库位置：{directory}" : "当前目录未初始化 Git 仓库。请先在小程序目录内配置独立 Git 仓库。",
            Margin = new Thickness(0, 15, 0, 6), TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Git fetch 只获取远端代码；自动更新仅允许 fast-forward。冲突或未提交文件绝不覆盖，也不会自动重启正在运行的服务。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        _status.Margin = new Thickness(0, 4, 0, 10);
        panel.Children.Add(_status);

        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var check = new Button { Content = "立即检查", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), IsEnabled = isRepo };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            _status.Text = "正在检查 Git 远端...";
            try
            {
                var probe = Current() with { Mode = "notify" };
                if (!GitExtensionUpdateService.ValidRemote(probe.Remote) || !GitExtensionUpdateService.ValidBranch(probe.Branch))
                {
                    _status.Text = "远端或分支名称不合法";
                    return;
                }
                var result = await GitExtensionUpdateService.CheckAsync(_directory, probe);
                _status.Text = result.Message;
            }
            finally { check.IsEnabled = true; }
        };
        var save = new Button { Content = "保存设置", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0), IsEnabled = isRepo };
        save.Click += (_, _) =>
        {
            try
            {
                GitExtensionUpdatePolicyStore.Save(Current());
                DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                _status.Text = ex.Message;
            }
        };
        var cancel = new Button { Content = "取消", Padding = new Thickness(14, 8, 14, 8) };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(check);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
    }

    private GitExtensionUpdatePolicy Current() => new()
    {
        ExtensionId = _extensionId,
        Mode = (_mode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "off",
        Frequency = (_frequency.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "startup",
        Remote = _remote.Text.Trim(),
        Branch = _branch.Text.Trim()
    };

    private static void AddLabel(StackPanel panel, string text) =>
        panel.Children.Add(new TextBlock
        {
            Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 5)
        });

    private static void AddOption(ComboBox box, string name, string value) =>
        box.Items.Add(new ComboBoxItem { Content = name, Tag = value });

    private static void Select(ComboBox box, string value)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (Equals(item.Tag, value)) { box.SelectedItem = item; return; }
        }
        box.SelectedIndex = 0;
    }
}