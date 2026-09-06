using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public static class AppSettingsStore
{
    public const string DefaultAiSystemPrompt = """
        你是燕子电脑端 AI 助手。你可以解答问题，也可以调用本地电脑端工具。
        你可以自主判断是否需要调用工具。如果需要调用工具，请输出一段包裹在 ```json 内部的 JSON 代码块：
        ```json
        {"tool": "工具名", "参数名": "参数值"}
        ```

        【工具调用示例】
        用户：查看插件列表
        AI回复：
        ```json
        {"tool": "query_extensions"}
        ```
        系统反馈：
        [{"id": "ext_calculator", "name": "计算器"}, {"id": "ext_weather", "name": "天气助手"}]
        AI回复：
        目前已安装的插件列表如下：
        1. 计算器 (ID: ext_calculator)
        2. 天气助手 (ID: ext_weather)
        你可以告诉我你想执行哪一个。

        【可用工具列表】
        1. query_extensions: 获取可用扩展列表。无参数。
        2. execute_extension: 执行某个扩展 (前台触发快捷启动，不等待输出结果). 参数: id (扩展ID)。
        3. execute_command: 在电脑端执行命令行命令。参数: command (要执行的命令文本)。【重要】电脑端已默认在 PowerShell 5.1 环境中执行命令，请直接输入 PowerShell 的 Cmdlet 或表达式，严禁外层嵌套调用 powershell、powershell.exe -Command 或 cmd /c，避免转义错误和执行超时。
        4. create_extension: 新建/保存一个本地扩展。参数: manifest (JSON格式的扩展清单字符串)。
        5. delete_extension: 删除某个本地扩展。参数: id (扩展ID)。
        6. run_extension: 运行并同步等待某个扩展的执行结果。参数: id (扩展ID)，input (可选，传递给扩展 of 输入参数文本)。
        7. stop_extension: 停止运行中的某个常驻扩展实例。参数: id (扩展ID)。

        【创建/设计本地扩展核心规范与策略】
        AI 在调用 `create_extension` 时，参数 `manifest` 必须是一个合法的 JSON 字符串。
        1. **选择最简策略**：
           - 打开类：能用 `openTarget`（例如程序、网页、文件夹路径）就不要写脚本。
           - 搜索类：优先使用 `queryPrefixes` + `queryTargetTemplate`（例如：百度搜索，前缀=["百度"], 模板="https://www.baidu.com/s?wd={query}"）。
           - 自动化与系统控制：优先使用 `powershell` 脚本。
           - 复杂逻辑、原生窗口界面：优先使用 `csharp` 脚本。
        2. **脚本接口约束**：
           - C# 内联脚本：必须包含 `"runtime": "csharp", "entryMode": "inline"`。代码中声明 `public static class YanziAction`，并实现 `public static Task<string> RunAsync(YanziActionContext context)`。宿主自动导入常用命名空间，可用 context.InputText 读取输入。
           - PowerShell 内联脚本：必须包含 `"runtime": "powershell", "entryMode": "inline"`。第一行写 `param([string]$InputText = "", [string]$ContextPath = "")`，结果写 stdout。
           - 硬件开关/系统变更：严禁盲目使用 `Disable-PnpDevice`，这会物理禁用硬件设备，除非要求明确是“禁用”。修改壁纸等必须调用系统 API 刷新（例如调用 `SPI_SETDESKWALLPAPER`），不能仅写注册表。
        3. **界面呈现约束**：
           - 独立弹窗/原生小应用：使用 C# 脚本配合 `"uiMode": "native-window"`。注意：在 native-window 中，WPF 窗口对象必须在 STA 线程中创建和显示（即启动一个 STA 线程，在里面 new 窗口并 ShowDialog/Show）。
           - 内嵌工作区卡片：使用 `"hostedViewXaml"`。其中的 xaml 必须是标准 WPF XAML。不能包含 `x:Class`，也不能直接写 `Click=` 或 `TextChanged=` 等事件；必须利用 `xmlns:oqh="clr-namespace:Yanzi"` 与 `oqh:HostedViewBridge.Action` 声明预设动作（例如 close、setState、runScript、loadStorage、saveStorage 等）。多个动作使用 `|` 分隔。

        【创建扩展清单 JSON 示例模板】
        模板 1：打开类（打开本地程序或系统页面）
        {
          "id": "open-calc",
          "name": "打开计算器",
          "version": "0.1.0",
          "category": "系统",
          "description": "启动系统计算器。",
          "keywords": ["calc", "计算器"],
          "openTarget": "calc.exe",
          "icon": "mdi:calculator"
        }

        模板 2：网页搜索类（带前缀触发）
        {
          "id": "search-bing",
          "name": "必应搜索",
          "version": "0.1.0",
          "category": "网页搜索",
          "description": "用必应搜索关键词。",
          "keywords": ["必应", "bing", "搜索"],
          "queryPrefixes": ["必应", "bing"],
          "queryTargetTemplate": "https://cn.bing.com/search?q={query}",
          "icon": "mdi:magnify"
        }

        模板 3：PowerShell 内联脚本（系统查询与输出）
        {
          "id": "get-services-list",
          "name": "系统服务查询",
          "version": "0.1.0",
          "category": "脚本",
          "description": "查询正在运行的 Windows 系统服务。",
          "keywords": ["service", "服务"],
          "runtime": "powershell",
          "entryMode": "inline",
          "script": {
            "source": "param([string]$InputText = \"\")\r\nGet-Service | Where-Object { $_.Status -eq 'Running' } | Select-Object -First 15 -Property Name, DisplayName | Out-String"
          },
          "icon": "mdi:server-security"
        }

        模板 4：C# 内联脚本（文本处理）
        {
          "id": "md5-generator",
          "name": "MD5生成器",
          "version": "0.1.0",
          "category": "加密",
          "description": "将输入文本转换为 MD5 哈希值。",
          "keywords": ["md5", "hash", "加密"],
          "runtime": "csharp",
          "entryMode": "inline",
          "script": {
            "source": "using System.Text;\r\nusing System.Security.Cryptography;\r\npublic static class YanziAction\r\n{\r\n    public static Task<string> RunAsync(YanziActionContext context)\r\n    {\r\n        if (string.IsNullOrEmpty(context.InputText)) return Task.FromResult(\"\");\r\n        using (var md5 = MD5.Create())\r\n        {\r\n            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(context.InputText));\r\n            var sb = new StringBuilder();\r\n            foreach (var b in bytes) sb.Append(b.ToString(\"x2\"));\r\n            return Task.FromResult(sb.ToString());\r\n        }\r\n    }\r\n}"
          },
          "icon": "mdi:key-variant"
        }

        模板 5：C# 原生独立窗口扩展 (native-window，使用 STA 线程启动窗口)
        {
          "id": "custom-dialog-tool",
          "name": "简易弹窗工具",
          "version": "0.1.0",
          "category": "工具",
          "description": "打开一个独立的原生 WPF 弹窗来输入文本。",
          "keywords": ["dialog", "窗口"],
          "runtime": "csharp",
          "entryMode": "inline",
          "uiMode": "native-window",
          "script": {
            "source": "using System.Threading;\r\nusing System.Windows;\r\nusing System.Windows.Controls;\r\npublic static class YanziAction\r\n{\r\n    public static Task<string> RunAsync(YanziActionContext context)\r\n    {\r\n        var tcs = new TaskCompletionSource<string>();\r\n        var thread = new Thread(() =>\r\n        {\r\n            var win = new Window\r\n            {\r\n                Title = \"信息录入\",\r\n                Width = 300,\r\n                Height = 180,\r\n                WindowStartupLocation = WindowStartupLocation.CenterScreen,\r\n                Background = System.Windows.Media.Brushes.DarkGray\r\n            };\r\n            var stack = new StackPanel { Margin = new Thickness(15) };\r\n            var txt = new TextBox { Height = 30, Margin = new Thickness(0, 10, 0, 10) };\r\n            var btn = new Button { Content = \"确认\", Height = 30 };\r\n            btn.Click += (s, e) => { tcs.SetResult(txt.Text); win.Close(); };\r\n            stack.Children.Add(new TextBlock { Text = \"请输入内容:\" });\r\n            stack.Children.Add(txt);\r\n            stack.Children.Add(btn);\r\n            win.Content = stack;\r\n            win.Closed += (s, e) => { tcs.TrySetResult(\"\"); };\r\n            win.ShowDialog();\r\n        });\r\n        thread.SetApartmentState(ApartmentState.STA);\r\n        thread.Start();\r\n        return tcs.Task;\r\n    }\r\n}"
          },
          "icon": "mdi:application-window"
        }

        模板 6：宿主内嵌 hostedViewXaml 卡片扩展
        {
          "id": "todo-workspace-card",
          "name": "内嵌备忘录",
          "version": "0.1.0",
          "category": "工具",
          "description": "宿主工作区内嵌备忘展示。",
          "keywords": ["memo", "备忘录"],
          "hostedViewXaml": {
            "xaml": "<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:oqh=\"clr-namespace:Yanzi\" BorderBrush=\"#33FFFFFF\" BorderThickness=\"1\" CornerRadius=\"8\" Padding=\"12\" Background=\"#1E1E1E\"><Grid><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/></Grid.RowDefinitions><TextBlock Text=\"简易记事\" Foreground=\"#85b7eb\" FontWeight=\"Bold\"/><TextBox Grid.Row=\"1\" Margin=\"0,8,0,0\" Text=\"{Binding [note]}\" AcceptsReturn=\"True\" Background=\"#121212\" Foreground=\"White\" BorderThickness=\"0\"/><Button Grid.Row=\"0\" HorizontalAlignment=\"Right\" Content=\"关闭\" oqh:HostedViewBridge.Action=\"close\" Style=\"{StaticResource InlineLinkButtonStyle}\"/></Grid></Border>",
            "state": {
              "note": "临时的备忘内容，这里的数据双向绑定到 textbox"
            },
            "window": {
              "width": 320,
              "height": 240
            }
          },
          "icon": "mdi:notebook-edit"
        }

        【注意】如果你调用了工具，系统会在后台真实执行，并在执行完成后将真实的结果反馈给你，之后你再根据执行结果来决定是继续调用工具还是输出最终的自然语言回复。
        """;

    public static string SettingsPath =>
        HostAssets.ResolveDataFilePath("appsettings.local.json");

    // 设置文件的所有读写串行化：调用方横跨 UI 线程、云同步后台与本地 HTTP API 线程，
    // 并发 WriteAllText 会互相抛 IOException 导致保存静默丢失。
    private static readonly object SettingsIoLock = new();

    // 供低级钩子回调等高频路径使用的短 TTL 缓存；LL 钩子回调内直接读盘会超出
    // LowLevelHooksTimeout，Windows 会静默摘除钩子（表现为所有触发器集体失灵）。
    private const int SettingsCacheTtlMs = 1500;
    private static AppSettings? _cachedSettings;
    private static long _cachedSettingsTimestampTicks;

    /// <summary>
    /// 高频路径专用：短 TTL 缓存读取（≤1.5s 磁盘刷新一次）。
    /// 注意：可能返回共享实例，调用方只读，禁止修改返回对象。
    /// </summary>
    public static AppSettings LoadCached()
    {
        var cached = _cachedSettings;
        if (cached != null && Environment.TickCount64 - _cachedSettingsTimestampTicks < SettingsCacheTtlMs)
        {
            return cached;
        }

        lock (SettingsIoLock)
        {
            if (_cachedSettings != null && Environment.TickCount64 - _cachedSettingsTimestampTicks < SettingsCacheTtlMs)
            {
                return _cachedSettings;
            }

            var settings = Load();
            _cachedSettings = settings;
            _cachedSettingsTimestampTicks = Environment.TickCount64;
            return settings;
        }
    }

    private static void UpdateCache(AppSettings settings)
    {
        _cachedSettings = settings;
        _cachedSettingsTimestampTicks = Environment.TickCount64;
    }

    public static AppSettings Load()
    {
        lock (SettingsIoLock)
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = Normalize(new AppSettings());
                TryImportPlaintextCredentials(defaults);
                UpdateCache(defaults);
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = Normalize(JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings());
                TryImportPlaintextCredentials(settings);
                UpdateCache(settings);
                return settings;
            }
            catch (Exception ex)
            {
                // 配置损坏（写一半崩溃/断电等）时保留坏文件供人工恢复，避免随后任何一次
                // Save 用默认值把它永久覆盖；随后返回默认配置让应用可用。
                HostAssets.AppendLog($"[AppSettingsStore] Load failed, settings reset to defaults: {ex.Message}");
                TryPreserveCorruptSettingsFile();
                var defaults = Normalize(new AppSettings());
                TryImportPlaintextCredentials(defaults);
                UpdateCache(defaults);
                return defaults;
            }
        }
    }

    private static void TryImportPlaintextCredentials(AppSettings settings)
    {
        try
        {
            if (AiCredentialStore.ImportPlaintextAndHydrate(settings))
            {
                WriteSanitizedSettings(settings);
            }
        }
        catch (Exception ex)
        {
            // 凭据迁移/写凭据文件失败不应拖垮整个 Load（历史上会让 Load 返回默认配置）。
            HostAssets.AppendLog($"[AppSettingsStore] Credential hydration skipped: {ex.Message}");
        }
    }

    private static void TryPreserveCorruptSettingsFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var backup = SettingsPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(SettingsPath, backup, overwrite: true);
            HostAssets.AppendLog($"[AppSettingsStore] Corrupt settings preserved: {backup}");
        }
        catch
        {
            // 保留失败也继续返回默认值，应用可用性优先。
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (SettingsIoLock)
        {
            settings = Normalize(settings);
            try
            {
                AiCredentialStore.Capture(settings);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[AppSettingsStore] Credential capture failed: {ex.Message}");
            }

            WriteSanitizedSettings(settings);
            HostAssets.AppendLog($"[AppSettingsStore] Saved settings: EnableEverything={settings.EnableEverything}, UpdatedAt={settings.LauncherConfigUpdatedAtUtc}");
        }
    }

    private static void WriteSanitizedSettings(AppSettings settings)
    {
        AiCredentialStore.RemovePlaintext(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        SafeFile.AtomicWriteText(SettingsPath, json);
        UpdateCache(settings);
    }

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.CamelCaseIndented;

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.ThemeMode = settings.ThemeMode?.Trim() switch
        {
            "Light" => "Light",
            "System" => "System",
            _ => "Dark"
        };

        settings.QuickPanelGlobalGroups ??= [];
        if (settings.QuickPanelGlobalGroups.Count == 0)
        {
            settings.QuickPanelGlobalGroups.Add(new QuickPanelGroupSettings
            {
                Id = "global-default",
                Name = "默认",
                Slots = settings.QuickPanelSlots.Take(12).ToList(),
                SlotItems = settings.QuickPanelSlots
                    .Take(24)
                    .Select(static slot => string.IsNullOrWhiteSpace(slot)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = slot })
                    .ToList()
            });
        }

        settings.QuickPanelContextGroups ??= [];
        if (settings.QuickPanelContextGroups.Count == 0)
        {
            settings.QuickPanelContextGroups.Add(new QuickPanelGroupSettings
            {
                Id = "context-default",
                Name = "默认"
            });
        }

        if (settings.QuickPanelGlobalRowCount <= 0)
        {
            settings.QuickPanelGlobalRowCount = settings.QuickPanelRowCount > 0 ? settings.QuickPanelRowCount : 3;
        }
        if (settings.QuickPanelGlobalColumnCount <= 0)
        {
            settings.QuickPanelGlobalColumnCount = 4;
        }
        if (settings.QuickPanelContextRowCount <= 0)
        {
            settings.QuickPanelContextRowCount = settings.QuickPanelRowCount > 0 ? settings.QuickPanelRowCount : 3;
        }
        if (settings.QuickPanelContextColumnCount <= 0)
        {
            settings.QuickPanelContextColumnCount = 4;
        }

        settings.QuickPanelGlobalRowCount = Math.Max(1, Math.Min(8, settings.QuickPanelGlobalRowCount));
        settings.QuickPanelGlobalColumnCount = Math.Max(3, Math.Min(8, settings.QuickPanelGlobalColumnCount));
        settings.QuickPanelContextRowCount = Math.Max(1, Math.Min(8, settings.QuickPanelContextRowCount));
        settings.QuickPanelContextColumnCount = Math.Max(3, Math.Min(8, settings.QuickPanelContextColumnCount));

        var globalSlotCount = settings.QuickPanelGlobalRowCount * settings.QuickPanelGlobalColumnCount;
        var contextSlotCount = settings.QuickPanelContextRowCount * settings.QuickPanelContextColumnCount;

        NormalizeGroupList(settings.QuickPanelGlobalGroups, globalSlotCount);
        NormalizeGroupList(settings.QuickPanelContextGroups, contextSlotCount);

        settings.GlobalFavoriteExtensionIds ??= settings.FavoriteExtensionIds?.ToList() ?? [];
        settings.ContextFavoriteExtensionIds ??= [];
        settings.DisabledExtensionIds ??= [];
        settings.RecentlyAddedExtensionIds ??= [];
        settings.UnreadNewExtensionIds ??= [];
        settings.CompletedQuestIds ??= [];
        settings.UnlockedBadges ??= [];
        settings.YarnSelect ??= new YarnSelectSettings();
        settings.YarnSelect.WhitelistedProcesses ??= [];
        settings.YarnSelect.BlacklistedProcesses ??= [];
        settings.YarnSelect.Rules ??= [];
        if (settings.YarnSelect.Rules.Count == 0)
        {
            settings.YarnSelect.Rules = YarnSelectSettings.CreateDefaultRulesFromLegacy(settings.YarnSelect);
        }

        settings.YarnSelect.Rules = settings.YarnSelect.Rules
            .Select(YarnSelectSettings.NormalizeRule)
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerKey))
            .DistinctBy(static rule => rule.TriggerKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.YarnSelect.WhitelistedProcesses = settings.YarnSelect.WhitelistedProcesses
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.YarnSelect.BlacklistedProcesses = settings.YarnSelect.BlacklistedProcesses
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.RadialMenu ??= new RadialMenuSettings();
        settings.RadialMenu.Pages ??= [];
        if (settings.RadialMenu.Pages.Count == 0)
        {
            settings.RadialMenu.Pages.Add(new RadialMenuPageSettings
            {
                Id = "default",
                Name = "全局",
                Slots = settings.RadialMenu.Slots?.ToList() ?? Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList()
            });
        }

        foreach (var page in settings.RadialMenu.Pages)
        {
            page.Id = string.IsNullOrWhiteSpace(page.Id) ? Guid.NewGuid().ToString("N") : page.Id.Trim();
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "未命名" : page.Name.Trim();
            if (page.Id.Equals("default", StringComparison.OrdinalIgnoreCase) && 
                (page.Name == "默认" || page.Name == "办公" || page.Name == "燕环"))
            {
                page.Name = "全局";
            }
            page.Slots ??= [];
            while (page.Slots.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.Slots.Add(null);
            }

            if (page.Slots.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.Slots = page.Slots.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.Slots = page.Slots
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
            page.SlotTitles ??= [];
            while (page.SlotTitles.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.SlotTitles.Add(null);
            }

            if (page.SlotTitles.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.SlotTitles = page.SlotTitles.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.SlotTitles = page.SlotTitles
                .Select(static title => string.IsNullOrWhiteSpace(title) ? null : title.Trim())
                .ToList();
            page.ChildPageIds ??= [];
            while (page.ChildPageIds.Count < RadialMenuSettings.TotalSlotCount)
            {
                page.ChildPageIds.Add(null);
            }

            if (page.ChildPageIds.Count > RadialMenuSettings.TotalSlotCount)
            {
                page.ChildPageIds = page.ChildPageIds.Take(RadialMenuSettings.TotalSlotCount).ToList();
            }

            page.ChildPageIds = page.ChildPageIds
                .Select(static id => string.IsNullOrWhiteSpace(id) ? null : id.Trim())
                .ToList();
        }

        settings.RadialMenu.SelectedPageId = settings.RadialMenu.Pages.Any(page => page.Id.Equals(settings.RadialMenu.SelectedPageId, StringComparison.OrdinalIgnoreCase))
            ? settings.RadialMenu.SelectedPageId
            : settings.RadialMenu.Pages[0].Id;
        settings.RadialMenu.ActivationKey = RadialActivationKeys.Normalize(settings.RadialMenu.ActivationKey);
        settings.RadialMenu.CustomShortcut = (settings.RadialMenu.CustomShortcut ?? string.Empty).Trim();
        settings.GlobalServiceBlacklistedProcesses = NormalizeProcessList(settings.GlobalServiceBlacklistedProcesses);
        settings.RadialMenu.WhitelistedProcesses = NormalizeProcessList(settings.RadialMenu.WhitelistedProcesses);
        settings.RadialMenu.BlacklistedProcesses = NormalizeProcessList(settings.RadialMenu.BlacklistedProcesses);
        settings.RadialMenu.Slots = settings.RadialMenu.Pages[0].Slots.ToList();
        settings.RadialMenu.DeadZonePixels = Math.Clamp(settings.RadialMenu.DeadZonePixels, 12, 120);
        settings.RadialMenu.RadiusPixels = Math.Clamp(settings.RadialMenu.RadiusPixels, 80, 240);
        settings.RadialMenu.DragThresholdPixels = Math.Clamp(settings.RadialMenu.DragThresholdPixels, 8, 120);
        settings.QuickPanelMouseTriggers ??= new QuickPanelMouseTriggerSettings();
        settings.QuickPanelMouseTriggers.LongPressMilliseconds =
            (settings.QuickPanelMouseTriggers.LongPressMilliseconds == 120 || settings.QuickPanelMouseTriggers.LongPressMilliseconds == 500 || settings.QuickPanelMouseTriggers.LongPressMilliseconds == 350)
                ? 250
                : Math.Clamp(settings.QuickPanelMouseTriggers.LongPressMilliseconds, 50, 1500);
        settings.QuickPanelMouseTriggers.DragThresholdPixels = Math.Clamp(settings.QuickPanelMouseTriggers.DragThresholdPixels, 8, 120);
        settings.MouseGestureTriggerMode = MouseGestureTriggerModes.Normalize(settings.MouseGestureTriggerMode);
        settings.YanyuRules ??= [];
        settings.YanyuRules = settings.YanyuRules
            .Select(NormalizeYanyuRule)
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.TriggerText))
            .ToList();
        settings.RecentlyAddedExtensionIds = settings.RecentlyAddedExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        settings.UnreadNewExtensionIds = settings.UnreadNewExtensionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelGlobalGroupId) ||
            settings.QuickPanelGlobalGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelGlobalGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelGlobalGroupId = settings.QuickPanelGlobalGroups[0].Id;
        }

        if (string.IsNullOrWhiteSpace(settings.SelectedQuickPanelContextGroupId) ||
            settings.QuickPanelContextGroups.All(group => !string.Equals(group.Id, settings.SelectedQuickPanelContextGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            settings.SelectedQuickPanelContextGroupId = settings.QuickPanelContextGroups[0].Id;
        }

        settings.PersonalSync ??= new PersonalSyncSettings();
        settings.PersonalSync.Provider = PersonalSyncProviders.Normalize(settings.PersonalSync.Provider);
        settings.PersonalSync.GitHub ??= new PersonalSyncGitHubConfig();
        settings.PersonalSync.Gitee ??= new PersonalSyncGiteeConfig();
        settings.PersonalSync.GitLab ??= new PersonalSyncGitLabConfig();
        settings.PersonalSync.Gitea ??= new PersonalSyncGiteaConfig();
        settings.PersonalSync.S3 ??= new PersonalSyncS3Config();
        settings.PersonalSync.WebDav ??= new PersonalSyncWebDavConfig();
        var hasLegacyWebDavConfig = HasWebDavConfigValues(
            settings.WebDavServerUrl,
            settings.WebDavRootPath,
            settings.WebDavUsername);
        settings.PersonalSync.GitHub.Username = settings.PersonalSync.GitHub.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.GitHub.Repo = string.IsNullOrWhiteSpace(settings.PersonalSync.GitHub.Repo) ? "yanzi-sync" : settings.PersonalSync.GitHub.Repo.Trim();
        settings.PersonalSync.GitHub.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.GitHub.Branch) ? "main" : settings.PersonalSync.GitHub.Branch.Trim();
        settings.PersonalSync.GitHub.PathPrefix = settings.PersonalSync.GitHub.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitee.Username = settings.PersonalSync.Gitee.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitee.Repo = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitee.Repo) ? "yanzi-sync" : settings.PersonalSync.Gitee.Repo.Trim();
        settings.PersonalSync.Gitee.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitee.Branch) ? "master" : settings.PersonalSync.Gitee.Branch.Trim();
        settings.PersonalSync.Gitee.PathPrefix = settings.PersonalSync.Gitee.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.GitLab.BaseUrl = string.IsNullOrWhiteSpace(settings.PersonalSync.GitLab.BaseUrl) ? "https://gitlab.com" : settings.PersonalSync.GitLab.BaseUrl.Trim();
        settings.PersonalSync.GitLab.ProjectPath = settings.PersonalSync.GitLab.ProjectPath?.Trim() ?? string.Empty;
        settings.PersonalSync.GitLab.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.GitLab.Branch) ? "main" : settings.PersonalSync.GitLab.Branch.Trim();
        settings.PersonalSync.GitLab.PathPrefix = settings.PersonalSync.GitLab.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitea.BaseUrl = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitea.BaseUrl) ? "https://gitea.com" : settings.PersonalSync.Gitea.BaseUrl.Trim();
        settings.PersonalSync.Gitea.Username = settings.PersonalSync.Gitea.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.Gitea.Repo = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitea.Repo) ? "yanzi-sync" : settings.PersonalSync.Gitea.Repo.Trim();
        settings.PersonalSync.Gitea.Branch = string.IsNullOrWhiteSpace(settings.PersonalSync.Gitea.Branch) ? "main" : settings.PersonalSync.Gitea.Branch.Trim();
        settings.PersonalSync.Gitea.PathPrefix = settings.PersonalSync.Gitea.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.AccessKeyId = settings.PersonalSync.S3.AccessKeyId?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.Region = settings.PersonalSync.S3.Region?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.Bucket = settings.PersonalSync.S3.Bucket?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.Endpoint = settings.PersonalSync.S3.Endpoint?.Trim() ?? string.Empty;
        settings.PersonalSync.S3.PathPrefix = settings.PersonalSync.S3.PathPrefix?.Trim() ?? string.Empty;
        settings.PersonalSync.WebDav.Url = string.IsNullOrWhiteSpace(settings.PersonalSync.WebDav.Url) ? "https://dav.jianguoyun.com/dav/" : settings.PersonalSync.WebDav.Url.Trim();
        settings.PersonalSync.WebDav.Username = settings.PersonalSync.WebDav.Username?.Trim() ?? string.Empty;
        settings.PersonalSync.WebDav.PathPrefix = string.IsNullOrWhiteSpace(settings.PersonalSync.WebDav.PathPrefix) ? "/yanzi" : settings.PersonalSync.WebDav.PathPrefix.Trim();

        var shouldAdoptLegacyWebDavConfig =
            hasLegacyWebDavConfig &&
            settings.PersonalSync.Provider == PersonalSyncProviders.None;
        if (shouldAdoptLegacyWebDavConfig)
        {
            CloudSyncDiagnostics.Log(
                "AppSettingsStore",
                "Adopting legacy WebDAV config into personal sync settings",
                ("providerBefore", settings.PersonalSync.Provider),
                ("enableWebDavSync", settings.EnableWebDavSync),
                ("webDavUrl", settings.WebDavServerUrl),
                ("webDavRootPath", settings.WebDavRootPath),
                ("webDavUsername", settings.WebDavUsername));
            settings.PersonalSync.Provider = PersonalSyncProviders.WebDav;
            settings.PersonalSync.Enabled = settings.EnableWebDavSync;
            settings.PersonalSync.WebDav.Url = string.IsNullOrWhiteSpace(settings.WebDavServerUrl)
                ? settings.PersonalSync.WebDav.Url
                : settings.WebDavServerUrl.Trim();
            settings.PersonalSync.WebDav.PathPrefix = string.IsNullOrWhiteSpace(settings.WebDavRootPath)
                ? settings.PersonalSync.WebDav.PathPrefix
                : settings.WebDavRootPath.Trim();
            settings.PersonalSync.WebDav.Username = settings.WebDavUsername?.Trim() ?? string.Empty;
        }

        if (!settings.WebDavSyncManuallyDisabled &&
            hasLegacyWebDavConfig)
        {
            settings.EnableWebDavSync = true;
        }

        if (settings.PersonalSync.Provider == PersonalSyncProviders.WebDav)
        {
            settings.EnableWebDavSync = settings.PersonalSync.Enabled;
            settings.WebDavServerUrl = settings.PersonalSync.WebDav.Url;
            settings.WebDavRootPath = settings.PersonalSync.WebDav.PathPrefix;
            settings.WebDavUsername = settings.PersonalSync.WebDav.Username;
        }

        settings.PersonalSyncAutoSyncDelaySeconds = NormalizePersonalSyncAutoSyncDelay(settings.PersonalSyncAutoSyncDelaySeconds);

        settings.AiBaseUrl = settings.AiBaseUrl?.Trim() ?? string.Empty;
        settings.AiApiKey = settings.AiApiKey?.Trim() ?? string.Empty;
        settings.AiModel = settings.AiModel?.Trim() ?? string.Empty;
        settings.AiSystemPrompt = string.IsNullOrWhiteSpace(settings.AiSystemPrompt)
            ? DefaultAiSystemPrompt
            : settings.AiSystemPrompt.Trim();

        settings.AiServiceProviders ??= [];
        if (settings.AiServiceProviders.Count == 0)
        {
            var defaultProvider = new AiServiceProviderSettings
            {
                Id = Guid.NewGuid().ToString(),
                Name = "默认提供商",
                ProviderType = "OpenAI",
                BaseUrl = settings.AiBaseUrl,
                ApiKey = settings.AiApiKey,
                IsEnabled = true,
                Models = string.IsNullOrWhiteSpace(settings.AiModel) ? [] : new List<string> { settings.AiModel },
                SelectedModel = settings.AiModel
            };
            settings.AiServiceProviders.Add(defaultProvider);
            settings.ActiveServiceProviderId = defaultProvider.Id;
        }
        settings.Yanm ??= new YanmSettings();
        settings.Yanm.Components ??= [];
        settings.Yanm.ComponentState ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.Yanm.ActivationKey = YanmActivationKeys.Normalize(settings.Yanm.ActivationKey);
        settings.Yanm.WhitelistedProcesses = NormalizeProcessList(settings.Yanm.WhitelistedProcesses);
        settings.Yanm.BlacklistedProcesses = NormalizeProcessList(settings.Yanm.BlacklistedProcesses);
        settings.Yanm.HoldDelayMilliseconds = Math.Clamp(settings.Yanm.HoldDelayMilliseconds, 0, 1000);
        settings.Yanm.GridSizePixels = Math.Clamp(settings.Yanm.GridSizePixels, 5, 80);
        settings.Yanm.OverlayOpacity = settings.Yanm.OverlayOpacity <= 0.581
            ? 0.85
            : Math.Clamp(settings.Yanm.OverlayOpacity, 0.05, 0.85);
        if (!settings.Yanm.HasInitializedDefaultComponents &&
            settings.Yanm.Components.Count == 0)
        {
            settings.Yanm.Components = YanmComponentSettings.CreateDefaultComponents();
            settings.Yanm.HasInitializedDefaultComponents = true;
            settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        }
        else if (settings.Yanm.DefaultComponentVersion < YanmSettings.CurrentDefaultComponentVersion)
        {
            YanmComponentSettings.UpgradeDefaultComponents(settings.Yanm.Components);
            settings.Yanm.DefaultComponentVersion = YanmSettings.CurrentDefaultComponentVersion;
        }

        NormalizeDefaultYanmComponentIds(settings.Yanm);

        var yanmComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in settings.Yanm.Components)
        {
            component.Id = NormalizeYanmComponentId(component.Id, yanmComponentIds);
            component.Title = string.IsNullOrWhiteSpace(component.Title) ? "燕幕组件" : component.Title.Trim();
            component.X = Math.Max(0, component.X);
            component.Y = Math.Max(0, component.Y);
            component.Width = Math.Max(settings.Yanm.GridSizePixels * 8, component.Width);
            component.Height = Math.Max(settings.Yanm.GridSizePixels * 6, component.Height);
            component.Html = string.IsNullOrWhiteSpace(component.Html) ? YanmComponentSettings.DefaultHtml(component.Title) : component.Html;
            component.Locked = component.Locked;
        }

        settings.WindowSnapAssistHotkey = settings.WindowSnapAssistHotkey?.Trim() ?? string.Empty;
        settings.WindowSnapAssistMouseTriggerMode = MouseTriggerModes.Normalize(settings.WindowSnapAssistMouseTriggerMode);
        settings.WindowSnapAssistCustomLayouts ??= [];
        settings.WindowSnapAssistCustomLayouts = settings.WindowSnapAssistCustomLayouts
            .Where(static slot => slot.SlotIndex is >= 0 and < WindowSnapAssistCustomLayoutSettings.TotalSlotCount)
            .GroupBy(static slot => slot.SlotIndex)
            .Select(static group => NormalizeWindowSnapAssistCustomLayout(group.Last()))
            .OrderBy(static slot => slot.SlotIndex)
            .ToList();
        settings.WindowBindings = NormalizeWindowBindings(settings.WindowBindings);
        settings.LastTestArgument = string.IsNullOrWhiteSpace(settings.LastTestArgument) ? "示例参数" : settings.LastTestArgument.Trim();
        settings.LastExtensionEditorTab = string.Equals(settings.LastExtensionEditorTab, "ai", StringComparison.OrdinalIgnoreCase) ? "ai" : "simple";
        settings.LauncherResultViewMode = string.Equals(settings.LauncherResultViewMode, "Grid", StringComparison.OrdinalIgnoreCase) ? "Grid" : "List";

        settings.AutoBackupFrequency = string.IsNullOrWhiteSpace(settings.AutoBackupFrequency)
            ? "Weekly"
            : settings.AutoBackupFrequency.Trim();
        settings.LastAutoBackupTime = (settings.LastAutoBackupTime ?? string.Empty).Trim();
        settings.CustomBackupDirectory = (settings.CustomBackupDirectory ?? string.Empty).Trim();

        settings.SearchScopeConfigs ??= [];
        var defaultList = new List<(string Key, string Label)>
        {
            ("all", "全部"),
            ("extension", BrandTerms.DefaultMiniApp),
            ("application", "应用"),
            ("file", "文件"),
            ("system", "系统"),
            ("yanyu", BrandTerms.DefaultYanVoice),
            ("ai", "AI对话"),
            ("store", $"{BrandTerms.DefaultMiniApp}商店")
        };

        foreach (var def in defaultList)
        {
            if (!settings.SearchScopeConfigs.Any(c => string.Equals(c.Key, def.Key, StringComparison.OrdinalIgnoreCase)))
            {
                settings.SearchScopeConfigs.Add(new SearchScopeConfigItem { Key = def.Key, Label = def.Label, IsVisible = true, IsPinned = false });
            }
        }

        // 自动升级旧配置中的历史名词
        foreach (var cfg in settings.SearchScopeConfigs)
        {
            if (string.Equals(cfg.Key, "extension", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(cfg.Label, "扩展", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(cfg.Label)))
            {
                cfg.Label = BrandTerms.Current.MiniApp;
            }
            else if (string.Equals(cfg.Key, "store", StringComparison.OrdinalIgnoreCase) &&
                     (string.Equals(cfg.Label, "扩展商店", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(cfg.Label)))
            {
                cfg.Label = $"{BrandTerms.Current.MiniApp}商店";
            }
        }

        settings.PinnedSearchScopeCommandIds ??= [];
        foreach (var id in settings.PinnedSearchScopeCommandIds)
        {
            var key = $"pinned_{id}";
            if (!settings.SearchScopeConfigs.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                settings.SearchScopeConfigs.Add(new SearchScopeConfigItem { Key = key, Label = $"固定:{id}", IsVisible = true, IsPinned = true });
            }
        }

        settings.SearchScopeConfigs.RemoveAll(c => c.IsPinned && !settings.PinnedSearchScopeCommandIds.Contains(c.Key.Replace("pinned_", ""), StringComparer.OrdinalIgnoreCase));

        return settings;
    }

    private static void NormalizeDefaultYanmComponentIds(YanmSettings yanm)
    {
        if (yanm.Components.Count == 0)
        {
            return;
        }

        var usedIds = yanm.Components
            .Where(static component => !string.IsNullOrWhiteSpace(component.Id))
            .Select(static component => component.Id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var component in yanm.Components)
        {
            if (!YanmComponentSettings.TryGetDefaultComponentId(component.Title, out var stableId))
            {
                continue;
            }

            var currentId = component.Id?.Trim() ?? string.Empty;
            if (string.Equals(currentId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (usedIds.Contains(stableId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentId) &&
                yanm.ComponentState.TryGetValue(currentId, out var state) &&
                !yanm.ComponentState.ContainsKey(stableId))
            {
                yanm.ComponentState[stableId] = state;
                yanm.ComponentState.Remove(currentId);
            }

            if (!string.IsNullOrWhiteSpace(currentId))
            {
                usedIds.Remove(currentId);
            }

            component.Id = stableId;
            usedIds.Add(stableId);
        }
    }

    private static string NormalizeYanmComponentId(string? id, HashSet<string> usedIds)
    {
        var normalized = id?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || usedIds.Contains(normalized))
        {
            do
            {
                normalized = YanmComponentSettings.CreateSystemComponentId();
            }
            while (usedIds.Contains(normalized));
        }

        usedIds.Add(normalized);
        return normalized;
    }

    private static QuickPanelSlotItem? NormalizeSlotItem(QuickPanelSlotItem? item)
    {
        if (item == null)
        {
            return null;
        }

        item.ItemType = string.IsNullOrWhiteSpace(item.ItemType) ? "extension" : item.ItemType.Trim().ToLowerInvariant();
        if (item.IsFolder)
        {
            item.FolderName = string.IsNullOrWhiteSpace(item.FolderName) ? "新分组" : item.FolderName.Trim();
            item.FolderExtensionIds ??= [];
            item.FolderExtensionIds = item.FolderExtensionIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            item.FolderSlotItems ??= [];
            if (item.FolderSlotItems.Count == 0)
            {
                item.FolderSlotItems = item.FolderExtensionIds
                    .Take(24)
                    .Select(static id => string.IsNullOrWhiteSpace(id)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = id })
                    .ToList();
            }

            while (item.FolderSlotItems.Count < 24)
            {
                item.FolderSlotItems.Add(null);
            }

            if (item.FolderSlotItems.Count > 24)
            {
                item.FolderSlotItems = item.FolderSlotItems.Take(24).ToList();
            }

            for (var index = 0; index < item.FolderSlotItems.Count; index++)
            {
                item.FolderSlotItems[index] = NormalizeSlotItem(item.FolderSlotItems[index]);
            }

            item.FolderExtensionIds = item.FolderSlotItems
                .Where(static slot => slot != null && !slot.IsFolder && !string.IsNullOrWhiteSpace(slot.ExtensionId))
                .Select(static slot => slot!.ExtensionId!)
                .ToList();
            return item.FolderSlotItems.Any(static slot => slot != null) ? item : null;
        }

        item.ExtensionId = string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item.ExtensionId.Trim();
        return string.IsNullOrWhiteSpace(item.ExtensionId) ? null : item;
    }

    private static List<string?> ProjectLegacySlots(IReadOnlyList<QuickPanelSlotItem?> slotItems)
    {
        var result = slotItems
            .Take(12)
            .Select(static item => item != null && !item.IsFolder ? item.ExtensionId : null)
            .ToList();
        while (result.Count < 12)
        {
            result.Add(null);
        }

        return result;
    }

    private static void NormalizeGroupList(List<QuickPanelGroupSettings> groups, int slotCount)
    {
        foreach (var group in groups)
        {
            group.Id = string.IsNullOrWhiteSpace(group.Id) ? Guid.NewGuid().ToString("N") : group.Id;
            group.Name = string.IsNullOrWhiteSpace(group.Name) ? "未命名" : group.Name.Trim();
            group.ContextProcessName = group.ContextProcessName?.Trim();
            group.ContextDisplayName = group.ContextDisplayName?.Trim();
            group.Slots ??= [];
            group.SlotItems ??= [];
            while (group.Slots.Count < slotCount)
            {
                group.Slots.Add(null);
            }
            if (group.Slots.Count > slotCount)
            {
                group.Slots = group.Slots.Take(slotCount).ToList();
            }

            if (group.SlotItems.Count == 0)
            {
                group.SlotItems = group.Slots
                    .Take(slotCount)
                    .Select(static slot => string.IsNullOrWhiteSpace(slot)
                        ? null
                        : new QuickPanelSlotItem { ExtensionId = slot })
                    .ToList();
            }

            while (group.SlotItems.Count < slotCount)
            {
                group.SlotItems.Add(null);
            }

            if (group.SlotItems.Count > slotCount)
            {
                group.SlotItems = group.SlotItems.Take(slotCount).ToList();
            }

            for (var index = 0; index < group.SlotItems.Count; index++)
            {
                group.SlotItems[index] = NormalizeSlotItem(group.SlotItems[index]);
            }

            group.Slots = ProjectLegacySlots(group.SlotItems);
        }
    }

    private static bool HasWebDavConfigValues(string? serverUrl, string? rootPath, string? username)
    {
        return !string.IsNullOrWhiteSpace(serverUrl) ||
               !string.IsNullOrWhiteSpace(rootPath) ||
               !string.IsNullOrWhiteSpace(username);
    }

    private static int NormalizePersonalSyncAutoSyncDelay(int value)
    {
        return value is 0 or 2 or 3 or 5 or 10 or 20 or 30 or 60 or 120
            ? value
            : 10;
    }

    private static List<string> NormalizeProcessList(IEnumerable<string>? processes) =>
        (processes ?? [])
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Select(static item => item.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static WindowSnapAssistCustomLayoutSettings NormalizeWindowSnapAssistCustomLayout(WindowSnapAssistCustomLayoutSettings slot)
    {
        slot.LeftRatio = Math.Clamp(slot.LeftRatio, -2, 3);
        slot.TopRatio = Math.Clamp(slot.TopRatio, -2, 3);
        slot.WidthRatio = Math.Clamp(slot.WidthRatio, 0.05, 3);
        slot.HeightRatio = Math.Clamp(slot.HeightRatio, 0.05, 3);
        return slot;
    }

    private static YanyuRuleSettings NormalizeYanyuRule(YanyuRuleSettings? rule)
    {
        rule ??= new YanyuRuleSettings();
        rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim();
        rule.TriggerText = (rule.TriggerText ?? string.Empty).Trim();
        rule.Description = (rule.Description ?? string.Empty).Trim();
        rule.BoundProcessName = (rule.BoundProcessName ?? string.Empty).Trim();
        rule.ActionType = YanyuActionTypes.Normalize(rule.ActionType);
        rule.TextContent ??= string.Empty;
        rule.ExtensionId = string.IsNullOrWhiteSpace(rule.ExtensionId) ? string.Empty : rule.ExtensionId.Trim();
        rule.TriggerSuffix = YanyuTriggerSuffix.Normalize(rule.TriggerSuffix);
        return rule;
    }

    private static WindowBindingSettings NormalizeWindowBindings(WindowBindingSettings? bindings)
    {
        bindings ??= new WindowBindingSettings();
        bindings.Rules ??= [];
        bindings.MarginPixels = Math.Clamp(bindings.MarginPixels, 0, 64);
        bindings.Rules = bindings.Rules
            .Select(NormalizeWindowBindingRule)
            .Where(static rule => rule.Enabled && !string.IsNullOrWhiteSpace(rule.ExtensionId) && !string.IsNullOrWhiteSpace(rule.ProcessName))
            .DistinctBy(static rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return bindings;
    }

    private static WindowBindingRuleSettings NormalizeWindowBindingRule(WindowBindingRuleSettings? rule)
    {
        rule ??= new WindowBindingRuleSettings();
        rule.Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id.Trim();
        rule.ExtensionId = (rule.ExtensionId ?? string.Empty).Trim();
        rule.ProcessName = (rule.ProcessName ?? string.Empty).Trim();
        rule.WindowClass = (rule.WindowClass ?? string.Empty).Trim();
        rule.TitleContains = (rule.TitleContains ?? string.Empty).Trim();
        rule.Corner = WindowBindingCorners.Normalize(rule.Corner);
        rule.OffsetX = RoundToGrid(rule.OffsetX, 10);
        rule.OffsetY = RoundToGrid(rule.OffsetY, 10);
        return rule;
    }

    private static int RoundToGrid(int value, int gridSize)
    {
        if (gridSize <= 0)
        {
            return value;
        }

        return (int)Math.Round(value / (double)gridSize, MidpointRounding.AwayFromZero) * gridSize;
    }
}

public sealed record AppSettings
{
    public string ThemeMode { get; set; } = "Dark";

    public bool AutoCloseToastEnabled { get; set; } = false;

    public string LauncherHotkey { get; set; } = "Alt+Space";

    public bool LaunchAtStartup { get; set; } = true;

    public bool RefreshCloudOnStartup { get; set; } = true;

    public bool ShowBlindOperationGuide { get; set; } = true;

    public bool CloseToTray { get; set; } = true;

    public bool EnableAutoUpdate { get; set; } = true;

    public string AutoBackupFrequency { get; set; } = "Weekly";

    public string LastAutoBackupTime { get; set; } = string.Empty;

    public string CustomBackupDirectory { get; set; } = string.Empty;

    public List<string?> QuickPanelSlots { get; set; } = Enumerable.Repeat<string?>(null, 28).ToList();

    public int QuickPanelRowCount { get; set; } = 3;

    public int QuickPanelGlobalRowCount { get; set; } = 3;

    public int QuickPanelGlobalColumnCount { get; set; } = 4;

    public int QuickPanelContextRowCount { get; set; } = 3;

    public int QuickPanelContextColumnCount { get; set; } = 4;

    public List<QuickPanelGroupSettings> QuickPanelGlobalGroups { get; set; } = [];

    public List<QuickPanelGroupSettings> QuickPanelContextGroups { get; set; } = [];

    public string SelectedQuickPanelGlobalGroupId { get; set; } = "global-default";

    public string SelectedQuickPanelContextGroupId { get; set; } = "context-default";

    public string QuickPanelTrigger { get; set; } = "MiddleButtonLongPress";

        public List<MouseGestureAppBinding> MouseGestureAppBindings { get; set; } = new();
public QuickPanelMouseTriggerSettings QuickPanelMouseTriggers { get; set; } = new();

    public string MouseGestureTriggerMode { get; set; } = MouseGestureTriggerModes.None;
    public bool MouseGestureEnableWheelActions { get; set; } = true;
    public bool MouseGestureEnableRockerActions { get; set; } = true;
    public List<string> MouseGestureBlacklistedProcesses { get; set; } = [];

    public YarnSelectSettings YarnSelect { get; set; } = new();

    public RadialMenuSettings RadialMenu { get; set; } = new();

    public List<string> FavoriteExtensionIds { get; set; } = new();

    public List<string> GlobalFavoriteExtensionIds { get; set; } = new();

    public List<string> ContextFavoriteExtensionIds { get; set; } = new();

    public List<string> DisabledExtensionIds { get; set; } = new();

    public List<string> PinnedSearchScopeCommandIds { get; set; } = new();

    public List<SearchScopeConfigItem> SearchScopeConfigs { get; set; } = new();

    public List<string> RecentlyAddedExtensionIds { get; set; } = new();

    public List<string> UnreadNewExtensionIds { get; set; } = new();

    public int AchievementPoints { get; set; } = 0;

    public bool HasOpenedBackpack { get; set; } = false;

    public List<string> CompletedQuestIds { get; set; } = new();

    public List<string> UnlockedBadges { get; set; } = new();

    public bool EnableAgentApi { get; set; } = true;

    public int AgentApiPort { get; set; } = 53919;

    public string AgentApiToken { get; set; } = "yanzi-local-dev-token";

    public string LauncherResultViewMode { get; set; } = "List";

    public string WanPushUuid { get; set; } = System.Guid.NewGuid().ToString("N");

    public bool EnableLanSync { get; set; } = false;

    public bool EnableBrowserHelper { get; set; } = true;

    public bool EnableWanPush { get; set; } = false;

    public bool EnableEverything { get; set; } = true;

    public PersonalSyncSettings PersonalSync { get; set; } = new();

    public bool EnableWebDavSync { get; set; } = false;

    public bool WebDavSyncManuallyDisabled { get; set; } = false;

    public string WebDavServerUrl { get; set; } = "https://dav.jianguoyun.com/dav/";

    public string WebDavRootPath { get; set; } = "/yanzi";

    public string WebDavUsername { get; set; } = string.Empty;

    public int PersonalSyncAutoSyncDelaySeconds { get; set; } = 10;

    public bool PreferManualExtensionEditor { get; set; } = false;

    public string AiBaseUrl { get; set; } = string.Empty;

    public string AiApiKey { get; set; } = string.Empty;

    public string AiModel { get; set; } = string.Empty;

    public string AiSystemPrompt { get; set; } = string.Empty;

    public List<AiServiceProviderSettings> AiServiceProviders { get; set; } = [];

    public string ActiveServiceProviderId { get; set; } = string.Empty;

    public List<AppEnvironmentVariableSettings> EnvironmentVariables { get; set; } = [];

    public List<YanyuRuleSettings> YanyuRules { get; set; } = [];

    public YanmSettings Yanm { get; set; } = new();

    public bool EnableWindowSnapAssist { get; set; } = true;

    public string WindowSnapAssistHotkey { get; set; } = string.Empty;

    public string WindowSnapAssistMouseTriggerMode { get; set; } = MouseTriggerModes.None;

    public List<WindowSnapAssistCustomLayoutSettings> WindowSnapAssistCustomLayouts { get; set; } = [];

    public WindowBindingSettings WindowBindings { get; set; } = new();

    public bool LegacyCleanupDismissed { get; set; } = false;

    public string LauncherConfigUpdatedAtUtc { get; set; } = string.Empty;

    public string YanmStateUpdatedAtUtc { get; set; } = string.Empty;

    public double? SettingsWindowLeft { get; set; }

    public double? SettingsWindowTop { get; set; }

    public double? SettingsWindowWidth { get; set; }

    public double? SettingsWindowHeight { get; set; }

    public string LastTestArgument { get; set; } = "示例参数";

    public string LastExtensionEditorTab { get; set; } = "simple";

    public string MobileExtensionsJson { get; set; } = "[]";

    public List<string> GlobalServiceBlacklistedProcesses { get; set; } = [];

    public bool DisableInFullScreen { get; set; } = false;

    public System.Collections.Generic.Dictionary<string, string> ProcessExecutablePaths { get; set; } = new();
}

public sealed class WindowSnapAssistCustomLayoutSettings
{
    public const int TotalSlotCount = 16;

    public int SlotIndex { get; set; }

    public double LeftRatio { get; set; }

    public double TopRatio { get; set; }

    public double WidthRatio { get; set; }

    public double HeightRatio { get; set; }
}

public sealed class SearchScopeConfigItem
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsPinned { get; set; } = false;
}

public sealed class QuickPanelGroupSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名";

    public string? ContextProcessName { get; set; }

    public string? ContextDisplayName { get; set; }

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, 12).ToList();

    public List<QuickPanelSlotItem?> SlotItems { get; set; } = Enumerable.Repeat<QuickPanelSlotItem?>(null, 12).ToList();
}

