using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Security;
using System.Security.Cryptography;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    private static JobAgentWindow? _currentWindow;

    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var input = context.InputText?.Trim() ?? string.Empty;

        if (string.Equals(input, "--resume", StringComparison.OrdinalIgnoreCase))
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var resume = new ResumeWindow(context, context.ExtensionDataDirectory, () =>
                {
                    JobRepository.RescoreJobs(context.ExtensionDataDirectory);
                });
                resume.Show();
                resume.Activate();
            });
            return "简历编辑器已打开";
        }

        if (input.StartsWith("--export-resume=", StringComparison.OrdinalIgnoreCase))
        {
            var path = input.Substring("--export-resume=".Length).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("导出路径为空。");
            var profile = JobRepository.LoadResume(context.ExtensionDataDirectory);
            ResumeDocument.ExportDocx(profile, path);
            return "附件简历已导出：" + path;
        }

        if (string.Equals(input, "--resume-read", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await ZhaopinResumeService.ReadAsync(
                context,
                context.ExtensionDataDirectory,
                CancellationToken.None).ConfigureAwait(false);
            return $"智联简历已读取：完整度 {snapshot.CompletionScore}，工作经历 {snapshot.WorkExperiences.Count} 条，项目 {snapshot.ProjectExperiences.Count} 条";
        }

        if (string.Equals(input, "--resume-diff", StringComparison.OrdinalIgnoreCase))
        {
            var local = JobRepository.LoadResume(context.ExtensionDataDirectory);
            var online = await ZhaopinResumeService.ReadAsync(
                context,
                context.ExtensionDataDirectory,
                CancellationToken.None).ConfigureAwait(false);
            var diff = ZhaopinResumeService.Compare(
                local,
                online,
                JobRepository.LoadSettings(context.ExtensionDataDirectory));
            return diff.ToText();
        }

        if (string.Equals(input, "--resume-sync", StringComparison.OrdinalIgnoreCase))
        {
            var local = JobRepository.LoadResume(context.ExtensionDataDirectory);
            var result = await ZhaopinResumeService.SyncSafeAsync(
                context,
                context.ExtensionDataDirectory,
                local,
                CancellationToken.None).ConfigureAwait(false);
            DailyJournalService.Capture(
                context.ExtensionDataDirectory,
                "同步智联简历：" + result.ToText());
            return result.ToText();
        }

        if (string.Equals(input, "--check-messages", StringComparison.OrdinalIgnoreCase))
        {
            var result = await ZhaopinInteractionService.CheckMessagesAsync(
                context,
                context.ExtensionDataDirectory,
                allowAutoReply: JobRepository.LoadSettings(context.ExtensionDataDirectory).AutoReplyEnabled,
                CancellationToken.None).ConfigureAwait(false);
            DailyJournalService.Capture(context.ExtensionDataDirectory, "手动检查消息：" + result.Message);
            return result.Message;
        }

        if (string.Equals(input, "--login", StringComparison.OrdinalIgnoreCase))
        {
            await ZhaopinInteractionService.OpenLoginAsync(context, CancellationToken.None).ConfigureAwait(false);
            return "已打开智联登录页";
        }

        if (input.StartsWith("--greet=", StringComparison.OrdinalIgnoreCase))
        {
            var jobId = input.Substring("--greet=".Length).Trim();
            var job = JobRepository.LoadJobs(context.ExtensionDataDirectory)
                .FirstOrDefault(x => string.Equals(x.Id, jobId, StringComparison.OrdinalIgnoreCase));
            if (job == null) return "没有找到对应岗位：" + jobId;

            var result = await ZhaopinInteractionService.GreetAsync(
                context,
                context.ExtensionDataDirectory,
                job,
                CancellationToken.None).ConfigureAwait(false);
            DailyJournalService.Capture(context.ExtensionDataDirectory, "打招呼：" + job.Title + " - " + result.Message);
            return result.Message;
        }

        if (input.StartsWith("--apply=", StringComparison.OrdinalIgnoreCase))
        {
            var jobId = input.Substring("--apply=".Length).Trim();
            var job = JobRepository.LoadJobs(context.ExtensionDataDirectory)
                .FirstOrDefault(x => string.Equals(x.Id, jobId, StringComparison.OrdinalIgnoreCase));
            if (job == null) return "没有找到对应岗位：" + jobId;

            var result = await ZhaopinInteractionService.ApplyAndGreetAsync(
                context,
                context.ExtensionDataDirectory,
                job,
                CancellationToken.None).ConfigureAwait(false);
            DailyJournalService.Capture(context.ExtensionDataDirectory, "投递：" + job.Title + " - " + result.Message);
            return result.Message;
        }

        var isBackground = string.Equals(context.LaunchSource, "idle", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(context.LaunchSource, "scheduler", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(input, "--scan", StringComparison.OrdinalIgnoreCase);

        if (isBackground)
        {
            try
            {
                var message = await JobBackgroundService.RunAsync(
                    context,
                    forceScan: string.Equals(input, "--scan", StringComparison.OrdinalIgnoreCase),
                    CancellationToken.None).ConfigureAwait(false);
                context.Log("求职后台循环：" + message);
                return message;
            }
            catch (Exception ex)
            {
                context.Log("求职后台循环失败：" + ex);
                try { context.ShowDesktopNotification("求职 - 后台循环失败", ex.Message); } catch { }
                return "后台循环失败：" + ex.Message;
            }
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_currentWindow != null && _currentWindow.IsLoaded)
            {
                if (_currentWindow.WindowState == WindowState.Minimized)
                    _currentWindow.WindowState = WindowState.Normal;
                _currentWindow.Show();
                _currentWindow.Activate();
                _currentWindow.Focus();
                return;
            }

            _currentWindow = new JobAgentWindow(context);
            _currentWindow.Closed += (_, _) => _currentWindow = null;
            _currentWindow.Show();
            _currentWindow.Activate();
        });

        return "求职已启动";
    }
}

public sealed class JobAgentWindow : Window
{
    private readonly YanziActionContext _context;
    private readonly string _dataDirectory;

    private readonly StackPanel _jobList = new();
    private readonly StackPanel _filterList = new();
    private readonly StackPanel _detailPanel = new();
    private readonly TextBlock _summaryText = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBox _searchBox = new();
    private readonly Button _scanButton;
    private readonly Button _resumeButton;
    private readonly Button _reviewButton;
    private readonly Button _messagesButton;
    private readonly Button _loginButton;
    private readonly TextBox _keywordsBox = new();
    private readonly TextBox _citiesBox = new();
    private readonly TextBox _salaryMinBox = new();
    private readonly TextBox _salaryMaxBox = new();
    private readonly TextBox _hardMaxBox = new();
    private readonly CheckBox _messageWatchCheck = new();
    private readonly CheckBox _autoReplyCheck = new();
    private readonly TextBox _dailyApplyLimitBox = new();
    private readonly TextBox _replyTemplateBox = new();

    private string _filter = "all";
    private string? _selectedId;
    private bool _refreshing;

    private static readonly Brush Bg = BrushFrom("#0B1016");
    private static readonly Brush PanelBg = BrushFrom("#121923");
    private static readonly Brush CardBg = BrushFrom("#17212D");
    private static readonly Brush CardSelected = BrushFrom("#1D3146");
    private static readonly Brush BorderBrush = BrushFrom("#263444");
    private static readonly Brush MainText = BrushFrom("#F4F7FB");
    private static readonly Brush Muted = BrushFrom("#94A3B8");
    private static readonly Brush Accent = BrushFrom("#60A5FA");
    private static readonly Brush Green = BrushFrom("#34D399");
    private static readonly Brush Amber = BrushFrom("#FBBF24");
    private static readonly Brush Red = BrushFrom("#FB7185");
    private static readonly Brush Violet = BrushFrom("#A78BFA");

