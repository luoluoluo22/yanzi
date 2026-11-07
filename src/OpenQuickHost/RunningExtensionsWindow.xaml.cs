using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class RunningExtensionsWindow : Window
{
    private readonly ObservableCollection<RunningExtensionItemViewModel> _items = [];
    private readonly Dictionary<string, CommandItem?> _commandCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };
    private readonly DispatcherTimer _refreshDebounceTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    private bool _isClosed;
    private bool _refreshInFlight;
    private bool _refreshQueued;
    private int _registryRefreshPosted;
    private bool _catalogDirty = true;
    private string _refreshReason = "startup";

    public RunningExtensionsWindow()
    {
        InitializeComponent();
        // Resolve shared styles per window, preserving the user's selected theme.
        var theme = AppSettingsStore.Load().ThemeMode;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(this,
            string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light : Yanzi.UI.Wpf.YanziTheme.Dark);
        ExtensionListView.ItemsSource = _items;
        Loaded += RunningExtensionsWindow_Loaded;
        Closed += RunningExtensionsWindow_Closed;
        _refreshTimer.Tick += (_, _) => ScheduleRefresh("periodic");
        _refreshDebounceTimer.Tick += (_, _) =>
        {
            _refreshDebounceTimer.Stop();
            _ = RefreshItemsAsync();
        };
    }

    private void RunningExtensionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RunningExtensionRegistry.Changed += RunningExtensionRegistry_Changed;
        ScheduleRefresh("loaded", reloadCatalog: true);
        _refreshTimer.Start();
    }

    private void RunningExtensionsWindow_Closed(object? sender, EventArgs e)
    {
        _isClosed = true;
        _refreshTimer.Stop();
        _refreshDebounceTimer.Stop();
        RunningExtensionRegistry.Changed -= RunningExtensionRegistry_Changed;
    }

    private void RunningExtensionRegistry_Changed(object? sender, EventArgs e)
    {
        // This notification may originate on an extension worker thread.
        // Never synchronously wait on the UI dispatcher from that worker.
        if (_isClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        if (Interlocked.Exchange(ref _registryRefreshPosted, 1) != 0)
            return;

        try
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _registryRefreshPosted, 0);
                ScheduleRefresh("registry-change");
            }), DispatcherPriority.Background);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _registryRefreshPosted, 0);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        ScheduleRefresh("manual", reloadCatalog: true);
    }

    private void TerminateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid instanceId })
            return;

        var target = _items.FirstOrDefault(item => item.InstanceId == instanceId);
        if (target == null)
        {
            ScheduleRefresh("terminate-stale");
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确定要强制结束扩展“{target.Title}”吗？",
            "结束扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        var success = RunningExtensionRegistry.TryTerminate(instanceId, out var message);
        if (!success)
        {
            System.Windows.MessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(message) ? "结束小程序进程失败。" : message,
                "操作失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        ScheduleRefresh("terminate");
    }

    private void ScheduleRefresh(string reason, bool reloadCatalog = false)
    {
        if (_isClosed || !IsLoaded)
            return;

        _catalogDirty |= reloadCatalog;
        _refreshReason = reason;
        // Coalesce timer ticks, rapid extension events and manual refresh requests.
        _refreshDebounceTimer.Stop();
        _refreshDebounceTimer.Start();
    }

    private async Task RefreshItemsAsync()
    {
        if (_isClosed)
            return;
        if (_refreshInFlight)
        {
            _refreshQueued = true;
            return;
        }

        _refreshInFlight = true;
        try
        {
            do
            {
                _refreshQueued = false;
                var reason = _refreshReason;
                var catalogDirty = _catalogDirty;
                _catalogDirty = false;
                var total = Stopwatch.StartNew();
                var stage = Stopwatch.StartNew();

                // Registry callbacks can probe processes and managed worker state.
                // Keep these operations off the WPF UI thread.
                var snapshot = await Task.Run(RunningExtensionRegistry.GetSnapshot);
                var snapshotMs = stage.ElapsedMilliseconds;
                if (_isClosed)
                    return;

                var missingMetadata = snapshot.Any(item => !_commandCache.ContainsKey(item.ExtensionId));
                var sameItems = snapshot.Count == _items.Count &&
                    snapshot.Select((info, index) => _items[index].Info == info).All(static same => same);

                long catalogMs = 0;
                if (!catalogDirty && !missingMetadata && sameItems)
                {
                    if (snapshotMs >= 120)
                        HostAssets.AppendLog($"Running extensions refresh slow snapshot: reason={reason}, snapshotMs={snapshotMs}, count={snapshot.Count}");
                    continue;
                }

                Dictionary<string, LocalExtensionCatalogEntry>? entries = null;
                if (snapshot.Count > 0 && (catalogDirty || missingMetadata))
                {
                    var ids = snapshot.Select(static item => item.ExtensionId)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    stage.Restart();
                    // Read manifests in the background. Create WPF icon/brush
                    // objects only for visible running entries, on the UI thread.
                    entries = await Task.Run(() => LocalExtensionCatalog.LoadEntries()
                        .Where(entry => ids.Contains(entry.Manifest.Id) &&
                            (entry.Manifest.DevicePolicy ?? new LocalExtensionDevicePolicy())
                                .AllowsDevice(ExtensionDevicePolicy.CurrentDeviceId))
                        .GroupBy(entry => entry.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase));
                    catalogMs = stage.ElapsedMilliseconds;
                    if (_isClosed)
                        return;
                }

                stage.Restart();
                if (catalogDirty)
                    _commandCache.Clear();

                if (entries != null)
                {
                    foreach (var item in snapshot)
                    {
                        if (_commandCache.ContainsKey(item.ExtensionId))
                            continue;

                        _commandCache[item.ExtensionId] = entries.TryGetValue(item.ExtensionId, out var entry)
                            ? LocalExtensionCatalog.CreateCommand(entry)
                            : null;
                    }
                }

                var changes = ApplySnapshot(snapshot);
                var uiMs = stage.ElapsedMilliseconds;
                total.Stop();
                if (changes > 0 || catalogDirty || snapshotMs >= 120 || catalogMs >= 120 ||
                    uiMs >= 50 || total.ElapsedMilliseconds >= 250)
                {
                    HostAssets.AppendLog(
                        $"Running extensions refresh: reason={reason}, count={snapshot.Count}, changes={changes}, " +
                        $"snapshotMs={snapshotMs}, catalogMs={catalogMs}, uiMs={uiMs}, totalMs={total.ElapsedMilliseconds}");
                }
            }
            while (_refreshQueued && !_isClosed);
        }
        catch (Exception ex)
        {
            _catalogDirty = true;
            HostAssets.AppendLog($"Running extensions refresh failed: {ex}");
        }
        finally
        {
            _refreshInFlight = false;
            if (_refreshQueued && !_isClosed)
                ScheduleRefresh("queued-after-refresh");
        }
    }

    private int ApplySnapshot(IReadOnlyList<RunningExtensionInfo> snapshot)
    {
        var previousById = _items.ToDictionary(static item => item.InstanceId);
        var desired = new List<RunningExtensionItemViewModel>(snapshot.Count);

        foreach (var info in snapshot)
        {
            _commandCache.TryGetValue(info.ExtensionId, out var command);
            if (previousById.TryGetValue(info.InstanceId, out var current) &&
                current.Info == info && ReferenceEquals(current.Command, command))
            {
                desired.Add(current);
            }
            else
            {
                desired.Add(new RunningExtensionItemViewModel(info, command));
            }
        }

        var changes = 0;
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < _items.Count && ReferenceEquals(_items[i], desired[i]))
                continue;

            var existingIndex = _items.IndexOf(desired[i]);
            if (existingIndex >= 0)
                _items.Move(existingIndex, i);
            else
                _items.Insert(i, desired[i]);
            changes++;
        }

        while (_items.Count > desired.Count)
        {
            _items.RemoveAt(_items.Count - 1);
            changes++;
        }

        var summary = $"当前共 {_items.Count} 个正在运行的小程序";
        if (SummaryTextBlock.Text != summary)
            SummaryTextBlock.Text = summary;
        var emptyVisibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (EmptyStateTextBlock.Visibility != emptyVisibility)
            EmptyStateTextBlock.Visibility = emptyVisibility;

        return changes;
    }

    private sealed class RunningExtensionItemViewModel
    {
        private static readonly System.Windows.Media.Brush FallbackAccentBrush =
            (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF4B5563")!;

        public RunningExtensionItemViewModel(RunningExtensionInfo info, CommandItem? command)
        {
            Info = info;
            InstanceId = info.InstanceId;
            Title = info.Title;
            ExtensionId = info.ExtensionId;
            ProcessId = info.ProcessId > 0 ? info.ProcessId.ToString() : "宿主内";
            Runtime = info.Runtime;
            LaunchSource = info.LaunchSource;
            StartedAtText = info.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            Command = command;
        }

        public RunningExtensionInfo Info { get; }
        public Guid InstanceId { get; }
        public string Title { get; }
        public string ExtensionId { get; }
        public string ProcessId { get; }
        public string Runtime { get; }
        public string LaunchSource { get; }
        public string StartedAtText { get; }
        public CommandItem? Command { get; }

        public ImageSource? IconSource => Command?.IconSource;
        public Geometry? VectorIcon => Command?.VectorIcon;
        public string DisplayGlyph => Command?.DisplayGlyph ?? "E";
        public bool HasImageIcon => Command?.HasImageIcon ?? false;
        public bool HasVectorIcon => Command?.HasVectorIcon ?? false;
        public bool UseGlyphIcon => Command == null || Command.UseGlyphIcon;
        public System.Windows.Media.Brush AccentBrush => Command?.AccentBrush ?? FallbackAccentBrush;
    }
}
