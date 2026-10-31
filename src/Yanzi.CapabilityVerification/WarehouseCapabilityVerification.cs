using System.Reflection;
using System.Text.Json;
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
        var registered = YanziCapabilityQueryService.ListCapabilities();
        Assert(commands.Count >= registered.Count && registered.Count > 0,
            "Catalog must include every registered capability and optionally dormant declarations");
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
        var getEntry = window.GetMethod("GetCapabilityEntry", flags)
            ?? throw new InvalidOperationException("Capability entry lookup missing.");
        var help = window.GetMethod("BuildCapabilityHelpContext", flags)
            ?? throw new InvalidOperationException("Help context formatter missing.");
        var format = window.GetMethod("FormatCapabilityDetails", flags, null, [typeof(YanziCapabilityDescriptor)], null)
            ?? throw new InvalidOperationException("Registered details formatter missing.");

        foreach (var command in commands)
        {
            Assert(command.Source == CommandSource.Capability, "Capability must use its own non-runnable source");
            Assert(command.OpenTarget == null, "Capability must not be launchable as an app");
            Assert((bool)allows.Invoke(null, [command, "capability"])!, "Capability scope should include capability");
            Assert(!(bool)allows.Invoke(null, [command, "extension"])!, "Miniapp scope must exclude capability");
            Assert(!(bool)allows.Invoke(null, [command, "all"])!, "All scope should not surface unlaunchable entries");
            var entry = getEntry.Invoke(null, [command]);
            Assert(entry != null, "Capability row must retain registered or declared metadata");
            var context = (string)help.Invoke(null, [entry])!;
            Assert(context.Contains(command.Title, StringComparison.Ordinal), "Help context missing invocation name");
            Assert(context.Contains("输入 Schema：", StringComparison.Ordinal) &&
                   context.Contains("输出 Schema：", StringComparison.Ordinal),
                "Help context missing input/output schema");
            Assert(context.Contains("提供者ID：", StringComparison.Ordinal) &&
                   context.Contains("定位建议：", StringComparison.Ordinal),
                "Help context missing provider and source-search hints");

            if (registered.Any(x => x.Name == command.Title))
            {
                var descriptor = registered.First(x => x.Name == command.Title);
                var details = (string)format.Invoke(null, [descriptor])!;
                Assert(details.Contains(command.Title, StringComparison.Ordinal) &&
                       details.Contains("权限：", StringComparison.Ordinal),
                    "Registered details must contain invocation name and permissions");
                if (descriptor.ProviderExtensionId == "yanzi-host")
                {
                    Assert(command.IconSource != null, "Host capability must use the Yanzi app icon");
                    Assert(context.Contains("宿主注册入口：", StringComparison.Ordinal) &&
                           context.Contains("宿主源码根目录：", StringComparison.Ordinal),
                        "Host help missing source location");
                }
            }
        }

        VerifyDeclaredProvider(window, flags);

        var alias = window.GetMethod("TryResolveBuiltInSearchScopeAlias", flags)
            ?? throw new InvalidOperationException("Capability search alias missing.");
        foreach (var term in new[] { "能力", "capability", "capabilities" })
        {
            object?[] arguments = [term, null];
            Assert((bool)alias.Invoke(null, arguments)! && (string?)arguments[1] == "capability",
                $"Capability scope alias failed: {term}");
        }

        Console.WriteLine($"WAREHOUSE_CAPABILITY_UI_QA=PASS; registered={registered.Count}; projected={commands.Count}; alias=3; providerIcons=true; helpContext=true; nonRunnable=true");
    }

    private static void VerifyDeclaredProvider(Type window, BindingFlags flags)
    {
        var merge = window.GetMethod("MergeCapabilityCatalog", flags)
            ?? throw new InvalidOperationException("Manifest merge missing");
        var help = window.GetMethod("BuildCapabilityHelpContext", flags)!;
        var root = Path.Combine(Path.GetTempPath(), "yanzi-capability-qa-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    provides = new[] { new { name = "qa.dormant.ping", description = "Dormant Provider", version = "1.0",
                        permissions = new[] { "qa.read" }, inputSchema = new { type = "object" },
                        outputSchema = new { type = "object" } } }
                }));
            File.WriteAllText(Path.Combine(root, "provider.cs"), "// synthetic capability provider fixture");
            var icon = "mdi:calendar";
            var provider = new CommandItem(
                glyph: "测", title: "测试小程序", subtitle: "测试", category: "小程序",
                accentHex: "#64748B", openTarget: null, keywords: [],
                source: CommandSource.LocalExtension, extensionId: "qa-provider",
                extensionDirectoryPath: root, entryPoint: "provider.cs", runtime: "csharp",
                iconReference: icon);
            var mapped = merge.Invoke(null, [YanziCapabilityQueryService.ListCapabilities(), new[] { provider }])
                ?? throw new InvalidOperationException("Manifest merge returned null");
            var entry = mapped.GetType().GetProperty("Item")!.GetValue(mapped, ["qa.dormant.ping"])!;
            var context = (string)help.Invoke(null, [entry])!;
            Assert(context.Contains("qa.dormant.ping", StringComparison.Ordinal), "Dormant ability not discovered");
            Assert(context.Contains("待启动", StringComparison.Ordinal) &&
                   context.Contains("可按需激活：True", StringComparison.Ordinal), "Dormant provider state inaccurate");
            Assert(context.Contains(Path.Combine(root, "manifest.json"), StringComparison.Ordinal),
                "Help context missing local manifest path");
            Assert(context.Contains(Path.Combine(root, "provider.cs"), StringComparison.Ordinal),
                "Help context missing provider source path");
            Assert(context.Contains("qa-provider", StringComparison.Ordinal), "Help context missing extension ID");

            var entryType = entry.GetType();
            Assert((bool)entryType.GetProperty("IsHost")!.GetValue(entry)! == false,
                "Miniapp provider must not be classified as host");
            Assert((string)entryType.GetProperty("ProviderTitle")!.GetValue(entry)! == "测试小程序",
                "Miniapp provider name missing");
            Assert(provider.IconReference == icon, "Provider icon reference should remain usable for rows");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
