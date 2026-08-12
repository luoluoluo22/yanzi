using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenQuickHost;

public class ProcessItem
{
    public string ProcessName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public ImageSource? Icon { get; set; }
}

public class BlacklistItem
{
    public string ProcessName { get; set; } = string.Empty;
    public string? ExecutablePath { get; set; }
    public ImageSource? Icon { get; set; }
}

public partial class ProcessPickerWindow : Window
{
    public ObservableCollection<BlacklistItem> Blacklist { get; } = new();

    private System.Collections.Generic.List<ProcessItem> _allRunningProcesses = new();

    public ProcessPickerWindow(string title, string description, string defaultProcess, System.Collections.Generic.List<string>? initialList = null)
    {
        InitializeComponent();
        Title = title;
        DescriptionText.Text = description;
        
        BlacklistItemsControl.ItemsSource = Blacklist;

        _allRunningProcesses = Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero)
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => 
            {
                string? validPath = null;
                foreach (var p in g)
                {
                    try
                    {
                        var path = ProcessHelper.GetProcessExecutablePath(p);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            validPath = path;
                            break;
                        }
                    }
                    catch { }
                }

                ImageSource? icon = null;
                if (!string.IsNullOrWhiteSpace(validPath))
                {
                    try { icon = NativeFileIconService.GetIcon(validPath, isFolder: false); }
                    catch { }
                }

                if (icon == null)
                {
                    icon = FallbackIconResolver.GetFallbackIcon(g.Key);
                }

                return new ProcessItem
                {
                    ProcessName = g.Key,
                    ExecutablePath = validPath,
                    Icon = icon
                };
            })
            .OrderByDescending(p => !string.IsNullOrWhiteSpace(defaultProcess) && p.ProcessName.Equals(defaultProcess, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.ProcessName)
            .ToList();

        if (initialList != null)
        {
            foreach (var process in initialList)
            {
                if (!string.IsNullOrWhiteSpace(process))
                {
                    var processItem = _allRunningProcesses.FirstOrDefault(p => p.ProcessName.Equals(process, StringComparison.OrdinalIgnoreCase));
                    ImageSource? icon = processItem?.Icon;
                    string? exePath = processItem?.ExecutablePath;

                    // If not running, check if we cached its path previously
                    if (icon == null && AppSettingsStore.Load().ProcessExecutablePaths.TryGetValue(process, out var cachedPath))
                    {
                        try
                        {
                            if (System.IO.File.Exists(cachedPath))
                            {
                                icon = NativeFileIconService.GetIcon(cachedPath, false);
                                exePath = cachedPath;
                            }
                        }
                        catch { }
                    }

                    if (icon == null) icon = FallbackIconResolver.GetFallbackIcon(process);
                    Blacklist.Add(new BlacklistItem { ProcessName = process, ExecutablePath = exePath, Icon = icon });
                }
            }
        }

        RefreshComboBoxItemsSource();
        ProcessComboBox.Text = string.Empty; // Default empty so placeholder shows

        Loaded += (_, _) =>
        {
            ProcessComboBox.Focus();
            UpdatePlaceholderVisibility();
        };
    }

    private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessComboBox.SelectedItem is ProcessItem selectedItem)
        {
            if (!string.IsNullOrWhiteSpace(selectedItem.ProcessName) && !Blacklist.Any(p => p.ProcessName.Equals(selectedItem.ProcessName, StringComparison.OrdinalIgnoreCase)))
            {
                Blacklist.Add(new BlacklistItem { ProcessName = selectedItem.ProcessName, ExecutablePath = selectedItem.ExecutablePath, Icon = selectedItem.Icon });
                RefreshComboBoxItemsSource();
            }
            
            // Clear selection by dispatching to avoid interfering with combobox internal state
            Dispatcher.BeginInvoke(new Action(() => 
            {
                ProcessComboBox.SelectedItem = null;
                ProcessComboBox.Text = string.Empty;
                UpdatePlaceholderVisibility();
            }));
        }
    }

    private void RemoveBlacklistItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.DataContext is BlacklistItem item)
        {
            Blacklist.Remove(item);
            RefreshComboBoxItemsSource();
        }
    }

    private void RefreshComboBoxItemsSource()
    {
        ProcessComboBox.ItemsSource = _allRunningProcesses
            .Where(p => !Blacklist.Any(b => b.ProcessName.Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void ProcessComboBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdatePlaceholderVisibility();
    }

    private void UpdatePlaceholderVisibility()
    {
        if (PlaceholderTextBlock != null)
        {
            PlaceholderTextBlock.Visibility = string.IsNullOrEmpty(ProcessComboBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

