using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OpenQuickHost;

public partial class MainWindow
{
    // The capability catalog uses the same CommandList, SearchBox, list/grid templates
    // and scoped search pipeline as miniapps. Capabilities must never be executed as apps.
    private static readonly object CapabilityCatalogSync = new();
    private static string _cachedCapabilityFingerprint = "";
    private static IReadOnlyList<CommandItem> _cachedCapabilityCommands = Array.Empty<CommandItem>();

    private static IReadOnlyList<CommandItem> BuildCapabilityCommands()
    {
        // Search counts and results both use this projection. Do not recreate hundreds
        // of CommandItems (which also load usage history) for every typed character.
        var capabilities = YanziCapabilityQueryService.ListCapabilities()
            .OrderBy(capability => capability.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(capability => capability.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fingerprint = string.Join("\n", capabilities.Select(capability => string.Join("\u001f",
            capability.Name, capability.Description, capability.Category, capability.ProviderExtensionId,
            capability.Version, capability.RiskLevel, string.Join(",", capability.Permissions))));

        lock (CapabilityCatalogSync)
        {
            if (fingerprint == _cachedCapabilityFingerprint)
            {
                return _cachedCapabilityCommands;
            }

            _cachedCapabilityCommands = capabilities.Select(capability => new CommandItem(
                glyph: "能",
                title: capability.Name,
                subtitle: capability.Description,
                category: $"能力 · {NormalizeCapabilityCategory(capability.Category)}",
                accentHex: "#397DC7",
                openTarget: null,
                keywords: new[] { "能力", capability.Category, capability.ProviderExtensionId, capability.Version, capability.RiskLevel }
                    .Concat(capability.Permissions),
                source: CommandSource.Capability,
                extensionId: $"capability:{capability.Name}",
                declaredVersion: capability.Version,
                permissions: capability.Permissions)).ToArray();
            _cachedCapabilityFingerprint = fingerprint;
            return _cachedCapabilityCommands;
        }
    }

    private static string NormalizeCapabilityCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "其他" : category;

    private static YanziCapabilityDescriptor? GetCapabilityDescriptor(CommandItem command)
    {
        if (command.Source != CommandSource.Capability)
        {
            return null;
        }

        return YanziCapabilityQueryService.Describe(command.Title) as YanziCapabilityDescriptor;
    }

    private static string FormatCapabilityDetails(YanziCapabilityDescriptor capability) =>
        $"调用名称：{capability.Name}\n\n{capability.Description}\n\n" +
        $"类别：{NormalizeCapabilityCategory(capability.Category)}\n" +
        $"来源：{capability.ProviderExtensionId}\n" +
        $"版本：{capability.Version}\n" +
        $"权限：{(capability.Permissions.Count > 0 ? string.Join(", ", capability.Permissions) : "无")}\n" +
        $"风险：{capability.RiskLevel}\n" +
        $"需要确认：{(capability.RequiresConfirmation ? "是" : "否")}";

    private void ShowCapabilityDetails(CommandItem command)
    {
        var capability = GetCapabilityDescriptor(command);
        if (capability == null)
        {
            LastRunMessage = $"能力已不存在：{command.Title}。请切换搜索分类刷新。";
            return;
        }

        System.Windows.MessageBox.Show(this, FormatCapabilityDetails(capability),
            "能力详情", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyCapabilityText(string text, string successMessage)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            LastRunMessage = successMessage;
        }
        catch (Exception ex)
        {
            LastRunMessage = $"复制失败：{ex.Message}";
        }
    }

    private void OpenCapabilityContextMenu(CommandItem command, bool keyboardInvoked = false)
    {
        var capability = GetCapabilityDescriptor(command);
        if (capability == null)
        {
            return;
        }

        var selectedRow = CommandList.ItemContainerGenerator.ContainerFromItem(command) as FrameworkElement;
        var menu = new ContextMenu
        {
            PlacementTarget = selectedRow ?? CommandList,
            Placement = keyboardInvoked ? PlacementMode.Right : PlacementMode.MousePoint
        };

        var details = new MenuItem { Header = "查看能力详情" };
        details.Click += (_, _) => ShowCapabilityDetails(command);
        menu.Items.Add(details);

        var copyName = new MenuItem { Header = "复制调用名称" };
        copyName.Click += (_, _) => CopyCapabilityText(capability.Name, $"已复制调用名称：{capability.Name}");
        menu.Items.Add(copyName);

        var copyDetails = new MenuItem { Header = "复制能力详情" };
        copyDetails.Click += (_, _) => CopyCapabilityText(
            FormatCapabilityDetails(capability), $"已复制能力详情：{capability.Name}");
        menu.Items.Add(copyDetails);

        menu.IsOpen = true;
    }
}
