using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenQuickHost.CSharpRuntime;
using Yanzi.UI.Wpf;

public static class YanziAction
{
    private static CapabilityLabWindow? _currentWindow;
    private static readonly object Gate = new();

    public static async Task<string> RunAsync(YanziActionContext context)
    {
        lock (Gate)
        {
            if (_currentWindow != null && _currentWindow.IsLoaded)
            {
                _currentWindow.Dispatcher.Invoke(() =>
                {
                    if (_currentWindow.WindowState == WindowState.Minimized)
                        _currentWindow.WindowState = WindowState.Normal;
                    _currentWindow.Activate();
                });
                return "能力实验室窗口已激活";
            }
        }

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CapabilityLabWindow(context);
                lock (Gate) _currentWindow = window;
                window.Closed += (_, _) =>
                {
                    lock (Gate)
                    {
                        if (ReferenceEquals(_currentWindow, window))
                            _currentWindow = null;
                    }
                    tcs.TrySetResult("能力实验室已关闭");
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                context.Log("能力实验室异常: " + ex);
                tcs.TrySetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Name = "YanziCapabilityLab";
        thread.Start();
        return await tcs.Task;
    }
}

public sealed class CapabilityLabWindow : Window
{
    private readonly YanziActionContext _context;
    private readonly Dictionary<string, CapabilityCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _logBox = new();
    private readonly TextBlock _summaryText = new();
    private readonly Button _refreshAllButton = new();
    private readonly Button _runAllButton = new();
    private readonly string _experimentRoot;
    private readonly TextBlock _npmStatus = new();
    private readonly TextBlock _npxStatus = new();

    private static readonly CapabilitySpec[] Specs =
    {
        new("git", "Git", "git>=2.40", "版本控制 · 时间胶囊提交"),
        new("python", "Python", "python>=3.12", "纯标准库 · 曼德勃罗图"),
        new("node", "Node.js / npm / npx", "node>=22", "异步计算 · 离线安装包及命令执行"),
        new("ffmpeg", "FFmpeg", "ffmpeg>=7", "音视频工具链 · 合成测试片")
    };

    public CapabilityLabWindow(YanziActionContext context)
    {
        _context = context;
        _experimentRoot = Path.Combine(context.ExtensionDataDirectory, "capability-lab", "experiments");
        Directory.CreateDirectory(_experimentRoot);

        Title = "能力实验室 · 燕子";
        Width = 1120;
        Height = 780;
        MinWidth = 900;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#0B0F14");
        Foreground = Brush("#E5E7EB");
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        YanziUi.ApplyTo(this, YanziTheme.Dark);
        SetResourceReference(Window.BackgroundProperty, "Yanzi.Color.Background");
        Content = BuildUi();

        Loaded += async (_, _) =>
        {
            AddLog("能力实验室已启动。界面通过 dependency.* 宿主能力工作，不直接管理 WinGet。");
            await RefreshAllAsync();
        };
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "能力实验室",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F8FAFC")
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "宿主 Provider × 小程序能力调用 × 真实脚本验证",
            FontSize = 13,
            Foreground = Brush("#94A3B8"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        _summaryText.Text = "正在读取能力状态…";
        _summaryText.FontSize = 12;
        _summaryText.Foreground = Brush("#64748B");
        _summaryText.Margin = new Thickness(0, 7, 0, 0);
        titleStack.Children.Add(_summaryText);
        header.Children.Add(titleStack);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top
        };
        ConfigureButton(_refreshAllButton, "刷新全部", false);
        _refreshAllButton.Click += async (_, _) => await RefreshAllAsync();
        actions.Children.Add(_refreshAllButton);

        ConfigureButton(_runAllButton, "运行全部实验", true);
        _runAllButton.Margin = new Thickness(8, 0, 0, 0);
        _runAllButton.Click += async (_, _) => await RunAllExperimentsAsync();
        actions.Children.Add(_runAllButton);

        var openFolder = new Button();
        ConfigureButton(openFolder, "打开实验目录", false);
        openFolder.Margin = new Thickness(8, 0, 0, 0);
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(_experimentRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", _experimentRoot) { UseShellExecute = true });
        };
        actions.Children.Add(openFolder);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var cardsGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < Specs.Length; i++)
        {
            var card = CreateCard(Specs[i]);
            _cards[Specs[i].Name] = card;
            Grid.SetRow(card.Container, i / 2);
            Grid.SetColumn(card.Container, i % 2);
            card.Container.Margin = new Thickness(i % 2 == 0 ? 0 : 8, i / 2 == 0 ? 0 : 8, i % 2 == 0 ? 8 : 0, 0);
            cardsGrid.Children.Add(card.Container);
        }

