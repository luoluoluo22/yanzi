using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class LocalExtensionCatalog
{
    private static readonly HashSet<string> HiddenBuiltInSampleIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "sample-notes",
        "sample-translate",
        "script-clipboard",
        "script-foreground-window",
        "inline-clipboard",
        "inline-timestamp",
        "selection-context-demo",
        "csharp-context-demo"
    };

    public static string CatalogRootPath => ExtensionPackageService.ExtensionsRootPath;

    public static void EnsureSampleExtension()
    {
        Directory.CreateDirectory(CatalogRootPath);
        EnsureDefaultWebSearchExtensions();
    }

    private static void EnsureDefaultWebSearchExtensions()
    {
        if (Directory.EnumerateFiles(CatalogRootPath, "manifest.json", SearchOption.AllDirectories)
            .Any(path => Path.GetFileName(Path.GetDirectoryName(path))?.StartsWith("web-search-", StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        EnsureWebSearchExtension("web-search-baidu", "百度搜索", "百度网页搜索。", "https://www.baidu.com/s?wd={query}", ["百度", "baidu", "bd"], "https://www.baidu.com/favicon.ico");
        EnsureWebSearchExtension("web-search-bing", "Bing 搜索", "Bing 网页搜索。", "https://www.bing.com/search?q={query}", ["Bing", "必应", "bing"], "https://www.bing.com/favicon.ico");
        EnsureWebSearchExtension("web-search-google", "谷歌搜索", "Google 网页搜索。", "https://www.google.com/search?q={query}", ["谷歌", "Google", "google", "gg", "guge"], "https://www.google.com/favicon.ico");
    }

    private static void EnsureWebSearchExtension(string id, string name, string description, string targetTemplate, string[] prefixes, string icon)
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, id);
        Directory.CreateDirectory(extensionDirectory);
        var manifest = new LocalExtensionManifest
        {
            Id = id,
            Name = name,
            Version = "1.0.0",
            Category = "网页搜索",
            Description = description,
            Keywords = prefixes.Concat(["网页", "搜索"]).ToArray(),
            QueryPrefixes = prefixes,
            QueryTargetTemplate = targetTemplate,
            Icon = icon
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing => existing);
    }

    public static IReadOnlyList<CommandItem> LoadCommands()
    {
        if (!Directory.Exists(CatalogRootPath))
        {
            return [];
        }

        var commands = new List<CommandItem>();
        foreach (var manifestPath in Directory.EnumerateFiles(CatalogRootPath, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(json, JsonOptions);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
                {
                    continue;
                }

                if (HiddenBuiltInSampleIds.Contains(manifest.Id))
                {
                    continue;
                }

                commands.Add(new CommandItem(
                    glyph: GetDefaultGlyph(manifest, "E"),
                    title: manifest.Name,
                    subtitle: manifest.Description ?? $"来自本地扩展目录：{Path.GetDirectoryName(manifestPath)}",
                    category: manifest.Category ?? "扩展",
                    accentHex: "#FF38BDF8",
                    openTarget: manifest.OpenTarget,
                    keywords: manifest.Keywords ?? [],
                    source: CommandSource.LocalExtension,
                    extensionId: manifest.Id,
                    declaredVersion: manifest.Version ?? "0.1.0",
                    extensionDirectoryPath: Path.GetDirectoryName(manifestPath),
                    hostedView: manifest.HostedViewXaml?.ToDefinition() ?? manifest.HostedViewV2?.ToDefinition() ?? manifest.HostedView?.ToDefinition(),
                    globalShortcut: manifest.GlobalShortcut,
                    hotkeyBehavior: manifest.HotkeyBehavior,
                    runtime: manifest.Runtime,
                    entryPoint: manifest.Entry,
                    permissions: manifest.Permissions ?? [],
                    entryMode: manifest.EntryMode,
                    inlineScriptSource: manifest.Script?.Source,
                    iconReference: manifest.Icon,
                    queryPrefixes: manifest.QueryPrefixes,
                    queryTargetTemplate: manifest.QueryTargetTemplate,
                    startup: manifest.Startup?.ToDefinition()));
            }
            catch
            {
                // Skip invalid manifests so one broken extension does not block the host.
            }
        }

        return commands;
    }

    private static void EnsureSampleNotesExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "sample-notes");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "sample-notes",
            Name = "快速便签",
            Version = "0.1.0",
            Category = "扩展",
            Description = "示例扩展：打开桌面扩展目录里的便签说明文件。",
            Keywords = ["note", "memo", "sample", "extension"],
            OpenTarget = Path.Combine(extensionDirectory, "README.txt"),
            GlobalShortcut = "Ctrl+Alt+N",
            Icon = "mdi:note"
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                OpenTarget = existing.OpenTarget ?? manifest.OpenTarget,
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
        File.WriteAllText(
            Path.Combine(extensionDirectory, "README.txt"),
            "这是一个本地示例扩展。把 manifest.json 改掉后，宿主会在下次启动时重新扫描并自动尝试云同步。");
    }

    private static void EnsureSampleTranslateExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "sample-translate");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "sample-translate",
            Name = "双栏翻译",
            Version = "0.1.0",
            Category = "扩展",
            Description = "示例脚本扩展：在当前窗口中打开双栏翻译工作区。",
            Keywords = ["translate", "translator", "翻译", "双栏", "script"],
            GlobalShortcut = "Ctrl+Alt+T",
            Runtime = "powershell",
            Entry = "main.ps1",
            Permissions = ["clipboard", "network"],
            Icon = "mdi:translate",
            HostedView = new LocalExtensionHostedViewManifest
            {
                Type = "split-workbench",
                Title = "双栏翻译",
                Description = "左侧输入待翻译内容，右侧显示脚本输出。",
                InputLabel = "原文",
                InputPlaceholder = "输入要翻译的中文、英文或任意文本...",
                OutputLabel = "译文",
                ActionButtonText = "开始翻译",
                ActionType = "script",
                EmptyState = "这里会显示 PowerShell 脚本的执行结果。"
            }
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Runtime = existing.Runtime ?? manifest.Runtime,
                Entry = existing.Entry ?? manifest.Entry,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                HostedView = existing.HostedView ?? manifest.HostedView,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
        WritePowerShellScript(
            Path.Combine(extensionDirectory, "main.ps1"),
            """
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrWhiteSpace($InputText)) {
    Write-Output "请输入要翻译的文本。"
    exit 0
}

$trimmed = $InputText.Trim()
Write-Output "译文：$trimmed"
Write-Output ""
Write-Output "说明：这是示例脚本输出。后续可以替换为真实翻译 API 调用。"
""");
    }

    private static void EnsureClipboardScriptExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "script-clipboard");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "script-clipboard",
            Name = "读取剪贴板",
            Version = "0.1.0",
            Category = "脚本",
            Description = "PowerShell 示例：直接读取当前剪贴板文本。",
            Keywords = ["clipboard", "剪贴板", "powershell", "script"],
            GlobalShortcut = "Ctrl+Alt+C",
            Runtime = "powershell",
            Entry = "main.ps1",
            Permissions = ["clipboard.read"],
            Icon = "mdi:clipboard"
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Runtime = existing.Runtime ?? manifest.Runtime,
                Entry = existing.Entry ?? manifest.Entry,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
        WritePowerShellScript(
            Path.Combine(extensionDirectory, "main.ps1"),
            """
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName PresentationCore
$text = Get-Clipboard -Raw
if ([string]::IsNullOrWhiteSpace($text)) {
    Write-Output "当前剪贴板为空。"
} else {
    Write-Output $text.Trim()
}
""");
    }

    private static void EnsureForegroundWindowScriptExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "script-foreground-window");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "script-foreground-window",
            Name = "前台窗口信息",
            Version = "0.1.0",
            Category = "脚本",
            Description = "PowerShell 示例：获取当前前台窗口标题和进程。",
            Keywords = ["window", "foreground", "前台窗口", "powershell", "script"],
            GlobalShortcut = "Ctrl+Alt+W",
            Runtime = "powershell",
            Entry = "main.ps1",
            Permissions = ["window.foreground"],
            Icon = "mdi:window"
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Runtime = existing.Runtime ?? manifest.Runtime,
                Entry = existing.Entry ?? manifest.Entry,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
        WritePowerShellScript(
            Path.Combine(extensionDirectory, "main.ps1"),
            """
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Window {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
"@

$handle = [Win32Window]::GetForegroundWindow()
$titleBuilder = New-Object System.Text.StringBuilder 512
[void][Win32Window]::GetWindowText($handle, $titleBuilder, $titleBuilder.Capacity)
[uint32]$processId = 0
[void][Win32Window]::GetWindowThreadProcessId($handle, [ref]$processId)
$process = Get-Process -Id $processId -ErrorAction SilentlyContinue

Write-Output ("窗口标题: " + $titleBuilder.ToString().Trim())
Write-Output ("进程名: " + $(if ($process) { $process.ProcessName } else { "unknown" }))
Write-Output ("进程 ID: " + $processId)
""");
    }

    private static void EnsureInlineClipboardExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "inline-clipboard");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "inline-clipboard",
            Name = "内联读取剪贴板",
            Version = "0.1.0",
            Category = "脚本",
            Description = "单 JSON 内联 PowerShell 示例：读取当前剪贴板文本。",
            Keywords = ["clipboard", "剪贴板", "inline", "powershell", "json"],
            GlobalShortcut = "Ctrl+Alt+Shift+C",
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.read"],
            Icon = "mdi:clipboard",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$text = Get-Clipboard -Raw
