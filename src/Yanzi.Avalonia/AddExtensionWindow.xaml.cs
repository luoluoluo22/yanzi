using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class AddExtensionWindow : Window
{
    private readonly MainWindow? _mainWindow;
    private string _currentType = "url";
    private bool _isUpdatingFromForm;

    public CommandItem? ResultCommand { get; private set; }

    public AddExtensionWindow() : this(null) { }

    public AddExtensionWindow(MainWindow? mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        SetupPresets();
        SelectType("url");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTypeCardClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag)
        {
            SelectType(tag);
        }
    }

    private void SelectType(string type)
    {
        _currentType = type;

        var cardUrl = this.FindControl<Border>("CardTypeUrl");
        var cardSearch = this.FindControl<Border>("CardTypeSearch");
        var cardSnippet = this.FindControl<Border>("CardTypeSnippet");
        var cardScript = this.FindControl<Border>("CardTypeScript");
        var cardShell = this.FindControl<Border>("CardTypeShell");
        var cardShortcut = this.FindControl<Border>("CardTypeShortcut");

        SetSelectedClass(cardUrl, type == "url");
        SetSelectedClass(cardSearch, type == "search");
        SetSelectedClass(cardSnippet, type == "snippet");
        SetSelectedClass(cardScript, type == "applescript");
        SetSelectedClass(cardShell, type == "shell");
        SetSelectedClass(cardShortcut, type == "shortcut");

        var mainLabel = this.FindControl<TextBlock>("MainFieldLabel");
        var mainInput = this.FindControl<TextBox>("MainFieldInput");
        var scriptContainer = this.FindControl<StackPanel>("ScriptFieldContainer");
        var scriptLabel = this.FindControl<TextBlock>("ScriptFieldLabel");
        var abbrevContainer = this.FindControl<StackPanel>("AbbrevContainer");
        var browseBtn = this.FindControl<Button>("BrowseBtn");

        if (scriptContainer != null) scriptContainer.IsVisible = (type == "applescript" || type == "shell" || type == "snippet");
        if (abbrevContainer != null) abbrevContainer.IsVisible = (type == "snippet");
        if (browseBtn != null) browseBtn.IsVisible = (type == "url");

        var iconInput = this.FindControl<TextBox>("IconInput");
        var titleInput = this.FindControl<TextBox>("TitleInput");
        var descInput = this.FindControl<TextBox>("DescInput");

        switch (type)
        {
            case "url":
                if (mainLabel != null) mainLabel.Text = "目标地址 (URL 网址、应用名称或文件路径)";
                if (mainInput != null) mainInput.Watermark = "例如: https://yanzi.luoluoluo.cc.cd, Safari, /Applications";
                if (iconInput != null && string.IsNullOrEmpty(iconInput.Text)) iconInput.Text = "🌐";
                break;
            case "search":
                if (mainLabel != null) mainLabel.Text = "搜索 URL 模板 (支持 {query} 占位符)";
                if (mainInput != null) mainInput.Watermark = "例如: https://www.google.com/search?q={query}";
                if (iconInput != null && string.IsNullOrEmpty(iconInput.Text)) iconInput.Text = "🔍";
                break;
            case "snippet":
                if (mainLabel != null) mainLabel.Text = "短语标识 / 简短名称";
                if (scriptLabel != null) scriptLabel.Text = "短语替换后的完整文本内容";
                if (iconInput != null && string.IsNullOrEmpty(iconInput.Text)) iconInput.Text = "📋";
                break;
            case "applescript":
                if (mainLabel != null) mainLabel.Text = "脚本简述";
                if (scriptLabel != null) scriptLabel.Text = "AppleScript 苹果脚本源码";
                if (iconInput != null && string.IsNullOrEmpty(iconInput.Text)) iconInput.Text = "🍎";
                break;
            case "shell":
                if (mainLabel != null) mainLabel.Text = "命令简述";
                if (scriptLabel != null) scriptLabel.Text = "Shell 终端脚本命令";
                if (iconInput != null && string.IsNullOrEmpty(iconInput.Text)) iconInput.Text = "💻";
                break;
            case "shortcut":
                if (mainLabel != null) mainLabel.Text = "模拟按键组合 (例如: cmd+c, cmd+shift+4, alt+space)";
                if (mainInput != null) mainInput.Watermark = "例如: cmd+c, cmd+v, cmd+shift+4";
                if (iconInput != null && string.IsNullOrEmpty(iconInput.Text)) iconInput.Text = "⌨️";
                break;
        }

        UpdatePresetsForType(type);
    }

    private void SetSelectedClass(Border? border, bool selected)
    {
        if (border == null) return;
        if (selected)
        {
            if (!border.Classes.Contains("selected")) border.Classes.Add("selected");
        }
        else
        {
            border.Classes.Remove("selected");
        }
    }

    private void SetupPresets()
    {
        UpdatePresetsForType("url");
    }

    private void UpdatePresetsForType(string type)
    {
        var wrap = this.FindControl<WrapPanel>("PresetsWrapPanel");
        if (wrap == null) return;
        wrap.Children.Clear();

        List<(string Name, string Main, string? Extra, string Icon, string Desc)> presets = new();

        if (type == "url")
        {
            presets.Add(("燕子官网", "https://yanzi.luoluoluo.cc.cd", null, "🌐", "打开燕子官方网站"));
            presets.Add(("GitHub", "https://github.com/luoluoluo22/yanzi", null, "🐙", "打开 GitHub 仓库"));
            presets.Add(("系统设置", "x-apple.systempreferences:", null, "⚙️", "打开 macOS 系统设置"));
            presets.Add(("访达", "Finder", null, "📁", "打开访达"));
            presets.Add(("终端", "Terminal", null, "💻", "启动终端"));
            presets.Add(("下载文件夹", "~/Downloads", null, "📥", "打开下载目录"));
        }
        else if (type == "search")
        {
            presets.Add(("Google 搜索", "https://www.google.com/search?q={query}", null, "🔍", "Google 网页搜索"));
            presets.Add(("百度搜索", "https://www.baidu.com/s?wd={query}", null, "🔍", "百度 网页搜索"));
            presets.Add(("Bing 搜索", "https://www.bing.com/search?q={query}", null, "🔍", "微软必应搜索"));
            presets.Add(("GitHub 搜索", "https://github.com/search?q={query}", null, "🐙", "GitHub 源码与仓库搜索"));
            presets.Add(("哔哩哔哩", "https://search.bilibili.com/all?keyword={query}", null, "📺", "Bilibili 视频搜索"));
            presets.Add(("知乎", "https://www.zhihu.com/search?q={query}", null, "📖", "知乎问答搜索"));
        }
        else if (type == "applescript")
        {
            presets.Add(("静音切换", "静音", "set volume output muted (not (output muted of (get volume settings)))", "🔇", "切换系统静音状态"));
            presets.Add(("锁定屏幕", "锁屏", "tell application \"System Events\" to keystroke \"q\" using {command down, control down}", "🔒", "快速锁屏"));
            presets.Add(("清空废纸篓", "清空废纸篓", "tell application \"Finder\" to empty trash", "🗑️", "清空 macOS 废纸篓"));
        }
        else if (type == "shell")
        {
            presets.Add(("查看公网 IP", "查看 IP", "curl -s ipinfo.io/ip", "🌐", "获取当前公网 IP 地址"));
            presets.Add(("刷新 DNS 缓存", "DNS 缓存", "sudo dscacheutil -flushcache; sudo killall -HUP mDNSResponder", "🔄", "刷新 macOS 系统 DNS"));
        }
        else if (type == "snippet")
        {
            presets.Add(("我的邮箱", "常用邮箱", "my_email@example.com", "✉️", "自动填充常用电子邮箱"));
            presets.Add(("当前时间", "当前时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "⏱️", "自动粘贴当前格式化时间戳"));
        }

        foreach (var p in presets)
        {
            var btn = new Button
            {
                Content = p.Name,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4),
                FontSize = 11.5,
                CornerRadius = new CornerRadius(8),
                Background = Application.Current?.FindResource("BrushSecondaryBtnBG") as IBrush,
                Foreground = Application.Current?.FindResource("BrushTextMain") as IBrush,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            btn.Click += (s, e) =>
            {
                var mainInput = this.FindControl<TextBox>("MainFieldInput");
                var scriptInput = this.FindControl<TextBox>("ScriptFieldInput");
                var titleInput = this.FindControl<TextBox>("TitleInput");
                var iconInput = this.FindControl<TextBox>("IconInput");
                var descInput = this.FindControl<TextBox>("DescInput");

                if (mainInput != null) mainInput.Text = p.Main;
                if (scriptInput != null && p.Extra != null) scriptInput.Text = p.Extra;
                if (titleInput != null) titleInput.Text = p.Name;
                if (iconInput != null) iconInput.Text = p.Icon;
                if (descInput != null) descInput.Text = p.Desc;
            };
            wrap.Children.Add(btn);
        }
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        // Set sample default or path
        var mainInput = this.FindControl<TextBox>("MainFieldInput");
        if (mainInput != null) mainInput.Text = "/Applications";
    }

    private async void OnCopyAiPromptClick(object? sender, RoutedEventArgs e)
    {
        var title = this.FindControl<TextBox>("TitleInput")?.Text ?? "自定义小程序";
        var desc = this.FindControl<TextBox>("DescInput")?.Text ?? string.Empty;
        var prompt = $"请为 macOS 燕子效率工具编写一个 {_currentType} 类型的自定义小程序：\n名称：{title}\n描述：{desc}\n请输出可用的 AppleScript 或 Shell 执行命令与参数。";
        if (Clipboard != null)
        {
            await Clipboard.SetTextAsync(prompt);
            ShowToast("📋 AI 提示词已复制到剪贴板！");
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var titleInput = this.FindControl<TextBox>("TitleInput")?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(titleInput))
        {
            ShowToast("⚠️ 请先输入小程序标题");
            return;
        }

        var mainInput = this.FindControl<TextBox>("MainFieldInput")?.Text?.Trim() ?? string.Empty;
        var scriptInput = this.FindControl<TextBox>("ScriptFieldInput")?.Text?.Trim() ?? string.Empty;
        var iconInput = this.FindControl<TextBox>("IconInput")?.Text?.Trim() ?? "🌐";
        var descInput = this.FindControl<TextBox>("DescInput")?.Text?.Trim() ?? string.Empty;
        var catInput = this.FindControl<TextBox>("CategoryInput")?.Text?.Trim() ?? "快捷工具";
        var abbrevInput = this.FindControl<TextBox>("AbbrevInput")?.Text?.Trim() ?? string.Empty;

        var command = new CommandItem
        {
            ExtensionId = $"custom-{Guid.NewGuid():N}",
            Title = titleInput,
            Glyph = iconInput,
            Description = descInput,
            Category = catInput,
            Abbreviation = abbrevInput
        };

        if (_currentType == "url" || _currentType == "search")
        {
            command.ActionKind = CommandActionKind.OpenUrl;
            command.Url = mainInput;
        }
        else if (_currentType == "applescript")
        {
            command.ActionKind = CommandActionKind.AppleScript;
            command.ScriptSource = string.IsNullOrWhiteSpace(scriptInput) ? mainInput : scriptInput;
        }
        else if (_currentType == "shell")
        {
            command.ActionKind = CommandActionKind.ShellScript;
            command.ScriptSource = string.IsNullOrWhiteSpace(scriptInput) ? mainInput : scriptInput;
        }
        else if (_currentType == "snippet")
        {
            command.ActionKind = CommandActionKind.Snippet;
            command.SnippetText = string.IsNullOrWhiteSpace(scriptInput) ? mainInput : scriptInput;
        }
        else
        {
            command.ActionKind = CommandActionKind.KeyboardShortcut;
            command.ShortcutKey = mainInput;
        }

        ResultCommand = command;
        _mainWindow?.AddCustomExtension(command);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowToast(string msg)
    {
        var toast = this.FindControl<TextBlock>("StatusText");
        if (toast != null) toast.Text = msg;
    }
}
