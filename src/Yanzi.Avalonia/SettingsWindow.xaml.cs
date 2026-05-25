using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _mainWindow;

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
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
}