if ([string]::IsNullOrWhiteSpace($text)) {
    Write-Output "当前剪贴板为空。"
} else {
    Write-Output $text.Trim()
}
"""
            }
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Runtime = existing.Runtime ?? manifest.Runtime,
                EntryMode = existing.EntryMode ?? manifest.EntryMode,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                Script = existing.Script ?? manifest.Script,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
    }

    private static void EnsureInlineTimestampExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "inline-timestamp");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "inline-timestamp",
            Name = "内联时间戳",
            Version = "0.1.0",
            Category = "脚本",
            Description = "单 JSON 内联 PowerShell 示例：返回当前时间和输入内容。",
            Keywords = ["time", "timestamp", "时间戳", "inline", "powershell"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.read"],
            Icon = "mdi:clock",
            HostedView = new LocalExtensionHostedViewManifest
            {
                Type = "split-workbench",
                Title = "内联时间戳",
                Description = "左侧输入任意文本，右侧显示时间戳和输入内容。",
                InputLabel = "输入",
                InputPlaceholder = "输入任意内容...",
                OutputLabel = "结果",
                ActionButtonText = "执行脚本",
                ActionType = "script",
                EmptyState = "脚本输出会显示在这里。"
            },
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$now = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
if ([string]::IsNullOrWhiteSpace($InputText)) {
    Write-Output "当前时间: $now"
} else {
    Write-Output "当前时间: $now"
    Write-Output "输入内容: $InputText"
}
"""
            }
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                Runtime = existing.Runtime ?? manifest.Runtime,
                EntryMode = existing.EntryMode ?? manifest.EntryMode,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                HostedView = existing.HostedView ?? manifest.HostedView,
                Script = existing.Script ?? manifest.Script,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
    }

    private static void EnsureSelectionContextExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "selection-context-demo");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "selection-context-demo",
            Name = "选中内容示例",
            Version = "0.1.0",
            Category = "脚本",
            Description = "示例：优先读取宿主传入的 InputText，没有时回退到剪贴板文本或文件列表。",
            Keywords = ["selection", "context", "clipboard", "选中", "右键", "面板"],
            Runtime = "powershell",
            EntryMode = "inline",
            Permissions = ["clipboard.read"],
            Icon = "app:selection",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
