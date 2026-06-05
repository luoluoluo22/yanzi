using System.Diagnostics;

namespace OpenQuickHost;

public static class RunningExtensionRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, RunningExtensionEntry> Entries = [];

    public static event EventHandler? Changed;

    public static IReadOnlyList<RunningExtensionInfo> GetSnapshot()
    {
        List<Guid>? staleIds = null;
        List<RunningExtensionInfo> snapshot;

        lock (Gate)
        {
            snapshot = [];
            foreach (var pair in Entries)
            {
                if (pair.Value.Process != null)
                {
                    if (!IsAlive(pair.Value.Process))
                    {
                        staleIds ??= [];
                        staleIds.Add(pair.Key);
                        continue;
                    }
                }
                else if (pair.Value.IsAliveFunc != null)
                {
                    if (!pair.Value.IsAliveFunc())
                    {
                        staleIds ??= [];
                        staleIds.Add(pair.Key);
                        continue;
                    }
                }

                snapshot.Add(new RunningExtensionInfo(
                    pair.Value.InstanceId,
                    pair.Value.ExtensionId,
                    pair.Value.Title,
                    pair.Value.Process?.Id ?? -1,
                    pair.Value.Runtime,
                    pair.Value.LaunchSource,
                    pair.Value.StartedAt));
            }
        }

        if (staleIds is { Count: > 0 })
        {
            foreach (var staleId in staleIds)
            {
                Remove(staleId, "snapshot cleanup");
            }
        }

        return snapshot
            .OrderByDescending(static item => item.StartedAt)
            .ToArray();
    }

    public static int GetRunningCount()
    {
        return GetSnapshot().Count;
    }

    public static void RegisterNativeWindowProcess(CommandItem command, Process process, string launchSource)
    {
        if (!IsAlive(process))
        {
            return;
        }

        var processId = TryGetProcessId(process);
        var entry = new RunningExtensionEntry(
            Guid.NewGuid(),
            command.ExtensionId ?? $"pid-{processId}",
            string.IsNullOrWhiteSpace(command.Title) ? command.ExtensionId ?? "未命名扩展" : command.Title,
            string.IsNullOrWhiteSpace(command.Runtime) ? "csharp" : command.Runtime,
            string.IsNullOrWhiteSpace(launchSource) ? "unknown" : launchSource,
            DateTimeOffset.Now,
            process,
            IsAliveFunc: null,
            AbortAction: null);

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Remove(entry.InstanceId, "process exited");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"RunningExtensionRegistry register skipped: id={entry.ExtensionId}, title={entry.Title}, pid={processId}, error={ex.Message}");
            return;
        }

        lock (Gate)
        {
            Entries[entry.InstanceId] = entry;
        }

        HostAssets.AppendLog($"RunningExtensionRegistry registered: id={entry.ExtensionId}, title={entry.Title}, pid={processId}, launchSource={entry.LaunchSource}");
        RaiseChanged();
    }

    public static void RegisterManagedExtension(
        string extensionId,
        string title,
        string runtime,
        string launchSource,
        Func<bool> isAliveFunc,
        Action abortAction,
        out Guid instanceId)
    {
        var id = Guid.NewGuid();
        instanceId = id;

        var entry = new RunningExtensionEntry(
            id,
            extensionId,
            title,
            runtime,
            launchSource,
            DateTimeOffset.Now,
            Process: null,
            IsAliveFunc: isAliveFunc,
            AbortAction: abortAction);

        lock (Gate)
        {
            Entries[id] = entry;
        }

        HostAssets.AppendLog($"RunningExtensionRegistry registered managed: id={extensionId}, title={title}, launchSource={launchSource}");
        RaiseChanged();
    }

    public static bool TryTerminate(Guid instanceId, out string message)
    {
        RunningExtensionEntry? entry;
        lock (Gate)
        {
            Entries.TryGetValue(instanceId, out entry);
        }

        if (entry == null)
        {
            message = "该扩展已经结束。";
            return false;
        }

        try
        {
            if (entry.Process != null)
            {
                if (!IsAlive(entry.Process))
                {
                    Remove(instanceId, "terminate cleanup");
                    message = "该扩展已经结束。";
                    return false;
                }

                entry.Process.Kill(entireProcessTree: true);
                Remove(instanceId, "terminated by user");
                message = $"已结束扩展：{entry.Title}";
                HostAssets.AppendLog($"RunningExtensionRegistry terminated process: id={entry.ExtensionId}, title={entry.Title}, pid={TryGetProcessId(entry.Process)}");
                return true;
            }
            else if (entry.AbortAction != null)
            {
                if (entry.IsAliveFunc != null && !entry.IsAliveFunc())
                {
                    Remove(instanceId, "terminate cleanup");
                    message = "该扩展已经结束。";
                    return false;
                }

                entry.AbortAction();
                Remove(instanceId, "terminated by user");
                message = $"已结束扩展：{entry.Title}";
                HostAssets.AppendLog($"RunningExtensionRegistry terminated managed: id={entry.ExtensionId}, title={entry.Title}");
                return true;
            }

            message = "无法结束该扩展。";
            return false;
        }
        catch (Exception ex)
        {
            message = $"结束扩展失败：{ex.Message}";
            HostAssets.AppendLog($"RunningExtensionRegistry terminate failed: id={entry.ExtensionId}, title={entry.Title}, error={ex.Message}");
            return false;
        }
    }

    public static int TerminateAll()
    {
        List<RunningExtensionEntry> entries;
        lock (Gate)
        {
            entries = Entries.Values.ToList();
        }

        var terminatedCount = 0;
        foreach (var entry in entries)
        {
            try
            {
                if (entry.Process != null)
                {
                    if (!IsAlive(entry.Process))
                    {
                        Remove(entry.InstanceId, "terminate all cleanup");
                        continue;
                    }

                    entry.Process.Kill(entireProcessTree: true);
                    Remove(entry.InstanceId, "terminated on app shutdown");
                    terminatedCount++;
                    HostAssets.AppendLog($"RunningExtensionRegistry terminated on shutdown: id={entry.ExtensionId}, title={entry.Title}, pid={TryGetProcessId(entry.Process)}");
                }
                else if (entry.AbortAction != null)
                {
                    if (entry.IsAliveFunc != null && !entry.IsAliveFunc())
                    {
                        Remove(entry.InstanceId, "terminate all cleanup");
                        continue;
                    }

                    entry.AbortAction();
                    Remove(entry.InstanceId, "terminated on app shutdown");
                    terminatedCount++;
                    HostAssets.AppendLog($"RunningExtensionRegistry terminated managed on shutdown: id={entry.ExtensionId}, title={entry.Title}");
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"RunningExtensionRegistry shutdown terminate failed: id={entry.ExtensionId}, title={entry.Title}, error={ex.Message}");
            }
        }

        return terminatedCount;
    }

    public static void Remove(Guid instanceId, string reason)
    {
        var removed = false;
        lock (Gate)
        {
            removed = Entries.Remove(instanceId);
        }

        if (removed)
        {
            HostAssets.AppendLog($"RunningExtensionRegistry removed: instance={instanceId}, reason={reason}");
            RaiseChanged();
        }
    }

    private static void RaiseChanged()
    {
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsAlive(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string TryGetProcessId(Process process)
    {
        try
        {
            return process.Id.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private sealed record RunningExtensionEntry(
        Guid InstanceId,
        string ExtensionId,
        string Title,
        string Runtime,
        string LaunchSource,
        DateTimeOffset StartedAt,
        Process? Process,
        Func<bool>? IsAliveFunc,
        Action? AbortAction);
}

public sealed record RunningExtensionInfo(
    Guid InstanceId,
    string ExtensionId,
    string Title,
    int ProcessId,
    string Runtime,
    string LaunchSource,
    DateTimeOffset StartedAt);
