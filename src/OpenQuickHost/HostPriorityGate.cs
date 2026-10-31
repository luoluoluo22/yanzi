namespace OpenQuickHost;

/// <summary>
/// Bounded, aging priority gate. Low priority eventually outranks new high jobs.
/// Cancellation removes pending waiters; a granted lease is always released by its owner.
/// </summary>
public sealed class HostPriorityGate
{
    private sealed class Waiter
    {
        public required int Priority { get; init; }
        public required long Sequence { get; init; }
        public required DateTimeOffset Enqueued { get; init; }
        public TaskCompletionSource<IDisposable> Promise { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Lease(HostPriorityGate owner) : IDisposable
    {
        private HostPriorityGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private readonly object _gate = new();
    private readonly List<Waiter> _waiters = [];
    private readonly int _concurrency;
    private readonly int _maxQueue;
    private readonly Func<DateTimeOffset> _clock;
    private int _active;
    private long _sequence;

    public HostPriorityGate(int concurrency = 2, int maxQueue = 128, Func<DateTimeOffset>? clock = null)
    {
        if (concurrency < 1 || maxQueue < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
        _concurrency = concurrency;
        _maxQueue = maxQueue;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public (int Running, int Waiting, int Capacity) Snapshot()
    {
        lock (_gate) return (_active, _waiters.Count, _concurrency);
    }

    public async Task<IDisposable?> AcquireAsync(int priority, CancellationToken token = default)
    {
        if (priority is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(priority));
        token.ThrowIfCancellationRequested();
        Waiter? waiter;
        lock (_gate)
        {
            if (_active < _concurrency && _waiters.Count == 0)
            {
                _active++;
                return new Lease(this);
            }
            if (_waiters.Count >= _maxQueue) return null;
            waiter = new Waiter { Priority = priority, Sequence = ++_sequence, Enqueued = _clock() };
            _waiters.Add(waiter);
        }

        using var registration = token.Register(() => Cancel(waiter, token));
        var lease = await waiter.Promise.Task.ConfigureAwait(false);
        // Cancellation after permit grant must not leak the permit.
        if (token.IsCancellationRequested)
        {
            lease.Dispose();
            token.ThrowIfCancellationRequested();
        }
        return lease;
    }

    private void Cancel(Waiter waiter, CancellationToken token)
    {
        lock (_gate)
        {
            if (!_waiters.Remove(waiter)) return; // Already granted; acquirer owns release.
            waiter.Promise.TrySetCanceled(token);
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_waiters.Count == 0) { _active--; return; }
            var now = _clock();
            // Each 30 seconds waiting adds one virtual priority point; ties are FIFO.
            var next = _waiters.OrderByDescending(w =>
                    w.Priority + Math.Max(0d, (now - w.Enqueued).TotalSeconds) / 30d)
                .ThenBy(w => w.Sequence).First();
            _waiters.Remove(next);
            next.Promise.TrySetResult(new Lease(this));
            // Active count is unchanged: ownership of this slot transferred.
        }
    }
}
