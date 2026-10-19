using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class RunningExtensionsWindow : Window
{
    private readonly ObservableCollection<RunningExtensionItemViewModel> _items = [];
    private IReadOnlyList<CommandItem> _commands = [];
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public RunningExtensionsWindow()
    {
        InitializeComponent();
        var theme = MainWindow.Instance is { } mainWindow
            ? Yanzi.UI.Wpf.YanziUi.GetTheme(mainWindow)
            : string.Equals(AppSettingsStore.Load().ThemeMode, "Light", StringComparison.OrdinalIgnoreCase)
                ? Yanzi.UI.Wpf.YanziTheme.Light
                : Yanzi.UI.Wpf.YanziTheme.Dark;
        Yanzi.UI.Wpf.YanziUi.ApplyTo(this, theme);
        _commands = LocalExtensionCatalog.LoadCommands();
        ExtensionListView.ItemsSource = _items;
        Loaded += RunningExtensionsWindow_Loaded;
        Closed += RunningExtensionsWindow_Closed;
        _refreshTimer.Tick += (_, _) => RefreshItems();
    }

    private void RunningExtensionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RunningExtensionRegistry.Changed += RunningExtensionRegistry_Changed;
        RefreshItems();
        _refreshTimer.Start();
    }

    private void RunningExtensionsWindow_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        RunningExtensionRegistry.Changed -= RunningExtensionRegistry_Changed;
    }

    private void RunningExtensionRegistry_Changed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => RefreshItems(reloadCommands: true));
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshItems(reloadCommands: true);
    }

    private void TerminateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid instanceId })
        {
            return;
        }

        var target = _items.FirstOrDefault(item => item.InstanceId == instanceId);
        if (target == null)
        {
            RefreshItems();
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"确定要强制结束扩展“{target.Title}”吗？",
            "结束扩展",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

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
        RefreshItems();
    }

    private void RefreshItems(bool reloadCommands = false)
    {
        if (reloadCommands)
        {
            _commands = LocalExtensionCatalog.LoadCommands();
        }

        var snapshot = RunningExtensionRegistry.GetSnapshot();
        var desired = snapshot
            .Select(info => (
                Info: info,
                Command: _commands.FirstOrDefault(command =>
                    string.Equals(command.ExtensionId, info.ExtensionId, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var desiredIds = desired.Select(item => item.Info.InstanceId).ToHashSet();

        // Reconcile by instance identity instead of clearing and rebuilding the entire
        // ObservableCollection every second. Keeping existing row containers alive
        // preserves the ListView scroll position during ordinary status refreshes.
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            if (!desiredIds.Contains(_items[index].InstanceId))
            {
                _items.RemoveAt(index);
            }
        }

        for (var targetIndex = 0; targetIndex < desired.Length; targetIndex++)
        {
            var target = desired[targetIndex];
            var existingIndex = -1;
            for (var index = 0; index < _items.Count; index++)
            {
                if (_items[index].InstanceId == target.Info.InstanceId)
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                _items.Insert(Math.Min(targetIndex, _items.Count),
                    new RunningExtensionItemViewModel(target.Info, target.Command));
                continue;
            }

            var existing = _items[existingIndex];
            existing.Update(target.Info, target.Command);
            if (existingIndex != targetIndex)
            {
                _items.Move(existingIndex, targetIndex);
            }
        }

        SummaryTextBlock.Text = $"当前共 {_items.Count} 个正在运行的小程序";
        EmptyStateTextBlock.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private sealed class RunningExtensionItemViewModel : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _extensionId = string.Empty;
        private string _processId = string.Empty;
        private string _runtime = string.Empty;
        private string _launchSource = string.Empty;
        private string _startedAtText = string.Empty;
        private CommandItem? _command;

        public RunningExtensionItemViewModel(RunningExtensionInfo info, CommandItem? command)
        {
            InstanceId = info.InstanceId;
            Update(info, command);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public Guid InstanceId { get; }

        public string Title
        {
            get => _title;
            private set => SetField(ref _title, value);
        }

        public string ExtensionId
        {
            get => _extensionId;
            private set => SetField(ref _extensionId, value);
        }

        public string ProcessId
        {
            get => _processId;
            private set => SetField(ref _processId, value);
        }

        public string Runtime
        {
            get => _runtime;
            private set => SetField(ref _runtime, value);
        }

        public string LaunchSource
        {
            get => _launchSource;
            private set => SetField(ref _launchSource, value);
        }

        public string StartedAtText
        {
            get => _startedAtText;
            private set => SetField(ref _startedAtText, value);
        }

        public CommandItem? Command
        {
            get => _command;
            private set
            {
                if (!SetField(ref _command, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(IconSource));
                OnPropertyChanged(nameof(VectorIcon));
                OnPropertyChanged(nameof(DisplayGlyph));
                OnPropertyChanged(nameof(HasImageIcon));
                OnPropertyChanged(nameof(HasVectorIcon));
                OnPropertyChanged(nameof(UseGlyphIcon));
                OnPropertyChanged(nameof(AccentBrush));
            }
        }

        public ImageSource? IconSource => Command?.IconSource;

        public Geometry? VectorIcon => Command?.VectorIcon;

        public string DisplayGlyph => Command?.DisplayGlyph ?? "E";

        public bool HasImageIcon => Command?.HasImageIcon ?? false;

        public bool HasVectorIcon => Command?.HasVectorIcon ?? false;

        public bool UseGlyphIcon => Command == null || Command.UseGlyphIcon;

        public System.Windows.Media.Brush AccentBrush => Command?.AccentBrush
            ?? (System.Windows.Media.Brush)new BrushConverter().ConvertFromString("#FF4B5563")!;

        public void Update(RunningExtensionInfo info, CommandItem? command)
        {
            Title = info.Title;
            ExtensionId = info.ExtensionId;
            ProcessId = info.ProcessId > 0 ? info.ProcessId.ToString() : "宿主内";
            Runtime = info.Runtime;
            LaunchSource = info.LaunchSource;
            StartedAtText = info.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            Command = command;
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
