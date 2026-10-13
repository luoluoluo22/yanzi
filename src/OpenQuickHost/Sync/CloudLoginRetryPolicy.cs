using System.Net;

namespace OpenQuickHost.Sync;

/// <summary>
/// Controls only background sign-in attempts. Manual sign-in bypasses the cooldown,
/// but its failures still protect background sync from retry storms.
/// Unlike general transport backoff, successful GET requests cannot reset this policy.
/// </summary>
public sealed class CloudLoginRetryPolicy
{
    private readonly object _gate = new();
    private int _transientFailures;
    private DateTimeOffset _retryAfterUtc = DateTimeOffset.MinValue;

    public bool IsCoolingDown(DateTimeOffset nowUtc)
    {
        lock (_gate) return nowUtc < _retryAfterUtc;
    }

    public TimeSpan RecordFailure(HttpStatusCode? status, TimeSpan? serverRetryAfter, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            TimeSpan delay;
            switch (status)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.NotFound:
                    _transientFailures = 0;
                    delay = TimeSpan.FromMinutes(10);
                    break;
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.UpgradeRequired:
                    _transientFailures = 0;
                    delay = TimeSpan.FromMinutes(30);
                    break;
                case HttpStatusCode.TooManyRequests:
                    _transientFailures = 0;
                    delay = serverRetryAfter.HasValue && serverRetryAfter.Value > TimeSpan.FromMinutes(1)
                        ? serverRetryAfter.Value : TimeSpan.FromMinutes(1);
                    if (delay > TimeSpan.FromHours(1)) delay = TimeSpan.FromHours(1);
                    break;
                default:
                    if (status.HasValue && (int)status.Value >= 400 && (int)status.Value < 500)
                    {
                        _transientFailures = 0;
                        delay = TimeSpan.FromMinutes(10);
                    }
                    else
                    {
                        _transientFailures = Math.Min(_transientFailures + 1, 5);
                        delay = TimeSpan.FromSeconds(Math.Min(30 * (1 << (_transientFailures - 1)), 300));
                    }
                    break;
            }
            var candidate = nowUtc + delay;
            if (candidate > _retryAfterUtc) _retryAfterUtc = candidate;
            return delay;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _retryAfterUtc = DateTimeOffset.MinValue;
            _transientFailures = 0;
        }
    }
}
