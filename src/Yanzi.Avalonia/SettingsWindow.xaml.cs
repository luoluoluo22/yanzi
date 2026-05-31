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

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();
}
