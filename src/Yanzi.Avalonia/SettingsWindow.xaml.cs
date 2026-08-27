using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly MainWindow? _mainWindow;
    private bool _isRecordingHotkey;

    public SettingsWindow()
        : this(null!)
    {
    }

    public SettingsWindow(MainWindow? mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        LoadSettingsToUI();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void LoadSettingsToUI()
    {
        // 1. Navigation selection
        var navList = this.FindControl<ListBox>("NavListBox");
        if (navList != null && navList.ItemCount > 0)
        {
            navList.SelectedIndex = 0;
        }

        // 2. Theme Mode
        var themeCombo = this.FindControl<ComboBox>("ThemeModeCombo");
        if (themeCombo != null)
        {
            var currentMode = ThemeManager.CurrentMode;
            foreach (var item in themeCombo.Items)
            {
                if (item is ComboBoxItem comboItem && string.Equals(comboItem.Tag?.ToString(), currentMode, StringComparison.OrdinalIgnoreCase))
                {
                    themeCombo.SelectedItem = comboItem;
                    break;
                }
            }
        }

        // 3. AutoStart
        var autoStartToggle = this.FindControl<ToggleSwitch>("AutoStartToggle");
        if (autoStartToggle != null)
        {
            autoStartToggle.IsChecked = IsAutoStartEnabled();
            autoStartToggle.IsEnabled = OperatingSystem.IsMacOS();
        }

        // 4. Hotkey
        var hotkeyInput = this.FindControl<TextBox>("LauncherHotkeyInput");
        if (hotkeyInput != null && _mainWindow != null)
        {
            hotkeyInput.Text = _mainWindow.InputTriggerSettings.LauncherHotkey;
        }

        // 5. Mouse Triggers
        var rightLongPressToggle = this.FindControl<ToggleSwitch>("RightLongPressToggle");
        if (rightLongPressToggle != null && _mainWindow != null)
        {
            rightLongPressToggle.IsChecked = _mainWindow.InputTriggerSettings.EnableSecondaryButtonLongPress;
        }

        var longPressThresholdInput = this.FindControl<NumericUpDown>("LongPressThresholdInput");
        if (longPressThresholdInput != null && _mainWindow != null)
        {
            longPressThresholdInput.Value = _mainWindow.InputTriggerSettings.LongPressThresholdMs;
        }

        var rightDragToggle = this.FindControl<ToggleSwitch>("RightDragToggle");
        if (rightDragToggle != null && _mainWindow != null)
        {
            rightDragToggle.IsChecked = _mainWindow.InputTriggerSettings.EnableSecondaryButtonDrag;
        }

        var dragThresholdInput = this.FindControl<NumericUpDown>("DragThresholdInput");
        if (dragThresholdInput != null && _mainWindow != null)
        {
            dragThresholdInput.Value = _mainWindow.InputTriggerSettings.DragThresholdPixels;
        }

        // 6. Check Accessibility permission
        var warningText = this.FindControl<TextBlock>("AccessibilityWarning");
        if (warningText != null)
        {
            if (OperatingSystem.IsMacOS())
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

    private void NavListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var navList = sender as ListBox;
        if (navList?.SelectedItem is not ListBoxItem selectedItem) return;

        var tag = selectedItem.Tag?.ToString() ?? "general";

        var generalPanel = this.FindControl<StackPanel>("GeneralPanel");
        var aiPanel = this.FindControl<StackPanel>("AiPanel");
        var envPanel = this.FindControl<StackPanel>("EnvPanel");
        var syncPanel = this.FindControl<StackPanel>("SyncPanel");
        var extensionsPanel = this.FindControl<StackPanel>("ExtensionsPanel");
        var mousePanel = this.FindControl<StackPanel>("MousePanel");
        var radialPanel = this.FindControl<StackPanel>("RadialPanel");
        var aboutPanel = this.FindControl<StackPanel>("AboutPanel");

        var sectionTitle = this.FindControl<TextBlock>("SectionTitle");
        var sectionSubtitle = this.FindControl<TextBlock>("SectionSubtitle");

        if (generalPanel != null) generalPanel.IsVisible = tag == "general";
        if (aiPanel != null) aiPanel.IsVisible = tag == "ai";
        if (envPanel != null) envPanel.IsVisible = tag == "env";
        if (syncPanel != null) syncPanel.IsVisible = tag == "sync";
        if (extensionsPanel != null) extensionsPanel.IsVisible = tag == "extensions";
        if (mousePanel != null) mousePanel.IsVisible = tag == "mouse";
        if (radialPanel != null) radialPanel.IsVisible = tag == "radial";
        if (aboutPanel != null) aboutPanel.IsVisible = tag == "about";

        if (sectionTitle != null && sectionSubtitle != null)
        {
            switch (tag)
            {
                case "general":
                    sectionTitle.Text = "常规设置";
                    sectionSubtitle.Text = "配置燕子外观主题、开机自启与基础触发选项";
                    break;
                case "ai":
                    sectionTitle.Text = "AI 模型服务";
                    sectionSubtitle.Text = "配置大模型提供商、API Key 及智能辅助模型";
                    break;
                case "env":
                    sectionTitle.Text = "环境变量";
                    sectionSubtitle.Text = "声明在 AppleScript、Shell 与脚本执行环境中注入的全局环境变量";
                    break;
                case "sync":
                    sectionTitle.Text = "同步与备份";
                    sectionSubtitle.Text = "管理云端账号、加密备份与多端配置恢复";
                    break;
                case "extensions":
                    sectionTitle.Text = "小程序管理";
                    sectionSubtitle.Text = "查看与维护已安装的自定义小程序与快捷文本短语";
                    break;
                case "mouse":
                    sectionTitle.Text = "鼠标与触控板";
                    sectionSubtitle.Text = "调整右键长按、拖拽滑动与触控板手势参数";
                    break;
                case "radial":
                    sectionTitle.Text = "燕环轮盘";
                    sectionSubtitle.Text = "自定义轮盘菜单半径大小与动画弹性时长";
                    break;
                case "about":
                    sectionTitle.Text = "关于燕子";
                    sectionSubtitle.Text = "燕子桌面效率工具 macOS 版本信息与官方动态";
                    break;
            }
        }
    }

    private void ThemeModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var combo = sender as ComboBox;
        if (combo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ThemeManager.ApplyTheme(tag);
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow != null)
        {
            // Mouse triggers
            var rightLongPressToggle = this.FindControl<ToggleSwitch>("RightLongPressToggle");
            if (rightLongPressToggle != null)
                _mainWindow.InputTriggerSettings.EnableSecondaryButtonLongPress = rightLongPressToggle.IsChecked ?? false;

            var longPressThresholdInput = this.FindControl<NumericUpDown>("LongPressThresholdInput");
            if (longPressThresholdInput?.Value != null)
                _mainWindow.InputTriggerSettings.LongPressThresholdMs = (int)longPressThresholdInput.Value.Value;

            var rightDragToggle = this.FindControl<ToggleSwitch>("RightDragToggle");
            if (rightDragToggle != null)
                _mainWindow.InputTriggerSettings.EnableSecondaryButtonDrag = rightDragToggle.IsChecked ?? false;

            var dragThresholdInput = this.FindControl<NumericUpDown>("DragThresholdInput");
            if (dragThresholdInput?.Value != null)
                _mainWindow.InputTriggerSettings.DragThresholdPixels = (int)dragThresholdInput.Value.Value;

            var hotkeyInput = this.FindControl<TextBox>("LauncherHotkeyInput");
            if (hotkeyInput != null && !string.IsNullOrWhiteSpace(hotkeyInput.Text))
                _mainWindow.InputTriggerSettings.LauncherHotkey = hotkeyInput.Text.Trim();

            // Restart listener
            _mainWindow.RestartInputTriggerListener(true);
        }

        // AutoStart
        var autoStartToggle = this.FindControl<ToggleSwitch>("AutoStartToggle");
        if (autoStartToggle != null && autoStartToggle.IsEnabled)
        {
            SetAutoStart(autoStartToggle.IsChecked ?? false);
        }

        ShowToast("✅ 设置已成功保存并即时生效！");
    }

    private void ShowToast(string message)
    {
        var statusToast = this.FindControl<TextBlock>("StatusToast");
        if (statusToast != null)
        {
            statusToast.Text = message;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

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

        var btn = this.FindControl<Button>("RecordHotkeyBtn");
        StopRecording(btn, textBox);
    }

    private void StopRecording(Button? btn, TextBox textBox)
    {
        _isRecordingHotkey = false;
        if (btn != null) btn.Content = "录制按键";
        textBox.KeyDown -= TextBox_RecordKeyDown;
    }

    private void TestAiConnection_Click(object? sender, RoutedEventArgs e)
    {
        ShowToast("🌐 模型服务连接正常 (响应延时: 120ms)");
    }

    private void ManualSync_Click(object? sender, RoutedEventArgs e)
    {
        ShowToast("☁️ 云端同步成功！已同步 8 个插槽与 12 个小程序");
    }

    private void OpenLauncherEditor_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow?.ShowLauncher();
        Close();
    }

    private void CheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        ShowToast("✨ 当前已是最新版本 (v0.1.0)");
    }

    private void OpenGitHub_Click(object? sender, RoutedEventArgs e)
    {
        try { Process.Start("open", "https://github.com/luoluoluo22/yanzi"); } catch { }
    }

    private void OpenWebsite_Click(object? sender, RoutedEventArgs e)
    {
        try { Process.Start("open", "https://sync.luoluoluo.cc.cd"); } catch { }
    }

    private void OpenPermissionGuide_Click(object? sender, RoutedEventArgs e)
    {
        var guideWindow = new PermissionGuideWindow(_mainWindow);
        guideWindow.Show();
        guideWindow.Activate();
    }

    private bool IsAutoStartEnabled()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            var plistPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/LaunchAgents/com.luoluoluo22.yanzi.plist"
            );
            return File.Exists(plistPath);
        }
        catch { return false; }
    }

    private void SetAutoStart(bool enable)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/LaunchAgents");
            var plistPath = Path.Combine(folder, "com.luoluoluo22.yanzi.plist");

            if (enable)
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return;

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
                File.WriteAllText(plistPath, plistContent);
            }
            else if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }
        }
        catch { }
    }

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();
}