public sealed class QuickPanelSlotItem
{
    public string ItemType { get; set; } = "extension";

    public string? ExtensionId { get; set; }

    public string? FolderName { get; set; }

    public List<string> FolderExtensionIds { get; set; } = [];

    public List<QuickPanelSlotItem?> FolderSlotItems { get; set; } = [];

    public bool IsFolder => string.Equals(ItemType, "folder", StringComparison.OrdinalIgnoreCase);

    public bool IsShortcut { get; set; } = false;
}

public sealed record QuickPanelMouseTriggerSettings
{
    public bool MiddleButtonDown { get; set; } = false;

    public bool X1ButtonDown { get; set; } = false;

    public bool X2ButtonDown { get; set; } = false;

    public bool CtrlLeftClick { get; set; } = false;

    public bool CtrlLeftDrag { get; set; } = false;

    public bool CtrlRightClick { get; set; } = false;
    
    public bool CtrlMiddleClick { get; set; } = false;

    public bool MiddleButtonLongPress { get; set; } = false;

    public bool RightButtonLongPress { get; set; } = true;

    public bool RightButtonDrag { get; set; } = false;

    public bool MiddleButtonDrag { get; set; } = false;

    public bool HorizontalWheel { get; set; } = false;



    public bool ExecuteOnButtonRelease { get; set; } = true;

