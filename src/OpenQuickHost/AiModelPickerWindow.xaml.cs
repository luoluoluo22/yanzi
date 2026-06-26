using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace OpenQuickHost;

public partial class AiModelPickerWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<AiModelPickerItem> Items { get; } = [];

    public IReadOnlyList<string> SelectedModels =>
        Items.Where(static item => item.CanSelect && item.IsSelected)
             .Select(static item => item.Name)
             .ToList();

    public string SummaryText => $"可添加 {Items.Count(static item => item.CanSelect)} 个，已勾选 {Items.Count(static item => item.CanSelect && item.IsSelected)} 个";

    public event PropertyChangedEventHandler? PropertyChanged;

    public AiModelPickerWindow(string providerName, IEnumerable<string> availableModels, IEnumerable<string> existingModels)
    {
        InitializeComponent();
        DataContext = this;

        Title = $"选择模型 - {providerName}";
        TitleTextBlock.Text = $"为 {providerName} 选择模型";
        DescriptionTextBlock.Text = "已添加的模型会标记为“已添加”。勾选新的模型后点击右下角直接加入当前提供商。";

        var existing = new HashSet<string>(existingModels, StringComparer.OrdinalIgnoreCase);
        foreach (var modelName in availableModels)
        {
            var item = new AiModelPickerItem(modelName, existing.Contains(modelName));
            item.PropertyChanged += Item_PropertyChanged;
            Items.Add(item);
        }

        UpdateSummary();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiModelPickerItem.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateSummary()
    {
        SummaryTextBlock.Text = SummaryText;
        OnPropertyChanged(nameof(SummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}

public sealed class AiModelPickerItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; }

    public bool IsAlreadyAdded { get; }

    public bool CanSelect => !IsAlreadyAdded;

    public Visibility AlreadyAddedVisibility => IsAlreadyAdded ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AiModelPickerItem(string name, bool isAlreadyAdded)
    {
        Name = name;
        IsAlreadyAdded = isAlreadyAdded;
        _isSelected = isAlreadyAdded;
    }
}
