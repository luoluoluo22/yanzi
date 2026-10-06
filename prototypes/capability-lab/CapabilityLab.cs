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

    private static readonly CapabilitySpec[] Specs =
    {
        new("git", "Git", "git>=2.40", "版本控制 · 时间胶囊提交"),
        new("python", "Python", "python>=3.12", "纯标准库 · 曼德勃罗图"),
        new("node", "Node.js", "node>=22", "异步运行时 · 并行加密链"),
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
            Background = Brush("#101720"),
            BorderBrush = Brush("#1F2937"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14)
        };

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
            Background = Brush("#111821"),
            BorderBrush = Brush("#202B38"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16)
        };

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

        var badge = new Border
        {
            Background = Brush("#1F2937"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 4, 9, 4)
        };
        var badgeText = new TextBlock
        {
            Text = "未检查",
            FontSize = 11,
            Foreground = Brush("#94A3B8")
        };
        badge.Child = badgeText;
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
        var card = new CapabilityCard(spec, container, badge, badgeText, version, path, progress, stage, check, ensure, experiment);
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
        return $"完成 8 路并行 SHA-256 哈希链：{Compact(run.StdOut)}";
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
            card.Badge.Background = Brush("#123C2A");
            card.BadgeText.Foreground = Brush("#6EE7B7");
            card.BadgeText.Text = "已就绪";
            card.Progress.Value = 100;
            card.Stage.Text = "满足 " + card.Spec.Requirement;
            card.Stage.Foreground = Brush("#6EE7B7");
        }
        else
        {
            card.Badge.Background = Brush("#3B2C16");
            card.BadgeText.Foreground = Brush("#FBBF24");
            card.BadgeText.Text = "需准备";
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
            card.Badge.Background = Brush("#3F1D24");
            card.BadgeText.Foreground = Brush("#FCA5A5");
            card.BadgeText.Text = "失败";
            card.Stage.Foreground = Brush("#FCA5A5");
        }
        else if (state == "completed")
        {
            card.Badge.Background = Brush("#123C2A");
            card.BadgeText.Foreground = Brush("#6EE7B7");
            card.BadgeText.Text = "已完成";
            card.Stage.Foreground = Brush("#6EE7B7");
        }
        else
        {
            card.Badge.Background = Brush("#172554");
            card.BadgeText.Foreground = Brush("#93C5FD");
            card.BadgeText.Text = state == "installing" ? "安装中" : state == "verifying" ? "验证中" : "检测中";
            card.Stage.Foreground = Brush("#93C5FD");
        }
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

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Content = text;
        button.Padding = new Thickness(13, 7, 13, 7);
        button.Background = Brush(primary ? "#2563EB" : "#18212C");
        button.Foreground = Brush(primary ? "#FFFFFF" : "#CBD5E1");
        button.BorderBrush = Brush(primary ? "#3B82F6" : "#2A3645");
        button.BorderThickness = new Thickness(1);
        button.Cursor = Cursors.Hand;
        button.FontSize = 12;
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
        public Border Badge { get; }
        public TextBlock BadgeText { get; }
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
            Border badge,
            TextBlock badgeText,
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
            BadgeText = badgeText;
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
