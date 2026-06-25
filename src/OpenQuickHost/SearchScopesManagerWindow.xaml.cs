using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace OpenQuickHost
{
    public partial class SearchScopesManagerWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly AppSettings _settings;
        private ObservableCollection<SearchScopeConfigItemViewModel> _items = new();

        public SearchScopesManagerWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _settings = AppSettingsStore.Load();

            _settings.SearchScopeConfigs ??= new();
            
            foreach (var config in _settings.SearchScopeConfigs)
            {
                if (config == null) continue;
                
                string displayLabel = config.Label;
                if (!config.IsPinned)
                {
                    displayLabel = config.Key switch
                    {
                        "all" => "全部",
                        "extension" => "扩展",
                        "application" => "应用",
                        "file" => "文件",
                        "system" => "系统",
                        "yanyu" => "燕语",
                        "ai" => "AI对话",
                        "store" => "扩展商店",
                        _ => config.Label
                    };
                }
                else
                {
                    var commandId = config.Key.Replace("pinned_", "");
                    var cmd = _mainWindow.GetAllCommands().FirstOrDefault(c => string.Equals(c.ExtensionId, commandId, System.StringComparison.OrdinalIgnoreCase));
                    if (cmd != null)
                    {
                        displayLabel = $"固定: {cmd.Title}";
                    }
                    else
                    {
                        displayLabel = $"固定: {config.Label}";
                    }
                }

                _items.Add(new SearchScopeConfigItemViewModel
                {
                    Key = config.Key,
                    Label = displayLabel,
                    IsVisible = config.IsVisible,
                    IsPinned = config.IsPinned
                });
            }

            ScopesListBox.ItemsSource = _items;
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = ScopesListBox.SelectedIndex;
            if (selectedIndex > 0)
            {
                _items.Move(selectedIndex, selectedIndex - 1);
                ScopesListBox.SelectedIndex = selectedIndex - 1;
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = ScopesListBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _items.Count - 1)
            {
                _items.Move(selectedIndex, selectedIndex + 1);
                ScopesListBox.SelectedIndex = selectedIndex + 1;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.SearchScopeConfigs.Clear();
            foreach (var item in _items)
            {
                _settings.SearchScopeConfigs.Add(new SearchScopeConfigItem
                {
                    Key = item.Key,
                    Label = item.Label,
                    IsVisible = item.IsVisible,
                    IsPinned = item.IsPinned
                });
            }

            AppSettingsStore.Save(_settings);
            DialogResult = true;
            Close();
        }
    }

    public class SearchScopeConfigItemViewModel
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public bool IsPinned { get; set; }
    }
}