        Grid.SetRow(cardsGrid, 1);
        root.Children.Add(cardsGrid);

        var logBorder = new Border
        {
            Background = null,
            BorderBrush = null,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14)
        };

        logBorder.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        logBorder.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");
        var logGrid = new Grid();
        logGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        logGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var logHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        logHeader.Children.Add(new TextBlock
        {
            Text = "实验日志",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#E2E8F0")
        });
        var clear = new Button();
        ConfigureButton(clear, "清空", false);
        clear.Padding = new Thickness(10, 4, 10, 4);
        clear.Click += (_, _) => _logBox.Clear();
        Grid.SetColumn(clear, 1);
        logHeader.Children.Add(clear);
        logGrid.Children.Add(logHeader);

        _logBox.IsReadOnly = true;
        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _logBox.Background = Brush("#0B1118");
        _logBox.Foreground = Brush("#CBD5E1");
        _logBox.BorderThickness = new Thickness(0);
        _logBox.Padding = new Thickness(12);
        _logBox.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _logBox.FontSize = 12;
        Grid.SetRow(_logBox, 1);
        logGrid.Children.Add(_logBox);
        logBorder.Child = logGrid;

        Grid.SetRow(logBorder, 2);
        root.Children.Add(logBorder);
        return root;
    }

    private CapabilityCard CreateCard(CapabilitySpec spec)
    {
        var container = new Border
        {
            Background = null,
            BorderBrush = null,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16)
        };
        container.SetResourceReference(Border.BackgroundProperty, "Yanzi.Color.Card");
        container.SetResourceReference(Border.BorderBrushProperty, "Yanzi.Color.Border");

        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = spec.DisplayName,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#F1F5F9")
        });
        title.Children.Add(new TextBlock
        {
            Text = spec.Description,
            FontSize = 11.5,
            Foreground = Brush("#718096"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        top.Children.Add(title);

        // Shared 22-DIP capsule Badge, not a stretchable hand-built Border.
        var badge = new YanziBadge
        {
            Content = "未检查",
            Variant = YanziBadgeVariant.Secondary,
            Background = Brush("#1F2937"),
            Foreground = Brush("#94A3B8"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(badge, 1);
        top.Children.Add(badge);
        stack.Children.Add(top);

        stack.Children.Add(new TextBlock
        {
            Text = spec.Requirement,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Foreground = Brush("#A5B4FC"),
            Margin = new Thickness(0, 12, 0, 0)
        });

        var version = new TextBlock
        {
            Text = "版本：—",
            FontSize = 12,
            Foreground = Brush("#CBD5E1"),
            Margin = new Thickness(0, 10, 0, 0)
        };
        stack.Children.Add(version);

        var path = new TextBlock
        {
            Text = "路径：—",
            FontSize = 11,
            Foreground = Brush("#64748B"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = "—",
            Margin = new Thickness(0, 4, 0, 0)
        };
        stack.Children.Add(path);

        if (spec.Name == "node")
        {
            var packageTools = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            ConfigureToolStatus(_npmStatus, "npm");
            ConfigureToolStatus(_npxStatus, "npx");
            packageTools.Children.Add(_npmStatus);
            packageTools.Children.Add(_npxStatus);
            stack.Children.Add(packageTools);
        }

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 5,
            BorderThickness = new Thickness(0),
            Background = Brush("#1E293B"),
            Foreground = Brush("#60A5FA"),
            Margin = new Thickness(0, 13, 0, 0)
        };
        stack.Children.Add(progress);

        var stage = new TextBlock
        {
            Text = "等待操作",
            FontSize = 11,
            Foreground = Brush("#64748B"),
            Margin = new Thickness(0, 5, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        stack.Children.Add(stage);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 13, 0, 0)
        };
        var check = new Button();
        ConfigureButton(check, "检查", false);
        var ensure = new Button();
        ConfigureButton(ensure, "准备", false);
        ensure.Margin = new Thickness(7, 0, 0, 0);
        var experiment = new Button();
        ConfigureButton(experiment, "运行实验", true);
        experiment.Margin = new Thickness(7, 0, 0, 0);
        buttons.Children.Add(check);
        buttons.Children.Add(ensure);
        buttons.Children.Add(experiment);
        stack.Children.Add(buttons);

        container.Child = stack;
        var card = new CapabilityCard(spec, container, badge, version, path, progress, stage, check, ensure, experiment);
        check.Click += async (_, _) => await CheckAsync(card);
        ensure.Click += async (_, _) => await EnsureAsync(card);
        experiment.Click += async (_, _) => await RunExperimentAsync(card);
        return card;
    }

    private async Task RefreshAllAsync()
    {
        SetGlobalBusy(true);
        try
        {
            var ready = 0;
            foreach (var spec in Specs)
            {
                if (await CheckAsync(_cards[spec.Name], false))
                    ready++;
            }
            _summaryText.Text = $"{ready}/{Specs.Length} 项能力满足默认实验要求 · {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            SetGlobalBusy(false);
        }
    }

    private async Task<bool> CheckAsync(CapabilityCard card, bool log = true)
    {
        try
        {
            SetCardStage(card, "checking", "正在通过宿主能力检测…", 8);
            var data = await _context.Capabilities.InvokeAsync(
                "dependency.status",
                new { name = card.Spec.Requirement });
            var available = ReadBool(data, "available");
            ApplyStatus(card, data, available);
            if (card.Spec.Name == "node")
                await RefreshNodeToolsAsync(ReadString(data, "path"), available);
            if (log)
                AddLog($"[{card.Spec.DisplayName}] 检查完成：{(available ? "满足" : "未满足")} {ReadString(data, "version") ?? ""}");
            return available;
        }
        catch (Exception ex)
        {
            SetCardStage(card, "failed", ex.Message, 0);
            AddLog($"[{card.Spec.DisplayName}] 检查失败：{ex.Message}");
            return false;
        }
    }

    private async Task<bool> EnsureAsync(CapabilityCard card)
    {
        SetCardBusy(card, true);
        try
        {
            AddLog($"[{card.Spec.DisplayName}] 请求宿主准备 {card.Spec.Requirement}");
            SetCardStage(card, "checking", "提交 dependency.ensure…", 5);

            var ensureTask = _context.Capabilities.InvokeAsync(
                "dependency.ensure",
                new { name = card.Spec.Requirement });

            while (!ensureTask.IsCompleted)
            {
                await UpdateProgressFromHostAsync(card);
                await Task.Delay(300);
            }

            var data = await ensureTask;
            await UpdateProgressFromHostAsync(card);
            var available = ReadBool(data, "available");
            ApplyStatus(card, data, available);
            if (card.Spec.Name == "node")
                await RefreshNodeToolsAsync(ReadString(data, "path"), available);
            AddLog(
                $"[{card.Spec.DisplayName}] 准备结果：{(available ? "可用" : "失败")}；" +
                $"version={ReadString(data, "version") ?? "—"}；provider={ReadString(data, "provider") ?? "—"}");
            return available;
        }
        catch (Exception ex)
        {
            SetCardStage(card, "failed", ex.Message, 0);
            AddLog($"[{card.Spec.DisplayName}] 准备失败：{ex.Message}");
            return false;
        }
        finally
        {
            SetCardBusy(card, false);
        }
    }

    private async Task UpdateProgressFromHostAsync(CapabilityCard card)
    {
        try
        {
            var data = await _context.Capabilities.InvokeAsync(
                "dependency.progress",
                new { name = card.Spec.Requirement });
            var state = ReadString(data, "state") ?? "idle";
            var message = ReadString(data, "message") ?? "处理中…";
            var percent = ReadInt(data, "percent");
            SetCardStage(card, state, message, percent ?? (state == "installing" ? 35 : 0));
        }
        catch
        {
            // 进度查询是辅助信息，不影响主安装任务。
        }
    }

    private async Task RunExperimentAsync(CapabilityCard card)
    {
        SetCardBusy(card, true);
        try
        {
            if (!await EnsureAsyncCore(card))
                return;

            var status = await _context.Capabilities.InvokeAsync(
                "dependency.status",
                new { name = card.Spec.Requirement });
            var path = ReadString(status, "path");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AddLog($"[{card.Spec.DisplayName}] 找不到可执行文件路径，无法运行实验。");
                return;
            }

            AddLog($"[{card.Spec.DisplayName}] 开始实验。");
            var result = card.Spec.Name switch
            {
                "python" => await RunPythonExperimentAsync(path),
                "node" => await RunNodeExperimentAsync(path),
                "ffmpeg" => await RunFfmpegExperimentAsync(path),
                "git" => await RunGitExperimentAsync(path),
                _ => "未定义实验"
            };

            if (card.Spec.Name == "node")
                await RefreshNodeToolsAsync(path, true);
            AddLog($"[{card.Spec.DisplayName}] 实验完成：{result}");
            SetCardStage(card, "completed", "实验执行完成", 100);
        }
        catch (Exception ex)
        {
            SetCardStage(card, "failed", "实验失败：" + ex.Message, 0);
            AddLog($"[{card.Spec.DisplayName}] 实验失败：{ex}");
        }
        finally
        {
            SetCardBusy(card, false);
        }
    }

    private async Task<bool> EnsureAsyncCore(CapabilityCard card)
    {
        var status = await _context.Capabilities.InvokeAsync(
            "dependency.status",
            new { name = card.Spec.Requirement });
        if (ReadBool(status, "available"))
        {
            ApplyStatus(card, status, true);
            return true;
        }

        AddLog($"[{card.Spec.DisplayName}] 实验前发现依赖未满足，自动调用 dependency.ensure。");
        var ensureTask = _context.Capabilities.InvokeAsync(
            "dependency.ensure",
            new { name = card.Spec.Requirement });
        while (!ensureTask.IsCompleted)
        {
            await UpdateProgressFromHostAsync(card);
            await Task.Delay(300);
        }
        var ensured = await ensureTask;
        await UpdateProgressFromHostAsync(card);
        var ok = ReadBool(ensured, "available");
        ApplyStatus(card, ensured, ok);
        return ok;
    }

    private async Task RunAllExperimentsAsync()
    {
        SetGlobalBusy(true);
        try
        {
            foreach (var name in new[] { "python", "node", "ffmpeg", "git" })
                await RunExperimentAsync(_cards[name]);
            AddLog("全部实验链执行结束。Git 时间胶囊会收集前面生成的实验产物摘要。");
        }
        finally
        {
            SetGlobalBusy(false);
        }
    }

    private async Task<string> RunPythonExperimentAsync(string python)
    {
        var folder = NewExperimentFolder("python");
        var output = Path.Combine(folder, "mandelbrot.pgm");
        var script = Path.Combine(_context.ExtensionDirectory, "scripts", "python_mandelbrot.py");
        var run = await RunProcessAsync(python, new[] { script, output }, folder);
        EnsureSuccess(run, "Python");
        return $"生成纯标准库曼德勃罗图：{output}；输出={Compact(run.StdOut)}";
    }

    private async Task<string> RunNodeExperimentAsync(string node)
    {
        var folder = NewExperimentFolder("node");
        var script = Path.Combine(_context.ExtensionDirectory, "scripts", "node_crypto.mjs");
        var run = await RunProcessAsync(node, new[] { script }, folder);
        EnsureSuccess(run, "Node.js");
        var jsonPath = Path.Combine(folder, "crypto-result.json");
        File.WriteAllText(jsonPath, run.StdOut.Trim(), new UTF8Encoding(false));
        var npmNpxResult = await RunNpmNpxExperimentAsync(node, folder);
        return $"完成 8 路并行 SHA-256 哈希链：{Compact(run.StdOut)}；{npmNpxResult}";
    }

    private async Task RefreshNodeToolsAsync(string? node, bool available)
    {
        await CheckNodeToolAsync(node, available, "npm", _npmStatus);
        await CheckNodeToolAsync(node, available, "npx", _npxStatus);
    }

    private async Task CheckNodeToolAsync(string? node, bool available, string tool, TextBlock label)
    {
        if (!available || string.IsNullOrWhiteSpace(node) || !File.Exists(node))
        {
            SetToolStatus(label, tool, "等待 Node 就绪", false);
            return;
        }

        var directory = Path.GetDirectoryName(node)!;
        var commandFile = Path.Combine(directory, tool + ".cmd");
        var cli = NodeToolCliPath(node, tool);
        if (!File.Exists(commandFile) || !File.Exists(cli))
        {
            SetToolStatus(label, tool, "缺少命令或 CLI 文件", false);
            AddLog($"[{tool}] 当前 Node 安装缺少 {tool}.cmd 或 {Path.GetFileName(cli)}");
            return;
        }

        try
        {
            // Directly run the official npm/npx CLI with this exact Node runtime.
            // This avoids PowerShell execution policies and Windows .cmd quoting.
            var result = await RunProcessAsync(node, new[] { cli, "--version" }, directory);
            EnsureSuccess(result, tool);
            var version = result.StdOut.Trim();
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException(tool + " 没有输出版本号");
            SetToolStatus(label, tool, "v" + version.TrimStart('v'), true);
        }
        catch (Exception ex)
        {
            SetToolStatus(label, tool, "不可用：" + Compact(ex.Message), false);
            AddLog($"[{tool}] 独立检测失败：{ex.Message}");
        }
    }

    private static string NodeToolCliPath(string node, string tool)
        => Path.Combine(Path.GetDirectoryName(node)!, "node_modules", "npm", "bin",
            tool == "npx" ? "npx-cli.js" : "npm-cli.js");

    private async Task<string> RunNpmNpxExperimentAsync(string node, string folder)
    {
        var npmCli = NodeToolCliPath(node, "npm");
        var npxCli = NodeToolCliPath(node, "npx");
        if (!File.Exists(npmCli) || !File.Exists(npxCli))
            throw new FileNotFoundException("Node 运行时缺少 npm 或 npx CLI，无法完成包管理实验。");

        var smoke = Path.Combine(folder, "npm-npx-smoke");
        var package = Path.Combine(smoke, "probe-package");
        Directory.CreateDirectory(package);
        try
        {
            // Local file dependency, offline and without lifecycle scripts:
            // no registry access, global packages or installed user tools are changed.
            File.WriteAllText(Path.Combine(package, "package.json"),
                "{\"name\":\"yanzi-npm-smoke-cli\",\"version\":\"1.0.0\"," +
                "\"bin\":{\"yanzi-npm-smoke-cli\":\"cli.js\"}}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(package, "cli.js"),
                "#!/usr/bin/env node\nconsole.log('YANZI_NPX_OK');\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(smoke, "package.json"),
                "{\"private\":true,\"dependencies\":{\"yanzi-npm-smoke-cli\":\"file:./probe-package\"}}",
                new UTF8Encoding(false));

            var install = await RunProcessAsync(node, new[]
            {
                npmCli, "install", "--offline", "--ignore-scripts", "--no-audit", "--no-fund"
            }, smoke);
            EnsureSuccess(install, "npm 离线本地依赖安装");
            if (!File.Exists(Path.Combine(smoke, "node_modules", ".bin", "yanzi-npm-smoke-cli.cmd")))
                throw new InvalidDataException("npm 没有生成本地命令入口。");

            var execute = await RunProcessAsync(node, new[]
            {
                npxCli, "--offline", "--no-install", "yanzi-npm-smoke-cli"
            }, smoke);
            EnsureSuccess(execute, "npx 本地命令执行");
            if (!execute.StdOut.Contains("YANZI_NPX_OK", StringComparison.Ordinal))
                throw new InvalidDataException("npx 命令输出没有通过校验。");

            var summary = "npm 离线本地安装通过；npx 无下载执行本地 CLI 通过";
            File.WriteAllText(Path.Combine(folder, "npm-npx-result.txt"),
                summary + Environment.NewLine + DateTimeOffset.Now.ToString("O"),
                new UTF8Encoding(false));
            return summary;
        }
        finally
        {
            try { Directory.Delete(smoke, recursive: true); }
            catch (IOException ex) { AddLog("[npm/npx] 测试临时目录清理失败：" + ex.Message); }
            catch (UnauthorizedAccessException ex) { AddLog("[npm/npx] 测试临时目录清理失败：" + ex.Message); }
        }
    }

    private async Task<string> RunFfmpegExperimentAsync(string ffmpeg)
    {
        var folder = NewExperimentFolder("ffmpeg");
        var video = Path.Combine(folder, "synthetic-lab.mp4");
        var thumb = Path.Combine(folder, "thumbnail.png");

        var create = await RunProcessAsync(ffmpeg, new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=523.25:sample_rate=44100:duration=2",
            "-shortest", "-c:v", "mpeg4", "-q:v", "5", "-c:a", "aac", video
        }, folder);
        EnsureSuccess(create, "FFmpeg 合成");

        var capture = await RunProcessAsync(ffmpeg, new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-ss", "00:00:01.000", "-i", video, "-frames:v", "1", thumb
        }, folder);
        EnsureSuccess(capture, "FFmpeg 截帧");

        return $"从零合成 2 秒测试视频与 523.25Hz 音调，并截取缩略图：{folder}";
    }

    private async Task<string> RunGitExperimentAsync(string git)
    {
        var folder = NewExperimentFolder("git");
        var capsule = Path.Combine(folder, "capsule.txt");
        var recentArtifacts = Directory
            .EnumerateFiles(_experimentRoot, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(12)
            .Select(path => Path.GetRelativePath(_experimentRoot, path))
            .ToArray();

        File.WriteAllText(
            capsule,
            "燕子能力实验室 · 时间胶囊" + Environment.NewLine +
            "time=" + DateTimeOffset.Now.ToString("O") + Environment.NewLine +
            "artifacts=" + Environment.NewLine +
            string.Join(Environment.NewLine, recentArtifacts.Select(x => "  - " + x)),
            new UTF8Encoding(false));

        EnsureSuccess(await RunProcessAsync(git, new[] { "init", "-b", "capability-lab" }, folder), "git init");
        EnsureSuccess(await RunProcessAsync(git, new[] { "config", "user.name", "Yanzi Capability Lab" }, folder), "git config");
        EnsureSuccess(await RunProcessAsync(git, new[] { "config", "user.email", "capability-lab@local" }, folder), "git config");
        EnsureSuccess(await RunProcessAsync(git, new[] { "add", "." }, folder), "git add");
        EnsureSuccess(await RunProcessAsync(git, new[] { "commit", "-m", "能力实验室：生成时间胶囊" }, folder), "git commit");
        var log = await RunProcessAsync(git, new[] { "log", "-1", "--oneline", "--decorate" }, folder);
        EnsureSuccess(log, "git log");
        return $"临时仓库已提交：{log.StdOut.Trim()}；目录={folder}";
    }

    private string NewExperimentFolder(string name)
    {
        var path = Path.Combine(
            _experimentRoot,
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + name);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<ProcessRunResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        // npx launches child executables through PATH. Portable Node may not be on
        // the user's global PATH, so give only this child process the Node directory.
        var executableDirectory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(executableDirectory))
            process.StartInfo.Environment["PATH"] = executableDirectory + Path.PathSeparator +
                (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

        if (!process.Start())
            throw new InvalidOperationException("无法启动进程：" + executable);

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessRunResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static void EnsureSuccess(ProcessRunResult result, string label)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"{label} 退出码 {result.ExitCode}: {Compact(result.StdErr)}");
    }

    private void ApplyStatus(CapabilityCard card, JsonElement data, bool available)
    {
        var version = ReadString(data, "version");
        var path = ReadString(data, "path");
        var provider = ReadString(data, "provider");
        var error = ReadString(data, "error");

        card.Version.Text = "版本：" + (version ?? "—") + "  ·  Provider：" + (provider ?? "—");
        card.Path.Text = "路径：" + (path ?? "—");
        card.Path.ToolTip = path ?? "—";

        if (available)
        {
            SetBadgeStatus(card, "已就绪", "#123C2A", "#6EE7B7");
            card.Progress.Value = 100;
            card.Stage.Text = "满足 " + card.Spec.Requirement;
            card.Stage.Foreground = Brush("#6EE7B7");
        }
        else
        {
            SetBadgeStatus(card, "需准备", "#3B2C16", "#FBBF24");
            card.Progress.Value = 0;
            card.Stage.Text = string.IsNullOrWhiteSpace(error) ? "未满足版本要求" : error;
            card.Stage.Foreground = Brush("#FBBF24");
        }
    }

    private void SetCardStage(CapabilityCard card, string state, string message, int percent)
    {
        card.Progress.IsIndeterminate = state == "installing" && percent <= 0;
        if (!card.Progress.IsIndeterminate)
            card.Progress.Value = Math.Clamp(percent, 0, 100);
        card.Stage.Text = message;

        if (state == "failed")
        {
            SetBadgeStatus(card, "失败", "#3F1D24", "#FCA5A5");
            card.Stage.Foreground = Brush("#FCA5A5");
        }
        else if (state == "completed")
        {
            SetBadgeStatus(card, "已完成", "#123C2A", "#6EE7B7");
            card.Stage.Foreground = Brush("#6EE7B7");
        }
        else
        {
            SetBadgeStatus(card, state == "installing" ? "安装中" : state == "verifying" ? "验证中" : "检测中", "#172554", "#93C5FD");
            card.Stage.Foreground = Brush("#93C5FD");
        }
    }

    private static void SetBadgeStatus(CapabilityCard card, string label, string background, string foreground)
    {
        card.Badge.Content = label;
        card.Badge.Background = Brush(background);
        card.Badge.Foreground = Brush(foreground);
    }

    private void SetCardBusy(CapabilityCard card, bool busy)
    {
        card.Check.IsEnabled = !busy;
        card.Ensure.IsEnabled = !busy;
        card.Experiment.IsEnabled = !busy;
    }

    private void SetGlobalBusy(bool busy)
    {
        _refreshAllButton.IsEnabled = !busy;
        _runAllButton.IsEnabled = !busy;
    }

    private void AddLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AddLog(message));
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.ScrollToEnd();
        _context.Log(message);
    }

    private static void ConfigureToolStatus(TextBlock label, string tool)
    {
        label.Text = tool + "：未检查";
        label.FontSize = 11.5;
        label.Margin = new Thickness(0, 2, 0, 0);
        label.Foreground = Brush("#94A3B8");
        label.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    private static void SetToolStatus(TextBlock label, string tool, string result, bool ready)
    {
        label.Text = tool + "：" + result;
        label.ToolTip = label.Text;
        label.Foreground = Brush(ready ? "#6EE7B7" : "#FBBF24");
    }

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Content = text;
        button.Cursor = Cursors.Hand;
        YanziUi.WithStyle(button, primary
            ? YanziUi.Styles.PrimaryButton
            : YanziUi.Styles.SecondaryButton);
    }

    private static SolidColorBrush Brush(string hex)
        => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private static string? ReadString(JsonElement element, string name)
        => TryGet(element, name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static bool ReadBool(JsonElement element, string name)
        => TryGet(element, name, out var value) &&
           (value.ValueKind == JsonValueKind.True ||
            (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed));

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
            return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string Compact(string? value)
    {
        value = (value ?? string.Empty).Trim().Replace("\r", " ").Replace("\n", " ");
        return value.Length <= 220 ? value : value[..220] + "…";
    }

    private sealed record CapabilitySpec(
        string Name,
        string DisplayName,
        string Requirement,
        string Description);

    private sealed record ProcessRunResult(
        int ExitCode,
        string StdOut,
        string StdErr);

    private sealed class CapabilityCard
    {
        public CapabilitySpec Spec { get; }
        public Border Container { get; }
        public YanziBadge Badge { get; }
        public TextBlock Version { get; }
        public TextBlock Path { get; }
        public ProgressBar Progress { get; }
        public TextBlock Stage { get; }
        public Button Check { get; }
        public Button Ensure { get; }
        public Button Experiment { get; }

        public CapabilityCard(
            CapabilitySpec spec,
            Border container,
            YanziBadge badge,
            TextBlock version,
            TextBlock path,
            ProgressBar progress,
            TextBlock stage,
            Button check,
            Button ensure,
            Button experiment)
        {
            Spec = spec;
            Container = container;
            Badge = badge;
            Version = version;
            Path = path;
            Progress = progress;
            Stage = stage;
            Check = check;
            Ensure = ensure;
            Experiment = experiment;
        }
    }
}