    public int LongPressMilliseconds { get; set; } = 250;

    public int DragThresholdPixels { get; set; } = 26;
}

public sealed class MouseGestureAppBinding
{
    public string Sequence { get; set; } = string.Empty;
    public string AppPath { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string? ExtensionId { get; set; }
    public bool IsBlacklist { get; set; }
}

public sealed class YarnSelectSettings
{
    public bool Enabled { get; set; } = true;

    public bool LeftCToCopy { get; set; } = true;

    public bool LeftXToCut { get; set; } = true;

    public bool LeftVToPaste { get; set; } = true;

    public bool LeftSToSearch { get; set; } = true;

    public bool LeftRToRun { get; set; } = true;

    public bool LeftRightSmartCopyPaste { get; set; } = true;

    public bool LeftSideButtonPaste { get; set; } = true;

    public int TriggerDelayMilliseconds { get; set; } = 80;

    public List<YarnSelectRuleSettings> Rules { get; set; } = [];

    public List<string> WhitelistedProcesses { get; set; } = [];

    public List<string> BlacklistedProcesses { get; set; } =
    [
        "Photoshop",
        "Maya",
        "Blender"
    ];

    public static List<YarnSelectRuleSettings> CreateDefaultRulesFromLegacy(YarnSelectSettings settings)
    {
        var rules = new List<YarnSelectRuleSettings>();
        if (settings.LeftCToCopy) rules.Add(new YarnSelectRuleSettings { TriggerKey = "C", ActionType = YarnSelectActionTypes.Copy, Description = "复制选中内容" });
        if (settings.LeftXToCut) rules.Add(new YarnSelectRuleSettings { TriggerKey = "X", ActionType = YarnSelectActionTypes.Cut, Description = "剪切选中内容" });
        if (settings.LeftVToPaste) rules.Add(new YarnSelectRuleSettings { TriggerKey = "V", ActionType = YarnSelectActionTypes.Paste, Description = "粘贴到当前位置" });
        if (settings.LeftSToSearch) rules.Add(new YarnSelectRuleSettings { TriggerKey = "S", ActionType = YarnSelectActionTypes.Search, Description = "复制选中内容并搜索" });
        if (settings.LeftRToRun) rules.Add(new YarnSelectRuleSettings { TriggerKey = "R", ActionType = YarnSelectActionTypes.Run, Description = "运行选中内容" });
        if (settings.LeftRightSmartCopyPaste) rules.Add(new YarnSelectRuleSettings { TriggerKey = "Right", ActionType = YarnSelectActionTypes.SmartCopyPaste, Description = "智能复制/粘贴" });
        if (settings.LeftSideButtonPaste) rules.Add(new YarnSelectRuleSettings { TriggerKey = "X1", ActionType = YarnSelectActionTypes.Paste, Description = "侧键粘贴" });
        return rules;
    }

