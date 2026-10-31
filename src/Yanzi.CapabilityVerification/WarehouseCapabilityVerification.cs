using System.Reflection;
using OpenQuickHost;

internal static class WarehouseCapabilityVerification
{
    public static void Run()
    {
        YanziBuiltinCapabilityRegistration.Register();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var window = typeof(MainWindow);
        var build = window.GetMethod("BuildCapabilityCommands", flags)
            ?? throw new InvalidOperationException("Capability projection missing.");
        var commands = build.Invoke(null, null) as IReadOnlyList<CommandItem>
            ?? throw new InvalidOperationException("Capability projection type changed.");
        var descriptors = YanziCapabilityQueryService.ListCapabilities();
        Assert(commands.Count == descriptors.Count && commands.Count > 0, "Catalog count does not match registry");
        Assert(ReferenceEquals(commands, build.Invoke(null, null)), "Repeated search must reuse projected rows");

        var dynamicName = "qa.warehouse." + Guid.NewGuid().ToString("N");
        try
        {
            YanziCapabilityRegistry.Register(dynamicName, _ => Task.FromResult<object?>(null), "warehouse-qa", "Dynamic capability");
            var added = (IReadOnlyList<CommandItem>)build.Invoke(null, null)!;
            Assert(added.Count == commands.Count + 1 && added.Any(x => x.Title == dynamicName),
                "Dynamically registered capabilities must appear without restarting the window");
        }
        finally
        {
            YanziCapabilityRegistry.Unregister(dynamicName, "warehouse-qa");
        }
        Assert(((IReadOnlyList<CommandItem>)build.Invoke(null, null)!).Count == commands.Count,
            "Unregistered capabilities must disappear from the next search");
        Assert(commands.Select(x => x.ExtensionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == commands.Count,
            "Capability IDs must be distinct");

        var allows = window.GetMethod("SearchScopeAllows", flags)
            ?? throw new InvalidOperationException("Search scope filtering missing.");
        var resolve = window.GetMethod("GetCapabilityDescriptor", flags)
            ?? throw new InvalidOperationException("Capability lookup missing.");
        var format = window.GetMethod("FormatCapabilityDetails", flags)
            ?? throw new InvalidOperationException("Capability details formatter missing.");
        foreach (var command in commands)
        {
            Assert(command.Source == CommandSource.Capability, "Capability must use its own non-runnable source");
            Assert(command.OpenTarget == null, "Capability must not be launchable as an app");
            Assert((bool)allows.Invoke(null, [command, "capability"])!, "Capability scope should include capability");
            Assert(!(bool)allows.Invoke(null, [command, "extension"])!, "Miniapp scope must exclude capability");
            Assert(!(bool)allows.Invoke(null, [command, "all"])!, "All scope should not surface unlaunchable entries");
            var descriptor = resolve.Invoke(null, [command]) as YanziCapabilityDescriptor;
            Assert(descriptor != null && descriptor.Name == command.Title, "Registry descriptor lookup failed");
            var details = (string)format.Invoke(null, [descriptor])!;
            Assert(details.Contains(command.Title, StringComparison.Ordinal), "Details missing invocation name");
            Assert(details.Contains("权限：", StringComparison.Ordinal), "Details missing permission information");
        }

        var alias = window.GetMethod("TryResolveBuiltInSearchScopeAlias", flags)
            ?? throw new InvalidOperationException("Capability search alias missing.");
        foreach (var term in new[] { "能力", "capability", "capabilities" })
        {
            object?[] arguments = [term, null];
            Assert((bool)alias.Invoke(null, arguments)! && (string?)arguments[1] == "capability",
                $"Capability scope alias failed: {term}");
        }

        Console.WriteLine($"WAREHOUSE_CAPABILITY_UI_QA=PASS; registered={descriptors.Count}; projected={commands.Count}; alias=3; nonRunnable=true");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
