using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly MainWindow? _mainWindow;

    public SettingsWindow()
        : this(null!)
    {
    }

    public SettingsWindow(MainWindow? mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        if (_mainWindow == null) return;

        // Load current settings into UI
        var serviceSwitch = this.FindControl<ToggleSwitch>("ServiceSwitch");
        if (serviceSwitch != null)
        {
            serviceSwitch.IsChecked = _mainWindow.IsServiceRunning;
        }

        var rightLongPressCheck = this.FindControl<CheckBox>("RightLongPressCheck");
        if (rightLongPressCheck != null)
        {
            rightLongPressCheck.IsChecked = _mainWindow.InputTriggerSettings.EnableSecondaryButtonLongPress;
        }

        var longPressThresholdInput = this.FindControl<NumericUpDown>("LongPressThresholdInput");
        if (longPressThresholdInput != null)
        {
            longPressThresholdInput.Value = _mainWindow.InputTriggerSettings.LongPressThresholdMs;
        }

        var rightDragCheck = this.FindControl<CheckBox>("RightDragCheck");
        if (rightDragCheck != null)
        {
            rightDragCheck.IsChecked = _mainWindow.InputTriggerSettings.EnableSecondaryButtonDrag;
        }

        var dragThresholdInput = this.FindControl<NumericUpDown>("DragThresholdInput");
        if (dragThresholdInput != null)
        {
            dragThresholdInput.Value = _mainWindow.InputTriggerSettings.DragThresholdPixels;
        }

        var autoStartCheck = this.FindControl<CheckBox>("AutoStartCheck");
        if (autoStartCheck != null)
        {
            autoStartCheck.IsChecked = IsAutoStartEnabled();
            autoStartCheck.IsEnabled = System.OperatingSystem.IsMacOS();
        }

        var launcherHotkeyInput = this.FindControl<TextBox>("LauncherHotkeyInput");
        if (launcherHotkeyInput != null)
        {
            launcherHotkeyInput.Text = _mainWindow.InputTriggerSettings.LauncherHotkey;
        }

        // Check accessibility permission on macOS
        var warningText = this.FindControl<TextBlock>("AccessibilityWarning");
        if (warningText != null)
        {
            if (System.OperatingSystem.IsMacOS())
            {
                try
                {
                    warningText.IsVisible = !AXIsProcessTrusted();
                }
                catch
                {
                    warningText.IsVisible = false;
                }
            }
            else
            {
                warningText.IsVisible = false;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        // Save settings from UI to MainWindow
        var rightLongPressCheck = this.FindControl<CheckBox>("RightLongPressCheck");
        if (rightLongPressCheck != null)
        {
            _mainWindow.InputTriggerSettings.EnableSecondaryButtonLongPress = rightLongPressCheck.IsChecked ?? false;
        }

        var longPressThresholdInput = this.FindControl<NumericUpDown>("LongPressThresholdInput");
        if (longPressThresholdInput != null && longPressThresholdInput.Value.HasValue)
        {
            _mainWindow.InputTriggerSettings.LongPressThresholdMs = (int)longPressThresholdInput.Value.Value;
        }

        var rightDragCheck = this.FindControl<CheckBox>("RightDragCheck");
        if (rightDragCheck != null)
        {
            _mainWindow.InputTriggerSettings.EnableSecondaryButtonDrag = rightDragCheck.IsChecked ?? false;
        }

        var dragThresholdInput = this.FindControl<NumericUpDown>("DragThresholdInput");
        if (dragThresholdInput != null && dragThresholdInput.Value.HasValue)
        {
            _mainWindow.InputTriggerSettings.DragThresholdPixels = (int)dragThresholdInput.Value.Value;
        }

        var launcherHotkeyInput = this.FindControl<TextBox>("LauncherHotkeyInput");
        if (launcherHotkeyInput != null && !string.IsNullOrEmpty(launcherHotkeyInput.Text))
        {
            _mainWindow.InputTriggerSettings.LauncherHotkey = launcherHotkeyInput.Text.Trim();
        }

        // Save auto-start setting on macOS
        var autoStartCheck = this.FindControl<CheckBox>("AutoStartCheck");
        if (autoStartCheck != null && autoStartCheck.IsEnabled)
        {
            SetAutoStart(autoStartCheck.IsChecked ?? false);
        }

        // Apply service running state
        var serviceSwitch = this.FindControl<ToggleSwitch>("ServiceSwitch");
        var wasRunning = _mainWindow.IsServiceRunning;
        var shouldRun = serviceSwitch?.IsChecked ?? false;

        // Recreate the listener with updated settings
        _mainWindow.RestartInputTriggerListener(shouldRun);

        if (Application.Current is App app)
        {
            app.UpdateTrayMenuState();
        }

        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool _isRecordingHotkey;

    private void RecordHotkey_Click(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var textBox = this.FindControl<TextBox>("LauncherHotkeyInput");
        if (btn == null || textBox == null) return;

        if (!_isRecordingHotkey)
        {
            _isRecordingHotkey = true;
            btn.Content = "请按键...";
            textBox.Focus();
            textBox.KeyDown += TextBox_RecordKeyDown;
        }
        else
        {
            StopRecording(btn, textBox);
        }
    }

    private void TextBox_RecordKeyDown(object? sender, KeyEventArgs e)
    {
        var textBox = sender as TextBox;
        if (textBox == null) return;

        e.Handled = true;

        var parts = new List<string>();
        if ((e.KeyModifiers & KeyModifiers.Meta) != 0) parts.Add("cmd");
        if ((e.KeyModifiers & KeyModifiers.Control) != 0) parts.Add("ctrl");
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0) parts.Add("alt");
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0) parts.Add("shift");

        var keyStr = e.Key.ToString().ToLowerInvariant();
        if (keyStr != "lmeta" && keyStr != "rmeta" && keyStr != "lcontrol" && keyStr != "rcontrol" && 
            keyStr != "lalt" && keyStr != "ralt" && keyStr != "lshift" && keyStr != "rshift")
        {
            if (keyStr == "oemcomma") keyStr = ",";
            parts.Add(keyStr);
        }

        if (parts.Count > 0)
        {
            textBox.Text = string.Join("+", parts);
        }

        var btn = this.FindControl<Button>("RecordButton");
        StopRecording(btn, textBox);
    }

    private void StopRecording(Button? btn, TextBox textBox)
    {
        _isRecordingHotkey = false;
        if (btn != null) btn.Content = "录制";
        textBox.KeyDown -= TextBox_RecordKeyDown;
    }

    private bool IsAutoStartEnabled()
    {
        if (!System.OperatingSystem.IsMacOS()) return false;
        try
        {
            var plistPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/LaunchAgents/com.luoluoluo22.yanzi.plist"
            );
            return System.IO.File.Exists(plistPath);
        }
        catch
        {
            return false;
        }
    }

    private void SetAutoStart(bool enable)
    {
        if (!System.OperatingSystem.IsMacOS()) return;
        try
        {
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/LaunchAgents"
            );
            var plistPath = System.IO.Path.Combine(folder, "com.luoluoluo22.yanzi.plist");

            if (enable)
            {
                if (!System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                }

                string exePath = Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                }

                if (string.IsNullOrEmpty(exePath))
                {
                    Console.WriteLine("Could not resolve executing process path for auto-start registration.");
                    return;
                }

                var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.luoluoluo22.yanzi</string>
    <key>ProgramArguments</key>
    <array>
        <string>{exePath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>ProcessType</key>
    <string>Interactive</string>
</dict>
</plist>";
                System.IO.File.WriteAllText(plistPath, plistContent);
                Console.WriteLine($"Successfully registered auto-start LaunchAgent pointing to {exePath}");
            }
            else
            {
                if (System.IO.File.Exists(plistPath))
                {
                    System.IO.File.Delete(plistPath);
                    Console.WriteLine("Successfully removed auto-start LaunchAgent");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to toggle auto-start settings: {ex.Message}");
        }
    }

    private void OpenPermissionGuide_Click(object? sender, RoutedEventArgs e)
    {
        var guideWindow = new PermissionGuideWindow(_mainWindow);
        guideWindow.Show();
        guideWindow.Activate();
    }

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();
}
