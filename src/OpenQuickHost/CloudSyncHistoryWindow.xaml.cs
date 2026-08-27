using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using OpenQuickHost.Sync;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace OpenQuickHost;

public partial class CloudSyncHistoryWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindow _mainWindow;
    private readonly CloudSyncClient _client;
    private long _currentObjectRevision;
    private string _statusText = "正在读取历史...";

    public CloudSyncHistoryWindow(
        MainWindow mainWindow,
        CloudSyncClient client,
        string objectId,
        string displayName,
        long currentObjectRevision)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _client = client;
        ObjectId = objectId;
        DisplayName = displayName;
        _currentObjectRevision = currentObjectRevision;
        DataContext = this;
        Loaded += async (_, _) => await LoadHistoryAsync();
    }

    public string ObjectId { get; }

    public string DisplayName { get; }

    public ObservableCollection<CloudSyncHistoryVersionView> Versions { get; } = [];

    public bool Restored { get; private set; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task LoadHistoryAsync()
    {
        try
        {
            StatusText = "正在读取不可变版本记录...";
            var response = await _client.GetSyncObjectHistoryAsync(ObjectId, limit: 100);
            if (response.Versions.Count > 0)
            {
                _currentObjectRevision = response.Versions.Max(static version => version.Revision);
            }

            Versions.Clear();
            foreach (var version in response.Versions)
            {
                Versions.Add(CloudSyncHistoryVersionView.FromRecord(version, _currentObjectRevision));
            }

            StatusText = Versions.Count == 0
                ? "这个对象还没有可查询的历史。部署历史迁移前产生的旧版本不会被反向补录。"
                : $"已载入 {Versions.Count} 个版本；当前对象 rev {_currentObjectRevision}。" +
                  (response.HasMore ? " 仍有更早版本未显示。" : string.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"读取历史失败：{ex.Message}";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RunWithButtonDisabledAsync(sender, LoadHistoryAsync);
    }

    private async void RestoreVersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CloudSyncHistoryVersionView version } button || !version.CanRestore)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"将“{DisplayName}”恢复到 rev {version.Revision}？\n\n恢复会生成一个新版本，现有版本和历史记录都不会删除。",
            "确认恢复同步版本",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            StatusText = $"正在恢复 rev {version.Revision}...";
            var restored = await _mainWindow.RestoreCloudObjectVersionAsync(
                ObjectId,
                _currentObjectRevision,
                version.Revision);
            _currentObjectRevision = restored.Revision;
            Restored = true;

            // 立即走正常拉取/合成链，让本机缓存、界面和云端恢复结果保持一致。
            await _mainWindow.RefreshCloudFromSettingsAsync();
            await LoadHistoryAsync();
            StatusText = $"已从 rev {version.Revision} 恢复，并生成新版本 rev {restored.Revision}。";
        }
        catch (Exception ex)
        {
            StatusText = $"恢复失败：{ex.Message}。请刷新历史后重试，避免覆盖其他设备的新修改。";
            await LoadHistoryAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task RunWithButtonDisabledAsync(object sender, Func<Task> action)
    {
        if (sender is Button button) button.IsEnabled = false;
        try
        {
            await action();
        }
        finally
        {
            if (sender is Button completedButton) completedButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record CloudSyncHistoryVersionView(
    long Revision,
    string OperationText,
    string UpdatedAtText,
    string DeviceText,
    string DetailText,
    bool IsCurrent,
    bool CanRestore)
{
    public static CloudSyncHistoryVersionView FromRecord(CloudSyncObjectHistoryRecord record, long currentRevision)
    {
        var operation = record.Operation.ToLowerInvariant() switch
        {
            "create" => "首次创建",
            "delete" => "删除",
            "restore" => "历史恢复",
            _ => "配置修改"
        };
        var device = !string.IsNullOrWhiteSpace(record.UpdatedByDeviceName)
            ? record.UpdatedByDeviceName!
            : !string.IsNullOrWhiteSpace(record.UpdatedByDeviceId)
                ? record.UpdatedByDeviceId!
                : "未知设备";
        var time = DateTimeOffset.TryParse(record.UpdatedAtUtc, out var timestamp)
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)
            : "未知时间";
        var detail = record.Operation.Equals("restore", StringComparison.OrdinalIgnoreCase) && record.RestoredFromRevision.HasValue
            ? $"来自 rev {record.RestoredFromRevision.Value}"
            : record.Deleted ? "墓碑版本" : "完整对象版本";
        var isCurrent = record.Revision == currentRevision;
        return new(record.Revision, operation, time, device, detail, isCurrent, !isCurrent);
    }
}
