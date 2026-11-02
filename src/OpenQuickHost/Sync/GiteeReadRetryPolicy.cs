using System.Net.Http;

namespace OpenQuickHost.Sync;

/// <summary>
/// Bounded, jittered Gitee GET retry backoff. Honors Retry-After when safe to
/// wait; if the server asks for more than two minutes, the caller receives
/// the original 429/503 rather than issuing an early retry.
/// </summary>
internal static class GiteeReadRetryPolicy
{
    internal static TimeSpan ExponentialDelay(int attempt, int jitterMs = 0)
    {
        var step = Math.Clamp(attempt, 1, 4) - 1;
        return TimeSpan.FromMilliseconds(400 * (1 << step) + Math.Clamp(jitterMs, 0, 200));
    }

    internal static TimeSpan? ComputeDelay(
        HttpResponseMessage response, TimeSpan minimumDelay, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        var serverDelay = header?.Delta ?? (header?.Date - now);
        if (serverDelay is null || serverDelay <= TimeSpan.Zero)
            return minimumDelay;
        if (serverDelay > TimeSpan.FromMinutes(2))
            return null; // Fail fast: scheduler may retry later, never violate server deadline.
        return serverDelay > minimumDelay ? serverDelay : minimumDelay;
    }
}
