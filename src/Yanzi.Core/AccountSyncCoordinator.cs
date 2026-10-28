namespace Yanzi.Core;

/// <summary>Platform-independent lifetime and serialization of account operations.</summary>
public sealed class AccountSyncCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long generation;
    private readonly object sessionGate = new();
    public long Generation => Interlocked.Read(ref generation);
    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        var expected = Generation;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { RequireCurrent(expected); }
        catch { gate.Release(); throw; }
    }
    public void Release() => gate.Release();
    public void Invalidate() { lock (sessionGate) Interlocked.Increment(ref generation); }
    public void Invalidate(Action clearSession)
    {
        lock (sessionGate) { Interlocked.Increment(ref generation); clearSession(); }
    }
    public void Commit(long expected, Action commit)
    {
        lock (sessionGate) { RequireCurrent(expected); commit(); }
    }
    public void RequireCurrent(long expected)
    {
        if (Generation != expected) throw new OperationCanceledException("account_session_changed");
    }
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        var expected = Generation;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { RequireCurrent(expected); return new Lease(gate); }
        catch { gate.Release(); throw; }
    }
    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var expected = Generation;
        using var lease = await EnterAsync(cancellationToken).ConfigureAwait(false);
        RequireCurrent(expected);
        var result = await operation().ConfigureAwait(false);
        RequireCurrent(expected);
        return result;
    }
    public async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await RunAsync(async () => { await operation().ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? owner = gate;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}