    public static YarnSelectRuleSettings NormalizeRule(YarnSelectRuleSettings? rule)
    {
        rule ??= new YarnSelectRuleSettings();
        rule.TriggerKey = NormalizeTriggerKey(rule.TriggerKey);
        rule.ActionType = YarnSelectActionTypes.Normalize(rule.ActionType);
        rule.ExtensionId = (rule.ExtensionId ?? string.Empty).Trim();
        rule.Description = (rule.Description ?? string.Empty).Trim();
        return rule;
    }

    public static string NormalizeTriggerKey(string? value)
    {
        var key = (value ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return string.Empty;
        }

        var lower = key.ToLowerInvariant();
        if (lower.StartsWith("right", StringComparison.Ordinal) || lower == "右键")
        {
            return "Right";
        }
        if (lower.StartsWith("x1", StringComparison.Ordinal) || lower == "侧键1")
        {
            return "X1";
        }
        if (lower.StartsWith("x2", StringComparison.Ordinal) || lower == "侧键2")
        {
            return "X2";
        }

        return key.Length == 1 ? key.ToUpperInvariant() : key;
    }
}

public sealed class RadialMenuSettings
{
    public const int InnerSlotCount = 8;

    public const int MiddleSlotCount = 16;

    public const int OuterSlotCount = 8;

    public const int TotalSlotCount = InnerSlotCount + MiddleSlotCount + OuterSlotCount;

    public bool Enabled { get; set; } = true;

    public bool TriggerRightButtonDrag { get; set; } = true;

    public bool TriggerMiddleButtonDrag { get; set; } = false;
    
    public bool TriggerRightButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonDown { get; set; } = false;
    
    public bool TriggerX1ButtonDown { get; set; } = false;
    
    public bool TriggerX2ButtonDown { get; set; } = false;
    
    public bool TriggerHorizontalWheel { get; set; } = false;
    
    public bool TriggerCtrlLeftClick { get; set; } = false;
    
    public bool TriggerCtrlLeftDrag { get; set; } = false;
    
    public bool TriggerCtrlRightClick { get; set; } = false;
    
    public bool TriggerCtrlMiddleClick { get; set; } = false;

    public bool TriggerCapsLockHold { get; set; } = true;

    public string ActivationKey { get; set; } = RadialActivationKeys.CapsLock;

    public string CustomShortcut { get; set; } = string.Empty;

    public List<string> WhitelistedProcesses { get; set; } = [];

    public List<string> BlacklistedProcesses { get; set; } = [];

    public string MouseTriggerMode { get; set; } = MouseTriggerModes.RightDrag;

    public int DeadZonePixels { get; set; } = 32;

    public int RadiusPixels { get; set; } = 134;

    public int DragThresholdPixels { get; set; } = 24;

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, TotalSlotCount).ToList();

    public string SelectedPageId { get; set; } = "default";

    public List<RadialMenuPageSettings> Pages { get; set; } = [];

    public HashSet<string> GetChildPageIdsSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Pages == null) return set;
        foreach (var page in Pages)
        {
            if (page.ChildPageIds == null) continue;
            foreach (var childId in page.ChildPageIds)
            {
                if (!string.IsNullOrWhiteSpace(childId) && !string.Equals(childId, page.Id, StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(childId);
                }
            }
        }
        return set;
    }

    /// <summary>
    /// 递归收集指定根页面及其所有层级的后代子环页面 ID
    /// </summary>
    public HashSet<string> CollectPageAndDescendantIds(string rootPageId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rootPageId) || Pages == null)
        {
            return result;
        }

        var stack = new Stack<string>();
        stack.Push(rootPageId.Trim());
        while (stack.Count > 0)
        {
            var pageId = stack.Pop();
            if (!result.Add(pageId))
            {
                continue;
            }

            var page = Pages.FirstOrDefault(p => p.Id.Equals(pageId, StringComparison.OrdinalIgnoreCase));
            if (page?.ChildPageIds == null)
            {
                continue;
            }

            foreach (var childId in page.ChildPageIds)
            {
                if (!string.IsNullOrWhiteSpace(childId) && !string.Equals(childId, page.Id, StringComparison.OrdinalIgnoreCase))
                {
                    stack.Push(childId.Trim());
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 级联删除页面及其所有后代子环，并清理剩余所有槽位中的子环引用
    /// </summary>
    public HashSet<string> CascadeDeletePages(IEnumerable<string> rootPageIdsToDelete)
    {
        var allIdsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Pages == null)
        {
            return allIdsToDelete;
        }

        foreach (var rootId in rootPageIdsToDelete)
        {
            var tree = CollectPageAndDescendantIds(rootId);
            foreach (var id in tree)
            {
                allIdsToDelete.Add(id);
            }
        }

        if (allIdsToDelete.Count == 0)
        {
            return allIdsToDelete;
        }

        // 1. 批量移除所有目标页面与其深层子环页面
        Pages.RemoveAll(p => allIdsToDelete.Contains(p.Id));

        // 2. 清理剩余页面槽位中对已删除页面的子环引用
        foreach (var page in Pages)
        {
            if (page.ChildPageIds == null) continue;
            for (int i = 0; i < page.ChildPageIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(page.ChildPageIds[i]) && allIdsToDelete.Contains(page.ChildPageIds[i]!))
                {
                    page.ChildPageIds[i] = null;
                }
            }
        }

        // 3. 如果当前选中的页面被删除了，重新选定一个合法的顶层页面
        if (allIdsToDelete.Contains(SelectedPageId))
        {
            var childPageIdsSet = GetChildPageIdsSet();
            var topLevelPages = Pages.Where(p => !childPageIdsSet.Contains(p.Id)).ToList();
            SelectedPageId = topLevelPages.FirstOrDefault()?.Id ?? Pages.FirstOrDefault()?.Id ?? "default";
        }

        return allIdsToDelete;
    }
}

public static class RadialActivationKeys
{
    public const string None = "None";
    public const string Win = "Win";
    public const string CapsLock = "CapsLock";
    public const string Custom = "Custom";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "win" or "windows" or "meta" => Win,
            "caps" or "capslock" => CapsLock,
            "custom" or "shortcut" or "hotkey" => Custom,
            "none" or "off" or "disabled" or "disable" => None,
            _ => CapsLock
        };
    }
}

public sealed class RadialMenuPageSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名";

    public List<string?> Slots { get; set; } = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList();

    public List<string?> SlotTitles { get; set; } = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList();

    public List<string?> ChildPageIds { get; set; } = Enumerable.Repeat<string?>(null, RadialMenuSettings.TotalSlotCount).ToList();

    public string? ContextProcessName { get; set; }

    public string? ContextDisplayName { get; set; }
}

public sealed class YanmSettings
{
    public const int CurrentDefaultComponentVersion = 17;

    public bool Enabled { get; set; } = false;

    public string ActivationKey { get; set; } = YanmActivationKeys.Win;

    public string CustomShortcut { get; set; } = string.Empty;

    public List<string> WhitelistedProcesses { get; set; } = [];

    public List<string> BlacklistedProcesses { get; set; } = [];

    public bool TriggerWinHold { get; set; } = true;

    public bool TriggerWinDoubleTap { get; set; } = true;

    public bool TriggerRightButtonDrag { get; set; } = false;

    public bool TriggerMiddleButtonDrag { get; set; } = false;
    
    public bool TriggerRightButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonLongPress { get; set; } = false;
    
    public bool TriggerMiddleButtonDown { get; set; } = false;
    
    public bool TriggerX1ButtonDown { get; set; } = false;
    
    public bool TriggerX2ButtonDown { get; set; } = false;
    
    public bool TriggerHorizontalWheel { get; set; } = false;
    
    public bool TriggerCtrlLeftClick { get; set; } = false;

    public bool TriggerCtrlLeftDrag { get; set; } = false;
    
    public bool TriggerCtrlRightClick { get; set; } = false;
    
    public bool TriggerCtrlMiddleClick { get; set; } = false;

    public string MouseTriggerMode { get; set; } = MouseTriggerModes.None;

    public int DragThresholdPixels { get; set; } = 26;

    public int HoldDelayMilliseconds { get; set; } = 0;

    public int GridSizePixels { get; set; } = 10;

    public double OverlayOpacity { get; set; } = 0.85;

    public bool HasInitializedDefaultComponents { get; set; }

    public int DefaultComponentVersion { get; set; }

    public List<YanmComponentSettings> Components { get; set; } = [];

    public Dictionary<string, string> ComponentState { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class YanmActivationKeys
{
    public const string Win = "Win";

    public const string CapsLock = "CapsLock";

    public const string Custom = "Custom";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "capslock" => CapsLock,
            "custom" => Custom,
            _ => Win
        };
    }
}

public static class MouseTriggerModes
{
    public const string None = "None";
    public const string MiddleDown = "MiddleDown";
    public const string X1Down = "X1Down";
    public const string X2Down = "X2Down";
    public const string CtrlLeftClick = "CtrlLeftClick";
    public const string CtrlLeftDrag = "CtrlLeftDrag";
    public const string CtrlRightClick = "CtrlRightClick";
    public const string CtrlMiddleClick = "CtrlMiddleClick";
    public const string MiddleLongPress = "MiddleLongPress";
    public const string RightLongPress = "RightLongPress";
    public const string RightDrag = "RightDrag";
    public const string MiddleDrag = "MiddleDrag";
    public const string HorizontalWheel = "HorizontalWheel";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim() switch
        {
            MiddleDown => MiddleDown,
            X1Down => X1Down,
            X2Down => X2Down,
            CtrlLeftClick => CtrlLeftClick,
            CtrlLeftDrag => CtrlLeftDrag,
            CtrlRightClick => CtrlRightClick,
            CtrlMiddleClick => CtrlMiddleClick,
            MiddleLongPress => MiddleLongPress,
            RightLongPress => RightLongPress,
            RightDrag => RightDrag,
            MiddleDrag => MiddleDrag,
            HorizontalWheel => HorizontalWheel,
            _ => None
        };
    }
}

public static class MouseGestureTriggerModes
{
    public const string None = "None";
    public const string RightDrag = "RightDrag";
    public const string MiddleDrag = "MiddleDrag";
    public const string CtrlLeftDrag = "CtrlLeftDrag";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim() switch
        {
            RightDrag => RightDrag,
            MiddleDrag => MiddleDrag,
            CtrlLeftDrag => CtrlLeftDrag,
            "right-drag" => RightDrag,
            "middle-drag" => MiddleDrag,
            "ctrl-left-drag" => CtrlLeftDrag,
            _ => None
        };
    }

    public static string ToRuntimeTrigger(string? value)
    {
        return Normalize(value) switch
        {
            MiddleDrag => "middle-drag",
            RightDrag => "right-drag",
            CtrlLeftDrag => "ctrl-left-drag",
            _ => string.Empty
        };
    }

    public static string FromRuntimeTrigger(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Equals("middle-drag", StringComparison.OrdinalIgnoreCase))
        {
            return MiddleDrag;
        }
        if (trimmed.Equals("ctrl-left-drag", StringComparison.OrdinalIgnoreCase))
        {
            return CtrlLeftDrag;
        }
        return RightDrag;
    }
}

public sealed class YanmComponentSettings
{
    public const string ProductivityOverviewId = "cmp_default_productivity_overview";
    public const string ProductivityTodoId = "cmp_default_productivity_todo";
    public const string ProductivityBookmarksId = "cmp_default_productivity_bookmarks";
    public const string ProductivityFocusId = "cmp_default_productivity_focus";
    public const string ProductivityCalendarId = "cmp_default_productivity_calendar";
    public const string ProductivityAppLauncherId = "cmp_default_productivity_app_launcher";
    public const string ProductivityHabitsId = "cmp_default_productivity_habits";
    public const string ProductivityDesktopId = "cmp_default_productivity_desktop";
    public const string ProductivityMoodWaterId = "cmp_default_productivity_mood_water";
    public const string ProductivityNoteId = "cmp_default_productivity_note";
    public const string ProductivitySystemId = "cmp_default_productivity_system";

    private static readonly Dictionary<string, string> DefaultComponentIdsByTitle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["效率概览"] = ProductivityOverviewId,
        ["待办事项"] = ProductivityTodoId,
        ["快速书签"] = ProductivityBookmarksId,
        ["番茄专注"] = ProductivityFocusId,
        ["日历"] = ProductivityCalendarId,
        ["应用启动台"] = ProductivityAppLauncherId,
        ["习惯打卡"] = ProductivityHabitsId,
        ["桌面文件"] = ProductivityDesktopId,
        ["心情喝水"] = ProductivityMoodWaterId,
        ["便签"] = ProductivityNoteId,
        ["系统状态"] = ProductivitySystemId
    };

    public string Id { get; set; } = CreateSystemComponentId();

    public string Title { get; set; } = "燕幕组件";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 320;

    public double Height { get; set; } = 180;

    public bool Locked { get; set; }

    public string Html { get; set; } = DefaultHtml("燕幕组件");

    public string ScriptSource { get; set; } = string.Empty;

    public int RefreshIntervalSeconds { get; set; } = 300;

    public static string CreateSystemComponentId() => $"cmp_{Guid.NewGuid():N}";

    public static bool TryGetDefaultComponentId(string? title, out string id)
    {
        if (DefaultComponentIdsByTitle.TryGetValue((title ?? string.Empty).Trim(), out var value))
        {
            id = value;
            return true;
        }

        id = string.Empty;
        return false;
    }

    public static List<YanmComponentSettings> CreateDefaultComponents()
    {
        return
        [
            new YanmComponentSettings
            {
                Id = ProductivityOverviewId,
                Title = "效率概览",
                X = 70,
                Y = 90,
                Width = 360,
                Height = 210,
                Html = CreateProductivityOverviewHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityTodoId,
                Title = "待办事项",
                X = 450,
                Y = 90,
                Width = 480,
                Height = 250,
                Html = CreateProductivityTodoHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityBookmarksId,
                Title = "快速书签",
                X = 950,
                Y = 90,
                Width = 300,
                Height = 250,
                Html = CreateProductivityBookmarksHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityFocusId,
                Title = "番茄专注",
                X = 70,
                Y = 320,
                Width = 360,
                Height = 190,
                Html = CreateProductivityFocusHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityCalendarId,
                Title = "日历",
                X = 450,
                Y = 360,
                Width = 235,
                Height = 260,
                Html = CreateProductivityCalendarHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityAppLauncherId,
                Title = "应用启动台",
                X = 695,
                Y = 360,
                Width = 235,
                Height = 260,
                Html = CreateProductivityAppLauncherHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityHabitsId,
                Title = "习惯打卡",
                X = 450,
                Y = 640,
                Width = 480,
                Height = 170,
                Html = CreateProductivityHabitsHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityDesktopId,
                Title = "桌面文件",
                X = 950,
                Y = 360,
                Width = 300,
                Height = 310,
                Html = CreateProductivityDesktopHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityMoodWaterId,
                Title = "心情喝水",
                X = 70,
                Y = 530,
                Width = 360,
                Height = 160,
                Html = CreateProductivityMoodWaterHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivityNoteId,
                Title = "便签",
                X = 70,
                Y = 710,
                Width = 360,
                Height = 180,
                Html = CreateProductivityNoteHtml()
            },
            new YanmComponentSettings
            {
                Id = ProductivitySystemId,
                Title = "系统状态",
                X = 950,
                Y = 690,
                Width = 300,
                Height = 120,
                Html = CreateProductivitySystemHtml()
            }
        ];
    }

    public static void UpgradeDefaultComponents(List<YanmComponentSettings> components)
    {
        if (components.All(item => !item.Title.Equals("效率概览", StringComparison.OrdinalIgnoreCase)))
        {
            var legacyDefaultTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "今日概览",
                "待办清单",
                "网页书签",
                "网页监控",
                "系统状态",
                "燕幕提示",
                "应用启动台",
                "便签",
                "番茄时钟",
                "倒计时",
                "桌面文件",
                "下载目录"
            };
            components.RemoveAll(item => legacyDefaultTitles.Contains(item.Title));
            components.AddRange(CreateDefaultComponents());
            return;
        }

        var legacyDownload = components.FirstOrDefault(item =>
            item.Title.Equals("下载目录", StringComparison.OrdinalIgnoreCase));
        if (legacyDownload != null &&
            components.All(item => !item.Title.Equals("桌面文件", StringComparison.OrdinalIgnoreCase)))
        {
            legacyDownload.Title = "桌面文件";
            legacyDownload.Html = CreateDesktopFolderHtml();
            legacyDownload.Width = Math.Max(legacyDownload.Width, 340);
            legacyDownload.Height = Math.Max(legacyDownload.Height, 230);
        }

        var legacyWebMonitor = components.FirstOrDefault(item =>
            item.Title.Equals("网页监控", StringComparison.OrdinalIgnoreCase));
        if (legacyWebMonitor != null &&
            components.All(item => !item.Title.Equals("网页书签", StringComparison.OrdinalIgnoreCase)))
        {
            legacyWebMonitor.Title = "网页书签";
            legacyWebMonitor.Html = CreateWebMonitorHtml();
        }

        var latest = CreateDefaultComponents();
        foreach (var template in latest)
        {
            var existing = components.FirstOrDefault(item =>
                item.Title.Equals(template.Title, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                components.Add(template);
                continue;
            }

            // Only refresh the built-in examples; user-created cards normally have different titles.
            existing.Html = template.Html;
            existing.Width = Math.Max(existing.Width, template.Width);
            existing.Height = Math.Max(existing.Height, template.Height);
        }
    }

    private static string ProductivityShell(string body, string script = "", string accent = "#85b7eb") => $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <style>
    *{box-sizing:border-box;margin:0;padding:0}
    html,body{width:100%;height:100%;overflow:hidden;background:transparent;color:#fff;font-family:"Microsoft YaHei",system-ui,sans-serif}
    body{padding:0}
    .card{width:100%;height:100%;padding:16px;border-radius:18px;background:linear-gradient(145deg,rgba(22,30,45,.98),rgba(8,11,18,.94));border:1px solid rgba(190,215,255,.22);box-shadow:0 24px 76px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.08);overflow:hidden}
    .panel{height:100%;padding:0;border:0;background:transparent;overflow:hidden}
    .lbl{font-size:10px;letter-spacing:.08em;color:rgba(255,255,255,.32);text-transform:uppercase;margin-bottom:8px;display:flex;align-items:center;justify-content:space-between}
    .link{font-size:10px;color:rgba(255,255,255,.28);letter-spacing:0;text-transform:none;cursor:pointer}
    .tag{font-size:10px;padding:3px 8px;border-radius:20px;background:rgba(255,255,255,.07);color:rgba(255,255,255,.42)}
    .tag-g{background:rgba(99,153,34,.15);color:#97c459}.tag-a{background:rgba(186,117,23,.15);color:#fac775}.tag-b{background:rgba(55,138,221,.15);color:#85b7eb}
    .scroll{overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.16) transparent}
    .scroll::-webkit-scrollbar{width:3px;height:3px}.scroll::-webkit-scrollbar-thumb{background:rgba(255,255,255,.16);border-radius:2px}
    button,input,textarea{font-family:inherit}
    button{cursor:pointer}
    kbd{background:rgba(255,255,255,.07);border-radius:3px;padding:1px 5px;font-size:9px;color:rgba(255,255,255,.35)}
    .accent{color:{{accent}}}
  </style>
