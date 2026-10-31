using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using SelectionMode = System.Windows.Controls.SelectionMode;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Binding = System.Windows.Data.Binding;
using Clipboard = System.Windows.Clipboard;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

/// <summary>Read-only host dispatch diagnostics. Never executes miniapps while opened.</summary>
public sealed class DispatchCenterWindow : Window
{
    private readonly Func<IReadOnlyDictionary<string, DateTime>> _nextRuns;
    private readonly TextBox _query = new() { Margin = new Thickness(12, 10, 12, 7), Height = 32 };
    private readonly ListView _list = new() { Margin = new Thickness(12, 4, 12, 8) };
    private readonly TextBlock _footer = new() { Margin = new Thickness(12, 2, 12, 10) };
    private List<Row> _rows = [];

    private sealed record Row(string Name, string ExtensionId, string Trigger,
        string State, string Reason, string NextRun)
    {
        public string Diagnostic => $"{Name} ({ExtensionId})\n事件：{Trigger}\n状态：{State}\n原因：{Reason}\n下次执行：{NextRun}";
    }

    public DispatchCenterWindow(Func<IReadOnlyDictionary<string, DateTime>> nextRuns)
    {
        _nextRuns = nextRuns;
        Title = "燕子 · 调度中心";
        Width = 980;
        Height = 610;
        MinWidth = 680;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(23, 27, 34));
        Foreground = Brushes.White;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new DockPanel { Margin = new Thickness(12, 12, 12, 0) };
        var header = new TextBlock { Text = "任务与事件", FontSize = 19, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var refresh = new Button { Content = "刷新", Width = 78, Height = 30 };
        refresh.Click += (_, _) => Refresh();
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);
        top.Children.Add(header);
        Grid.SetRow(top, 0);
        layout.Children.Add(top);

        _query.Text = "";
        _query.ToolTip = "搜索小程序名称、事件、执行状态或原因";
        _query.TextChanged += (_, _) => Filter();
        Grid.SetRow(_query, 1);
        layout.Children.Add(_query);

        var view = new GridView();
        foreach (var (label, binding, width) in new[] {
            ("小程序", "Name", 145d), ("事件", "Trigger", 155d),
            ("状态", "State", 100d), ("原因", "Reason", 300d), ("下次运行", "NextRun", 160d)
        })
        {
            view.Columns.Add(new GridViewColumn { Header = label,
                DisplayMemberBinding = new Binding(binding), Width = width });
        }
        _list.View = view;
        _list.Background = new SolidColorBrush(Color.FromRgb(33, 38, 47));
        _list.Foreground = Brushes.White;
        _list.SelectionMode = SelectionMode.Single;
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "复制调度诊断上下文" };
        copy.Click += (_, _) => CopyDiagnostic();
        menu.Items.Add(copy);
        _list.ContextMenu = menu;
        _list.PreviewMouseRightButtonDown += (_, e) =>
        {
            var current = e.OriginalSource as DependencyObject;
            while (current is not null && current is not ListViewItem)
                current = VisualTreeHelper.GetParent(current);
            if (current is ListViewItem item) item.IsSelected = true;
        };
        _list.MouseDoubleClick += (_, _) => CopyDiagnostic();
        Grid.SetRow(_list, 2);
        layout.Children.Add(_list);

        _footer.Foreground = new SolidColorBrush(Color.FromRgb(169, 181, 194));
        Grid.SetRow(_footer, 3);
        layout.Children.Add(_footer);
        Content = layout;
        Loaded += (_, _) => Refresh();
    }

    private void CopyDiagnostic()
    {
        if (_list.SelectedItem is Row selected)
            Clipboard.SetText(selected.Diagnostic);
    }

    private void Filter()
    {
        var search = _query.Text.Trim();
        var result = string.IsNullOrEmpty(search) ? _rows
            : _rows.Where(x => x.Diagnostic.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = result;
        _footer.Text = $"共 {_rows.Count} 个调度项 · 显示 {result.Count} 个。右键复制诊断信息。仅显示记录，不会启动小程序。";
    }

    private void Refresh()
    {
        var latest = HostDispatchJournal.Shared.Snapshot()
            .GroupBy(x => (x.ExtensionId, x.EventName))
            .Select(x => x.OrderByDescending(y => y.UpdatedUtc).First()).ToArray();
        var nextRuns = _nextRuns();
        var rows = new List<Row>();
        foreach (var item in LocalExtensionCatalog.LoadEntries())
        {
            var manifest = item.Manifest;
            var triggers = new List<string>();
            if (manifest.Startup?.Idle?.Enabled == true) triggers.Add("闲时");
            if (!string.IsNullOrWhiteSpace(manifest.Startup?.Schedule)) triggers.Add("Cron");
            if (manifest.Events?.BeforeExit?.Enabled == true) triggers.Add("退出前");
            triggers.AddRange(manifest.Events?.Subscribe ?? []);
            if (triggers.Count == 0) continue;
            var entries = latest.Where(x => x.ExtensionId.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            string cronNext = nextRuns.TryGetValue(manifest.Id, out var upcoming) ? upcoming.ToString("yyyy-MM-dd HH:mm") : "—";
            foreach (var trigger in triggers.Distinct())
            {
                string eventName = trigger switch { "闲时" => "host.idle", "Cron" => "host.schedule",
                    "退出前" => "host.beforeExit", _ => trigger };
                var record = entries.FirstOrDefault(x => x.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase));
                var reason = record?.Detail ?? "未收到此事件";
                var state = record?.State ?? "waiting";
                var next = trigger == "Cron" ? cronNext : "—";
                if (trigger == "闲时")
                {
                    var idlePath = Path.Combine(ExtensionStorageService.GetExtensionStorageDirectoryPath(manifest.Id),
                        "idle-trigger-status.json");
                    try
                    {
                        if (File.Exists(idlePath))
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(idlePath));
                            var status = doc.RootElement;
                            if (status.TryGetProperty("reason", out var r)) reason = r.GetString() ?? reason;
                            if (status.TryGetProperty("nextEligibleAt", out var n) && n.ValueKind == JsonValueKind.String)
                                next = n.GetDateTimeOffset().LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or FormatException)
                    { reason = "闲时诊断数据暂不可读"; }
                }
                rows.Add(new Row(manifest.Name, manifest.Id, trigger, state, reason, next));
            }
        }
        _rows = rows.OrderBy(x => x.Name).ThenBy(x => x.Trigger).ToList();
        Filter();
    }
}
