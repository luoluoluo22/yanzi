using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text.Encodings.Web;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public partial class LauncherWindow : Window, INotifyPropertyChanged
{
    private readonly ICommandActionExecutor _commandActionExecutor;
    private readonly MainWindow? _mainWindow;
    private readonly string _customExtensionsFilePath;

    private double _windowWidth = 620;
    private string _searchText = string.Empty;
    private string _editorJsonText = string.Empty;
    private string _validationMessage = string.Empty;
    private IBrush _validationColor = Brushes.Gray;
    private string _activeCategory = "All";
    
    private LauncherItemViewModel? _selectedItem;
    private readonly List<LauncherItemViewModel> _allItems = [];
    private readonly List<AppInfo> _cachedApps = [];
    private readonly List<CommandItem> _customExtensions = [];
    private readonly string[] _searchPaths;

    public new event PropertyChangedEventHandler? PropertyChanged;

    public double WindowWidth
    {
        get => _windowWidth;
        set => SetField(ref _windowWidth, value);
    }

    public bool IsAllActive => _activeCategory == "All";
    public bool IsAppActive => _activeCategory == "App";
    public bool IsFileActive => _activeCategory == "File";
    public bool IsExtensionActive => _activeCategory == "Extension";
    public bool IsClipboardActive => _activeCategory == "Clipboard";
    public bool IsSnippetActive => _activeCategory == "Snippet";

    public bool IsAccessibilityWarningVisible
    {
        get
        {
            if (System.OperatingSystem.IsMacOS())
            {
                try
                {
                    return !AXIsProcessTrusted();
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                // Check abbreviation space triggers
                if (!string.IsNullOrEmpty(value) && value.EndsWith(" "))
                {
                    var abbreviationQuery = value.Trim();
                    var matchingSnippet = _customExtensions.FirstOrDefault(ext => 
                        !string.IsNullOrEmpty(ext.Abbreviation) && 
                        ext.Abbreviation.Equals(abbreviationQuery, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(ext.SnippetText));
                        
                    if (matchingSnippet != null)
                    {
                        _searchText = string.Empty;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
                        
                        Hide();
                        
                        Task.Run(() =>
                        {
                            Dispatcher.UIThread.Post(async () =>
                            {
                                var clipboard = this.Clipboard;
                                if (clipboard != null)
                                {
                                    await clipboard.SetTextAsync(matchingSnippet.SnippetText);
                                }
                            });

                            Thread.Sleep(120);

                            var cmdV = new CommandItem
                            {
                                ActionKind = CommandActionKind.KeyboardShortcut,
                                ShortcutKey = "v",
                                ShortcutCommand = true,
                                ShortcutShift = false,
                                ShortcutOption = false,
                                ShortcutControl = false
                            };
                            _commandActionExecutor.Execute(cmdV);
                        });
                        
                        return;
                    }
                }

                FilterItems();
            }
        }
    }

    public string EditorJsonText
    {
        get => _editorJsonText;
        set => SetField(ref _editorJsonText, value);
    }

    private bool _isSnippetEditor;
    public bool IsSnippetEditor
    {
        get => _isSnippetEditor;
        set => SetField(ref _isSnippetEditor, value);
    }

    private string _snippetName = string.Empty;
    public string SnippetName
    {
        get => _snippetName;
        set => SetField(ref _snippetName, value);
    }

    private string _snippetAbbreviation = string.Empty;
    public string SnippetAbbreviation
    {
        get => _snippetAbbreviation;
        set => SetField(ref _snippetAbbreviation, value);
    }

    private string _snippetText = string.Empty;
    public string SnippetText
    {
        get => _snippetText;
        set => SetField(ref _snippetText, value);
    }

    private string _snippetDescription = string.Empty;
    public string SnippetDescription
    {
        get => _snippetDescription;
        set => SetField(ref _snippetDescription, value);
    }

    private string _snippetIcon = "📝";
    public string SnippetIcon
    {
        get => _snippetIcon;
        set => SetField(ref _snippetIcon, value);
    }

    private string _editorTitle = "添加自定义扩展 (JSON)";
    public string EditorTitle
    {
        get => _editorTitle;
        set => SetField(ref _editorTitle, value);
    }

    private bool _isDeleteMode;
    public bool IsDeleteMode
    {
        get => _isDeleteMode;
        set => SetField(ref _isDeleteMode, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        set => SetField(ref _validationMessage, value);
    }

    public IBrush ValidationColor
    {
        get => _validationColor;
        set => SetField(ref _validationColor, value);
    }

    public LauncherItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                UpdateEditorForSelected();
            }
        }
    }

    public ObservableCollection<LauncherItemViewModel> FilteredItems { get; } = [];

    public LauncherWindow()
        : this(new DisabledCommandActionExecutor(), null!)
    {
    }

    public LauncherWindow(ICommandActionExecutor commandActionExecutor, MainWindow mainWindow)
    {
        InitializeComponent();
        _commandActionExecutor = commandActionExecutor;
        _mainWindow = mainWindow;

        _customExtensionsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".yanzi_custom_extensions.json"
        );

        _searchPaths = [
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        ];

        DataContext = this;

        // Register tunneling KeyDown handler to intercept keyboard events (e.g. Tab) before focus manager handles them
        this.AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

        // Load Custom Extensions from File
        LoadCustomExtensions();

        // Start scanning Applications in the background
        Task.Run(CacheApplications);

        // Pre-fill JSON Editor with template
        EditorJsonText = GetDefaultJsonTemplate();

        // Populate initial static items
        BuildStaticItems();

        // Subscribe to clipboard history change events
        if (_mainWindow?.ClipboardMonitor != null)
        {
            _mainWindow.ClipboardMonitor.HistoryChanged += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_activeCategory == "Clipboard")
                    {
                        FilterItems();
                    }
                });
            };
        }

        this.Activated += (s, e) =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAccessibilityWarningVisible)));
        };

        FilterItems();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BuildStaticItems()
    {
        _allItems.Clear();

        // Add special static "Add New" placeholder items at the beginning of Snippet and Extension lists
        _allItems.Add(new LauncherItemViewModel
        {
            Title = "＋ 添加新短语",
            Subtitle = "定义一个新的快捷文本短语，点击或回车在此面板添加",
            DisplayIcon = "➕",
            AccentBrush = new SolidColorBrush(Color.Parse("#FF8B5CF6")),
            KindText = "新建",
            CategoryText = "短语",
            Category = "Snippet",
            Command = null
        });

        _allItems.Add(new LauncherItemViewModel
        {
            Title = "＋ 添加新扩展",
            Subtitle = "定义一个新的 AppleScript 苹果脚本内容，点击或回车在此面板添加",
            DisplayIcon = "➕",
            AccentBrush = new SolidColorBrush(Color.Parse("#FF10B981")),
            KindText = "新建",
            CategoryText = "扩展",
            Category = "Extension",
            Command = null
        });

        // 1. Add Custom AppleScript / Snippet Extensions
        foreach (var ext in _customExtensions)
        {
            var isSnippet = ext.ActionKind == CommandActionKind.Snippet;
            var subText = ext.Description ?? (isSnippet ? "快捷输入短语" : "自定义苹果脚本");
            if (!string.IsNullOrEmpty(ext.GlobalHotkey))
            {
                subText += $" [⌥: {ext.GlobalHotkey}]";
            }
            if (!string.IsNullOrEmpty(ext.Abbreviation))
            {
                subText += $" [简写: {ext.Abbreviation}]";
            }

            _allItems.Add(new LauncherItemViewModel
            {
                Title = ext.Title,
                Subtitle = subText,
                DisplayIcon = ext.Glyph ?? (isSnippet ? "📝" : "🍎"),
                AccentBrush = new SolidColorBrush(Color.Parse(isSnippet ? "#FF8B5CF6" : "#FF10B981")),
                KindText = isSnippet ? "短语" : "自定义",
                CategoryText = isSnippet ? "短语" : "扩展",
                Category = isSnippet ? "Snippet" : "Extension",
                Command = ext
            });
        }

        // 2. Add System Default Commands (from MainWindow)
        if (_mainWindow != null)
        {
            var defaultCommands = _mainWindow.GetRadialMenuCommandCandidates(string.Empty);
            foreach (var cmd in defaultCommands)
            {
                _allItems.Add(new LauncherItemViewModel
                {
                    Title = cmd.Title,
                    Subtitle = cmd.Description ?? cmd.ApplicationName ?? "内置系统命令",
                    DisplayIcon = cmd.Glyph ?? "⚙",
                    AccentBrush = new SolidColorBrush(Color.Parse("#FF3B82F6")),
                    KindText = "系统",
                    CategoryText = "扩展",
                    Category = "Extension",
                    Command = cmd
                });
            }
        }
    }

    private async Task CacheApplications()
    {
        try
        {
            var appsList = new List<AppInfo>();
            
            // Standard macOS App directories
            string[] appPaths = ["/Applications", "/System/Applications"];
            foreach (var path in appPaths)
            {
                if (!Directory.Exists(path)) continue;

                foreach (var dir in Directory.GetDirectories(path, "*.app", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(dir);
                    appsList.Add(new AppInfo(name, dir));
                }

                // Add utilities (standard macOS apps nested inside Utilities)
                string utilPath = Path.Combine(path, "Utilities");
                if (Directory.Exists(utilPath))
                {
                    foreach (var dir in Directory.GetDirectories(utilPath, "*.app", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(dir);
                        appsList.Add(new AppInfo(name, dir));
                    }
                }
            }

            lock (_cachedApps)
            {
                _cachedApps.Clear();
                _cachedApps.AddRange(appsList);
            }

            // Refresh file list & UI items safely on UI thread
            await Dispatcher.UIThread.InvokeAsync(() => {
                RefreshAppItems();
                FilterItems();
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to cache applications: {ex.Message}");
        }
    }

    private void RefreshAppItems()
    {
        // Remove old app items
        _allItems.RemoveAll(item => item.Category == "App");

        // Add newly cached apps
        lock (_cachedApps)
        {
            foreach (var app in _cachedApps)
            {
                global::Avalonia.Media.Imaging.Bitmap? realIcon = null;
                try
                {
                    var pngBytes = MacIconExtractor.GetFileIconPngBytes(app.Path);
                    if (pngBytes != null && pngBytes.Length > 0)
                    {
                        using var ms = new System.IO.MemoryStream(pngBytes);
                        realIcon = new global::Avalonia.Media.Imaging.Bitmap(ms);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading icon for {app.Name}: {ex.Message}");
                }

                _allItems.Add(new LauncherItemViewModel
                {
                    Title = app.Name,
                    Subtitle = app.Path,
                    DisplayIcon = "📱",
                    RealIcon = realIcon,
                    AccentBrush = new SolidColorBrush(Color.Parse("#FF8B5CF6")),
                    KindText = "应用",
                    CategoryText = "应用",
                    Category = "App",
                    AppPath = app.Path
                });
            }
        }
    }

    private void LoadCustomExtensions()
    {
        _customExtensions.Clear();
        try
        {
            if (!File.Exists(_customExtensionsFilePath))
            {
                var defaultExamples = new List<CustomExtensionDto>
                {
                    new CustomExtensionDto
                    {
                        name = "常用邮箱短语",
                        description = "我的个人常用电子邮件短语，输入 'em ' 即可快速插入",
                        icon = "✉️",
                        script = string.Empty,
                        globalHotkey = string.Empty,
                        abbreviation = "em",
                        snippet = "myemail@example.com"
                    },
                    new CustomExtensionDto
                    {
                        name = "苹果弹窗测试",
                        description = "执行苹果脚本弹窗示例，支持设定全局快捷键和缩写指令",
                        icon = "🍎",
                        script = "display dialog \"燕子启动器：苹果脚本自定义扩展运行成功！\" buttons {\"确定\"} default button \"确定\"",
                        globalHotkey = "ctrl+shift+t",
                        abbreviation = "tc",
                        snippet = string.Empty
                    }
                };

                var defaultJson = JsonSerializer.Serialize(defaultExamples, new JsonSerializerOptions 
                { 
                    WriteIndented = true, 
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                });
                File.WriteAllText(_customExtensionsFilePath, defaultJson);
            }

            if (File.Exists(_customExtensionsFilePath))
            {
                var json = File.ReadAllText(_customExtensionsFilePath);
                var items = JsonSerializer.Deserialize<List<CustomExtensionDto>>(json);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var isSnippet = !string.IsNullOrEmpty(item.snippet);
                        _customExtensions.Add(new CommandItem
                        {
                            ExtensionId = $"custom_{Guid.NewGuid()}",
                            Title = item.name,
                            Description = item.description,
                            Glyph = string.IsNullOrEmpty(item.icon) ? (isSnippet ? "📝" : "🍎") : item.icon,
                            ActionKind = isSnippet ? CommandActionKind.Snippet : CommandActionKind.AppleScript,
                            ScriptSource = item.script,
                            SnippetText = item.snippet,
                            Abbreviation = item.abbreviation,
                            GlobalHotkey = item.globalHotkey
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load custom extensions: {ex.Message}");
        }

        _mainWindow?.RefreshSnippetAbbreviations();
    }

    public IReadOnlyList<CommandItem> GetCustomExtensions() => _customExtensions;

    public void AddCustomExtension(CommandItem command)
    {
        _customExtensions.Add(command);
        SaveCustomExtensions();
        BuildStaticItems();
        FilterItems();
    }

    private void SaveCustomExtensions()
    {
        try
        {
            var dtos = _customExtensions.Select(ext => new CustomExtensionDto
            {
                name = ext.Title,
                description = ext.Description ?? string.Empty,
                icon = ext.Glyph ?? "🍎",
                script = ext.ScriptSource ?? string.Empty,
                globalHotkey = ext.GlobalHotkey,
                abbreviation = ext.Abbreviation,
                snippet = ext.SnippetText
            }).ToList();

            var json = JsonSerializer.Serialize(dtos, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(_customExtensionsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save custom extensions: {ex.Message}");
        }

        _mainWindow?.RefreshSnippetAbbreviations();
    }

    private void FilterItems()
    {
        var query = SearchText.Trim();

        if (_activeCategory == "Clipboard")
        {
            var history = _mainWindow?.ClipboardMonitor?.History ?? Array.Empty<ClipboardHistoryItem>();
            var clipboardList = new List<LauncherItemViewModel>();
            
            foreach (var clip in history)
            {
                var displayTitle = clip.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (displayTitle.Length > 50)
                {
                    displayTitle = displayTitle[..47] + "...";
                }
                
                var timeStr = clip.Timestamp.ToString("HH:mm:ss");

                clipboardList.Add(new LauncherItemViewModel
                {
                    Title = displayTitle,
                    Subtitle = clip.Text,
                    DisplayIcon = "📋",
                    AccentBrush = new SolidColorBrush(Color.Parse("#FF10B981")),
                    KindText = "剪贴板",
                    CategoryText = timeStr,
                    Category = "Clipboard"
                });
            }

            var clipEnumerable = clipboardList.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                clipEnumerable = clipEnumerable.Where(item =>
                    item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            FilteredItems.Clear();
            foreach (var item in clipEnumerable.Take(40))
            {
                FilteredItems.Add(item);
            }

            SelectedItem = FilteredItems.Count > 0 ? FilteredItems[0] : null;
            return;
        }
        
        // Scan matching local Files in user directories asynchronously
        if ((_activeCategory == "File") || (query.Length >= 2 && _activeCategory == "All"))
        {
            Task.Run(() => SearchLocalFiles(query));
        }

        var list = _allItems.AsEnumerable();

        // 1. Filter Category
        if (_activeCategory != "All")
        {
            list = list.Where(item => item.Category.Equals(_activeCategory, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // If Category is All, filter out the special "+ 添加新" static creation helper items
            list = list.Where(item => item.Command != null);
        }

        // 2. Filter Search Query
        if (!string.IsNullOrWhiteSpace(query))
        {
            list = list.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.CategoryText.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredItems.Clear();

        // 1. Math Calculator integration
        if (IsMathExpression(query))
        {
            var mathResult = EvaluateMathExpression(query);
            if (mathResult != null)
            {
                FilteredItems.Add(new LauncherItemViewModel
                {
                    Title = mathResult,
                    Subtitle = $"计算结果 (公式: {query})",
                    DisplayIcon = "🔢",
                    AccentBrush = new SolidColorBrush(Color.Parse("#FFF59E0B")),
                    KindText = "计算器",
                    CategoryText = "计算器",
                    Category = "Calculator"
                });
            }
        }

        foreach (var item in list.Take(40))
        {
            FilteredItems.Add(item);
        }

        if (FilteredItems.Count > 0)
        {
            SelectedItem = FilteredItems[0];
        }
        else
        {
            SelectedItem = null;
        }
    }

    private void SearchLocalFiles(string query)
    {
        try
        {
            var fileItems = new List<LauncherItemViewModel>();
            var searchPattern = string.IsNullOrEmpty(query) ? "*" : $"*{query}*";

            foreach (var path in _searchPaths)
            {
                if (!Directory.Exists(path)) continue;

                var files = Directory.EnumerateFileSystemEntries(path, searchPattern, SearchOption.TopDirectoryOnly);
                foreach (var file in files.Take(12))
                {
                    var isDir = Directory.Exists(file);
                    var name = Path.GetFileName(file);

                    global::Avalonia.Media.Imaging.Bitmap? fileIcon = null;
                    try
                    {
                        var pngBytes = Dispatcher.UIThread.Invoke(() => MacIconExtractor.GetFileIconPngBytes(file));
                        if (pngBytes != null && pngBytes.Length > 0)
                        {
                            using var ms = new System.IO.MemoryStream(pngBytes);
                            fileIcon = new global::Avalonia.Media.Imaging.Bitmap(ms);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading icon for file {name}: {ex.Message}");
                    }

                    fileItems.Add(new LauncherItemViewModel
                    {
                        Title = name,
                        Subtitle = file,
                        DisplayIcon = isDir ? "📁" : "📄",
                        RealIcon = fileIcon,
                        AccentBrush = new SolidColorBrush(Color.Parse("#FFF59E0B")),
                        KindText = isDir ? "文件夹" : "文件",
                        CategoryText = "文件",
                        Category = "File",
                        FilePath = file
                    });
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                // Remove old File items
                _allItems.RemoveAll(item => item.Category == "File");
                
                // Add newly matched files
                _allItems.AddRange(fileItems);

                // Re-run standard filter to display
                var finalQuery = SearchText.Trim();
                var list = _allItems.AsEnumerable();

                if (_activeCategory != "All")
                {
                    list = list.Where(item => item.Category.Equals(_activeCategory, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(finalQuery))
                {
                    list = list.Where(item =>
                        item.Title.Contains(finalQuery, StringComparison.OrdinalIgnoreCase) ||
                        item.Subtitle.Contains(finalQuery, StringComparison.OrdinalIgnoreCase));
                }

                FilteredItems.Clear();
                foreach (var item in list.Take(40))
                {
                    FilteredItems.Add(item);
                }

                if (FilteredItems.Count > 0 && SelectedItem == null)
                {
                    SelectedItem = FilteredItems[0];
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"File search failed: {ex.Message}");
        }
    }

    public void SwitchCategory(string category)
    {
        _activeCategory = category;
        
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAllActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAppActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFileActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExtensionActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsClipboardActive)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSnippetActive)));
        
        FilterItems();
    }

    public void SwitchCategoryCommand(string category) => SwitchCategory(category);
    public void ToggleEditorCommand() => ToggleEditor();
    public void ExecuteSelectedCommand() => ExecuteSelected();
    public void SaveCustomExtensionCommand() => SaveCustomExtension();
    public void SaveCustomSnippetCommand() => SaveCustomSnippet();
    public void DeleteCustomExtensionCommand() => DeleteCustomExtension();
    public void CopyPromptCommand() => CopyPrompt();

    private void CopyPrompt()
    {
        var prompt = "请帮我写一个燕子启动器 (Yanzi) 的单文件 JSON 扩展。要求格式如下：\n{\n  \"name\": \"扩展名称\",\n  \"description\": \"描述\",\n  \"icon\": \"🍎\",\n  \"script\": \"这里是单行 AppleScript\",\n  \"globalHotkey\": \"\",\n  \"abbreviation\": \"\",\n  \"snippet\": \"\"\n}";
        Task.Run(async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(prompt);
                Dispatcher.UIThread.Post(() =>
                {
                    ValidationMessage = "已复制提示词到剪贴板！";
                    ValidationColor = Brushes.Green;
                    Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                    {
                        if (ValidationMessage == "已复制提示词到剪贴板！")
                        {
                            ValidationMessage = string.Empty;
                            ValidationColor = Brushes.Gray;
                        }
                    }));
                });
            }
        });
    }

    private void ToggleEditor()
    {
        if (WindowWidth == 620)
        {
            if (_activeCategory == "Snippet")
            {
                IsSnippetEditor = true;
                EditorTitle = "添加自定义短语";
                IsDeleteMode = false;
                
                SnippetName = string.Empty;
                SnippetAbbreviation = string.Empty;
                SnippetText = string.Empty;
                SnippetDescription = string.Empty;
                SnippetIcon = "📝";
            }
            else
            {
                IsSnippetEditor = false;
                EditorTitle = "添加自定义扩展 (JSON)";
                IsDeleteMode = false;
                EditorJsonText = GetDefaultJsonTemplate();
            }
            WindowWidth = 980;
        }
        else
        {
            WindowWidth = 620;
        }
    }

    private void ExecuteSelected()
    {
        if (SelectedItem == null)
            return;

        var item = SelectedItem;
        if (item.Command == null && item.Title.Contains("添加新"))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (WindowWidth == 620)
                {
                    ToggleEditor();
                }
                var editor = this.FindControl<TextBox>("JsonEditorInput");
                editor?.Focus();
            });
            return;
        }

        Hide(); // Hide the launcher immediately

        Task.Run(() =>
        {
            try
            {
                if (item.Category == "Extension" && item.Command != null)
                {
                    _commandActionExecutor.Execute(item.Command);
                }
                else if (item.Category == "Clipboard" && !string.IsNullOrEmpty(item.Subtitle))
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var clipboard = this.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(item.Subtitle);
                        }
                    });

                    Thread.Sleep(120);

                    var cmdV = new CommandItem
                    {
                        ActionKind = CommandActionKind.KeyboardShortcut,
                        ShortcutKey = "v",
                        ShortcutCommand = true,
                        ShortcutShift = false,
                        ShortcutOption = false,
                        ShortcutControl = false
                    };
                    _commandActionExecutor.Execute(cmdV);
                }
                else if (item.Category == "Calculator" && !string.IsNullOrEmpty(item.Title))
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var clipboard = this.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(item.Title);
                        }
                    });

                    Thread.Sleep(120);

                    var cmdV = new CommandItem
                    {
                        ActionKind = CommandActionKind.KeyboardShortcut,
                        ShortcutKey = "v",
                        ShortcutCommand = true,
                        ShortcutShift = false,
                        ShortcutOption = false,
                        ShortcutControl = false
                    };
                    _commandActionExecutor.Execute(cmdV);
                }
                else if (item.Category == "Snippet" && item.Command != null && !string.IsNullOrEmpty(item.Command.SnippetText))
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var clipboard = this.Clipboard;
                        if (clipboard != null)
                        {
                            await clipboard.SetTextAsync(item.Command.SnippetText);
                        }
                    });

                    Thread.Sleep(120);

                    var cmdV = new CommandItem
                    {
                        ActionKind = CommandActionKind.KeyboardShortcut,
                        ShortcutKey = "v",
                        ShortcutCommand = true,
                        ShortcutShift = false,
                        ShortcutOption = false,
                        ShortcutControl = false
                    };
                    _commandActionExecutor.Execute(cmdV);
                }
                else if (item.Category == "App" && !string.IsNullOrEmpty(item.AppPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/usr/bin/open",
                        ArgumentList = { "-a", item.AppPath },
                        UseShellExecute = false
                    });
                }
                else if (item.Category == "File" && !string.IsNullOrEmpty(item.FilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/usr/bin/open",
                        ArgumentList = { item.FilePath },
                        UseShellExecute = false
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to execute item: {ex.Message}");
            }
        });
    }

    private void SaveCustomSnippet()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SnippetName))
            {
                ValidationMessage = "保存失败：'短语名称' 为必填项！";
                ValidationColor = Brushes.Red;
                return;
            }

            if (string.IsNullOrWhiteSpace(SnippetText))
            {
                ValidationMessage = "保存失败：'短语内容' 为必填项！";
                ValidationColor = Brushes.Red;
                return;
            }

            // Remove existing custom extension if it has the same name
            _customExtensions.RemoveAll(ext => ext.Title.Equals(SnippetName.Trim(), StringComparison.OrdinalIgnoreCase));

            // Add new custom extension as Snippet
            _customExtensions.Add(new CommandItem
            {
                ExtensionId = $"custom_{Guid.NewGuid()}",
                Title = SnippetName.Trim(),
                Description = string.IsNullOrWhiteSpace(SnippetDescription) ? "快捷输入短语" : SnippetDescription.Trim(),
                Glyph = string.IsNullOrWhiteSpace(SnippetIcon) ? "📝" : SnippetIcon.Trim(),
                ActionKind = CommandActionKind.Snippet,
                SnippetText = SnippetText,
                Abbreviation = string.IsNullOrWhiteSpace(SnippetAbbreviation) ? string.Empty : SnippetAbbreviation.Trim(),
                GlobalHotkey = string.Empty,
                ScriptSource = string.Empty
            });

            // Save & Reload
            SaveCustomExtensions();
            BuildStaticItems();
            FilterItems();

            ValidationMessage = $"成功保存并添加短语：'{SnippetName.Trim()}'";
            ValidationColor = Brushes.Green;

            // Reset validation message after 2 seconds
            Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                ValidationMessage = string.Empty;
                ValidationColor = Brushes.Gray;
            }));
        }
        catch (Exception ex)
        {
            ValidationMessage = $"保存失败：{ex.Message}";
            ValidationColor = Brushes.Red;
        }
    }

    private void SaveCustomExtension()
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CustomExtensionDto>(EditorJsonText);
            if (dto == null || string.IsNullOrWhiteSpace(dto.name))
            {
                ValidationMessage = "保存失败：'name' 字段为必填项！";
                ValidationColor = Brushes.Red;
                return;
            }

            var isSnippet = !string.IsNullOrEmpty(dto.snippet);
            if (!isSnippet && string.IsNullOrWhiteSpace(dto.script))
            {
                ValidationMessage = "保存失败：自定义脚本类型必须填写 'script' 字段！";
                ValidationColor = Brushes.Red;
                return;
            }

            // Remove existing custom extension if it has the same name
            _customExtensions.RemoveAll(ext => ext.Title.Equals(dto.name, StringComparison.OrdinalIgnoreCase));

            // Add new custom extension
            _customExtensions.Add(new CommandItem
            {
                ExtensionId = $"custom_{Guid.NewGuid()}",
                Title = dto.name,
                Description = dto.description,
                Glyph = string.IsNullOrWhiteSpace(dto.icon) ? (isSnippet ? "📝" : "🍎") : dto.icon,
                ActionKind = isSnippet ? CommandActionKind.Snippet : CommandActionKind.AppleScript,
                ScriptSource = dto.script,
                SnippetText = dto.snippet,
                Abbreviation = dto.abbreviation,
                GlobalHotkey = dto.globalHotkey
            });

            // Save & Reload
            SaveCustomExtensions();
            BuildStaticItems();
            FilterItems();

            ValidationMessage = $"成功保存并添加：'{dto.name}'";
            ValidationColor = Brushes.Green;

            // Reset template in textbox after 2 seconds
            Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                ValidationMessage = string.Empty;
                ValidationColor = Brushes.Gray;
            }));
        }
        catch (JsonException jex)
        {
            ValidationMessage = $"JSON 格式错误：{jex.Message}";
            ValidationColor = Brushes.Red;
        }
        catch (Exception ex)
        {
            ValidationMessage = $"保存失败：{ex.Message}";
            ValidationColor = Brushes.Red;
        }
    }

    private string GetDefaultJsonTemplate()
    {
        var dto = new CustomExtensionDto
        {
            name = "\u82f9\u679c\u5f39\u7a97\u6d4b\u8bd5", // "苹果弹窗测试"
            description = "\u6267\u884c\u82f9\u679c\u811a\u672c\u5f39\u7a97\u793a\u4f8b\uff0c\u652f\u6301\u8bbe\u5b9a\u5168\u5c40\u5feb\u6d77\u952e\u548c\u7f29\u5199\u6307\u4ee4", // "执行苹果脚本弹窗示例，支持设定全局快捷键和缩写指令"
            icon = "\ud83c\udf4e", // "🍎"
            script = "display dialog \"\u71d5\u5b50\u542f\u52a8\u5668\uff1a\u82f9\u679c\u811a\u672c\u81ea\u5b9a\u4e49\u6269\u5c55\u8fd0\u884c\u6210\u529f\uff01\" buttons {\"\u786e\u5b9a\"} default button \"\u786e\u5b9a\"", // "display dialog "燕子启动器：苹果脚本自定义扩展运行成功！" buttons {"确定"} default button "确定""
            globalHotkey = "ctrl+shift+t",
            abbreviation = "em",
            snippet = "myemail@example.com"
        };
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Hide();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            var shiftPressed = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            CycleCategoryTab(!shiftPressed);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            if (e.Key == Key.D1) { SwitchCategory("All"); e.Handled = true; }
            else if (e.Key == Key.D2) { SwitchCategory("App"); e.Handled = true; }
            else if (e.Key == Key.D3) { SwitchCategory("File"); e.Handled = true; }
            else if (e.Key == Key.D4) { SwitchCategory("Extension"); e.Handled = true; }
            else if (e.Key == Key.D5) { SwitchCategory("Clipboard"); e.Handled = true; }
            else if (e.Key == Key.D6) { SwitchCategory("Snippet"); e.Handled = true; }
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.Meta))
        {
            if (SelectedItem != null && SelectedItem.Command != null && SelectedItem.Command.ExtensionId.StartsWith("custom_"))
            {
                DeleteCustomExtension();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.K && e.KeyModifiers == KeyModifiers.Control)
        {
            // Ctrl K triggers categories or options if needed, here we toggle editor as a nice trick
            ToggleEditor();
            e.Handled = true;
        }
    }

    private void CycleCategoryTab(bool forward)
    {
        string[] categories = ["All", "App", "File", "Extension", "Clipboard", "Snippet"];
        int index = Array.IndexOf(categories, _activeCategory);
        if (index < 0) index = 0;

        if (forward)
        {
            index = (index + 1) % categories.Length;
        }
        else
        {
            index = (index - 1 + categories.Length) % categories.Length;
        }

        SwitchCategory(categories[index]);
    }

    private void UpdateEditorForSelected()
    {
        if (SelectedItem != null && SelectedItem.Command != null && SelectedItem.Command.ExtensionId.StartsWith("custom_"))
        {
            var cmd = SelectedItem.Command;
            
            if (cmd.ActionKind == CommandActionKind.Snippet)
            {
                IsSnippetEditor = true;
                SnippetName = cmd.Title;
                SnippetAbbreviation = cmd.Abbreviation ?? string.Empty;
                SnippetText = cmd.SnippetText ?? string.Empty;
                SnippetDescription = cmd.Description ?? string.Empty;
                SnippetIcon = cmd.Glyph ?? "📝";
                
                EditorTitle = "修改自定义短语";
            }
            else
            {
                IsSnippetEditor = false;
                var dto = new CustomExtensionDto
                {
                    name = cmd.Title,
                    description = cmd.Description ?? string.Empty,
                    icon = cmd.Glyph ?? "🍎",
                    script = cmd.ScriptSource ?? string.Empty,
                    globalHotkey = cmd.GlobalHotkey,
                    abbreviation = cmd.Abbreviation,
                    snippet = cmd.SnippetText
                };

                try
                {
                    EditorJsonText = JsonSerializer.Serialize(dto, new JsonSerializerOptions 
                    { 
                        WriteIndented = true, 
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    });
                }
                catch {}

                EditorTitle = "修改自定义扩展 (JSON)";
            }
            IsDeleteMode = true;
        }
        else
        {
            IsDeleteMode = false;
            if (_activeCategory == "Snippet")
            {
                IsSnippetEditor = true;
                EditorTitle = "添加自定义短语";
                SnippetName = string.Empty;
                SnippetAbbreviation = string.Empty;
                SnippetText = string.Empty;
                SnippetDescription = string.Empty;
                SnippetIcon = "📝";
            }
            else
            {
                IsSnippetEditor = false;
                EditorTitle = "添加自定义扩展 (JSON)";
                if (string.IsNullOrEmpty(EditorJsonText) || EditorJsonText == GetDefaultJsonTemplate())
                {
                    EditorJsonText = GetDefaultJsonTemplate();
                }
            }
        }
    }

    private void DeleteCustomExtension()
    {
        if (SelectedItem != null && SelectedItem.Command != null && SelectedItem.Command.ExtensionId.StartsWith("custom_"))
        {
            var nameToDelete = SelectedItem.Command.Title;
            _customExtensions.RemoveAll(ext => ext.Title.Equals(nameToDelete, StringComparison.OrdinalIgnoreCase));
            SaveCustomExtensions();
            BuildStaticItems();
            FilterItems();
            
            // Slide editor closed
            WindowWidth = 620;
            
            ValidationMessage = $"已成功删除扩展：'{nameToDelete}'";
            ValidationColor = Brushes.Orange;
            Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                ValidationMessage = string.Empty;
                ValidationColor = Brushes.Gray;
            }));
        }
    }

    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string category)
        {
            SwitchCategory(category);
        }
    }

    public void OnAccessibilityWarningClick(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start("open", "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open accessibility system preferences: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button?.ContextMenu != null)
        {
            button.ContextMenu.Open(button);
        }
    }

    private void OnCycleTabClick(object? sender, RoutedEventArgs e) => CycleCategoryTab(true);

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e)
    {
        Hide();
        if (_mainWindow != null)
        {
            var settingsWindow = new SettingsWindow(_mainWindow);
            settingsWindow.Show();
            settingsWindow.Activate();
        }
    }

    private void OnToggleEditorClick(object? sender, RoutedEventArgs e) => ToggleEditor();

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private void MoveSelection(int direction)
    {
        if (FilteredItems.Count == 0)
            return;

        var currentIndex = FilteredItems.IndexOf(SelectedItem ?? FilteredItems[0]);
        var nextIndex = currentIndex + direction;

        if (nextIndex >= 0 && nextIndex < FilteredItems.Count)
        {
            SelectedItem = FilteredItems[nextIndex];
            
            // Scroll selected item into view safely
            var resultsList = this.FindControl<ListBox>("ResultsList");
            if (resultsList != null)
            {
                var selectedContainer = resultsList.ContainerFromIndex(nextIndex);
                if (selectedContainer != null)
                {
                    resultsList.ScrollIntoView(SelectedItem);
                }
            }
        }
    }

    private static bool IsMathExpression(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        bool hasDigit = text.Any(char.IsDigit);
        bool hasOperator = text.Any(c => c == '+' || c == '-' || c == '*' || c == '/' || c == '(' || c == ')' || c == '%');
        bool hasLetters = text.Any(c => char.IsLetter(c) && c != 'p' && c != 'i' && c != 'e'); // allow pi, e
        
        return hasDigit && hasOperator && !hasLetters;
    }

    private static string? EvaluateMathExpression(string text)
    {
        try
        {
            var dt = new System.Data.DataTable();
            var result = dt.Compute(text, "");
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private class CustomExtensionDto
    {
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string icon { get; set; } = "🍎";
        public string script { get; set; } = string.Empty;
        public string? globalHotkey { get; set; }
        public string? abbreviation { get; set; }
        public string? snippet { get; set; }
    }

    private record AppInfo(string Name, string Path);
}

public class LauncherItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isHovered;

    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string DisplayIcon { get; set; } = "🍎";
    public IBrush AccentBrush { get; set; } = Brushes.Blue;
    public string KindText { get; set; } = "系统";
    public string CategoryText { get; set; } = "扩展";
    public string Category { get; set; } = "Extension"; // Extension, App, File, Clipboard, Snippet

    public CommandItem? Command { get; set; }
    public string? AppPath { get; set; }
    public string? FilePath { get; set; }

    private global::Avalonia.Media.Imaging.Bitmap? _realIcon;
    public global::Avalonia.Media.Imaging.Bitmap? RealIcon
    {
        get => _realIcon;
        set => SetField(ref _realIcon, value);
    }

    public bool HasRealIcon => RealIcon != null;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsHovered
    {
        get => _isHovered;
        set => SetField(ref _isHovered, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public static class MacIconExtractor
{
    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    public static byte[]? GetFileIconPngBytes(string path)
    {
        try
        {
            if (!System.OperatingSystem.IsMacOS()) return null;

            if (!path.StartsWith("/"))
            {
                var fullPath = GetApplicationPath(path);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    path = fullPath;
                }
            }

            IntPtr nsStringClass = objc_getClass("NSString");
            IntPtr allocSel = sel_registerName("alloc");
            IntPtr initWithUtf8Sel = sel_registerName("initWithUTF8String:");
            
            // Create NSString for path
            IntPtr pathStrAlloc = objc_msgSend(nsStringClass, allocSel);
            IntPtr pathStr = objc_msgSend(pathStrAlloc, initWithUtf8Sel, Marshal.StringToHGlobalAnsi(path));

            // Get NSWorkspace sharedWorkspace
            IntPtr nsWorkspaceClass = objc_getClass("NSWorkspace");
            IntPtr sharedWorkspaceSel = sel_registerName("sharedWorkspace");
            IntPtr workspace = objc_msgSend(nsWorkspaceClass, sharedWorkspaceSel);

            // Get iconForFile:
            IntPtr iconForFileSel = sel_registerName("iconForFile:");
            IntPtr image = objc_msgSend(workspace, iconForFileSel, pathStr);
            if (image == IntPtr.Zero) return null;

            // Get TIFFRepresentation
            IntPtr tiffRepSel = sel_registerName("TIFFRepresentation");
            IntPtr tiffData = objc_msgSend(image, tiffRepSel);
            if (tiffData == IntPtr.Zero) return null;

            // Get NSBitmapImageRep imageRepWithData:
            IntPtr nsBitmapClass = objc_getClass("NSBitmapImageRep");
            IntPtr imageRepSel = sel_registerName("imageRepWithData:");
            IntPtr bitmapRep = objc_msgSend(nsBitmapClass, imageRepSel, tiffData);
            if (bitmapRep == IntPtr.Zero) return null;

            // Get representationUsingType:properties:
            // NSPNGFileType = 4
            IntPtr repSel = sel_registerName("representationUsingType:properties:");
            IntPtr pngData = objc_msgSend(bitmapRep, repSel, (IntPtr)4, IntPtr.Zero);
            if (pngData == IntPtr.Zero) return null;

            // Get length and bytes
            IntPtr lengthSel = sel_registerName("length");
            IntPtr bytesSel = sel_registerName("bytes");
            
            int length = (int)objc_msgSend(pngData, lengthSel);
            IntPtr bytesPtr = objc_msgSend(pngData, bytesSel);

            if (length <= 0 || bytesPtr == IntPtr.Zero) return null;

            byte[] buffer = new byte[length];
            Marshal.Copy(bytesPtr, buffer, 0, length);

            // Release pathStr
            IntPtr releaseSel = sel_registerName("release");
            objc_msgSend(pathStr, releaseSel);

            return buffer;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting macOS app icon: {ex}");
            return null;
        }
    }

    public static string? GetApplicationPath(string appName)
    {
        try
        {
            if (!System.OperatingSystem.IsMacOS()) return null;

            IntPtr nsStringClass = objc_getClass("NSString");
            IntPtr allocSel = sel_registerName("alloc");
            IntPtr initWithUtf8Sel = sel_registerName("initWithUTF8String:");
            
            IntPtr nameStrAlloc = objc_msgSend(nsStringClass, allocSel);
            IntPtr nameStr = objc_msgSend(nameStrAlloc, initWithUtf8Sel, Marshal.StringToHGlobalAnsi(appName));

            IntPtr nsWorkspaceClass = objc_getClass("NSWorkspace");
            IntPtr sharedWorkspaceSel = sel_registerName("sharedWorkspace");
            IntPtr workspace = objc_msgSend(nsWorkspaceClass, sharedWorkspaceSel);

            IntPtr fullPathSel = sel_registerName("fullPathForApplication:");
            IntPtr pathStrObj = objc_msgSend(workspace, fullPathSel, nameStr);
            
            string? result = null;
            if (pathStrObj != IntPtr.Zero)
            {
                IntPtr utf8StringSel = sel_registerName("UTF8String");
                IntPtr cStringPtr = objc_msgSend(pathStrObj, utf8StringSel);
                if (cStringPtr != IntPtr.Zero)
                {
                    result = Marshal.PtrToStringAnsi(cStringPtr);
                }
            }

            IntPtr releaseSel = sel_registerName("release");
            objc_msgSend(nameStr, releaseSel);

            return result;
        }
        catch
        {
            return null;
        }
    }
}
