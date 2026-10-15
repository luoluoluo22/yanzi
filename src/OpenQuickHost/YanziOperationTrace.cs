namespace OpenQuickHost;

public static class YanziOperationTrace
{
    private static readonly AsyncLocal<string?> Active = new();
    public static string? Current => Active.Value;
    public static IDisposable Push(string? traceId = null)
    {
        traceId ??= Current ?? Guid.NewGuid().ToString("N");
        if (!System.Text.RegularExpressions.Regex.IsMatch(traceId, "^[a-zA-Z0-9_.:-]{1,100}$")) throw new ArgumentException("invalid_trace_id");
        var previous = Active.Value; Active.Value = traceId; return new Scope(() => Active.Value = previous);
    }
    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}