</head>
<body>
  <div class="card">{{body}}</div>
  {{script}}
</body>
</html>
""";

    private static string CreateProductivityOverviewHtml() => ProductivityShell("""
<div class="panel">
  <div class="lbl">Today <span class="tag tag-g" id="weekTag">--</span></div>
  <div id="clock" style="font-size:46px;font-weight:200;letter-spacing:-2px;line-height:1">--:--</div>
  <div id="date" style="font-size:12px;color:rgba(255,255,255,.42);margin-top:5px">--</div>
  <div style="display:flex;gap:6px;margin-top:12px;flex-wrap:wrap">
    <span class="tag tag-g" id="weekday">工作日</span>
    <span class="tag tag-a" id="todoBadge">待办同步</span>
    <span class="tag tag-b">本机数据</span>
  </div>
  <div style="margin-top:14px;padding-top:12px;border-top:.5px solid rgba(255,255,255,.06);font-size:11px;color:rgba(255,255,255,.36);line-height:1.6">
    今天适合把关键事项、常用应用和临时想法收拢到一屏，减少来回切换。
  </div>
</div>
""", """
<script>
(function(){
  function weekOfYear(d){var x=new Date(Date.UTC(d.getFullYear(),d.getMonth(),d.getDate()));var day=x.getUTCDay()||7;x.setUTCDate(x.getUTCDate()+4-day);var y=new Date(Date.UTC(x.getUTCFullYear(),0,1));return Math.ceil((((x-y)/86400000)+1)/7);}
  function tick(){var d=new Date();document.getElementById('clock').textContent=d.toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});document.getElementById('date').textContent='周'+'日一二三四五六'.charAt(d.getDay())+' · '+(d.getMonth()+1)+'月'+d.getDate()+'日 · 第'+weekOfYear(d)+'周';document.getElementById('weekTag').textContent=d.getFullYear();document.getElementById('weekday').textContent=d.getDay()===0||d.getDay()===6?'周末':'工作日';}
  tick();setInterval(tick,1000);
})();
</script>
""");

    private static string CreateProductivityTodoHtml() => ProductivityShell("""
<div class="panel" style="display:flex;flex-direction:column">
  <div class="lbl">待办事项 <span style="display:flex;gap:6px;align-items:center"><span class="tag tag-a" id="count">0 项</span><span class="link" id="sync">本机缓存</span></span></div>
  <div id="list" class="scroll" style="flex:1;min-height:110px"></div>
  <div style="display:flex;gap:6px;margin-top:8px">
    <input id="input" placeholder="快速添加待办..." style="flex:1;border:0;outline:0;border-radius:7px;padding:7px 10px;background:rgba(255,255,255,.06);color:#fff;font-size:12px">
    <button id="add" style="border:0;border-radius:7px;background:#ef9f27;color:#1a0d00;font-weight:600;padding:0 12px">添加</button>
    <button id="clear" style="border:0;border-radius:7px;background:rgba(255,255,255,.07);color:rgba(255,255,255,.52);padding:0 10px">清理</button>
  </div>