param(
    [string]$InputText = "",
    [string]$ContextPath = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms

$source = "HostInput"
$normalized = $InputText
$fileList = @()

if ([string]::IsNullOrWhiteSpace($normalized)) {
    if ([System.Windows.Forms.Clipboard]::ContainsFileDropList()) {
        $fileList = [System.Windows.Forms.Clipboard]::GetFileDropList()
        $normalized = ($fileList | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        $source = "ClipboardFileDropList"
    }
    elseif ([System.Windows.Forms.Clipboard]::ContainsText()) {
        $normalized = [System.Windows.Forms.Clipboard]::GetText()
        $source = "ClipboardText"
    }
}

if ([string]::IsNullOrWhiteSpace($normalized)) {
    Write-Output "没有检测到宿主输入，也没有检测到剪贴板里的文本/文件。"
    Write-Output ""
    Write-Output "后续如果宿主在长按面板前自动抓取选中内容，这个扩展会直接收到 InputText。"
    exit 0
}

Write-Output "来源: $source"
Write-Output ""

if ($fileList.Count -gt 0) {
    Write-Output "识别为文件选择，共 $($fileList.Count) 个："
    Write-Output ""
    foreach ($file in $fileList) {
        Write-Output $file
    }
    exit 0
}

$trimmed = $normalized.Trim()
Write-Output "识别为文本输入："
Write-Output ""
Write-Output $trimmed
"""
            }
        };

        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                Runtime = existing.Runtime ?? manifest.Runtime,
                EntryMode = existing.EntryMode ?? manifest.EntryMode,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                Script = existing.Script ?? manifest.Script,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
    }

    private static void EnsureCSharpInlineExtension()
    {
        var extensionDirectory = Path.Combine(CatalogRootPath, "csharp-context-demo");
        Directory.CreateDirectory(extensionDirectory);

        var manifest = new LocalExtensionManifest
        {
            Id = "csharp-context-demo",
            Name = "C# 动作示例",
            Version = "0.1.0",
            Category = "C#",
            Description = "示例：使用 C# 读取宿主传入的上下文并返回结果。",
            Keywords = ["csharp", "dotnet", "context", "示例"],
            Runtime = "csharp",
            EntryMode = "inline",
            Permissions = ["context.read"],
            Icon = "mdi:code",
            Script = new LocalExtensionInlineScriptManifest
            {
                Source =
"""
using OpenQuickHost.CSharpRuntime;

public static class YanziAction
{
    public static Task<string> RunAsync(YanziActionContext context)
    {
        var input = string.IsNullOrWhiteSpace(context.InputText)
            ? "没有收到选中内容。"
            : context.InputText.Trim();

        return Task.FromResult(
            $"来源: {context.LaunchSource}" + Environment.NewLine +
            $"扩展目录: {context.ExtensionDirectory}" + Environment.NewLine +
            Environment.NewLine +
            "输入:" + Environment.NewLine +
            input);
    }
}
"""
            }
        };

        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                Runtime = existing.Runtime ?? manifest.Runtime,
                EntryMode = existing.EntryMode ?? manifest.EntryMode,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions,
                Script = existing.Script ?? manifest.Script,
                Icon = string.IsNullOrWhiteSpace(existing.Icon) ? manifest.Icon : existing.Icon
            });
    }

    public static CommandItem SaveJsonExtension(string json)
    {
        Directory.CreateDirectory(CatalogRootPath);

        var manifest = ParseManifest(json);

        var extensionDirectory = Path.Combine(CatalogRootPath, manifest.Id);
        Directory.CreateDirectory(extensionDirectory);
        var manifestPath = Path.Combine(extensionDirectory, "manifest.json");
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new CommandItem(
            glyph: GetDefaultGlyph(manifest, "J"),
            title: manifest.Name,
            subtitle: manifest.Description ?? $"来自本地扩展目录：{extensionDirectory}",
            category: manifest.Category ?? "扩展",
            accentHex: "#FF22C55E",
            openTarget: manifest.OpenTarget,
            keywords: manifest.Keywords ?? [],
            source: CommandSource.LocalExtension,
            extensionId: manifest.Id,
            declaredVersion: manifest.Version ?? "0.1.0",
            extensionDirectoryPath: extensionDirectory,
            hostedView: manifest.HostedViewXaml?.ToDefinition() ?? manifest.HostedViewV2?.ToDefinition() ?? manifest.HostedView?.ToDefinition(),
            globalShortcut: manifest.GlobalShortcut,
            hotkeyBehavior: manifest.HotkeyBehavior,
            runtime: manifest.Runtime,
            entryPoint: manifest.Entry,
            permissions: manifest.Permissions ?? [],
            entryMode: manifest.EntryMode,
            inlineScriptSource: manifest.Script?.Source,
            iconReference: manifest.Icon,
            queryPrefixes: manifest.QueryPrefixes,
            queryTargetTemplate: manifest.QueryTargetTemplate,
            startup: manifest.Startup?.ToDefinition());
    }

    public static string LoadManifestJson(string extensionId)
    {
        var manifestPath = GetManifestPath(extensionId);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("没有找到对应扩展的 manifest.json。", manifestPath);
        }

        return File.ReadAllText(manifestPath);
    }

    public static void DeleteExtension(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        var extensionDirectory = Path.Combine(CatalogRootPath, extensionId);
        if (!Directory.Exists(extensionDirectory))
        {
            throw new DirectoryNotFoundException("没有找到对应扩展目录。");
        }

        Directory.Delete(extensionDirectory, true);
    }

    public static CommandItem RenameExtension(string extensionId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new InvalidOperationException("扩展名称不能为空。");
        }

        var manifestPath = GetManifestPath(extensionId);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("没有找到对应扩展的 manifest.json。", manifestPath);
        }

        var manifest = ParseManifest(File.ReadAllText(manifestPath));
        var renamed = new LocalExtensionManifest
        {
            Id = manifest.Id,
            Name = newName.Trim(),
            Version = manifest.Version,
            Category = manifest.Category,
            Description = manifest.Description,
            Keywords = manifest.Keywords,
            OpenTarget = manifest.OpenTarget,
            Icon = manifest.Icon,
            HostedView = manifest.HostedView,
            GlobalShortcut = manifest.GlobalShortcut,
            HotkeyBehavior = manifest.HotkeyBehavior,
            Runtime = manifest.Runtime,
            EntryMode = manifest.EntryMode,
            Entry = manifest.Entry,
            Permissions = manifest.Permissions,
            Script = manifest.Script
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(renamed, JsonOptions));
        return SaveJsonExtension(JsonSerializer.Serialize(renamed, JsonOptions));
    }

    public static CommandItem SetGlobalShortcut(string extensionId, string? globalShortcut)
    {
        var manifestPath = GetManifestPath(extensionId);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("没有找到对应扩展的 manifest.json。", manifestPath);
        }

        var manifest = ParseManifest(File.ReadAllText(manifestPath));
        var updated = manifest with
        {
            GlobalShortcut = string.IsNullOrWhiteSpace(globalShortcut) ? null : globalShortcut.Trim()
        };

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(updated, JsonOptions));
        return SaveJsonExtension(JsonSerializer.Serialize(updated, JsonOptions));
    }

    public static string CreateTemplateJson()
    {
        var manifest = new LocalExtensionManifest
        {
            Id = "open-desktop-template",
            Name = "打开桌面",
            Version = "0.1.0",
            Category = "扩展",
            Description = "示例：打开当前用户桌面目录。",
            Keywords = ["桌面", "desktop", "打开"],
            OpenTarget = "shell:Desktop",
            Icon = "mdi:monitor-dashboard"
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private static LocalExtensionManifest ParseManifest(string json)
    {
        var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(json, JsonOptions);
        if (manifest == null)
        {
            throw new InvalidOperationException("JSON 解析失败。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidOperationException("扩展必须包含 id。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException("扩展必须包含 name。");
        }

        if (string.Equals(manifest.EntryMode, "inline", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsSupportedInlineRuntime(manifest.Runtime))
            {
                throw new InvalidOperationException("当前内联动作只支持 runtime = csharp 或 powershell。");
            }

            if (string.IsNullOrWhiteSpace(manifest.Script?.Source))
            {
                throw new InvalidOperationException("entryMode = inline 时必须提供 script.source。");
            }
        }

        return manifest;
    }

    private static bool IsSupportedInlineRuntime(string? runtime)
    {
        return string.Equals(runtime, "powershell", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(runtime, "ps1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(runtime, "csharp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(runtime, "cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(runtime, "c#", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultGlyph(LocalExtensionManifest manifest, string staticGlyph)
    {
        if (string.IsNullOrWhiteSpace(manifest.Runtime) && manifest.Script == null)
        {
            return staticGlyph;
        }

        return string.Equals(manifest.Runtime, "csharp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(manifest.Runtime, "cs", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(manifest.Runtime, "c#", StringComparison.OrdinalIgnoreCase)
            ? "C"
            : "S";
    }

    private static string GetManifestPath(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            throw new InvalidOperationException("扩展 ID 不能为空。");
        }

        return Path.Combine(CatalogRootPath, extensionId, "manifest.json");
    }

    private static void TryBackfillSampleShortcut(string manifestPath, string shortcut)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<LocalExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest == null || !string.IsNullOrWhiteSpace(manifest.GlobalShortcut))
            {
                return;
            }

            var updated = manifest with { GlobalShortcut = shortcut };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(updated, JsonOptions));
        }
        catch
        {
            // Ignore sample manifest migrations when local files were manually customized.
        }
    }

    private static void EnsureSampleManifest(
        string manifestPath,
        LocalExtensionManifest defaultManifest,
        Func<LocalExtensionManifest, LocalExtensionManifest> upgrade)
    {
        if (!File.Exists(manifestPath))
        {
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(defaultManifest, JsonOptions));
            return;
        }

        try
        {
            var existing = JsonSerializer.Deserialize<LocalExtensionManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (existing == null)
            {
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(defaultManifest, JsonOptions));
                return;
            }

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(upgrade(existing), JsonOptions));
        }
        catch
        {
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(defaultManifest, JsonOptions));
        }
    }

    private static void WritePowerShellScript(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

public sealed record LocalExtensionManifest
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = "0.1.0";

    public string? Category { get; init; }

    public string? Description { get; init; }

    public string[]? Keywords { get; init; }

    public string? OpenTarget { get; init; }

    public string[]? QueryPrefixes { get; init; }

    public string? QueryTargetTemplate { get; init; }

    public string? Icon { get; init; }

    public LocalExtensionHostedViewManifest? HostedView { get; init; }

    public LocalExtensionHostedViewV2Manifest? HostedViewV2 { get; init; }

    public LocalExtensionHostedViewXamlManifest? HostedViewXaml { get; init; }

    public string? GlobalShortcut { get; init; }

    public string? HotkeyBehavior { get; init; }

    public string? Runtime { get; init; }

    public string? EntryMode { get; init; }

    public string? Entry { get; init; }

    public string[]? Permissions { get; init; }

    public LocalExtensionInlineScriptManifest? Script { get; init; }

    public LocalExtensionStartupManifest? Startup { get; init; }
}

public sealed record LocalExtensionStartupManifest
{
    /// <summary>
    /// 启动模式：null (不自动启动), "on_app_launch" (软件启动时启动)
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>
    /// 定时计划（可选）：cron 表达式或简单的间隔描述
    /// </summary>
    public string? Schedule { get; init; }

    public ExtensionStartupDefinition ToDefinition()
    {
        return new ExtensionStartupDefinition(Mode, Schedule);
    }
}

public sealed class LocalExtensionInlineScriptManifest
{
    public string? Source { get; init; }
}

public sealed class LocalExtensionHostedViewManifest
{
    public string Type { get; init; } = "split-workbench";

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? InputLabel { get; init; }

    public string? InputPlaceholder { get; init; }

    public string? OutputLabel { get; init; }

    public string? ActionButtonText { get; init; }

    public string? ActionType { get; init; }

    public string? OutputTemplate { get; init; }

    public string? EmptyState { get; init; }

    public double? WindowWidth { get; init; }

    public double? WindowHeight { get; init; }

    public double? MinWindowWidth { get; init; }

    public double? MinWindowHeight { get; init; }

    public HostedPluginViewDefinition ToDefinition()
    {
        return new HostedPluginViewDefinition(
            Type,
            Title,
            Description,
            InputLabel,
            InputPlaceholder,
            OutputLabel,
            ActionButtonText,
            ActionType,
            OutputTemplate,
            EmptyState,
            WindowWidth,
            WindowHeight,
            MinWindowWidth,
            MinWindowHeight,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            []);
    }
}

public sealed class LocalExtensionHostedViewV2Manifest
{
    public string Type { get; init; } = "single-pane";

    public string? Title { get; init; }

    public string? Description { get; init; }

    public LocalExtensionHostedViewWindowManifest? Window { get; init; }

    public Dictionary<string, string>? State { get; init; }

    public LocalExtensionHostedViewV2ComponentManifest[]? Components { get; init; }

    public HostedPluginViewDefinition ToDefinition()
    {
        var window = Window;
        return new HostedPluginViewDefinition(
            Type,
            Title,
            Description,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            window?.Width,
            window?.Height,
            window?.MinWidth,
            window?.MinHeight,
            null,
            new Dictionary<string, string>(State ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            Components?.Select(component => component.ToDefinition()).ToArray() ?? []);
    }
}

public sealed class LocalExtensionHostedViewXamlManifest
{
    public string Type { get; init; } = "xaml";

    public string? Title { get; init; }

    public string? Description { get; init; }

    public LocalExtensionHostedViewWindowManifest? Window { get; init; }

    public Dictionary<string, string>? State { get; init; }

    public string Xaml { get; init; } = string.Empty;

    public HostedPluginViewDefinition ToDefinition()
    {
        var window = Window;
        return new HostedPluginViewDefinition(
            Type,
            Title,
            Description,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            window?.Width,
            window?.Height,
            window?.MinWidth,
            window?.MinHeight,
            Xaml,
            new Dictionary<string, string>(State ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            []);
    }
}

public sealed class LocalExtensionHostedViewWindowManifest
{
    public double? Width { get; init; }

    public double? Height { get; init; }

    public double? MinWidth { get; init; }

    public double? MinHeight { get; init; }
}

public sealed class LocalExtensionHostedViewV2ComponentManifest
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Type { get; init; } = "text";

    public string? Label { get; init; }

    public string? Text { get; init; }

    public string? Bind { get; init; }

    public string? Placeholder { get; init; }

    public string? Region { get; init; }

    public LocalExtensionHostedViewV2ActionManifest[]? Actions { get; init; }

    public HostedViewComponentDefinition ToDefinition()
    {
        return new HostedViewComponentDefinition(
            Id,
            Type,
            Label,
            Text,
            Bind,
            Placeholder,
            Region,
            Actions?.Select(action => action.ToDefinition()).ToArray() ?? []);
    }
}

public sealed class LocalExtensionHostedViewV2ActionManifest
{
    public string Type { get; init; } = "setState";

    public string? Path { get; init; }

    public string? Value { get; init; }

    public string? ValueFrom { get; init; }

    public string? InputFrom { get; init; }

    public string? OutputTo { get; init; }

    public string? SuccessMessage { get; init; }

    public bool Append { get; init; }

    public string? Separator { get; init; }

    public string? Key { get; init; }

    public string? Scope { get; init; }

    public string? DefaultValue { get; init; }

    public HostedViewActionDefinition ToDefinition()
    {
        return new HostedViewActionDefinition(
            Type,
            Path,
            Value,
            ValueFrom,
            InputFrom,
            OutputTo,
            SuccessMessage,
            Append,
            Separator,
            Key,
            Scope,
            DefaultValue);
    }
}
