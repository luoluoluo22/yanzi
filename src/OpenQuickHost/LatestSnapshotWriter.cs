namespace OpenQuickHost;

/// <summary>One background writer; rapid updates replace pending snapshots by key.</summary>
internal sealed class LatestSnapshotWriter<T>(Action<T> write, Action<Exception> onError)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, T> _pending = new(StringComparer.OrdinalIgnoreCase);
    private Task _worker = Task.CompletedTask;
    private bool _running;

    public void Enqueue(string key, T snapshot)
    {
        lock (_gate)
        {
            _pending[key] = snapshot;
            if (_running) return;
            _running = true;
            _worker = Task.Run(Drain);
        }
    }

    public Task WhenIdleAsync() { lock (_gate) return _worker; }

    private void Drain()
    {
        while (true)
        {
            T snapshot;
            lock (_gate)
            {
                if (_pending.Count == 0) { _running = false; return; }
                var first = _pending.First();
                snapshot = first.Value;
                _pending.Remove(first.Key);
            }
            try { write(snapshot); }
            catch (Exception ex) { try { onError(ex); } catch { /* Keep draining other keys. */ } }
        }
    }
}