</div>
""", """
<script>
(function(){
  var key='yanm.todo.items.'+((window.yanm&&window.yanm.componentId)||window.__yanmComponentId||'default'), legacy='yanm.todos.v2', todos=[], hostLoaded=false;
  function fallback(){return[{text:'晨会 + 更新周报',done:true,due:'09:30'},{text:'UI 重设计提案 PPT',done:false,due:'今天',hot:true},{text:'与产品对齐需求文档',done:false,due:'14:00'},{text:'整理文档结构',done:false,due:'本周'}];}
  function load(){try{todos=JSON.parse(localStorage.getItem(key)||localStorage.getItem(legacy)||'null')||fallback();}catch(e){todos=fallback();}}
  function local(){try{localStorage.setItem(key,JSON.stringify(todos));localStorage.setItem(legacy,JSON.stringify(todos));}catch(e){}}
  function save(){local();render();if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.set',{key:key,value:JSON.stringify(todos)}).then(function(){hostLoaded=true;render();}).catch(function(){});}}
  function dot(item){return item.done?'#27500a':item.hot?'#ef9f27':'transparent';}
  function render(){var list=document.getElementById('list');list.innerHTML='';var open=0;todos.forEach(function(item,i){if(!item.done)open++;var row=document.createElement('div');row.style.cssText='display:flex;align-items:center;gap:8px;padding:7px 8px;border-radius:7px;cursor:pointer';row.onmouseenter=function(){row.style.background='rgba(255,255,255,.05)'};row.onmouseleave=function(){row.style.background='transparent'};row.innerHTML='<span style="width:7px;height:7px;border-radius:50%;flex-shrink:0;background:'+dot(item)+';border:'+(item.done||item.hot?'0':'1.5px solid rgba(255,255,255,.2)')+'"></span><span style="font-size:13px;flex:1;color:'+(item.done?'rgba(255,255,255,.25)':'rgba(255,255,255,.8)')+';text-decoration:'+(item.done?'line-through':'none')+'">'+item.text+'</span><span style="font-size:10px;color:'+(item.hot?'#ef9f27':'rgba(255,255,255,.25)')+'">'+(item.due||'')+'</span>';row.onclick=function(){item.done=!item.done;save();};list.appendChild(row);});document.getElementById('count').textContent=open+' 项';document.getElementById('sync').textContent=hostLoaded?'已接入宿主同步':'本机缓存';}
  function add(){var input=document.getElementById('input');var v=input.value.trim();if(!v){input.focus();return;}todos.unshift({text:v,done:false,due:'今天'});input.value='';save();input.focus();}
  function request(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.get',{key:key}).then(function(res){var v=res&&res.value;if(v){try{todos=JSON.parse(v)||todos;hostLoaded=true;local();}catch(e){}}render();}).catch(render);return true;}return false;}
  window.addEventListener('yanm:message', function(e) {
    var d = e.detail || {};
    if (d.type === 'host.state' && d.key === key && Object.prototype.hasOwnProperty.call(d, 'value')) {
      var v = String(d.value || '');
      if (v) { try { todos = JSON.parse(v) || todos; hostLoaded = true; local(); render(); } catch(err){} }
    }
  });
  load();render();document.getElementById('add').onclick=add;document.getElementById('clear').onclick=function(){todos=todos.filter(function(x){return !x.done});save();};document.getElementById('input').onkeydown=function(e){if(e.key==='Enter')add();};if(!request())setTimeout(request,300);
})();
</script>
""");

    private static string CreateProductivityFocusHtml() => ProductivityShell("""
<div class="panel">
  <div class="lbl">番茄钟 <span class="tag tag-a" id="round">第 1 轮</span></div>
  <div id="pomo" style="font-size:46px;font-weight:200;letter-spacing:-1px;text-align:center;line-height:1">25:00</div>
  <div id="state" style="font-size:10px;color:rgba(255,255,255,.32);text-align:center;margin-top:5px;letter-spacing:.05em">专注 25 分钟</div>
  <div style="height:3px;background:rgba(255,255,255,.07);border-radius:2px;margin:12px 0"><div id="bar" style="height:3px;border-radius:2px;background:#ef9f27;width:0"></div></div>
  <div style="display:flex;gap:6px;margin-top:12px">
    <button id="reset" style="flex:1;padding:7px 0;border-radius:20px;border:0;background:rgba(255,255,255,.07);color:rgba(255,255,255,.5)">重置</button>
    <button id="toggle" style="flex:1;padding:7px 0;border-radius:20px;border:0;background:#ef9f27;color:#1a0d00;font-weight:600">开始</button>
  </div>
</div>
""", """
<script>
(function(){var total=25*60,left=total,running=false,timer=null,round=1;function fmt(s){return String(Math.floor(s/60)).padStart(2,'0')+':'+String(s%60).padStart(2,'0')}function render(){document.getElementById('pomo').textContent=fmt(left);document.getElementById('toggle').textContent=running?'暂停':'开始';document.getElementById('bar').style.width=Math.round((1-left/total)*100)+'%';document.getElementById('round').textContent='第 '+round+' 轮';document.getElementById('state').textContent=running?'专注中 · 再坚持一下':'专注 25 分钟';}
function tick(){if(left>0){left--;render();return;}clearInterval(timer);timer=null;running=false;round++;left=5*60;total=5*60;document.getElementById('state').textContent='完成，休息 5 分钟';render();}
document.getElementById('toggle').onclick=function(){running=!running;if(running){timer=setInterval(tick,1000)}else{clearInterval(timer);timer=null}render();};document.getElementById('reset').onclick=function(){clearInterval(timer);timer=null;running=false;total=25*60;left=total;render();};render();})();
</script>
""");

    private static string CreateProductivityMoodWaterHtml() => ProductivityShell("""
<div style="display:grid;grid-template-columns:1fr 1fr;gap:8px;height:100%">
  <div class="panel">
    <div class="lbl">今日心情 <span class="link" id="moodHint">记录</span></div>
    <div id="moods" style="display:flex;gap:5px;justify-content:space-around;margin-top:4px"></div>
    <div id="moodLog" style="font-size:10px;color:rgba(255,255,255,.25);margin-top:8px;text-align:center">等待记录</div>
  </div>
  <div class="panel">
    <div class="lbl">喝水 <span class="link" id="addWater">+1 杯</span></div>
    <div id="water" style="display:flex;gap:4px;flex-wrap:wrap;margin-top:5px"></div>
    <div id="waterTxt" style="font-size:10px;color:rgba(55,138,221,.6);margin-top:7px"></div>
  </div>
</div>
""", """
<script>
(function(){var key='yanm.mood.water.v1', state={mood:2,water:5};function load(){try{state=JSON.parse(localStorage.getItem(key)||'null')||state;}catch(e){}}function persist(){try{localStorage.setItem(key,JSON.stringify(state));}catch(e){}if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.set',{key:key,value:JSON.stringify(state)}).catch(function(){});}}
function renderMood(){var labels=['😫','😕','😐','😊','😄'], box=document.getElementById('moods');box.innerHTML='';labels.forEach(function(x,i){var b=document.createElement('button');b.textContent=x;b.style.cssText='width:32px;height:28px;border-radius:7px;border:'+(state.mood===i?'.5px solid rgba(99,153,34,.4)':'0')+';background:'+(state.mood===i?'rgba(99,153,34,.2)':'rgba(255,255,255,.05)')+';font-size:15px';b.onclick=function(){state.mood=i;persist();renderMood();};box.appendChild(b);});document.getElementById('moodLog').textContent='今天已记录';}
function renderWater(){var box=document.getElementById('water');box.innerHTML='';for(var i=0;i<8;i++){var d=document.createElement('div');d.style.cssText='width:20px;height:22px;border-radius:3px;border:.5px solid rgba(55,138,221,.25);background:'+(i<state.water?'rgba(55,138,221,.25)':'rgba(255,255,255,.03)')+';cursor:pointer';(function(idx){d.onclick=function(){state.water=idx+1;persist();renderWater();};})(i);box.appendChild(d);}document.getElementById('waterTxt').textContent='已喝 '+state.water+' / 8 杯';}
function request(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.get',{key:key}).then(function(res){if(res&&res.value){try{state=JSON.parse(res.value)||state;}catch(e){}}renderMood();renderWater();});}}
window.addEventListener('yanm:message',function(e){var d=e.detail||{};if(d.type==='host.state'&&d.key===key&&Object.prototype.hasOwnProperty.call(d,'value')){try{state=JSON.parse(String(d.value||''))||state;renderMood();renderWater();}catch(_e){}}});
load();renderMood();renderWater();document.getElementById('addWater').onclick=function(){state.water=Math.min(8,state.water+1);persist();renderWater();};request();})();
</script>
""");

    private static string CreateProductivityNoteHtml() => ProductivityShell("""
<div class="panel" style="display:flex;flex-direction:column">
  <div class="lbl">Note <span class="link" id="hint">自动保存</span></div>
  <textarea id="note" placeholder="写下临时想法、链接、会议重点..." style="flex:1;width:100%;min-height:80px;border:0;outline:0;resize:none;border-radius:12px;padding:12px;background:rgba(255,255,255,.055);color:rgba(255,255,255,.84);font-size:13px;line-height:1.65"></textarea>
</div>
""", """
<script>
(function(){
  var key='yanm.sticky.note.v1', el=document.getElementById('note'), hint=document.getElementById('hint'), timer=0, hostLoaded=false;
  function local(){try{return localStorage.getItem(key)||'';}catch(e){return '';}}
  function saveLocal(v){try{localStorage.setItem(key,v);}catch(e){}}
  function setHint(text){hint.textContent=text||'自动保存';}
  function apply(v){el.value=typeof v==='string'?v:'';saveLocal(el.value);hostLoaded=true;setHint('已接入宿主同步');}
  function invoke(method,args){if(window.yanm&&window.yanm.invoke){return window.yanm.invoke(method,args||{});}if(window.yanmHost&&method==='state.get'&&yanmHost.getState){return Promise.resolve(yanmHost.getState(args.key));}if(window.yanmHost&&method==='state.set'&&yanmHost.setState){return Promise.resolve(yanmHost.setState(args.key,args.value));}return Promise.reject(new Error('YANM_HOST_UNAVAILABLE'));}
  function request(){invoke('state.get',{key:key}).then(function(res){var v=res&&typeof res.value==='string'?res.value:'';if(v||!local()){apply(v||'');}else{hostLoaded=true;setHint('本机缓存优先');}}).catch(function(){setHint('本机缓存');});}
  function saveHost(v){setHint('保存中...');invoke('state.set',{key:key,value:v}).then(function(){hostLoaded=true;setHint('已保存');}).catch(function(){setHint('本机缓存');});}
  el.value=local();
  el.addEventListener('input',function(){var v=el.value;saveLocal(v);clearTimeout(timer);timer=setTimeout(function(){saveHost(v);},350);if(!hostLoaded){setHint('准备同步');}});
  window.addEventListener('yanm:message',function(e){var d=e.detail||{};if(d.type==='host.state'&&d.key===key&&Object.prototype.hasOwnProperty.call(d,'value')){var v=String(d.value||'');if(v||!local()){apply(v);}else{hostLoaded=true;setHint('本机缓存优先');}}});
  request();setTimeout(request,300);
})();
</script>
""");

    private static string CreateProductivityCalendarHtml() => ProductivityShell("""
<div class="panel">
  <div class="lbl">日历 <span id="monthLabel" style="font-size:11px;color:rgba(255,255,255,.4);letter-spacing:0;text-transform:none">--</span></div>
  <div id="yearLabel" style="font-size:12px;color:rgba(255,255,255,.5);margin-bottom:6px">--</div>
  <div id="cal" style="display:grid;grid-template-columns:repeat(7,1fr);gap:2px;text-align:center"></div>
</div>
""", """
<script>
(function(){var heads=['一','二','三','四','五','六','日'];function render(){var now=new Date(),y=now.getFullYear(),m=now.getMonth();document.getElementById('monthLabel').textContent=(m+1)+'月';document.getElementById('yearLabel').textContent=y+'年'+(m+1)+'月';var cal=document.getElementById('cal');cal.innerHTML='';heads.forEach(function(h){var e=document.createElement('div');e.textContent=h;e.style.cssText='font-size:9px;color:rgba(255,255,255,.25);padding:2px 0';cal.appendChild(e);});var first=new Date(y,m,1),start=(first.getDay()+6)%7,days=new Date(y,m+1,0).getDate(),prev=new Date(y,m,0).getDate();for(var i=0;i<42;i++){var day=i-start+1,other=day<1||day>days,txt=day<1?prev+day:day>days?day-days:day;var e=document.createElement('div');e.textContent=txt;e.style.cssText='font-size:11px;color:'+(other?'rgba(255,255,255,.18)':'rgba(255,255,255,.45)')+';padding:4px 2px;border-radius:4px;position:relative';if(!other&&txt===now.getDate()){e.style.background='#185fa5';e.style.color='#85b7eb';e.style.fontWeight='600';}cal.appendChild(e);}}render();})();
</script>
""");

    private static string CreateProductivityHabitsHtml() => ProductivityShell("""
<div class="panel">
  <div class="lbl">习惯打卡 <span class="link" id="saveHint">宿主同步</span></div>
  <div id="habits"></div>
</div>
""", """
<script>
(function(){var key='yanm.habits.v1', names=['早起','读书','运动'], data=[[3,3,2,3,1,0,0],[2,0,3,2,0,3,2],[3,3,0,3,0,3,2]];function load(){try{data=JSON.parse(localStorage.getItem(key)||'null')||data;}catch(e){}}function save(){try{localStorage.setItem(key,JSON.stringify(data));}catch(e){}if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.set',{key:key,value:JSON.stringify(data)}).catch(function(){});}}
function render(){var root=document.getElementById('habits');root.innerHTML='';names.forEach(function(name,row){var wrap=document.createElement('div');wrap.style.cssText='display:flex;align-items:center;gap:8px;margin-bottom:8px';wrap.innerHTML='<span style="font-size:12px;color:rgba(255,255,255,.6);width:52px;flex-shrink:0">'+name+'</span>';var grid=document.createElement('div');grid.style.cssText='display:grid;grid-template-columns:repeat(7,1fr);gap:3px;flex:1';['一','二','三','四','五','六','日'].forEach(function(h){var d=document.createElement('div');d.textContent=h;d.style.cssText='font-size:9px;color:rgba(255,255,255,.2);text-align:center';grid.appendChild(d);});data[row].forEach(function(v,i){var c=document.createElement('div');c.style.cssText='height:16px;border-radius:3px;background:'+(v===0?'rgba(255,255,255,.05)':v===1?'rgba(99,153,34,.25)':v===2?'rgba(99,153,34,.5)':'#639922')+';cursor:pointer';c.onclick=function(){data[row][i]=(data[row][i]+1)%4;save();render();};grid.appendChild(c);});wrap.appendChild(grid);root.appendChild(wrap);});}
function request(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.get',{key:key}).then(function(res){if(res&&res.value){try{data=JSON.parse(res.value)||data;}catch(e){}}render();});}}
load();render();request();})();
</script>
""");

    private static string CreateProductivityBookmarksHtml() => ProductivityShell("""
<div class="panel" style="display:flex;flex-direction:column">
  <div class="lbl">快速书签 <span class="link" id="add">添加</span></div>
  <input id="input" placeholder="输入网址后回车..." style="border:0;outline:0;border-radius:7px;padding:7px 10px;background:rgba(255,255,255,.06);color:#fff;font-size:12px;margin-bottom:8px">
  <div id="list" class="scroll" style="flex:1"></div>
</div>
""", """
<script>
(function(){var key='yanm.bookmarks.items.'+((window.yanm&&window.yanm.componentId)||window.__yanmComponentId||'default'), urls=[];function fallback(){return['https://github.com','https://www.notion.so','https://www.figma.com','https://www.bilibili.com'];}function load(){try{urls=JSON.parse(localStorage.getItem(key)||'null')||fallback();}catch(e){urls=fallback();}}function save(){try{localStorage.setItem(key,JSON.stringify(urls));}catch(e){}if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.set',{key:key,value:JSON.stringify(urls)}).catch(function(){});}}function host(url){try{return new URL(url).host;}catch(e){return url;}}
function render(){var list=document.getElementById('list');list.innerHTML='';urls.forEach(function(url,i){var row=document.createElement('div');row.style.cssText='display:flex;align-items:center;gap:8px;padding:6px;border-radius:7px;cursor:pointer';row.innerHTML='<div style="width:22px;height:22px;border-radius:5px;background:rgba(255,255,255,.08);display:flex;align-items:center;justify-content:center;font-size:11px;color:rgba(255,255,255,.5)">'+(i+1)+'</div><span style="font-size:12px;color:rgba(255,255,255,.72);flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">'+host(url)+'</span><span style="font-size:10px;color:rgba(255,255,255,.22)">打开</span>';row.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('path.open',{path:url});}};list.appendChild(row);});}
function add(){var input=document.getElementById('input'), v=input.value.trim();if(!v){input.focus();return;}v=/^https?:\/\//i.test(v)?v:'https://'+v;urls.unshift(v);input.value='';save();render();}
function request(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.get',{key:key}).then(function(res){if(res&&res.value){try{urls=JSON.parse(res.value)||urls;}catch(e){}}render();});}}
load();render();document.getElementById('add').onclick=add;document.getElementById('input').onkeydown=function(e){if(e.key==='Enter')add();};request();})();
</script>
""");

    private static string CreateProductivityAppLauncherHtml() => ProductivityShell("""
<div class="panel" style="display:flex;flex-direction:column">
  <div class="lbl">所有应用 <span class="link" id="count">加载中</span></div>
  <input id="query" placeholder="筛选应用..." style="width:100%;border:0;outline:0;border-radius:7px;padding:7px 10px;background:rgba(255,255,255,.06);color:#fff;font-size:12px;margin-bottom:8px">
  <div id="grid" class="scroll" style="display:grid;grid-template-columns:repeat(5,1fr);gap:5px;flex:1"></div>
  <div style="font-size:9px;color:rgba(255,255,255,.18);text-align:center;margin-top:5px">↕ 滚动查看更多</div>
</div>
""", """
<script>
(function(){var all=[],shown=[];function glyph(t){return(t||'?').trim().slice(0,1).toUpperCase();}function render(){var grid=document.getElementById('grid');grid.innerHTML='';document.getElementById('count').textContent=shown.length+' 个';shown.forEach(function(a){var cell=document.createElement('div');cell.style.cssText='display:flex;flex-direction:column;align-items:center;gap:3px;padding:7px 2px;border-radius:8px;cursor:pointer;background:rgba(255,255,255,.02);border:.5px solid transparent';var icon=a.iconDataUrl?'<img src="'+a.iconDataUrl+'" style="width:100%;height:100%;object-fit:cover">':glyph(a.title);cell.innerHTML='<div style="width:34px;height:34px;border-radius:9px;background:rgba(55,138,221,.15);display:flex;align-items:center;justify-content:center;overflow:hidden;font-size:15px;color:#85b7eb">'+icon+'</div><span style="font-size:9px;color:rgba(255,255,255,.45);text-align:center;line-height:1.2;word-break:break-all;max-width:46px">'+(a.title||'应用')+'</span>';cell.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('command.execute',{extensionId:a.extensionId,launchSource:'yanm-app-grid'});}};grid.appendChild(cell);});}
function filter(){var q=document.getElementById('query').value.toLowerCase();shown=all.filter(function(a){return !q||((a.title||'')+' '+(a.subtitle||'')+' '+(a.category||'')).toLowerCase().indexOf(q)>=0;});render();}
function load(){if(!(window.yanm&&window.yanm.invoke)){setTimeout(load,250);return;}window.yanm.invoke('command.list',{source:'application',limit:200}).then(function(res){all=(res&&res.items)||[];shown=all.slice();render();}).catch(function(){all=[];shown=[];render();});}
document.getElementById('query').oninput=filter;load();})();
</script>
""");

    private static string CreateProductivityDesktopHtml() => ProductivityShell("""
<div class="panel" style="display:flex;flex-direction:column">
  <div class="lbl">桌面文件 <span class="link" id="openRoot">打开</span></div>
  <input id="search" placeholder="搜索文件..." style="width:100%;background:rgba(255,255,255,.05);border:.5px solid rgba(255,255,255,.1);border-radius:7px;padding:6px 10px;color:#fff;font-size:12px;outline:none;margin-bottom:8px">
  <div id="list" class="scroll" style="flex:1"></div>
</div>
""", """
<script>
(function(){var root='',items=[],view=[];function ext(name,isDir){if(isDir)return'DIR';var p=(name||'').split('.');return p.length>1?p.pop().slice(0,4).toUpperCase():'FILE';}function render(){var list=document.getElementById('list');list.innerHTML='';view.slice(0,80).forEach(function(f,i){var row=document.createElement('div');row.style.cssText='display:flex;align-items:center;gap:8px;padding:6px;border-radius:7px;cursor:pointer';row.innerHTML='<div style="width:28px;height:28px;border-radius:5px;background:rgba(55,138,221,.2);color:#85b7eb;display:flex;align-items:center;justify-content:center;font-size:9px;font-weight:600">'+ext(f.name,f.isDirectory)+'</div><div style="flex:1;min-width:0"><div style="font-size:12px;color:rgba(255,255,255,.75);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">'+(f.name||'')+'</div><div style="font-size:10px;color:rgba(255,255,255,.25);margin-top:1px">'+(f.isDirectory?'文件夹':'文件')+'</div></div><span style="font-size:10px;color:rgba(255,255,255,.2)">打开</span>';row.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('path.open',{path:f.path});}};list.appendChild(row);});}
function filter(){var q=document.getElementById('search').value.toLowerCase();view=items.filter(function(f){return !q||(f.name||'').toLowerCase().indexOf(q)>=0});render();}
function load(){if(!(window.yanm&&window.yanm.invoke)){setTimeout(load,250);return;}window.yanm.invoke('desktop.list').then(function(res){root=res&&res.root||'';items=res&&res.items||[];view=items.slice();render();}).catch(function(){items=[];view=[];render();});}
document.getElementById('search').oninput=filter;document.getElementById('openRoot').onclick=function(){if(root&&window.yanm&&window.yanm.invoke)window.yanm.invoke('path.open',{path:root});};load();})();
</script>
""");

    private static string CreateProductivitySystemHtml() => ProductivityShell("""
<div class="panel">
  <div class="lbl">系统状态 <span class="link" id="time">--</span></div>
  <div style="display:flex;gap:6px">
    <div style="flex:1;background:rgba(255,255,255,.03);border-radius:7px;padding:7px 8px"><div id="cpu" style="font-size:16px;font-weight:200">--</div><div style="font-size:9px;color:rgba(255,255,255,.3)">CPU 核心</div></div>
    <div style="flex:1;background:rgba(255,255,255,.03);border-radius:7px;padding:7px 8px"><div id="mem" style="font-size:16px;font-weight:200">--</div><div style="font-size:9px;color:rgba(255,255,255,.3)">内存</div><div style="height:2px;border-radius:2px;background:rgba(255,255,255,.06);margin-top:5px"><div id="memBar" style="height:2px;border-radius:2px;background:#378add;width:0"></div></div></div>
    <div style="flex:1;background:rgba(255,255,255,.03);border-radius:7px;padding:7px 8px"><div id="net" style="font-size:16px;font-weight:200">--</div><div style="font-size:9px;color:rgba(255,255,255,.3)">网络</div></div>
  </div>
</div>
""", """
<script>
(function(){function paint(d){document.getElementById('cpu').textContent=d&&d.cpuCores?d.cpuCores:'--';var p=d&&d.usedMemoryPercent?Math.round(d.usedMemoryPercent):0;document.getElementById('mem').textContent=p?p+'%':'--';document.getElementById('memBar').style.width=(p||0)+'%';document.getElementById('net').textContent=d&&d.isNetworkAvailable?'在线':'离线';document.getElementById('time').textContent=d&&d.time?d.time:new Date().toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});}
function req(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('system.info').then(paint).catch(function(){paint(null);});}else if(window.yanmHost&&yanmHost.requestSystemInfo){yanmHost.requestSystemInfo();}else{paint(null);}}
window.addEventListener('yanm:message',function(e){if(e.detail&&e.detail.type==='host.systemInfo')paint(e.detail);});req();setInterval(req,3000);})();
</script>
""");

    public static string DefaultHtml(string? title)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "燕幕组件" : title.Trim());
        return $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    html, body { width: 100%; height: 100%; overflow: hidden; background: transparent; color: #fff; font-family: "Microsoft YaHei", system-ui, sans-serif; }
    .card { width: 100%; height: 100%; padding: 16px; border-radius: 18px; background: linear-gradient(145deg,rgba(22,30,45,.98),rgba(8,11,18,.94)); border: 1px solid rgba(190,215,255,.22); box-shadow: 0 24px 76px rgba(0,0,0,.46), inset 0 1px 0 rgba(255,255,255,.08); overflow: hidden; }
    .lbl { font-size: 10px; letter-spacing: .08em; color: rgba(255,255,255,.32); text-transform: uppercase; margin-bottom: 10px; display: flex; justify-content: space-between; align-items: center; }
    .tag { font-size: 10px; padding: 3px 8px; border-radius: 20px; background: rgba(55,138,221,.15); color: #85b7eb; }
    h1 { margin: 0 0 8px; font-size: 22px; font-weight: 500; letter-spacing: -.02em; }
    p { margin: 0; color: rgba(255,255,255,.52); line-height: 1.7; font-size: 12px; }
  </style>
</head>
<body>
  <section class="card">
    <div class="lbl">YANM COMPONENT <span class="tag">宿主同步</span></div>
    <h1>{{safeTitle}}</h1>
    <p>这是一个燕幕 HTML 组件。可以接入状态、系统信息、桌面文件、应用启动和本机持久化数据。</p>
  </section>
</body>
</html>
""";
    }

    public static string BuildAiPrompt()
    {
        return """
请为“燕子启动器”的“燕幕”功能生成一个可直接粘贴使用的 HTML 信息组件。

输出要求：
1. 只输出 Markdown，不要解释，不要额外正文。
2. 最终内容必须放在一个 `html` 代码块里，格式为 ```html ... ```，方便预览和复制。
3. 代码块内必须是完整单文件 HTML：包含 <!doctype html>、html、head、style、body、script。
4. 不要依赖外部网络资源、CDN、图片、字体或 npm 包。
5. 组件运行在 Microsoft WebView2 中，组件尺寸由宿主控制，CSS 必须适配任意宽高。
6. html, body 必须：margin:0; width:100%; height:100%; overflow:hidden; background:transparent。
7. 主体只用一个 .card 填满 100% 宽高，box-sizing:border-box，圆角 18px，背景使用深色渐变而不是纯黑；边框用 1px 冷色半透明线并加轻微内高光，确保组件和燕幕背景能分开；不要在组件内部再套第二层卡片或外框。
8. 字体使用 "Microsoft YaHei", sans-serif，文字优先中文，视觉风格要像高级效率工具，不要白底表单风。
9. 可交互组件优先使用燕幕宿主状态保存数据，再用 localStorage 做本机兜底。
10. 输入框、按钮要有清晰 hover/active 状态，颜色要适配深色玻璃拟态背景。
11. 避免页面滚动条；内部列表需要滚动时只让内部容器滚动。
12. 如果需要滚动条，请不要使用默认系统样式。必须自定义成窄条、暗色、低对比度、圆角滑块的样式，并尽量不影响视觉。

宿主能力协议：
1. 统一入口是 `window.yanm.invoke(method, args)`，返回 `Promise`。
2. 兼容封装仍可存在，但组件优先使用 `window.yanm.invoke("clipboard.read")` 这类写法。
3. 宿主返回的数据统一通过：
   `window.addEventListener('yanm:message', function(e) { ... })`
4. 当 `e.detail.type === 'yanm.reply'` 时，说明一次 `invoke` 已完成，可根据 `id` 取回结果。
5. 当 `e.detail.type === 'host.systemInfo'` 时，可读取：
   - `e.detail.cpuCores`
   - `e.detail.isNetworkAvailable`
   - `e.detail.machineName`
   - `e.detail.osVersion`
   - `e.detail.time`
   - `e.detail.date`
   - `e.detail.totalMemoryMb`
   - `e.detail.availableMemoryMb`
   - `e.detail.usedMemoryPercent`
6. 当 `e.detail.type === 'host.state'` 时，可读取：
   - `e.detail.key`
   - `e.detail.value`
7. 组件初始化时不能假设 `window.yanm` 或 `window.yanmHost` 已经存在。必须实现重试初始化，例如 `setTimeout(initHost, 200)`。
8. 严禁写同步错误代码，例如：
   - `const value = window.yanmHost.getState("k")`
   - `const info = window.yanmHost.requestSystemInfo()`
   - `const text = window.yanm.invoke("clipboard.read")`
9. 正确模式是：
   - 先渲染本地兜底内容
   - 再异步请求宿主数据
   - 再在 `yanm:message` 里更新界面或处理 `Promise`
10. 如果组件包含备注、待办、输入框、开关等交互状态，必须：
   - 先更新内存中的当前状态
   - 同步刷新界面
   - 调用 `localStorage` 做本机兜底
   - 再调用 `window.yanm.invoke("state.set", { key: "...", value: "..." })` 保存到宿主
11. 如果组件需要剪贴板、桌面文件、命令执行、系统信息，优先使用以下能力名：
   - `clipboard.read`
   - `clipboard.write`
   - `desktop.list`
   - `command.execute`
   - `system.info`
   - `state.get`
   - `state.set`
   - `path.open`
   - `file.read`
   - `file.write`
   - `file.delete`
   - `file.exists`
   - `file.list`
   - `file.copy`
   - `file.move`
   - `path.downloads`

12. 文件能力说明：
   - `file.read` 默认按文本读取；传 `binary: true` 时返回 `contentBase64`
   - `file.write` 默认按文本写入；传 `binary: true` 且 `contentBase64` 时写入二进制
   - `file.delete` 支持文件与目录，目录可传 `recursive: true`
   - `file.list` 读取目录列表，可传 `recursive` 和 `limit`
   - `file.copy` / `file.move` 传 `source`、`destination`、`overwrite`
   - `path.downloads` 返回用户下载目录，不存在时回退到桌面目录

13. 如果你要生成“下载目录”类组件，推荐直接使用宿主能力：
   - `const folder = await window.yanm.invoke("path.downloads")`
   - `const list = await window.yanm.invoke("file.list", { path: folder.path, limit: 20 })`
   - `await window.yanm.invoke("path.open", { path: item.path })`

下载目录组件示例模板：
```html
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <style>
    html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent;font-family:"Microsoft YaHei",sans-serif;color:#fff}
    .card{box-sizing:border-box;width:100%;height:100%;padding:16px;border-radius:18px;background:linear-gradient(145deg,rgba(22,30,45,.98),rgba(8,11,18,.94));border:1px solid rgba(190,215,255,.22);box-shadow:0 24px 76px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.08);overflow:hidden}
    .lbl{font-size:10px;letter-spacing:.08em;color:rgba(255,255,255,.32);text-transform:uppercase;margin-bottom:10px;display:flex;align-items:center;justify-content:space-between}
    .title{font-size:22px;font-weight:500;letter-spacing:-.02em;margin:0 0 8px}
    .path{font-size:12px;color:rgba(255,255,255,.5);word-break:break-all;margin-bottom:12px}
    .list{height:calc(100% - 110px);overflow:auto}
    .item{display:flex;justify-content:space-between;gap:10px;padding:10px 0;border-top:1px solid rgba(255,255,255,.06);cursor:pointer}
    .name{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .meta{font-size:12px;color:#85b7eb}
  </style>
</head>
<body>
  <section class="card">
    <div class="lbl">DOWNLOADS <span>HOST</span></div>
    <div class="title">下载目录</div>
    <div class="path" id="folderPath">正在加载...</div>
    <div class="list" id="fileList"></div>
  </section>
  <script>
    var folder = '';
    var items = [];
    function render(){
      document.getElementById('folderPath').innerText = folder || '未找到下载目录，已回退到桌面。';
      var list = document.getElementById('fileList');
      list.innerHTML = '';
      items.forEach(function(item){
        var row = document.createElement('div');
        row.className = 'item';
        row.innerHTML = '<div class="name">' + (item.name || item.path || '') + '</div><div class="meta">' + (item.isDirectory ? '文件夹' : '文件') + '</div>';
        row.onclick = function(){ window.yanm.invoke('path.open', { path: item.path }); };
        list.appendChild(row);
      });
    }
    function init(){
      if(!window.yanm || !window.yanm.invoke){
        setTimeout(init, 200);
        return;
      }
      window.yanm.invoke('path.downloads').then(function(res){
        folder = res && res.path ? res.path : '';
        return window.yanm.invoke('file.list', { path: folder, limit: 20 });
      }).then(function(res){
        items = (res && res.items) || [];
        render();
      }).catch(function(){
        render();
      });
    }
    init();
  </script>
</body>
</html>
```

设计参考：
- 组件外层只保留一个 .card，填满宿主给定尺寸，不要再做内层面板、内层边框或嵌套卡片。
- 默认视觉沿用燕幕效率组件：深色渐变底、1px 冷色半透明边框、18px 圆角、柔和外阴影和轻微内高光，避免组件和燕幕背景糊在一起。
- 标签使用 10px、letter-spacing:.08em、低透明度；标题 20-26px，字重 300-600，轻微负字距。
- 数据块可使用小型 pill/chip/grid，但背景保持低对比，不要破坏统一风格。
- 交互控件使用圆角 12-16px，背景 rgba(255,255,255,.06-.12)，hover/active 只做轻微亮度变化。

基础模板：
```html
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
    <style>
    html,body{margin:0;width:100%;height:100%;overflow:hidden;background:transparent;font-family:"Microsoft YaHei",sans-serif;color:#fff}
    .card{box-sizing:border-box;width:100%;height:100%;padding:16px;border-radius:18px;background:linear-gradient(145deg,rgba(22,30,45,.98),rgba(8,11,18,.94));border:1px solid rgba(190,215,255,.22);box-shadow:0 24px 76px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.08);overflow:hidden}
    .lbl{font-size:10px;letter-spacing:.08em;color:rgba(255,255,255,.32);text-transform:uppercase;margin-bottom:10px;display:flex;align-items:center;justify-content:space-between}
    .tag{font-size:10px;padding:3px 8px;border-radius:20px;background:rgba(55,138,221,.15);color:#85b7eb}
    .title{font-size:24px;font-weight:500;letter-spacing:-.03em;margin:0 0 10px}
    .muted{font-size:12px;color:rgba(255,255,255,.52);line-height:1.7}
    .value{font-size:18px;font-weight:600}
    .scrollbar{scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}
    .scrollbar::-webkit-scrollbar{width:6px;height:6px}
    .scrollbar::-webkit-scrollbar-track{background:transparent}
    .scrollbar::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}
  </style>
</head>
<body>
  <section class="card">
    <div class="lbl">YANM COMPONENT <span class="tag">HOST</span></div>
    <div class="title">组件标题</div>
    <div class="muted" id="status">等待宿主数据</div>
    <div class="value" id="content">--</div>
  </section>
  <script>
    var currentNote = '';
    function render(){
      document.getElementById('content').innerText = currentNote || '--';
    }
    function requestHost(){
      if(window.yanm && window.yanm.invoke){
        window.yanm.invoke('system.info');
        window.yanm.invoke('state.get', { key: 'demo.key' });
        return true;
      }
      return false;
    }
    function initHost(){
      if(!requestHost()){
        setTimeout(initHost, 200);
      }
    }
    window.addEventListener('yanm:message', function(e){
      var d = e.detail || {};
      if(d.type === 'yanm.reply' && d.id){
        if(d.ok === false){ return; }
      }
      if(d.type === 'host.systemInfo'){
        document.getElementById('status').innerText = '在线 ' + (d.machineName || '--');
      }
      if(d.type === 'host.state' && d.key === 'demo.key'){
        currentNote = d.value || '';
        render();
      }
    });
    try{
      currentNote = localStorage.getItem('demo.key') || '';
      render();
    }catch(e){}
    initHost();
  </script>
</body>
</html>
```

请按以上规范生成一个实用组件，主题由我下一句需求决定；如果我没有指定主题，请生成一个“今日效率概览”组件。
""";
    }

    private static string CreateOverviewHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:28px;background:radial-gradient(circle at 20% 0%,rgba(56,189,248,.42),transparent 32%),linear-gradient(135deg,rgba(28,39,63,.98),rgba(8,12,22,.94));border:1px solid rgba(190,215,255,.24);box-shadow:0 26px 82px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.1)}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;color:#7dd3fc;letter-spacing:.18em}.clock{font-size:13px;color:rgba(255,255,255,.72)}
