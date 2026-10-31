using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class MainWindow
{
    // Entries are indexed by invocation name, because the unified capability API also
    // dispatches by name. Registered providers take precedence over dormant declarations.
    private sealed record CapabilityCatalogEntry(
        YanziAgentCapabilityDescriptor Descriptor,
        CommandItem? ProviderCommand)
    {
        public bool IsHost => Descriptor.ProviderExtensionId is "yanzi-host" or "host";
        public string ProviderTitle => IsHost ? "燕子宿主" : ProviderCommand?.Title ?? Descriptor.ProviderExtensionId;
        public string Status => Descriptor.Available ? "已就绪" : Descriptor.OnDemand ? "待启动" : "未注册";
        public string? Directory => ProviderCommand?.ExtensionDirectoryPath;
        public string? ManifestPath => Directory is null ? null : Path.Combine(Directory, "manifest.json");
    }

    private static readonly object CapabilityCatalogSync = new();
    private static DateTime _lastCapabilityManifestScanUtc;
    private static string _cachedCapabilityFingerprint = "";
    private static IReadOnlyList<CommandItem> _cachedCapabilityCommands = Array.Empty<CommandItem>();
    private static IReadOnlyDictionary<string, CapabilityCatalogEntry> _cachedCapabilityEntries =
        new Dictionary<string, CapabilityCatalogEntry>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, CapabilityCatalogEntry> MergeCapabilityCatalog(
        IReadOnlyList<YanziCapabilityDescriptor> registered,
        IEnumerable<CommandItem> extensionCommands)
    {
        var commands = extensionCommands
            .Where(command => command.Source == CommandSource.LocalExtension &&
                              !string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
            .GroupBy(command => command.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var providers = commands.ToDictionary(command => command.ExtensionId, StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, CapabilityCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        // Keep the already-existing Agent catalog semantics: declarations are on-demand,
        // and a running provider overrides its manifest declaration.
        foreach (var command in commands)
        {
            foreach (var descriptor in YanziAgentCapabilityCatalog.ForExtension(command))
            {
                if (string.IsNullOrWhiteSpace(descriptor.Name)) continue;
                if (!entries.TryGetValue(descriptor.Name, out var existing) ||
                    (!existing.Descriptor.Available && descriptor.Available))
                {
                    entries[descriptor.Name] = new CapabilityCatalogEntry(descriptor, command);
                }
            }
        }

        foreach (var capability in registered)
        {
            providers.TryGetValue(capability.ProviderExtensionId, out var provider);
            entries[capability.Name] = new CapabilityCatalogEntry(
                YanziAgentCapabilityCatalog.FromRegistered(capability), provider);
        }
        return entries;
    }

    private static IReadOnlyList<CommandItem> BuildCapabilityCommands()
    {
        var registered = YanziCapabilityQueryService.ListCapabilities();
        var fingerprint = string.Join("\n", registered.Select(capability => string.Join("\u001f",
            capability.Name, capability.Description, capability.Category, capability.ProviderExtensionId,
            capability.Version, capability.RiskLevel, string.Join(",", capability.Permissions))));
        lock (CapabilityCatalogSync)
        {
            var now = DateTime.UtcNow;
            // Registry changes are reflected immediately; manifests refresh periodically so
            // search keystrokes do not reread every installed manifest.
            if (fingerprint == _cachedCapabilityFingerprint &&
                now - _lastCapabilityManifestScanUtc < TimeSpan.FromSeconds(2))
            {
                return _cachedCapabilityCommands;
            }

            var entries = MergeCapabilityCatalog(registered, LocalExtensionCatalog.LoadCommands());
            var hostExePath = Path.Combine(HostAssets.InstallRootPath, "Yanzi.exe");
            // A test runner / portable deployment may not carry the PNG in its output.
            // Use the real app executable icon as a fallback rather than showing a glyph.
            var hostIcon = ExtensionIconLibrary.ResolveImageSource(HostAssets.LogoPath, null)
                ?? (File.Exists(hostExePath) ? NativeFileIconService.GetIcon(hostExePath, false) : null);
            _cachedCapabilityCommands = entries.Values
                .OrderBy(entry => entry.Descriptor.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Descriptor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new CommandItem(
                    glyph: entry.IsHost ? "燕" : entry.ProviderCommand?.Glyph ?? "能",
                    title: entry.Descriptor.Name,
                    subtitle: $"{entry.Status} · {entry.ProviderTitle} · {entry.Descriptor.Description}",
                    category: $"能力 · {NormalizeCapabilityCategory(entry.Descriptor.Category)}",
                    accentHex: "#397DC7",
                    openTarget: null,
                    keywords: new[] { "能力", entry.ProviderTitle, entry.Descriptor.Category,
                            entry.Descriptor.ProviderExtensionId, entry.Descriptor.Version, entry.Descriptor.RiskLevel,
                            entry.Status, entry.IsHost ? "宿主" : "小程序" }
                        .Concat(entry.Descriptor.Permissions),
                    source: CommandSource.Capability,
                    extensionId: $"capability:{entry.Descriptor.Name}",
                    declaredVersion: entry.Descriptor.Version,
                    permissions: entry.Descriptor.Permissions,
                    iconReference: entry.IsHost ? HostAssets.LogoPath : entry.ProviderCommand?.IconReference,
                    iconSourceOverride: entry.IsHost ? hostIcon : entry.ProviderCommand?.IconSource,
                    extensionDirectoryPath: entry.Directory))
                .ToArray();
            _cachedCapabilityEntries = entries;
            _cachedCapabilityFingerprint = fingerprint;
            _lastCapabilityManifestScanUtc = now;
            return _cachedCapabilityCommands;
        }
    }

    private static string NormalizeCapabilityCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "其他" : category;

    private static CapabilityCatalogEntry? GetCapabilityEntry(CommandItem command)
    {
        if (command.Source != CommandSource.Capability) return null;
        // Use the cached entry corresponding to the selected result; this also handles
        // declarations whose runtime has not been registered.
        lock (CapabilityCatalogSync)
            return _cachedCapabilityEntries.TryGetValue(command.Title, out var item) ? item : null;
    }

    // Retained for existing registry contract tests and callers.
    private static YanziCapabilityDescriptor? GetCapabilityDescriptor(CommandItem command) =>
        command.Source == CommandSource.Capability
            ? YanziCapabilityQueryService.Describe(command.Title) as YanziCapabilityDescriptor
            : null;

    private static string FormatCapabilityDetails(YanziCapabilityDescriptor capability) =>
        $"调用名称：{capability.Name}\n\n{capability.Description}\n\n" +
        $"类别：{NormalizeCapabilityCategory(capability.Category)}\n" +
        $"来源：{capability.ProviderExtensionId}\n" +
        $"版本：{capability.Version}\n" +
        $"权限：{(capability.Permissions.Count > 0 ? string.Join(", ", capability.Permissions) : "无")}\n" +
        $"风险：{capability.RiskLevel}\n" +
        $"需要确认：{(capability.RequiresConfirmation ? "是" : "否")}";

    private static string FormatCapabilityDetails(CapabilityCatalogEntry entry)
    {
        var item = entry.Descriptor;
        return $"调用名称：{item.Name}\n\n{item.Description}\n\n" +
            $"状态：{entry.Status}\n类别：{NormalizeCapabilityCategory(item.Category)}\n" +
            $"提供者：{entry.ProviderTitle} ({item.ProviderExtensionId})\n" +
            $"版本：{item.Version}\n权限：{(item.Permissions.Count > 0 ? string.Join(", ", item.Permissions) : "无")}\n" +
            $"风险：{item.RiskLevel}\n需要确认：{(item.RequiresConfirmation ? "是" : "否")}\n" +
            $"目录：{entry.Directory ?? "内置于燕子宿主，无单独能力目录"}";
    }

    private void ShowCapabilityDetails(CommandItem command)
    {
        var entry = GetCapabilityEntry(command);
        if (entry == null)
        {
            LastRunMessage = $"能力已不存在：{command.Title}。请切换搜索分类刷新。";
            return;
        }
        System.Windows.MessageBox.Show(this, FormatCapabilityDetails(entry),
            "能力详情", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string SchemaText(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? "(未声明)" :
        JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });

    private static string? FindSourceRoot()
    {
        // Only directories that actually contain the host source tree qualify.
        var candidates = new[]
        {
            Environment.CurrentDirectory,
            HostAssets.InstallRootPath,
            @"F:\Desktop\kaifa\OpenQuickHost",
            @"F:\Desktop\kaifa\yanzi-release-main-1052"
        };
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            for (var depth = 0; directory != null && depth < 8; directory = directory.Parent, depth++)
                if (File.Exists(Path.Combine(directory.FullName,
                        "src", "OpenQuickHost", "YanziBuiltinCapabilityRegistration.cs")))
                    return directory.FullName;
        }
        return null;
    }

    private static IReadOnlyList<string> FindHostSourceCandidates(string name, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return [];
        var folder = Path.Combine(root, "src", "OpenQuickHost");
        try
        {
            // This scan is only performed when the user explicitly asks for help context.
            // Match literal capability names to avoid guessing the implementation file.
            return Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    try { return File.ReadAllText(path).Contains($"\"{name}\"", StringComparison.Ordinal); }
                    catch (IOException) { return false; }
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string BuildCapabilityHelpContext(CapabilityCatalogEntry entry)
    {
        var item = entry.Descriptor;
        var root = entry.IsHost ? FindSourceRoot() : null;
        var sb = new StringBuilder();
        sb.AppendLine("燕子能力排查与修改上下文（由能力仓库 > 右键 > 帮帮忙生成）")
            .AppendLine($"能力名称：{item.Name}")
            .AppendLine($"能力说明：{item.Description}")
            .AppendLine($"状态：{entry.Status}；已注册可调用：{item.Available}；可按需激活：{item.OnDemand}")
            .AppendLine($"提供者：{entry.ProviderTitle}")
            .AppendLine($"提供者ID：{item.ProviderExtensionId}")
            .AppendLine($"类型：{(entry.IsHost ? "燕子宿主内置能力" : "小程序提供的能力")}")
            .AppendLine($"分类：{item.Category}；版本：{item.Version}；受众：{item.Audience}")
            .AppendLine($"权限：{(item.Permissions.Count == 0 ? "(无)" : string.Join(", ", item.Permissions))}")
            .AppendLine($"风险等级：{item.RiskLevel}；调用前确认：{item.RequiresConfirmation}")
            .AppendLine($"API 路由：/v1/capabilities/invoke")
            .AppendLine($"调用名参数：name={item.Name}");

        if (entry.IsHost)
        {
            sb.AppendLine($"燕子安装目录：{HostAssets.InstallRootPath}")
                .AppendLine($"宿主注册入口：{(root is null ? "src/OpenQuickHost/YanziBuiltinCapabilityRegistration.cs" : Path.Combine(root, "src", "OpenQuickHost", "YanziBuiltinCapabilityRegistration.cs"))}")
                .AppendLine($"宿主源码根目录：{root ?? "当前电脑未发现宿主源码；从燕子主仓库的 src/OpenQuickHost 查找"}");
            var source = FindHostSourceCandidates(item.Name, root);
            sb.AppendLine($"能力源码匹配文件：{(source.Count == 0 ? "未能在本机源码中确认；请以调用名称全文搜索" : string.Join("; ", source))}");
        }
        else
        {
            sb.AppendLine($"小程序目录：{entry.Directory ?? "未找到本地安装目录"}")
                .AppendLine($"清单文件：{entry.ManifestPath ?? "未知"}")
                .AppendLine($"程序入口：{(entry.ProviderCommand?.EntryPoint is { Length: > 0 } ep && entry.Directory is { Length: > 0 } dir ? Path.Combine(dir, ep) : "请检查 manifest.json 的 entry / script")}")
                .AppendLine($"运行时：{entry.ProviderCommand?.Runtime ?? "未知"}")
                .AppendLine($"入口模式：{entry.ProviderCommand?.EntryMode ?? "未声明"}")
                .AppendLine($"小程序版本：{entry.ProviderCommand?.DeclaredVersion ?? "未知"}");
        }

        sb.AppendLine("输入 Schema：").AppendLine(SchemaText(item.InputSchema))
            .AppendLine("输出 Schema：").AppendLine(SchemaText(item.OutputSchema))
            .AppendLine("定位建议：先检查提供者的 manifest.json / 注册入口与实际 Handler；")
            .AppendLine("按调用名搜索代码，确认权限、Schema、运行实例和 API 调用链路；")
            .AppendLine("如能力待启动，先启动对应小程序并核对动态注册，不要误认为能力代码缺失。")
            .AppendLine("请据此定位代码和排查问题，修改前验证实际文件位置和版本。");
        return sb.ToString();
    }

    private void CopyCapabilityText(string value, string message)
    {
        try
        {
            System.Windows.Clipboard.SetText(value);
            LastRunMessage = message;
        }
        catch (Exception ex)
        {
            LastRunMessage = $"复制失败：{ex.Message}";
        }
    }

    private void OpenCapabilityContextMenu(CommandItem command, bool keyboardInvoked = false)
    {
        var entry = GetCapabilityEntry(command);
        if (entry == null) return;

        var selectedRow = CommandList.ItemContainerGenerator.ContainerFromItem(command) as FrameworkElement;
        var menu = new ContextMenu
        {
            PlacementTarget = selectedRow ?? CommandList,
            Placement = keyboardInvoked ? PlacementMode.Right : PlacementMode.MousePoint
        };

        var help = new MenuItem { Header = "帮帮忙 · 复制定位上下文" };
        help.Click += (_, _) => CopyCapabilityText(BuildCapabilityHelpContext(entry),
            $"已复制“{entry.Descriptor.Name}”定位上下文，可直接发给开发助手");
        menu.Items.Add(help);
        menu.Items.Add(new Separator());

        var details = new MenuItem { Header = "查看能力详情" };
        details.Click += (_, _) => ShowCapabilityDetails(command);
        menu.Items.Add(details);
        var copyName = new MenuItem { Header = "复制调用名称" };
        copyName.Click += (_, _) => CopyCapabilityText(entry.Descriptor.Name,
            $"已复制调用名称：{entry.Descriptor.Name}");
        menu.Items.Add(copyName);
        var copyDetails = new MenuItem { Header = "复制能力详情" };
        copyDetails.Click += (_, _) => CopyCapabilityText(FormatCapabilityDetails(entry),
            $"已复制能力详情：{entry.Descriptor.Name}");
        menu.Items.Add(copyDetails);
        menu.IsOpen = true;
    }
}
