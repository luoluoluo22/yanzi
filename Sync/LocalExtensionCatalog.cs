using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OpenQuickHost.Sync;

public static class LocalExtensionCatalog
{
    public static string CatalogRootPath => ExtensionPackageService.ExtensionsRootPath;

    public static void EnsureSampleExtension()
    {
        Directory.CreateDirectory(CatalogRootPath);
        EnsureSampleNotesExtension();
        EnsureSampleTranslateExtension();
        EnsureClipboardScriptExtension();
        EnsureForegroundWindowScriptExtension();
        EnsureInlineClipboardExtension();
        EnsureInlineTimestampExtension();
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

                commands.Add(new CommandItem(
                    glyph: string.IsNullOrWhiteSpace(manifest.Runtime) && manifest.Script == null ? "E" : "S",
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
                    hostedView: manifest.HostedView?.ToDefinition(),
                    globalShortcut: manifest.GlobalShortcut,
                    hotkeyBehavior: manifest.HotkeyBehavior,
                    runtime: manifest.Runtime,
                    entryPoint: manifest.Entry,
                    permissions: manifest.Permissions ?? [],
                    entryMode: manifest.EntryMode,
                    inlineScriptSource: manifest.Script?.Source));
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
            GlobalShortcut = "Ctrl+Alt+N"
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                OpenTarget = existing.OpenTarget ?? manifest.OpenTarget,
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut
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
                HostedView = existing.HostedView ?? manifest.HostedView
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
            Permissions = ["clipboard.read"]
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Runtime = existing.Runtime ?? manifest.Runtime,
                Entry = existing.Entry ?? manifest.Entry,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions
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
            Permissions = ["window.foreground"]
        };
        EnsureSampleManifest(Path.Combine(extensionDirectory, "manifest.json"), manifest, existing =>
            existing with
            {
                GlobalShortcut = string.IsNullOrWhiteSpace(existing.GlobalShortcut) ? manifest.GlobalShortcut : existing.GlobalShortcut,
                Runtime = existing.Runtime ?? manifest.Runtime,
                Entry = existing.Entry ?? manifest.Entry,
                Permissions = existing.Permissions is { Length: > 0 } ? existing.Permissions : manifest.Permissions
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
                Script = existing.Script ?? manifest.Script
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
                Script = existing.Script ?? manifest.Script
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
            glyph: string.IsNullOrWhiteSpace(manifest.Runtime) && manifest.Script == null ? "J" : "S",
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
            hostedView: manifest.HostedView?.ToDefinition(),
            globalShortcut: manifest.GlobalShortcut,
            hotkeyBehavior: manifest.HotkeyBehavior,
            runtime: manifest.Runtime,
            entryPoint: manifest.Entry,
            permissions: manifest.Permissions ?? [],
            entryMode: manifest.EntryMode,
            inlineScriptSource: manifest.Script?.Source);
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
            Id = "my-json-extension",
            Name = "我的 JSON 扩展",
            Version = "0.1.0",
            Category = "扩展",
            Description = "示例：打开本地文档或目录。",
            Keywords = ["json", "extension"],
            OpenTarget = HostAssets.DocsReadmePath
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
            if (!string.Equals(manifest.Runtime, "powershell", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("当前内联脚本只支持 runtime = powershell。");
            }

            if (string.IsNullOrWhiteSpace(manifest.Script?.Source))
            {
                throw new InvalidOperationException("entryMode = inline 时必须提供 script.source。");
            }
        }

        return manifest;
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

    public LocalExtensionHostedViewManifest? HostedView { get; init; }

    public string? GlobalShortcut { get; init; }

    public string? HotkeyBehavior { get; init; }

    public string? Runtime { get; init; }

    public string? EntryMode { get; init; }

    public string? Entry { get; init; }

    public string[]? Permissions { get; init; }

    public LocalExtensionInlineScriptManifest? Script { get; init; }
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
            EmptyState);
    }
}