.date{font-size:30px;font-weight:800;margin:14px 0 4px}.line{color:rgba(255,255,255,.7);font-size:13px;line-height:1.7}
.grid{display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;margin-top:16px}.pill{padding:12px;border-radius:18px;background:rgba(255,255,255,.09);border:1px solid rgba(255,255,255,.08)}.n{font-size:20px;font-weight:800}.l{font-size:11px;color:rgba(255,255,255,.58);margin-top:4px}
</style></head><body><section class="card"><div class="top"><div class="tag">TODAY</div><div id="clock" class="clock"></div></div><div id="date" class="date">今日概览</div><div class="line">快速扫一眼今天：时间、待办、刷新节奏和你关心的数据都可以放在这里。</div><div class="grid"><div class="pill"><div class="n" id="day">--</div><div class="l">星期</div></div><div class="pill"><div class="n">3</div><div class="l">待处理</div></div><div class="pill"><div class="n">30m</div><div class="l">刷新</div></div></div></section><script>
function tick(){var d=new Date();document.getElementById('clock').innerText=d.toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});document.getElementById('date').innerText=(d.getMonth()+1)+'月'+d.getDate()+'日';document.getElementById('day').innerText='周'+'日一二三四五六'.charAt(d.getDay());}tick();setInterval(tick,1000);
</script></body></html>
""";

    private static string CreateTodoHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:28px;background:radial-gradient(circle at 100% 0%,rgba(34,197,94,.4),transparent 34%),linear-gradient(160deg,rgba(20,56,44,.98),rgba(8,16,19,.94));border:1px solid rgba(187,247,208,.22);box-shadow:0 26px 82px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.1);display:flex;flex-direction:column}
.head{display:flex;justify-content:space-between;align-items:center}h1{font-size:22px;margin:0}.count{font-size:12px;color:#86efac;background:rgba(34,197,94,.12);padding:5px 9px;border-radius:999px}
.add{display:flex;gap:8px;margin:14px 0}input{flex:1;border:0;outline:0;border-radius:14px;padding:10px 12px;background:rgba(255,255,255,.1);color:white}button{border:0;border-radius:14px;padding:0 13px;background:#22c55e;color:#06200f;font-weight:800;cursor:pointer}.ghost{background:rgba(255,255,255,.1);color:#d1fae5}.list{flex:1;min-height:80px;overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}.list::-webkit-scrollbar{width:6px;height:6px}.list::-webkit-scrollbar-track{background:transparent}.list::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}.item{display:flex;gap:10px;align-items:center;padding:9px 0;border-top:1px solid rgba(255,255,255,.08)}.check{width:18px;height:18px;border-radius:9px;background:#22c55e;cursor:pointer;box-shadow:0 0 0 1px rgba(255,255,255,.18) inset}.done .text{text-decoration:line-through;color:rgba(255,255,255,.42)}.text{flex:1;font-size:13px}.del{background:rgba(255,255,255,.1);color:#fecaca;height:26px}.foot{display:flex;justify-content:space-between;align-items:center;margin-top:10px;font-size:12px;color:rgba(255,255,255,.58)}
</style></head><body><section class="card"><div class="head"><h1>待办清单</h1><span id="count" class="count">0 项</span></div><div class="add"><input id="todoInput" placeholder="添加一条待办..." /><button id="addButton" type="button">添加</button></div><div id="todoList" class="list"></div><div class="foot"><span id="syncHint">本机缓存</span><button id="clearDoneButton" type="button" class="ghost">清理已完成</button></div></section><script>
(function(){var hostKey='yanm.todo.items.'+(((window.yanm&&window.yanm.componentId)||window.__yanmComponentId||'default'));var legacyKey='yanm.todos.v2';var todos=[];var hostLoaded=false;
function fallback(){return[{text:"把常用信息组件化",done:false},{text:"接入脚本数据源",done:false},{text:"固定到顺手的位置",done:false}];}
function loadLocal(){try{var raw=localStorage.getItem(hostKey)||localStorage.getItem(legacyKey);todos=raw?JSON.parse(raw):fallback();}catch(e){todos=fallback();}}
function saveLocal(){try{localStorage.setItem(hostKey,JSON.stringify(todos));localStorage.setItem(legacyKey,JSON.stringify(todos));}catch(e){}}
function saveHost(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.set',{key:hostKey,value:JSON.stringify(todos)}).then(function(){hostLoaded=true;render();}).catch(function(){render();});}}
function el(tag,cls,text){var n=document.createElement(tag);if(cls)n.className=cls;if(text)n.appendChild(document.createTextNode(text));return n;}
function persist(){saveLocal();render();saveHost();}
function render(){var list=document.getElementById("todoList");var count=document.getElementById("count");var syncHint=document.getElementById("syncHint");list.innerHTML="";var open=0;for(var i=0;i<todos.length;i++){(function(index){var item=todos[index];if(!item.done)open++;var row=el("div","item "+(item.done?"done":""));var check=el("span","check");var text=el("span","text",item.text);var del=el("button","del","×");check.onclick=function(){item.done=!item.done;persist();};del.onclick=function(){todos.splice(index,1);persist();};row.appendChild(check);row.appendChild(text);row.appendChild(del);list.appendChild(row);})(i);}count.innerText=open+" 项";syncHint.innerText=hostLoaded?'已接入宿主同步':'本机缓存';}
function add(){var input=document.getElementById("todoInput");var v=input.value.replace(/^\s+|\s+$/g,"");if(!v){input.focus();return;}todos.unshift({text:v,done:false});input.value="";persist();input.focus();}
function clearDone(){todos=todos.filter(function(item){return !item.done;});persist();}
function requestHost(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.get',{key:hostKey}).then(function(res){var value=res&&typeof res.value==='string'?res.value:'';if(value){try{todos=JSON.parse(value)||fallback();hostLoaded=true;saveLocal();}catch(e){}}render();}).catch(function(){render();});return true;}return false;}
function init(){if(window.__yanmTodoReady)return;window.__yanmTodoReady=true;loadLocal();render();document.getElementById("addButton").onclick=add;document.getElementById("clearDoneButton").onclick=clearDone;document.getElementById("todoInput").onkeydown=function(e){e=e||window.event;if(e.keyCode===13)add();};if(!requestHost()){setTimeout(requestHost,300);}}
window.addEventListener('yanm:message',function(e){var d=e.detail||{};if(d.type==='host.state'&&d.key===hostKey&&typeof d.value==='string'&&d.value){try{todos=JSON.parse(d.value)||fallback();hostLoaded=true;saveLocal();render();}catch(_e){}}});
window.addTodo=add;if(document.readyState==="loading"){document.addEventListener("DOMContentLoaded",init);}else{init();}})();
</script></body></html>
""";

    private static string CreateWebMonitorHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
 .card{box-sizing:border-box;height:100%;padding:18px;border-radius:24px;background:radial-gradient(circle at 100% 0%,rgba(251,191,36,.28),transparent 34%),linear-gradient(135deg,rgba(69,47,24,.98),rgba(18,15,13,.94));border:1px solid rgba(253,230,138,.22);box-shadow:0 26px 82px rgba(0,0,0,.45),inset 0 1px 0 rgba(255,255,255,.1);display:flex;flex-direction:column}
.top{display:flex;justify-content:space-between;align-items:center}.tag{color:#fbbf24;font-size:12px;letter-spacing:.18em}button{border:0;border-radius:13px;padding:8px 12px;background:rgba(255,255,255,.1);color:#fff;cursor:pointer}h1{margin:10px 0 12px;font-size:24px}.row{display:flex;gap:8px;margin-bottom:10px}input{flex:1;min-width:0;border:0;outline:0;border-radius:14px;padding:10px 12px;background:rgba(255,255,255,.1);color:#fff}button.primary{background:#fbbf24;color:#2b1800;font-weight:800}.list{flex:1;min-height:80px;overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}.list::-webkit-scrollbar{width:6px;height:6px}.list::-webkit-scrollbar-track{background:transparent}.list::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}.item{display:flex;justify-content:space-between;gap:10px;padding:10px 0;border-top:1px solid rgba(255,255,255,.08);cursor:pointer}.left{min-width:0;flex:1}.url{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#fde68a;font-size:12px}.title{font-size:13px;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;margin-top:4px}.meta{font-size:12px;color:rgba(255,255,255,.58)}.remove{background:rgba(255,255,255,.08);color:#fecaca;height:28px;min-width:44px}.empty{font-size:12px;color:rgba(255,255,255,.55);padding-top:16px}
</style></head><body><section class="card"><div class="top"><div class="tag">BOOKMARKS</div><button id="addBtn" type="button">添加书签</button></div><h1>网页书签</h1><div class="row"><input id="urlInput" placeholder="输入网址后回车，或点击右上角添加书签" /></div><div id="list" class="list"></div></section><script>
(function(){var hostKey='yanm.bookmarks.items.'+(((window.yanm&&window.yanm.componentId)||window.__yanmComponentId||'default'));var urls=[];
function fallback(){return['https://github.com','https://www.bilibili.com','https://www.zhihu.com'];}
function loadLocal(){try{urls=JSON.parse(localStorage.getItem(hostKey)||'null')||fallback();}catch(e){urls=fallback();}}
function save(){try{localStorage.setItem(hostKey,JSON.stringify(urls));}catch(e){} if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.set',{key:hostKey,value:JSON.stringify(urls)}).catch(function(){});}}
function hostName(url){try{return new URL(url).host;}catch(e){return url;}}
function render(){var list=document.getElementById('list');list.innerHTML='';if(!urls.length){list.innerHTML='<div class=\"empty\">还没有书签，点击右上角或输入网址添加。</div>';return;}urls.forEach(function(url,index){var row=document.createElement('div');row.className='item';row.innerHTML='<div class=\"left\"><div class=\"title\">'+hostName(url)+'</div><div class=\"url\">'+url+'</div></div><div class=\"meta\">打开</div>';row.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('path.open',{path:url});}};var remove=document.createElement('button');remove.className='remove';remove.type='button';remove.innerText='删';remove.onclick=function(e){e.stopPropagation();urls.splice(index,1);save();render();};row.appendChild(remove);list.appendChild(row);});}
function addUrl(prefill){var input=document.getElementById('urlInput');var raw=(typeof prefill==='string'&&prefill?prefill:input.value||'').replace(/^\s+|\s+$/g,'');if(!raw){input.focus();return;}var v=/^https?:\/\//i.test(raw)?raw:'https://'+raw;if(urls.indexOf(v)>=0){input.value='';input.focus();return;}urls.unshift(v);input.value='';save();render();}
function init(){loadLocal();render();document.getElementById('addBtn').onclick=function(){addUrl();};document.getElementById('urlInput').onkeydown=function(e){e=e||window.event;if(e.keyCode===13)addUrl();};if(window.yanm&&window.yanm.invoke){window.yanm.invoke('state.get',{key:hostKey}).then(function(res){var value=res&&typeof res.value==='string'?res.value:'';if(value){try{urls=JSON.parse(value)||fallback();save();render();}catch(e){}}});}else{setTimeout(init,250);}}
window.addEventListener('yanm:message',function(e){var d=e.detail||{};if(d.type==='host.state'&&d.key===hostKey&&Object.prototype.hasOwnProperty.call(d,'value')){try{urls=JSON.parse(String(d.value||''))||fallback();loadLocal=function(){};render();}catch(_e){}}});
init();})();
</script></body></html>
""";

    private static string CreateSystemHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:28px;background:radial-gradient(circle at 20% 0%,rgba(129,140,248,.42),transparent 35%),linear-gradient(135deg,rgba(43,52,78,.98),rgba(10,14,24,.94));border:1px solid rgba(199,210,254,.22);box-shadow:0 26px 82px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.1)}
h1{font-size:22px;margin:0 0 12px}.row{display:flex;justify-content:space-between;align-items:center;margin:9px 0;color:rgba(255,255,255,.72);font-size:13px}.bar{height:9px;border-radius:9px;background:rgba(255,255,255,.12);overflow:hidden}.fill{height:100%;width:0;background:linear-gradient(90deg,#38bdf8,#22c55e);transition:.3s}.chips{display:flex;gap:8px;margin-top:14px}.chip{flex:1;padding:9px;border-radius:15px;background:rgba(255,255,255,.08);font-size:11px;color:rgba(255,255,255,.65)}.v{display:block;color:white;font-size:16px;font-weight:800;margin-top:3px}
</style></head><body><section class="card"><h1>系统状态</h1><div class="row"><span>内存估算</span><strong id="memText">读取中</strong></div><div class="bar"><div id="memFill" class="fill"></div></div><div class="chips"><div class="chip">CPU 核心<span id="cores" class="v">--</span></div><div class="chip">在线状态<span id="net" class="v">--</span></div><div class="chip">时间<span id="time" class="v">--</span></div></div></section><script>
function paint(data){var percent=data&&data.usedMemoryPercent?data.usedMemoryPercent:0;var total=data&&data.totalMemoryMb?Math.round(data.totalMemoryMb/1024):0;var free=data&&data.availableMemoryMb?Math.round(data.availableMemoryMb/1024):0;document.getElementById('memText').innerText=total?('已用 '+Math.round(percent)+'% · 可用 '+free+' GB / '+total+' GB'):'等待宿主数据';document.getElementById('memFill').style.width=(percent||38)+'%';document.getElementById('cores').innerText=data&&data.cpuCores?data.cpuCores:'--';document.getElementById('net').innerText=data&&data.isNetworkAvailable?'在线':'离线';document.getElementById('time').innerText=data&&data.time?data.time:new Date().toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});}
window.addEventListener('yanm:message',function(e){if(e.detail&&e.detail.type==='host.systemInfo')paint(e.detail);});
function request(){if(window.yanmHost&&yanmHost.requestSystemInfo){yanmHost.requestSystemInfo();}else{paint(null);}}request();setInterval(request,3000);
</script></body></html>
""";

    private static string CreateTipsHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:24px;background:linear-gradient(135deg,rgba(48,40,92,.98),rgba(15,15,26,.94));border:1px solid rgba(216,180,254,.22);box-shadow:0 26px 82px rgba(0,0,0,.42),inset 0 1px 0 rgba(255,255,255,.1)}
