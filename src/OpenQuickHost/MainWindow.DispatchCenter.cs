using System.Reflection;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public partial class MainWindow
{
    public IReadOnlyDictionary<string, DateTime> DispatchNextRunsSnapshot() =>
        _extensionScheduler?.NextRunsSnapshot() ?? new Dictionary<string, DateTime>();

    private bool _hostExitPreparing;
    private bool _hostExitPrepared;
    private static readonly HashSet<string> SupportedHostEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "host.resume", "host.unlock", "network.online"
    };

    /// <summary>
    /// Host-triggered work uses a shared concurrency limit, one in-flight run
    /// per miniapp, and a durable journal. Existing launchSource values stay intact.
    /// </summary>
    private async Task DispatchManagedExtensionAsync(CommandItem command, string eventName, string launchSource)
    {
        if (_hostExitPreparing || !IsExtensionEnabled(command.ExtensionId)) return;
        await HostDispatchJournal.Shared.DispatchAsync(command.ExtensionId, eventName,
            _ => ExecuteCommandAsync(command, launchSource: launchSource));
    }

    internal void QueueHostEvent(string eventName)
    {
        if (!SupportedHostEvents.Contains(eventName) || _hostExitPreparing || !HostRuntimeProfile.OwnsBackgroundServices)
            return;
        _ = Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await PublishHostEventAsync(eventName); }
            catch (Exception ex) { HostAssets.AppendLog($"Host event dispatch failed: {eventName}: {ex.Message}"); }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task PublishHostEventAsync(string eventName)
    {
        if (_hostExitPreparing) return;
        try { await YanziEventBus.PublishAsync(eventName, new { occurredUtc = DateTimeOffset.UtcNow }); }
        catch (Exception ex) { HostAssets.AppendLog($"Host event bus error: {eventName}: {ex.Message}"); }

        var subscribedIds = LocalExtensionCatalog.LoadEntries()
            .Where(x => x.Manifest.Events?.Subscribe?.Contains(eventName, StringComparer.OrdinalIgnoreCase) == true)
            .Select(x => x.Manifest.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var command in GetAllCommands().Where(x => subscribedIds.Contains(x.ExtensionId)))
        {
            if (_hostExitPreparing) break;
            if (command.App != null && !command.App.RunInBackground &&
                !AppExtensionWindow.HasActiveInstance(command.ExtensionId))
            {
                HostDispatchJournal.Shared.RecordDecision(command.ExtensionId, eventName,
                    "等待小程序允许后台启动；不会弹出前台窗口");
                continue;
            }
            try { await DispatchManagedExtensionAsync(command, eventName, eventName); }
            catch (Exception ex) { HostAssets.AppendLog($"Host event miniapp error: {command.ExtensionId}: {ex.Message}"); }
        }
    }

    private async Task RecoverInterruptedDispatchesAsync()
    {
        if (_hostExitPreparing) return;
        var recoverable = LocalExtensionCatalog.LoadEntries()
            .Where(x => x.Manifest.Events?.RecoverInterrupted == true)
            .Select(x => x.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commands = GetAllCommands().GroupBy(x => x.ExtensionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var record in HostDispatchJournal.Shared.Interrupted())
        {
            if (_hostExitPreparing) break;
            if (!recoverable.Contains(record.ExtensionId) || !commands.TryGetValue(record.ExtensionId, out var command)
                || !IsExtensionEnabled(record.ExtensionId) || record.CreatedUtc < DateTimeOffset.UtcNow.AddDays(-1)
                || record.EventName is not ("host.idle" or "host.schedule" or "host.resume" or "host.unlock" or "network.online"))
                continue;
            try
            {
                if (await HostDispatchJournal.Shared.DispatchAsync(record.ExtensionId, "host.recover",
                    _ => ExecuteCommandAsync(command, launchSource: "recovery"), previousId: record.Id))
                    HostDispatchJournal.Shared.MarkRecovered(record.Id);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Host event recovery remains pending: {record.ExtensionId}: {ex.Message}");
            }
        }
    }

    private async Task PrepareSubscribedExtensionsForExitAsync()
    {
        // Non-running extensions have no in-memory state to flush; do not launch them.
        // Across all subscribers, shutdown has a bounded window, not one bound per app.
        using var total = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var active = RunningExtensionRegistry.GetSnapshot()
            .Select(x => x.ExtensionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subscribers = LocalExtensionCatalog.LoadEntries()
            .Where(x => x.Manifest.Events?.BeforeExit?.Enabled == true &&
                (active.Contains(x.Manifest.Id) || AppExtensionWindow.HasActiveInstance(x.Manifest.Id)))
            .ToArray();
        var work = subscribers.Select(async entry =>
        {
            var timeout = TimeSpan.FromMilliseconds(
                Math.Clamp(entry.Manifest.Events!.BeforeExit!.TimeoutMs, 100, 2000));
            using var perApp = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
            perApp.CancelAfter(timeout);
            try
            {
                await HostDispatchJournal.Shared.DispatchExitAsync(entry.Manifest.Id,
                    async ct => await AwaitMiniappExitAckAsync(entry.Manifest.Id, timeout, ct),
                    perApp.Token);
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Miniapp beforeExit did not acknowledge: {entry.Manifest.Id}: {ex.GetType().Name}");
            }
        }).ToArray();
        try { await Task.WhenAll(work).WaitAsync(total.Token); }
        catch (OperationCanceledException)
        {
            HostAssets.AppendLog("Miniapp exit preparation reached the global 5-second deadline.");
        }
    }

    private static async Task AwaitMiniappExitAckAsync(string extensionId, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var webAck = await AppExtensionWindow.TryPrepareForHostExitAsync(extensionId, timeout);
        if (webAck.HasValue)
        {
            if (!webAck.Value) throw new InvalidOperationException("WebView beforeExit handler missing or rejected");
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!HostObjectRegistry.TryGetObject(extensionId + "-window", out var window) || window == null)
            throw new InvalidOperationException("Running miniapp has no registered exit handler");
        var method = window.GetType().GetMethod("HandleTrigger", BindingFlags.Public | BindingFlags.Instance,
            binder: null, types: [typeof(string)], modifiers: null);
        if (method == null) throw new InvalidOperationException("HandleTrigger not available");
        var result = method.Invoke(window, ["host.beforeExit"]);
        if (result is Task task)
            await task.WaitAsync(cancellationToken);
        else if (result is bool acknowledged)
        {
            if (!acknowledged) throw new InvalidOperationException("Exit handler returned false");
        }
        else
            throw new InvalidOperationException("Exit handler must return Task or a synchronous acknowledgement");
    }

    public async Task RequestGracefulShutdownAsync()
    {
        if (_hostExitPrepared || _hostExitPreparing) return;
        _hostExitPreparing = true;
        AllowClose = true;
        try { await PrepareSubscribedExtensionsForExitAsync(); }
        catch (Exception ex) { HostAssets.AppendLog($"Miniapp exit preparation failed: {ex.Message}"); }
        finally
        {
            _hostExitPrepared = true;
            System.Windows.Application.Current.Shutdown();
        }
    }
}