    public JobAgentWindow(YanziActionContext context)
    {
        _context = context;
        _dataDirectory = context.ExtensionDataDirectory;
        Directory.CreateDirectory(_dataDirectory);

        Title = "求职";
        Width = 1320;
        Height = 820;
        MinWidth = 1050;
        MinHeight = 680;
        Background = Bg;
        Foreground = MainText;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _reviewButton = MakeButton("每日复盘");
        _reviewButton.Margin = new Thickness(0, 0, 8, 0);
        _reviewButton.Click += (_, _) => OpenDailyReview();

        _messagesButton = MakeButton("检查消息");
        _messagesButton.Margin = new Thickness(0, 0, 8, 0);
        _messagesButton.Click += async (_, _) => await CheckMessagesAsync();

        _loginButton = MakeButton("登录智联");
        _loginButton.Margin = new Thickness(0, 0, 8, 0);
        _loginButton.Click += async (_, _) => await OpenZhaopinLoginAsync();

        _resumeButton = MakeButton("我的简历");
        _resumeButton.Margin = new Thickness(0, 0, 8, 0);
        _resumeButton.Click += (_, _) => OpenResumeEditor();

        _scanButton = MakeButton("扫描智联", primary: true);
        _scanButton.Click += async (_, _) => await ScanAsync();

        Content = BuildUi();

        Loaded += (_, _) =>
        {
            LoadSettingsToUi();
            DailyJournalService.Capture(_dataDirectory, "打开求职小程序");
            Refresh();
        };
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader();
        root.Children.Add(header);

        var body = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        Grid.SetRow(body, 1);

        var left = BuildSidebar();
        left.Margin = new Thickness(0, 0, 12, 0);
        body.Children.Add(left);

        var center = BuildJobsPanel();
        center.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(center, 1);
        body.Children.Add(center);

        var right = BuildDetailPanel();
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        root.Children.Add(body);
        return root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = "求职",
            FontSize = 25,
            FontWeight = FontWeights.SemiBold,
            Foreground = MainText
        });
        left.Children.Add(new TextBlock
        {
            Text = "目标：传统企业 AI 转型 · 智联招聘 · 每天形成“发现 → 判断 → 投递 → 反馈 → 调整”闭环",
            FontSize = 12.5,
            Foreground = Muted,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _summaryText.FontSize = 12;
        _summaryText.Foreground = Accent;
        _summaryText.Margin = new Thickness(0, 7, 0, 0);
        left.Children.Add(_summaryText);
        grid.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _statusText.FontSize = 12;
        _statusText.Foreground = Muted;
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Margin = new Thickness(0, 0, 12, 0);
        right.Children.Add(_statusText);
        right.Children.Add(_reviewButton);
        right.Children.Add(_messagesButton);
        right.Children.Add(_loginButton);
        right.Children.Add(_resumeButton);
        right.Children.Add(_scanButton);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return grid;
    }

    private Border BuildSidebar()
    {
        var panel = Panel();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel();
        scroll.Content = root;
        panel.Child = scroll;

        root.Children.Add(Section("状态"));
        _filterList.Margin = new Thickness(0, 8, 0, 18);
        root.Children.Add(_filterList);

        root.Children.Add(Section("策略"));
        root.Children.Add(Label("搜索关键词"));
        StyleTextBox(_keywordsBox, 72);
        _keywordsBox.AcceptsReturn = true;
        _keywordsBox.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(_keywordsBox);

        root.Children.Add(Label("优先城市（只加权，全国仍扫描）"));
        StyleTextBox(_citiesBox);
        root.Children.Add(_citiesBox);

        var salaryGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        salaryGrid.ColumnDefinitions.Add(new ColumnDefinition());
        salaryGrid.ColumnDefinitions.Add(new ColumnDefinition());
        salaryGrid.ColumnDefinitions.Add(new ColumnDefinition());
        StyleTextBox(_salaryMinBox); StyleTextBox(_salaryMaxBox); StyleTextBox(_hardMaxBox);
        _salaryMinBox.ToolTip = "理想月薪下限";
        _salaryMaxBox.ToolTip = "理想月薪上限";
        _hardMaxBox.ToolTip = "超过此月薪会明显降权";
        _salaryMinBox.Margin = new Thickness(0, 0, 5, 0);
        _salaryMaxBox.Margin = new Thickness(0, 0, 5, 0);
        Grid.SetColumn(_salaryMaxBox, 1);
        Grid.SetColumn(_hardMaxBox, 2);
        salaryGrid.Children.Add(_salaryMinBox);
        salaryGrid.Children.Add(_salaryMaxBox);
        salaryGrid.Children.Add(_hardMaxBox);
        root.Children.Add(Label("理想下限 / 上限 / 高薪警戒"));
        root.Children.Add(salaryGrid);

        root.Children.Add(Section("互动", 18));

        _messageWatchCheck.Content = "后台检查 HR 消息";
        _messageWatchCheck.Foreground = MainText;
        _messageWatchCheck.FontSize = 11.5;
        _messageWatchCheck.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_messageWatchCheck);

        _autoReplyCheck.Content = "首次招呼自动回复";
        _autoReplyCheck.Foreground = MainText;
        _autoReplyCheck.FontSize = 11.5;
        _autoReplyCheck.Margin = new Thickness(0, 7, 0, 0);
        root.Children.Add(_autoReplyCheck);

        root.Children.Add(Label("每日投递上限"));
        StyleTextBox(_dailyApplyLimitBox);
        root.Children.Add(_dailyApplyLimitBox);

        root.Children.Add(Label("首次招呼回复模板"));
        StyleTextBox(_replyTemplateBox, 105);
        _replyTemplateBox.AcceptsReturn = true;
        _replyTemplateBox.TextWrapping = TextWrapping.Wrap;
        _replyTemplateBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.Children.Add(_replyTemplateBox);

        var save = MakeButton("保存策略");
        save.Margin = new Thickness(0, 10, 0, 0);
        save.Click += (_, _) => SaveSettingsFromUi();
        root.Children.Add(save);

        root.Children.Add(new TextBlock
        {
            Text = "投递和打招呼由你在岗位详情中触发，不做自动海投。消息监控会随闲置循环检查；自动回复只针对首次新招呼，并做重复指纹去重。",
            FontSize = 11.5,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });

        return panel;
    }

    private Border BuildJobsPanel()
    {
        var panel = Panel();
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Child = grid;

        StyleTextBox(_searchBox);
        _searchBox.ToolTip = "搜索职位、公司、城市、行业";
        _searchBox.TextChanged += (_, _) => Refresh();
        grid.Children.Add(_searchBox);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 12, 0, 0)
        };
        scroll.Content = _jobList;
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        return panel;
    }

    private Border BuildDetailPanel()
    {
        var panel = Panel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        scroll.Content = _detailPanel;
        panel.Child = scroll;
        return panel;
    }

    private async Task ScanAsync()
    {
        if (_refreshing) return;
        SaveSettingsFromUi(silent: true);
        _refreshing = true;
        _scanButton.IsEnabled = false;
        _scanButton.Content = "扫描中…";
        _statusText.Text = "正在抓取智联公开职位与详情";
        try
        {
            var result = await JobScanService.RunDailyAsync(_dataDirectory, force: true, CancellationToken.None);
            _statusText.Text = result.Message;
            DailyJournalService.AppendEvent(_dataDirectory, "scan", "", "", result.Message);
            DailyJournalService.Capture(_dataDirectory, "手动扫描：" + result.Message);
        }
        catch (Exception ex)
        {
            _statusText.Text = "扫描失败：" + ex.Message;
            try { _context.ShowDesktopNotification("求职 - 扫描失败", ex.Message); } catch { }
        }
        finally
        {
            _refreshing = false;
            _scanButton.IsEnabled = true;
            _scanButton.Content = "扫描智联";
            Refresh();
        }
    }

    private void LoadSettingsToUi()
    {
        var settings = JobRepository.LoadSettings(_dataDirectory);
        _keywordsBox.Text = string.Join(Environment.NewLine, settings.SearchKeywords);
        _citiesBox.Text = string.Join("、", settings.PreferredCities);
        _salaryMinBox.Text = settings.IdealSalaryMin.ToString(CultureInfo.InvariantCulture);
        _salaryMaxBox.Text = settings.IdealSalaryMax.ToString(CultureInfo.InvariantCulture);
        _hardMaxBox.Text = settings.HighSalaryWarning.ToString(CultureInfo.InvariantCulture);
        _messageWatchCheck.IsChecked = settings.EnableMessageWatch;
        _autoReplyCheck.IsChecked = settings.AutoReplyEnabled;
        _dailyApplyLimitBox.Text = settings.DailyApplyLimit.ToString(CultureInfo.InvariantCulture);
        _replyTemplateBox.Text = settings.ReplyTemplate;
    }

    private void SaveSettingsFromUi(bool silent = false)
    {
        var settings = JobRepository.LoadSettings(_dataDirectory);
        settings.SearchKeywords = SplitTerms(_keywordsBox.Text);
        if (settings.SearchKeywords.Count == 0)
            settings.SearchKeywords = JobAgentSettings.DefaultKeywords();
        settings.PreferredCities = SplitTerms(_citiesBox.Text);
        settings.IdealSalaryMin = ParseInt(_salaryMinBox.Text, 4500);
        settings.IdealSalaryMax = ParseInt(_salaryMaxBox.Text, 6500);
        settings.HighSalaryWarning = ParseInt(_hardMaxBox.Text, 10000);
        settings.EnableMessageWatch = _messageWatchCheck.IsChecked == true;
        settings.AutoReplyEnabled = _autoReplyCheck.IsChecked == true;
        settings.DailyApplyLimit = ParseInt(_dailyApplyLimitBox.Text, 5);
        settings.ReplyTemplate = (_replyTemplateBox.Text ?? string.Empty).Trim();
        if (settings.IdealSalaryMax < settings.IdealSalaryMin)
            (settings.IdealSalaryMin, settings.IdealSalaryMax) = (settings.IdealSalaryMax, settings.IdealSalaryMin);
        if (settings.HighSalaryWarning < settings.IdealSalaryMax)
            settings.HighSalaryWarning = settings.IdealSalaryMax;
        JobRepository.SaveSettings(_dataDirectory, settings);
        JobRepository.RescoreJobs(_dataDirectory);
        if (!silent)
        {
            DailyJournalService.AppendEvent(_dataDirectory, "settings", "", "", "更新求职策略并重新评分");
            DailyJournalService.Capture(_dataDirectory, "更新求职策略");
            _statusText.Text = "策略已保存，岗位已重新评分";
        }
    }

    private void OpenResumeEditor()
    {
        var win = new ResumeWindow(_context, _dataDirectory, () =>
        {
            JobRepository.RescoreJobs(_dataDirectory);
            LoadSettingsToUi();
            Refresh();
        })
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Refresh);
            return;
        }

        var jobs = JobRepository.LoadJobs(_dataDirectory);
        BuildFilters(jobs);

        var q = (_searchBox.Text ?? string.Empty).Trim();
        IEnumerable<JobRecord> filtered = jobs;

        if (_filter == "candidate")
            filtered = filtered.Where(x => x.OverallScore >= 68 && x.Status is "new" or "candidate");
        else if (_filter != "all")
            filtered = filtered.Where(x => string.Equals(x.Status, _filter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(x =>
                Contains(x.Title, q) || Contains(x.Company, q) || Contains(x.Location, q)
                || Contains(x.CompanyTags, q) || Contains(x.Description, q));
        }

        var list = filtered
            .OrderByDescending(x => PipelinePriority(x.Status))
            .ThenByDescending(x => x.OverallScore)
            .ThenByDescending(x => x.DiscoveredAt)
            .ToList();

        _jobList.Children.Clear();
        foreach (var job in list)
            _jobList.Children.Add(BuildJobCard(job));

        if (_selectedId == null || jobs.All(x => x.Id != _selectedId))
            _selectedId = list.FirstOrDefault()?.Id;

        BuildDetail(jobs.FirstOrDefault(x => x.Id == _selectedId));

        var today = DateTimeOffset.Now.Date;
        var newToday = jobs.Count(x => x.DiscoveredAt.LocalDateTime.Date == today);
        var appliedToday = jobs.Count(x => x.AppliedAt?.LocalDateTime.Date == today);
        var feedback = jobs.Count(x => x.Status is "viewed" or "contacted" or "interview" or "offer");
        var candidate = jobs.Count(x => x.OverallScore >= 68 && x.Status is "new" or "candidate");
        _summaryText.Text = $"岗位 {jobs.Count} · 今日发现 {newToday} · 待处理候选 {candidate} · 今日投递 {appliedToday} · 已有反馈 {feedback}";

        var state = JobRepository.LoadScanState(_dataDirectory);
        if (!_refreshing && state.LastScanAt.HasValue)
            _statusText.Text = "上次扫描 " + state.LastScanAt.Value.LocalDateTime.ToString("MM-dd HH:mm");
    }

    private void BuildFilters(List<JobRecord> jobs)
    {
        _filterList.Children.Clear();
        var specs = new[]
        {
            ("all", "全部"),
            ("candidate", "候选"),
            ("applied", "已投递"),
            ("viewed", "已查看"),
            ("contacted", "HR联系"),
            ("interview", "面试"),
            ("offer", "Offer"),
            ("no_response", "无回应"),
            ("rejected", "被拒"),
            ("skipped", "主动放弃")
        };

        foreach (var (key, label) in specs)
        {
            var count = key switch
            {
                "all" => jobs.Count,
                "candidate" => jobs.Count(x => x.OverallScore >= 68 && x.Status is "new" or "candidate"),
                _ => jobs.Count(x => string.Equals(x.Status, key, StringComparison.OrdinalIgnoreCase))
            };
            var b = MakeFilterButton($"{label}  {count}", key == _filter);
            b.Click += (_, _) => { _filter = key; Refresh(); };
            _filterList.Children.Add(b);
        }
    }

    private UIElement BuildJobCard(JobRecord job)
    {
        var selected = job.Id == _selectedId;
        var border = new Border
        {
            Background = selected ? CardSelected : CardBg,
            BorderBrush = selected ? Accent : BorderBrush,
            BorderThickness = new Thickness(selected ? 1.5 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13, 11, 13, 11),
            Margin = new Thickness(0, 0, 0, 9),
            Cursor = Cursors.Hand
        };

        var root = new StackPanel();
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(new TextBlock
        {
            Text = job.Title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = MainText,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var salary = new TextBlock
        {
            Text = job.Salary,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = SalaryBrush(job),
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(salary, 1);
        titleRow.Children.Add(salary);
        root.Children.Add(titleRow);

        root.Children.Add(new TextBlock
        {
            Text = $"{job.Company} · {job.Location} · {job.Experience} · {job.Education}",
            FontSize = 11.8,
            Foreground = Muted,
            Margin = new Thickness(0, 6, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var scoreRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        scoreRow.Children.Add(Chip($"推荐 {job.OverallScore}", ScoreBrush(job.OverallScore)));
        scoreRow.Children.Add(Chip($"简历 {job.ResumeMatchScore}", Accent));
        scoreRow.Children.Add(Chip($"经验 {job.ExperienceFitScore}", job.ExperienceFitScore >= 80 ? Green : job.ExperienceFitScore >= 55 ? Amber : Red));
        scoreRow.Children.Add(Chip($"转型 {job.TransitionScore}", Violet));
        scoreRow.Children.Add(Chip($"强度风险 {job.BossRisk}", job.BossRisk >= 60 ? Red : Muted));
        scoreRow.Children.Add(Chip(StatusLabel(job.Status), StatusBrush(job.Status)));
        root.Children.Add(scoreRow);

        if (!string.IsNullOrWhiteSpace(job.Recommendation))
        {
            root.Children.Add(new TextBlock
            {
                Text = job.Recommendation,
                FontSize = 11.5,
                Foreground = job.OverallScore >= 68 ? Green : Muted,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        border.Child = root;
        border.MouseLeftButtonUp += (_, _) =>
        {
            _selectedId = job.Id;
            Refresh();
        };
        return border;
    }

    private void BuildDetail(JobRecord? job)
    {
        _detailPanel.Children.Clear();
        if (job == null)
        {
            _detailPanel.Children.Add(new TextBlock
            {
                Text = "扫描后从左侧选择职位，这里会显示反向评估与反馈操作。",
                Foreground = Muted,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        _detailPanel.Children.Add(new TextBlock
        {
            Text = job.Title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = MainText,
            TextWrapping = TextWrapping.Wrap
        });
        _detailPanel.Children.Add(new TextBlock
        {
            Text = $"{job.Company}\n{job.Salary} · {job.Location}",
            FontSize = 12.5,
            Foreground = Muted,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var score = new Grid { Margin = new Thickness(0, 15, 0, 0) };
        for (int i = 0; i < 2; i++) score.ColumnDefinitions.Add(new ColumnDefinition());
        score.RowDefinitions.Add(new RowDefinition());
        score.RowDefinitions.Add(new RowDefinition());
        score.RowDefinitions.Add(new RowDefinition());
        score.RowDefinitions.Add(new RowDefinition());
        AddScoreCell(score, 0, 0, "综合推荐", job.OverallScore, ScoreBrush(job.OverallScore));
        AddScoreCell(score, 0, 1, "传统转型", job.TransitionScore, Violet);
        AddScoreCell(score, 1, 0, "简历匹配", job.ResumeMatchScore, Accent);
        AddScoreCell(score, 1, 1, "经验匹配", job.ExperienceFitScore, job.ExperienceFitScore >= 80 ? Green : job.ExperienceFitScore >= 55 ? Amber : Red);
        AddScoreCell(score, 2, 0, "地点成本", job.LocationScore, job.LocationScore >= 90 ? Green : Muted);
        AddScoreCell(score, 2, 1, "岗位匹配", job.RoleFitScore, Accent);
        AddScoreCell(score, 3, 0, "薪资舒适", job.SalaryComfortScore, job.SalaryComfortScore >= 80 ? Green : Amber);
        AddScoreCell(score, 3, 1, "Boss/强度风险", job.BossRisk, job.BossRisk >= 60 ? Red : Amber);
        _detailPanel.Children.Add(score);

        _detailPanel.Children.Add(Section("为什么这样判断", 18));
        _detailPanel.Children.Add(InfoText(job.ScoreReason));
        if (!string.IsNullOrWhiteSpace(job.BossRiskReason))
        {
            _detailPanel.Children.Add(new TextBlock
            {
                Text = "风险：" + job.BossRiskReason,
                Foreground = job.BossRisk >= 60 ? Red : Amber,
                FontSize = 11.8,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0)
            });
        }

        _detailPanel.Children.Add(Section("推进", 18));

        var actionWrap = new WrapPanel();
        var apply = MakeButton("投递并打招呼", primary: true);
        apply.Margin = new Thickness(0, 0, 7, 7);
        apply.Click += async (_, _) => await ApplyJobAsync(job);
        actionWrap.Children.Add(apply);

        var greet = MakeButton("只打招呼");
        greet.Margin = new Thickness(0, 0, 7, 7);
        greet.Click += async (_, _) => await GreetJobAsync(job);
        actionWrap.Children.Add(greet);

        var openMessages = MakeButton("打开消息");
        openMessages.Margin = new Thickness(0, 0, 7, 7);
        openMessages.Click += async (_, _) => await OpenMessagesAsync();
        actionWrap.Children.Add(openMessages);

        var open = MakeButton("打开职位");
        open.Margin = new Thickness(0, 0, 7, 7);
        open.Click += (_, _) => OpenUrl(job.Url);
        actionWrap.Children.Add(open);
        _detailPanel.Children.Add(actionWrap);

        if (!string.IsNullOrWhiteSpace(job.LastInteractionMessage))
        {
            _detailPanel.Children.Add(new TextBlock
            {
                Text = "最近互动：" + job.LastInteractionMessage,
                Foreground = job.InteractionState == "error" ? Red : Muted,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 2)
            });
        }

        var statusWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (key, text) in new[]
        {
            ("applied","已投递"), ("viewed","已查看"), ("contacted","HR联系"),
            ("interview","面试"), ("offer","Offer"), ("no_response","无回应"),
            ("rejected","被拒"), ("skipped","主动放弃")
        })
        {
            var b = MakeButton(text);
            b.Margin = new Thickness(0, 0, 7, 7);
            if (job.Status == key)
            {
                b.BorderBrush = StatusBrush(key);
                b.BorderThickness = new Thickness(1.5);
            }
            b.Click += (_, _) => SetStatus(job.Id, key);
            statusWrap.Children.Add(b);
        }
        _detailPanel.Children.Add(statusWrap);

        _detailPanel.Children.Add(Section("反馈记录", 15));
        var note = new TextBox
        {
            Text = job.UserNote ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 78,
            Background = BrushFrom("#0D141D"),
            Foreground = MainText,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9),
            FontSize = 12
        };
        _detailPanel.Children.Add(note);
        var saveNote = MakeButton("保存反馈");
        saveNote.Margin = new Thickness(0, 7, 0, 0);
        saveNote.Click += (_, _) =>
        {
            var current = JobRepository.LoadJobs(_dataDirectory);
            var target = current.FirstOrDefault(x => x.Id == job.Id);
            if (target != null)
            {
                target.UserNote = note.Text.Trim();
                target.UpdatedAt = DateTimeOffset.Now;
                JobRepository.SaveJobs(_dataDirectory, current);
                _statusText.Text = "反馈已保存";
            }
        };
        _detailPanel.Children.Add(saveNote);

        _detailPanel.Children.Add(Section("公司与岗位", 18));
        _detailPanel.Children.Add(InfoText("公司标签：" + Empty(job.CompanyTags)));
        _detailPanel.Children.Add(InfoText("技能标签：" + Empty(job.SkillTags)));
        _detailPanel.Children.Add(InfoText("招聘者：" + Empty(job.RecruiterState)));

        if (!string.IsNullOrWhiteSpace(job.CompanyIntro))
        {
            _detailPanel.Children.Add(Section("公司介绍", 16));
            _detailPanel.Children.Add(InfoText(job.CompanyIntro));
        }
        if (!string.IsNullOrWhiteSpace(job.Description))
        {
            _detailPanel.Children.Add(Section("职位描述", 16));
            _detailPanel.Children.Add(InfoText(job.Description));
        }
    }

    private void SetStatus(string id, string status)
    {
        var jobs = JobRepository.LoadJobs(_dataDirectory);
        var job = jobs.FirstOrDefault(x => x.Id == id);
        if (job == null) return;
        job.Status = status;
        job.UpdatedAt = DateTimeOffset.Now;
        if (status == "applied" && !job.AppliedAt.HasValue) job.AppliedAt = DateTimeOffset.Now;
        if (status is "viewed" or "contacted" or "interview" or "offer" or "rejected")
            job.LastFeedbackAt = DateTimeOffset.Now;
        JobRepository.SaveJobs(_dataDirectory, jobs);
        DailyJournalService.AppendEvent(_dataDirectory, "status", job.Id, job.Title, "状态改为：" + StatusLabel(status));
        DailyJournalService.Capture(_dataDirectory, "状态更新：" + job.Title + " → " + StatusLabel(status));
        Refresh();
    }

    private async Task ApplyJobAsync(JobRecord job)
    {
        SaveSettingsFromUi(silent: true);
        _statusText.Text = "正在投递并发送招呼…";
        try
        {
            var result = await ZhaopinInteractionService.ApplyAndGreetAsync(
                _context, _dataDirectory, job, CancellationToken.None);
            _statusText.Text = result.Message;
            DailyJournalService.Capture(_dataDirectory, "投递：" + job.Title + " - " + result.Message);
        }
        catch (Exception ex)
        {
            _statusText.Text = "投递失败：" + ex.Message;
            try { _context.ShowDesktopNotification("求职 - 投递失败", ex.Message); } catch { }
        }
        Refresh();
    }

    private async Task GreetJobAsync(JobRecord job)
    {
        SaveSettingsFromUi(silent: true);
        _statusText.Text = "正在发起沟通…";
        try
        {
            var result = await ZhaopinInteractionService.GreetAsync(
                _context, _dataDirectory, job, CancellationToken.None);
            _statusText.Text = result.Message;
            DailyJournalService.Capture(_dataDirectory, "打招呼：" + job.Title + " - " + result.Message);
        }
        catch (Exception ex)
        {
            _statusText.Text = "沟通失败：" + ex.Message;
            try { _context.ShowDesktopNotification("求职 - 沟通失败", ex.Message); } catch { }
        }
        Refresh();
    }

    private async Task CheckMessagesAsync()
    {
        SaveSettingsFromUi(silent: true);
        _messagesButton.IsEnabled = false;
        _messagesButton.Content = "检查中…";
        try
        {
            var result = await ZhaopinInteractionService.CheckMessagesAsync(
                _context,
                _dataDirectory,
                JobRepository.LoadSettings(_dataDirectory).AutoReplyEnabled,
                CancellationToken.None);
            _statusText.Text = result.Message;
            DailyJournalService.Capture(_dataDirectory, "检查消息：" + result.Message);
        }
        catch (Exception ex)
        {
            _statusText.Text = "消息检查失败：" + ex.Message;
        }
        finally
        {
            _messagesButton.IsEnabled = true;
            _messagesButton.Content = "检查消息";
            Refresh();
        }
    }

    private async Task OpenZhaopinLoginAsync()
    {
        try
        {
            await ZhaopinInteractionService.OpenLoginAsync(_context, CancellationToken.None);
            _statusText.Text = "已打开智联登录页，登录完成后再点“检查消息”";
        }
        catch (Exception ex)
        {
            _statusText.Text = "打开登录页失败：" + ex.Message;
        }
    }

    private async Task OpenMessagesAsync()
    {
        try
        {
            await ZhaopinInteractionService.OpenMessagesAsync(_context, CancellationToken.None);
            _statusText.Text = "已打开智联消息页";
        }
        catch (Exception ex)
        {
            _statusText.Text = "打开消息页失败：" + ex.Message;
        }
    }

    private void OpenDailyReview()
    {
        DailyJournalService.Capture(_dataDirectory, "打开每日复盘");
        var win = new DailyReviewWindow(_dataDirectory) { Owner = this };
        win.ShowDialog();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void AddScoreCell(Grid grid, int row, int col, string title, int value, Brush color)
    {
        var border = new Border
        {
            Background = BrushFrom("#0E1620"),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(col == 0 ? 0 : 5, row == 0 ? 0 : 5, col == 0 ? 5 : 0, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 10.8, Foreground = Muted });
        stack.Children.Add(new TextBlock { Text = value.ToString(), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = color, Margin = new Thickness(0, 2, 0, 0) });
        border.Child = stack;
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        grid.Children.Add(border);
    }

    private static Border Chip(string text, Brush color)
    {
        return new Border
        {
            Background = BrushFrom("#0E1620"),
            BorderBrush = color,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock { Text = text, FontSize = 10.5, Foreground = color }
        };
    }

    private static TextBlock Section(string text, double top = 0) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = MainText,
        Margin = new Thickness(0, top, 0, 0)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 10.8,
        Foreground = Muted,
        Margin = new Thickness(0, 8, 0, 4)
    };

    private static TextBlock InfoText(string text) => new()
    {
        Text = text,
        FontSize = 11.7,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0)
    };

    private static Border Panel() => new()
    {
        Background = PanelBg,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14)
    };

    private static Button MakeButton(string text, bool primary = false)
    {
        return new Button
        {
            Content = text,
            Padding = new Thickness(12, 7, 12, 7),
            Background = primary ? Accent : BrushFrom("#1A2633"),
            Foreground = primary ? BrushFrom("#06111C") : MainText,
            BorderBrush = primary ? Accent : BorderBrush,
            BorderThickness = new Thickness(1),
            FontSize = 11.8,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static Button MakeFilterButton(string text, bool active)
    {
        return new Button
        {
            Content = text,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Background = active ? BrushFrom("#1D3146") : Brushes.Transparent,
            Foreground = active ? Accent : Muted,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontSize = 11.8
        };
    }

    private static void StyleTextBox(TextBox box, double? height = null)
    {
        box.Background = BrushFrom("#0D141D");
        box.Foreground = MainText;
        box.BorderBrush = BorderBrush;
        box.BorderThickness = new Thickness(1);
        box.Padding = new Thickness(8, 6, 8, 6);
        box.FontSize = 11.8;
        if (height.HasValue) box.Height = height.Value;
    }

    private static Brush SalaryBrush(JobRecord job) =>
        job.SalaryComfortScore >= 80 ? Green : job.SalaryComfortScore >= 50 ? Amber : Red;

    private static Brush ScoreBrush(int score) => score >= 78 ? Green : score >= 65 ? Accent : score >= 50 ? Amber : Muted;

    private static Brush StatusBrush(string status) => status switch
    {
        "offer" => Green,
        "interview" => Violet,
        "contacted" => Accent,
        "viewed" => Accent,
        "applied" => Amber,
        "rejected" => Red,
        "no_response" => Muted,
        "skipped" => Muted,
        _ => Muted
    };

    private static string StatusLabel(string status) => status switch
    {
        "new" => "新发现",
        "candidate" => "候选",
        "applied" => "已投递",
        "viewed" => "已查看",
        "contacted" => "HR联系",
        "interview" => "面试",
        "offer" => "Offer",
        "no_response" => "无回应",
        "rejected" => "被拒",
        "skipped" => "放弃",
        _ => status
    };

    private static int PipelinePriority(string status) => status switch
    {
        "offer" => 10,
        "interview" => 9,
        "contacted" => 8,
        "viewed" => 7,
        "applied" => 6,
        "candidate" => 5,
        "new" => 4,
        "no_response" => 2,
        "rejected" => 1,
        "skipped" => 0,
        _ => 3
    };

    private static bool Contains(string? source, string query) =>
        !string.IsNullOrWhiteSpace(source) && source.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitTerms(string text) =>
        Regex.Split(text ?? string.Empty, @"[\r\n,，、;；]+")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Empty(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text;

    private static SolidColorBrush BrushFrom(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }
}

public sealed class ResumeWindow : Window
{
    private readonly YanziActionContext _context;
    private readonly string _dataDirectory;
    private readonly Action _onSaved;

    private readonly TextBox _name = Box();
    private readonly TextBox _years = Box();
    private readonly TextBox _educationLevel = Box();
    private readonly TextBox _employmentStatus = Box();
    private readonly TextBox _phone = Box();
    private readonly TextBox _email = Box();
    private readonly TextBox _city = Box();
    private readonly TextBox _province = Box();
    private readonly TextBox _headline = Box();
    private readonly TextBox _advantage = MultiBox(120);
    private readonly TextBox _expected = MultiBox(90);
    private readonly TextBox _work = MultiBox(260);
    private readonly TextBox _projects = MultiBox(150);
    private readonly TextBox _education = MultiBox(90);
    private readonly TextBox _skills = MultiBox(90);
    private readonly TextBox _portfolio = MultiBox(100);
    private readonly TextBox _certificates = MultiBox(80);
    private readonly TextBox _volunteer = MultiBox(80);
    private readonly TextBox _rawImported = MultiBox(100);
    private readonly TextBlock _status = new();

    private static readonly Brush Bg = BrushFrom("#0B1016");
    private static readonly Brush Panel = BrushFrom("#121923");
    private static readonly Brush Input = BrushFrom("#0D141D");
    private static readonly Brush Border = BrushFrom("#263444");
    private static readonly Brush Text = BrushFrom("#F4F7FB");
    private static readonly Brush Muted = BrushFrom("#94A3B8");
    private static readonly Brush Accent = BrushFrom("#60A5FA");

    public ResumeWindow(YanziActionContext context, string dataDirectory, Action onSaved)
    {
        _context = context;
        _dataDirectory = dataDirectory;
        _onSaved = onSaved;

        Title = "我的简历";
        Width = 980;
        Height = 860;
        MinWidth = 760;
        MinHeight = 620;
        Background = Bg;
        Foreground = Text;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI");

        Content = BuildUi();
        Loaded += (_, _) => Fill(JobRepository.LoadResume(_dataDirectory));
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "我的简历",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Text
        });
        heading.Children.Add(new TextBlock
        {
            Text = "长期基础资料 · 本地保存 · 用于岗位匹配和后续投递，可随时更新",
            FontSize = 12,
            Foreground = Muted,
            Margin = new Thickness(0, 4, 0, 0)
        });
        _status.FontSize = 11.5;
        _status.Foreground = Accent;
        _status.Margin = new Thickness(0, 6, 0, 0);
        heading.Children.Add(_status);
        header.Children.Add(heading);

        var buttons = new WrapPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        var readOnline = Button("读取智联");
        readOnline.Click += async (_, _) => await ReadZhaopinResumeAsync();
        buttons.Children.Add(readOnline);

        var compareOnline = Button("对比智联");
        compareOnline.Margin = new Thickness(7, 0, 0, 0);
        compareOnline.Click += async (_, _) => await CompareZhaopinResumeAsync();
        buttons.Children.Add(compareOnline);

        var syncOnline = Button("同步智联");
        syncOnline.Margin = new Thickness(7, 0, 0, 0);
        syncOnline.Click += async (_, _) => await SyncZhaopinResumeAsync();
        buttons.Children.Add(syncOnline);

        var manageOnline = Button("管理智联");
        manageOnline.Margin = new Thickness(7, 0, 0, 0);
        manageOnline.Click += (_, _) => OpenZhaopinManager();
        buttons.Children.Add(manageOnline);

        var import = Button("导入已有简历");
        import.Margin = new Thickness(7, 0, 0, 0);
        import.Click += (_, _) => ImportResume();
        buttons.Children.Add(import);

        var preview = Button("预览");
        preview.Margin = new Thickness(7, 0, 0, 0);
        preview.Click += (_, _) => Preview();
        buttons.Children.Add(preview);

        var export = Button("导出附件简历");
        export.Margin = new Thickness(7, 0, 0, 0);
        export.Click += (_, _) => ExportResume();
        buttons.Children.Add(export);

        var save = Button("保存", true);
        save.Margin = new Thickness(7, 0, 0, 0);
        save.Click += (_, _) => Save();
        buttons.Children.Add(save);

        Grid.SetColumn(buttons, 1);
        header.Children.Add(buttons);
        root.Children.Add(header);

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroller, 1);

        var form = new StackPanel();
        form.Children.Add(Pair("姓名", _name, "当前城市", _city));
        form.Children.Add(Pair("工作经验", _years, "省份", _province));
        form.Children.Add(Pair("学历", _educationLevel, "求职状态", _employmentStatus));
        form.Children.Add(Pair("手机", _phone, "邮箱", _email));
        form.Children.Add(Field("职业定位", _headline));
        form.Children.Add(Field("个人优势", _advantage));
        form.Children.Add(Field("期望职位", _expected));
        form.Children.Add(Field("工作经历", _work));
        form.Children.Add(Field("项目经历", _projects));
        form.Children.Add(Field("教育经历", _education));
        form.Children.Add(Field("技能关键词", _skills));
        form.Children.Add(Field("作品 / 主页", _portfolio));
        form.Children.Add(Field("资格证书", _certificates));
        form.Children.Add(Field("志愿者经历", _volunteer));

        var raw = Field("导入原文（导入文件无法完整结构化时保留在这里，不会丢失）", _rawImported);
        raw.Margin = new Thickness(0, 0, 0, 15);
        form.Children.Add(raw);

        scroller.Content = form;
        root.Children.Add(scroller);
        return root;
    }

    private void Fill(ResumeProfile profile)
    {
        profile.Normalize();
        _name.Text = profile.Name;
        _years.Text = profile.YearsExperience;
        _educationLevel.Text = profile.EducationLevel;
        _employmentStatus.Text = profile.EmploymentStatus;
        _phone.Text = profile.Phone;
        _email.Text = profile.Email;
        _city.Text = profile.CurrentCity;
        _province.Text = profile.CurrentProvince;
        _headline.Text = profile.Headline;
        _advantage.Text = profile.PersonalAdvantage;
        _expected.Text = profile.ExpectedPosition;
        _work.Text = profile.WorkExperience;
        _projects.Text = profile.ProjectExperience;
        _education.Text = profile.Education;
        _skills.Text = profile.Skills;
        _portfolio.Text = profile.Portfolio;
        _certificates.Text = profile.Certificates;
        _volunteer.Text = profile.Volunteer;
        _rawImported.Text = profile.RawImportedText;
    }

    private ResumeProfile ReadForm()
    {
        return new ResumeProfile
        {
            Name = _name.Text.Trim(),
            YearsExperience = _years.Text.Trim(),
            EducationLevel = _educationLevel.Text.Trim(),
            EmploymentStatus = _employmentStatus.Text.Trim(),
            Phone = _phone.Text.Trim(),
            Email = _email.Text.Trim(),
            CurrentCity = _city.Text.Trim(),
            CurrentProvince = _province.Text.Trim(),
            Headline = _headline.Text.Trim(),
            PersonalAdvantage = _advantage.Text.Trim(),
            ExpectedPosition = _expected.Text.Trim(),
            WorkExperience = _work.Text.Trim(),
            ProjectExperience = _projects.Text.Trim(),
            Education = _education.Text.Trim(),
            Skills = _skills.Text.Trim(),
            Portfolio = _portfolio.Text.Trim(),
            Certificates = _certificates.Text.Trim(),
            Volunteer = _volunteer.Text.Trim(),
            RawImportedText = _rawImported.Text.Trim(),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private void Save()
    {
        try
        {
            var profile = ReadForm();
            JobRepository.SaveResume(_dataDirectory, profile);
            ZhaopinResumeService.MarkLocalChanged(_dataDirectory, profile.UpdatedAt);

            var settings = JobRepository.LoadSettings(_dataDirectory);
            if (!string.IsNullOrWhiteSpace(profile.CurrentCity)
                && !settings.PreferredCities.Contains(profile.CurrentCity, StringComparer.OrdinalIgnoreCase))
            {
                settings.PreferredCities.Insert(0, profile.CurrentCity);
                JobRepository.SaveSettings(_dataDirectory, settings);
            }

            _onSaved();
            _status.Text = "已保存，并按最新简历重新计算岗位匹配";
        }
        catch (Exception ex)
        {
            _status.Text = "保存失败：" + ex.Message;
        }
    }

    private async Task ReadZhaopinResumeAsync()
    {
        try
        {
            _status.Text = "正在读取智联在线简历…";
            var snapshot = await ZhaopinResumeService.ReadAsync(
                _context,
                _dataDirectory,
                CancellationToken.None);
            _status.Text = $"智联简历已读取：完整度 {snapshot.CompletionScore}，工作经历 {snapshot.WorkExperiences.Count} 条，项目 {snapshot.ProjectExperiences.Count} 条";
        }
        catch (Exception ex)
        {
            _status.Text = "读取智联失败：" + ex.Message;
        }
    }

    private async Task CompareZhaopinResumeAsync()
    {
        try
        {
            var local = ReadForm();
            _status.Text = "正在对比本地与智联简历…";
            var snapshot = await ZhaopinResumeService.ReadAsync(
                _context,
                _dataDirectory,
                CancellationToken.None);
            var diff = ZhaopinResumeService.Compare(local, snapshot);
            ShowTextDialog("智联简历差异", diff.ToText());
            _status.Text = diff.NeedsSync
                ? $"发现 {diff.Items.Count} 项差异，投递前建议先同步"
                : "智联简历与本地主简历关键字段一致";
        }
        catch (Exception ex)
        {
            _status.Text = "对比失败：" + ex.Message;
        }
    }

    private async Task SyncZhaopinResumeAsync()
    {
        try
        {
            var local = ReadForm();
            JobRepository.SaveResume(_dataDirectory, local);
            _status.Text = "正在同步可安全确认的智联简历字段…";

            var result = await ZhaopinResumeService.SyncSafeAsync(
                _context,
                _dataDirectory,
                local,
                CancellationToken.None);

            _onSaved();
            ShowTextDialog("智联简历同步结果", result.ToText());
            _status.Text = result.RemainingDiffCount == 0
                ? "智联简历关键字段已同步，可进入投递流程"
                : $"已同步确定字段，仍有 {result.RemainingDiffCount} 项需要确认";
        }
        catch (Exception ex)
        {
            _status.Text = "同步智联失败：" + ex.Message;
        }
    }

    private void ShowTextDialog(string title, string content)
    {
        var box = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Input,
            Foreground = Text,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18),
            FontSize = 12.5
        };

        var win = new Window
        {
            Owner = this,
            Title = title,
            Width = 760,
            Height = 680,
            MinWidth = 580,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg,
            Content = box,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        win.ShowDialog();
    }

    private void OpenZhaopinManager()
    {
        var win = new ZhaopinResumeManagerWindow(_context, _dataDirectory)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    private void Preview()
    {
        var profile = ReadForm();
        var box = new TextBox
        {
            Text = profile.ToPlainText(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Input,
            Foreground = Text,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(20),
            FontSize = 13.2
        };

        var win = new Window
        {
            Owner = this,
            Title = "简历预览",
            Width = 760,
            Height = 820,
            MinWidth = 600,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg,
            Content = box,
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };
        win.ShowDialog();
    }

    private void ImportResume()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "导入已有简历",
                Filter = "支持的简历|*.docx;*.txt;*.md;*.json|Word 简历|*.docx|文本简历|*.txt;*.md|求职小程序简历|*.json"
            };
            if (dialog.ShowDialog(this) != true) return;

            ResumeProfile profile;
            if (string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                profile = JsonSerializer.Deserialize<ResumeProfile>(
                              File.ReadAllText(dialog.FileName),
                              new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new ResumeProfile();
            }
            else
            {
                var text = ResumeDocument.ReadText(dialog.FileName);
                profile = ResumeTextParser.Merge(ReadForm(), text);
            }

            profile.Normalize();
            Fill(profile);
            _status.Text = "已导入，确认内容后点击“保存”";
        }
        catch (Exception ex)
        {
            _status.Text = "导入失败：" + ex.Message;
        }
    }

    private void ExportResume()
    {
        try
        {
            var profile = ReadForm();
            var safeName = string.IsNullOrWhiteSpace(profile.Name) ? "附件简历" : profile.Name + "-附件简历";
            var dialog = new SaveFileDialog
            {
                Title = "导出附件简历",
                FileName = safeName + ".docx",
                Filter = "Word 附件简历|*.docx|纯文本简历|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            var ext = Path.GetExtension(dialog.FileName);
            if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(dialog.FileName, profile.ToPlainText(), new UTF8Encoding(false));
            else
                ResumeDocument.ExportDocx(profile, dialog.FileName);

            JobRepository.SaveResume(_dataDirectory, profile);
            _onSaved();
            _status.Text = "已导出：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            _status.Text = "导出失败：" + ex.Message;
        }
    }

    private static Border Pair(string leftLabel, TextBox left, string rightLabel, TextBox right)
    {
        var border = Card();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var a = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        a.Children.Add(Label(leftLabel));
        a.Children.Add(left);
        grid.Children.Add(a);

        var b = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        b.Children.Add(Label(rightLabel));
        b.Children.Add(right);
        Grid.SetColumn(b, 1);
        grid.Children.Add(b);

        border.Child = grid;
        return border;
    }

    private static Border Field(string label, TextBox box)
    {
        var border = Card();
        var stack = new StackPanel();
        stack.Children.Add(Label(label));
        stack.Children.Add(box);
        border.Child = stack;
        return border;
    }

    private static Border Card() => new()
    {
        Background = Panel,
        BorderBrush = Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(13),
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Muted,
        FontSize = 11,
        Margin = new Thickness(0, 0, 0, 5)
    };

    private static TextBox Box() => new()
    {
        Background = Input,
        Foreground = Text,
        BorderBrush = Border,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(9, 6, 9, 6),
        FontSize = 12.5
    };

    private static TextBox MultiBox(double height)
    {
        var box = Box();
        box.Height = height;
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        return box;
    }

    private static Button Button(string text, bool primary = false) => new()
    {
        Content = text,
        Padding = new Thickness(12, 7, 12, 7),
        Background = primary ? Accent : BrushFrom("#1A2633"),
        Foreground = primary ? BrushFrom("#06111C") : Text,
        BorderBrush = primary ? Accent : Border,
        BorderThickness = new Thickness(1),
        FontSize = 11.5,
        Cursor = Cursors.Hand
    };

    private static SolidColorBrush BrushFrom(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}


public sealed class ZhaopinResumeManagerWindow : Window
{
    private readonly YanziActionContext _context;
    private readonly string _dataDirectory;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new();
    private ZhaopinResumeSnapshot? _snapshot;

    private static readonly Brush Bg = BrushFrom("#0B1016");
    private static readonly Brush Panel = BrushFrom("#121923");
    private static readonly Brush Border = BrushFrom("#263444");
    private static readonly Brush Text = BrushFrom("#F4F7FB");
    private static readonly Brush Muted = BrushFrom("#94A3B8");
    private static readonly Brush Accent = BrushFrom("#60A5FA");

    public ZhaopinResumeManagerWindow(YanziActionContext context, string dataDirectory)
    {
        _context = context;
        _dataDirectory = dataDirectory;

        Title = "管理智联在线简历";
        Width = 860;
        Height = 760;
        MinWidth = 680;
        MinHeight = 500;
        Background = Bg;
        Foreground = Text;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildUi();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "智联在线简历",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = Text
        });
        title.Children.Add(new TextBlock
        {
            Text = "读取 / 新增 / 修改 / 删除都复用当前浏览器登录态；删除只影响智联线上，不会删除本地主简历。",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Muted,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        });
        _status.Margin = new Thickness(0, 6, 0, 0);
        _status.Foreground = Accent;
        _status.FontSize = 11.5;
        title.Children.Add(_status);
        top.Children.Add(title);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var reload = ActionButton("刷新");
        reload.Click += async (_, _) => await ReloadAsync();
        buttons.Children.Add(reload);

        var sync = ActionButton("同步本地主简历", true);
        sync.Margin = new Thickness(7, 0, 0, 0);
        sync.Click += async (_, _) => await SyncAsync();
        buttons.Children.Add(sync);

        Grid.SetColumn(buttons, 1);
        top.Children.Add(buttons);
        root.Children.Add(top);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        return root;
    }

    private async Task ReloadAsync()
    {
        try
        {
            _status.Text = "正在读取智联…";
            _snapshot = await ZhaopinResumeService.ReadAsync(
                _context,
                _dataDirectory,
                CancellationToken.None).ConfigureAwait(true);
            Render();
            _status.Text =
                $"完整度 {_snapshot.CompletionScore} · 工作 {_snapshot.WorkExperiences.Count} · 项目 {_snapshot.ProjectExperiences.Count} · 技能 {_snapshot.ProfessionalSkills.Count}";
        }
        catch (Exception ex)
        {
            _status.Text = "读取失败：" + ex.Message;
        }
    }

    private async Task SyncAsync()
    {
        try
        {
            _status.Text = "正在从本地主简历同步…";
            var local = JobRepository.LoadResume(_dataDirectory);
            var result = await ZhaopinResumeService.SyncSafeAsync(
                _context,
                _dataDirectory,
                local,
                CancellationToken.None).ConfigureAwait(true);
            await ReloadAsync();
            MessageBox.Show(
                this,
                result.ToText(),
                "智联简历同步",
                MessageBoxButton.OK,
                result.ReadyForApply ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _status.Text = "同步失败：" + ex.Message;
        }
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_snapshot == null) return;

        AddSectionTitle("个人优势");
        if (!string.IsNullOrWhiteSpace(_snapshot.SelfEvaluate))
        {
            _list.Children.Add(NodeCard(
                "个人优势",
                Short(_snapshot.SelfEvaluate, 180),
                "SelfEvaluate",
                _snapshot.SelfEvaluatePath ?? "",
                allowDelete: true));
        }
        else
        {
            AddEmpty("暂无个人优势");
        }

        AddSectionTitle("工作经历");
        if (_snapshot.WorkExperiences.Count == 0)
        {
            AddEmpty("暂无工作经历");
        }
        else
        {
            foreach (var item in _snapshot.WorkExperiences)
            {
                var start = FormatMonth(item.StartDate);
                var end = item.EndDate <= 0 ? "至今" : FormatMonth(item.EndDate);
                _list.Children.Add(NodeCard(
                    item.CompanyName + " · " + item.JobTitle,
                    $"{start}-{end}\n{Short(item.Description, 220)}",
                    "WorkExperience",
                    item.Path,
                    allowDelete: true));
            }
        }

        AddSectionTitle("项目经历");
        if (_snapshot.ProjectExperiences.Count == 0)
        {
            AddEmpty("暂无项目经历；本地主简历里有项目，但日期还需要确认，因此不会自动编造。");
        }
        else
        {
            foreach (var item in _snapshot.ProjectExperiences)
            {
                var start = FormatMonth(item.StartDate);
                var end = item.EndDate <= 0 ? "至今" : FormatMonth(item.EndDate);
                _list.Children.Add(NodeCard(
                    item.Name,
                    $"{start}-{end}\n{Short(item.Description, 220)}",
                    "ProjectExperience",
                    item.Path,
                    allowDelete: true));
            }
        }

        AddSectionTitle("专业技能");
        if (_snapshot.ProfessionalSkills.Count == 0)
        {
            AddEmpty("暂无专业技能；熟练度和使用年限需要事实确认后再新增。");
        }
        else
        {
            foreach (var item in _snapshot.ProfessionalSkills)
            {
                _list.Children.Add(NodeCard(
                    item.Name,
                    $"熟练度：{item.Level} · 使用时长：{item.UseTime}",
                    "ProfessionalSkill",
                    item.Path,
                    allowDelete: true));
            }
        }

        AddSectionTitle("教育经历");
        if (_snapshot.EducationExperiences.Count == 0)
        {
            AddEmpty("暂无教育经历");
        }
        else
        {
            foreach (var item in _snapshot.EducationExperiences)
            {
                _list.Children.Add(NodeCard(
                    $"{item.School} · {item.Major} · {item.Degree}",
                    $"{item.StartYear}-{item.EndYear}（教育信息只读展示，避免误删）",
                    "EducationExperience",
                    item.Path,
                    allowDelete: false));
            }
        }
    }

    private Border NodeCard(
        string title,
        string subtitle,
        string nodeName,
        string path,
        bool allowDelete)
    {
        var border = new Border
        {
            Background = Panel,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Muted,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 12, 0)
        });
        grid.Children.Add(text);

        if (allowDelete && !string.IsNullOrWhiteSpace(path))
        {
            var delete = ActionButton("删除");
            delete.Margin = new Thickness(8, 0, 0, 0);
            delete.VerticalAlignment = VerticalAlignment.Top;
            delete.Click += async (_, _) => await DeleteAsync(nodeName, path, title);
            Grid.SetColumn(delete, 1);
            grid.Children.Add(delete);
        }

        border.Child = grid;
        return border;
    }

    private async Task DeleteAsync(string nodeName, string path, string title)
    {
        if (_snapshot == null) return;

        var confirm = MessageBox.Show(
            this,
            "确定从智联在线简历删除这一项吗？\n\n" + title
            + "\n\n本地主简历不会被删除；如本地主简历仍保留该项，下次同步可能会重新新增。",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _status.Text = "正在删除：" + title;
            await ZhaopinResumeService.DeleteNodeAsync(
                _context,
                _snapshot,
                nodeName,
                path,
                CancellationToken.None).ConfigureAwait(true);
            await ReloadAsync();
            _status.Text = "已删除：" + title;
        }
        catch (Exception ex)
        {
            _status.Text = "删除失败：" + ex.Message;
        }
    }

    private void AddSectionTitle(string text)
    {
        _list.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 8)
        });
    }

    private void AddEmpty(string text)
    {
        _list.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Muted,
            FontSize = 11.5,
            Margin = new Thickness(4, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static Button ActionButton(string text, bool primary = false) => new()
    {
        Content = text,
        Padding = new Thickness(11, 6, 11, 6),
        Background = primary ? Accent : BrushFrom("#1A2633"),
        Foreground = primary ? BrushFrom("#06111C") : Text,
        BorderBrush = primary ? Accent : Border,
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand,
        FontSize = 11.5
    };

    private static string FormatMonth(long value)
    {
        if (value <= 0) return "";
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value)
                .ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy.MM");
        }
        catch
        {
            return "";
        }
    }

    private static string Short(string text, int max)
    {
        var value = (text ?? "").Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }

    private static SolidColorBrush BrushFrom(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

public static class ResumeTextParser
{
    private static readonly string[] Headings =
    {
        "个人优势","期望职位","工作经历","项目经历","教育经历","技能","专业技能","作品","图片作品","视频作品",
        "资格证书","证书","志愿者经历"
    };

    public static ResumeProfile Merge(ResumeProfile current, string text)
    {
        text = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (string.IsNullOrWhiteSpace(text)) return current;

        var result = current;
        result.RawImportedText = text;

        var advantage = Section(text, "个人优势");
        var expected = Section(text, "期望职位");
        var work = Section(text, "工作经历");
        var projects = Section(text, "项目经历");
        var education = Section(text, "教育经历");
        var skills = Section(text, "技能", "专业技能");
        var portfolio = Section(text, "作品", "图片作品", "视频作品");
        var certificates = Section(text, "资格证书", "证书");
        var volunteer = Section(text, "志愿者经历");

        if (!string.IsNullOrWhiteSpace(advantage)) result.PersonalAdvantage = advantage;
        if (!string.IsNullOrWhiteSpace(expected)) result.ExpectedPosition = expected;
        if (!string.IsNullOrWhiteSpace(work)) result.WorkExperience = work;
        if (!string.IsNullOrWhiteSpace(projects)) result.ProjectExperience = projects;
        if (!string.IsNullOrWhiteSpace(education)) result.Education = education;
        if (!string.IsNullOrWhiteSpace(skills)) result.Skills = skills;
        if (!string.IsNullOrWhiteSpace(portfolio)) result.Portfolio = portfolio;
        if (!string.IsNullOrWhiteSpace(certificates)) result.Certificates = certificates;
        if (!string.IsNullOrWhiteSpace(volunteer)) result.Volunteer = volunteer;

        var lines = text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (string.IsNullOrWhiteSpace(result.Name) && lines.Count > 0 && lines[0].Length <= 12)
            result.Name = lines[0];

        result.UpdatedAt = DateTimeOffset.Now;
        return result;
    }

    private static string Section(string text, params string[] aliases)
    {
        var starts = new List<(int Index, string Heading)>();
        foreach (var heading in Headings)
        {
            var i = text.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) starts.Add((i, heading));
        }

        var own = starts
            .Where(x => aliases.Any(a => string.Equals(a, x.Heading, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Index)
            .FirstOrDefault();

        if (own.Heading == null) return "";
        var start = own.Index + own.Heading.Length;
        var end = starts.Where(x => x.Index > own.Index).Select(x => x.Index).DefaultIfEmpty(text.Length).Min();
        return text.Substring(start, Math.Max(0, end - start)).Trim(' ', '\t', ':', '：', '\n');
    }
}

public static class ResumeDocument
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string ReadText(string path)
    {
        var ext = Path.GetExtension(path);
        if (!string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllText(path);

        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidDataException("DOCX 中没有找到正文。");
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace w = WordNamespace;
        return string.Join("\n",
            doc.Descendants(w + "p")
                .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)))
                .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public static void ExportDocx(ResumeProfile profile, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteEntry(archive, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>");

        WriteEntry(archive, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>");

        var lines = profile.ToPlainText().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var body = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i] ?? "";
            var isSection = line.StartsWith("【", StringComparison.Ordinal) && line.EndsWith("】", StringComparison.Ordinal);
            var isName = i == 0 && !string.IsNullOrWhiteSpace(line);
            body.Append(Paragraph(line, isName, isSection));
        }

        body.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>");

        WriteEntry(archive, "word/document.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"" + WordNamespace + "\"><w:body>" +
            body +
            "</w:body></w:document>");
    }

    private static string Paragraph(string text, bool isName, bool isSection)
    {
        var escaped = SecurityElement.Escape(text) ?? "";
        var size = isName ? "34" : isSection ? "27" : "22";
        var bold = isName || isSection ? "<w:b/>" : "";
        var spaceAfter = isSection ? "120" : "60";
        return "<w:p><w:pPr><w:spacing w:after=\"" + spaceAfter + "\"/></w:pPr>" +
               "<w:r><w:rPr>" + bold + "<w:sz w:val=\"" + size + "\"/><w:szCs w:val=\"" + size + "\"/></w:rPr>" +
               "<w:t xml:space=\"preserve\">" + escaped + "</w:t></w:r></w:p>";
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}



public static class ZhaopinResumeService
{
    private const string ResumePageUrl = "https://i.zhaopin.com/resume";
    private const string ApiBase = "https://fe-api.zhaopin.com";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<ZhaopinResumeSnapshot> ReadAsync(
        YanziActionContext context,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        var browser = new LocalBrowserClient(context);

        var listResult = await browser.DirectWorkflowAsync(
            ResumePageUrl,
            new List<Dictionary<string, object?>>
            {
                FetchStep("list", ApiBase + "/c/i/resumes?lang=1", "GET")
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var listFetch = ParseFetchResult(First(listResult, "list"));
        var listData = listFetch.GetProperty("data").GetProperty("data");
        if (listData.ValueKind != JsonValueKind.Array || listData.GetArrayLength() == 0)
            throw new InvalidOperationException("智联账号没有可编辑的在线简历。");

        var selected = listData.EnumerateArray()
            .FirstOrDefault(x => GetString(x, "isEditable") == "y");
        if (selected.ValueKind == JsonValueKind.Undefined)
            selected = listData[0];

        var resumeId = GetLong(selected, "resumeId");
        var listResumeNumber = GetString(selected, "resumeNumber");
        if (resumeId <= 0 || string.IsNullOrWhiteSpace(listResumeNumber))
            throw new InvalidOperationException("无法识别智联在线简历标识。");

        var detailUrl = ApiBase + "/c/i/resume?resumeId=" + resumeId
                        + "&resumeNumber=" + Uri.EscapeDataString(listResumeNumber)
                        + "&lang=1";

        var detailResult = await browser.DirectWorkflowAsync(
            ResumePageUrl,
            new List<Dictionary<string, object?>>
            {
                FetchStep("resume", detailUrl, "GET")
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var detailFetch = ParseFetchResult(First(detailResult, "resume"));
        var detail = detailFetch.GetProperty("data").GetProperty("data");
        var snapshot = ParseSnapshot(detail);

        Directory.CreateDirectory(dataDirectory);
        var rawJson = detail.GetRawText();
        var rawPath = Path.Combine(dataDirectory, "zhaopin-resume-raw.json");
        File.WriteAllText(rawPath, rawJson, new UTF8Encoding(false));

        var diff = Compare(
            JobRepository.LoadResume(dataDirectory),
            snapshot,
            JobRepository.LoadSettings(dataDirectory));
        var state = LoadState(dataDirectory);

        var snapshotHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
        if (!string.Equals(snapshotHash, state.LastSnapshotHash, StringComparison.OrdinalIgnoreCase))
        {
            var historyDir = Path.Combine(dataDirectory, "zhaopin-resume-history");
            Directory.CreateDirectory(historyDir);
            var historyPath = Path.Combine(
                historyDir,
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(historyPath, rawJson, new UTF8Encoding(false));
            state.LastSnapshotHash = snapshotHash;
            state.LastSnapshotPath = historyPath;
        }

        state.ResumeId = snapshot.ResumeId;
        state.ResumeNumber = snapshot.ResumeNumber;
        state.LastReadAt = DateTimeOffset.Now;
        state.LastOnlineModifiedDate = snapshot.ModifiedDate;
        state.NeedsSync = diff.NeedsSync;
        state.ReadyForApply = !diff.HasCriticalDiff;
        state.DiffSummary = diff.ToCompactText();
        state.RawSnapshotPath = rawPath;
        SaveState(dataDirectory, state);

        return snapshot;
    }

    public static ZhaopinResumeDiff Compare(
        ResumeProfile local,
        ZhaopinResumeSnapshot online,
        JobAgentSettings? settings = null)
    {
        settings ??= new JobAgentSettings();
        settings.Normalize();
        var diff = new ZhaopinResumeDiff();

        var localAdvantage = Normalize(local.PersonalAdvantage);
        var onlineAdvantage = Normalize(online.SelfEvaluate);
        if (!string.IsNullOrWhiteSpace(localAdvantage)
            && !EquivalentText(localAdvantage, onlineAdvantage))
        {
            diff.Add("个人优势", "本地主简历与智联线上内容不同", critical: true, autoSync: true);
        }

        var expectedCompanies = ParseLocalWorkCompanies(local.WorkExperience);
        var onlineCompanies = online.WorkExperiences
            .Select(x => Normalize(x.CompanyName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        foreach (var company in expectedCompanies)
        {
            var normalized = Normalize(company);
            if (!onlineCompanies.Any(x => x.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                                          || normalized.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                diff.Add("工作经历", "智联缺少：" + company, critical: true, autoSync: false);
            }
        }

        foreach (var onlineWork in online.WorkExperiences)
        {
            if (expectedCompanies.Count == 0) break;
            var normalized = Normalize(onlineWork.CompanyName);
            var exists = expectedCompanies.Any(x =>
            {
                var localName = Normalize(x);
                return localName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                       || normalized.Contains(localName, StringComparison.OrdinalIgnoreCase);
            });
            if (!exists)
                diff.Add("工作经历", "线上存在但本地主简历未列出：" + onlineWork.CompanyName + "（先保留，不自动删除）",
                    critical: false, autoSync: false);
        }

        var localProjects = ParseLocalProjects(local.ProjectExperience);
        foreach (var project in localProjects)
        {
            if (!online.ProjectExperiences.Any(x =>
                    Normalize(x.Name).Contains(Normalize(project), StringComparison.OrdinalIgnoreCase)
                    || Normalize(project).Contains(Normalize(x.Name), StringComparison.OrdinalIgnoreCase)))
            {
                diff.Add("项目经历", "智联缺少：" + project + "（项目日期需要确认）",
                    critical: false, autoSync: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(local.Skills) && online.ProfessionalSkills.Count == 0)
            diff.Add("专业技能", "本地有技能关键词，但智联专业技能为空；熟练度/使用年限需要确认",
                critical: false, autoSync: false);

        var educationYears = Regex.Match(local.Education ?? "", @"(?<a>20\d{2})\s*[-—至]\s*(?<b>20\d{2})");
        if (educationYears.Success
            && online.EducationExperiences.Count > 0
            && int.TryParse(educationYears.Groups["a"].Value, out var localStartYear)
            && int.TryParse(educationYears.Groups["b"].Value, out var localEndYear))
        {
            var edu = online.EducationExperiences[0];
            if (edu.StartYear != localStartYear || edu.EndYear != localEndYear)
            {
                diff.Add("教育经历",
                    $"本地为 {localStartYear}-{localEndYear}，智联为 {edu.StartYear}-{edu.EndYear}；月份需确认后再改",
                    critical: true, autoSync: false);
            }
        }

        if (!string.Equals(
                online.PurposeJobTypeId,
                settings.OnlinePurposeJobTypeId,
                StringComparison.Ordinal)
            || online.PurposeSalaryMin != settings.OnlinePurposeSalaryMin
            || online.PurposeSalaryMax != settings.OnlinePurposeSalaryMax
            || online.PurposeIndustryUnlimited != settings.OnlinePurposeIndustryUnlimited)
        {
            diff.Add("求职意向",
                $"智联为“{online.PurposeTitle} / {online.PurposeSalaryMin}-{online.PurposeSalaryMax} / "
                + (online.PurposeIndustryUnlimited ? "不限行业" : "限定行业")
                + $"”，目标为“{settings.OnlinePurposeJobTitle} / {settings.OnlinePurposeSalaryMin}-{settings.OnlinePurposeSalaryMax} / "
                + (settings.OnlinePurposeIndustryUnlimited ? "不限行业" : "限定行业") + "”",
                critical: true, autoSync: true);
        }

        return diff;
    }

    public static async Task<ZhaopinResumeSyncResult> SyncSafeAsync(
        YanziActionContext context,
        string dataDirectory,
        ResumeProfile local,
        CancellationToken cancellationToken)
    {
        var settings = JobRepository.LoadSettings(dataDirectory);
        var before = await ReadAsync(context, dataDirectory, cancellationToken).ConfigureAwait(false);
        var messages = new List<string>();
        var changed = 0;

        if (!EquivalentText(Normalize(local.PersonalAdvantage), Normalize(before.SelfEvaluate))
            && !string.IsNullOrWhiteSpace(local.PersonalAdvantage))
        {
            var values = new Dictionary<string, object?>
            {
                ["selfEvaTitle"] = "自我介绍",
                ["selfEvaUserdefTitle"] = "自我介绍",
                ["selfEvaContent"] = BuildPublicSelfEvaluate(local),
                ["path"] = before.SelfEvaluatePath ?? ""
            };

            await UpsertNodeAsync(
                context,
                before,
                "SelfEvaluate",
                values,
                cancellationToken).ConfigureAwait(false);

            changed++;
            messages.Add("已同步个人优势");
        }

        var localWorks = ParseLocalWorkEntries(local.WorkExperience);
        foreach (var localWork in localWorks)
        {
            var remote = before.WorkExperiences.FirstOrDefault(x =>
                CompanyEquivalent(localWork.CompanyName, x.CompanyName));

            if (remote != null)
            {
                var titleChanged = !EquivalentText(Normalize(localWork.JobTitle), Normalize(remote.JobTitle));
                var startChanged = remote.StartDate != localWork.StartDate;
                var endChanged = remote.EndDate != localWork.EndDate;
                var descChanged = !EquivalentText(Normalize(localWork.Description), Normalize(remote.Description));

                if (!titleChanged && !startChanged && !endChanged && !descChanged)
                    continue;

                var jobTypeId = remote.NewJobSubType;
                var skillTagStandard = remote.SkillTagStandard;

                if (titleChanged)
                {
                    var recommendation = await GetJobTypeRecommendationAsync(
                        context,
                        localWork.JobTitle,
                        cancellationToken).ConfigureAwait(false);
                    if (recommendation != null)
                        jobTypeId = recommendation.Value;

                    var mappedSkill = MapSkillTagStandard(localWork.JobTitle);
                    if (!string.IsNullOrWhiteSpace(mappedSkill))
                        skillTagStandard = mappedSkill;
                }

                if (string.IsNullOrWhiteSpace(jobTypeId) || string.IsNullOrWhiteSpace(skillTagStandard))
                {
                    messages.Add("跳过结构化修改：" + localWork.CompanyName + "（职位分类或技能关键词需要确认）");
                    continue;
                }

                var values = new Dictionary<string, object?>
                {
                    ["newCompanyId"] = remote.NewCompanyId,
                    ["companyName"] = localWork.CompanyName,
                    ["wnewIndustry"] = remote.NewIndustry,
                    ["industrySerial"] = remote.IndustrySerial,
                    ["jobTitle"] = localWork.JobTitle,
                    ["wnewJobSubType"] = jobTypeId,
                    ["jobTypeSerial"] = remote.JobTypeSerial,
                    ["startDate"] = localWork.StartDate,
                    ["endDate"] = localWork.EndDate,
                    ["realSalary"] = remote.RealSalary,
                    ["workdays"] = remote.Workdays,
                    ["dailyWage"] = remote.DailyWage,
                    ["workDesc"] = localWork.Description,
                    ["path"] = remote.Path,
                    ["skillTagStandard"] = skillTagStandard,
                    ["skillTagCustomized"] = remote.SkillTagCustomized,
                    ["preferenceQuestionAndAnswer"] = remote.PreferenceQuestionAndAnswer,
                    ["internshipWork"] = remote.InternshipWork
                };

                await UpsertNodeAsync(
                    context,
                    before,
                    "WorkExperience",
                    values,
                    cancellationToken).ConfigureAwait(false);

                changed++;
                messages.Add("已更新工作经历：" + localWork.CompanyName);
                continue;
            }

            var jobType = await GetJobTypeRecommendationAsync(
                context,
                localWork.JobTitle,
                cancellationToken).ConfigureAwait(false);
            var skillTags = MapSkillTagStandard(localWork.JobTitle);

            if (jobType == null || string.IsNullOrWhiteSpace(skillTags))
            {
                messages.Add("未自动新增：" + localWork.CompanyName + "（职位分类或技能关键词需要确认）");
                continue;
            }

            var createValues = new Dictionary<string, object?>
            {
                ["newCompanyId"] = "",
                ["companyName"] = localWork.CompanyName,
                ["wnewIndustry"] = "",
                ["industrySerial"] = "",
                ["jobTitle"] = localWork.JobTitle,
                ["wnewJobSubType"] = jobType.Value,
                ["jobTypeSerial"] = "",
                ["startDate"] = localWork.StartDate,
                ["endDate"] = localWork.EndDate,
                ["realSalary"] = 0,
                ["workdays"] = "",
                ["dailyWage"] = "",
                ["workDesc"] = localWork.Description,
                ["path"] = null,
                ["skillTagStandard"] = skillTags,
                ["skillTagCustomized"] = "",
                ["preferenceQuestionAndAnswer"] = "",
                ["internshipWork"] = ""
            };

            await UpsertNodeAsync(
                context,
                before,
                "WorkExperience",
                createValues,
                cancellationToken).ConfigureAwait(false);

            changed++;
            messages.Add("已新增工作经历：" + localWork.CompanyName);
        }

        var cityCode = before.PurposeLocationCode;
        if (string.IsNullOrWhiteSpace(cityCode))
            cityCode = "801";

        var purposeNeedsUpdate =
            !string.Equals(before.PurposeJobTypeId, settings.OnlinePurposeJobTypeId, StringComparison.Ordinal)
            || before.PurposeSalaryMin != settings.OnlinePurposeSalaryMin
            || before.PurposeSalaryMax != settings.OnlinePurposeSalaryMax
            || before.PurposeIndustryUnlimited != settings.OnlinePurposeIndustryUnlimited;

        if (purposeNeedsUpdate
            && !string.IsNullOrWhiteSpace(settings.OnlinePurposeJobTypeId)
            && settings.OnlinePurposeSalaryMax >= settings.OnlinePurposeSalaryMin)
        {
            var purposeValues = new Dictionary<string, object?>
            {
                ["preferredJobNature"] = "2",
                ["preferredLocation"] = cityCode.Split(':')[0],
                ["preferredCityDistrict"] = cityCode,
                ["pnewPreferredJobType"] = settings.OnlinePurposeJobTypeId,
                ["preferredJobTypeSerial"] = "",
                ["preferredIndustrySerial"] = settings.OnlinePurposeIndustryUnlimited
                    ? "[{\"code\":\"-99\",\"serial\":\"\"}]"
                    : "[]",
                ["preferredSalaryMin"] = settings.OnlinePurposeSalaryMin,
                ["preferredSalaryMax"] = settings.OnlinePurposeSalaryMax,
                ["preferenceQuestionAndAnswer"] = "[]",
                ["path"] = before.PurposePath
            };

            await UpsertNodeAsync(
                context,
                before,
                "UnifiedPurpose",
                purposeValues,
                cancellationToken).ConfigureAwait(false);

            changed++;
            messages.Add(
                $"已同步求职意向：{settings.OnlinePurposeJobTitle} / {settings.OnlinePurposeSalaryMin}-{settings.OnlinePurposeSalaryMax}");
        }

        var after = await ReadAsync(context, dataDirectory, cancellationToken).ConfigureAwait(false);
        var diff = Compare(local, after, settings);

        var state = LoadState(dataDirectory);
        state.LastSyncAt = DateTimeOffset.Now;
        state.LastVerifiedAt = DateTimeOffset.Now;
        state.NeedsSync = diff.NeedsSync;
        state.ReadyForApply = !diff.HasCriticalDiff;
        state.DiffSummary = diff.ToCompactText();
        SaveState(dataDirectory, state);

        return new ZhaopinResumeSyncResult
        {
            ChangedCount = changed,
            RemainingDiffCount = diff.Items.Count,
            ReadyForApply = state.ReadyForApply,
            Messages = messages,
            Remaining = diff.Items.Select(x => x.Section + "：" + x.Message).ToList()
        };
    }

    public static async Task<JsonElement> UpsertNodeAsync(
        YanziActionContext context,
        ZhaopinResumeSnapshot snapshot,
        string nodeName,
        Dictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var browser = new LocalBrowserClient(context);
        var body = new Dictionary<string, object?>
        {
            ["resumeId"] = snapshot.ResumeId,
            ["resumeNumber"] = snapshot.ResumeNumber,
            ["nodeName"] = nodeName,
            ["lang"] = 1,
            ["sid"] = "none",
            ["site"] = "none",
            ["values"] = values
        };

        var result = await browser.DirectWorkflowAsync(
            ResumePageUrl,
            new List<Dictionary<string, object?>>
            {
                FetchStep("write", ApiBase + "/c/i/resume/node", "POST", body)
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var fetch = ParseFetchResult(First(result, "write"));
        EnsureApiSuccess(fetch, "更新 " + nodeName);
        return fetch;
    }

    public static async Task DeleteNodeAsync(
        YanziActionContext context,
        ZhaopinResumeSnapshot snapshot,
        string nodeName,
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("删除节点必须提供 path。", nameof(path));

        var url = ApiBase + "/c/i/resume/node/delete?resumeId=" + snapshot.ResumeId
                  + "&resumeNumber=" + Uri.EscapeDataString(snapshot.ResumeNumber)
                  + "&nodeName=" + Uri.EscapeDataString(nodeName)
                  + "&lang=1&path=" + Uri.EscapeDataString(path);

        var browser = new LocalBrowserClient(context);
        var result = await browser.DirectWorkflowAsync(
            ResumePageUrl,
            new List<Dictionary<string, object?>>
            {
                FetchStep("delete", url, "GET")
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var fetch = ParseFetchResult(First(result, "delete"));
        EnsureApiSuccess(fetch, "删除 " + nodeName);
    }

    public static bool CanApply(string dataDirectory, out string reason)
    {
        var state = LoadState(dataDirectory);
        var local = JobRepository.LoadResume(dataDirectory);

        if (!state.LastReadAt.HasValue)
        {
            reason = "尚未读取智联在线简历。请先进入“我的简历”点击“读取智联/对比智联”。";
            return false;
        }

        if (!state.LastVerifiedAt.HasValue)
        {
            reason = "智联简历尚未完成同步验证。请先处理简历差异。";
            return false;
        }

        if (local.UpdatedAt > state.LastVerifiedAt.Value)
        {
            reason = "本地主简历在最近一次智联同步后又被修改，请重新对比并同步。";
            return false;
        }

        if (!state.ReadyForApply)
        {
            reason = "智联在线简历仍有关键差异：" + state.DiffSummary;
            return false;
        }

        reason = "";
        return true;
    }

    public static void MarkLocalChanged(string dataDirectory, DateTimeOffset changedAt)
    {
        var state = LoadState(dataDirectory);
        state.LocalChangedAt = changedAt;
        state.ReadyForApply = false;
        if (state.LastVerifiedAt.HasValue && changedAt > state.LastVerifiedAt.Value)
        {
            state.NeedsSync = true;
            state.DiffSummary = "本地主简历已更新，需要重新对比智联。";
        }
        SaveState(dataDirectory, state);
    }

    public static ZhaopinResumeSyncState LoadState(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "resume-sync-state.json");
        if (!File.Exists(path)) return new ZhaopinResumeSyncState();

        try
        {
            return JsonSerializer.Deserialize<ZhaopinResumeSyncState>(File.ReadAllText(path), Json)
                   ?? new ZhaopinResumeSyncState();
        }
        catch
        {
            return new ZhaopinResumeSyncState();
        }
    }

    public static void SaveState(string dataDirectory, ZhaopinResumeSyncState state)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "resume-sync-state.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static ZhaopinResumeSnapshot ParseSnapshot(JsonElement detail)
    {
        var snapshot = new ZhaopinResumeSnapshot();

        if (detail.TryGetProperty("Business", out var business))
        {
            snapshot.ResumeId = GetLong(business, "resumeId");
            snapshot.ResumeNumber = GetString(business, "resumeNumber");
            snapshot.ModifiedDate = GetLong(business, "modifiedDate");
        }

        if (detail.TryGetProperty("ComPletionDegree", out var completion)
            && completion.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in completion.EnumerateArray())
            {
                if (GetString(item, "languageId") == "1")
                {
                    snapshot.CompletionScore = (int)GetLong(item, "totalScore");
                    break;
                }
            }
        }

        if (detail.TryGetProperty("SelfEvaluate", out var self)
            && self.ValueKind == JsonValueKind.Array
            && self.GetArrayLength() > 0)
        {
            var item = self[0];
            snapshot.SelfEvaluate = GetString(item, "selfEvaContent");
            snapshot.SelfEvaluatePath = GetString(item, "path");
        }

        if (detail.TryGetProperty("WorkExperience", out var works)
            && works.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in works.EnumerateArray())
            {
                snapshot.WorkExperiences.Add(new ZhaopinWorkExperience
                {
                    CompanyName = GetString(item, "companyName"),
                    JobTitle = GetString(item, "jobTitle"),
                    Description = GetString(item, "workDesc"),
                    Path = GetString(item, "path"),
                    StartDate = GetLong(item, "startDate"),
                    EndDate = GetLong(item, "endDate"),
                    RealSalary = GetLong(item, "realSalary"),
                    NewCompanyId = GetString(item, "newCompanyId"),
                    NewIndustry = GetString(item, "wnewIndustry"),
                    IndustrySerial = GetString(item, "industrySerial"),
                    NewJobSubType = GetString(item, "wnewJobSubType"),
                    JobTypeSerial = GetString(item, "jobTypeSerial"),
                    Workdays = GetString(item, "workdays"),
                    DailyWage = GetString(item, "dailyWage"),
                    SkillTagStandard = GetString(item, "skillTagStandard"),
                    SkillTagCustomized = GetString(item, "skillTagCustomized"),
                    PreferenceQuestionAndAnswer = GetString(item, "preferenceQuestionAndAnswer"),
                    InternshipWork = GetString(item, "internshipWork")
                });
            }
        }

        if (detail.TryGetProperty("ProjectExperience", out var projects)
            && projects.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in projects.EnumerateArray())
            {
                snapshot.ProjectExperiences.Add(new ZhaopinProjectExperience
                {
                    Name = GetString(item, "proExpProjectName"),
                    Description = GetString(item, "proExpProjectDesc"),
                    Path = GetString(item, "path"),
                    StartDate = GetLong(item, "proExpStartDate"),
                    EndDate = GetLong(item, "proExpEndDate")
                });
            }
        }

        if (detail.TryGetProperty("ProfessionalSkill", out var skills)
            && skills.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in skills.EnumerateArray())
            {
                snapshot.ProfessionalSkills.Add(new ZhaopinProfessionalSkill
                {
                    Name = GetString(item, "proskillName"),
                    Level = GetString(item, "proskillLevel"),
                    UseTime = GetString(item, "proskillUseTime"),
                    Path = GetString(item, "path")
                });
            }
        }

        if (detail.TryGetProperty("EducationExperience", out var educations)
            && educations.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in educations.EnumerateArray())
            {
                var start = FromUnix(GetLong(item, "eduStartDate"));
                var end = FromUnix(GetLong(item, "eduEndDate"));
                snapshot.EducationExperiences.Add(new ZhaopinEducationExperience
                {
                    School = GetString(item, "eduSchoolName"),
                    Major = GetString(item, "eduMajorV"),
                    Degree = GetString(item, "eduBackgroundTranslation"),
                    StartYear = start?.Year ?? 0,
                    EndYear = end?.Year ?? 0,
                    Path = GetString(item, "path")
                });
            }
        }

        if (detail.TryGetProperty("UnifiedPurpose", out var purposes)
            && purposes.ValueKind == JsonValueKind.Array
            && purposes.GetArrayLength() > 0)
        {
            var purpose = purposes[0];
            snapshot.PurposeTitle = GetString(purpose, "pnewPreferredJobTypeTranslation");
            snapshot.PurposeJobTypeId = GetString(purpose, "pnewPreferredJobType");
            snapshot.PurposeSalaryMin = (int)GetLong(purpose, "preferredSalaryMin");
            snapshot.PurposeSalaryMax = (int)GetLong(purpose, "preferredSalaryMax");
            snapshot.PurposeLocationCode = GetString(purpose, "preferredCityDistrict");
            snapshot.PurposeLocationName = GetString(purpose, "preferredCityDistrictTranslation");
            snapshot.PurposeIndustryUnlimited =
                purpose.TryGetProperty("industryUnlimited", out var unlimited)
                && unlimited.ValueKind == JsonValueKind.True;
            snapshot.PurposePath = GetString(purpose, "path");
        }

        return snapshot;
    }

    private static string BuildPublicSelfEvaluate(ResumeProfile local)
    {
        var text = local.PersonalAdvantage ?? "";
        text = Regex.Replace(text, @"\b1\d{10}\b", "");
        text = Regex.Replace(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "");
        text = Regex.Replace(text, @"\b[1-9]\d{5,11}\b", "");
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();

        if (text.Length > 500) text = text[..500];
        return text;
    }

    private static async Task<ZhaopinJobTypeRecommendation?> GetJobTypeRecommendationAsync(
        YanziActionContext context,
        string title,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var browser = new LocalBrowserClient(context);
        var url = ApiBase + "/c/i/resume/job-type/recommendation?title="
                  + Uri.EscapeDataString(title.Trim())
                  + "&jobTypeId=";

        var result = await browser.DirectWorkflowAsync(
            ResumePageUrl,
            new List<Dictionary<string, object?>>
            {
                FetchStep("recommend", url, "GET")
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var fetch = ParseFetchResult(First(result, "recommend"));
        if (!fetch.TryGetProperty("data", out var payload)
            || !payload.TryGetProperty("data", out var data)
            || !data.TryGetProperty("recommendList", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() == 0)
        {
            return null;
        }

        var exact = list.EnumerateArray()
            .FirstOrDefault(x => string.Equals(
                GetString(x, "name"),
                title.Trim(),
                StringComparison.OrdinalIgnoreCase));

        var selected = exact.ValueKind == JsonValueKind.Undefined ? list[0] : exact;
        var value = GetString(selected, "value");
        if (string.IsNullOrWhiteSpace(value)) return null;

        return new ZhaopinJobTypeRecommendation
        {
            Name = GetString(selected, "name"),
            ParentName = GetString(selected, "parentName"),
            ParentValue = GetString(selected, "parentValue"),
            Value = value
        };
    }

    private static string MapSkillTagStandard(string title)
    {
        var t = Normalize(title);
        var ids = new List<string>();

        if (t.Contains("ai") || t.Contains("人工智能") || t.Contains("智能体") || t.Contains("agent"))
            ids.Add("270062136");

        if (t.Contains("产品"))
        {
            ids.Add("270061210");
            ids.Add("270061256");
        }

        if (t.Contains("设计") || t.Contains("视觉"))
        {
            ids.Add("270059708");
            ids.Add("270061144");
        }

        return string.Join(",", ids.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool CompanyEquivalent(string a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        if (string.IsNullOrWhiteSpace(x) || string.IsNullOrWhiteSpace(y)) return false;
        return x.Equals(y, StringComparison.OrdinalIgnoreCase)
               || x.Contains(y, StringComparison.OrdinalIgnoreCase)
               || y.Contains(x, StringComparison.OrdinalIgnoreCase);
    }

    private static List<LocalWorkEntry> ParseLocalWorkEntries(string text)
    {
        var result = new List<LocalWorkEntry>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var header = new Regex(
            @"(?m)^(?<company>[^\n｜]+?)\s*｜\s*(?<title>[^\n｜]+?)\s*｜\s*(?<start>20\d{2}\.\d{2})\s*-\s*(?<end>至今|20\d{2}\.\d{2})\s*$",
            RegexOptions.Compiled);

        var matches = header.Matches(normalized);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var descStart = match.Index + match.Length;
            if (descStart < normalized.Length && normalized[descStart] == '\n')
                descStart++;

            var descEnd = i + 1 < matches.Count ? matches[i + 1].Index : normalized.Length;
            var description = normalized.Substring(descStart, Math.Max(0, descEnd - descStart)).Trim();

            if (!TryMonthToUnix(match.Groups["start"].Value, out var startDate))
                continue;

            var endText = match.Groups["end"].Value;
            long endDate;
            if (string.Equals(endText, "至今", StringComparison.OrdinalIgnoreCase))
                endDate = 0;
            else if (!TryMonthToUnix(endText, out endDate))
                continue;

            result.Add(new LocalWorkEntry
            {
                CompanyName = match.Groups["company"].Value.Trim(),
                JobTitle = match.Groups["title"].Value.Trim(),
                StartDate = startDate,
                EndDate = endDate,
                Description = description
            });
        }

        return result;
    }

    private static bool TryMonthToUnix(string text, out long value)
    {
        value = 0;
        if (!DateTime.TryParseExact(
                text.Trim(),
                "yyyy.MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return false;
        }

        var dto = new DateTimeOffset(
            date.Year,
            date.Month,
            1,
            0,
            0,
            0,
            TimeSpan.FromHours(8));
        value = dto.ToUnixTimeMilliseconds();
        return true;
    }

    private static List<string> ParseLocalWorkCompanies(string text)
    {
        var result = new List<string>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !trimmed.Contains('｜')) continue;
            if (!Regex.IsMatch(trimmed, @"20\d{2}\.\d{2}")) continue;

            var company = trimmed.Split('｜')[0].Trim();
            if (!string.IsNullOrWhiteSpace(company))
                result.Add(company);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ParseLocalProjects(string text)
    {
        return (text ?? "")
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x =>
            {
                var i = x.IndexOf('：');
                if (i < 0) i = x.IndexOf(':');
                return i > 0 ? x[..i].Trim() : x;
            })
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FindLocalWorkDescription(string text, string company)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(company)) return "";

        var sections = Regex.Split(text, @"(?m)(?=^.+?｜.+?｜20\d{2}\.\d{2}-)");
        foreach (var section in sections)
        {
            if (!section.Contains(company, StringComparison.OrdinalIgnoreCase)) continue;
            var lines = section.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (lines.Count <= 1) return "";
            return string.Join("\n", lines.Skip(1)).Trim();
        }
        return "";
    }

    private static bool EquivalentText(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Length >= 20 && b.Contains(a, StringComparison.OrdinalIgnoreCase)) return true;
        if (b.Length >= 20 && a.Contains(b, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string Normalize(string text) =>
        Regex.Replace(text ?? "", @"[\s，,。；;：:·•\-—_（）()【】\[\]]+", "")
            .Trim()
            .ToLowerInvariant();

    private static Dictionary<string, object?> FetchStep(
        string key,
        string url,
        string method,
        object? body = null)
    {
        var step = new Dictionary<string, object?>
        {
            ["type"] = "fetch",
            ["key"] = key,
            ["url"] = url,
            ["method"] = method
        };
        if (body != null) step["body"] = body;
        return step;
    }

    private static JsonElement ParseFetchResult(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("智联接口没有返回数据。");
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static void EnsureApiSuccess(JsonElement fetch, string action)
    {
        if (!fetch.TryGetProperty("data", out var payload))
            throw new InvalidOperationException(action + "：返回结构异常。");

        var code = payload.TryGetProperty("code", out var codeElement)
            && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : 0;

        if (code != 200)
            throw new InvalidOperationException(action + "失败：" + payload.GetRawText());

        if (payload.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(action + "失败：" + data.GetRawText());
        }
    }

    private static string First(Dictionary<string, List<string>> data, string key) =>
        data.TryGetValue(key, out var values) ? values.FirstOrDefault() ?? "" : "";

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static long GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
        return long.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static DateTimeOffset? FromUnix(long value)
    {
        if (value <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(value); }
        catch { return null; }
    }
}

public sealed class LocalWorkEntry
{
    public string CompanyName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public long StartDate { get; set; }
    public long EndDate { get; set; }
    public string Description { get; set; } = "";
}

public sealed class ZhaopinJobTypeRecommendation
{
    public string Name { get; set; } = "";
    public string ParentName { get; set; } = "";
    public string ParentValue { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class ZhaopinResumeSyncState
{
    public long ResumeId { get; set; }
    public string ResumeNumber { get; set; } = "";
    public DateTimeOffset? LastReadAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public DateTimeOffset? LocalChangedAt { get; set; }
    public long LastOnlineModifiedDate { get; set; }
    public bool NeedsSync { get; set; } = true;
    public bool ReadyForApply { get; set; }
    public string DiffSummary { get; set; } = "";
    public string RawSnapshotPath { get; set; } = "";
    public string LastSnapshotHash { get; set; } = "";
    public string LastSnapshotPath { get; set; } = "";
}

public sealed class ZhaopinResumeSnapshot
{
    public long ResumeId { get; set; }
    public string ResumeNumber { get; set; } = "";
    public long ModifiedDate { get; set; }
    public int CompletionScore { get; set; }
    public string SelfEvaluate { get; set; } = "";
    public string? SelfEvaluatePath { get; set; }
    public string PurposeTitle { get; set; } = "";
    public string PurposeJobTypeId { get; set; } = "";
    public int PurposeSalaryMin { get; set; }
    public int PurposeSalaryMax { get; set; }
    public string PurposeLocationCode { get; set; } = "";
    public string PurposeLocationName { get; set; } = "";
    public bool PurposeIndustryUnlimited { get; set; }
    public string PurposePath { get; set; } = "";
    public List<ZhaopinWorkExperience> WorkExperiences { get; set; } = new();
    public List<ZhaopinProjectExperience> ProjectExperiences { get; set; } = new();
    public List<ZhaopinProfessionalSkill> ProfessionalSkills { get; set; } = new();
    public List<ZhaopinEducationExperience> EducationExperiences { get; set; } = new();
}

public sealed class ZhaopinWorkExperience
{
    public string CompanyName { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public long StartDate { get; set; }
    public long EndDate { get; set; }
    public long RealSalary { get; set; }
    public string NewCompanyId { get; set; } = "";
    public string NewIndustry { get; set; } = "";
    public string IndustrySerial { get; set; } = "";
    public string NewJobSubType { get; set; } = "";
    public string JobTypeSerial { get; set; } = "";
    public string Workdays { get; set; } = "";
    public string DailyWage { get; set; } = "";
    public string SkillTagStandard { get; set; } = "";
    public string SkillTagCustomized { get; set; } = "";
    public string PreferenceQuestionAndAnswer { get; set; } = "";
    public string InternshipWork { get; set; } = "";
}

public sealed class ZhaopinProjectExperience
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    public long StartDate { get; set; }
    public long EndDate { get; set; }
}

public sealed class ZhaopinProfessionalSkill
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = "";
    public string UseTime { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class ZhaopinEducationExperience
{
    public string School { get; set; } = "";
    public string Major { get; set; } = "";
    public string Degree { get; set; } = "";
    public int StartYear { get; set; }
    public int EndYear { get; set; }
    public string Path { get; set; } = "";
}

public sealed class ZhaopinResumeDiff
{
    public List<ZhaopinResumeDiffItem> Items { get; set; } = new();
    public bool NeedsSync => Items.Count > 0;
    public bool HasCriticalDiff => Items.Any(x => x.Critical);

    public void Add(string section, string message, bool critical, bool autoSync)
    {
        Items.Add(new ZhaopinResumeDiffItem
        {
            Section = section,
            Message = message,
            Critical = critical,
            AutoSync = autoSync
        });
    }

    public string ToText()
    {
        if (Items.Count == 0) return "没有发现关键差异。";
        var sb = new StringBuilder();
        sb.AppendLine("本地主简历 ↔ 智联在线简历");
        sb.AppendLine(new string('=', 40));
        foreach (var item in Items)
        {
            sb.AppendLine((item.Critical ? "【关键】" : "【提示】") + " " + item.Section);
            sb.AppendLine(item.Message);
            sb.AppendLine(item.AutoSync ? "→ 可自动同步" : "→ 需要确认后处理");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    public string ToCompactText() =>
        Items.Count == 0
            ? ""
            : string.Join("；", Items.Where(x => x.Critical).Take(5).Select(x => x.Section + "：" + x.Message));
}

public sealed class ZhaopinResumeDiffItem
{
    public string Section { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Critical { get; set; }
    public bool AutoSync { get; set; }
}

public sealed class ZhaopinResumeSyncResult
{
    public int ChangedCount { get; set; }
    public int RemainingDiffCount { get; set; }
    public bool ReadyForApply { get; set; }
    public List<string> Messages { get; set; } = new();
    public List<string> Remaining { get; set; } = new();

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("本次同步更新：" + ChangedCount + " 项");
        foreach (var message in Messages) sb.AppendLine("✓ " + message);

        if (Remaining.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("仍需确认");
            foreach (var item in Remaining) sb.AppendLine("• " + item);
        }

        sb.AppendLine();
        sb.AppendLine(ReadyForApply
            ? "状态：关键字段已通过同步检查，可以投递。"
            : "状态：仍存在关键差异，已阻止自动投递。");

        return sb.ToString().Trim();
    }
}

public static class JobBackgroundService
{
    public static async Task<string> RunAsync(
        YanziActionContext context,
        bool forceScan,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        var settings = JobRepository.LoadSettings(context.ExtensionDataDirectory);

        var scan = await JobScanService.RunDailyAsync(
            context.ExtensionDataDirectory,
            forceScan,
            cancellationToken).ConfigureAwait(false);
        messages.Add(scan.Message);

        if (settings.EnableMessageWatch)
        {
            var state = JobRepository.LoadMessageState(context.ExtensionDataDirectory);
            var due = !state.LastCheckedAt.HasValue
                      || DateTimeOffset.Now - state.LastCheckedAt.Value
                         >= TimeSpan.FromMinutes(settings.MessageCheckIntervalMinutes);
            if (due)
            {
                try
                {
                    var check = await ZhaopinInteractionService.CheckMessagesAsync(
                        context,
                        context.ExtensionDataDirectory,
                        settings.AutoReplyEnabled,
                        cancellationToken).ConfigureAwait(false);
                    messages.Add(check.Message);
                }
                catch (Exception ex)
                {
                    context.Log("智联消息检查失败：" + ex.Message);
                    messages.Add("消息检查失败：" + ex.Message);
                }
            }
            else
            {
                messages.Add("消息检查未到间隔");
            }
        }

        DailyJournalService.Capture(
            context.ExtensionDataDirectory,
            "后台循环：" + string.Join("；", messages));

        return string.Join("；", messages);
    }
}

public sealed class LocalBrowserClient
{
    private readonly YanziActionContext _context;
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(130)
    };

    public LocalBrowserClient(YanziActionContext context)
    {
        _context = context;
    }

    public async Task<Dictionary<string, List<string>>> ScrapeAsync(
        string url,
        Dictionary<string, string> selectors,
        bool closeOnComplete,
        CancellationToken cancellationToken)
    {
        var data = await InvokeAsync("browser.scrape", new
        {
            url,
            selectors,
            closeOnComplete,
            timeoutSeconds = 90
        }, cancellationToken).ConfigureAwait(false);

        return ExtractTaskData(data);
    }

    public async Task<Dictionary<string, List<string>>> WorkflowAsync(
        string url,
        List<Dictionary<string, object?>> steps,
        bool closeOnComplete,
        CancellationToken cancellationToken)
    {
        var data = await InvokeAsync("browser.workflow", new
        {
            url,
            steps,
            closeOnComplete,
            timeoutSeconds = 120
        }, cancellationToken).ConfigureAwait(false);

        return ExtractTaskData(data);
    }

    public async Task<Dictionary<string, List<string>>> DirectWorkflowAsync(
        string url,
        List<Dictionary<string, object?>> steps,
        bool closeOnComplete,
        CancellationToken cancellationToken)
    {
        var baseUrl = (_context.AgentApiBaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("燕子本地 Agent API 地址不可用。");

        var requestPayload = JsonSerializer.Serialize(new
        {
            url,
            steps,
            closeOnComplete,
            timeoutSeconds = 120
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            baseUrl + "/v1/browser/execute");
        request.Headers.TryAddWithoutValidation("X-Yanzi-Token", _context.AgentApiToken);
        request.Content = new StringContent(requestPayload, Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("浏览器工作流失败：" + response.StatusCode + " " + Short(responseText));

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (!root.TryGetProperty("status", out var status)
            || !string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            var message = root.TryGetProperty("message", out var m) ? m.ToString() : "网页任务执行失败";
            throw new InvalidOperationException(message);
        }

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in data.EnumerateObject())
        {
            var values = new List<string>();
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                    values.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString());
            }
            else if (property.Value.ValueKind != JsonValueKind.Null
                     && property.Value.ValueKind != JsonValueKind.Undefined)
            {
                values.Add(property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString());
            }
            result[property.Name] = values;
        }

        return result;
    }

    private async Task<JsonElement> InvokeAsync(
        string name,
        object payload,
        CancellationToken cancellationToken)
    {
        var baseUrl = (_context.AgentApiBaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("燕子本地 Agent API 地址不可用。");

        var requestPayload = JsonSerializer.Serialize(new { name, payload });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            baseUrl + "/v1/capabilities/invoke");
        request.Headers.TryAddWithoutValidation("X-Yanzi-Token", _context.AgentApiToken);
        request.Content = new StringContent(requestPayload, Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("浏览器能力调用失败：" + response.StatusCode + " " + Short(responseText));

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.ToString()
                : "未知错误";
            throw new InvalidOperationException("浏览器能力失败：" + error);
        }

        if (!root.TryGetProperty("data", out var data))
            throw new InvalidOperationException("浏览器能力没有返回 data。");

        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("success", out var taskSuccess)
            && taskSuccess.ValueKind == JsonValueKind.False)
        {
            var error = data.TryGetProperty("message", out var message)
                ? message.ToString()
                : "网页任务执行失败";
            throw new InvalidOperationException(error);
        }

        return data.Clone();
    }

    private static Dictionary<string, List<string>> ExtractTaskData(JsonElement invocationData)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (invocationData.ValueKind != JsonValueKind.Object
            || !invocationData.TryGetProperty("data", out var taskData)
            || taskData.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in taskData.EnumerateObject())
        {
            var values = new List<string>();
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                    values.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString());
            }
            else if (property.Value.ValueKind != JsonValueKind.Null
                     && property.Value.ValueKind != JsonValueKind.Undefined)
            {
                values.Add(property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.ToString());
            }

            result[property.Name] = values;
        }

        return result;
    }

    private static string Short(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 280 ? text : text[..280] + "…";
    }
}

public static class ZhaopinInteractionService
{
    public const string MessageUrl = "https://i.zhaopin.com/im";

    private const string UnreadSelector =
        ".session-item.unread, .conversation-item.unread, .chat-list-item.unread, .im-list-item.unread, " +
        "[class*='session'][class*='unread'], [class*='conversation'][class*='unread'], " +
        "[class*='chat'][class*='unread'], [class*='item'][class*='unread']";

    private const string ChatInputSelector =
        "textarea[placeholder*='消息'], textarea[placeholder*='回复'], textarea, " +
        "[contenteditable='true'][role='textbox'], [contenteditable='true']";

    private const string SendSelector =
        "button[type='submit'], button[class*='send'], [class*='send-btn'], [class*='sendButton']";

    public static async Task OpenLoginAsync(
        YanziActionContext context,
        CancellationToken cancellationToken)
    {
        var browser = new LocalBrowserClient(context);
        await browser.ScrapeAsync(
            MessageUrl,
            new Dictionary<string, string>
            {
                ["body"] = "body|innerText"
            },
            closeOnComplete: false,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task OpenMessagesAsync(
        YanziActionContext context,
        CancellationToken cancellationToken)
    {
        var browser = new LocalBrowserClient(context);
        await browser.ScrapeAsync(
            MessageUrl,
            new Dictionary<string, string>
            {
                ["body"] = "body|innerText"
            },
            closeOnComplete: false,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<InteractionResult> ApplyAndGreetAsync(
        YanziActionContext context,
        string dataDirectory,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        var settings = JobRepository.LoadSettings(dataDirectory);
        var jobs = JobRepository.LoadJobs(dataDirectory);
        var stored = jobs.FirstOrDefault(x => x.Id == job.Id) ?? job;

        if (!ZhaopinResumeService.CanApply(dataDirectory, out var resumeReason))
        {
            stored.InteractionState = "resume_sync_required";
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.LastInteractionMessage = "投递已阻止：" + resumeReason;
            SaveJob(dataDirectory, jobs, stored);
            return new InteractionResult(false, "resume_sync_required", stored.LastInteractionMessage);
        }

        if (stored.AppliedAt.HasValue || stored.Status is "applied" or "viewed" or "contacted" or "interview" or "offer")
            return new InteractionResult(true, "already_applied", "这个岗位已经记录为已投递，不重复提交。");

        var todayApplied = jobs.Count(x => x.AppliedAt?.LocalDateTime.Date == DateTimeOffset.Now.Date);
        if (todayApplied >= settings.DailyApplyLimit)
            return new InteractionResult(false, "daily_limit",
                "今天已达到投递上限 " + settings.DailyApplyLimit + " 个，避免无效海投。");

        var browser = new LocalBrowserClient(context);

        // 投递前直接检查岗位页登录态。智联的消息中心与职位页可能处于不同域名会话，
        // 不能仅用 i.zhaopin.com/im 判断职位页是否已经登录。
        var preflight = await browser.ScrapeAsync(
            stored.Url,
            new Dictionary<string, string>
            {
                ["body"] = "body|innerText",
                ["primary"] = ".summary-planes__action button|innerText"
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var preBody = First(preflight, "body");
        if (string.IsNullOrWhiteSpace(preBody) || LooksJobPageLoggedOut(preBody))
        {
            await OpenLoginAsync(context, cancellationToken).ConfigureAwait(false);
            stored.InteractionState = "login_required";
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.LastInteractionMessage = "智联职位页未登录；已打开登录页，登录后重新点击投递。";
            SaveJob(dataDirectory, jobs, stored);
            return new InteractionResult(false, "login_required", stored.LastInteractionMessage);
        }

        Dictionary<string, List<string>> result;
        try
        {
            result = await browser.WorkflowAsync(
                stored.Url,
                new List<Dictionary<string, object?>>
                {
                    Step("wait", ("selector", ".summary-planes__action button"), ("timeout", 12000)),
                    Step("scrape", ("selectors", new Dictionary<string, string>
                    {
                        ["beforePrimary"] = ".summary-planes__action button|innerText"
                    })),
                    Step("click", ("selector", ".summary-planes__action button")),
                    Step("wait", ("timeout", 1800)),
                    Step("scrape", ("selectors", new Dictionary<string, string>
                    {
                        ["primary"] = ".summary-planes__action button|innerText",
                        ["modalTitle"] = ".deliver-greeting-modal__title|innerText",
                        ["modalContent"] = ".deliver-greeting-modal__content-text|innerText",
                        ["modalButtons"] = ".deliver-greeting-modal__btn|innerText",
                        ["modalStyle"] = ".deliver-greeting-modal|style",
                        ["body"] = "body|innerText"
                    }))
                },
                closeOnComplete: false,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stored.InteractionState = "error";
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.LastInteractionMessage = "投递流程失败：" + ex.Message;
            SaveJob(dataDirectory, jobs, stored);
            DailyJournalService.AppendEvent(dataDirectory, "apply_error", stored.Id, stored.Title, stored.LastInteractionMessage);
            return new InteractionResult(false, "error", stored.LastInteractionMessage);
        }

        var text = Flatten(result);
        var primary = First(result, "primary");
        var modalTitle = First(result, "modalTitle");
        var modalStyle = First(result, "modalStyle");
        var visibleSuccessModal =
            modalTitle.Contains("已向对方发送简历和打招呼语", StringComparison.OrdinalIgnoreCase)
            && !modalStyle.Replace(" ", "").Contains("display:none", StringComparison.OrdinalIgnoreCase);

        var success = primary.Contains("已投递", StringComparison.OrdinalIgnoreCase)
                      || primary.Contains("继续沟通", StringComparison.OrdinalIgnoreCase)
                      || visibleSuccessModal;

        if (success)
        {
            stored.Status = "applied";
            stored.AppliedAt ??= DateTimeOffset.Now;
            stored.GreetingSentAt ??= DateTimeOffset.Now;
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.InteractionState = "applied_greeted";
            stored.LastInteractionMessage = "已投递，并由智联向招聘者发送招呼语。";
            SaveJob(dataDirectory, jobs, stored);
            DailyJournalService.AppendEvent(dataDirectory, "apply", stored.Id, stored.Title, stored.LastInteractionMessage);
            return new InteractionResult(true, "applied_greeted", stored.LastInteractionMessage);
        }

        if (LooksLoggedOut(text))
        {
            stored.InteractionState = "login_required";
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.LastInteractionMessage = "投递过程中出现登录界面，请完成登录后重试。";
            SaveJob(dataDirectory, jobs, stored);
            return new InteractionResult(false, "login_required", stored.LastInteractionMessage);
        }

        stored.InteractionState = "manual_required";
        stored.LastInteractionAt = DateTimeOffset.Now;
        stored.LastInteractionMessage =
            "已经打开投递流程，但网页需要你确认简历/补充字段；当前标签页已保留，完成后可在小程序标记“已投递”。";
        SaveJob(dataDirectory, jobs, stored);
        DailyJournalService.AppendEvent(dataDirectory, "apply_manual", stored.Id, stored.Title, stored.LastInteractionMessage);
        return new InteractionResult(false, "manual_required", stored.LastInteractionMessage);
    }

    public static async Task<InteractionResult> GreetAsync(
        YanziActionContext context,
        string dataDirectory,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        var jobs = JobRepository.LoadJobs(dataDirectory);
        var stored = jobs.FirstOrDefault(x => x.Id == job.Id) ?? job;

        if (!ZhaopinResumeService.CanApply(dataDirectory, out var resumeReason))
        {
            stored.InteractionState = "resume_sync_required";
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.LastInteractionMessage = "沟通已阻止：" + resumeReason;
            SaveJob(dataDirectory, jobs, stored);
            return new InteractionResult(false, "resume_sync_required", stored.LastInteractionMessage);
        }

        var browser = new LocalBrowserClient(context);

        var preflight = await browser.ScrapeAsync(
            stored.Url,
            new Dictionary<string, string>
            {
                ["body"] = "body|innerText",
                ["chat"] = ".publisher-seo__btn.c-chat-jobs__title|innerText"
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);
        var preBody = First(preflight, "body");
        if (string.IsNullOrWhiteSpace(preBody) || LooksJobPageLoggedOut(preBody))
        {
            await OpenLoginAsync(context, cancellationToken).ConfigureAwait(false);
            return new InteractionResult(false, "login_required", "智联职位页未登录；已打开登录页。");
        }

        Dictionary<string, List<string>> result;
        try
        {
            result = await browser.WorkflowAsync(
                stored.Url,
                new List<Dictionary<string, object?>>
                {
                    Step("wait", ("selector", ".publisher-seo__btn.c-chat-jobs__title"), ("timeout", 12000)),
                    Step("click", ("selector", ".publisher-seo__btn.c-chat-jobs__title")),
                    Step("wait", ("timeout", 1400)),
                    Step("scrape", ("selectors", new Dictionary<string, string>
                    {
                        ["modalTitle"] = ".deliver-greeting-modal__title|innerText",
                        ["modalContent"] = ".deliver-greeting-modal__content-text|innerText",
                        ["chatText"] = ".c-chat-jobs__content|innerText",
                        ["body"] = "body|innerText"
                    }))
                },
                closeOnComplete: false,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stored.InteractionState = "error";
            stored.LastInteractionAt = DateTimeOffset.Now;
            stored.LastInteractionMessage = "发起沟通失败：" + ex.Message;
            SaveJob(dataDirectory, jobs, stored);
            return new InteractionResult(false, "error", stored.LastInteractionMessage);
        }

        var text = Flatten(result);
        if (LooksLoggedOut(text))
            return new InteractionResult(false, "login_required", "页面要求重新登录，已保留当前标签页。");

        var confirmed = text.Contains("已向对方发送打招呼语", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("已向对方发送简历和打招呼语", StringComparison.OrdinalIgnoreCase);

        stored.LastInteractionAt = DateTimeOffset.Now;
        if (confirmed)
        {
            stored.GreetingSentAt ??= DateTimeOffset.Now;
            stored.InteractionState = "greeted";
            stored.LastInteractionMessage = "已向招聘者发送招呼语。";
            SaveJob(dataDirectory, jobs, stored);
            DailyJournalService.AppendEvent(dataDirectory, "greet", stored.Id, stored.Title, stored.LastInteractionMessage);
            return new InteractionResult(true, "greeted", stored.LastInteractionMessage);
        }

        stored.InteractionState = "chat_opened";
        stored.LastInteractionMessage =
            "已发起“立即沟通”，但当前版本无法从页面确认消息是否真正发出；聊天标签页已保留，请确认一次。";
        SaveJob(dataDirectory, jobs, stored);
        return new InteractionResult(false, "chat_opened", stored.LastInteractionMessage);
    }

    public static async Task<MessageCheckResult> CheckMessagesAsync(
        YanziActionContext context,
        string dataDirectory,
        bool allowAutoReply,
        CancellationToken cancellationToken)
    {
        var state = JobRepository.LoadMessageState(dataDirectory);
        var settings = JobRepository.LoadSettings(dataDirectory);
        var browser = new LocalBrowserClient(context);

        var page = await browser.ScrapeAsync(
            MessageUrl,
            new Dictionary<string, string>
            {
                ["body"] = "body|innerText",
                ["unread"] = UnreadSelector + "|innerText",
                ["sessions"] = "[class*='session'], [class*='conversation'], [class*='chat-list'] li, [class*='im-list'] li|innerText",
                ["inputs"] = ChatInputSelector + "|outerHTML",
                ["sendButtons"] = SendSelector + "|innerText"
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        state.LastCheckedAt = DateTimeOffset.Now;
        var body = First(page, "body");
        if (LooksLoggedOut(body))
        {
            state.LoggedIn = false;
            state.LastResult = "login_required";
            JobRepository.SaveMessageState(dataDirectory, state);
            return new MessageCheckResult(false, false, 0, "智联当前未登录；消息监控已暂停。点击“登录智联”后即可继续。");
        }

        state.LoggedIn = true;
        var unread = Values(page, "unread")
            .Select(CleanMessage)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // 某些版本不挂 unread class，但会在会话列表中显式显示“未读/新消息”。
        if (unread.Count == 0)
        {
            unread = Values(page, "sessions")
                .Select(CleanMessage)
                .Where(x => x.Contains("未读", StringComparison.OrdinalIgnoreCase)
                            || x.Contains("新消息", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        state.LastUnreadCount = unread.Count;
        state.LastResult = unread.Count == 0 ? "no_unread" : "unread";

        if (unread.Count == 0)
        {
            JobRepository.SaveMessageState(dataDirectory, state);
            MarkContactedFromMessageBody(dataDirectory, body);
            return new MessageCheckResult(true, false, 0, "智联已登录，没有检测到新的未读招呼。");
        }

        var fingerprint = Hash(string.Join("\n---\n", unread));
        var isNew = !string.Equals(fingerprint, state.LastUnreadFingerprint, StringComparison.Ordinal);
        if (!isNew)
        {
            JobRepository.SaveMessageState(dataDirectory, state);
            return new MessageCheckResult(true, false, unread.Count, "有 " + unread.Count + " 条未读会话，但与上次检查相同。");
        }

        state.LastUnreadFingerprint = fingerprint;
        state.LastNewMessageAt = DateTimeOffset.Now;
        state.NewMessageCount++;
        MarkContactedFromMessageBody(dataDirectory, string.Join("\n", unread) + "\n" + body);

        var preview = string.Join(" / ", unread.Take(2));
        DailyJournalService.AppendEvent(dataDirectory, "hr_message", "", "", preview);
        try
        {
            context.ShowDesktopNotification(
                "求职 - HR 新消息",
                preview.Length > 180 ? preview[..180] + "…" : preview);
        }
        catch { }

        var autoReplyMessage = "";
        if (allowAutoReply
            && !state.AutoRepliedFingerprints.Contains(fingerprint, StringComparer.Ordinal))
        {
            var reply = await TryAutoReplyAsync(
                context,
                settings.ReplyTemplate,
                cancellationToken).ConfigureAwait(false);

            if (reply.Success)
            {
                state.LastAutoReplyAt = DateTimeOffset.Now;
                state.AutoReplyCount++;
                state.AutoRepliedFingerprints.Add(fingerprint);
                if (state.AutoRepliedFingerprints.Count > 300)
                    state.AutoRepliedFingerprints = state.AutoRepliedFingerprints.TakeLast(300).ToList();

                autoReplyMessage = "；已自动回复首次招呼";
                DailyJournalService.AppendEvent(dataDirectory, "auto_reply", "", "", settings.ReplyTemplate);
            }
            else
            {
                autoReplyMessage = "；自动回复未执行：" + reply.Message;
            }
        }

        JobRepository.SaveMessageState(dataDirectory, state);
        DailyJournalService.Capture(dataDirectory, "发现 HR 新消息：" + preview + autoReplyMessage);
        return new MessageCheckResult(true, true, unread.Count, "发现 " + unread.Count + " 条新招呼" + autoReplyMessage);
    }

    private static async Task<InteractionResult> TryAutoReplyAsync(
        YanziActionContext context,
        string replyText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyText))
            return new InteractionResult(false, "empty_template", "回复模板为空");

        var browser = new LocalBrowserClient(context);
        try
        {
            await browser.WorkflowAsync(
                MessageUrl,
                new List<Dictionary<string, object?>>
                {
                    Step("wait", ("selector", UnreadSelector), ("timeout", 12000)),
                    Step("click", ("selector", UnreadSelector)),
                    Step("wait", ("timeout", 900)),
                    Step("wait", ("selector", ChatInputSelector), ("timeout", 12000)),
                    Step("fill", ("selector", ChatInputSelector), ("value", replyText)),
                    Step("wait", ("timeout", 250)),
                    Step("click", ("selector", SendSelector)),
                    Step("wait", ("timeout", 800)),
                    Step("scrape", ("selectors", new Dictionary<string, string>
                    {
                        ["body"] = "body|innerText"
                    }))
                },
                closeOnComplete: true,
                cancellationToken).ConfigureAwait(false);

            return new InteractionResult(true, "replied", "已回复");
        }
        catch (Exception ex)
        {
            return new InteractionResult(false, "reply_failed", ex.Message);
        }
    }

    private static async Task<LoginCheckResult> CheckLoginAsync(
        YanziActionContext context,
        CancellationToken cancellationToken)
    {
        var browser = new LocalBrowserClient(context);
        var page = await browser.ScrapeAsync(
            MessageUrl,
            new Dictionary<string, string>
            {
                ["body"] = "body|innerText"
            },
            closeOnComplete: true,
            cancellationToken).ConfigureAwait(false);

        var body = First(page, "body");
        return new LoginCheckResult(!string.IsNullOrWhiteSpace(body) && !LooksLoggedOut(body), body);
    }

    private static void MarkContactedFromMessageBody(string dataDirectory, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;

        var jobs = JobRepository.LoadJobs(dataDirectory);
        var changed = false;
        foreach (var job in jobs.Where(x => x.Status is "applied" or "viewed" or "candidate"))
        {
            var companyHit = !string.IsNullOrWhiteSpace(job.Company)
                             && body.Contains(job.Company, StringComparison.OrdinalIgnoreCase);
            var titleHit = !string.IsNullOrWhiteSpace(job.Title)
                           && body.Contains(job.Title, StringComparison.OrdinalIgnoreCase);
            if (!companyHit && !titleHit) continue;

            job.Status = "contacted";
            job.LastFeedbackAt = DateTimeOffset.Now;
            job.LastInteractionAt = DateTimeOffset.Now;
            job.LastInteractionMessage = "消息中心检测到与该岗位/公司相关的 HR 会话。";
            changed = true;
        }

        if (changed) JobRepository.SaveJobs(dataDirectory, jobs);
    }

    private static Dictionary<string, object?> Step(string type, params (string Key, object? Value)[] values)
    {
        var step = new Dictionary<string, object?> { ["type"] = type };
        foreach (var (key, value) in values) step[key] = value;
        return step;
    }

    private static void SaveJob(string dataDirectory, List<JobRecord> jobs, JobRecord job)
    {
        var index = jobs.FindIndex(x => x.Id == job.Id);
        if (index >= 0) jobs[index] = job;
        else jobs.Add(job);
        job.UpdatedAt = DateTimeOffset.Now;
        JobRepository.SaveJobs(dataDirectory, jobs);
    }

    private static bool LooksLoggedOut(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("微信扫码快捷登录", StringComparison.OrdinalIgnoreCase)
               || (text.Contains("登录/注册", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("获取验证码", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksJobPageLoggedOut(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return text.Contains("登录/注册", StringComparison.OrdinalIgnoreCase)
               || text.Contains("登录查看完整内容", StringComparison.OrdinalIgnoreCase);
    }

    private static string Flatten(Dictionary<string, List<string>> data) =>
        string.Join("\n", data.Values.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string First(Dictionary<string, List<string>> data, string key) =>
        data.TryGetValue(key, out var values) ? values.FirstOrDefault() ?? "" : "";

    private static List<string> Values(Dictionary<string, List<string>> data, string key) =>
        data.TryGetValue(key, out var values) ? values : new List<string>();

    private static string CleanMessage(string value) =>
        Regex.Replace((value ?? "").Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? "")));
}

public static class DailyJournalService
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Capture(string dataDirectory, string reason)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dataDirectory);
            var jobs = JobRepository.LoadJobs(dataDirectory);
            var scan = JobRepository.LoadScanState(dataDirectory);
            var messages = JobRepository.LoadMessageState(dataDirectory);
            var now = DateTimeOffset.Now;
            var date = now.LocalDateTime.Date;
            var dayDir = Path.Combine(dataDirectory, "daily");
            Directory.CreateDirectory(dayDir);

            var snapshot = new DailySnapshot
            {
                Date = date.ToString("yyyy-MM-dd"),
                CapturedAt = now,
                Reason = reason ?? "",
                TotalJobs = jobs.Count,
                NewToday = jobs.Count(x => x.DiscoveredAt.LocalDateTime.Date == date),
                CandidateCount = jobs.Count(x => x.Status == "candidate"),
                AppliedToday = jobs.Count(x => x.AppliedAt?.LocalDateTime.Date == date),
                AppliedTotal = jobs.Count(x => x.AppliedAt.HasValue || x.Status is "applied" or "viewed" or "contacted" or "interview" or "offer"),
                ContactedTotal = jobs.Count(x => x.Status is "contacted" or "interview" or "offer"),
                InterviewTotal = jobs.Count(x => x.Status is "interview" or "offer"),
                OfferTotal = jobs.Count(x => x.Status == "offer"),
                RejectedTotal = jobs.Count(x => x.Status == "rejected"),
                NoResponseTotal = jobs.Count(x => x.Status == "no_response"),
                LastScanAt = scan.LastScanAt,
                LastScanDiscovered = scan.LastDiscoveredCount,
                LastRawParsed = scan.LastRawParsedCount,
                MessageLoggedIn = messages.LoggedIn,
                LastMessageCheckAt = messages.LastCheckedAt,
                LastUnreadCount = messages.LastUnreadCount,
                NewMessageCount = messages.NewMessageCount,
                AutoReplyCount = messages.AutoReplyCount,
                TopJobs = jobs
                    .Where(x => x.Status is "candidate" or "new")
                    .OrderByDescending(x => x.OverallScore)
                    .Take(12)
                    .Select(x => new DailyJobSummary
                    {
                        Id = x.Id,
                        Title = x.Title,
                        Company = x.Company,
                        Location = x.Location,
                        Salary = x.Salary,
                        Experience = x.Experience,
                        Score = x.OverallScore,
                        Status = x.Status
                    })
                    .ToList()
            };

            var path = Path.Combine(dayDir, snapshot.Date + ".json");
            WriteAtomic(path, JsonSerializer.Serialize(snapshot, Json));
        }
    }

    public static void AppendEvent(
        string dataDirectory,
        string type,
        string jobId,
        string title,
        string message)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.Now;
            var dir = Path.Combine(dataDirectory, "events");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, now.ToString("yyyy-MM") + ".jsonl");
            var item = new DailyJournalEvent
            {
                At = now,
                Type = type ?? "",
                JobId = jobId ?? "",
                Title = title ?? "",
                Message = message ?? ""
            };
            File.AppendAllText(
                path,
                JsonSerializer.Serialize(item) + Environment.NewLine,
                new UTF8Encoding(false));
        }
    }

    public static List<DailySnapshot> LoadSnapshots(string dataDirectory)
    {
        lock (Gate)
        {
            var dir = Path.Combine(dataDirectory, "daily");
            if (!Directory.Exists(dir)) return new List<DailySnapshot>();

            return Directory.GetFiles(dir, "*.json")
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    try
                    {
                        return JsonSerializer.Deserialize<DailySnapshot>(File.ReadAllText(path), Json);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(x => x != null)
                .Cast<DailySnapshot>()
                .ToList();
        }
    }

    public static List<DailyJournalEvent> LoadEvents(string dataDirectory, string date)
    {
        var list = new List<DailyJournalEvent>();
        if (!DateTime.TryParse(date, out var parsed)) return list;

        var path = Path.Combine(dataDirectory, "events", parsed.ToString("yyyy-MM") + ".jsonl");
        if (!File.Exists(path)) return list;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var item = JsonSerializer.Deserialize<DailyJournalEvent>(line, Json);
                if (item != null && item.At.LocalDateTime.ToString("yyyy-MM-dd") == date)
                    list.Add(item);
            }
            catch { }
        }

        return list.OrderBy(x => x.At).ToList();
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

public sealed class DailyReviewWindow : Window
{
    private readonly string _dataDirectory;
    private readonly ListBox _dates = new();
    private readonly TextBox _content = new();

    public DailyReviewWindow(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        Title = "求职 · 每日复盘";
        Width = 960;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        Background = BrushFrom("#0B1016");
        Foreground = BrushFrom("#F4F7FB");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Content = BuildUi();
        Loaded += (_, _) => LoadDays();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _dates.Background = BrushFrom("#121923");
        _dates.Foreground = BrushFrom("#F4F7FB");
        _dates.BorderBrush = BrushFrom("#263444");
        _dates.BorderThickness = new Thickness(1);
        _dates.SelectionChanged += (_, _) => RenderSelected();
        root.Children.Add(_dates);

        _content.IsReadOnly = true;
        _content.AcceptsReturn = true;
        _content.TextWrapping = TextWrapping.Wrap;
        _content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _content.Background = BrushFrom("#121923");
        _content.Foreground = BrushFrom("#DCE6F2");
        _content.BorderBrush = BrushFrom("#263444");
        _content.BorderThickness = new Thickness(1);
        _content.Padding = new Thickness(18);
        _content.FontSize = 12.5;
        _content.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_content, 1);
        root.Children.Add(_content);
        return root;
    }

    private void LoadDays()
    {
        var items = DailyJournalService.LoadSnapshots(_dataDirectory);
        _dates.Items.Clear();
        foreach (var item in items)
            _dates.Items.Add(item.Date);
        if (_dates.Items.Count > 0) _dates.SelectedIndex = 0;
    }

    private void RenderSelected()
    {
        if (_dates.SelectedItem is not string date) return;
        var snapshot = DailyJournalService.LoadSnapshots(_dataDirectory)
            .FirstOrDefault(x => x.Date == date);
        if (snapshot == null) return;

        var events = DailyJournalService.LoadEvents(_dataDirectory, date);
        var sb = new StringBuilder();
        sb.AppendLine(date + " 求职复盘");
        sb.AppendLine(new string('=', 42));
        sb.AppendLine("岗位池：" + snapshot.TotalJobs);
        sb.AppendLine("今日发现：" + snapshot.NewToday);
        sb.AppendLine("候选：" + snapshot.CandidateCount);
        sb.AppendLine("今日投递：" + snapshot.AppliedToday);
        sb.AppendLine("累计投递：" + snapshot.AppliedTotal);
        sb.AppendLine("HR 联系：" + snapshot.ContactedTotal);
        sb.AppendLine("面试：" + snapshot.InterviewTotal);
        sb.AppendLine("Offer：" + snapshot.OfferTotal);
        sb.AppendLine("被拒：" + snapshot.RejectedTotal);
        sb.AppendLine("无回应：" + snapshot.NoResponseTotal);
        sb.AppendLine("未读消息：" + snapshot.LastUnreadCount);
        sb.AppendLine("检测到新消息累计：" + snapshot.NewMessageCount);
        sb.AppendLine("自动回复累计：" + snapshot.AutoReplyCount);
        sb.AppendLine();
        sb.AppendLine("最后记录：" + snapshot.Reason);

        if (snapshot.TopJobs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("当天优先岗位");
            sb.AppendLine(new string('-', 42));
            foreach (var job in snapshot.TopJobs)
                sb.AppendLine(job.Score + " · " + job.Title + " · " + job.Company + " · " + job.Location + " · " + job.Salary + " · " + job.Experience);
        }

        if (events.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("事件流水");
            sb.AppendLine(new string('-', 42));
            foreach (var e in events)
                sb.AppendLine(e.At.LocalDateTime.ToString("HH:mm:ss") + " · " + e.Type + " · " + e.Title + " · " + e.Message);
        }

        _content.Text = sb.ToString();
        _content.ScrollToHome();
    }

    private static SolidColorBrush BrushFrom(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}

public sealed class DailySnapshot
{
    public string Date { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public string Reason { get; set; } = "";
    public int TotalJobs { get; set; }
    public int NewToday { get; set; }
    public int CandidateCount { get; set; }
    public int AppliedToday { get; set; }
    public int AppliedTotal { get; set; }
    public int ContactedTotal { get; set; }
    public int InterviewTotal { get; set; }
    public int OfferTotal { get; set; }
    public int RejectedTotal { get; set; }
    public int NoResponseTotal { get; set; }
    public DateTimeOffset? LastScanAt { get; set; }
    public int LastScanDiscovered { get; set; }
    public int LastRawParsed { get; set; }
    public bool MessageLoggedIn { get; set; }
    public DateTimeOffset? LastMessageCheckAt { get; set; }
    public int LastUnreadCount { get; set; }
    public int NewMessageCount { get; set; }
    public int AutoReplyCount { get; set; }
    public List<DailyJobSummary> TopJobs { get; set; } = new();
}

public sealed class DailyJobSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string Salary { get; set; } = "";
    public string Experience { get; set; } = "";
    public int Score { get; set; }
    public string Status { get; set; } = "";
}

public sealed class DailyJournalEvent
{
    public DateTimeOffset At { get; set; }
    public string Type { get; set; } = "";
    public string JobId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class MessageWatchState
{
    public DateTimeOffset? LastCheckedAt { get; set; }
    public bool LoggedIn { get; set; }
    public string LastResult { get; set; } = "";
    public int LastUnreadCount { get; set; }
    public string LastUnreadFingerprint { get; set; } = "";
    public DateTimeOffset? LastNewMessageAt { get; set; }
    public DateTimeOffset? LastAutoReplyAt { get; set; }
    public int NewMessageCount { get; set; }
    public int AutoReplyCount { get; set; }
    public List<string> AutoRepliedFingerprints { get; set; } = new();
}

public sealed record InteractionResult(bool Success, string State, string Message);
public sealed record MessageCheckResult(bool LoggedIn, bool HasNew, int UnreadCount, string Message);
public sealed record LoginCheckResult(bool LoggedIn, string Body);

public static class JobScanService
{
    private static readonly HttpClient Http = CreateHttp();

    public static async Task<JobScanResult> RunDailyAsync(string dataDirectory, bool force, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataDirectory);
        var state = JobRepository.LoadScanState(dataDirectory);
        var today = DateTimeOffset.Now.Date;
        if (!force && state.LastScanAt?.LocalDateTime.Date == today)
            return new JobScanResult(0, 0, "今天已经扫描过，等待下一轮反馈");

        var settings = JobRepository.LoadSettings(dataDirectory);
        var resume = JobRepository.LoadResume(dataDirectory);
        var existing = JobRepository.LoadJobs(dataDirectory);
        var byUrl = existing
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .GroupBy(x => NormalizeUrl(x.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var discovered = new Dictionary<string, JobRecord>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var scanStats = new List<string>();
        var requestCount = 0;
        var rawParsedCount = 0;
        var keywordCount = 0;

        foreach (var keyword in settings.SearchKeywords.Take(12))
        {
            keywordCount++;

            for (var page = 1; page <= settings.PagesPerKeyword; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var url = "https://www.zhaopin.com/sou/?kw=" + Uri.EscapeDataString(keyword);
                    if (page > 1) url += "&p=" + page.ToString(CultureInfo.InvariantCulture);

                    requestCount++;
                    var html = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
                    var parsed = ZhaopinParser.ParseSearch(html, keyword)
                        .Take(settings.MaxResultsPerKeyword)
                        .ToList();
                    rawParsedCount += parsed.Count;

                    foreach (var job in parsed)
                    {
                        // 前期调查保持全国样本，不按城市过滤；地点只在评分阶段作为轻量成本因子。
                        var key = NormalizeUrl(job.Url);
                        if (!discovered.ContainsKey(key))
                            discovered[key] = job;
                    }

                    scanStats.Add(keyword + "#" + page + "=" + parsed.Count + "/" + discovered.Count);
                    if (parsed.Count == 0) break;
                }
                catch (Exception ex)
                {
                    errors.Add(keyword + " 第" + page + "页：" + ex.Message);
                    break;
                }

                await Task.Delay(settings.PageDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        var merged = new List<JobRecord>(existing);
        var newJobs = new List<JobRecord>();
        foreach (var pair in discovered)
        {
            if (byUrl.TryGetValue(pair.Key, out var old))
            {
                CopyFreshFields(pair.Value, old);
                JobScorer.Score(old, settings, resume);
            }
            else
            {
                var job = pair.Value;
                job.Id = Guid.NewGuid().ToString("N");
                job.DiscoveredAt = DateTimeOffset.Now;
                job.UpdatedAt = job.DiscoveredAt;
                JobScorer.Score(job, settings, resume);
                newJobs.Add(job);
                merged.Add(job);
                byUrl[pair.Key] = job;
            }
        }

        var detailTargets = newJobs
            .OrderByDescending(x => x.OverallScore)
            .Take(settings.DetailFetchLimit)
            .ToList();

        foreach (var job in detailTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var html = await Http.GetStringAsync(job.Url.Replace("http://", "https://"), cancellationToken).ConfigureAwait(false);
                var detail = ZhaopinParser.ParseDetail(html);
                job.Description = detail.Description;
                job.CompanyIntro = detail.CompanyIntro;
                job.DetailFetchedAt = DateTimeOffset.Now;
                JobScorer.Score(job, settings, resume);
            }
            catch (Exception ex)
            {
                job.DetailError = ex.Message;
            }
            await Task.Delay(220, cancellationToken).ConfigureAwait(false);
        }

        foreach (var job in merged)
        {
            if (job.Status == "new" && job.OverallScore >= 68)
                job.Status = "candidate";
            else if (job.Status == "candidate" && job.OverallScore < 68)
                job.Status = "new";
            job.UpdatedAt = DateTimeOffset.Now;
        }

        JobRepository.SaveJobs(dataDirectory, merged
            .OrderByDescending(x => x.DiscoveredAt)
            .Take(1500)
            .ToList());

        state.LastScanAt = DateTimeOffset.Now;
        state.LastNewCount = newJobs.Count;
        state.LastCandidateCount = newJobs.Count(x => x.OverallScore >= 68);
        state.LastKeywordCount = keywordCount;
        state.LastRequestCount = requestCount;
        state.LastRawParsedCount = rawParsedCount;
        state.LastDiscoveredCount = discovered.Count;
        state.LastScanStats = string.Join("；", scanStats.Take(50));
        state.LastError = errors.Count == 0 ? null : string.Join("；", errors.Take(3));
        JobRepository.SaveScanState(dataDirectory, state);

        var msg = $"扫描完成：发现 {discovered.Count} 个，新增 {newJobs.Count} 个，候选 {state.LastCandidateCount} 个";
        if (errors.Count > 0) msg += $"，{errors.Count} 个关键词出现异常";
        return new JobScanResult(newJobs.Count, state.LastCandidateCount, msg);
    }

    private static void CopyFreshFields(JobRecord from, JobRecord to)
    {
        to.Title = from.Title;
        to.Salary = from.Salary;
        to.Location = from.Location;
        to.Experience = from.Experience;
        to.Education = from.Education;
        to.Company = from.Company;
        to.CompanyTags = from.CompanyTags;
        to.SkillTags = from.SkillTags;
        to.RecruiterState = from.RecruiterState;
        to.SourceKeyword = from.SourceKeyword;
    }

    private static string NormalizeUrl(string url) => (url ?? string.Empty).Trim().Replace("http://", "https://").TrimEnd('/');

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.7");
        return http;
    }
}

public static class ZhaopinParser
{
    private static readonly Regex JobLink = new(
        @"<a\s+href=""(?<url>https?://(?:www\.)?zhaopin\.com/jobdetail/[^""]+)""[^>]*class=""jobinfo__name""[^>]*>(?<title>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static IEnumerable<JobRecord> ParseSearch(string html, string sourceKeyword)
    {
        var matches = JobLink.Matches(html ?? string.Empty);
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var end = i + 1 < matches.Count ? matches[i + 1].Index : Math.Min(html.Length, m.Index + 30000);
            var length = Math.Max(0, end - m.Index);
            if (length <= 0) continue;
            var block = html.Substring(m.Index, Math.Min(length, 30000));

            var job = new JobRecord
            {
                Url = WebUtility.HtmlDecode(m.Groups["url"].Value).Replace("http://", "https://"),
                Title = Clean(m.Groups["title"].Value),
                Salary = Extract(block, @"<p\s+class=""jobinfo__salary"">(?<v>.*?)</p>"),
                Company = ExtractCompany(block),
                RecruiterState = Extract(block, @"class=""companyinfo__staff-state"">(?<v>.*?)</div>"),
                SourceKeyword = sourceKeyword
            };

            var other = Regex.Matches(block, @"<div\s+class=""jobinfo__other-info-item""[^>]*>(?<v>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Cast<Match>()
                .Select(x => Clean(x.Groups["v"].Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(4)
                .ToList();

            if (other.Count > 0) job.Location = other[0];
            if (other.Count > 1) job.Experience = other[1];
            if (other.Count > 2) job.Education = other[2];

            job.SkillTags = ExtractSectionTags(block, "jobinfo__tag", "jobinfo__other-info");
            job.CompanyTags = ExtractSectionTags(block, "companyinfo__tag", "companyinfo__staff");

            if (!string.IsNullOrWhiteSpace(job.Title) && !string.IsNullOrWhiteSpace(job.Url))
                yield return job;
        }
    }

    public static JobDetail ParseDetail(string html)
    {
        var description = Extract(html,
            @"<div\s+class=""describtion-card__detail-content[^""]*""[^>]*>(?<v>.*?)</div>");
        var intro = Extract(html,
            @"<p\s+class=""company-info__intro""[^>]*>(?<v>.*?)</p>");
        return new JobDetail(description, intro);
    }

    private static string ExtractCompany(string block)
    {
        var m = Regex.Match(block,
            @"<a[^>]*title=""(?<v>[^""]+)""[^>]*class=""companyinfo__name[^""]*""[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success) return Clean(m.Groups["v"].Value);

        return Extract(block, @"class=""companyinfo__name[^""]*""[^>]*>(?<v>.*?)</a>");
    }

    private static string ExtractSectionTags(string block, string startMarker, string endMarker)
    {
        var start = block.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        var end = block.IndexOf(endMarker, start + startMarker.Length, StringComparison.OrdinalIgnoreCase);
        if (end < 0) end = Math.Min(block.Length, start + 8000);
        var section = block.Substring(start, Math.Min(end - start, 8000));
        return string.Join(" · ",
            Regex.Matches(section, @"joblist-box__item-tag[^>]*>(?<v>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Cast<Match>()
                .Select(x => Clean(x.Groups["v"].Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16));
    }

    private static string Extract(string source, string pattern)
    {
        var m = Regex.Match(source ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? Clean(m.Groups["v"].Value) : string.Empty;
    }

    private static string Clean(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\u00A0", " ");
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @"\s*\n\s*", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}

public static class JobScorer
{
    private static readonly string[] Traditional =
    {
        "制造","工业","机械","汽车","零部件","能源","电力","光伏","环保","建筑","工程","物流","仓储","供应链",
        "零售","餐饮","食品","服装","鞋服","家居","物业","农业","交通","医疗","医药","化工","材料","门店","实体","工厂"
    };

    private static readonly string[] Transformation =
    {
        "AI应用","AI 应用","AI赋能","AI 赋能","数字化转型","智能化","大模型应用","智能体","Agent","知识库","RAG",
        "自动化","工作流","RPA","Dify","Coze","MCP","API","Prompt","提示词","业务场景","提效","数据打通","内部培训"
    };

    private static readonly string[] PureTech =
    {
        "互联网","人工智能","游戏","算法","推荐系统","搜索算法","训练","微调","CUDA","推理优化","模型训练","计算机视觉","CV算法"
    };

    private static readonly string[] BossRiskWords =
    {
        "全面负责","整体负责","独立负责","从0到1","从 0 到 1","向总经理汇报","向董事长汇报","总经理","董事长",
        "战略规划","全公司","全链路","一人","单兵","抗压","高强度","加班","随时响应","技术负责人","AI负责人","负责人",
        "专家","总监","统筹所有","全栈负责"
    };

    public static void Score(JobRecord job, JobAgentSettings settings, ResumeProfile resume)
    {
        var combined = string.Join("\n", new[]
        {
            job.Title, job.CompanyTags, job.SkillTags, job.Description, job.CompanyIntro, job.RecruiterState
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var traditionalHits = Hits(combined, Traditional);
        var transformHits = Hits(combined, Transformation);
        var pureHits = Hits(combined, PureTech);
        var skillHits = Hits(combined, settings.SkillKeywords.ToArray());
        var resumeTerms = resume.MatchTerms().ToArray();
        var resumeHits = Hits(combined, resumeTerms);

        var transition = 28 + Math.Min(48, traditionalHits * 12) + Math.Min(34, transformHits * 5) - Math.Min(34, pureHits * 9);
        if (ContainsAny(job.Title, "数字化","AI应用","AI 应用","智能体","AI赋能","AI 赋能","大模型应用")) transition += 10;
        transition = Clamp(transition);

        var roleFit = 30 + Math.Min(60, transformHits * 7 + skillHits * 6);
        if (ContainsAny(job.Title, "算法","训练","研究员","科学家")) roleFit -= 28;
        if (ContainsAny(job.Title, "应用","落地","自动化","数字化","智能体","Agent","产品")) roleFit += 12;
        roleFit = Clamp(roleFit);

        var resumeFit = 34 + Math.Min(56, resumeHits * 7);
        if (ContainsAny(job.Title, "AI应用","AI 应用","数字化","自动化","智能体","Agent","AI产品","AI 产品","AI工具","AI 工具")) resumeFit += 10;
        if (ContainsAny(job.Title, "算法","训练","研究员","科学家","CUDA")) resumeFit -= 24;

        var campusMismatch = ContainsAny(combined, "校招","应届","应届生","实习生","毕业生")
                             && !string.IsNullOrWhiteSpace(resume.YearsExperience);
        var nonTargetFunction = ContainsAny(job.Title,
            "渠道运营","内容运营","电商运营","运营专员","销售","客服","行政","人事","人力资源",
            "市场专员","讲师","培训专员","培训师","测试运维","运维技术员","采购","仓储","财务专员",
            "售后支持","招聘专员");
        var experienceFit = ExperienceFit(job.Experience, resume.YearsExperience, out var experienceCap, out var experienceReason);
        if (campusMismatch)
        {
            resumeFit -= 50;
            roleFit -= 35;
        }
        if (nonTargetFunction)
        {
            resumeFit -= 18;
            roleFit -= 30;
        }

        resumeFit = Clamp(resumeFit);
        roleFit = Clamp(roleFit);

        var locationScore = LocationScore(job.Location, settings, resume);

        var salaryComfort = SalaryComfort(job.Salary, settings, out var salaryMid);
        job.SalaryMonthlyMid = salaryMid;

        var riskReasons = new List<string>();
        var bossRisk = 8;
        foreach (var word in BossRiskWords)
        {
            if (combined.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                bossRisk += word is "总经理" or "董事长" ? 12 : 7;
                if (riskReasons.Count < 5) riskReasons.Add(word);
            }
        }
        if (ContainsAny(job.Title, "负责人","总监","专家","Leader","Lead")) bossRisk += 16;
        if (ContainsAny(job.Title, "总经理助理","董事长助理","CEO助理")) bossRisk += 22;
        if (salaryMid >= settings.HighSalaryWarning) bossRisk += 12;
        bossRisk = Clamp(bossRisk);

        var competition = 20;
        competition += pureHits * 8;
        if (salaryMid >= 25000) competition += 18;
        else if (salaryMid >= 20000) competition += 10;
        if (ContainsAny(job.Title, "高级","专家","架构师","算法")) competition += 12;
        if (combined.Contains("10000人以上", StringComparison.OrdinalIgnoreCase)) competition += 8;
        competition = Clamp(competition);

        var overall = (int)Math.Round(
            transition * 0.28 +
            resumeFit * 0.25 +
            roleFit * 0.15 +
            salaryComfort * 0.14 +
            (100 - bossRisk) * 0.08 +
            (100 - competition) * 0.05 +
            locationScore * 0.05);

        if (salaryMid > 0 && salaryMid > settings.HighSalaryWarning * 1.25) overall -= 8;

        // 反向选择企业：高薪与职责膨胀不是“加分项”，而是工作强度和预期失控的风险信号。
        // 即使业务方向极其匹配，也不能让“负责人/向老板直接汇报/全公司推动”类岗位霸占榜首。
        if (bossRisk >= 75) overall = Math.Min(overall, 66);
        else if (bossRisk >= 60) overall = Math.Min(overall, 74);
        else if (bossRisk >= 48) overall = Math.Min(overall, 82);

        if (salaryMid >= settings.HighSalaryWarning * 1.35) overall = Math.Min(overall, 62);
        else if (salaryMid >= settings.HighSalaryWarning) overall = Math.Min(overall, 76);

        if (campusMismatch) overall = Math.Min(overall, 38);
        if (nonTargetFunction) overall = Math.Min(overall, 56);
        if (experienceCap.HasValue) overall = Math.Min(overall, experienceCap.Value);

        overall = Clamp(overall);

        job.TransitionScore = transition;
        job.RoleFitScore = roleFit;
        job.ResumeMatchScore = resumeFit;
        job.ExperienceFitScore = experienceFit;
        job.LocationScore = locationScore;
        job.SalaryComfortScore = salaryComfort;
        job.BossRisk = bossRisk;
        job.CompetitionRisk = competition;
        job.OverallScore = overall;
        job.BossRiskReason = riskReasons.Count == 0 ? "暂未发现明显的职责膨胀信号" : string.Join("、", riskReasons.Distinct());

        var reasons = new List<string>();
        if (traditionalHits > 0) reasons.Add($"传统业务信号 {traditionalHits} 项");
        if (transformHits > 0) reasons.Add($"AI落地信号 {transformHits} 项");
        if (skillHits > 0) reasons.Add($"技能匹配 {skillHits} 项");
        if (resumeHits > 0) reasons.Add($"简历经历命中 {resumeHits} 项");
        if (locationScore >= 95) reasons.Add("当前城市匹配");
        if (salaryComfort >= 80) reasons.Add("薪资处于舒适区");
        if (pureHits >= 2) reasons.Add("纯技术竞争信号偏多");
        if (campusMismatch) reasons.Add("校招/应届要求与当前经历冲突");
        if (!string.IsNullOrWhiteSpace(experienceReason)) reasons.Add(experienceReason);
        if (nonTargetFunction) reasons.Add("岗位职能偏运营/销售等非目标方向");
        if (bossRisk >= 60) reasons.Add("职责/强度风险偏高");
        job.ScoreReason = reasons.Count == 0 ? "信息不足，建议打开职位详情人工判断" : string.Join("；", reasons);

        job.Recommendation = overall switch
        {
            >= 82 => "优先投递：传统转型与实际 AI 落地特征都比较明显",
            >= 72 => "值得投递：总体匹配，建议看清职责边界后推进",
            >= 62 => "值得观察：有部分匹配，但需要人工确认公司与工作强度",
            >= 50 => "低优先级：可以保留，暂不抢占每日投递名额",
            _ => "暂不建议：与当前“传统企业 AI 转型”目标偏离较大"
        };
    }

    private static int ExperienceFit(string required, string resumeYearsText, out int? cap, out string reason)
    {
        cap = null;
        reason = "";

        var resumeYears = ParseFirstInt(resumeYearsText);
        if (resumeYears <= 0 || string.IsNullOrWhiteSpace(required)) return 70;

        var text = required.Trim();
        if (ContainsAny(text, "经验不限","不限经验","不限")) return 82;

        if (ContainsAny(text, "无经验","应届","在校","实习"))
        {
            cap = 42;
            reason = "经验要求偏新人，与当前 " + resumeYears + " 年经历明显不匹配";
            return 20;
        }

        if (Regex.IsMatch(text, @"1\s*年以下|不满\s*1\s*年"))
        {
            cap = 46;
            reason = "岗位仅要求 1 年以下经验，与当前 " + resumeYears + " 年经历明显不匹配";
            return 25;
        }

        var range = Regex.Match(text, @"(?<a>\d+)\s*[-~—至]\s*(?<b>\d+)\s*年");
        if (range.Success
            && int.TryParse(range.Groups["a"].Value, out var min)
            && int.TryParse(range.Groups["b"].Value, out var max))
        {
            if (resumeYears > max)
            {
                var gap = resumeYears - max;
                if (max <= 3)
                {
                    cap = 55;
                    reason = "岗位仅要求 " + min + "-" + max + " 年经验，与当前 " + resumeYears + " 年经历差距较大";
                    return 32;
                }

                if (max <= 5 && gap >= 4)
                {
                    cap = 64;
                    reason = "岗位要求 " + min + "-" + max + " 年经验，低于当前 " + resumeYears + " 年经历";
                    return 48;
                }

                return 68;
            }

            if (resumeYears < min)
            {
                cap = Math.Min(58, 48 + resumeYears * 2);
                reason = "岗位要求 " + min + "-" + max + " 年经验，高于当前可证明年限";
                return 40;
            }

            return 100;
        }

        var plus = Regex.Match(text, @"(?<a>\d+)\s*年(?:以上|\+)");
        if (plus.Success && int.TryParse(plus.Groups["a"].Value, out var atLeast))
        {
            if (resumeYears >= atLeast) return 100;
            cap = 58;
            reason = "岗位要求至少 " + atLeast + " 年经验，高于当前可证明年限";
            return 42;
        }

        return 70;
    }

    private static int ParseFirstInt(string text)
    {
        var m = Regex.Match(text ?? "", @"\d+");
        return m.Success && int.TryParse(m.Value, out var n) ? n : 0;
    }

    private static int LocationScore(string location, JobAgentSettings settings, ResumeProfile resume)
    {
        if (string.IsNullOrWhiteSpace(location)) return 68;
        if (location.Contains("远程", StringComparison.OrdinalIgnoreCase)) return 95;
        if (!string.IsNullOrWhiteSpace(resume.CurrentCity)
            && location.Contains(resume.CurrentCity, StringComparison.OrdinalIgnoreCase)) return 100;
        if (!string.IsNullOrWhiteSpace(resume.CurrentProvince)
            && location.Contains(resume.CurrentProvince, StringComparison.OrdinalIgnoreCase)) return 88;
        if (settings.PreferredCities.Any(c => location.Contains(c, StringComparison.OrdinalIgnoreCase))) return 92;
        // 全国调查阶段不做硬过滤，异地只产生很轻的现实成本。
        return 66;
    }

    private static int SalaryComfort(string salary, JobAgentSettings settings, out int midpoint)
    {
        midpoint = ParseSalaryMidpoint(salary);
        if (midpoint <= 0) return 55;

        if (midpoint >= settings.IdealSalaryMin && midpoint <= settings.IdealSalaryMax) return 100;

        if (midpoint < settings.IdealSalaryMin)
        {
            var ratio = midpoint / (double)Math.Max(1, settings.IdealSalaryMin);
            return Clamp((int)Math.Round(45 + ratio * 45));
        }

        if (midpoint <= settings.HighSalaryWarning)
        {
            var range = Math.Max(1, settings.HighSalaryWarning - settings.IdealSalaryMax);
            var t = (midpoint - settings.IdealSalaryMax) / (double)range;
            return Clamp((int)Math.Round(95 - t * 38));
        }

        var over = midpoint - settings.HighSalaryWarning;
        return Clamp(52 - over / 500);
    }

    public static int ParseSalaryMidpoint(string salary)
    {
        if (string.IsNullOrWhiteSpace(salary) || salary.Contains("面议", StringComparison.OrdinalIgnoreCase)) return 0;
        var m = Regex.Match(salary, @"(?<a>\d+(?:\.\d+)?)\s*[-~—至]\s*(?<b>\d+(?:\.\d+)?)");
        if (!m.Success) return 0;
        if (!double.TryParse(m.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) return 0;
        if (!double.TryParse(m.Groups["b"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return 0;
        var multiplier = salary.Contains("万", StringComparison.OrdinalIgnoreCase) ? 10000d : 1d;
        if (multiplier == 1d && Math.Max(a, b) < 1000) multiplier = 1000d;
        return (int)Math.Round((a + b) / 2d * multiplier);
    }

    private static int Hits(string text, string[] terms) =>
        terms.Count(x => !string.IsNullOrWhiteSpace(x) && text.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string? text, params string[] terms) =>
        !string.IsNullOrWhiteSpace(text) && terms.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));

    private static int Clamp(int value) => Math.Max(0, Math.Min(100, value));
}

public static class JobRepository
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static List<JobRecord> LoadJobs(string dir)
    {
        lock (Gate)
        {
            var path = Path.Combine(dir, "jobs.json");
            if (!File.Exists(path)) return new List<JobRecord>();
            try
            {
                return JsonSerializer.Deserialize<List<JobRecord>>(File.ReadAllText(path), Json) ?? new List<JobRecord>();
            }
            catch
            {
                BackupCorrupt(path);
                return new List<JobRecord>();
            }
        }
    }

    public static void SaveJobs(string dir, List<JobRecord> jobs)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "jobs.json"), JsonSerializer.Serialize(jobs, Json));
        }
    }

    public static JobAgentSettings LoadSettings(string dir)
    {
        lock (Gate)
        {
            var path = Path.Combine(dir, "settings.json");
            if (!File.Exists(path))
            {
                var defaults = new JobAgentSettings();
                WriteAtomic(path, JsonSerializer.Serialize(defaults, Json));
                return defaults;
            }
            try
            {
                var settings = JsonSerializer.Deserialize<JobAgentSettings>(File.ReadAllText(path), Json) ?? new JobAgentSettings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                BackupCorrupt(path);
                return new JobAgentSettings();
            }
        }
    }

    public static void SaveSettings(string dir, JobAgentSettings settings)
    {
        lock (Gate)
        {
            settings.Normalize();
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "settings.json"), JsonSerializer.Serialize(settings, Json));
        }
    }

    public static ResumeProfile LoadResume(string dir)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "resume.json");
            if (!File.Exists(path))
            {
                var profile = new ResumeProfile();
                WriteAtomic(path, JsonSerializer.Serialize(profile, Json));
                return profile;
            }

            try
            {
                var profile = JsonSerializer.Deserialize<ResumeProfile>(File.ReadAllText(path), Json) ?? new ResumeProfile();
                profile.Normalize();
                return profile;
            }
            catch
            {
                BackupCorrupt(path);
                return new ResumeProfile();
            }
        }
    }

    public static void SaveResume(string dir, ResumeProfile profile)
    {
        lock (Gate)
        {
            profile.Normalize();
            profile.UpdatedAt = DateTimeOffset.Now;
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "resume.json"), JsonSerializer.Serialize(profile, Json));
        }
    }

    public static void RescoreJobs(string dir)
    {
        var settings = LoadSettings(dir);
        var resume = LoadResume(dir);
        var jobs = LoadJobs(dir);
        foreach (var job in jobs)
        {
            JobScorer.Score(job, settings, resume);
            if (job.Status == "new" && job.OverallScore >= 68)
                job.Status = "candidate";
            else if (job.Status == "candidate" && job.OverallScore < 68)
                job.Status = "new";
            job.UpdatedAt = DateTimeOffset.Now;
        }
        SaveJobs(dir, jobs);
    }

    public static MessageWatchState LoadMessageState(string dir)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "message-state.json");
            if (!File.Exists(path)) return new MessageWatchState();

            try
            {
                var state = JsonSerializer.Deserialize<MessageWatchState>(File.ReadAllText(path), Json)
                            ?? new MessageWatchState();
                state.AutoRepliedFingerprints ??= new List<string>();
                return state;
            }
            catch
            {
                BackupCorrupt(path);
                return new MessageWatchState();
            }
        }
    }

    public static void SaveMessageState(string dir, MessageWatchState state)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dir);
            state.AutoRepliedFingerprints ??= new List<string>();
            WriteAtomic(Path.Combine(dir, "message-state.json"), JsonSerializer.Serialize(state, Json));
        }
    }

    public static JobScanState LoadScanState(string dir)
    {
        lock (Gate)
        {
            var path = Path.Combine(dir, "scan-state.json");
            if (!File.Exists(path)) return new JobScanState();
            try
            {
                return JsonSerializer.Deserialize<JobScanState>(File.ReadAllText(path), Json) ?? new JobScanState();
            }
            catch
            {
                BackupCorrupt(path);
                return new JobScanState();
            }
        }
    }

    public static void SaveScanState(string dir, JobScanState state)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "scan-state.json"), JsonSerializer.Serialize(state, Json));
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static void BackupCorrupt(string path)
    {
        try
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, backup, false);
        }
        catch { }
    }
}

public sealed class JobAgentSettings
{
    public List<string> SearchKeywords { get; set; } = DefaultKeywords();
    public List<string> PreferredCities { get; set; } = new();
    public int IdealSalaryMin { get; set; } = 4500;
    public int IdealSalaryMax { get; set; } = 6500;
    public int HighSalaryWarning { get; set; } = 10000;
    public int MaxResultsPerKeyword { get; set; } = 20;
    public int PagesPerKeyword { get; set; } = 3;
    public int PageDelayMs { get; set; } = 260;
    public int DetailFetchLimit { get; set; } = 36;
    public bool EnableMessageWatch { get; set; } = true;
    public bool AutoReplyEnabled { get; set; } = true;
    public int DailyApplyLimit { get; set; } = 5;
    public int MessageCheckIntervalMinutes { get; set; } = 60;
    public string OnlinePurposeJobTitle { get; set; } = "AI产品经理";
    public string OnlinePurposeJobTypeId { get; set; } = "3000100210000";
    public int OnlinePurposeSalaryMin { get; set; } = 5000;
    public int OnlinePurposeSalaryMax { get; set; } = 6000;
    public bool OnlinePurposeIndustryUnlimited { get; set; } = true;
    public string ReplyTemplate { get; set; } = "您好，感谢联系。我对贵司的 AI 应用落地/数字化转型方向比较感兴趣。目前已离职，可随时到岗。我主要做 AI 应用、Agent 和自动化工作流落地，也有传统企业设计和产品经历。方便的话可以告诉我这个岗位目前最希望解决的业务问题和工作地点，我可以结合实际案例具体聊聊。";
    public List<string> SkillKeywords { get; set; } = new()
    {
        "Agent","智能体","MCP","RAG","Tool Calling","Function Calling","Skill","Python","C#","自动化","工作流",
        "API","Prompt","Dify","Coze","RPA","知识库","大模型应用","AI剪辑","剪映","产品经理","数字化转型","企业AI落地"
    };

    public static List<string> DefaultKeywords() => new()
    {
        "AI应用", "AI赋能", "大模型应用", "AI自动化", "智能体", "数字化转型 AI",
        "AI应用专员", "AI工具", "AI产品经理", "AI工作流", "AI数字化", "AI剪辑"
    };

    public void Normalize()
    {
        SearchKeywords ??= DefaultKeywords();
        PreferredCities ??= new List<string>();
        SkillKeywords ??= new List<string>();
        ReplyTemplate ??= "";
        SearchKeywords = SearchKeywords.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        if (SearchKeywords.Count == 0) SearchKeywords = DefaultKeywords();
        PreferredCities = PreferredCities.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        SkillKeywords = SkillKeywords.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IdealSalaryMin = Math.Max(0, IdealSalaryMin);
        IdealSalaryMax = Math.Max(IdealSalaryMin, IdealSalaryMax);
        HighSalaryWarning = Math.Max(IdealSalaryMax, HighSalaryWarning);
        MaxResultsPerKeyword = Math.Max(5, Math.Min(20, MaxResultsPerKeyword));
        PagesPerKeyword = Math.Max(1, Math.Min(6, PagesPerKeyword));
        PageDelayMs = Math.Max(150, Math.Min(2000, PageDelayMs));
        DetailFetchLimit = Math.Max(6, Math.Min(80, DetailFetchLimit));
        DailyApplyLimit = Math.Max(1, Math.Min(30, DailyApplyLimit));
        MessageCheckIntervalMinutes = Math.Max(15, Math.Min(360, MessageCheckIntervalMinutes));
        OnlinePurposeJobTitle ??= "AI产品经理";
        OnlinePurposeJobTypeId ??= "3000100210000";
        OnlinePurposeSalaryMin = Math.Max(0, OnlinePurposeSalaryMin);
        OnlinePurposeSalaryMax = Math.Max(OnlinePurposeSalaryMin, OnlinePurposeSalaryMax);
        if (string.IsNullOrWhiteSpace(ReplyTemplate))
            ReplyTemplate = "您好，感谢联系。我对贵司的 AI 应用落地/数字化转型方向比较感兴趣。目前已离职，可随时到岗。方便的话可以告诉我岗位当前最希望解决的业务问题和工作地点，我们可以具体聊聊。";
    }
}

public sealed class ResumeProfile
{
    public string Name { get; set; } = "";
    public string YearsExperience { get; set; } = "";
    public string EducationLevel { get; set; } = "";
    public string EmploymentStatus { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string CurrentCity { get; set; } = "";
    public string CurrentProvince { get; set; } = "";
    public string Headline { get; set; } = "";
    public string PersonalAdvantage { get; set; } = "";
    public string ExpectedPosition { get; set; } = "";
    public string WorkExperience { get; set; } = "";
    public string ProjectExperience { get; set; } = "";
    public string Education { get; set; } = "";
    public string Portfolio { get; set; } = "";
    public string Skills { get; set; } = "";
    public string Certificates { get; set; } = "";
    public string Volunteer { get; set; } = "";
    public string RawImportedText { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public void Normalize()
    {
        Name ??= "";
        YearsExperience ??= "";
        EducationLevel ??= "";
        EmploymentStatus ??= "";
        Phone ??= "";
        Email ??= "";
        CurrentCity ??= "";
        CurrentProvince ??= "";
        Headline ??= "";
        PersonalAdvantage ??= "";
        ExpectedPosition ??= "";
        WorkExperience ??= "";
        ProjectExperience ??= "";
        Education ??= "";
        Portfolio ??= "";
        Skills ??= "";
        Certificates ??= "";
        Volunteer ??= "";
        RawImportedText ??= "";
    }

    public IEnumerable<string> MatchTerms()
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AI应用","AI 应用","Agent","智能体","Skill","自动化","工作流","企业AI","企业 AI",
            "数字化","产品经理","B端","B 端","用户调研","剪映","AI剪辑","AI 剪辑","视频剪辑",
            "API","Prompt","设计","UI","H5","咨询","落地","提效"
        };

        foreach (var token in Regex.Split(Skills ?? "", @"[，,、;；/|\r\n]+"))
        {
            var t = token.Trim();
            if (t.Length >= 2 && t.Length <= 30) terms.Add(t);
        }

        foreach (var token in Regex.Split(ExpectedPosition ?? "", @"[，,、;；/|\r\n]+"))
        {
            var t = token.Trim();
            if (t.Length >= 2 && t.Length <= 30) terms.Add(t);
        }

        return terms;
    }

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Name)) sb.AppendLine(Name);
        var meta = string.Join("  ", new[] { YearsExperience, EducationLevel, EmploymentStatus }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(meta)) sb.AppendLine(meta);
        var contact = string.Join("  ", new[] { Phone, Email, CurrentCity }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(contact)) sb.AppendLine(contact);
        if (!string.IsNullOrWhiteSpace(Headline)) sb.AppendLine(Headline);
        sb.AppendLine();

        AppendSection(sb, "个人优势", PersonalAdvantage);
        AppendSection(sb, "期望职位", ExpectedPosition);
        AppendSection(sb, "工作经历", WorkExperience);
        AppendSection(sb, "项目经历", ProjectExperience);
        AppendSection(sb, "教育经历", Education);
        AppendSection(sb, "技能", Skills);
        AppendSection(sb, "作品/主页", Portfolio);
        AppendSection(sb, "资格证书", Certificates);
        AppendSection(sb, "志愿者经历", Volunteer);

        if (!string.IsNullOrWhiteSpace(RawImportedText))
            AppendSection(sb, "导入原文", RawImportedText);

        return sb.ToString().Trim();
    }

    private static void AppendSection(StringBuilder sb, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.AppendLine("【" + title + "】");
        sb.AppendLine(body.Trim());
        sb.AppendLine();
    }
}

public sealed class JobRecord
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "智联";
    public string SourceKeyword { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public string Salary { get; set; } = "";
    public int SalaryMonthlyMid { get; set; }
    public string Location { get; set; } = "";
    public string Experience { get; set; } = "";
    public string Education { get; set; } = "";
    public string Company { get; set; } = "";
    public string CompanyTags { get; set; } = "";
    public string SkillTags { get; set; } = "";
    public string RecruiterState { get; set; } = "";
    public string Description { get; set; } = "";
    public string CompanyIntro { get; set; } = "";
    public string? DetailError { get; set; }

    public int TransitionScore { get; set; }
    public int RoleFitScore { get; set; }
    public int ResumeMatchScore { get; set; }
    public int ExperienceFitScore { get; set; }
    public int LocationScore { get; set; }
    public int SalaryComfortScore { get; set; }
    public int BossRisk { get; set; }
    public int CompetitionRisk { get; set; }
    public int OverallScore { get; set; }
    public string ScoreReason { get; set; } = "";
    public string BossRiskReason { get; set; } = "";
    public string Recommendation { get; set; } = "";

    public string Status { get; set; } = "new";
    public string? UserNote { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DetailFetchedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? GreetingSentAt { get; set; }
    public DateTimeOffset? LastFeedbackAt { get; set; }
    public DateTimeOffset? LastInteractionAt { get; set; }
    public string InteractionState { get; set; } = "";
    public string LastInteractionMessage { get; set; } = "";
}

public sealed class JobScanState
{
    public DateTimeOffset? LastScanAt { get; set; }
    public int LastNewCount { get; set; }
    public int LastCandidateCount { get; set; }
    public int LastKeywordCount { get; set; }
    public int LastRequestCount { get; set; }
    public int LastRawParsedCount { get; set; }
    public int LastDiscoveredCount { get; set; }
    public string? LastScanStats { get; set; }
    public string? LastError { get; set; }
}

public sealed record JobScanResult(int NewCount, int CandidateCount, string Message);
public sealed record JobDetail(string Description, string CompanyIntro);