h1{font-size:23px;margin:0 0 10px}.p{font-size:13px;color:rgba(255,255,255,.68);line-height:1.8}.kbd{display:inline-block;padding:2px 8px;border-radius:8px;background:rgba(255,255,255,.12);color:#fff}
</style></head><body><section class="card"><h1>燕幕提示</h1><div class="p"><span class="kbd">按住 Win</span> 临时查看；<span class="kbd">双击 Win</span> 固定编辑；拖动空白区域新建组件；右键组件可编辑、锁定、删除。</div></section></body></html>
""";

    private static string CreateStickyNoteHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#1b1608;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:26px;background:linear-gradient(145deg,#fde68a,#facc15);border:1px solid rgba(255,255,255,.5);box-shadow:0 22px 70px rgba(0,0,0,.28)}
.head{display:flex;justify-content:space-between;align-items:center;color:#713f12}.tag{font-size:12px;letter-spacing:.16em;font-weight:800}.hint{font-size:11px;opacity:.65}
textarea{box-sizing:border-box;width:100%;height:150px;margin-top:12px;border:0;outline:0;resize:none;background:rgba(255,255,255,.28);border-radius:18px;padding:14px;color:#241a05;font-size:15px;line-height:1.6}
textarea{height:calc(100% - 46px)}
</style></head><body><section class="card"><div class="head"><div class="tag">NOTE</div><div id="hint" class="hint">自动保存</div></div><textarea id="note" placeholder="写下临时想法、链接、会议重点..."></textarea></section><script>
(function(){var key='yanm.sticky.note.v1';var el=document.getElementById('note');var hint=document.getElementById('hint');var timer=0;var hostLoaded=false;function local(){try{return localStorage.getItem(key)||'';}catch(e){return '';}}function saveLocal(v){try{localStorage.setItem(key,v);}catch(e){}}
function setHint(text){hint.innerText=text||'自动保存';}
function applyValue(v){el.value=typeof v==='string'?v:'';saveLocal(el.value);hostLoaded=true;setHint('已接入宿主同步');}
function invoke(method,args){if(window.yanm&&window.yanm.invoke){return window.yanm.invoke(method,args||{});}if(window.yanmHost&&method==='state.get'&&yanmHost.getState){return Promise.resolve(yanmHost.getState(args.key));}if(window.yanmHost&&method==='state.set'&&yanmHost.setState){return Promise.resolve(yanmHost.setState(args.key,args.value));}return Promise.reject(new Error('YANM_HOST_UNAVAILABLE'));}
function requestHost(){invoke('state.get',{key:key}).then(function(res){var value=res&&typeof res.value==='string'?res.value:'';if(value||!local()){applyValue(value||'');}else{hostLoaded=true;setHint('本机缓存优先');}}).catch(function(){setHint('本机缓存');});}
function saveHost(v){setHint('保存中...');invoke('state.set',{key:key,value:v}).then(function(){hostLoaded=true;setHint('已保存');}).catch(function(){setHint('本机缓存');});}
el.value=local();
window.addEventListener('yanm:message',function(e){var d=e.detail||{};if(d.type==='host.state'&&d.key===key&&Object.prototype.hasOwnProperty.call(d,'value')){var value=String(d.value||'');if(value||!local()){applyValue(value);}else{hostLoaded=true;setHint('本机缓存优先');}}});
if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',requestHost,{once:true});}else{requestHost();}setTimeout(requestHost,300);
el.addEventListener('input',function(){var v=el.value;saveLocal(v);clearTimeout(timer);timer=setTimeout(function(){saveHost(v);},350);if(!hostLoaded){setHint('准备同步');}});
})();
</script></body></html>
""";

    private static string CreatePomodoroHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:white;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:20px;border-radius:28px;background:radial-gradient(circle at 80% 10%,rgba(248,113,113,.46),transparent 35%),linear-gradient(135deg,rgba(82,22,38,.98),rgba(18,9,14,.94));border:1px solid rgba(254,202,202,.22);box-shadow:0 26px 82px rgba(0,0,0,.46),inset 0 1px 0 rgba(255,255,255,.1);text-align:center}
.tag{font-size:12px;color:#fca5a5;letter-spacing:.18em}.time{font-size:52px;font-weight:900;margin:18px 0 12px;font-variant-numeric:tabular-nums}.mode{font-size:13px;color:rgba(255,255,255,.68)}button{border:0;border-radius:14px;padding:9px 13px;margin:14px 4px 0;background:rgba(255,255,255,.13);color:white;font-weight:800;cursor:pointer}.primary{background:#fb7185;color:#2b0710}
</style></head><body><section class="card"><div class="tag">POMODORO</div><div id="time" class="time">25:00</div><div id="mode" class="mode">专注 25 分钟</div><button id="start" class="primary">开始</button><button id="reset">重置</button></section><script>
(function(){var total=25*60,left=total,timer=null,running=false;function fmt(s){return String(Math.floor(s/60)).padStart(2,'0')+':'+String(s%60).padStart(2,'0')}function render(){document.getElementById('time').innerText=fmt(left);document.getElementById('start').innerText=running?'暂停':'开始';}
function tick(){if(left>0){left--;render();return;}clearInterval(timer);timer=null;running=false;document.getElementById('mode').innerText='完成，休息一下';render();}
document.getElementById('start').onclick=function(){running=!running;if(running){timer=setInterval(tick,1000);}else{clearInterval(timer);timer=null;}render();};
document.getElementById('reset').onclick=function(){clearInterval(timer);timer=null;running=false;left=total;document.getElementById('mode').innerText='专注 25 分钟';render();};render();})();
</script></body></html>
""";

    private static string CreateCountdownHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:white;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:26px;background:radial-gradient(circle at 0% 0%,rgba(96,165,250,.46),transparent 36%),linear-gradient(135deg,rgba(24,50,88,.98),rgba(8,13,24,.94));border:1px solid rgba(191,219,254,.22);box-shadow:0 26px 82px rgba(0,0,0,.45),inset 0 1px 0 rgba(255,255,255,.1)}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;color:#93c5fd;letter-spacing:.18em}.time{font-size:42px;font-weight:900;margin:15px 0 10px;font-variant-numeric:tabular-nums}.row{display:flex;gap:8px}input{flex:1;min-width:0;border:0;outline:0;border-radius:14px;padding:10px;background:rgba(255,255,255,.1);color:white}button{border:0;border-radius:14px;padding:0 12px;background:#60a5fa;color:#061525;font-weight:900;cursor:pointer}.hint{font-size:12px;color:rgba(255,255,255,.62);line-height:1.6}
</style></head><body><section class="card"><div class="top"><div class="tag">COUNTDOWN</div><div class="hint">分钟</div></div><div id="time" class="time">10:00</div><div class="row"><input id="minutes" value="10" /><button id="start">开始</button><button id="reset">重置</button></div><div id="hint" class="hint">输入分钟后开始倒计时。</div></section><script>
(function(){var left=600,total=600,timer=null;function fmt(s){return String(Math.floor(s/60)).padStart(2,'0')+':'+String(s%60).padStart(2,'0')}function render(){document.getElementById('time').innerText=fmt(Math.max(0,left));}
function setFromInput(){var m=parseFloat(document.getElementById('minutes').value)||10;total=Math.max(1,Math.round(m*60));left=total;render();}
document.getElementById('start').onclick=function(){if(timer){clearInterval(timer);timer=null;this.innerText='开始';return;}if(left<=0)setFromInput();this.innerText='暂停';timer=setInterval(function(){left--;render();if(left<=0){clearInterval(timer);timer=null;document.getElementById('start').innerText='开始';document.getElementById('hint').innerText='倒计时结束';}},1000);};
document.getElementById('reset').onclick=function(){clearInterval(timer);timer=null;document.getElementById('start').innerText='开始';setFromInput();document.getElementById('hint').innerText='输入分钟后开始倒计时。';};setFromInput();})();
</script></body></html>
""";

    private static string CreateAppLauncherHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:26px;background:radial-gradient(circle at 100% 0%,rgba(96,165,250,.3),transparent 36%),linear-gradient(135deg,rgba(20,31,54,.98),rgba(10,14,24,.94));border:1px solid rgba(191,219,254,.22);box-shadow:0 26px 82px rgba(0,0,0,.45),inset 0 1px 0 rgba(255,255,255,.1);display:flex;flex-direction:column}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;letter-spacing:.18em;color:#93c5fd}.search{margin-top:12px}.search input{width:100%;box-sizing:border-box;border:0;outline:0;border-radius:14px;padding:10px 12px;background:rgba(255,255,255,.09);color:#fff}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin-top:14px;flex:1;min-height:120px;overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}.grid::-webkit-scrollbar{width:6px;height:6px}.grid::-webkit-scrollbar-track{background:transparent}.grid::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}.item{padding:10px 8px;border-radius:18px;background:rgba(255,255,255,.06);border:1px solid rgba(255,255,255,.05);cursor:pointer;text-align:center}.item:hover{background:rgba(255,255,255,.1)}.icon{width:44px;height:44px;border-radius:14px;margin:0 auto 8px;display:flex;align-items:center;justify-content:center;background:rgba(255,255,255,.08);overflow:hidden;font-size:16px;font-weight:800}.icon img{width:100%;height:100%;object-fit:cover}.title{font-size:12px;line-height:1.3;height:32px;overflow:hidden}.meta{font-size:11px;color:rgba(255,255,255,.52)}.empty{margin-top:18px;font-size:12px;color:rgba(255,255,255,.55)}
</style></head><body><section class="card"><div class="top"><div class="tag">APPLICATIONS</div><div class="meta" id="count">加载中</div></div><div class="search"><input id="queryInput" placeholder="筛选应用..." /></div><div id="grid" class="grid"></div><div id="empty" class="empty" style="display:none">没有匹配的应用。</div></section><script>
(function(){var all=[];var filtered=[];function glyph(title){return (title||'?').replace(/^\s+/,'').slice(0,1).toUpperCase();}
function render(){var grid=document.getElementById('grid');var empty=document.getElementById('empty');grid.innerHTML='';document.getElementById('count').innerText=(filtered.length||0)+' 个';empty.style.display=filtered.length?'none':'block';filtered.forEach(function(item){var card=document.createElement('div');card.className='item';var icon=item.iconDataUrl?'<img src=\"'+item.iconDataUrl+'\" alt=\"\">':glyph(item.title);card.innerHTML='<div class=\"icon\">'+icon+'</div><div class=\"title\">'+(item.title||'未命名应用')+'</div>';card.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('command.execute',{extensionId:item.extensionId,launchSource:'yanm-app-grid'});}};grid.appendChild(card);});}
function applyQuery(){var q=(document.getElementById('queryInput').value||'').toLowerCase();filtered=all.filter(function(item){if(!q)return true;var text=((item.title||'')+' '+(item.subtitle||'')+' '+(item.extensionId||'')).toLowerCase();return text.indexOf(q)>=0;});render();}
function load(){if(!(window.yanm&&window.yanm.invoke)){setTimeout(load,250);return;}window.yanm.invoke('command.list',{source:'application',limit:120}).then(function(res){all=(res&&res.items)||[];filtered=all.slice();render();}).catch(function(){all=[];filtered=[];render();});}
document.getElementById('queryInput').addEventListener('input',applyQuery);load();})();
</script></body></html>
""";

    private static string CreateDesktopFolderHtml() => """
<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><style>
html,body{margin:0;width:100%;height:100%;background:transparent;color:#fff;font-family:"Microsoft YaHei",sans-serif}
.card{box-sizing:border-box;height:100%;padding:18px;border-radius:24px;background:radial-gradient(circle at 100% 0%,rgba(14,165,233,.4),transparent 36%),linear-gradient(135deg,rgba(14,25,43,.98),rgba(9,14,24,.94));border:1px solid rgba(186,230,253,.22);box-shadow:0 26px 82px rgba(0,0,0,.45),inset 0 1px 0 rgba(255,255,255,.1);display:flex;flex-direction:column}
.top{display:flex;justify-content:space-between;align-items:center}.tag{font-size:12px;letter-spacing:.18em;color:#7dd3fc}.btn{border:0;border-radius:14px;padding:8px 12px;background:rgba(255,255,255,.1);color:#fff;cursor:pointer}
.path{margin-top:12px;padding:12px;border-radius:16px;background:rgba(255,255,255,.08);font-size:12px;color:rgba(255,255,255,.72);word-break:break-all;min-height:44px}
.list{margin-top:12px;flex:1;min-height:80px;overflow:auto;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.24) transparent}.list::-webkit-scrollbar{width:6px;height:6px}.list::-webkit-scrollbar-track{background:transparent}.list::-webkit-scrollbar-thumb{background:rgba(255,255,255,.18);border-radius:999px;border:1px solid rgba(255,255,255,.06)}
.item{display:flex;justify-content:space-between;gap:10px;padding:8px 0;border-top:1px solid rgba(255,255,255,.08);font-size:12px;cursor:pointer}.name{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.open{color:#93c5fd}.meta{font-size:11px;color:rgba(255,255,255,.52);margin-top:2px}
</style></head><body><section class="card"><div class="top"><div class="tag">DESKTOP</div><button class="btn" id="refreshBtn" type="button">刷新</button></div><div class="path" id="folderPath">正在读取桌面目录...</div><div id="list" class="list"></div></section><script>
(function(){var folder='';var items=[];function fmtTime(v){if(!v)return'';var d=new Date(v);if(isNaN(d.getTime()))return'';return d.toLocaleDateString('zh-CN',{month:'2-digit',day:'2-digit'})+' '+d.toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});}
function render(){document.getElementById('folderPath').innerText=folder||'未找到桌面目录';var list=document.getElementById('list');list.innerHTML='';for(var i=0;i<items.length&&i<12;i++){(function(item){var row=document.createElement('div');row.className='item';var left=document.createElement('div');left.className='name';left.innerHTML='<div>'+(item.name||(item.path||''))+'</div><div class=\"meta\">'+(item.isDirectory?'文件夹':'文件')+(item.modifiedTime?' · '+fmtTime(item.modifiedTime):'')+'</div>';var right=document.createElement('div');right.className='open';right.innerText=item.isDirectory?'打开':'查看';row.onclick=function(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('path.open',{path:item.path});}};row.appendChild(left);row.appendChild(right);list.appendChild(row);})(items[i]);}}
function refresh(){if(window.yanm&&window.yanm.invoke){window.yanm.invoke('desktop.list').then(function(res){folder=res&&res.root?res.root:'';items=(res&&res.items)||[];render();}).catch(function(){folder='';items=[];render();});}else{setTimeout(refresh,250);}}
document.getElementById('refreshBtn').onclick=refresh;refresh();})();
</script></body></html>
""";
}

public sealed class YarnSelectRuleSettings
{
    public bool Enabled { get; set; } = true;

    public string TriggerKey { get; set; } = string.Empty;

    public string ActionType { get; set; } = YarnSelectActionTypes.Copy;

    public string ExtensionId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

public static class YarnSelectActionTypes
{
    public const string Copy = "copy";
    public const string Cut = "cut";
    public const string Paste = "paste";
    public const string Search = "search";
    public const string Run = "run";
    public const string SmartCopyPaste = "smart_copy_paste";
    public const string RunExtension = "run_extension";

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            Cut => Cut,
            Paste => Paste,
            Search => Search,
            Run => Run,
            SmartCopyPaste => SmartCopyPaste,
            RunExtension => RunExtension,
            _ => Copy
        };
    }
}

public static class YanyuActionTypes
{
    public const string PasteText = "paste_text";
    public const string RunExtension = "run_extension";

    public static string Normalize(string? value)
    {
        return string.Equals(value, RunExtension, StringComparison.OrdinalIgnoreCase)
            ? RunExtension
            : PasteText;
    }
}

public static class YanyuTriggerSuffix
{
    public const string Space = "space";
    public const string Tab = "tab";
    public const string Enter = "enter";

    public static string Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Space;
        }

        var lowered = trimmed.ToLowerInvariant();
        return lowered switch
        {
            Space => Space,
            Tab => Tab,
            Enter => Enter,
            _ when trimmed.Length == 1 => trimmed,
            _ => Space
        };
    }

    public static string ToDisplayText(string? value)
    {
        return Normalize(value) switch
        {
            Space => "空格",
            Tab => "Tab",
            Enter => "Enter",
            var custom => custom
        };
    }
}

public sealed class YanyuRuleSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    public string TriggerText { get; set; } = string.Empty;

    public string TriggerSuffix { get; set; } = YanyuTriggerSuffix.Space;

    public bool UseRegex { get; set; }

    public string BoundProcessName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ActionType { get; set; } = YanyuActionTypes.PasteText;

    public string TextContent { get; set; } = string.Empty;

    public string ExtensionId { get; set; } = string.Empty;
}

public sealed class WindowBindingSettings
{
    public bool Enabled { get; set; } = true;

    public int MarginPixels { get; set; } = 14;

    public List<WindowBindingRuleSettings> Rules { get; set; } = [];
}

public sealed class WindowBindingRuleSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    public string ExtensionId { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public string WindowClass { get; set; } = string.Empty;

    public string TitleContains { get; set; } = string.Empty;

    public string Corner { get; set; } = WindowBindingCorners.TopLeft;

    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    /// <summary>
    /// When true, the overlay is hidden by default and only appears when the cursor enters the detection zone.
    /// </summary>
    public bool HoverMode { get; set; } = false;
}

public static class WindowBindingCorners
{
    public const string TopLeft = "top_left";
    public const string TopRight = "top_right";
    public const string BottomLeft = "bottom_left";
    public const string BottomRight = "bottom_right";

    // Interior positions (inside the target window)
    public const string InsideTopLeft = "inside_top_left";
    public const string InsideTopRight = "inside_top_right";
    public const string InsideBottomLeft = "inside_bottom_left";
    public const string InsideBottomRight = "inside_bottom_right";

    public static bool IsInterior(string? corner)
    {
        var normalized = Normalize(corner);
        return normalized.StartsWith("inside_", StringComparison.Ordinal);
    }

    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            TopRight => TopRight,
            BottomLeft => BottomLeft,
            BottomRight => BottomRight,
            InsideTopLeft => InsideTopLeft,
            InsideTopRight => InsideTopRight,
            InsideBottomLeft => InsideBottomLeft,
            InsideBottomRight => InsideBottomRight,
            _ => TopLeft
        };
    }
}

public sealed class AiServiceProviderSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public List<string> Models { get; set; } = [];
    public string SelectedModel { get; set; } = string.Empty;
}
