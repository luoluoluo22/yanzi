using System.IO;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Durable, bounded execution receipt for miniapp triggers. It records dispatch
/// completion, not completion of arbitrary work that a miniapp starts later.
/// The running operation is owned by the host; old user data is never deleted.
/// </summary>
public sealed class HostDispatchJournal
{
    public sealed record Entry(
        string Id, string ExtensionId, string EventName, string State,
        DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, string? Detail = null,
        string? PreviousId = null);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HostPriorityGate _slots = new(concurrency: 2, maxQueue: 128);
    private List<Entry> _history;

    public static HostDispatchJournal Shared { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenQuickHost", "DispatchCenter", "history.json"));

    public HostDispatchJournal(string path)
    {
        _path = path;
        try
        {
            _history = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (JsonException)
        {
            var quarantine = _path + ".corrupt-" + Guid.NewGuid().ToString("N");
            File.Copy(_path, quarantine); // Preserve exact damaged bytes for diagnosis.
            _history = [];
        }
        var now = DateTimeOffset.UtcNow;
        var interrupted = _history.Any(x => x.State is "running" or "queued");
        _history = _history.Select(x => x.State is "running" or "queued"
            ? x with { State = "interrupted", UpdatedUtc = now, Detail = "宿主中断，等待恢复策略确认" }
            : x).ToList();
        if (interrupted) Save();
    }

    public IReadOnlyList<Entry> Snapshot()
    {
        lock (_gate) return _history.OrderByDescending(x => x.CreatedUtc).ToArray();
    }

    public (int Running, int Waiting, int Capacity) CapacitySnapshot() => _slots.Snapshot();

    public IReadOnlyList<Entry> Interrupted()
    {
        lock (_gate) return _history.Where(x => x.State == "interrupted").ToArray();
    }

    public void RecordDecision(string extensionId, string eventName, string reason)
    {
        // Inexpensive, latest-only diagnostic. This is not a job and does not fill the history.
        lock (_gate)
        {
            var row = _history.LastOrDefault(x => x.ExtensionId.Equals(extensionId, StringComparison.OrdinalIgnoreCase)
                && x.EventName.Equals(eventName, StringComparison.OrdinalIgnoreCase)
                && x.State == "waiting");
            var now = DateTimeOffset.UtcNow;
            if (row is not null)
            {
                if (row.Detail == reason) return;
                _history[_history.IndexOf(row)] = row with { UpdatedUtc = now, Detail = reason };
            }
            else _history.Add(new Entry(Guid.NewGuid().ToString("N"), extensionId, eventName,
                "waiting", now, now, reason));
            TrimAndSave();
        }
    }

    public async Task<bool> DispatchAsync(string extensionId, string eventName,
        Func<CancellationToken, Task> action, CancellationToken cancellationToken = default,
        string? previousId = null, int priority = 1)
    {
        if (string.IsNullOrWhiteSpace(extensionId) || string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Dispatch requires extension and event.");
        Entry entry;
        lock (_gate)
        {
            // The same miniapp must not execute concurrently even if different
            // event sources (idle, cron, resume) fire together.
            if (!_pending.Add(extensionId)) return false;
            var now = DateTimeOffset.UtcNow;
            entry = new Entry(Guid.NewGuid().ToString("N"), extensionId, eventName,
                "queued", now, now, "等待执行槽", previousId);
            _history.Add(entry);
            try { TrimAndSave(); }
            catch { _pending.Remove(extensionId); _history.Remove(entry); throw; }
        }

        IDisposable? lease = null;
        try
        {
            lease = await _slots.AcquireAsync(priority, cancellationToken);
            if (lease is null)
            {
                Update(entry.Id, "waiting", "全局调度队列已满，等待下次事件重试");
                return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            Update(entry.Id, "running", "已获得执行槽");
            await action(cancellationToken);
            Update(entry.Id, "dispatched", "事件已交付小程序；后续异步任务由小程序自行报告");
            return true;
        }
        catch (Exception ex)
        {
            Update(entry.Id, ex is OperationCanceledException ? "cancelled" : "failed",
                ex.GetType().Name + ": " + ex.Message[..Math.Min(300, ex.Message.Length)]);
            throw;
        }
        finally
        {
            lease?.Dispose();
            lock (_gate) _pending.Remove(extensionId);
        }
    }

    /// <summary>
    /// Exit preparation is safety-critical and cannot wait behind long-running
    /// idle/Cron dispatches or be deduplicated as an ordinary event.
    /// The caller must enforce a short deadline and call it only once per shutdown.
    /// </summary>
    public async Task DispatchExitAsync(string extensionId,
        Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new Entry(Guid.NewGuid().ToString("N"), extensionId, "host.beforeExit",
            "running", now, now, "等待保存确认");
        lock (_gate) { _history.Add(entry); TrimAndSave(); }
        try
        {
            await action(cancellationToken);
            Update(entry.Id, "dispatched", "保存处理函数已完成并确认");
        }
        catch (Exception ex)
        {
            Update(entry.Id, ex is OperationCanceledException ? "cancelled" : "failed",
                ex.GetType().Name + ": " + ex.Message[..Math.Min(300, ex.Message.Length)]);
            throw;
        }
    }

    public void MarkRecovered(string id)
    {
        Update(id, "recovered", "已通过显式恢复策略重新派发");
    }

    private void Update(string id, string state, string detail)
    {
        lock (_gate)
        {
            var i = _history.FindIndex(x => x.Id == id);
            if (i < 0) return;
            _history[i] = _history[i] with { State = state,
                UpdatedUtc = DateTimeOffset.UtcNow, Detail = detail };
            TrimAndSave();
        }
    }

    private void TrimAndSave()
    {
        if (_history.Count > 500)
        {
            var activeIds = _history.Where(x => x.State is "queued" or "running" or "interrupted")
                .Select(x => x.Id).ToHashSet();
            var keep = _history.TakeLast(500).Select(x => x.Id).ToHashSet();
            keep.UnionWith(activeIds);
            _history = _history.Where(x => keep.Contains(x.Id)).ToList();
        }
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(_history));
            File.Move(tmp, _path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
