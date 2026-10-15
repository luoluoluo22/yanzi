using System.Threading;

namespace OpenQuickHost;

/// <summary>
/// 表示一次独立的搜索会话，持有生命周期取消令牌。
/// </summary>
public sealed class SearchSession : IDisposable
{
    private int _disposed;

    public int SessionId { get; }
    public string Query { get; }
    public string ScopeKey { get; }
    public CancellationTokenSource Cts { get; }
    // A captured token remains readable by in-flight work after its source is disposed.
    public CancellationToken Token { get; }
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;

    public SearchSession(int sessionId, string query, string scopeKey)
    {
        SessionId = sessionId;
        Query = query ?? string.Empty;
        ScopeKey = scopeKey ?? string.Empty;
        Cts = new CancellationTokenSource();
        Token = Cts.Token;
    }

    public void Cancel()
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 0 && !Token.IsCancellationRequested)
            {
                Cts.Cancel();
            }
        }
        catch (ObjectDisposedException) { }
        catch (AggregateException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Cts.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException) { }
        finally { Cts.Dispose(); }
    }
}

/// <summary>
/// 响应式搜索管道管理器，统一调度搜索会话、取消上一轮未完成任务并保障线程安全。
/// </summary>
public sealed class SearchPipelineManager : IDisposable
{
    private readonly Lock _gate = new();
    private int _nextSessionId;
    private SearchSession? _currentSession;

    public SearchSession CreateSession(string query, string scopeKey)
    {
        lock (_gate)
        {
            _currentSession?.Dispose();
            var sessionId = Interlocked.Increment(ref _nextSessionId);
            _currentSession = new SearchSession(sessionId, query, scopeKey);
            return _currentSession;
        }
    }

    public bool IsActive(SearchSession session)
    {
        lock (_gate)
        {
            return _currentSession == session && !session.Token.IsCancellationRequested;
        }
    }

    public void CancelActive()
    {
        lock (_gate)
        {
            _currentSession?.Dispose();
            _currentSession = null;
        }
    }

    public void Dispose()
    {
        CancelActive();
    }
}
