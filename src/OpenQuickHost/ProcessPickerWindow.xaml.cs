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
    public bool ShowFullscreenSwitch { get; set; }
    public bool DisableInFullscreen { get; set; }

    private System.Collections.Generic.List<ProcessItem> _allRunningProcesses = new();

    public ProcessPickerWindow(
        string title,
        string description,
        string defaultProcess,
        System.Collections.Generic.List<string>? initialList = null,
        bool showFullscreenSwitch = false,
        bool disableInFullscreen = false)
    {
        InitializeComponent();
        Title = title;
        DescriptionText.Text = description;
        ShowFullscreenSwitch = showFullscreenSwitch;
        DisableInFullscreen = disableInFullscreen;
        
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
            FullscreenSwitchPanel.Visibility = ShowFullscreenSwitch ? Visibility.Visible : Visibility.Collapsed;
            DisableInFullscreenCheckBox.IsChecked = DisableInFullscreen;
            ProcessComboBox.Focus();
            UpdatePlaceholderVisibility();
        };
    }

    public void AddProcessToBlacklist(string processName, string? executablePath = null, ImageSource? icon = null)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        if (Blacklist.Any(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (icon == null)
        {
            var match = _allRunningProcesses.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                icon = match.Icon;
                executablePath ??= match.ExecutablePath;
            }
        }

        if (icon == null && !string.IsNullOrWhiteSpace(executablePath) && System.IO.File.Exists(executablePath))
        {
            try { icon = NativeFileIconService.GetIcon(executablePath, false); } catch { }
        }

        if (icon == null && AppSettingsStore.Load().ProcessExecutablePaths.TryGetValue(processName, out var cachedPath))
        {
            try
            {
                if (System.IO.File.Exists(cachedPath))
                {
                    icon = NativeFileIconService.GetIcon(cachedPath, false);
                    executablePath ??= cachedPath;
                }
            }
            catch { }
        }

        if (icon == null)
        {
            try
            {
                var procs = Process.GetProcessesByName(processName);
                if (procs.Length > 0)
                {
                    var resolved = ProcessHelper.GetProcessExecutablePath(procs[0]);
                    if (!string.IsNullOrWhiteSpace(resolved) && System.IO.File.Exists(resolved))
                    {
                        icon = NativeFileIconService.GetIcon(resolved, false);
                        executablePath ??= resolved;
                    }
                }
            }
            catch { }
        }

        if (icon == null)
        {
            icon = FallbackIconResolver.GetFallbackIcon(processName);
        }

        Blacklist.Add(new BlacklistItem
        {
            ProcessName = processName,
            ExecutablePath = executablePath,
            Icon = icon
        });

        RefreshComboBoxItemsSource();
    }

    private async void PickWindowButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var prevVisibility = Visibility;
            Visibility = Visibility.Collapsed;

            var picker = new WindowPickerOverlay();
            var processName = await picker.ShowPickerAsync();

            Visibility = prevVisibility;
            Activate();

            if (!string.IsNullOrWhiteSpace(processName))
            {
                AddProcessToBlacklist(processName);
            }
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ProcessPicker: PickWindow failed, {ex.Message}");
            Visibility = Visibility.Visible;
            Activate();
        }
    }

    private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessComboBox.SelectedItem is ProcessItem selectedItem)
        {
            AddProcessToBlacklist(selectedItem.ProcessName, selectedItem.ExecutablePath, selectedItem.Icon);
            
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
        DisableInFullscreen = DisableInFullscreenCheckBox.IsChecked == true;
        DialogResult = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnCurrentScreen();
    }

    private void CenterOnCurrentScreen()
    {
        try
        {
            if (Win32Native.GetCursorPos(out var pt))
            {
                var hMonitor = Win32Native.MonitorFromPoint(pt, Win32Native.MonitorDefaultToNearest);
                if (hMonitor != IntPtr.Zero)
                {
                    var mi = new Win32Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.MONITORINFO>() };
                    if (Win32Native.GetMonitorInfo(hMonitor, ref mi))
                    {
                        var source = PresentationSource.FromVisual(this);
                        var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                        var wpfWorkLeft = mi.rcWork.Left / dpiX;
                        var wpfWorkTop = mi.rcWork.Top / dpiY;
                        var wpfWorkWidth = mi.rcWork.Width / dpiX;
                        var wpfWorkHeight = mi.rcWork.Height / dpiY;

                        Left = wpfWorkLeft + Math.Max(0, (wpfWorkWidth - Width) / 2);
                        Top = wpfWorkTop + Math.Max(0, (wpfWorkHeight - Height) / 2);
                        return;
                    }
                }
            }

            Left = Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
            Top = Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
        }
        catch
        {
            // Ignore
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

