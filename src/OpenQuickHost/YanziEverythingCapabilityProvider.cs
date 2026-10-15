namespace OpenQuickHost;

public static class YanziEverythingCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "everything.status",
            Description = "查询 Everything 搜索引擎的运行、IPC、数据库和燕子配置状态",
            Permissions = ["files.read"],
            Category = "everything",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = StatusAsync
        };

        yield return new()
        {
            Name = "everything.ensureRunning",
            Description = "确保 Everything 后台索引服务可通过 IPC 使用；优先复用现有实例",
            Permissions = ["application.run", "files.read"],
            Category = "everything",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = EnsureRunningAsync
        };

        yield return new()
        {
            Name = "everything.rebuildIndex",
            Description = "删除燕子自带 Everything 数据库并后台重建索引；用于索引异常时修复",
            Permissions = ["application.run", "file.write"],
            Category = "everything",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = RebuildIndexAsync
        };
    }

    private static Task<object?> StatusAsync(object? _)
    {
        var settings = AppSettingsStore.LoadCached();
        return Task.FromResult<object?>(new
        {
            enabledInYanzi = settings.EnableEverything,
            bundledRuntime = EverythingRuntimeService.HasBundledRuntime(),
            processRunning = EverythingRuntimeService.IsProcessRunning(),
            ipcReachable = EverythingSearchService.IsIpcReachable(),
            databaseLoaded = EverythingSearchService.IsDatabaseLoaded(),
            backgroundServiceOwner = HostRuntimeProfile.OwnsBackgroundServices
        });
    }

    private static Task<object?> EnsureRunningAsync(object? _)
    {
        var ready = EverythingRuntimeService.EnsureRunning();
        return Task.FromResult<object?>(new
        {
            ready,
            processRunning = EverythingRuntimeService.IsProcessRunning(),
            ipcReachable = EverythingSearchService.IsIpcReachable(),
            databaseLoaded = EverythingSearchService.IsDatabaseLoaded()
        });
    }

    private static Task<object?> RebuildIndexAsync(object? _)
    {
        if (!HostRuntimeProfile.OwnsBackgroundServices)
            throw new InvalidOperationException("当前进程不是后台服务所有者，不能重建 Everything 索引。");

        EverythingRuntimeService.RebuildDatabaseAndRestart();
        return Task.FromResult<object?>(new
        {
            accepted = true,
            status = "rebuilding",
            note = "索引在后台重建，期间 files.search 可能短暂不可用。"
        });
    }
}
