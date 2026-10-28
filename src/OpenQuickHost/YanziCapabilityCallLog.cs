using System.Collections.Concurrent;

namespace OpenQuickHost;

/// <summary>
/// 能力调用日志基础设施。
/// 后续用于权限审计和 AI 调试。
/// </summary>
public static class YanziCapabilityCallLog
{
    private static readonly ConcurrentQueue<CapabilityCallRecord> Records = new();

    public static void Add(string capability, string provider, bool success, string caller = "anonymous", string? errorCode = null)
    {
        Records.Enqueue(new CapabilityCallRecord(DateTime.UtcNow, capability, provider, success, caller, errorCode, YanziOperationTrace.Current));
        while (Records.Count > 1000) Records.TryDequeue(out _);
    }

    public static IReadOnlyList<CapabilityCallRecord> List() => Records.ToArray();
}

public sealed record CapabilityCallRecord(DateTime Time, string Capability, string Provider, bool Success,
    string Caller = "anonymous", string? ErrorCode = null, string? TraceId = null);
