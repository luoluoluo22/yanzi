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
    private static long _writeVersion;
    internal static long WriteVersion => System.Threading.Interlocked.Read(ref _writeVersion);

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
                var defaults = Normalize(new AppSettings
                {
                    HasShownInitialWelcomePanel = false
                });
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

    public static AppSettings Update(Func<AppSettings, AppSettings> edit)
    {
        lock (SettingsIoLock)
        {
            var settings = Normalize(edit(Load()));
            Save(settings);
            return settings;
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
        System.Threading.Interlocked.Increment(ref _writeVersion);
        UpdateCache(settings);
    }

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.CamelCaseIndented;

    internal static int NormalizeLongPressMilliseconds(int milliseconds) => AppSettingsMigration.NormalizeLongPressMilliseconds(milliseconds);
    private static AppSettings Normalize(AppSettings settings) => AppSettingsMigration.Normalize(settings);
}
